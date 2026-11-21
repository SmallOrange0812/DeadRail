using System;
using System.Linq;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class FarmingMapAuthoring
{
    private const string MaterialFolder = "Assets/_Dev_A/Materials/FarmingMaps";
    private const string TunnelAsset = "Assets/_Dev_A/Models/New_FBX/DeadRail_Blackout_Tunnel.fbx";
    private const string VariantFolder = "Assets/_Dev_A/Prefabs/FarmingStops";
    private static readonly Vector3 AuthoringPosition = Vector3.forward * 640f;

    [MenuItem("DeadRail/Route/Refresh underground lighting")]
    public static void RefreshUndergroundLighting()
    {
        RailRouteLoop loop = UnityEngine.Object.FindFirstObjectByType<RailRouteLoop>();
        if (loop == null || loop.farmingMaps == null) return;
        for (int i = MapNodeManager.NormalMapCount; i < loop.farmingMaps.variants.Length; i++)
        {
            foreach (Light light in loop.farmingMaps.variants[i].GetComponentsInChildren<Light>(true))
            {
                Undo.RecordObject(light, "Adjust underground work lights");
                light.intensity = light.name == "Farming work light" ? 18f : 12f;
                EditorUtility.SetDirty(light);
            }
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    [MenuItem("DeadRail/Route/Update farming maps %#F7")]
    public static void UpdateMaps()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
        RailRouteLoop loop = UnityEngine.Object.FindFirstObjectByType<RailRouteLoop>();
        if (loop == null || loop.farmingGeometry == null) throw new InvalidOperationException("Build the route loop first.");
        Transform farm = loop.farmingGeometry;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Update DeadRail farming maps");
        int group = Undo.GetCurrentGroup();
        try
        {
            Undo.RecordObject(loop, "Update route map references");
            foreach (Transform child in farm)
            {
                if (!child.name.StartsWith("DeadRail_Blackout_Tunnel") && !child.name.StartsWith("DeadRail_Track_ThreeWay")) continue;
                Undo.RecordObject(child.gameObject, "Use outdoor farming approach");
                child.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }

            FarmingMapSet set = farm.GetComponent<FarmingMapSet>();
            if (set == null) set = Undo.AddComponent<FarmingMapSet>(farm.gameObject);
            Undo.RecordObject(set, "Update map variants");
            if (set.variants != null)
                foreach (GameObject variant in set.variants) if (variant != null) Undo.DestroyObjectImmediate(variant);
            set.variants = new GameObject[MapNodeManager.MapCount];
            if (!AssetDatabase.IsValidFolder(VariantFolder))
                AssetDatabase.CreateFolder("Assets/_Dev_A/Prefabs", "FarmingStops");
            for (int i = 0; i < set.variants.Length; i++)
            {
                Transform variant = NewRoot(farm, MapNodeManager.GetMapName(i));
                BuildThemedStop(variant, i);
                string path = $"{VariantFolder}/Stop_{i + 1:00}.prefab";
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(variant.gameObject, path, out bool saved);
                if (!saved) throw new InvalidOperationException($"Could not save farming stop {i + 1}.");
                Undo.DestroyObjectImmediate(variant.gameObject);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, farm);
                Undo.RegisterCreatedObjectUndo(instance, "Place farming stop prefab");
                instance.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                set.variants[i] = instance;
            }
            loop.farmingMaps = set;
            loop.startingYard = UnityEngine.Object.FindFirstObjectByType<StartingRailYard>();
            ConfigureSupplies(loop, farm);
            Transform roadblock = loop.junctionGeometry.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.StartsWith("DeadRail_Approach_Roadblock"));
            if (roadblock == null) throw new InvalidOperationException("The central tunnel approach roadblock is missing.");
            loop.centralRoadblock = roadblock.gameObject;
            Undo.RecordObject(roadblock.gameObject, "Close central route by default");
            roadblock.gameObject.SetActive(true);
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(loop);
            Undo.RecordObject(farm, "Park farming layout away from the starting depot");
            Undo.RecordObject(farm.gameObject, "Hide farming layout in the starting scene");
            farm.SetPositionAndRotation(AuthoringPosition, Quaternion.identity);
            farm.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(farm);
            PrefabUtility.RecordPrefabInstancePropertyModifications(farm.gameObject);
            NetworkObjectPostprocessor.BakeScene(SceneManager.GetActiveScene());
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(group);
            Debug.Log("[DeadRail] Saved 6 outdoor maps, 2 enclosed blackout maps, 6 shared supply types and central roadblock.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static void ConfigureSupplies(RailRouteLoop loop, Transform farm)
    {
        var kinds = new[] { FarmingSupplyKind.RouteCapacity, FarmingSupplyKind.MedicalKit, FarmingSupplyKind.RepairKit, FarmingSupplyKind.Ammunition, FarmingSupplyKind.Fuel, FarmingSupplyKind.Battery };
        var points = new Transform[kinds.Length];
        var renderers = new Renderer[kinds.Length];
        var labels = new TextMesh[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            Transform point = farm.Find($"Supply_{i + 1}");
            if (point == null) point = Box(farm, $"Supply_{i + 1}", Vector3.zero, Vector3.one, Material("Supply", new Color(0.7f, 0.7f, 0.65f))).transform;
            Undo.RecordObject(point, "Position shared supplies");
            point.localPosition = new Vector3(6f + (i % 3) * 4f, 0.45f, -8f + (i / 3) * 11f);
            point.localScale = new Vector3(1f, 0.8f, 1f);
            point.localRotation = Quaternion.identity;
            Transform label = point.Find("Supply label");
            if (label == null) label = NewRoot(point, "Supply label");
            TextMesh text = label.GetComponent<TextMesh>();
            if (text == null) text = Undo.AddComponent<TextMesh>(label.gameObject);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 48;
            text.characterSize = 0.055f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = kinds[i].ToString();
            label.localPosition = new Vector3(0f, 1.3f, 0f);
            label.localRotation = Quaternion.Euler(0f, 90f, 0f);
            points[i] = point;
            renderers[i] = point.GetComponent<Renderer>();
            labels[i] = text;
        }
        loop.supplyPoints = points;
        loop.supplyRenderers = renderers;
        loop.supplyKinds = kinds;
        loop.supplyLabels = labels;
    }

    private static Transform NewRoot(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(obj, "Create farming geometry");
        obj.transform.SetParent(parent, false);
        return obj.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material) => Primitive(parent, name, PrimitiveType.Cube, position, scale, material);
    private static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material) => Primitive(parent, name, PrimitiveType.Cylinder, position, scale, material);

    private static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        Undo.RegisterCreatedObjectUndo(obj, "Create farming prop");
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }

    private static void PointLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
    {
        Transform obj = NewRoot(parent, name);
        obj.localPosition = position;
        Light light = obj.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    private static Material Material(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Dev_A/Materials")) AssetDatabase.CreateFolder("Assets/_Dev_A", "Materials");
            AssetDatabase.CreateFolder("Assets/_Dev_A/Materials", "FarmingMaps");
        }
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = name;
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
