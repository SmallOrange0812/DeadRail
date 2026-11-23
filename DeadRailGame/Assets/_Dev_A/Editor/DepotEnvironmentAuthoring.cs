using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Authors visual-only rear scenery and a miniature 3D skyline; preserves the depot's gameplay.</summary>
public static class DepotEnvironmentAuthoring
{
    private const string Folder = "Assets/_Dev_A/StartingRailYard/Backdrop";
    private const string DepotPath = "Assets/_Dev_A/StartingRailYard/AbandonedRailDepot.prefab";
    private const string BackdropPath = Folder + "/DepotBackdrop.prefab";
    private const string MainScene = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";
    private const string MenuPath = "Assets/_Dev_A/UI/MenuDepot.prefab";
    private const string BackdropName = "Rear district and 3D skyline";

    [MenuItem("DeadRail/Starting Rail Yard/Apply rear district and night backdrop")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode first.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Save your scene edits before applying the environment.");
        string previousScene = SceneManager.GetActiveScene().path;
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        BuildBackdrop();
        GameObject depot = PrefabUtility.LoadPrefabContents(DepotPath);
        try
        {
            AttachBackdrop(depot);
            PrefabUtility.SaveAsPrefabAsset(depot, DepotPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(depot); }
        SynchronizeMenus();
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
        CaptureEnvironment();
        Debug.Log("[Depot environment] Rear district, 1:32 3D skyline and matching gameplay night applied.");
    }

    // Also called after a deliberate depot regeneration, so decoration is not silently lost.
    public static void AttachBackdrop(GameObject depot)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(BackdropPath);
        if (asset == null) return;
        Transform scenery = depot.transform.Find("Yard scenery");
        if (scenery == null) throw new InvalidOperationException("The depot scenery root is missing.");
        // Remove the old nested placement before attaching the independent background.
        Transform oldNested = scenery.Find(BackdropName);
        if (oldNested != null) UnityEngine.Object.DestroyImmediate(oldNested.gameObject);
        Transform previous = depot.transform.Find(BackdropName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, depot.transform);
        instance.name = BackdropName;
        instance.transform.localPosition = Vector3.zero;
        var settings = new SerializedObject(depot.GetComponent<StartingRailYard>());
        settings.FindProperty("rearDistrict").objectReferenceValue =
            instance.transform.Find("Rear service district - full size");
        settings.FindProperty("outdoorBackdrop").objectReferenceValue =
            instance.transform.Find("Persistent outdoor background");
        settings.FindProperty("clearDistance").floatValue = 350f;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SynchronizeMenus()
    {
        string previousScene = SceneManager.GetActiveScene().path;
        EnvironmentLook look;
        Scene gameplay = SceneManager.GetSceneByPath(MainScene);
        bool openedGameplay = !gameplay.IsValid() || !gameplay.isLoaded;
        if (openedGameplay) gameplay = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(gameplay);
            look = new EnvironmentLook();
        }
        finally { if (openedGameplay) EditorSceneManager.CloseScene(gameplay, true); }

        GameObject world = PrefabUtility.LoadPrefabContents(MenuPath);
        try
        {
            Transform previous = world.transform.Find("AbandonedRailDepot");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            GameObject depot = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(DepotPath), world.transform);
            depot.name = "AbandonedRailDepot";
            PrefabUtility.UnpackPrefabInstance(depot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (StartingRailYard component in depot.GetComponentsInChildren<StartingRailYard>(true))
                UnityEngine.Object.DestroyImmediate(component);
            foreach (Collider collider in depot.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Light light in world.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Directional) look.ApplySun(light);
            PrefabUtility.SaveAsPrefabAsset(world, MenuPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(world); }

        foreach (string scenePath in new[] { FrontendMenu.TitleScene, FrontendMenu.LobbyScene })
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath);
            GameObject previous = scene.GetRootGameObjects().Single(go => go.name == "MenuDepot");
            Vector3 position = previous.transform.position;
            Quaternion rotation = previous.transform.rotation;
            UnityEngine.Object.DestroyImmediate(previous);
            GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath), scene);
            replacement.transform.SetPositionAndRotation(position, rotation);
            look.Apply(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
    }

    private sealed class EnvironmentLook
    {
        private readonly Material sky = RenderSettings.skybox;
        private readonly AmbientMode ambientMode = RenderSettings.ambientMode;
        private readonly Color ambientSky = RenderSettings.ambientSkyColor;
        private readonly Color ambientEquator = RenderSettings.ambientEquatorColor;
        private readonly Color ambientGround = RenderSettings.ambientGroundColor;
        private readonly float ambientIntensity = RenderSettings.ambientIntensity;
        private readonly bool fog = RenderSettings.fog;
        private readonly FogMode fogMode = RenderSettings.fogMode;
        private readonly Color fogColor = RenderSettings.fogColor;
        private readonly float fogStart = RenderSettings.fogStartDistance;
        private readonly float fogEnd = RenderSettings.fogEndDistance;
        private readonly float fogDensity = RenderSettings.fogDensity;
        private readonly float reflectionIntensity = RenderSettings.reflectionIntensity;
        private readonly Color sunColor;
        private readonly float sunIntensity;
        private readonly Quaternion sunRotation;
        private readonly LightShadows sunShadows;

        public EnvironmentLook()
        {
            Light sun = RenderSettings.sun;
            if (sun == null) throw new InvalidOperationException("Gameplay sun is not assigned.");
            sunColor = sun.color; sunIntensity = sun.intensity;
            sunRotation = sun.transform.rotation; sunShadows = sun.shadows;
        }

        public void ApplySun(Light sun)
        {
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.transform.rotation = sunRotation;
            sun.shadows = sunShadows;
            sun.enabled = true;
        }

        public void Apply(Scene scene)
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            Light sun = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Light>(true))
                .Single(light => light.type == LightType.Directional);
            ApplySun(sun);
            RenderSettings.sun = sun;
        }
    }

    private static Color ColorOf(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    private static void BuildBackdrop()
    {
        Shader skylineShader = Shader.Find("DeadRail/Environment/Distant City");
        if (skylineShader == null) throw new InvalidOperationException("The distant city shader has not imported.");
        Material distant = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/DistantCity.mat");
        if (distant == null)
        {
            distant = new Material(skylineShader) { name = "Distant city - night atmosphere" };
            AssetDatabase.CreateAsset(distant, Folder + "/DistantCity.mat");
        }
        Material[] weathered = AssetDatabase.LoadAllAssetsAtPath("Assets/_Dev_A/StartingRailYard/Weathering.asset").OfType<Material>().ToArray();
        Material Find(string name) => weathered.Single(material => material.name == name);
        Material warmWindow = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/DimWindows.mat");
        if (warmWindow == null)
        {
            warmWindow = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Dim occupied windows" };
            warmWindow.color = ColorOf(56, 49, 37);
            warmWindow.SetFloat("_Smoothness", 0.16f);
            warmWindow.EnableKeyword("_EMISSION");
            warmWindow.SetColor("_EmissionColor", ColorOf(100, 75, 43).linear * 0.3f);
            AssetDatabase.CreateAsset(warmWindow, Folder + "/DimWindows.mat");
        }
        var root = new GameObject(BackdropName);
        try
        {
            Transform near = Child(root.transform, "Rear service district - full size");
            var mesh = new Geometry();
            var random = new System.Random(11927);
            float R(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            // Beyond the perimeter; boarding markers and the north departure corridor stay unchanged.
            mesh.Box(0, new Vector3(0,-0.60f,-93), new Vector3(236,0.8f,86));
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < 10; i++)
            {
                float x = -98 + i * 21 + R(-2.2f,2.2f);
                float z = -67 - row * 39 + R(-3,3);
                float width = R(12,18), depth = R(13,22);
                int floors = row == 0 ? random.Next(2,6) : random.Next(5,11);
                Building(mesh, x,z,width,depth,floors,i%3==0?0:1,2,3,4,5,random);
            }
            foreach (int side in new[] {-1,1})
            for (int i=0;i<4;i++)
                Building(mesh,side*(64+i%2*19),-41+i*22,14,17,random.Next(4,9),1,2,3,4,5,random);
            for (int i=0;i<3;i++)
            {
                float x=-48+i*46;
                mesh.Box(2,new Vector3(x,3.7f,-52),new Vector3(21,0.35f,5));
                for(int p=-1;p<=1;p+=2)
                    mesh.Box(2,new Vector3(x+p*9,1.6f,-50),new Vector3(0.22f,3.6f,0.22f));
            }
            AddWaterTank(mesh,-72,-70,22,2,3);
            AddWaterTank(mesh,57,-104,29,2,3);
            MeshObject(near,"Warehouses, tenements and service yard",mesh,
                new[] {Find("Concrete"),Find("DarkConcrete"),Find("Steel"),Find("Rust"),Find("Black"),warmWindow},
                Folder+"/RearDistrict.asset",1);

            Transform outdoors = Child(root.transform,"Persistent outdoor background");
            var ground = new Geometry();
            // A continuous base beneath the depot and the repeating route tiles, with no visible holes.
            ground.Box(0,new Vector3(0,-.48f,0),new Vector3(2048,.4f,2048));
            ground.Box(1,new Vector3(0,-.38f,0),new Vector3(10,.24f,2048));
            foreach(int side in new[]{-1,1})
                ground.Box(2,new Vector3(side*9,-.36f,0),new Vector3(8,.24f,2048));
            MeshObject(outdoors,"Continuous trackside ground",ground,
                new[]{Find("Soil"),Find("Asphalt"),Find("Gravel")},
                Folder+"/ContinuousGround.asset",1f);
            BoxCollider groundCollider = outdoors.Find("Continuous trackside ground").gameObject.AddComponent<BoxCollider>();
            groundCollider.center = new Vector3(0,-.48f,0);
            groundCollider.size = new Vector3(2048,.4f,2048);

            Transform sky = Child(outdoors,"3D Skybox - 1 to 32 miniature");
            sky.localScale = Vector3.one * 32f;
            // Miniature source meshes render a distant world with actual 3D parallax.
            // No additional cameras, physics, lights or per-frame geometry are required.
            for (int ring=0;ring<3;ring++)
            {
                var skyline = new Geometry();
                int count = 36 + ring*13;
                float radius = 360 + ring*220;
                for(int i=0;i<count;i++)
                {
                    float angle = (i+R(-0.16f,0.16f))/count*Mathf.PI*2;
                    float x=Mathf.Sin(angle)*radius, z=Mathf.Cos(angle)*radius;
                    float width=R(13,28)*(1+ring*0.3f);
                    float depth=R(12,24)*(1+ring*0.3f);
                    // Keep a broad forward vista; the skyline frames the railway instead of closing it.
                    if (z > 0f && Mathf.Abs(x) - width*.5f < 80f + z*.65f) continue;
                    float height=R(19,62)*(1+ring*0.30f);
                    if(i%11==0)height*=1.7f;
                    Color stone=Color.Lerp(ColorOf(23,30,43),ColorOf(38,46,60),R(0,1));
                    skyline.Box(0,new Vector3(x,height*0.5f-3,z),new Vector3(width,height,depth),stone);
                    skyline.Box(0,new Vector3(x+width*.16f,height-1,z),new Vector3(width*.52f,4,depth*.56f),stone*.88f);
                    if(i%7==0)
                    {
                        skyline.Box(0,new Vector3(x,height+5,z),new Vector3(.48f,12,.48f),stone);
                        skyline.Box(0,new Vector3(x,height+11,z),new Vector3(.9f,.7f,.9f),ColorOf(110,43,35));
                    }
                    for(int face=0;face<4;face++)
                    for(int f=2;f<height/4;f++)
                    for(int bay=0;bay<5;bay++)
                    {
                        if(random.NextDouble()>.095)continue;
                        float off=(bay-2)*width/6f;
                        Vector3 p=face%2==0?new Vector3(x+off,f*4,z+(face==0?-1:1)*(depth*.5f+.04f)):
                            new Vector3(x+(face==1?1:-1)*(width*.5f+.04f),f*4,z+off*depth/width);
                        Vector3 right=face==0?Vector3.right:face==1?Vector3.forward:face==2?Vector3.left:Vector3.back;
                        skyline.Panel(0,p,right,1.3f,1.7f,ColorOf(83,78,62));
                    }
                }
                MeshObject(sky,"Skyline layer "+(ring+1),skyline,new[]{distant},Folder+"/Skyline"+(ring+1)+".asset",1f/32f);
            }
            PrefabUtility.SaveAsPrefabAsset(root,BackdropPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Transform Child(Transform parent,string name)
    {
        var obj=new GameObject(name);obj.transform.SetParent(parent,false);return obj.transform;
    }

    private static void Building(Geometry mesh,float x,float z,float width,float depth,int floors,
        int wall,int band,int roof,int dark,int lit,System.Random random)
    {
        float height=floors*2.9f;
        mesh.Box(wall,new Vector3(x,height*.5f-.2f,z),new Vector3(width,height,depth));
        mesh.Box(roof,new Vector3(x,height,z),new Vector3(width+.45f,.35f,depth+.45f));
        for(int floor=0;floor<floors;floor++)
        {
            float y=floor*2.9f+1.25f;
            foreach(int side in new[]{-1,1})
            {
                mesh.Box(band,new Vector3(x,y+1.35f,z+side*depth*.5f),new Vector3(width,.13f,.12f));
                int bays=Mathf.Max(3,Mathf.FloorToInt(width/2.4f));
                for(int bay=0;bay<bays;bay++)
                {
                    float wx=x-width*.5f+(bay+.5f)*width/bays;
                    int surface=random.NextDouble()<.055?lit:dark;
                    mesh.Panel(surface,new Vector3(wx,y,z+side*(depth*.5f+.035f)),
                        side<0?Vector3.right:Vector3.left,1.18f,1.25f,Color.white);
                    mesh.Box(wall,new Vector3(wx,y-.72f,z+side*(depth*.5f+.10f)),new Vector3(1.42f,.12f,.3f));
                }
                for(int bay=0;bay<Mathf.FloorToInt(depth/3);bay++)
                    mesh.Panel(dark,new Vector3(x+side*(width*.5f+.035f),y,z-depth*.5f+(bay+.5f)*3),
                        side>0?Vector3.forward:Vector3.back,1.1f,1.2f,Color.white);
            }
        }
        mesh.Box(roof,new Vector3(x+width*.22f,height+1.1f,z),new Vector3(width*.28f,2.1f,depth*.35f));
        for(int vent=0;vent<2;vent++)
            mesh.Box(band,new Vector3(x-width*.25f+vent*2,height+1.6f,z-2),new Vector3(.65f,3,.65f));
        mesh.Box(band,new Vector3(x,1.25f,z-depth*.5f-.12f),new Vector3(3.3f,2.7f,.2f));
    }

    private static void AddWaterTank(Geometry mesh,float x,float z,float baseHeight,int steel,int rust)
    {
        for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2)
            mesh.Box(steel,new Vector3(x+a*2,(baseHeight+4)*.5f,z+b*2),new Vector3(.3f,baseHeight+4,.3f));
        mesh.Box(rust,new Vector3(x,baseHeight+5,z),new Vector3(5.3f,3.4f,5.3f));
        mesh.Box(steel,new Vector3(x,baseHeight+6.8f,z),new Vector3(5.7f,.2f,5.7f));
    }

    private static void MeshObject(Transform parent,string name,Geometry geometry,Material[] materials,string path,float vertexScale)
    {
        Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create=mesh==null;
        if(create)mesh=new Mesh();
        geometry.Write(mesh,Path.GetFileNameWithoutExtension(path),vertexScale);
        if(create)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
        Transform obj=Child(parent,name);
        obj.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=obj.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials=materials;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        renderer.receiveShadows=false;
        renderer.lightProbeUsage=LightProbeUsage.Off;
        renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
    }

    private sealed class Geometry
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<Color> colors=new List<Color>();
        private readonly SortedDictionary<int,List<int>> indices=new SortedDictionary<int,List<int>>();

        public void Box(int material,Vector3 center,Vector3 size,Color? tint=null)
        {
            Vector3 h=size*.5f;
            Vector3 P(float x,float y,float z)=>center+Vector3.Scale(h,new Vector3(x,y,z));
            Vector3 a=P(-1,-1,-1),b=P(1,-1,-1),c=P(1,1,-1),d=P(-1,1,-1);
            Vector3 e=P(-1,-1,1),f=P(1,-1,1),g=P(1,1,1),j=P(-1,1,1);
            Face(material,a,d,c,b,tint??Color.white);Face(material,e,f,g,j,tint??Color.white);
            Face(material,a,e,j,d,tint??Color.white);Face(material,b,c,g,f,tint??Color.white);
            Face(material,d,j,g,c,tint??Color.white);Face(material,a,b,f,e,tint??Color.white);
        }

        public void Panel(int material,Vector3 center,Vector3 right,float width,float height,Color color)
        {
            Vector3 a=center-right*width*.5f-Vector3.up*height*.5f;
            Face(material,a,a+Vector3.up*height,a+Vector3.up*height+right*width,a+right*width,color);
        }

        private void Face(int material,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            int first=vertices.Count;
            Vector3 u=(d-a).normalized,v=(b-a).normalized;
            foreach(Vector3 p in new[]{a,b,c,d})
            {
                vertices.Add(p);uv.Add(new Vector2(Vector3.Dot(p,u),Vector3.Dot(p,v))*.29f);colors.Add(color);
            }
            if(!indices.TryGetValue(material,out List<int> triangles))indices.Add(material,triangles=new List<int>());
            triangles.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});
        }

        public void Write(Mesh mesh,string name,float scale)
        {
            mesh.Clear();mesh.name=name;
            mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(vertices.Select(p=>p*scale).ToList());mesh.SetUVs(0,uv);mesh.SetColors(colors);
            mesh.subMeshCount=indices.Count;
            int sub=0;foreach(List<int> triangles in indices.Values)mesh.SetTriangles(triangles,sub++,false);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
    }

    [MenuItem("DeadRail/Starting Rail Yard/Capture environment")]
    public static void CaptureEnvironment()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Exit Play Mode and save your scene edits before capturing.");
        string previous=SceneManager.GetActiveScene().path;
        string folder="Temp/DepotEnvironmentReports";
        Directory.CreateDirectory(folder);
        Scene scene=EditorSceneManager.OpenScene(MainScene);
        Transform depot=scene.GetRootGameObjects().Single(go=>go.name=="RailYard").transform.Find("AbandonedRailDepot");
        var cameraObject=new GameObject("Environment preview camera",typeof(Camera));
        Camera camera=cameraObject.GetComponent<Camera>();
        try
        {
            camera.fieldOfView=64;camera.farClipPlane=1000;camera.nearClipPlane=.1f;
            void Capture(string name,Vector3 localPosition,Vector3 localTarget)
            {
                camera.transform.position=depot.TransformPoint(localPosition);
                camera.transform.LookAt(depot.TransformPoint(localTarget));
                var rt=RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
                RenderTexture old=RenderTexture.active;
                try
                {
                    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                    var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                    File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                finally { camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt); }
            }
            Capture("RearAtSpawn",new Vector3(3,2,4),new Vector3(-14,10,-70));
            Capture("RearDistrict",new Vector3(30,12,14),new Vector3(-10,12,-95));
            Capture("Skyline",new Vector3(-7,11,-8),new Vector3(-160,30,-320));
            // The forward camera must remain continuous while walking and across the old distance cutoff.
            foreach(float z in new[]{10f,55f,110f,170f})
                Capture("ForwardWalk"+z.ToString("000"),new Vector3(3,2,z),new Vector3(0,2,z+180));
            Capture("GroundCoverage",new Vector3(40,60,220),new Vector3(0,-.2f,170));
            StartingRailYard yard = depot.GetComponent<StartingRailYard>();
            foreach(float distance in new[]{0f,119f,120f,121f,349f,350f,351f})
            {
                yard.Present(distance);
                Capture("ForwardTravel"+distance.ToString("000"),new Vector3(3,2,10),new Vector3(0,2,190));
            }
            Physics.SyncTransforms();
            var groundChecks = new List<string>();
            foreach(float z in new[]{80f,130f,170f,260f,400f})
            foreach(float x in new[]{-30f,-9f,9f,30f})
            {
                Vector3 origin=depot.TransformPoint(new Vector3(x,45,z));
                bool hit=Physics.Raycast(origin,Vector3.down,out RaycastHit groundHit,60f);
                if(!hit)throw new InvalidOperationException("Missing ground at "+x+", "+z);
                groundChecks.Add(x+","+z+": "+groundHit.collider.name);
            }
            File.WriteAllLines(folder+"/ground-checks.txt",groundChecks);

        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
            if(!string.IsNullOrEmpty(previous))EditorSceneManager.OpenScene(previous);
        }
        bool openMenu = previous != FrontendMenu.TitleScene && previous != FrontendMenu.LobbyScene;
        if (openMenu) EditorSceneManager.OpenScene(FrontendMenu.TitleScene);
        try { FrontendAuthoring.CapturePages(); }
        finally { if (openMenu && !string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous); }
        Debug.Log("[Depot environment] Environment previews saved to "+folder);
    }
}
