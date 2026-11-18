using System;
using System.Linq;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FarmingMapAuthoring
{
    private const string MaterialFolder = "Assets/_Dev_A/Materials/FarmingMaps";
    private const string TunnelAsset = "Assets/_Dev_A/Models/New_FBX/DeadRail_Blackout_Tunnel.fbx";

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
            for (int i = 0; i < set.variants.Length; i++)
            {
                Transform variant = NewRoot(farm, MapNodeManager.GetMapName(i));
                if (i < MapNodeManager.NormalMapCount) BuildOutdoor(variant, i);
                else BuildUnderground(variant, i);
                set.variants[i] = variant.gameObject;
                variant.gameObject.SetActive(false);
            }
            loop.farmingMaps = set;
            ConfigureSupplies(loop, farm);
            Transform roadblock = loop.junctionGeometry.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.StartsWith("DeadRail_Approach_Roadblock"));
            if (roadblock == null) throw new InvalidOperationException("The central tunnel approach roadblock is missing.");
            loop.centralRoadblock = roadblock.gameObject;
            Undo.RecordObject(roadblock.gameObject, "Close central route by default");
            roadblock.gameObject.SetActive(true);
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(loop);
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

    private static void BuildOutdoor(Transform root, int map)
    {
        Material steel = Material("Steel", new Color(0.22f, 0.28f, 0.3f));
        Material rust = Material("Rust", new Color(0.48f, 0.23f, 0.1f));
        Material concrete = Material("Concrete", new Color(0.45f, 0.46f, 0.4f));
        Material sand = Material("Sand", new Color(0.58f, 0.48f, 0.31f));
        switch (map)
        {
            case 0:
                for (int i = 0; i < 8; i++)
                {
                    Box(root, "Bypass fence post", new Vector3(18f, 0.8f, -25f + i * 7f), new Vector3(0.2f, 1.8f, 0.2f), steel);
                    Box(root, "Trackside sleepers", new Vector3(-7f, 0f, -24f + i * 7f), new Vector3(3f, 0.18f, 0.5f), rust);
                }
                Box(root, "Bypass service fence", new Vector3(18f, 1.2f, 0f), new Vector3(0.1f, 0.15f, 50f), steel);
                break;
            case 1:
                Box(root, "Station canopy", new Vector3(11f, 5.8f, 18f), new Vector3(14f, 0.35f, 15f), steel);
                foreach (float x in new[] { 5f, 17f })
                    foreach (float z in new[] { 12f, 24f })
                        Box(root, "Canopy column", new Vector3(x, 2.8f, z), new Vector3(0.35f, 5.7f, 0.35f), steel);
                Box(root, "Station bench", new Vector3(10f, 0.5f, 20f), new Vector3(5f, 0.3f, 1f), rust);
                Box(root, "Station sign", new Vector3(17f, 2.5f, 9f), new Vector3(0.25f, 1.5f, 4f), concrete);
                break;
            case 2:
                for (int i = 0; i < 12; i++)
                {
                    GameObject scrap = Box(root, "Scrap stack", new Vector3(5f + (i % 4) * 3.5f, 0.4f + (i % 3) * 0.35f, i < 6 ? -23f : 22f), new Vector3(2.2f, 0.7f + (i % 3), 2.5f), rust);
                    scrap.transform.localRotation = Quaternion.Euler(0f, i * 23f, i % 2 == 0 ? 8f : -8f);
                }
                Cylinder(root, "Scrap tank", new Vector3(18f, 1.5f, 14f), new Vector3(2.4f, 1.5f, 2.4f), steel);
                break;
            case 3:
                Box(root, "Dry earth", new Vector3(11f, -0.2f, 0f), new Vector3(28f, 0.15f, 70f), sand);
                for (int i = 0; i < 8; i++)
                {
                    GameObject rock = Primitive(root, "Dry plain rock", PrimitiveType.Sphere, new Vector3(5f + (i % 4) * 4f, 0.15f, i < 4 ? -24f : 24f), new Vector3(2.5f, 0.9f + (i % 3) * 0.4f, 2f), concrete);
                    rock.transform.localRotation = Quaternion.Euler(8f * i, 19f * i, 4f * i);
                }
                break;
            case 4:
                Box(root, "Factory rear wall", new Vector3(11f, 3.6f, 28f), new Vector3(16f, 7.5f, 0.4f), concrete);
                Box(root, "Factory roof", new Vector3(11f, 7.2f, 20f), new Vector3(16f, 0.4f, 16f), steel);
                foreach (float x in new[] { 4f, 18f }) Box(root, "Factory support", new Vector3(x, 3.5f, 13f), new Vector3(0.5f, 7f, 0.5f), steel);
                for (int i = 0; i < 3; i++) Cylinder(root, "Fuel storage silo", new Vector3(6f + i * 5f, 2.5f, -24f), new Vector3(3f, 2.5f, 3f), rust);
                break;
            case 5:
                for (int i = 0; i < 5; i++)
                {
                    GameObject wall = Box(root, "Broken research wall", new Vector3(4f + i * 3.5f, 1.4f + i % 2, 23f), new Vector3(2.8f, 3f + i % 2, 0.5f), concrete);
                    wall.transform.localRotation = Quaternion.Euler(0f, 0f, -12f + i * 7f);
                    Box(root, "Laboratory rubble", new Vector3(5f + i * 2.7f, 0.25f, -23f), new Vector3(2f, 0.5f, 2f), concrete);
                }
                Box(root, "Collapsed laboratory beam", new Vector3(11f, 4f, 20f), new Vector3(15f, 0.5f, 0.6f), steel).transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                break;
        }
    }

    private static void BuildUnderground(Transform root, int map)
    {
        bool contaminated = map == MapNodeManager.NormalMapCount;
        Material steel = Material("Steel", new Color(0.22f, 0.28f, 0.3f));
        Material concrete = Material("Concrete", new Color(0.45f, 0.46f, 0.4f));
        Material rust = Material("Rust", new Color(0.48f, 0.23f, 0.1f));
        Color lightColor = contaminated ? new Color(0.38f, 0.8f, 0.5f) : new Color(1f, 0.66f, 0.3f);
        Box(root, "Underground floor", new Vector3(2f, -0.25f, 0f), new Vector3(42f, 0.2f, 245f), concrete);
        GameObject tunnelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelAsset);
        if (tunnelAsset == null) throw new InvalidOperationException("Blackout tunnel FBX is missing.");
        for (int i = 0; i < 10; i++)
        {
            Transform shell = NewRoot(root, $"Underground tunnel shell {i + 1}");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(tunnelAsset);
            model.transform.SetParent(shell, false);
            shell.localPosition = new Vector3(0f, 0f, -96f + i * 24f);
            shell.localScale = new Vector3(4f, 1.5f, 1.01f);
            PointLight(root, $"Tunnel working light {i + 1}", new Vector3(0f, 6f, -108f + i * 24f), lightColor, 24f, 12f);
        }
        Box(root, "Distant tunnel end", new Vector3(0f, 6f, 122f), new Vector3(42f, 12f, 0.4f), concrete);
        Box(root, "Distant tunnel rear", new Vector3(0f, 6f, -122f), new Vector3(42f, 12f, 0.4f), concrete);
        PointLight(root, "Farming work light", new Vector3(10f, 4.5f, -1f), lightColor, 20f, 18f);
        for (int i = 0; i < 5; i++)
        {
            if (contaminated)
                Cylinder(root, "Contaminated storage tank", new Vector3(16f, 1.5f, -24f + i * 12f), new Vector3(2.2f, 1.5f, 2.2f), steel);
            else
            {
                Box(root, "Black market cargo stack", new Vector3(7f + i * 2.4f, 0.8f, 23f), new Vector3(2f, 1.6f + i % 2, 2.5f), rust);
                Box(root, "Cargo shelving", new Vector3(17f, 1.4f, -22f + i * 10f), new Vector3(1f, 2.8f, 4f), steel);
            }
        }
        if (contaminated)
        {
            Material chemical = Material("Contaminated liquid", new Color(0.12f, 0.55f, 0.2f));
            Box(root, "Contaminated drainage", new Vector3(-10f, -0.1f, 0f), new Vector3(6f, 0.08f, 60f), chemical);
        }
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
