using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class FarmingMapAuthoring
{
    private static void BuildThemedStop(Transform root, int index)
    {
        var art = new StopArt(root, index);
        switch (index)
        {
            case 0: art.Bypass(); break;
            case 1: art.Station(); break;
            case 2: art.Scrapyard(); break;
            case 3: art.DryPlain(); break;
            case 4: art.Factory(); break;
            case 5: art.ResearchSite(); break;
            case 6: art.ContaminatedTunnel(); break;
            case 7: art.BlackMarket(); break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
        EnsureDepartureCorridor(root, index);
    }

    [MenuItem("DeadRail/Route/Apply farming departure scenery")]
    private static void ApplyDepartureScenery()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        for (int index = 0; index < MapNodeManager.MapCount; index++)
        {
            string path = $"{VariantFolder}/Stop_{index + 1:00}.prefab";
            GameObject prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!EnsureDepartureCorridor(prefab.transform, index)) continue;
                if (PrefabUtility.SaveAsPrefabAsset(prefab, path) == null) throw new InvalidOperationException($"Could not save {path}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Rail landscape] Departure scenery applied to all farming maps.");
    }

    private static bool EnsureDepartureCorridor(Transform root, int index)
    {
        bool underground = index >= MapNodeManager.NormalMapCount;
        string name = underground ? "Underground departure corridor" : "Outdoor departure corridor";
        if (root.Find(name) != null) return false;
        string path = $"{VariantFolder}/{(underground ? "UndergroundDeparture" : "OutdoorDeparture")}.prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            var corridor = new GameObject(name);
            try
            {
                new StopArt(corridor.transform, index).DepartureCorridor(underground);
                asset = PrefabUtility.SaveAsPrefabAsset(corridor, path);
                if (asset == null) throw new InvalidOperationException($"Could not save {path}.");
            }
            finally { UnityEngine.Object.DestroyImmediate(corridor); }
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
        Undo.RegisterCreatedObjectUndo(instance, "Apply farming departure corridor");
        return true;
    }

    private sealed class StopArt
    {
        private const float Grade = -0.12f;
        private readonly Transform root;
        private readonly System.Random random;
        private readonly Material asphalt, concrete, dark, steel, rust, timber, dust, soil, moss, warning, teal, fadedTeal, cream, red, glass, black;

        public StopArt(Transform root, int index)
        {
            this.root = root;
            random = new System.Random(1831 + index * 1297);
            var materials = AssetDatabase.LoadAllAssetsAtPath("Assets/_Dev_A/StartingRailYard/Weathering.asset")
                .OfType<Material>().ToDictionary(material => material.name);
            asphalt = materials["Asphalt"]; concrete = materials["Concrete"]; dark = materials["DarkConcrete"];
            steel = materials["Steel"]; rust = materials["Rust"]; timber = materials["Wood"];
            dust = materials["GravelLight"]; soil = materials["Soil"]; moss = materials["Moss"];
            warning = materials["Yellow"]; teal = materials["Teal"]; fadedTeal = materials["FadedTeal"];
            cream = materials["Cream"]; red = materials["Red"]; glass = materials["Glass"]; black = materials["Black"];
        }

        public void Bypass()
        {
            OutdoorGround(asphalt);
            for (int i = 0; i < 9; i++)
            {
                float z = -30 + i * 7.2f;
                G("Leaning fence post", rust, 26, 1, z, .16f, 2.4f, .17f, roll: i == 4 ? 24 : -4);
                if (i != 4 && i != 5)
                    Beam("Broken fence runner", steel, V(26, 1.34f, z - 3), V(26, 1.25f, z + 3), .12f);
                G("Discarded sleeper", timber, -7.8f, .02f, z, 2.35f, .22f, .31f, yaw: i * 9);
            }
            Cabin("Signal hut", 11.8f, -21, teal);
            G("Collapsed relay cabinet", fadedTeal, 20, .66f, 16, 2.4f, 1.7f, 1.7f, true, 17, -18);
            for (int i = 0; i < 5; i++) G("Cable drum", timber, 22.8f, .61f, -10 + i * 4.3f, 1.13f, 1.35f, 1.2f, true, i * 12);
            Sign("BYPASS / SIGNAL 04", 10.2f, 2.5f, -14.5f, teal);
            Light("Signal hut emergency lamp", 11.8f, 3.15f, -18.5f, new Color(1f, .64f, .26f), 12, 7);
            for (int i = 0; i < 8; i++)
                G("Weathered railway embankment", concrete, -25.2f, 1.1f, -36 + i * 10.1f, 5.8f, 2.7f, 9.9f, roll: -16);
            Rubble(26, 16, 28, 2.3f, 15, soil);
            Supply((9,-16), (7,-6), (15,-3), (18,7), (21,12), (12,15));
        }

        public void Station()
        {
            OutdoorGround(dark);
            G("Platform concrete", concrete, 11, Grade + .025f, 2, 12.6f, .09f, 49, true);
            for (int i = 0; i < 12; i++) G("Faded platform edge", warning, 5.15f, .054f, -22 + i * 4.15f, .24f, .016f, 3.05f);
            for (int i = 0; i < 7; i++)
            {
                float z = -20 + i * 7.2f;
                G("Platform canopy column", steel, 15.9f, 2.4f, z, .27f, 5, .29f, true);
                Beam("Canopy cross brace", rust, V(9.7f, 4.65f, z), V(17.1f, 5.06f, z), .15f);
                if (i != 3 && i != 4) G("Surviving roof sheet", i % 2 == 0 ? teal : fadedTeal, 13.4f, 5.02f, z, 7.7f, .13f, 6.95f, roll: 4);
                if (i % 2 == 0) G("Empty station bench", timber, 13, .48f, z + 1.25f, .65f, .9f, 2.9f, true);
            }
            Cabin("Closed ticket office", 22.2f, -17, cream);
            Sign("HALT / SERVICE SUSPENDED", 11.5f, 3.55f, -13.5f, teal);
            G("Fallen canopy section", rust, 18, .55f, 14, 4.9f, .12f, 3.2f, true, 24, -11);
            Light("Platform emergency lamp", 12, 4, -11, new Color(1f, .72f, .4f), 16, 8);
            for (int i = 0; i < 5; i++)
            {
                float height = Range(3.3f, 7.2f);
                float z = -24 + i * 14f;
                G("Deserted station neighborhood", dark, 40 + i % 2 * 4, height / 2 - .18f, z, 8.5f, height, 10.3f);
                G("Vacant storefronts", black, 35.69f + i % 2 * 4, 1.47f, z, .08f, 1.6f, 7.8f);
            }
            Rubble(19, 14, 24, 4, 8, concrete);
            Supply((8,-15), (8,-2), (16,-9), (18,4), (22,11), (9,17));
        }

        public void Scrapyard()
        {
            OutdoorGround(soil);
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < 11; i++)
            {
                float x = Range(17.5f, 27), z = (row == 0 ? -16 : 15) + Range(-4, 4);
                G("Crushed steel or machine panel", i % 3 == 0 ? fadedTeal : rust, x, Range(.35f, 1.3f), z,
                    Range(1.1f, 3.1f), Range(.22f, .77f), Range(.9f, 2.1f), false, Range(0,360), Range(-23,22));
            }
            G("Salvage enclosure", steel, 29, 1.4f, 1, .3f, 3.2f, 50, true);
            G("Crane base", dark, 25, .58f, 3, 3.2f, 1.4f, 3.1f, true);
            Beam("Scrap crane mast", rust, V(25, 1, 3), V(25, 7.25f, 3), .54f);
            Beam("Collapsed hoist boom", steel, V(25, 7.25f, 3), V(14.5f, 5.1f, 5), .34f);
            Beam("Hoist chain", black, V(15.2f, 5.24f, 4.9f), V(15.2f, 3.35f, 4.9f), .045f);
            G("Scrap skip", teal, 17.8f, .57f, -3, 4.3f, 1.35f, 2.3f, true, 7);
            G("Scrap skip", rust, 21.9f, .57f, 7.6f, 3.6f, 1.35f, 2.2f, true, -11);
            Sign("SCRAP / SORT 03", 12.4f, 2.7f, -15, rust);
            for (int i = 0; i < 7; i++)
            {
                float z = -26 + i * 10.2f;
                G("Distant rusted container", i % 3 == 0 ? fadedTeal : rust, 35.7f + Range(0,5), 1.24f, z, 5.1f, 2.72f, 7.3f, yaw: Range(-12,11));
                if (i % 2 == 0) G("Buckled container stack", steel, 37.2f, 3.62f, z, 4.9f, 2.08f, 7f, yaw: 13, roll: -9);
            }
            Rubble(22, -11, 21, 5, 36, rust);
            Supply((9,-13), (8,-3), (17,-8), (17,1), (17,10), (10,16));
        }

        public void DryPlain()
        {
            OutdoorGround(dust);
            G("Dry irrigation channel", soil, 22, Grade + .04f, 0, 2.1f, .08f, 58);
            for (int i = 0; i < 18; i++)
            {
                float x = Range(7, 29), z = Range(-29, 29);
                Beam("Dry ground fissure", soil, V(x, .037f, z), V(x + Range(.4f, 1.7f), .037f, z + Range(-.8f, .8f)), .065f);
                if (i % 3 == 0)
                {
                    Beam("Dead scrub stem", timber, V(x, Grade, z), V(x + .2f, .67f, z), .065f);
                    Beam("Dead scrub branch", timber, V(x + .08f, .28f, z), V(x - .29f, .57f, z + .15f), .037f);
                }
            }
            for (int side = -1; side <= 1; side += 2)
            for (int end = -1; end <= 1; end += 2)
                Beam("Water tower leg", timber, V(26 + side * 2, Grade, -13 + end * 2), V(26 + side * 1.25f, 5.2f, -13 + end * 1.25f), .24f);
            Cylinder(root, "Empty water reservoir", V(26, 5.7f, -13), V(4.1f, .82f, 4.1f), rust);
            Cabin("Dry pump shed", 14.9f, 17.5f, timber);
            G("Broken irrigation bridge", timber, 22.1f, .08f, 7, 3.8f, .17f, 1.7f, true, -8);
            Sign("WATER STATION / DRY", 13, 2.6f, 9.4f, teal);
            for (int i = 0; i < 11; i++)
            {
                float x = (i % 2 == 0 ? 1 : -1) * Range(40, 80);
                GameObject ridge = Primitive(root, "Eroded dry ridge", PrimitiveType.Sphere,
                    V(x, -.7f, -45 + i * 18), V(Range(12,29), Range(1.9f,6), Range(13,31)), dust);
                Undo.DestroyObjectImmediate(ridge.GetComponent<Collider>());
            }
            Rubble(26, 6, 22, 6, 11, dust);
            Supply((10,-17), (8,-6), (15,-3), (18,6), (24,7), (10,15));
        }

        public void Factory()
        {
            OutdoorGround(asphalt);
            G("Oil-stained loading apron", dark, 17, Grade + .017f, 6, 22, .12f, 37, true);
            G("Factory blast wall", concrete, 18, 3.05f, 24.8f, 19, 6.35f, .48f, true);
            G("Factory side wall", dark, 27.8f, 3.1f, 13, .58f, 6.45f, 24, true);
            for (int i = 0; i < 6; i++)
            {
                float z = 1.6f + 4.4f * i;
                G("Roof support", rust, 9.8f, 3.32f, z, .39f, 7.05f, .41f, true);
                Beam("Factory roof joist", steel, V(9.6f,6.86f,z), V(27.8f,6.82f,z), .19f);
                if (i != 2 && i != 3) G("Broken corrugated roof", i % 2 == 0 ? fadedTeal : rust, 19, 7, z, 18.8f, .14f, 4.29f);
            }
            for (int i = 0; i < 3; i++) Cylinder(root, "Abandoned industrial tank", V(19 + i * 4.3f, 1.55f, -19), V(3.3f, 1.7f, 3.3f), rust);
            G("Assembly worktable", steel, 19.2f, .67f, 7.5f, 4.4f, 1.49f, 1.85f, true);
            G("Stripped machine housing", dark, 22.2f, 1.2f, 16.7f, 4.6f, 2.65f, 2.4f, true, 12);
            Sign("ORDNANCE WORKS / GATE 2", 16, 4.1f, 1.35f, rust);
            Light("Loading bay work light", 16, 5.3f, 15, new Color(1f, .55f, .26f), 19, 12);
            for (int i = 0; i < 3; i++)
            {
                float height = 7.8f + 2 * i;
                Cylinder(root, "Factory chimney", V(35.5f + i * 5, height / 2 - .17f, 23 + i * 5), V(1.95f, height / 2, 1.95f), dark);
                G("Disused factory outbuilding", steel, 38 + i * 8, 1.9f, 23 + i * 5, 7.5f, 4.1f, 8.8f);
            }
            Rubble(25, 4, 24, 4, 13, concrete);
            Supply((8,-14), (11,-7), (15,2), (16,9), (24,3), (18,18));
        }

        public void ResearchSite()
        {
            OutdoorGround(concrete);
            G("Cracked lab foundation", dark, 19.4f, Grade + .035f, 5.7f, 21.4f, .11f, 35, true);
            for (int i = 0; i < 7; i++)
            {
                float x = 10 + i * 2.85f;
                float h = Range(1.5f, 4.7f);
                G("Sheared laboratory wall", i % 2 == 0 ? cream : concrete, x, h / 2, 22, 2.72f, h, .48f, true, Range(-11,11), Range(-13,13));
                G("Painted laboratory trim", fadedTeal, x, .53f, 21.71f, 2.64f, .2f, .06f);
            }
            G("Sunken laboratory partition", concrete, 25.7f, 1.56f, 10.5f, .38f, 3.2f, 10.6f, true, -7, -9);
            Beam("Slumped laboratory beam", steel, V(15,1.35f,12.4f), V(25,3.6f,17.4f), .31f);
            G("Broken examination table", fadedTeal, 20.5f, .49f, 4.2f, 3.15f, 1.2f, 1.7f, true, 18, -10);
            G("Instrument cabinet", teal, 16.8f, 1.1f, -14, 1.65f, 2.37f, 1.15f, true, -12);
            G("Decontamination booth", glass, 25.2f, 1.65f, -12, 2.8f, 3.35f, 3.1f, true);
            Sign("RESEARCH WING / QUARANTINE", 16.3f, 2.68f, -19.2f, teal);
            Light("Emergency triage lamp", 14.4f, 2.8f, 9, new Color(.51f, .8f, 1f), 15, 9);
            for (int i = 0; i < 5; i++)
            {
                float z = -12 + 10 * i;
                G("Distant ruined research block", concrete, 38f, 2.3f, z, 8.8f, 5f, 8.5f, roll: Range(-8,8));
                G("Cracked upper floor plate", dark, 38f, 3.45f, z + .85f, 9.1f, .21f, 8.8f, roll: Range(-8,8));
            }
            Rubble(24, 16, 29, 7, 15, concrete);
            Supply((8,-15), (12,-8), (17,-5), (12,3), (22,-4), (18,15));
        }

        public void ContaminatedTunnel()
        {
            Tunnel(new Color(.32f, .75f, .54f));
            Material liquid = Material("Contaminated liquid", new Color(.12f, .55f, .2f));
            G("Chemical overflow", liquid, 16.8f, Grade + .05f, 1, 3.9f, .02f, 26);
            for (int i = 0; i < 9; i++)
            {
                float z = -27 + 6.4f * i;
                G("Pipe support", steel, 17.6f, .88f, z, .17f, 1.9f, .2f);
                if (i != 4) Beam("Contaminant feed pipe", rust, V(17.65f,1.7f,z - 3), V(17.65f,1.7f,z + 3), .28f);
            }
            Beam("Ruptured feeder", rust, V(17.65f,1.7f,-3), V(16.35f,.37f,-.2f), .28f);
            for (int i = 0; i < 4; i++) Cylinder(root, "Sealed chemical drum", V(13.8f, .63f, -17.5f + i), V(.77f, .73f, .77f), i % 2 == 0 ? warning : steel);
            G("Contaminated access gate", steel, 19.8f, 1.56f, 13, .3f, 3.4f, 8.2f, true);
            Sign("BIOHAZARD / DRAIN 07", 10.5f, 2.67f, -16.1f, teal);
            Light("Rupture work light", 14.7f, 3.7f, .4f, new Color(.28f, 1f, .49f), 16, 20);
            Light("Chemical drum emergency lamp", 13f, 3.1f, -17f, new Color(.42f, .87f, .53f), 11, 15);
            Rubble(19, 5, 13, 3.5f, 10, dark);
            Supply((7,-16), (10,-9), (12,-2), (7,5), (12,9), (11,17));
        }

        public void BlackMarket()
        {
            Tunnel(new Color(1f, .62f, .28f));
            for (int i = 0; i < 6; i++)
            {
                float z = -23 + i * 8.3f;
                G("Battered cargo rack", steel, 17.2f, 1.2f, z, 1.55f, 2.65f, 3.2f, true);
                G("Rack face", timber, 16.35f, 1.13f, z, .23f, .16f, 3.2f);
                if (i % 2 == 0) G("Unmarked cargo case", rust, 13.7f, .53f, z + 2, 1.63f, 1.22f, 1.2f, true, Range(-15,15));
            }
            G("Closed cargo shutter", dark, 13.5f, 2.8f, 24.9f, 12.3f, 5.7f, .57f, true);
            for (int i = 0; i < 10; i++) G("Shutter slat", rust, 13.5f, .42f + i * .51f, 24.57f, 11.9f, .15f, .16f);
            G("Abandoned market counter", timber, 10.7f, .69f, 12, 2.35f, 1.55f, 1.18f, true, -6);
            G("Sagging market awning", teal, 12.3f, 3.16f, 12, 5.15f, .10f, 3.15f, false, -7, -9);
            Sign("CARGO 19 / NIGHT TRADE", 9.4f, 2.62f, -13.1f, rust);
            Light("Market lantern", 10.8f, 2.6f, 10, new Color(1f, .55f, .24f), 14, 15);
            Light("Cargo inspection lamp", 13.5f, 3.35f, -17f, new Color(1f, .65f, .32f), 13, 13);
            Supply((8,-16), (12,-8), (8,-2), (13,2), (8,9), (11,17));
        }

        public void DepartureCorridor(bool underground)
        {
            Material signal = RailLandscapeAuthoring.NightSignalMaterial();
            foreach (int side in new[] { -1, 1 })
            {
                G("Trackside service shoulder", underground ? dark : soil, side * 6.9f, -.205f, 43f, 6.9f, .13f, 62f, true);
                for (int i = 0; i < 8; i++)
                {
                    float z = 21 + i * 7.4f;
                    G("Reflective limit post", steel, side * 4.9f, .37f, z, .15f, .99f, .16f);
                    G("Limit marker face", warning, side * 4.9f, .74f, z - .09f, .15f, .18f, .04f);
                    if (!underground)
                    {
                        G("Corridor retaining block", dark, side * 15.9f, .32f, z, .47f, 1.17f, 6.8f, true);
                        if (i % 2 == 0) G("Abandoned cable drum", timber, side * 10.7f, .34f, z, 1.1f, .89f, .77f, true, side * 21);
                    }
                }
            }

            foreach (float z in new[] { 23f, 52f })
            {
                G("Exit signal mast", steel, -3.9f, 1.45f, z, .16f, 3.12f, .17f);
                G("Exit signal housing", black, -3.9f, 2.8f, z, .47f, .72f, .31f);
                G("Exit signal lens", signal, -3.9f, 2.94f, z - .17f, .16f, .16f, .04f);
                Light("Railway exit signal spill", -2.4f, 1.28f, z - 1f, new Color(1f, .07f, .024f), 9.5f, 4.3f);
                if (underground) continue;
                foreach (int side in new[] { -1, 1 })
                    G("Exit catenary post", steel, side * 5.85f, 3.37f, z + 2, .23f, 7.1f, .24f);
                Beam("Exit catenary crossbeam", rust, V(-5.85f, 6.55f, z + 2), V(5.85f, 6.55f, z + 2), .15f);
                Beam("Catenary crossbrace", steel, V(-5.85f, 5.58f, z + 2), V(-3.8f, 6.55f, z + 2), .105f);
            }
            if (!underground)
                Beam("Overhead departure wire", steel, V(0, 5.8f, 10), V(0, 5.8f, 76f), .035f);
        }

        private void OutdoorGround(Material finish)
        {
            G("Distant terrain", finish, 0, -.46f, 22, 210, .42f, 252);
            G("Worn service side", finish, 16.8f, -.24f, -1, 29.9f, .29f, 75, true);
            G("Opposite track verge", soil, -13, -.32f, -1, 19, .24f, 75, true);
            for (int i = 0; i < 19; i++)
            {
                float x = i % 3 == 0 ? Range(-21,-5) : Range(19,30);
                G("Weathered ground patch", i % 2 == 0 ? moss : soil, x, -.025f, Range(-36,36), Range(.6f,3.2f), .027f, Range(.42f,1.84f), false, Range(-90,90));
            }
            for (int i = 0; i < 6; i++)
            {
                float z = -31 + i * 12.7f;
                G("Outer service-road barrier", dark, 30.6f, .38f, z, .41f, 1.0f, 11.6f, true, roll: i % 3 == 0 ? 8 : 0);
                G("Hazard paint on boundary", warning, 30.37f, .61f, z - 4.7f, .035f, .18f, .57f);
            }
        }

        private void Tunnel(Color color)
        {
            G("Underground floor", dark, 2, -.29f, 0, 42, .21f, 244, true);
            GameObject tunnelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelAsset);
            if (tunnelAsset == null) throw new InvalidOperationException("Blackout tunnel FBX is missing.");
            for (int i = 0; i < 10; i++)
            {
                Transform shell = NewRoot(root, $"Tunnel section {i + 1}");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(tunnelAsset, shell);
                Undo.RegisterCreatedObjectUndo(model, "Place underground tunnel section");
                shell.localPosition = V(0, 0, -96 + i * 24);
                shell.localScale = V(4, 1.5f, 1.01f);
                Light($"Tunnel working light {i + 1}", 0, 6, -108 + i * 24, color, 24, 12);
            }
            Light("Farming work light", 10, 4.5f, -1, color, 20, 18);
        }

        private void Supply(params (float x, float z)[] layout)
        {
            Transform positions = NewRoot(root, "Supply positions");
            for (int i = 0; i < layout.Length; i++)
            {
                var (x, z) = layout[i];
                float height = PhysicsPlatformTop(x);
                NewRoot(positions, $"Supply {i + 1}").localPosition = V(x, height + .45f, z);
            }
        }

        private static float PhysicsPlatformTop(float x) => x <= 19.9f ? Grade : Grade - .04f;

        private void Cabin(string name, float x, float z, Material wall)
        {
            G(name, wall, x, 1.48f, z, 4.2f, 3.2f, 3.6f, true);
            G("Bent cabin roof", rust, x, 3.12f, z, 4.65f, .17f, 4.05f, roll: -6);
            G("Boarded door", timber, x + .65f, 1.02f, z - 1.84f, 1.12f, 2.2f, .09f);
            G("Dark cabin window", black, x - .87f, 1.83f, z - 1.846f, 1.25f, .8f, .065f);
            Beam("Window crossboard", timber, V(x - 1.6f, 1.41f, z - 1.9f), V(x - .19f, 2.26f, z - 1.9f), .09f);
        }

        private void Sign(string caption, float x, float y, float z, Material board)
        {
            G("Sign post", rust, x, y / 2, z, .12f, y, .12f);
            G("Wayfinding board", board, x, y, z, 3.65f, .56f, .12f);
            Transform label = NewRoot(root, caption);
            label.localPosition = V(x, y, z - .075f);
            TextMesh text = label.gameObject.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontSize = 48;
            text.characterSize = .017f;
            text.color = new Color(.9f, .86f, .72f);
            text.text = caption;
        }

        private void Rubble(float x, float z, int count, float width, float length, Material debris)
        {
            for (int i = 0; i < count; i++)
                G("Broken debris fragment", i % 3 == 0 ? rust : debris, x + Range(-width/2,width/2), Range(.01f,.16f),
                    z + Range(-length/2,length/2), Range(.16f,.75f), Range(.12f,.48f), Range(.19f,.61f),
                    false, Range(0,360), Range(-24,24));
        }

        private GameObject G(string name, Material material, float x, float y, float z, float width, float height, float depth,
            bool solid = false, float yaw = 0, float roll = 0)
        {
            GameObject obj = Box(root, name, V(x,y,z), V(width,height,depth), material);
            obj.transform.localRotation = Quaternion.Euler(0, yaw, roll);
            if (!solid) Undo.DestroyObjectImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        private void Beam(string name, Material material, Vector3 start, Vector3 end, float width)
        {
            Vector3 offset = end - start;
            Vector3 center = (start + end) * .5f;
            GameObject beam = G(name, material, center.x, center.y, center.z, width, width, offset.magnitude);
            beam.transform.localRotation = Quaternion.LookRotation(offset,
                Mathf.Abs(offset.normalized.y) > .9f ? Vector3.forward : Vector3.up);
        }

        private void Light(string name, float x, float y, float z, Color color, float range, float intensity)
            => PointLight(root, name, V(x,y,z), color, range, intensity);

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    }
}
