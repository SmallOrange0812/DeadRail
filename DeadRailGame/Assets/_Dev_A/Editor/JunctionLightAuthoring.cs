using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class JunctionLightAuthoring
{
    private const string ScenePath = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";
    private const string AssetFolder = "Assets/_Dev_A/Prefabs/RouteLandscape";

    [MenuItem("DeadRail/Route/Apply junction warning lights")]
    private static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open Dev_A_Scene in Edit Mode.");

        var route = UnityEngine.Object.FindFirstObjectByType<RailRouteLoop>();
        Transform live = route.junctionGeometry.Find("Example geometry");
        Material redLens = RailLandscapeAuthoring.NightSignalMaterial();
        SetupTunnel(live.Find("DeadRail_Blackout_Tunnel"), route.junctionGeometry, redLens);
        SetupBarrier(route.centralRoadblock.transform, route.junctionGeometry, redLens);
        SetupSwitch(live.Find("DeadRail_TrackSwitch"));

        if (route.referenceLayout != null)
        {
            Transform cinematic = route.referenceLayout.transform;
            Transform[] props = cinematic.GetComponentsInChildren<Transform>(true);
            Transform tunnel = props.FirstOrDefault(prop => prop.name == "DeadRail_Blackout_Tunnel");
            Transform barrier = props.FirstOrDefault(prop => prop.name == "DeadRail_Approach_Roadblock");
            if (tunnel != null) SetupTunnel(tunnel, cinematic, redLens);
            if (barrier != null) SetupBarrier(barrier, cinematic, redLens);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("The scene could not be saved.");
        Debug.Log("[Rail lighting] Bright switch labels, four tunnel steps and alternating barrier lights saved.");
    }

    private static void SetupTunnel(Transform tunnel, Transform frame, Material material)
    {
        MeshRenderer[] lenses = FindLenses(tunnel, "Tunnel_Emergency_Lamp");
        float center = frame.InverseTransformPoint(tunnel.position).x;
        MeshRenderer[] left = lenses.Where(lens => Position(lens).x < center).OrderBy(lens => Position(lens).z).ToArray();
        MeshRenderer[] right = lenses.Where(lens => Position(lens).x > center).OrderBy(lens => Position(lens).z).ToArray();
        if (left.Length != 4 || right.Length != 4) throw new InvalidOperationException($"Expected four wall lamps per side on {tunnel.name}.");

        var lamps = new RailWarningSequence.Lamp[8];
        for (int step = 0; step < 4; step++)
        {
            lamps[step * 2] = ConfigureLens(left[step], material, step, frame.right * .36f, 4.6f, 1.05f);
            lamps[step * 2 + 1] = ConfigureLens(right[step], material, step, -frame.right * .36f, 4.6f, 1.05f);
        }
        SetSequence(tunnel, lamps, 4);

        Vector3 Position(MeshRenderer lens) => frame.InverseTransformPoint(lens.bounds.center);
    }

    private static void SetupBarrier(Transform barrier, Transform frame, Material material)
    {
        MeshRenderer[] lenses = FindLenses(barrier, "Warning_Red_Beacon")
            .OrderBy(lens => frame.InverseTransformPoint(lens.bounds.center).x).ToArray();
        if (lenses.Length != 4) throw new InvalidOperationException($"Expected four beacons on {barrier.name}.");
        var lamps = lenses.Select((lens, index) => ConfigureLens(lens, material, index % 2,
            Vector3.up * .12f - frame.forward * .15f, 3.3f, .74f)).ToArray();
        SetSequence(barrier, lamps, 2);
    }

    private static MeshRenderer[] FindLenses(Transform root, string prefix) => root.GetComponentsInChildren<MeshRenderer>(true)
        .Where(renderer => renderer.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

    private static RailWarningSequence.Lamp ConfigureLens(MeshRenderer lens, Material material, int step,
        Vector3 offset, float range, float intensity)
    {
        AssignMaterial(lens, material);
        Light spill = GetLight(lens.transform, "Warning light spill");
        spill.transform.position = lens.bounds.center + offset;
        spill.type = LightType.Point;
        spill.color = new Color(1f, .055f, .035f);
        spill.range = range;
        spill.intensity = intensity;
        spill.shadows = LightShadows.None;
        spill.enabled = step == 0;
        PrefabUtility.RecordPrefabInstancePropertyModifications(spill);
        PrefabUtility.RecordPrefabInstancePropertyModifications(spill.transform);
        return new RailWarningSequence.Lamp { lens = lens, spill = spill, step = step };
    }

    private static void SetSequence(Transform root, RailWarningSequence.Lamp[] lamps, int stepCount)
    {
        if (!root.TryGetComponent(out RailWarningSequence sequence)) sequence = Undo.AddComponent<RailWarningSequence>(root.gameObject);
        Undo.RecordObject(sequence, "Configure warning light order");
        sequence.Configure(stepCount, lamps);
        sequence.PresentAt(0d);
        EditorUtility.SetDirty(sequence);
    }

    private static void SetupSwitch(Transform root)
    {
        Material legends = LampMaterial("SwitchLettering", new Color(.79f, .78f, .65f), new Color(.71f, .61f, .36f));
        Material target = LampMaterial("SwitchTargetAmber", new Color(.80f, .24f, .04f), new Color(.94f, .25f, .037f));
        Material green = LampMaterial("SwitchRouteGreen", new Color(.07f, .7f, .29f), new Color(.054f, 1.1f, .29f));
        Material orange = LampMaterial("SwitchRouteAmber", new Color(.84f, .33f, .035f), new Color(1.2f, .37f, .034f));
        Material inactive = LampMaterial("SwitchRouteInactive", new Color(.095f, .15f, .17f), new Color(.024f, .042f, .049f));
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name is "Route_LEFT" or "Route_STRAIGHT" or "Route_RIGHT") AssignMaterial(renderer, legends);
            if (renderer.name == "Target_Bullseye") AssignMaterial(renderer, target);
            if (renderer.name is "Route_Lamp_LEFT" or "Route_Lamp_RIGHT") AssignMaterial(renderer, inactive);
            if (renderer.name == "Route_Lamp_STRAIGHT") AssignMaterial(renderer, green);
        }

        var switchSettings = new SerializedObject(root.GetComponent<TrackSwitch>());
        switchSettings.FindProperty("leftRouteLampOn").objectReferenceValue = green;
        switchSettings.FindProperty("rightRouteLampOn").objectReferenceValue = orange;
        switchSettings.FindProperty("inactiveRouteLamp").objectReferenceValue = inactive;
        switchSettings.ApplyModifiedProperties();

        Light fill = GetLight(root, "Switch board hood light");
        fill.transform.localPosition = new Vector3(0f, -.94f, 3.44f);
        fill.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, .83f, -.63f), Vector3.forward);
        fill.type = LightType.Spot;
        fill.spotAngle = 120f;
        fill.innerSpotAngle = 84f;
        fill.range = 4.3f;
        fill.color = new Color(.84f, .9f, 1f);
        fill.intensity = 4f;
        fill.shadows = LightShadows.None;
    }

    private static void AssignMaterial(MeshRenderer renderer, Material material)
    {
        Undo.RecordObject(renderer, "Adjust night lighting material");
        renderer.sharedMaterial = material;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }

    private static Light GetLight(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(child.gameObject, "Add junction night light");
        }
        if (!child.TryGetComponent(out Light light)) light = Undo.AddComponent<Light>(child.gameObject);
        Undo.RecordObjects(new UnityEngine.Object[] { child, light }, "Adjust junction night light");
        return light;
    }

    private static Material LampMaterial(string name, Color tint, Color emission)
    {
        string path = $"{AssetFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", tint);
        material.SetColor("_EmissionColor", emission);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }
}
