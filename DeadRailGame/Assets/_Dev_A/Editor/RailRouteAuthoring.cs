using System.IO;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public static class RailRouteAuthoring
{
    [MenuItem("DeadRail/Route/Build from example")]
    public static void BuildFromExample()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        GameObject source = GameObject.Find("보여주기");
        TreadmillManager train = UnityEngine.Object.FindFirstObjectByType<TreadmillManager>();
        if (source == null || train == null) throw new InvalidOperationException("보여주기 and TreadmillManager are required.");
        if (train.GetComponent<RailRouteLoop>() != null) throw new InvalidOperationException("Route already authored. Edit its references and cut markers in the Inspector.");

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Build DeadRail route loop");
        RailRouteLoop loop = Undo.AddComponent<RailRouteLoop>(train.gameObject);
        loop.referenceLayout = source;
        loop.trainTransform = GameObject.Find("Train").transform;
        loop.transitionDirector = UnityEngine.Object.FindFirstObjectByType<PlayableDirector>();
        loop.cinematicObjects = new[] { GameObject.Find("BlackoutCamera"), GameObject.Find("BlackoutTrainCamera"), GameObject.Find("Train(Cutscene)") }.Where(x => x != null).ToArray();
        loop.transitionDirector.playOnAwake = false;

        GameObject container = new GameObject("Route_Junction");
        Undo.RegisterCreatedObjectUndo(container, "Route junction");
        GameObject clone = UnityEngine.Object.Instantiate(source, container.transform);
        clone.name = "Example geometry";
        Transform threeWay = clone.transform.Find("DeadRail_Track_ThreeWay");
        Vector3 entry = threeWay.position;
        clone.transform.position -= entry;
        loop.junctionGeometry = container.transform;
        container.AddComponent<NetworkObject>();
        Transform[] children = clone.transform.Cast<Transform>().ToArray();

        Vector3[] left = ReadWaypoints(threeWay, "_Left_Waypoint_");
        Vector3[] right = ReadWaypoints(threeWay, "_Right_Waypoint_");
        Vector3[] straight = ReadWaypoints(threeWay, "_Straight_Waypoint_");
        var leftPoints = new List<Vector3>(left);
        var rightPoints = new List<Vector3>(right);
        var straightPoints = new List<Vector3>(straight);
        AppendSegments(leftPoints, clone.transform, new[] { "DeadRail_TrackCurve_Left_30deg", "DeadRail_Track_8m (4)", "DeadRail_Track_8m (5)", "DeadRail_TrackCurve_Right_60deg", "DeadRail_Track_8m (8)", "DeadRail_Track_8m (9)", "DeadRail_Track_8m (10)" }, false);
        AppendSegments(rightPoints, clone.transform, new[] { "DeadRail_TrackCurve_Right_30deg", "DeadRail_Track_8m (6)", "DeadRail_Track_8m (7)", "DeadRail_TrackCurve_Left_60deg", "DeadRail_Track_8m (9)", "DeadRail_Track_8m (11)" }, true);
        AppendSegments(straightPoints, clone.transform, new[] { "DeadRail_Track_8m", "DeadRail_Track_8m (1)", "DeadRail_Track_8m (2)", "DeadRail_Track_8m (3)", "DeadRail_Track_8m (14)", "DeadRail_Track_8m (15)" }, false);

        string[] incoming = { "DeadRail_Track_8m (12)", "DeadRail_Track_8m (13)", "DeadRail_Track_8m (16)" };
        for (int i = 0; i < incoming.Length; i++)
        {
            Transform segment = clone.transform.Find(incoming[i]);
            AlignSegment(segment, Vector3.back * (8f * i), Vector3.back);
        }
        foreach (Transform child in children)
        {
            if (child.name == "DeadRail_Track_8m (17)" || child.name == "DeadRail_Track_8m (18)" || child.name == "DeadRail_Track_8m (19)") child.gameObject.SetActive(false);
        }
        Transform tunnel = clone.transform.Find("DeadRail_Blackout_Tunnel");
        tunnel.position = new Vector3(0f, 0f, straightPoints[straightPoints.Count - 1].z);
        foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("DeadRail_Approach_Roadblock") || child.name.StartsWith("DeadRail_Tunnel_Entry_Boards")) child.gameObject.SetActive(false);
        }

        loop.leftPath = TrimAtLength(leftPoints, 59f);
        loop.rightPath = TrimAtLength(rightPoints, 45f);
        loop.straightPath = TrimAtLength(straightPoints, 54f);
        loop.cutMarkers = new[] { CreateMarker(container.transform, "Left red-circle cut", loop.leftPath.Last()), CreateMarker(container.transform, "Straight tunnel cut", loop.straightPath.Last()), CreateMarker(container.transform, "Right blue-circle cut", loop.rightPath.Last()) };

        Transform switchModel = clone.transform.Find("DeadRail_TrackSwitch");
        switchModel.position = new Vector3(3.9f, 0f, -24f);
        TrackSwitch trackSwitch = switchModel.gameObject.AddComponent<TrackSwitch>();
        trackSwitch.leverPivot = FindDescendant(switchModel, "TrackSwitch_Lever_Pivot");
        var serializedSwitch = new SerializedObject(trackSwitch);
        serializedSwitch.FindProperty("leftRouteLamp").objectReferenceValue = FindDescendant(switchModel, "Route_Lamp_LEFT").GetComponent<Renderer>();
        serializedSwitch.FindProperty("rightRouteLamp").objectReferenceValue = FindDescendant(switchModel, "Route_Lamp_RIGHT").GetComponent<Renderer>();
        serializedSwitch.FindProperty("straightRouteLamp").objectReferenceValue = FindDescendant(switchModel, "Route_Lamp_STRAIGHT").GetComponent<Renderer>();
        TrackSwitch oldSwitch = UnityEngine.Object.FindObjectsByType<TrackSwitch>(FindObjectsSortMode.None).FirstOrDefault(x => x != trackSwitch);
        if (oldSwitch != null)
        {
            var old = new SerializedObject(oldSwitch);
            foreach (string property in new[] { "inactiveRouteLamp", "leftRouteLampOn", "rightRouteLampOn" })
                serializedSwitch.FindProperty(property).objectReferenceValue = old.FindProperty(property).objectReferenceValue;
        }
        serializedSwitch.ApplyModifiedPropertiesWithoutUndo();
        Transform target = FindDescendant(switchModel, "TrackSwitch_HitTarget");
        BoxCollider hitCollider = target.GetComponent<BoxCollider>();
        if (hitCollider == null) hitCollider = target.gameObject.AddComponent<BoxCollider>();
        hitCollider.size = new Vector3(1.5f, 1.5f, 0.4f);
        target.tag = "TrackSwitch";

        GameObject farm = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Dev_A/Prefabs/FarmingStopGeometry.prefab"));
        farm.name = "Route_FarmingStop";
        Undo.RegisterCreatedObjectUndo(farm, "Route farming ground");
        farm.transform.position = new Vector3(0f, 0f, loop.approachDistance);
        loop.farmingGeometry = farm.transform;
        loop.supplyPoints = new Transform[3];
        loop.supplyRenderers = new Renderer[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject supply = GameObject.CreatePrimitive(PrimitiveType.Cube);
            supply.name = $"Supply_{i + 1}";
            supply.transform.SetParent(farm.transform, false);
            supply.transform.localPosition = new Vector3(7f + i * 3f, 0.45f, -5f - i * 2f);
            supply.transform.localScale = new Vector3(1f, 0.8f, 1f);
            loop.supplyPoints[i] = supply.transform;
            loop.supplyRenderers[i] = supply.GetComponent<Renderer>();
        }

        GameObject legacyJunction = GameObject.Find("Junction_Event_Set");
        if (legacyJunction != null)
        {
            Undo.RecordObject(legacyJunction, "Retire previous junction instance");
            legacyJunction.SetActive(false);
        }
        foreach (GameObject obj in loop.cinematicObjects)
        {
            Undo.RecordObject(obj, "Initialize cutscene state");
            obj.SetActive(false);
        }
        container.transform.position = Vector3.forward * loop.cruisingDistance;
        EditorUtility.SetDirty(loop);
        NetworkObjectPostprocessor.BakeScene(SceneManager.GetActiveScene());
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeObject = loop;
        Debug.Log($"[RailRoute] Authored three routes ({loop.leftPath.Length}/{loop.straightPath.Length}/{loop.rightPath.Length} points), switch 24m before entry, 3 supplies and Timeline.");
        FarmingMapAuthoring.UpdateMaps();
    }

    private static Transform FindDescendant(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(x => x.name == name);

    private static Vector3[] ReadWaypoints(Transform root, string token) => root.GetComponentsInChildren<Transform>(true).Where(x => x.name.Contains(token)).OrderBy(x => x.name, StringComparer.Ordinal).Select(x => new Vector3(x.position.x, 0f, x.position.z)).ToArray();

    private static void AppendSegments(List<Vector3> points, Transform root, string[] names, bool rightSide)
    {
        foreach (string name in names)
        {
            Transform segment = root.Cast<Transform>().First(x => x.name == name && (name != "DeadRail_Track_8m (9)" || (x.position.x > 0f) == rightSide));
            Vector3 tangent = (points[points.Count - 1] - points[points.Count - 2]).normalized;
            AlignSegment(segment, points[points.Count - 1], tangent);
            points.AddRange(ReadWaypoints(segment, "_Main_Waypoint_").Skip(1));
        }
    }

    private static void AlignSegment(Transform segment, Vector3 start, Vector3 tangent)
    {
        Vector3[] points = ReadWaypoints(segment, "_Main_Waypoint_");
        if (points.Length < 2) throw new InvalidOperationException($"Missing waypoints on {segment.name}");
        Quaternion rotation = Quaternion.FromToRotation((points[1] - points[0]).normalized, tangent);
        Vector3 pivotOffset = segment.position - points[0];
        segment.rotation = rotation * segment.rotation;
        segment.position = start + rotation * pivotOffset;
    }

    private static Vector3[] TrimAtLength(List<Vector3> points, float length)
    {
        var result = new List<Vector3> { points[0] };
        float remaining = length;
        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector3.Distance(points[i - 1], points[i]);
            if (segment >= remaining)
            {
                result.Add(Vector3.Lerp(points[i - 1], points[i], remaining / segment));
                return result.ToArray();
            }
            remaining -= segment;
            result.Add(points[i]);
        }
        return result.ToArray();
    }

    private static Transform CreateMarker(Transform parent, string name, Vector3 point)
    {
        Transform marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.localPosition = point;
        return marker;
    }

    [MenuItem("DeadRail/Route/Inspect layout")]
    public static void InspectLayout()
    {
        var report = new StringBuilder();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            report.AppendLine($"ROOT {root.name} {root.transform.position} active={root.activeSelf}");
            if (root.name == "보여주기" || root.name == "Train" || root.name == "BlackoutCutscene")
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    report.AppendLine($"  {child.name} world={child.position:F3} local={child.localPosition:F3} rot={child.eulerAngles:F1}");
                    if (child.TryGetComponent(out PlayableDirector director))
                        report.AppendLine($"  DIRECTOR {director.duration} seconds");
                }
            }
        }
        Directory.CreateDirectory("Temp/DeadRailRoute");
        File.WriteAllText("Temp/DeadRailRoute/layout.txt", report.ToString());
        Debug.Log("[RailRoute] Layout written to Temp/DeadRailRoute/layout.txt");
    }
}
