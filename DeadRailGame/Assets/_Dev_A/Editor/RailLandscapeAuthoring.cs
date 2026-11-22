using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RailLandscapeAuthoring
{
    private const string ScenePath = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";
    private const string Folder = "Assets/_Dev_A/Prefabs/RouteLandscape";
    private const float Grade = -0.20f;
    private static readonly string[] SideNames =
    {
        "01 Scrub and weathered housing", "02 Fir foothills", "03 Industrial ruins",
        "04 Exposed rock cutting", "05 Abandoned outskirts", "06 Woodland edge",
        "07 Closed rail works", "08 Broken hill slopes"
    };

    [MenuItem("DeadRail/Route/Apply trackside landscape")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open Dev_A_Scene in Edit Mode.");

        RailRouteLoop route = UnityEngine.Object.FindFirstObjectByType<RailRouteLoop>();
        if (route == null || route.junctionGeometry == null) throw new InvalidOperationException("The authored route is required.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Dev_A/Prefabs", "RouteLandscape");

        var materials = AssetDatabase.LoadAllAssetsAtPath("Assets/_Dev_A/StartingRailYard/Weathering.asset")
            .OfType<Material>().ToDictionary(material => material.name);
        materials.Add("Night signal red", NightSignalMaterial());
        Transform example = route.junctionGeometry.Find("Example geometry");
        Vector3[] left = ReadRoute(example, route.junctionGeometry, "Left", new[] { "DeadRail_TrackCurve_Left_30deg", "DeadRail_Track_8m (4)", "DeadRail_Track_8m (5)", "DeadRail_TrackCurve_Right_60deg", "DeadRail_Track_8m (8)", "DeadRail_Track_8m (9)", "DeadRail_Track_8m (10)" });
        Vector3[] right = ReadRoute(example, route.junctionGeometry, "Right", new[] { "DeadRail_TrackCurve_Right_30deg", "DeadRail_Track_8m (6)", "DeadRail_Track_8m (7)", "DeadRail_TrackCurve_Left_60deg", "DeadRail_Track_8m (9)", "DeadRail_Track_8m (11)" });
        Vector3[] straight = ReadRoute(example, route.junctionGeometry, "Straight", new[] { "DeadRail_Track_8m", "DeadRail_Track_8m (1)", "DeadRail_Track_8m (2)", "DeadRail_Track_8m (3)", "DeadRail_Track_8m (14)", "DeadRail_Track_8m (15)" });
        MeshRenderer railRenderer = example.Find("DeadRail_Track_8m (15)").GetComponentInChildren<MeshRenderer>();
        Matrix4x4 profileToJunction = route.junctionGeometry.worldToLocalMatrix * railRenderer.localToWorldMatrix;
        Vector3 profileFront = profileToJunction.inverse.MultiplyPoint3x4(straight[^1]);
        Vector3 profileBack = profileToJunction.inverse.MultiplyPoint3x4(straight[^1] - Vector3.forward * 8f);
        Vector3 leftHeading = AlignToRailMesh(left, "DeadRail_Track_8m (10)");
        Vector3 rightHeading = AlignToRailMesh(right, "DeadRail_Track_8m (11)");
        var art = new LandscapeArt(materials);
        GameObject sides = LoadOrBuild("TracksideScenery", art.BuildSides);
        Vector3[] leftExtension = ExtendBypass(left[^1], leftHeading, -1);
        Vector3[] rightExtension = ExtendBypass(right[^1], rightHeading, 1);
        Vector3[] straightExtension = null;
        float tunnelExit = 0f;
        GameObject pass = LoadOrBuild("MountainJunction", root =>
        {
            tunnelExit = art.BuildJunction(root, route.junctionGeometry, left.Concat(leftExtension).Concat(right).Concat(rightExtension).ToArray());
            straightExtension = Enumerable.Range(0, Mathf.CeilToInt(tunnelExit + 12f - straight[^1].z) + 1)
                .Select(step => straight[^1] + Vector3.forward * step).ToArray();
            art.BuildTrackExtensions(root.transform, railRenderer, profileToJunction, straight[^1],
                new[] { leftHeading, rightHeading, Vector3.forward }, leftExtension, rightExtension, straightExtension);
        });
        Material dampSteel = art.DampSteel(railRenderer.sharedMaterials);
        foreach (Transform railGroup in new[] { example, GameObject.Find("Track_Group")?.transform, route.referenceLayout?.transform, route.farmingGeometry })
            if (railGroup != null) ApplyRailFinish(railGroup, dampSteel);

        GameObject sidesInstance = Place(sides, scene, null);
        Place(pass, scene, route.junctionGeometry);
        var routeSettings = new SerializedObject(route);
        routeSettings.FindProperty("sideScenery").objectReferenceValue = sidesInstance.GetComponent<RailSideScenery>();
        routeSettings.ApplyModifiedProperties();

        Undo.RecordObject(route, "Extend junction routes");
        route.leftPath = left.Concat(leftExtension.Skip(1).TakeWhile(point => point.z < leftExtension[^1].z - 12f)).ToArray();
        route.rightPath = right.Concat(rightExtension.Skip(1).TakeWhile(point => point.z < rightExtension[^1].z - 12f)).ToArray();
        route.straightPath = straight.Concat(straightExtension.Skip(1).TakeWhile(point => point.z < tunnelExit - 14f)).ToArray();
        Vector3[] ends = { route.leftPath[^1], route.straightPath[^1], route.rightPath[^1] };
        for (int i = 0; i < ends.Length; i++)
        {
            Undo.RecordObject(route.cutMarkers[i], "Move junction transition marker");
            route.cutMarkers[i].localPosition = ends[i];
        }

        StartingRailYardAuthoring.ApplyNightLighting(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the route landscape.");
        Debug.Log("[Rail landscape] Eight roadside sections and the mountain pass are linked to Dev_A_Scene.");

        Vector3 AlignToRailMesh(Vector3[] path, string model)
        {
            var renderer = example.Find(model).GetComponentInChildren<MeshRenderer>();
            Matrix4x4 matrix = route.junctionGeometry.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector3 front = matrix.MultiplyPoint3x4(profileFront);
            Vector3 back = matrix.MultiplyPoint3x4(profileBack);
            if (front.z < back.z) (front, back) = (back, front);
            Vector3 original = path[^1];
            Vector3 delta = Vector3.ProjectOnPlane(front - original, Vector3.up);
            for (int i = path.Length - 1; i >= 0 && original.z - path[i].z < 8f; i--)
                path[i] += delta * Mathf.InverseLerp(8f, 0f, original.z - path[i].z);
            return Vector3.ProjectOnPlane(front - back, Vector3.up).normalized;
        }
    }

    internal static Material NightSignalMaterial()
    {
        const string path = Folder + "/NightSignalRed.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Night signal red" };
        material.SetColor("_BaseColor", new Color(.72f, .035f, .018f));
        material.SetColor("_EmissionColor", new Color(2.65f, .035f, .009f));
        material.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ApplyRailFinish(Transform group, Material dampSteel)
    {
        foreach (MeshRenderer renderer in group.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.name.StartsWith("Track", StringComparison.Ordinal)) continue;
            Material[] slots = renderer.sharedMaterials;
            int index = Array.FindIndex(slots, material => material != null && material.name == "DeadRail_LowPoly_Steel");
            if (index < 0) continue;
            slots[index] = dampSteel;
            Undo.RecordObject(renderer, "Apply damp rail finish");
            renderer.sharedMaterials = slots;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }

    private static Vector3[] ReadRoute(Transform example, Transform junction, string branch, string[] segmentNames)
    {
        var points = new List<Vector3>();
        Append(example.Find("DeadRail_Track_ThreeWay"), $"_{branch}_Waypoint_");
        foreach (string name in segmentNames)
        {
            Transform segment = example.Cast<Transform>().First(child => child.name == name &&
                (name != "DeadRail_Track_8m (9)" || (junction.InverseTransformPoint(child.position).x > 0f) == (branch == "Right")));
            Append(segment, "_Main_Waypoint_");
        }
        return points.ToArray();

        void Append(Transform segment, string token)
        {
            Vector3[] markers = segment.GetComponentsInChildren<Transform>(true)
                .Where(marker => marker.name.Contains(token, StringComparison.Ordinal))
                .OrderBy(marker => marker.name, StringComparer.Ordinal)
                .Select(marker => junction.InverseTransformPoint(marker.position)).ToArray();
            if (markers.Length < 2) throw new InvalidOperationException($"Missing rail markers in {segment.name}.");
            if (points.Count > 0 && (points[^1] - markers[^1]).sqrMagnitude < (points[^1] - markers[0]).sqrMagnitude)
                Array.Reverse(markers);
            foreach (Vector3 marker in markers)
            {
                Vector3 position = new Vector3(marker.x, 0f, marker.z);
                if (points.Count == 0 || (points[^1] - position).sqrMagnitude > .0001f) points.Add(position);
            }
        }
    }

    private static Vector3[] ExtendBypass(Vector3 start, Vector3 heading, int side)
    {
        Vector3 bend = new Vector3(side * 38f, 0f, 109f);
        Vector3 end = new Vector3(side * (side < 0 ? 58f : 63f), 0f, side < 0 ? 156f : 151f);
        Vector3 midTangent = new Vector3(side * .22f, 0f, 1f).normalized * 13f;
        Vector3 endTangent = new Vector3(side * .85f, 0f, 1f).normalized * 17f;
        var points = new List<Vector3> { start };
        AppendCurve(start, start + heading * 13f, bend - midTangent, bend, 40);
        AppendCurve(bend, bend + midTangent, end - endTangent, end, 52);
        return points.ToArray();

        void AppendCurve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int count)
        {
            for (int i = 1; i <= count; i++)
            {
                float t = (float)i / count, s = 1f - t;
                points.Add(s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d);
            }
        }
    }

    private static GameObject LoadOrBuild(string name, Action<GameObject> build)
    {
        string path = Folder + "/" + name + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject root = existing == null ? new GameObject(name) : PrefabUtility.LoadPrefabContents(path);
        try
        {
            build(root);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefab == null) throw new InvalidOperationException("Could not save " + path);
            AssetDatabase.SaveAssets();
            return prefab;
        }
        finally
        {
            if (existing == null) UnityEngine.Object.DestroyImmediate(root);
            else PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject Place(GameObject asset, Scene scene, Transform parent)
    {
        GameObject existing = parent != null ? parent.Find(asset.name)?.gameObject
            : scene.GetRootGameObjects().FirstOrDefault(root => root.name == asset.name);
        if (existing != null) return existing;

        var instance = parent != null ? (GameObject)PrefabUtility.InstantiatePrefab(asset, parent)
            : (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Apply " + asset.name);
        return instance;
    }

    private sealed class LandscapeArt
    {
        private readonly Dictionary<string, Material> materials;
        private readonly System.Random random = new(43927);
        private const string Ground = "Soil";
        private const string Green = "Moss";
        private const string Scrub = "Grass";
        private const string Stone = "Gravel";
        private const string Exposed = "GravelLight";
        private const string Concrete = "DarkConcrete";
        private const string Timber = "Wood";
        private const string Steel = "Steel";
        private const string Rust = "Rust";
        private const string Dark = "Black";
        private string meshAsset;

        public LandscapeArt(Dictionary<string, Material> materials) => this.materials = materials;

        public void BuildSides(GameObject root)
        {
            if (root.GetComponent<RailSideScenery>() == null) root.AddComponent<RailSideScenery>();
            meshAsset = Folder + "/TracksideMeshes.asset";
            for (int index = 0; index < SideNames.Length; index++)
            {
                Transform tile = Child(root.transform, SideNames[index]);
                tile.localPosition = Vector3.forward * (RailSideScenery.StartDistance + RailSideScenery.TileLength * index);
                // Keep the authored junction clear in Scene view. Play Mode positions every tile by distance.
                tile.gameObject.SetActive(index < 2);
                var ground = new SurfaceMesh();
                var details = new SurfaceMesh();
                foreach (int side in new[] { -1, 1 })
                {
                    bool builtUp = side < 0 ? index is 0 or 2 or 4 or 6 : index is 2 or 3 or 5 or 7;
                    BuildRoadside(ground, details, index, side, builtUp);
                }
                AddGantry(details, 17f);
                AddGantry(details, 47f);
                AddBeam(details, Steel, new Vector3(0f, 5.75f, 0f), new Vector3(0f, 5.75f, RailSideScenery.TileLength), .033f);
                AddBeam(details, Rust, new Vector3(.4f, 7.1f, 0f), new Vector3(.4f, 7.1f, RailSideScenery.TileLength), .027f);
                AddSignal(tile, details, index % 2 == 0 ? 3.6f : -3.6f, 37f);
                SaveMesh(tile, "Embankments " + (index + 1), ground, true);
                SaveMesh(tile, "Ruins and vegetation " + (index + 1), details, false);
            }
        }

        private void BuildRoadside(SurfaceMesh ground, SurfaceMesh details, int index, int side, bool builtUp)
        {
            const float length = RailSideScenery.TileLength;
            float Height(float offset, float z)
            {
                float envelope = Mathf.Sin(Mathf.Clamp01(z / length) * Mathf.PI);
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7.8f, 40f, offset));
                float n = Mathf.PerlinNoise(offset * .11f + index * 9.7f, z * .065f + side * 1.72f);
                float hill = builtUp ? 2.4f : 12f + index * .7f;
                float mound = Mathf.Exp(-Mathf.Pow((offset - 39f) / 20f, 2f));
                return Grade + rise * envelope * (n * hill * mound + rise * 1.9f);
            }

            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 13; x++)
            {
                float a = 7.8f + x * 4.8f, b = a + 4.8f;
                float c = z * 4f, d = c + 4f;
                string surface = x == 0 ? Stone : (x + z * 7 + index) % 9 == 0 ? Exposed
                    : x > 1 && (x * 3 + z) % 6 != 0 ? Green : Ground;
                Vector3 p0 = V(a, c), p1 = V(a, d), p2 = V(b, d), p3 = V(b, c);
                if (side == 1) ground.Quad(surface, p0, p1, p2, p3);
                else ground.Quad(surface, p3, p2, p1, p0);
            }

            if (builtUp)
            {
                for (int i = 0; i < 4; i++)
                {
                    float z = 9f + i * 15f;
                    float x = Range(19f, 34f);
                    float y = Height(x, z);
                    Ruin(details, new Vector3(side * x, y - .14f, z), Range(5f, 9f), Range(5f, 9.4f), i % 3 + 2, side, index == 2 || index == 6);
                    details.Box(Concrete, new Vector3(side * 12f, Height(12, z) + .6f, z), new Vector3(.55f, 1.35f, Range(4, 8)), Quaternion.Euler(0, side * Range(-6, 6), 0));
                }
            }
            else
            {
                for (int i = 0; i < 12; i++)
                {
                    float x = Range(14, 62), z = Range(3.8f, 60);
                    Rock(details, new Vector3(side * x, Height(x, z) - .2f, z), new Vector3(Range(2, 5), Range(1, 4), Range(2, 4.5f)), i % 3 == 0);
                }
            }

            for (int i = 0; i < (builtUp ? 19 : 39); i++)
            {
                float x = Range(builtUp ? 34 : 12, 64);
                float z = Range(2.2f, 61.8f);
                Pine(details, new Vector3(side * x, Height(x, z) - .1f, z), Range(2.1f, builtUp ? 6.4f : 9f), i % 4 == 0);
            }
            for (int i = 0; i < 32; i++)
            {
                float x = Range(8.4f, 19.8f), z = Range(.5f, 63.5f);
                Tuft(details, new Vector3(side * x, Height(x, z), z), Range(.24f, .82f));
            }

            Vector3 V(float x, float z) => new Vector3(x * side, Height(x, z), z);
        }

        private void Ruin(SurfaceMesh mesh, Vector3 center, float width, float depth, int floors, int trackSide, bool industrial)
        {
            float height = floors * 2.65f;
            mesh.Box(Concrete, center + Vector3.up * (height * .5f), new Vector3(width, height, depth));
            mesh.Box(Concrete, center + Vector3.up * height, new Vector3(width + .55f, .34f, depth + .55f));
            mesh.Box(industrial ? Rust : "FadedTeal", center + new Vector3(0, height - .68f, 0), new Vector3(width + .05f, .24f, depth + .05f));
            mesh.Box(Stone, center + new Vector3(width * .22f, height + .56f, depth * -.18f), new Vector3(2f, 1.03f, 2.35f));
            for (int level = 0; level < floors; level++)
            {
                float y = level * 2.65f + 1.60f;
                for (int bay = 0; bay < 3; bay++)
                {
                    float windowZ = depth * (bay - 1) * .28f;
                    float windowX = width * (bay - 1) * .28f;
                    bool missing = (bay + level * 2 + floors) % 4 == 0;
                    string glass = missing ? Dark : "Glass";
                    mesh.Box(glass, center + new Vector3(-trackSide * (width * .5f + .018f), y, windowZ), new Vector3(.045f, .94f, depth * .17f));
                    mesh.Box(glass, center + new Vector3(windowX, y, -depth * .5f - .018f), new Vector3(width * .16f, .94f, .045f));
                    if (missing)
                        mesh.Box(Timber, center + new Vector3(-trackSide * (width * .5f + .048f), y - .12f, windowZ), new Vector3(.042f, .19f, depth * .18f), Quaternion.Euler(13, 0, 0));
                }
            }
            mesh.Box(Dark, center + new Vector3(0, .9f, -depth * .5f - .031f), new Vector3(1.35f, 1.85f, .066f));
            if (industrial)
                mesh.Box(Rust, center + new Vector3(width * .29f, height + 2.5f, depth * .36f), new Vector3(.87f, 5.1f, .87f));
        }

        public float BuildJunction(GameObject root, Transform junction, Vector3[] bypass)
        {
            meshAsset = Folder + "/JunctionMeshes.asset";
            Transform example = junction.Find("Example geometry");
            Transform tunnel = example.Find("DeadRail_Blackout_Tunnel");
            Renderer[] portals = tunnel.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Stone_Portal_Arch", StringComparison.Ordinal)).ToArray();
            if (portals.Length < 2) throw new InvalidOperationException("Both tunnel portals are required.");
            Bounds nearPortal = portals.Select(renderer => BoundsIn(junction, renderer)).OrderBy(bounds => bounds.center.z).First();
            Bounds farPortal = portals.Select(renderer => BoundsIn(junction, renderer)).OrderByDescending(bounds => bounds.center.z).First();
            float nearZ = nearPortal.center.z + .24f;
            float moduleLength = farPortal.center.z - nearPortal.center.z;
            const int AdditionalModules = 3;
            float farZ = farPortal.center.z + moduleLength * AdditionalModules - .24f;
            float halfAperture = nearPortal.extents.x - .34f;
            float crown = nearPortal.max.y - .27f;
            float spring = crown * .55f;

            Vector3[] trackMarkers = example.GetComponentsInChildren<Transform>()
                .Where(t => t.name.Contains("Waypoint", StringComparison.Ordinal))
                .Select(t => junction.InverseTransformPoint(t.position))
                .Concat(bypass).ToArray();
            Vector3[] railMarkers = bypass
                .Where(p => Mathf.Abs(p.x) > 8f && p.z > nearZ - 12f).ToArray();

            var gravel = new SurfaceMesh();
            for (int z = 0; z < 60; z++)
            for (int x = 0; x < 44; x++)
            {
                float a = -110f + x * 5f, b = a + 5f, c = -110f + z * 5f, d = c + 5f;
                string surface = Clearance(a + 2.5f, c + 2.5f) < 3f ? Stone : (x * 3 + z * 5) % 8 < 2 ? Ground : Green;
                gravel.Quad(surface, Terrain(a, c), Terrain(a, d), Terrain(b, d), Terrain(b, c));
            }

            var ridge = new SurfaceMesh();
            const int Rows = 40;
            const int Columns = 24;
            for (int row = 0; row < Rows; row++)
            {
                float a = Mathf.Lerp(nearZ, farZ, (float)row / Rows);
                float b = Mathf.Lerp(nearZ, farZ, (float)(row + 1) / Rows);
                for (int column = 0; column < Columns; column++)
                {
                    float u = (float)column / Columns * 2f - 1f, v = (float)(column + 1) / Columns * 2f - 1f;
                    Vector3 p0 = Mountain(u, a), p1 = Mountain(u, b), p2 = Mountain(v, b), p3 = Mountain(v, a);
                    string surface = column < 3 || column > Columns - 4 || (row + column * 7) % 13 < 3 ? Stone
                        : (row * 3 + column * 5) % 15 < 7 ? Green : Ground;
                    ridge.Triangle(surface, p0, p1, p2);
                    ridge.Triangle((row + column) % 13 == 0 ? Exposed : surface, p0, p2, p3);
                }
            }

            AddPortalFace(nearZ, false);
            AddPortalFace(farZ, true);

            var surrounds = new SurfaceMesh();
            AddGantry(surrounds, -19f);
            AddSignal(root.transform, surrounds, -3.6f, 10f);
            AddSignal(root.transform, surrounds, 3.8f, 34f);
            for (int i = 0; i < 240; i++)
            {
                float z = Range(nearZ + .9f, farZ - .7f);
                float u = Range(-.97f, .97f);
                Vector3 point = Mountain(u, z);
                if (i % 9 == 0)
                    Rock(surrounds, point - Vector3.up * .2f, new Vector3(Range(.8f, 2.7f), Range(.7f, 2.5f), Range(1f, 2.5f)), i % 2 == 0);
                else
                    Pine(surrounds, point - Vector3.up * .25f, Range(1.1f, 4.9f), i % 4 == 0);
            }

            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < 104; i++)
                {
                    float z = Range(-31, 181);
                    float x = side * Range(17, 101);
                    if (Clearance(x, z) < 6.5f || (z >= nearZ - 3f && z <= farZ + 3f && Mathf.Abs(x) < Width(side, z) + 3f))
                        continue;
                    Vector3 point = Terrain(x, z);
                    if (i % 8 == 0)
                        Rock(surrounds, point, new Vector3(Range(.8f, 2.5f), Range(.6f, 1.9f), Range(1f, 2.6f)), i % 2 == 0);
                    else Pine(surrounds, point - Vector3.up * .16f, Range(2.1f, 6.3f), i % 3 == 0);
                }
                for (int i = 0; i < 8; i++)
                {
                    float x = side * (halfAperture + 1.3f + i * .57f);
                    float z = nearZ - Range(.85f, 2.45f);
                    Rock(surrounds, new Vector3(x, Grade, z), new Vector3(Range(.7f, 1.45f), Range(.65f, 1.8f), Range(1.2f, 2.75f)), i % 4 == 0);
                }
                for (int i = 0; i < 9; i++)
                {
                    float x = side * (halfAperture + 1f + .58f * i);
                    for (int course = 0; course < 2; course++)
                    {
                        if (i >= 7 && course == 1) continue;
                        surrounds.Box(course == 0 ? Concrete : Stone, new Vector3(x, Grade + .26f + course * .45f, nearZ - 1.8f),
                            new Vector3(.57f, .43f, 1.0f + .1f * i), Quaternion.Euler(0, side * -5f, 0));
                    }
                }
            }

            SaveMesh(root.transform, "Continuous junction grade", gravel, true);
            SaveMesh(root.transform, "Rock layers around tunnel", ridge, true);
            SaveMesh(root.transform, "Portal retaining walls and forest", surrounds, false);
            BuildTunnelModules(root.transform, junction, tunnel, moduleLength, AdditionalModules, nearZ, farZ, halfAperture, crown);
            Debug.Log($"[Rail landscape] Tunnel faces {nearZ:F1}m to {farZ:F1}m, aperture half width {halfAperture:F1}m, branch marker count {railMarkers.Length}.");
            return farZ;

            Vector3 Terrain(float x, float z)
            {
                float shoulder = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 33f, Clearance(x, z)));
                float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-100f, -15f, z));
                float outer = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(19f, 47f, Mathf.Abs(x)));
                float rise = Mathf.PerlinNoise(x * .041f + 31f, z * .047f + 8f) * 16f + Mathf.PerlinNoise(x * .11f, z * .092f) * 3f;
                return new Vector3(x, Grade + shoulder * outer * approach * rise, z);
            }

            float Clearance(float x, float z)
            {
                float nearest = x * x;
                foreach (Vector3 marker in trackMarkers)
                {
                    float dx = marker.x - x, dz = marker.z - z;
                    nearest = Mathf.Min(nearest, dx * dx + dz * dz);
                }
                return Mathf.Sqrt(nearest);
            }

            float Width(int side, float z)
            {
                float depth = z - nearZ;
                float wanted = 14f + Mathf.SmoothStep(0f, 12f, Mathf.InverseLerp(0f, 22f, depth))
                    + Mathf.SmoothStep(0f, 18f, Mathf.InverseLerp(58f, 103f, depth));
                foreach (Vector3 marker in railMarkers)
                {
                    if (marker.x * side <= 0f || Mathf.Abs(marker.z - z) >= 6f) continue;
                    wanted = Mathf.Min(wanted, Mathf.Abs(marker.x) - 6.5f);
                }
                return Mathf.Max(halfAperture + 2.6f, wanted);
            }

            Vector3 Mountain(float u, float z)
            {
                float width = Width(u < 0f ? -1 : 1, z);
                float x = u * width;
                float along = Mathf.InverseLerp(nearZ, farZ, z);
                float profile = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(u * Mathf.PI / 2f)), .98f);
                float noise = Mathf.PerlinNoise(x * .19f + 27f, z * .16f);
                float ridgeRise = Mathf.Sin(along * Mathf.PI * .87f) * (23f + 5f * Mathf.Sin(u * 5.4f + along * 4f));
                float h = Grade + profile * (crown + 2.6f + ridgeRise + noise * 2.5f);
                return new Vector3(x, h, z);
            }

            void AddPortalFace(float z, bool rear)
            {
                float left = Width(-1, z), right = Width(1, z);
                var cuts = new SortedSet<float>();
                for (int i = 0; i <= Columns; i++)
                {
                    float u = i * 2f / Columns - 1f;
                    cuts.Add(u * (u < 0f ? left : right));
                }
                for (int i = 0; i <= 18; i++) cuts.Add(Mathf.Lerp(-halfAperture, halfAperture, i / 18f));
                cuts.Add(-halfAperture - .012f);
                cuts.Add(halfAperture + .012f);
                float[] points = cuts.ToArray();
                for (int i = 1; i < points.Length; i++)
                {
                    float a = points[i - 1], b = points[i];
                    Vector3 lowerA = BasePoint(a);
                    Vector3 lowerB = BasePoint(b);
                    Vector3 upperA = RimPoint(a);
                    Vector3 upperB = RimPoint(b);
                    string rock = i % 6 == 0 ? Exposed : i % 7 == 0 ? Ground : Stone;
                    if (rear) ridge.Quad(rock, lowerB, upperB, upperA, lowerA);
                    else ridge.Quad(rock, lowerA, upperA, upperB, lowerB);
                }

                Vector3 BasePoint(float x)
                {
                    float apron = Mathf.SmoothStep(0f, 4.5f, Mathf.InverseLerp(halfAperture, .94f * (x < 0 ? left : right), Mathf.Abs(x)));
                    return new Vector3(x, Bottom(x), z + (rear ? apron : -apron));
                }

                Vector3 RimPoint(float x)
                {
                    float u = x / (x < 0f ? left : right);
                    float index = (Mathf.Clamp(u, -1f, 1f) + 1f) * Columns / 2f;
                    int before = Mathf.Min(Columns - 1, Mathf.FloorToInt(index));
                    return Vector3.Lerp(Mountain(before * 2f / Columns - 1f, z),
                        Mountain((before + 1) * 2f / Columns - 1f, z), index - before);
                }

                float Bottom(float x)
                {
                    if (Mathf.Abs(x) > halfAperture) return Grade;
                    return spring + (crown - spring) * Mathf.Sqrt(Mathf.Max(0, 1f - (x / halfAperture) * (x / halfAperture)));
                }
            }
        }

        private static void AddGantry(SurfaceMesh mesh, float z)
        {
            foreach (int side in new[] { -1, 1 })
            {
                mesh.Box(Concrete, new Vector3(side * 5.7f, .12f, z), new Vector3(.74f, .63f, .72f));
                mesh.Box(Steel, new Vector3(side * 5.7f, 3.85f, z), new Vector3(.21f, 7.52f, .24f));
            }
            foreach (float height in new[] { 6.5f, 7.35f })
                AddBeam(mesh, Rust, new Vector3(-5.7f, height, z), new Vector3(5.7f, height, z), .112f);
            for (int part = 0; part < 10; part++)
                AddBeam(mesh, Steel, new Vector3(-5.7f + part * 1.14f, 6.5f, z), new Vector3(-4.56f + part * 1.14f, 7.35f, z), .065f);
            AddBeam(mesh, Steel, new Vector3(0f, 5.77f, z), new Vector3(.4f, 7.3f, z), .045f);
        }

        private static void AddBeam(SurfaceMesh mesh, string material, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 delta = b - a;
            mesh.Box(material, (a + b) * .5f, new Vector3(thickness, thickness, delta.magnitude),
                Quaternion.LookRotation(delta, Mathf.Abs(delta.normalized.y) > .95f ? Vector3.forward : Vector3.up));
        }

        private static void AddSignal(Transform parent, SurfaceMesh mesh, float x, float z)
        {
            mesh.Box(Concrete, new Vector3(x, .12f, z), new Vector3(.64f, .62f, .64f));
            mesh.Box(Steel, new Vector3(x, 1.48f, z), new Vector3(.15f, 3.05f, .15f));
            mesh.Box(Dark, new Vector3(x, 2.97f, z - .13f), new Vector3(.44f, .76f, .26f));
            mesh.Box("Night signal red", new Vector3(x, 3.11f, z - .268f), new Vector3(.176f, .174f, .04f));
            mesh.Box(Dark, new Vector3(x, 3.237f, z - .30f), new Vector3(.35f, .074f, .27f));
            Transform lamp = Child(parent, $"Signal spill {z:F0}");
            lamp.localPosition = new Vector3(x * .64f, 1.25f, z - 1.1f);
            if (!lamp.TryGetComponent(out Light light)) light = lamp.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .052f, .026f);
            light.range = 8.5f;
            light.intensity = 4.4f;
            light.shadows = LightShadows.None;
        }

        private void BuildTunnelModules(Transform parent, Transform junction, Transform tunnel, float length, int count,
            float entrance, float exit, float halfAperture, float crown)
        {
            Renderer[] structure = tunnel.GetComponentsInChildren<Renderer>(true).Where(renderer =>
                renderer.name.StartsWith("Stone_Portal_Arch", StringComparison.Ordinal) ||
                renderer.name.StartsWith("Tunnel_Vault", StringComparison.Ordinal) ||
                renderer.name.StartsWith("Tunnel_Sidewall", StringComparison.Ordinal) ||
                renderer.name.StartsWith("Tunnel_Rib", StringComparison.Ordinal) ||
                renderer.name.StartsWith("Tunnel_Service_Walkway", StringComparison.Ordinal)).ToArray();
            Renderer front = structure.Where(renderer => renderer.name.StartsWith("Stone_Portal_Arch", StringComparison.Ordinal))
                .OrderBy(renderer => BoundsIn(junction, renderer).center.z).First();
            var modules = Child(parent, "Joined tunnel modules");
            for (int i = 1; i <= count; i++)
            {
                Transform module = Child(modules, $"Tunnel section {i + 1:00}");
                module.localPosition = Vector3.forward * (length * i);
                foreach (Renderer source in structure)
                {
                    if (source == front) continue;
                    Transform child = Child(module, source.name);
                    child.localPosition = junction.InverseTransformPoint(source.transform.position);
                    child.localRotation = Quaternion.Inverse(junction.rotation) * source.transform.rotation;
                    child.localScale = source.transform.lossyScale;
                    if (!child.TryGetComponent(out MeshFilter filter)) filter = child.gameObject.AddComponent<MeshFilter>();
                    if (!child.TryGetComponent(out MeshRenderer renderer)) renderer = child.gameObject.AddComponent<MeshRenderer>();
                    filter.sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                    bool exitArch = i == count && source.name.StartsWith("Stone_Portal_Arch", StringComparison.Ordinal);
                    Material lining = materials[source.name.StartsWith("Tunnel_Rib", StringComparison.Ordinal) ? Concrete : Dark];
                    renderer.sharedMaterials = exitArch ? source.sharedMaterials : Enumerable.Repeat(lining, filter.sharedMesh.subMeshCount).ToArray();
                }
            }

            var baffles = new SurfaceMesh();
            float interiorStart = entrance + 1.1f;
            float interiorEnd = exit - 1.1f;
            float center = (interiorStart + interiorEnd) * .5f;
            float depth = interiorEnd - interiorStart;
            baffles.Box(Dark, new Vector3(0f, crown + .52f, center), new Vector3(halfAperture * 2f + 1.6f, .65f, depth));
            foreach (int side in new[] { -1, 1 })
            {
                baffles.Box(Dark, new Vector3(side * (halfAperture + .32f), 1.68f, center), new Vector3(.65f, 3.75f, depth));
                for (float z = entrance + length + 8f; z < interiorEnd; z += 12f)
                    baffles.Box(Rust, new Vector3(side * (halfAperture - .13f), .65f, z), new Vector3(.085f, .19f, .3f));
            }
            SaveMesh(parent, "Tunnel shadow enclosure", baffles, true);
        }

        public Material DampSteel(Material[] sourceSlots)
        {
            const string name = "Damp rail steel";
            string path = Folder + "/JunctionMeshes.asset";
            Material existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault(material => material.name == name);
            if (existing != null) return existing;
            Material baseSteel = sourceSlots.First(material => material.name == "DeadRail_LowPoly_Steel");
            var damp = new Material(baseSteel) { name = name };
            damp.SetColor("_BaseColor", new Color(.32f, .36f, .42f));
            damp.SetFloat("_Metallic", .55f);
            damp.SetFloat("_Smoothness", .78f);
            AssetDatabase.AddObjectToAsset(damp, path);
            return damp;
        }

        public void BuildTrackExtensions(Transform root, MeshRenderer railSample, Matrix4x4 basis, Vector3 sourceEnd,
            Vector3[] startHeadings, params Vector3[][] paths)
        {
            Mesh template = railSample.GetComponent<MeshFilter>().sharedMesh;
            using var read = MeshUtility.AcquireReadOnlyMeshData(template);
            Mesh.MeshData source = read[0];
            using var positions = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
            using var normals = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
            using var texture = new NativeArray<Vector2>(source.vertexCount, Allocator.Temp);
            source.GetVertices(positions);
            source.GetNormals(normals);
            if (source.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) source.GetUVs(0, texture);
            Matrix4x4 normalMatrix = basis.inverse.transpose;
            var original = new RailVertex[source.vertexCount];
            for (int i = 0; i < original.Length; i++)
                original[i] = new RailVertex(basis.MultiplyPoint3x4(positions[i]) - sourceEnd,
                    normalMatrix.MultiplyVector(normals[i]).normalized, texture[i]);

            var vertices = new List<Vector3>();
            var vertexNormals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>[source.subMeshCount];
            var profiles = new List<RailVertex>[source.subMeshCount];
            for (int slot = 0; slot < source.subMeshCount; slot++)
            {
                triangles[slot] = new List<int>();
                using var indices = new NativeArray<int>(source.GetSubMesh(slot).indexCount, Allocator.Temp);
                source.GetIndices(indices, slot);
                profiles[slot] = SubdivideRailSection(original, indices);
            }

            for (int branch = 0; branch < paths.Length; branch++)
            {
                Vector3[] path = paths[branch];
                var distance = new float[path.Length];
                var headings = new Vector3[path.Length];
                for (int i = 0; i < path.Length; i++)
                {
                    if (i > 0) distance[i] = distance[i - 1] + Vector3.Distance(path[i - 1], path[i]);
                    headings[i] = (path[Mathf.Min(path.Length - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                }
                headings[0] = startHeadings[branch];
                int sectionCount = Mathf.Max(1, Mathf.RoundToInt(distance[^1] / 8f));
                float sectionLength = distance[^1] / sectionCount;
                for (int section = 0; section < sectionCount; section++)
                {
                    for (int slot = 0; slot < profiles.Length; slot++)
                    foreach (RailVertex vertex in profiles[slot])
                    {
                        Vector3 originalPosition = vertex.Position;
                        float station = (section + Mathf.Clamp01(1f + originalPosition.z / 8f)) * sectionLength;
                        int next = Array.BinarySearch(distance, station);
                        next = Mathf.Clamp(next < 0 ? ~next : next, 1, path.Length - 1);
                        float t = Mathf.InverseLerp(distance[next - 1], distance[next], station);
                        Quaternion orientation = Quaternion.LookRotation(Vector3.Slerp(headings[next - 1], headings[next], t));
                        triangles[slot].Add(vertices.Count);
                        vertices.Add(Vector3.Lerp(path[next - 1], path[next], t) + orientation * new Vector3(originalPosition.x, originalPosition.y, 0f));
                        vertexNormals.Add(orientation * vertex.Normal);
                        uv.Add(vertex.Uv);
                    }
                }
            }

            const string meshName = "Extended running rails";
            Mesh existing = AssetDatabase.LoadAllAssetsAtPath(meshAsset).OfType<Mesh>().FirstOrDefault(mesh => mesh.name == meshName);
            Mesh mesh = existing != null ? existing : new Mesh { name = meshName };
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertexNormals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = triangles.Length;
            for (int slot = 0; slot < triangles.Length; slot++) mesh.SetTriangles(triangles[slot], slot, false);
            mesh.RecalculateBounds();
            if (existing == null) AssetDatabase.AddObjectToAsset(mesh, meshAsset);
            else EditorUtility.SetDirty(mesh);
            Material[] slots = railSample.sharedMaterials;
            Material dampSteel = DampSteel(slots);
            for (int slot = 0; slot < slots.Length; slot++)
                if (slots[slot].name == "DeadRail_LowPoly_Steel") slots[slot] = dampSteel;
            Transform node = Child(root, meshName);
            if (!node.TryGetComponent(out MeshFilter filter)) filter = node.gameObject.AddComponent<MeshFilter>();
            if (!node.TryGetComponent(out MeshRenderer renderer)) renderer = node.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = slots;
            Debug.Log("[Rail landscape] Extensions use the original rail profile, aligned to the live mesh endpoints.");
        }

        private readonly struct RailVertex
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector2 Uv;

            public RailVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                Position = position;
                Normal = normal;
                Uv = uv;
            }
        }

        private static List<RailVertex> SubdivideRailSection(RailVertex[] points, NativeArray<int> triangles)
        {
            var subdivided = new List<RailVertex>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var triangle = new List<RailVertex> { points[triangles[i]], points[triangles[i + 1]], points[triangles[i + 2]] };
                float first = triangle.Min(vertex => vertex.Position.z), last = triangle.Max(vertex => vertex.Position.z);
                if (last - first <= .15f)
                {
                    subdivided.AddRange(triangle);
                    continue;
                }
                // Clip the long rail and ballast faces at one-metre intervals so they can follow the curve.
                for (int band = Mathf.FloorToInt(first); band < Mathf.CeilToInt(last); band++)
                {
                    List<RailVertex> polygon = Clip(Clip(triangle, band, true), band + 1f, false);
                    for (int j = 2; j < polygon.Count; j++)
                    {
                        subdivided.Add(polygon[0]);
                        subdivided.Add(polygon[j - 1]);
                        subdivided.Add(polygon[j]);
                    }
                }
            }
            return subdivided;

            static List<RailVertex> Clip(List<RailVertex> vertices, float plane, bool keepForward)
            {
                var result = new List<RailVertex>();
                for (int i = 0; i < vertices.Count; i++)
                {
                    RailVertex a = vertices[i], b = vertices[(i + 1) % vertices.Count];
                    bool aInside = keepForward ? a.Position.z >= plane : a.Position.z <= plane;
                    bool bInside = keepForward ? b.Position.z >= plane : b.Position.z <= plane;
                    if (aInside) result.Add(a);
                    if (aInside == bInside) continue;
                    float t = Mathf.InverseLerp(a.Position.z, b.Position.z, plane);
                    result.Add(new RailVertex(Vector3.Lerp(a.Position, b.Position, t), Vector3.Lerp(a.Normal, b.Normal, t).normalized, Vector2.Lerp(a.Uv, b.Uv, t)));
                }
                return result;
            }
        }
        private static Bounds BoundsIn(Transform root, Renderer renderer)
        {
            Bounds world = renderer.bounds;
            var bounds = new Bounds(root.InverseTransformPoint(world.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(root.InverseTransformPoint(world.center + Vector3.Scale(world.extents, new Vector3(x, y, z))));
            return bounds;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void SaveMesh(Transform parent, string name, SurfaceMesh source, bool collision)
        {
            Mesh existing = AssetDatabase.LoadAllAssetsAtPath(meshAsset).OfType<Mesh>().FirstOrDefault(mesh => mesh.name == name);
            Mesh mesh = source.Build(name, out string[] slots, existing);
            if (existing != null) EditorUtility.SetDirty(existing);
            else if (AssetDatabase.LoadMainAssetAtPath(meshAsset) == null) AssetDatabase.CreateAsset(mesh, meshAsset);
            else AssetDatabase.AddObjectToAsset(mesh, meshAsset);

            Transform node = Child(parent, name);
            if (!node.TryGetComponent(out MeshFilter filter)) filter = node.gameObject.AddComponent<MeshFilter>();
            if (!node.TryGetComponent(out MeshRenderer renderer)) renderer = node.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = slots.Select(slot => materials[slot]).ToArray();
            if (collision)
            {
                if (!node.TryGetComponent(out MeshCollider collider)) collider = node.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }
        }

        private void Pine(SurfaceMesh mesh, Vector3 ground, float h, bool dry)
        {
            float slope = Range(-.13f, .13f);
            mesh.Box(Timber, ground + Vector3.up * (.25f * h), new Vector3(.065f * h, .5f * h, .06f * h),
                Quaternion.Euler(0f, 0f, slope * 34f));
            for (int tier = 0; tier < (dry ? 2 : 3); tier++)
            {
                float start = (.25f + tier * .18f) * h;
                float radius = h * (.24f - .043f * tier);
                var basePoint = ground + new Vector3(slope * h * .42f, start, 0f);
                mesh.Crown(dry ? Ground : tier == 1 ? Scrub : Green, basePoint, radius, .39f * h, Range(0f, Mathf.PI));
            }
        }

        private void Rock(SurfaceMesh mesh, Vector3 origin, Vector3 size, bool exposed)
        {
            string surface = exposed ? Exposed : Stone;
            float angle = Range(0, Mathf.PI);
            var baseRing = new Vector3[6];
            var shoulder = new Vector3[6];
            Vector3 top = origin + new Vector3(-size.x * .15f, size.y, size.z * .1f);
            for (int i = 0; i < 6; i++)
            {
                float a = angle + i * Mathf.PI / 3f, r = Range(.7f, 1.05f);
                baseRing[i] = origin + new Vector3(Mathf.Cos(a) * size.x * .61f * r, -.18f, Mathf.Sin(a) * size.z * .61f * r);
                shoulder[i] = origin + new Vector3(Mathf.Cos(a) * size.x * .43f * r, size.y * Range(.36f, .7f), Mathf.Sin(a) * size.z * .47f * r);
            }
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                mesh.Quad(surface, baseRing[i], shoulder[i], shoulder[j], baseRing[j]);
                mesh.Triangle(i % 4 == 0 ? Ground : surface, shoulder[i], top, shoulder[j]);
            }
        }

        private void Tuft(SurfaceMesh mesh, Vector3 position, float height)
        {
            for (int i = 0; i < 3; i++)
            {
                var side = new Vector3(Range(-.13f, .13f), 0, Range(-.13f, .13f));
                Vector3 tip = position + new Vector3(side.x * 2.3f, height * Range(.45f, 1f), side.z * 2.3f);
                mesh.Triangle(i % 2 == 0 ? Scrub : "Straw", position - side, position + side, tip);
                mesh.Triangle(i % 2 == 0 ? Scrub : "Straw", position + side, position - side, tip);
            }
        }

        private float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
    }

    private sealed class SurfaceMesh
    {
        private readonly List<Vector3> points = new();
        private readonly List<Vector2> uv = new();
        private readonly SortedDictionary<string, List<int>> faces = new(StringComparer.Ordinal);

        public void Triangle(string material, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 u = (b - a).normalized;
            Vector3 v = Vector3.Cross(Vector3.Cross(b - a, c - a).normalized, u);
            if (!faces.TryGetValue(material, out List<int> triangles)) faces.Add(material, triangles = new List<int>());
            triangles.Add(points.Count);
            triangles.Add(points.Count + 1);
            triangles.Add(points.Count + 2);
            Add(a); Add(b); Add(c);

            void Add(Vector3 point)
            {
                points.Add(point);
                uv.Add(new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v)) * .31f);
            }
        }

        public void Quad(string material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Triangle(material, a, b, c);
            Triangle(material, a, c, d);
        }

        public void Box(string material, Vector3 center, Vector3 size, Quaternion rotation = default)
        {
            if (rotation == default) rotation = Quaternion.identity;
            Vector3 h = size * .5f;
            Vector3 At(int x, int y, int z) => center + rotation * Vector3.Scale(h, new Vector3(x, y, z));
            Vector3 a = At(-1, -1, -1), b = At(1, -1, -1), c = At(1, 1, -1), d = At(-1, 1, -1);
            Vector3 e = At(-1, -1, 1), f = At(1, -1, 1), g = At(1, 1, 1), j = At(-1, 1, 1);
            Quad(material, a, d, c, b); Quad(material, e, f, g, j); Quad(material, a, e, j, d);
            Quad(material, b, c, g, f); Quad(material, d, j, g, c); Quad(material, a, b, f, e);
        }

        public void Crown(string material, Vector3 basePoint, float radius, float height, float angle)
        {
            Vector3 tip = basePoint + Vector3.up * height;
            Vector3 Ring(int i)
            {
                float a = angle + Mathf.PI * i / 3f;
                return basePoint + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            for (int i = 0; i < 6; i++)
            {
                Triangle(material, Ring(i), tip, Ring(i + 1));
                Triangle(material, basePoint, Ring(i), Ring(i + 1));
            }
        }

        public Mesh Build(string name, out string[] slots, Mesh mesh)
        {
            mesh ??= new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = points.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(points);
            mesh.SetUVs(0, uv);
            slots = faces.Keys.ToArray();
            mesh.subMeshCount = slots.Length;
            for (int i = 0; i < slots.Length; i++) mesh.SetTriangles(faces[slots[i]], i, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
