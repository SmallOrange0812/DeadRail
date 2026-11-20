using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class StartingRailYardAuthoring
{
    private const string ScenePath = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";
    private const string Folder = "Assets/_Dev_A/StartingRailYard";
    private const string PrefabPath = Folder + "/AbandonedRailDepot.prefab";
    private const string CitySection = "City and viaduct";
    private const float BoardingViewForward = 5.5f;

    [MenuItem("DeadRail/Starting Rail Yard/Build and apply")]
    public static void BuildAndApply() => Apply(false);

    [MenuItem("DeadRail/Starting Rail Yard/Update skyline and evening")]
    public static void UpdateSkylineAndEvening()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before updating the starting yard.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }

        new Depot().UpdateSkyline();
        SaveEvening(scene);
        Debug.Log("Updated the depot façades and evening lighting in Dev_A_Scene.");
    }

    [MenuItem("DeadRail/Starting Rail Yard/Regenerate geometry and apply")]
    public static void Regenerate()
    {
        if (EditorUtility.DisplayDialog("Regenerate the starting yard?",
                "Regenerate the depot prefab geometry and collider layout from the authoring script.", "Regenerate", "Cancel"))
            Apply(true);
    }

    private static void Apply(bool regenerate)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before authoring the starting yard.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
        Transform yard = scene.GetRootGameObjects().Single(root => root.name == "RailYard").transform;
        Transform train = scene.GetRootGameObjects().Single(root => root.name == "Train").transform;
        Transform[] spawns = Enumerable.Range(1, 4).Select(i => yard.Find($"Spawn_P{i}")).ToArray();
        if (spawns.Any(spawn => spawn == null))
            throw new InvalidOperationException("The four original RailYard spawn markers are required.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || regenerate) prefab = new Depot().BuildPrefab();

        Transform oldInstance = yard.Find(prefab.name);
        GameObject instance = oldInstance != null ? oldInstance.gameObject :
            (GameObject)PrefabUtility.InstantiatePrefab(prefab, yard);
        if (oldInstance == null) Undo.RegisterCreatedObjectUndo(instance, "Add abandoned rail depot");
        Undo.RecordObject(instance.transform, "Place abandoned rail depot");
        instance.transform.SetPositionAndRotation(train.position, train.rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        RailRouteLoop route = UnityEngine.Object.FindFirstObjectByType<RailRouteLoop>();
        if (route != null && route.gameObject.scene == scene)
        {
            Undo.RecordObject(route, "Connect the starting yard to the route");
            route.startingYard = instance.GetComponent<StartingRailYard>();
        }

        foreach (Transform spawn in spawns)
        {
            Undo.RecordObject(spawn, "Face the boarding area");
            Vector3 target = new Vector3(train.position.x, spawn.position.y, train.position.z + BoardingViewForward);
            spawn.rotation = Quaternion.LookRotation(target - spawn.position);
        }

        SaveEvening(scene);
        Selection.activeGameObject = yard.gameObject;
        SceneView.lastActiveSceneView?.LookAt(new Vector3(0, 1.5f, 9), Quaternion.Euler(26, -32, 0), 51);
        Debug.Log("Abandoned rail depot applied to RailYard with all four original spawn references.");
    }

    private static void SaveEvening(Scene scene)
    {
        Light sun = scene.GetRootGameObjects().Single(root => root.name == "Directional Light").GetComponent<Light>();
        Undo.RecordObjects(new UnityEngine.Object[] { sun, sun.transform }, "Set evening sunlight");
        sun.color = Tint(255, 143, 102);
        sun.intensity = 0.60f;
        sun.transform.rotation = Quaternion.Euler(2.5f, 120f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.skybox = Depot.CreateSky();
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Tint(109, 120, 157);
        RenderSettings.ambientEquatorColor = Tint(89, 93, 115);
        RenderSettings.ambientGroundColor = Tint(55, 52, 66);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Tint(101, 106, 128);
        RenderSettings.fogStartDistance = 10f;
        RenderSettings.fogEndDistance = 88f;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save evening lighting in the main scene.");
    }

    private static Color Tint(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    private enum Surface
    {
        Asphalt, Gravel, Concrete, DarkConcrete, Steel, Rust, Teal, FadedTeal, Cream, Red, Glass,
        Black, Yellow, Soil, Moss, Grass, Straw, Wood, Puddle, GravelLight, Paint, Haze
    }

    private sealed class Depot
    {
        private readonly System.Random random = new System.Random(5719);
        private readonly Color[] palette =
        {
            Tint(89, 88, 74), Tint(98, 86, 68), Tint(158, 150, 125), Tint(109, 104, 87),
            Tint(78, 83, 76), Tint(126, 65, 37), Tint(51, 92, 87), Tint(94, 123, 113),
            Tint(185, 175, 146), Tint(140, 61, 49), Tint(49, 67, 63), Tint(36, 37, 31),
            Tint(208, 158, 59), Tint(76, 70, 49), Tint(86, 94, 54), Tint(121, 127, 65),
            Tint(151, 134, 76), Tint(98, 74, 50), Tint(62, 70, 61), Tint(128, 116, 91),
            Tint(211, 199, 154), Tint(126, 139, 129)
        };
        private const float Floor = -0.19f;
        private const float DepartureHalfWidth = 10f;
        private const float DepartureEnd = 150f;
        private const float YardNorth = 66.9f;
        private string meshPath;
        private Material[] materials;
        private Transform geometry;
        private Transform collision;
        private Chunk chunk;

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        private bool Chance(float threshold) => random.NextDouble() < threshold;
        private static Vector3 P(float x, float y, float z) => new Vector3(x, y, z);

        public GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Dev_A", "StartingRailYard");
            meshPath = Folder + "/DepotGeometry.asset";
            var root = new GameObject("AbandonedRailDepot");
            try
            {
                geometry = Child(root.transform, "Yard scenery");
                collision = Child(geometry, "Walkable ground and barriers");
                var presentation = new SerializedObject(root.AddComponent<StartingRailYard>());
                presentation.FindProperty("scenery").objectReferenceValue = geometry;
                CreateMaterials();
                presentation.ApplyModifiedPropertiesWithoutUndo();

                Section("Service apron and ballast", Apron);
                Section("West sidings", () => Sidings(new[] {-34f, -27f, -20f, -13f}));
                Section("East sidings", () => Sidings(new[] {14f, 21f, 28f, 35f}));
                Section("Maintenance hall 01", () => Shed(-26, 32, 21, 31, false));
                Section("Maintenance hall 02", () => Shed(24, 37, 22, 30, true));
                Section("Abandoned carriages west", () =>
                {
                    Carriage(-13, 13, 0, false); Carriage(-20, -15, -7, true);
                    Carriage(-27, 0, 0, false); Carriage(-27, 18, 0, false); Carriage(-34, 14, 0, false);
                });
                Section("Abandoned carriages east", () =>
                {
                    Carriage(14, 14, 3, true); Carriage(21, 2, 0, false);
                    Carriage(28, 8, 0, false); Carriage(35, 19, 0, false);
                });
                Section("Overhead power lines", PowerLines);
                Section("Collapsed checkpoint and perimeter", Perimeter);
                Section("Boarding apron wreckage", BoardingWreckage);
                Section("Ballast weeds and debris", Debris);
                Section(CitySection, Skyline);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success) throw new IOException($"Could not save {PrefabPath}");
                AssetDatabase.SaveAssets();
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public void UpdateSkyline()
        {
            var storedMaterials = AssetDatabase.LoadAllAssetsAtPath(Folder + "/Weathering.asset")
                .OfType<Material>().ToDictionary(material => material.name);
            chunk = new Chunk();
            Skyline();
            GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform city = prefab.transform.Find("Yard scenery/" + CitySection);
                Mesh storedMesh = city.GetComponent<MeshFilter>().sharedMesh;
                chunk.ToMesh(CitySection, out int[] surfaces, storedMesh);
                EditorUtility.SetDirty(storedMesh);
                city.GetComponent<MeshRenderer>().sharedMaterials =
                    surfaces.Select(index => storedMaterials[((Surface)index).ToString()]).ToArray();
                collision = prefab.transform.Find("Yard scenery/Walkable ground and barriers");
                UpdateDepartureColliders();
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath, out bool saved);
                if (!saved) throw new IOException("Could not save the depot departure corridor.");
                AssetDatabase.SaveAssetIfDirty(storedMesh);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        private void CreateMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The URP Lit shader is required.");
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = "Weathering", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[128 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float broad = Mathf.PerlinNoise(x / 19f, y / 23f);
                float grain = Mathf.PerlinNoise(x / 3.1f, y / 4.5f);
                float chips = Chance(0.05f) ? Range(-36, 15) : 0;
                byte tone = (byte)Mathf.Clamp(153 + broad * 43 + grain * 36 + Range(-14, 13) + chips, 85, 255);
                pixels[y * 128 + x] = new Color32(tone, tone, tone, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            string texturePath = Folder + "/Weathering.asset";
            Texture2D existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (existingTexture == null) AssetDatabase.CreateAsset(texture, texturePath);
            else texture = UpdateAsset(existingTexture, texture);
            Material[] existingMaterials = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Material>().ToArray();
            materials = palette.Select((color, i) =>
            {
                Surface surface = (Surface)i;
                var material = new Material(shader) { name = surface.ToString(), color = color };
                material.SetFloat("_Smoothness", surface == Surface.Puddle ? 0.8f : surface == Surface.Glass ? 0.44f : 0.13f);
                material.SetFloat("_Metallic", surface == Surface.Steel ? 0.31f : 0f);
                if (surface != Surface.Paint && surface != Surface.Yellow && surface != Surface.Haze)
                    material.SetTexture("_BaseMap", texture);
                Material existing = existingMaterials.FirstOrDefault(asset => asset.name == material.name);
                if (existing != null) return UpdateAsset(existing, material);
                AssetDatabase.AddObjectToAsset(material, texturePath);
                return material;
            }).ToArray();
        }

        public static Material CreateSky()
        {
            string path = Folder + "/OvercastSky.mat";
            Shader shader = Shader.Find("DeadRail/Starting Rail Yard/Overcast Sky");
            if (shader == null) throw new InvalidOperationException("The depot sky shader is required.");
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.shader == shader) return existing;
            var sky = new Material(shader) { name = "OvercastSky" };
            if (existing != null)
            {
                sky = UpdateAsset(existing, sky);
                AssetDatabase.SaveAssetIfDirty(sky);
                return sky;
            }
            AssetDatabase.CreateAsset(sky, path);
            return sky;
        }

        private static T UpdateAsset<T>(T existing, T replacement) where T : UnityEngine.Object
        {
            EditorUtility.CopySerialized(replacement, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(replacement);
            return existing;
        }

        private static Transform Child(Transform parent, string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            return obj.transform;
        }

        private void Section(string name, Action build)
        {
            chunk = new Chunk();
            build();
            Mesh mesh = chunk.ToMesh(name, out int[] surfaces);
            Mesh existing = AssetDatabase.LoadAllAssetsAtPath(meshPath).OfType<Mesh>().FirstOrDefault(asset => asset.name == name);
            if (existing != null) mesh = UpdateAsset(existing, mesh);
            else if (AssetDatabase.LoadMainAssetAtPath(meshPath) == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else AssetDatabase.AddObjectToAsset(mesh, meshPath);
            Transform obj = Child(geometry, name);
            obj.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = surfaces.Select(index => materials[index]).ToArray();
            renderer.shadowCastingMode = name == CitySection ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        private void Solid(string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            Transform obj = Child(collision, name);
            obj.SetLocalPositionAndRotation(center, rotation);
            obj.gameObject.AddComponent<BoxCollider>().size = size;
        }

        private void Box(Surface material, float x, float y, float z, float w, float h, float d)
            => chunk.Box(material, P(x,y,z), P(w,h,d), Quaternion.identity);

        private void Beam(Surface material, Vector3 start, Vector3 end, float width, float depth)
        {
            Vector3 axis = end - start;
            chunk.Box(material, (start + end) * 0.5f, P(width, depth, axis.magnitude), Quaternion.LookRotation(axis,
                Mathf.Abs(axis.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
        }

        private void Patch(Surface surface, float x, float z, float rx, float rz, float y = Floor + 0.008f)
        {
            int sides = random.Next(5, 10);
            var rim = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = -(float)i / sides * 2f * Mathf.PI;
                float radius = Range(0.66f, 1f);
                rim[i] = P(x + rx * Mathf.Cos(angle) * radius, y, z + rz * Mathf.Sin(angle) * radius);
            }
            for (int i = 0; i < sides; i++)
                chunk.Face(surface, P(x, y, z), rim[i], rim[(i + 1) % sides]);
        }

        private void Apron()
        {
            Box(Surface.Asphalt, 0, Floor - 0.22f, 5, 94, 0.44f, 124);
            Solid("Depot ground", P(0, Floor - 0.27f, 5), P(94, 0.44f, 124), Quaternion.identity);
            for (int i = 0; i < 470; i++)
            {
                float x = Range(-45, 45), z = Range(-55, 65);
                Surface finish = Mathf.Abs(x) > 10f ? (Chance(0.52f) ? Surface.Soil : Surface.Moss) : Surface.DarkConcrete;
                Patch(finish, x, z, Range(0.45f, 3.4f), Range(0.3f, 3.1f));
            }
            for (int i = 0; i < 37; i++)
            {
                float x = Range(-42, 42), z = Range(-47, 56);
                if (Mathf.Abs(x) > 3.5f) Patch(Surface.Puddle, x, z, Range(0.32f, 2.1f), Range(0.55f, 3), Floor + 0.017f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int z = -15; z <= 17; z += 3)
                    if (Chance(0.83f)) Box(Surface.Yellow, side * 5.5f, Floor + 0.012f, z, 0.095f, 0.012f, Range(1, 2.55f));
                for (int i = 0; i < 25; i++)
                {
                    float z = Range(-19, 24);
                    Vector3 p = P(side * Range(4.5f, 11.6f), Floor + 0.023f, z);
                    Vector3 q = p + P(Range(-0.65f, 0.8f), 0, Range(0.15f, 0.95f));
                    Vector3 r = q + P(Range(-0.35f, 0.8f), 0, Range(0.15f, 1.1f));
                    Beam(Surface.Black, p, q, 0.024f, 0.003f);
                    Beam(Surface.Black, q, r, 0.014f, 0.003f);
                }
            }
        }

        private void Sidings(IEnumerable<float> tracks)
        {
            foreach (float x in tracks)
            {
                Box(Surface.Gravel, x, Floor + 0.009f, 7, 3.2f, 0.03f, 111);
                for (int i = 0; i < 115; i++)
                    Patch(Chance(0.73f) ? Surface.GravelLight : Surface.Moss,
                        x + Range(-1.54f, 1.54f), Range(-47, 61), Range(0.08f, 0.43f), Range(0.08f, 0.48f), Floor + 0.026f);
                for (float z = -48; z < 62; z += 0.67f)
                {
                    if (Chance(0.07f)) continue;
                    chunk.Box(Chance(0.8f) ? Surface.DarkConcrete : Surface.Wood, P(x, Floor + 0.04f, z),
                        P(Range(2.2f, 2.65f), 0.10f, 0.24f), Quaternion.Euler(0, Range(-3, 3), 0));
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    for (float z = -47; z <= 61; z += 9)
                    {
                        float offset = Chance(0.10f) ? Range(-0.16f, 0.15f) : 0f;
                        Box(Surface.Rust, x + side * 0.78f + offset, Floor + 0.145f, z, 0.13f, 0.14f, 8.96f);
                        Box(Surface.Steel, x + side * 0.78f + offset, Floor + 0.224f, z, 0.07f, 0.025f, 8.95f);
                    }
                }
            }
        }

        private void Shed(float x, float z, float width, float length, bool heavyDamage)
        {
            float left = x - width / 2, right = x + width / 2, end = z + length;
            float eave = 6.7f, ridge = 9f;
            for (int i = 0; i <= 3; i++)
            {
                float bay = Mathf.Lerp(left, right, i / 3f);
                Box(Surface.Concrete, bay, 3.15f, z, 0.65f, 6.65f, 0.82f);
                Solid("Inspection bay pier", P(bay, 3.15f, z), P(0.65f, 6.65f, 0.82f), Quaternion.identity);
                Box(Surface.DarkConcrete, bay, 0.67f, z, 0.81f, 1.75f, 0.98f);
                if (i == 3) continue;
                float center = bay + width / 6;
                Box(Surface.Cream, center, 5.92f, z, width / 3, 1.2f, 0.35f);
                for (int k = -3; k <= 3; k++)
                    if (Chance(0.68f)) Box(Chance(0.76f) ? Surface.Glass : Surface.Black, center + k * 0.65f, 6.03f, z - 0.19f, 0.56f, 0.5f, 0.034f);
                if (i != 1 && Chance(0.78f))
                    for (int k = 0; k < 17 - i * 4; k++)
                        Box(k % 5 == 0 ? Surface.Rust : Surface.Steel, center, 5.12f - k * 0.21f, z + 0.29f, width / 3 - 0.76f, 0.18f, 0.13f);
            }
            Box(Surface.DarkConcrete, x, 2.95f, end, width, 6.2f, 0.6f);
            Solid("Rear wall", P(x, 2.95f, end), P(width, 6.2f, 0.6f), Quaternion.identity);
            foreach (float edge in new[] {left, right})
            {
                for (int i = 0; i < 12; i++)
                {
                    float sectionZ = z + (i + 0.5f) * length / 12f;
                    float height = (heavyDamage && i is 2 or 3) || Chance(0.13f) ? Range(1.6f, 4.3f) : Range(5.7f, 6.6f);
                    Box(Chance(0.75f) ? Surface.Concrete : Surface.DarkConcrete, edge, Floor + height / 2, sectionZ, 0.37f, height, length / 12f - 0.045f);
                }
                Solid("Hall side base", P(edge, 0.62f, z + length / 2), P(0.38f, 1.6f, length), Quaternion.identity);
            }
            for (int i = 0; i <= 7; i++)
            {
                float rib = z + length * i / 7;
                Beam(Surface.Rust, P(left,eave,rib), P(x,ridge,rib), 0.17f, 0.19f);
                Beam(Surface.Rust, P(x,ridge,rib), P(right,eave,rib), 0.17f, 0.19f);
                Beam(Surface.Steel, P(left,eave,rib), P(right,eave,rib), 0.14f, 0.17f);
                for (int k = 0; k < 6; k++)
                {
                    float a = Mathf.Lerp(left, right, k / 6f), b = a + width / 6;
                    Beam(Surface.Steel, P(a,eave,rib), P(b, eave + (ridge-eave)*(1-Mathf.Abs(b-x)/(width/2)), rib), 0.075f, 0.07f);
                }
            }
            Beam(Surface.Rust, P(x,ridge,z), P(x,ridge,end), 0.17f, 0.17f);
            for (int side = -1; side <= 1; side += 2)
            for (int row = 0; row < 9; row++)
            for (int panel = 0; panel < 10; panel++)
            {
                if ((heavyDamage && side < 0 && row is >= 1 and <= 5 && panel is >= 2 and <= 8) ||
                    (side > 0 && row is 3 or 4 && panel is > 4 and < 8) || Chance(0.14f)) continue;
                float span = width / 2 / 10;
                float px = x + side * (panel + 0.5f) * span;
                float py = Mathf.Lerp(ridge, eave, (panel + 0.5f) / 10);
                float pz = z + (row + 0.5f) * length / 9;
                float slope = -side * Mathf.Atan2(ridge - eave, width / 2) * Mathf.Rad2Deg;
                var tilt = Quaternion.Euler(0, 0, slope + Range(-2.3f, 2.3f));
                Surface steel = Chance(0.17f) ? Surface.Rust : Chance(0.55f) ? Surface.Teal : Surface.FadedTeal;
                chunk.Box(steel, P(px, py + 0.07f, pz), P(span + 0.027f, 0.095f, length / 9 - 0.048f), tilt);
                for (int rib = -1; rib <= 1; rib++)
                    chunk.Box(steel, P(px, py + 0.15f, pz + rib * length / 30), P(span + 0.025f, 0.05f, 0.045f), tilt);
            }
            Box(Surface.Teal, x, 5.87f, z - 0.27f, 5.35f, 0.71f, 0.18f);
            Lettering(heavyDamage ? "DEPOT 02" : "DEPOT 01", P(x, 5.86f, z - 0.372f), 0.061f, Surface.Paint);
        }

        private void Carriage(float x, float z, float yaw, bool damaged)
        {
            Quaternion rotation = Quaternion.Euler(damaged ? -0.8f : 0, yaw, damaged ? 7f : 0);
            Vector3 origin = P(x, 0, z);
            Vector3 At(float a, float b, float c) => origin + rotation * P(a,b,c);
            void Panel(Surface surface, float a, float b, float c, float w, float h, float d)
                => chunk.Box(surface, At(a,b,c), P(w,h,d), rotation);

            Panel(Surface.Black, 0, 0.65f, 0, 2.15f, 0.5f, 14.9f);
            Panel(Surface.Cream, 0, 1.22f, 0, 2.72f, 0.77f, 16.1f);
            Panel(Surface.FadedTeal, 0, 1.11f, 0, 2.76f, 0.24f, 16.15f);
            Panel(Surface.Steel, 0, 3.30f, 0, 2.58f, 0.27f, 16.04f);
            Panel(Surface.DarkConcrete, 0, 3.48f, 0, 2.04f, 0.18f, 15.82f);
            for (int b = -1; b <= 1; b++)
            {
                Panel(Surface.Steel, 0, 3.68f, b * 4.3f, 1.52f, 0.27f, 1.79f);
                for (int l = -4; l <= 4; l++)
                    Panel(Surface.Black, 0, 3.83f, b * 4.3f + l * 0.15f, 1.17f, 0.023f, 0.043f);
            }
            foreach (int side in new[] {-1, 1})
            {
                Panel(Surface.Cream, side * 1.31f, 3.03f, 0, 0.15f, 0.34f, 16.1f);
                for (int j = -6; j <= 6; j++)
                {
                    float at = j * 1.22f;
                    bool door = j is -4 or 0 or 4;
                    bool shattered = damaged ? Chance(0.79f) : Chance(0.37f);
                    Panel(door ? Surface.Red : Surface.Cream, side * 1.33f, 2.20f, at, 0.12f, 1.76f, 1.205f);
                    Panel(Surface.Black, side * 1.401f, 2.45f, at, 0.032f, 0.7f, door ? 0.6f : 0.98f);
                    if (!shattered) Panel(Surface.Glass, side * 1.424f, 2.45f, at, 0.019f, 0.61f, door ? 0.51f : 0.89f);
                    else
                    {
                        float cap = door ? 0.287f : 0.468f;
                        Vector3 a = At(side*1.427f, 2.78f, at-cap), b = At(side*1.427f, 2.78f, at+cap*(side < 0 ? 1 : 0.1f));
                        Vector3 c = At(side*1.427f, 2.5f, at-cap);
                        chunk.Face(Surface.Glass, a, b, c); chunk.Face(Surface.Glass, a, c, b);
                    }
                    if (door) Panel(Surface.Black, side * 1.44f, 2.05f, at, 0.026f, 1.82f, 0.035f);
                    for (int k = 0; k < 4; k++)
                        if (Chance(0.68f))
                            Panel(Chance(0.46f) ? Surface.DarkConcrete : Surface.Rust, side * (1.408f + k * 0.004f),
                                Range(1.30f, 2.02f), at + Range(-0.48f,0.48f), 0.008f, Range(0.018f, 0.19f), Range(0.045f, 0.54f));
                }
                for (int k = -1; k <= 1; k += 2)
                for (int bogie = -1; bogie <= 1; bogie += 2)
                    Wheel(At(side*1.055f, 0.35f, bogie*4.85f + k*0.74f), rotation);
            }
            foreach (int end in new[] {-1, 1})
            {
                Panel(Surface.Steel, 0, 2f, end * 8.14f, 2.6f, 2.57f, 0.3f);
                Panel(Surface.Black, 0, 2.58f, end * 8.31f, 2.24f, 0.89f, 0.052f);
                Panel(Surface.Glass, end * 0.5f, 2.59f, end * 8.35f, 0.91f, 0.75f, 0.034f);
                Panel(Surface.Red, 0, 1.51f, end * 8.32f, 2.43f, 0.47f, 0.09f);
                Panel(Surface.Black, 0, 0.72f, end * 8.51f, 0.82f, 0.28f, 0.57f);
                for (int side = -1; side <= 1; side += 2)
                    Panel(Surface.Paint, side * 0.93f, 1.5f, end * 8.38f, 0.25f, 0.15f, 0.025f);
            }
            if (damaged)
            {
                Beam(Surface.Rust, At(-0.6f,3.56f,2), At(0.55f,4.72f,4.12f), 0.17f, 0.21f);
                Beam(Surface.Steel, At(0.55f,4.72f,4.12f), At(0.78f,3.54f,6.4f), 0.09f, 0.12f);
            }
            Solid("Carriage shell", At(0,1.71f,0), P(2.73f, 3.1f, 16.7f), rotation);
        }

        private void Wheel(Vector3 center, Quaternion rotation)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5, b = (i+1)*Mathf.PI/5;
                Vector3 n = rotation * P(0, Mathf.Sin(a)*0.4f, Mathf.Cos(a)*0.4f);
                Vector3 m = rotation * P(0, Mathf.Sin(b)*0.4f, Mathf.Cos(b)*0.4f);
                Vector3 side = rotation * P(0.15f,0,0);
                chunk.Face(Surface.Black, center+side, center+n+side, center+m+side);
                chunk.Face(Surface.Black, center-side, center+m-side, center+n-side);
                chunk.Face(Surface.Steel, center+n-side, center+m-side, center+m+side, center+n+side);
            }
        }

        private void PowerLines()
        {
            foreach (int z in new[] {-30,-11,15,36,59})
            {
                foreach (float x in new[] {-39f, -9f, 9f, 39f})
                {
                    Box(Surface.DarkConcrete, x, 0.21f, z, 0.64f, 0.8f, 0.72f);
                    Box(Surface.Rust, x - 0.15f, 3.96f, z, 0.1f, 8.3f, 0.12f);
                    Box(Surface.Steel, x + 0.15f, 3.96f, z, 0.1f, 8.3f, 0.12f);
                    for (int h = 0; h < 10; h++)
                        Beam(Surface.Steel, P(x-0.16f, h*0.77f+0.43f,z), P(x+0.16f,(h+1)*0.77f+0.43f,z),0.043f,0.046f);
                    Solid("Catenary post", P(x, 3.94f, z), P(0.40f,8.28f,0.37f), Quaternion.identity);
                }
                for (int j = -8; j < 8; j++)
                {
                    if (z == 15 && j is >= 2 and <= 4) continue;
                    float a = j * 4.85f, b = a + 4.85f;
                    Beam(Surface.Rust, P(a,8.24f,z), P(b,8.24f,z),0.09f,0.11f);
                    Beam(Surface.Steel, P(a,7.72f,z), P(b,7.72f,z),0.09f,0.10f);
                    Beam(Surface.Steel, P(a,7.72f,z), P((a+b)/2,8.24f,z),0.06f,0.06f);
                    Beam(Surface.Steel, P((a+b)/2,8.24f,z), P(b,7.72f,z),0.06f,0.06f);
                }
            }
            foreach (float x in new[] {-34f,-27f,-20f,-13f,0,14f,21f,28f,35f})
            {
                foreach (var span in new[] {(-30,-11),(-11,15),(15,36),(36,59)})
                {
                    if (span.Item1 == 15 && x is 14 or 21) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 At(int i) => P(x, 7.51f - Mathf.Sin(i * Mathf.PI / 8) * 0.43f, Mathf.Lerp(span.Item1,span.Item2,i/8f));
                        Beam(Surface.Black, At(k), At(k+1),0.024f,0.026f);
                    }
                }
            }
            Beam(Surface.Rust, P(10,7.72f,15), P(13,4.2f,15.35f),0.16f,0.19f);
            Beam(Surface.Steel, P(13,4.2f,15.35f), P(15.8f,2.35f,16.1f),0.10f,0.12f);
        }

        private void Perimeter()
        {
            foreach (int side in new[] {-1, 1})
            {
                for (int i=0;i<15;i++)
                {
                    float z = -53+i*8;
                    float x = 45*side;
                    float h = Chance(0.20f) ? Range(1.2f, 2.0f) : Range(2.5f,3.5f);
                    Box(Chance(0.64f) ? Surface.DarkConcrete : Surface.Concrete, x, Floor+h/2,z,0.65f,h,7.87f);
                    Box(Surface.Rust, x, h-0.29f, z-3.85f,0.15f,1.47f,0.16f);
                    for (int k=0;k<8;k++)
                        if (Chance(0.72f)) Box(Surface.Moss,x-side*0.334f, Range(0,1.2f),z+Range(-3.8f,3.8f),0.025f,Range(0.09f,0.57f),Range(0.15f,1.2f));
                }
                Solid("Yard retaining wall", P(side*45,0.8f,3),P(0.66f,2,119),Quaternion.identity);
                Solid("Yard boundary", P(side*46.85f,1.25f,5),P(0.3f,3,124),Quaternion.identity);
            }
            UpdateDepartureColliders();
            Solid("South yard boundary",P(0,1.25f,-56.9f),P(94,3,0.3f),Quaternion.identity);
            foreach (int z in new[] {-18, 20})
            {
                float x = z < 0 ? 8.7f : -7.9f;
                Box(Surface.Black,x,1.34f,z,0.085f,3.1f,0.085f);
                Box(Surface.Yellow,x,2.77f,z,0.52f,0.82f,0.10f);
                Box(Surface.Black,x,2.77f,z-0.059f,0.42f,0.62f,0.024f);
                for (int j=-1;j<=1;j++) Box(j==-1?Surface.Red:Surface.Steel,x,2.77f+j*0.17f,z-0.082f,0.14f,0.13f,0.033f);
            }
            foreach (int x in new[] {-10,-8,8,10})
            {
                chunk.Box(Surface.DarkConcrete, P(x,0.34f,-18.5f), P(1.7f,1.05f,0.51f),Quaternion.Euler(0,Range(-18,18),Range(-8,8)));
                for (int stripe=-2;stripe<=2;stripe++)
                    Beam(stripe%2==0 ? Surface.Yellow : Surface.Black, P(x+stripe*0.24f,0.21f,-18.77f),P(x+stripe*0.24f+0.18f,0.61f,-18.77f),0.12f,0.012f);
            }
            foreach (float x in new[] {-7.5f, 7.5f})
            {
                Box(Surface.Black,x,1.29f,-5.5f,0.09f,2.9f,0.10f);
                Box(Surface.Teal,x,2.5f,-5.5f,2.05f,0.57f,0.13f);
                Lettering("BOARD", P(x,2.5f,-5.578f),0.053f,Surface.Paint);
                Lettering("BOARD", P(x,2.5f,-5.422f),-0.053f,Surface.Paint);
            }
        }

        private void UpdateDepartureColliders()
        {
            float sideWidth = 47f - DepartureHalfWidth;
            float sideCenter = DepartureHalfWidth + sideWidth * 0.5f;
            SetDepartureBox("North yard boundary", P(-sideCenter, 1.25f, YardNorth), P(sideWidth, 3, 0.3f));
            SetDepartureBox("North yard boundary east", P(sideCenter, 1.25f, YardNorth), P(sideWidth, 3, 0.3f));
            float length = DepartureEnd - YardNorth;
            SetDepartureBox("Departure grade", P(0, Floor - 0.27f, YardNorth + length * 0.5f),
                P(2 * DepartureHalfWidth, 0.44f, length));
            SetDepartureBox("Far end of departure grade", P(0, 1.25f, DepartureEnd),
                P(2 * DepartureHalfWidth, 3f, 0.3f));
        }

        private void SetDepartureBox(string name, Vector3 center, Vector3 size)
        {
            Transform marker = collision.Find(name);
            if (marker == null)
            {
                Solid(name, center, size, Quaternion.identity);
                return;
            }
            marker.localPosition = center;
            marker.GetComponent<BoxCollider>().size = size;
        }

        private void BoardingWreckage()
        {
            foreach (Vector3 basePoint in new[] {P(8.7f,Floor,8.4f), P(-9.2f,Floor,6.6f), P(10.7f,Floor,-13f), P(-12.2f,Floor,-14.2f)})
            {
                float x=basePoint.x, z=basePoint.z;
                Patch(Surface.Soil,x,z,2.9f,1.35f,Floor+0.019f);
                for (int i=0;i<24;i++)
                {
                    float px=x+Range(-2,2), pz=z+Range(-0.83f,0.84f);
                    var rotation=Quaternion.Euler(Range(-28,28),Range(0,360),Range(-22,22));
                    chunk.Box(Chance(0.7f)?Surface.DarkConcrete:Surface.Rust,P(px,Floor+Range(0.11f,0.24f),pz),
                        P(Range(0.12f,0.47f),Range(0.12f,0.23f),Range(0.16f,0.56f)),rotation);
                }
                for (int i=0;i<5;i++)
                {
                    float px=x+(i-2)*0.39f;
                    Beam(Surface.Wood,P(px,Floor+0.2f,z-0.62f),P(px+Range(-0.28f,0.23f),Floor+Range(0.25f,0.55f),z+Range(0.32f,0.85f)),0.11f,0.065f);
                    Beam(Surface.Rust,P(px,Floor+0.12f,z+0.51f),P(px+0.17f,Floor+Range(0.31f,0.66f),z+0.69f),0.039f,0.041f);
                }
                for (int i=0;i<9;i++)
                {
                    float px=x+Range(-2.3f,2.3f), pz=z+Range(-1.2f,1.2f);
                    Vector3 g=P(px,Floor+0.03f,pz);
                    for (int j=-1;j<=1;j++)
                    {
                        Vector3 a=g+P(-0.05f,0,0),b=g+P(0.05f,0,0),c=g+P(j*0.12f,Range(0.27f,0.63f),Range(-0.16f,0.19f));
                        chunk.Face(Surface.Straw,a,b,c); chunk.Face(Surface.Straw,b,a,c);
                    }
                }
            }
            Beam(Surface.Rust,P(12.75f,Floor+1.78f,7.4f),P(13.1f,Floor+0.26f,12.6f),0.17f,0.17f);
            Beam(Surface.Steel,P(12.5f,Floor+0.24f,8.6f),P(14.85f,Floor+0.53f,10.1f),0.09f,0.09f);
        }

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A']=new[]{"01110","10001","10001","11111","10001","10001","10001"},
            ['B']=new[]{"11110","10001","10001","11110","10001","10001","11110"},
            ['D']=new[]{"11110","10001","10001","10001","10001","10001","11110"},
            ['E']=new[]{"11111","10000","10000","11110","10000","10000","11111"},
            ['O']=new[]{"01110","10001","10001","10001","10001","10001","01110"},
            ['P']=new[]{"11110","10001","10001","11110","10000","10000","10000"},
            ['R']=new[]{"11110","10001","10001","11110","10100","10010","10001"},
            ['T']=new[]{"11111","00100","00100","00100","00100","00100","00100"},
            ['0']=new[]{"01110","10001","10011","10101","11001","10001","01110"},
            ['1']=new[]{"00100","01100","00100","00100","00100","00100","01110"},
            ['2']=new[]{"01110","10001","00001","00010","00100","01000","11111"}
        };

        private void Lettering(string text, Vector3 center, float pixel, Surface surface)
        {
            for (int i=0;i<text.Length;i++)
            {
                if (!Glyphs.TryGetValue(text[i],out var rows)) continue;
                for (int y=0;y<7;y++)
                for (int x=0;x<5;x++)
                {
                    if (rows[y][x] != '1') continue;
                    float px=center.x+(i*6+x-(text.Length*6-2)*0.5f)*pixel;
                    float py=center.y+(3-y)*Mathf.Abs(pixel);
                    Box(surface,px,py,center.z,Mathf.Abs(pixel),Mathf.Abs(pixel),0.012f);
                }
            }
        }

        private void Debris()
        {
            for (int i=0;i<1830;i++)
            {
                float x=Range(-44,44), z=Range(-54,64);
                if (Mathf.Abs(x) < 6 || (Mathf.Abs(x) < 13 && Mathf.Abs(z) < 13)) continue;
                float h=Range(0.10f, 0.66f);
                if (Chance(0.84f))
                {
                    for (int j=0;j<5;j++)
                    {
                        float angle=Range(0,Mathf.PI*2), spread=Range(0.05f,0.29f);
                        Vector3 delta=P(Mathf.Cos(angle)*spread, h*Range(0.45f,1),Mathf.Sin(angle)*spread);
                        Vector3 basePoint=P(x+Range(-0.09f,0.09f),Floor+0.037f,z+Range(-0.09f,0.09f));
                        Vector3 tangent=P(-Mathf.Sin(angle)*0.048f,0,Mathf.Cos(angle)*0.048f);
                        Surface surface=Chance(0.29f)?Surface.Straw:Chance(0.38f)?Surface.Grass:Surface.Moss;
                        chunk.Face(surface,basePoint-tangent,basePoint+tangent,basePoint+delta);
                        chunk.Face(surface,basePoint+tangent,basePoint-tangent,basePoint+delta);
                    }
                }
                else
                    chunk.Box(Chance(0.68f)?Surface.DarkConcrete:Surface.Rust,P(x,Floor+0.10f,z),
                        P(Range(0.12f,0.66f),Range(0.12f,0.28f),Range(0.15f,0.75f)),Quaternion.Euler(Range(-24,24),Range(0,360),Range(-20,20)));
            }
            for (int i=0;i<45;i++)
            {
                int side=i%2==0?1:-1;
                float x=side*Range(16,41),z=Range(23,38);
                var tilt=Quaternion.Euler(Range(-16,32),Range(0,360),Range(-24,24));
                chunk.Box(i%3==0?Surface.FadedTeal:Surface.Rust,P(x,Floor+Range(0.04f,0.40f),z),
                    P(Range(0.35f,1.7f),0.04f,Range(0.6f,3.2f)),tilt);
            }
        }

        private void Skyline()
        {
            const float foundation = -0.37f;
            const float skylineOpeningHalfWidth = 24f;
            var cityRandom = new System.Random(9214);
            float CityRange(float min, float max) => Mathf.Lerp(min, max, (float)cityRandom.NextDouble());
            Box(Surface.Haze, 0, -0.62f, 20, 250, 0.52f, 220);
            float gradeLength = DepartureEnd - 67f;
            Box(Surface.Asphalt, 0, Floor - 0.22f, 67f + gradeLength * 0.5f, 2 * DepartureHalfWidth, 0.44f, gradeLength);
            foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 9; i++)
            {
                float x = side * (skylineOpeningHalfWidth + (i + 0.5f) * 10.3f);
                float z = 69f + 6f * Mathf.Sin(x / 31f);
                Box(Surface.DarkConcrete,x,4.96f,z,10.35f,1.02f,5.8f);
                Box(Surface.Steel,x,5.59f,z-2.77f,10.4f,0.55f,0.17f);
                if (i%2==0) Box(Surface.Concrete,x,2.18f,z,1.42f,5.45f,2.7f);
            }
            for (int i=0;i<42;i++)
            {
                float x = i < 22 ? -100 + i * 9.4f : (i % 2 == 0 ? 1 : -1) * CityRange(51, 82);
                float z = i < 22 ? CityRange(80, 110) : CityRange(-54, 67);
                int floors = cityRandom.Next(3, 13);
                int bays = cityRandom.Next(3, 7);
                float width = bays * 1.58f;
                float depth = CityRange(4, 10);
                if (Mathf.Abs(x) - width * 0.5f < DepartureHalfWidth + 2f) continue;
                bool hasRoofShed = CityRange(0, 1) < 0.66f;
                if (Mathf.Abs(x) - width * 0.5f - 0.12f < skylineOpeningHalfWidth) continue;
                float top = foundation + floors * 2.45f;
                Surface stone = i % 3 == 0 ? Surface.Concrete : Surface.DarkConcrete;
                Vector3 frontLeft = P(x - width / 2, foundation, z - depth / 2);
                Vector3 frontRight = frontLeft + Vector3.right * width;
                Vector3 rearRight = frontRight + Vector3.forward * depth;
                Vector3 rearLeft = frontLeft + Vector3.forward * depth;
                int sideBays = Mathf.Max(2, Mathf.RoundToInt(depth / 1.65f));
                Facade(stone, frontLeft, frontRight, floors, bays);
                Facade(stone, frontRight, rearRight, floors, x < 0 ? sideBays : 0);
                Facade(stone, rearRight, rearLeft, floors, 0);
                Facade(stone, rearLeft, frontLeft, floors, x > 0 ? sideBays : 0);
                Box(Surface.DarkConcrete, x, top - 0.02f, z, width + 0.24f, 0.24f, depth + 0.24f);
                if (hasRoofShed) Box(stone, x + 0.68f, top + 0.79f, z, 2.25f, 1.6f, 2.7f);
            }
        }

        private void Facade(Surface stone, Vector3 start, Vector3 end, int floors, int bays)
        {
            const float floorHeight = 2.45f;
            Vector3 span = end - start;
            void Face(Surface surface, float left, float right, float bottom, float top)
            {
                Vector3 lowerLeft = start + span * left + Vector3.up * bottom;
                Vector3 lowerRight = start + span * right + Vector3.up * bottom;
                Vector3 rise = Vector3.up * (top - bottom);
                chunk.Face(surface, lowerLeft, lowerLeft + rise, lowerRight + rise, lowerRight);
            }

            if (bays == 0)
            {
                Face(stone, 0, 1, 0, floors * floorHeight);
                return;
            }

            for (int floor = 0; floor < floors; floor++)
            {
                float bottom = floor * floorHeight;
                float sill = bottom + 0.83f;
                float lintel = bottom + 1.67f;
                Face(stone, 0, 1, bottom, sill);
                Face(stone, 0, 1, lintel, bottom + floorHeight);
                for (int bay = 0; bay < bays; bay++)
                {
                    float left = (bay + 0.20f) / bays;
                    float right = (bay + 0.80f) / bays;
                    Face(stone, (float)bay / bays, left, sill, lintel);
                    Face((floor + bay) % 6 == 0 ? Surface.Glass : Surface.Black, left, right, sill, lintel);
                    Face(stone, right, (float)(bay + 1) / bays, sill, lintel);
                }
            }
        }
    }

    private sealed class Chunk
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly SortedDictionary<int, List<int>> triangles = new SortedDictionary<int, List<int>>();

        public void Face(Surface material, Vector3 a, Vector3 b, Vector3 c)
        {
            int index = vertices.Count;
            Vector3 ab = b - a, ac = c - a;
            Vector3 u = ab.normalized;
            Vector3 v = Vector3.Cross(Vector3.Cross(ab, ac).normalized, u);
            Vector2 Project(Vector3 point) => new Vector2(Vector3.Dot(point,u),Vector3.Dot(point,v)) * 0.29f;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uv.Add(Project(a)); uv.Add(Project(b)); uv.Add(Project(c));
            if (!triangles.TryGetValue((int)material,out var indices))
            {
                indices = new List<int>();
                triangles.Add((int)material, indices);
            }
            indices.Add(index); indices.Add(index+1); indices.Add(index+2);
        }

        public void Face(Surface material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int index = vertices.Count;
            Face(material, a, b, c);
            Vector3 u = (b-a).normalized, v = Vector3.Cross(Vector3.Cross(b-a,c-a).normalized,u);
            vertices.Add(d);
            uv.Add(new Vector2(Vector3.Dot(d,u),Vector3.Dot(d,v))*0.29f);
            var indices = triangles[(int)material];
            indices.Add(index); indices.Add(index+2); indices.Add(index+3);
        }

        public void Box(Surface material, Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;
            Vector3 At(float x,float y,float z) => center + rotation * new Vector3(h.x*x,h.y*y,h.z*z);
            Vector3 a=At(-1,-1,-1), b=At(1,-1,-1), c=At(1,1,-1), d=At(-1,1,-1);
            Vector3 e=At(-1,-1,1), f=At(1,-1,1), g=At(1,1,1), h1=At(-1,1,1);
            Face(material,a,d,c,b); Face(material,e,f,g,h1); Face(material,a,e,h1,d);
            Face(material,b,c,g,f); Face(material,d,h1,g,c); Face(material,a,b,f,e);
        }

        public Mesh ToMesh(string name, out int[] surfaces, Mesh mesh = null)
        {
            mesh ??= new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0,uv);
            mesh.subMeshCount = triangles.Count;
            surfaces = triangles.Keys.ToArray();
            for (int i = 0; i < surfaces.Length; i++) mesh.SetTriangles(triangles[surfaces[i]],i,false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
