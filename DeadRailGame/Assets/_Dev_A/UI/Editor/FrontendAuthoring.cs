using System;
using System.IO;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Imports the supplied artwork and authors editable UGUI, without editing gameplay assets.</summary>
public static class FrontendAuthoring
{
    private const string Folder = "Assets/_Dev_A/UI";
    private const string Art = Folder + "/Artwork/";
    private const string CanvasPrefab = Folder + "/FrontendCanvas.prefab";
    private const string WorldPrefab = Folder + "/MenuDepot.prefab";
    private static Font heading, body;

    [MenuItem("DeadRail/UI/Build supplied frontend")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode first.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Save your current scene changes before authoring the frontend.");
        ImportArtwork();
        heading = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Examples & Extras/Fonts/Oswald-Bold.ttf");
        body = AssetDatabase.LoadAssetAtPath<Font>("Assets/Photon/Fusion/Runtime/RuntimeAssets/Roboto-Regular.ttf");
        Scene title = EditorSceneManager.OpenScene(FrontendMenu.TitleScene);
        Camera camera = title.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>()).Single();
        GameObject world = title.GetRootGameObjects().First(root => root.name == "Title_Background" || root.name == "MenuDepot");
        Transform oldYard = world.transform.Find("RailYard");
        if (oldYard != null) UnityEngine.Object.DestroyImmediate(oldYard.gameObject);
        if (world.transform.Find("AbandonedRailDepot") == null)
        {
            var depot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Dev_A/StartingRailYard/AbandonedRailDepot.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(depot, world.transform);
            instance.transform.localPosition = Vector3.zero;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (MonoBehaviour component in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (component.GetType().Assembly.GetName().Name == "Assembly-CSharp") UnityEngine.Object.DestroyImmediate(component);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
        }
        world.name = "MenuDepot";
        GameObject storedWorld = PrefabUtility.SaveAsPrefabAsset(world, WorldPrefab);
        GameObject ui = BuildCanvas();
        GameObject storedCanvas = PrefabUtility.SaveAsPrefabAsset(ui, CanvasPrefab);
        UnityEngine.Object.DestroyImmediate(ui);
        foreach (GameObject root in title.GetRootGameObjects())
            if (root.GetComponentInChildren<Canvas>(true) != null || root.GetComponent<EventSystem>() != null)
                UnityEngine.Object.DestroyImmediate(root);
        GameObject canvasInstance = (GameObject)PrefabUtility.InstantiatePrefab(storedCanvas, title);
        FrontendMenu menu = canvasInstance.GetComponent<FrontendMenu>();
        menu.backdrop.backgroundCamera = camera;
        menu.SetPage(FrontendPage.Title);
        PrefabUtility.RecordPrefabInstancePropertyModifications(menu.backdrop);
        EditorSceneManager.SaveScene(title);

        // Copy only presentation objects and camera into the lobby; the gameplay scene stays untouched.
        Vector3 cameraPosition = camera.transform.position;
        Quaternion cameraRotation = camera.transform.rotation;
        float fov = camera.fieldOfView;
        Material skybox = RenderSettings.skybox;
        Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
        var ambientMode = RenderSettings.ambientMode;
        Scene lobby = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject lobbyWorld = (GameObject)PrefabUtility.InstantiatePrefab(storedWorld, lobby);
        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camera = cameraObject.GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = equator;
        RenderSettings.ambientGroundColor = ground;
        RenderSettings.sun = lobbyWorld.GetComponentsInChildren<Light>().First(light => light.type == LightType.Directional);
        RenderSettings.fog = false;
        canvasInstance = (GameObject)PrefabUtility.InstantiatePrefab(storedCanvas, lobby);
        menu = canvasInstance.GetComponent<FrontendMenu>();
        menu.backdrop.backgroundCamera = camera;
        menu.SetPage(FrontendPage.Lobby);
        PrefabUtility.RecordPrefabInstancePropertyModifications(menu.backdrop);
        foreach (GameObject panel in new[] { menu.titlePage, menu.lobbyPage, menu.createPage, menu.loadingPage })
            PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
        EditorSceneManager.SaveScene(lobby, FrontendMenu.LobbyScene);

        var existing = EditorBuildSettings.scenes.Where(scene => scene.path != FrontendMenu.TitleScene).ToList();
        existing.Insert(0, new EditorBuildSettingsScene(FrontendMenu.TitleScene, true));
        EditorBuildSettings.scenes = existing.ToArray();
        EditorSceneManager.OpenScene(FrontendMenu.TitleScene);
        Debug.Log("[Frontend] Supplied UI authored. Title → Lobby → Create Room / Join → Loading → game.");
        DepotEnvironmentAuthoring.SynchronizeMenus();
        CapturePages();
    }

    private static void ImportArtwork()
    {
        AssetDatabase.Refresh();
        foreach (string path in Directory.GetFiles(Art, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }

    private static GameObject BuildCanvas()
    {
        var go = new GameObject("Frontend", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1536, 1024);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        FrontendMenu menu = go.AddComponent<FrontendMenu>();
        menu.networking = go.AddComponent<LobbyManager>();
        menu.networking.runnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Dev_A/Prefabs/MyRunner.prefab").GetComponent<NetworkRunner>();
        menu.backdrop = go.AddComponent<FrontendBackdrop>();
        menu.backdrop.blurShader = Shader.Find("DeadRail/UI/Depot Blur");
        RectTransform blur = Stretch(go.transform, "Blurred 3D depot");
        menu.backdrop.blurredView = blur.gameObject.AddComponent<RawImage>();
        menu.backdrop.blurredView.raycastTarget = false;
        blur.gameObject.SetActive(false);
        menu.titlePage = Stretch(go.transform, "Title").gameObject;
        menu.lobbyPage = Stretch(go.transform, "Lobby").gameObject;
        menu.createPage = Stretch(go.transform, "CreateRoom").gameObject;
        menu.loadingPage = Stretch(go.transform, "Loading").gameObject;
        BuildTitle(menu);
        BuildLobby(menu);
        BuildCreate(menu);
        BuildLoading(menu);
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.transform.SetParent(go.transform, false);
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        menu.menuEvents = events.GetComponent<EventSystem>();
        menu.SetPage(FrontendPage.Title);
        return go;
    }

    private static void BuildTitle(FrontendMenu menu)
    {
        Transform page = menu.titlePage.transform;
        CenterTop(Picture(page, "DeadRail logo", "TitleLogo", 327, 103, 920, 224).rectTransform);
        menu.playButton = ArtButton(page, "Play", "TitlePlay", 802, 361, 475, 122);
        Right(menu.playButton.GetComponent<RectTransform>());
        Right(ArtButton(page, "Settings", "TitleSettings", 835, 501, 408, 85).GetComponent<RectTransform>());
        Right(ArtButton(page, "How to play", "TitleHowToPlay", 835, 595, 408, 85).GetComponent<RectTransform>());
        Button credits = ArtButton(page, "Credits", "TitleCredits", 53, 883, 79, 76);
        BottomLeft(credits.GetComponent<RectTransform>());
        BottomLeft(Label(page, "Credits label", "CREDITS", 51, 958, 84, 31, 19, TextAnchor.MiddleCenter).rectTransform);
        RectTransform version = Label(page, "Version", "v" + Application.version, 1409, 963, 95, 32, 23, TextAnchor.MiddleRight).rectTransform;
        version.anchorMin = version.anchorMax = Vector2.one;
        version.anchoredPosition = new Vector2(-127, -963);
        version.anchorMin = version.anchorMax = new Vector2(1, 0);
        version.anchoredPosition = new Vector2(-127, 61);
    }

    private static void SmallLogo(Transform page) => Picture(page, "DeadRail logo", "TitleLogo", 44, 33, 425, 111);

    private static void BuildLobby(FrontendMenu menu)
    {
        Transform page = menu.lobbyPage.transform;
        SmallLogo(page);
        RectTransform panel = Frame(page, "Lobby list", "Lobby", 731, 109, 744, 648, 96);
        Right(panel);
        menu.refreshButton = HitButton(panel, "Refresh rooms", 650, 16, 68, 65);
        Label(panel, "Room column", "ROOM NAME", 49, 107, 225, 31, 23);
        Label(panel, "Players column", "PLAYERS", 287, 107, 105, 31, 23);
        Label(panel, "Map column", "MAP", 420, 107, 75, 31, 23);
        Label(panel, "Ping column", "PING", 526, 107, 70, 31, 23);
        RectTransform viewport = Rect(panel, "Rooms viewport", 24, 145, 688, 477);
        viewport.gameObject.AddComponent<RectMask2D>();
        Image hit = viewport.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.viewport = viewport;
        scroll.scrollSensitivity = 28;
        RectTransform content = Rect(viewport, "Room rows", 0, 0, 688, 0);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        menu.roomContent = content;
        RectTransform barRect = Rect(panel, "Scrollbar", 716, 146, 8, 477);
        Image track = barRect.gameObject.AddComponent<Image>(); track.color = new Color(0, 0, 0, 0.1f);
        Scrollbar bar = barRect.gameObject.AddComponent<Scrollbar>();
        Image handle = Solid(barRect, "Handle", 0, 0, 8, 130, new Color(0.72f, 0.69f, 0.64f));
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        menu.lobbyStatus = Label(panel, "Lobby status", "No rooms found. Create a room to begin.", 30, 250, 680, 100, 25, TextAnchor.MiddleCenter);
        menu.rowTemplate = BuildRow(panel);
        menu.createButton = ArtButton(page, "Create room", "LobbyCreate", 731, 773, 363, 99);
        menu.joinButton = ArtButton(page, "Join room", "LobbyJoin", 1111, 773, 362, 99);
        Right(menu.createButton.GetComponent<RectTransform>());
        Right(menu.joinButton.GetComponent<RectTransform>());
        menu.joinButton.interactable = false;
        menu.lobbyBackButton = ArtButton(page, "Back to title", "LobbyBack", 47, 920, 139, 62);
        BottomLeft(menu.lobbyBackButton.GetComponent<RectTransform>());
    }

    private static FrontendRoomRow BuildRow(Transform parent)
    {
        RectTransform root = Rect(parent, "Room row template", 0, 0, 688, 59);
        root.gameObject.AddComponent<LayoutElement>().preferredHeight = 59;
        FrontendRoomRow row = root.gameObject.AddComponent<FrontendRoomRow>();
        row.highlight = root.gameObject.AddComponent<Image>();
        row.highlight.color = new Color(1, 1, 1, 0.035f);
        row.selectButton = root.gameObject.AddComponent<Button>();
        row.selectButton.targetGraphic = row.highlight;
        Picture(root, "Players icon", "UsersIcon", 14, 15, 36, 29);
        row.roomName = Label(root, "Name", "", 64, 10, 198, 40, 25);
        row.roomName.font = body;
        row.players = Label(root, "Players", "", 269, 10, 90, 40, 25, TextAnchor.MiddleCenter);
        row.map = Label(root, "Map", "", 371, 10, 106, 40, 25, TextAnchor.MiddleCenter);
        row.ping = Label(root, "Ping unavailable", "--", 499, 10, 50, 40, 23, TextAnchor.MiddleCenter);
        row.joinButton = ArtButton(root, "Join", "RowJoin", 565, 7, 104, 44);
        Solid(root, "Separator", 0, 58, 688, 1, new Color(0.76f, 0.70f, 0.62f, 0.13f));
        root.gameObject.SetActive(false);
        return row;
    }

    private static void BuildCreate(FrontendMenu menu)
    {
        Transform page = menu.createPage.transform;
        SmallLogo(page);
        RectTransform panel = Frame(page, "Create room settings", "Create", 731, 109, 744, 651, 96);
        Right(panel);
        menu.resetButton = HitButton(panel, "Reset room settings", 650, 16, 68, 65);
        Label(panel, "Room name label", "ROOM NAME", 42, 129, 500, 47, 31);
        Image field = Picture(panel, "Room name", "RoomInput", 44, 184, 658, 70);
        field.raycastTarget = true;
        menu.roomNameInput = field.gameObject.AddComponent<InputField>();
        Text input = Label(field.transform, "Text", "", 20, 8, 613, 54, 28);
        input.font = body;
        input.supportRichText = false;
        Text placeholder = Label(field.transform, "Placeholder", "Enter room name...", 20, 8, 613, 54, 28);
        placeholder.color = new Color(0.61f, 0.59f, 0.55f);
        placeholder.font = body;
        menu.roomNameInput.textComponent = input;
        menu.roomNameInput.placeholder = placeholder;
        menu.roomNameInput.targetGraphic = field;
        menu.roomNameInput.characterLimit = 32;
        menu.roomNameInput.lineType = InputField.LineType.SingleLine;
        Label(panel, "Player count label", "PLAYER COUNT", 42, 294, 500, 48, 31);
        menu.minusButton = ArtButton(panel, "Fewer players", "CountMinus", 153, 352, 84, 81);
        Picture(panel, "Player count frame", "CountValue", 259, 352, 221, 81);
        menu.playerCountText = Label(panel, "Player count", "4", 259, 352, 221, 81, 43, TextAnchor.MiddleCenter);
        menu.plusButton = ArtButton(panel, "More players", "CountPlus", 502, 352, 84, 81);
        menu.createStatus = Label(panel, "Create status", "2 or 4 players", 44, 444, 654, 54, 26, TextAnchor.MiddleCenter);
        menu.createStatus.color = new Color(0.65f, 0.61f, 0.55f);
        menu.submitButton = ArtButton(panel, "Confirm create room", "CreateSubmit", 33, 525, 680, 107);
        menu.createBackButton = ArtButton(page, "Back to lobby", "CreateBack", 47, 920, 139, 62);
        BottomLeft(menu.createBackButton.GetComponent<RectTransform>());
    }

    private static void BuildLoading(FrontendMenu menu)
    {
        Transform page = menu.loadingPage.transform;
        SmallLogo(page);
        RectTransform panel = Frame(page, "Joining room", "Loading", 348, 232, 841, 418, 98);
        CenterTop(panel);
        menu.loadingStatus = Label(panel, "Connection status", "Connecting to server...", 30, 155, 781, 61, 36, TextAnchor.MiddleCenter);
        Picture(panel, "Progress track", "ProgressTrack", 43, 238, 754, 60);
        Image fill = Picture(panel, "Progress fill", "ProgressFill", 48, 244, 744, 48);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0;
        menu.loadingFill = fill;
        menu.loadingDetail = Label(panel, "Loading detail", "Preparing game...", 30, 314, 781, 57, 28, TextAnchor.MiddleCenter);
        menu.cancelButton = ArtButton(page, "Cancel connection", "LoadingCancel", 545, 678, 448, 109);
        CenterTop(menu.cancelButton.GetComponent<RectTransform>());
    }

    private static RectTransform Frame(Transform parent, string name, string prefix, float x, float y, float w, float h, float header)
    {
        RectTransform root = Rect(parent, name, x, y, w, h);
        Solid(root, "Tinted panel", 3, header - 1, w - 6, h - header - 2, new Color(0.025f, 0.02f, 0.018f, 0.88f));
        Picture(root, "Header", prefix + "Header", 0, 0, w, header);
        Picture(root, "Left border", prefix + "EdgeLeft", 0, header, 7, h - header);
        Picture(root, "Right border", prefix + "EdgeRight", w - 7, header, 7, h - header);
        Picture(root, "Bottom border", prefix + "EdgeBottom", 0, h - 7, w, 7);
        return root;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rect = Rect(parent, name, 0, 0, 0, 0);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void Right(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.anchoredPosition -= Vector2.right * 1536;
    }

    private static void CenterTop(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.anchoredPosition -= Vector2.right * 768;
    }

    private static void BottomLeft(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.anchoredPosition += Vector2.up * 1024;
    }

    private static Image Picture(Transform parent, string name, string sprite, float x, float y, float w, float h)
    {
        Image image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + sprite + ".png");
        if (image.sprite == null) throw new InvalidOperationException("Missing sprite " + sprite);
        image.raycastTarget = false;
        return image;
    }

    private static Image Solid(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        Image image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Button ArtButton(Transform parent, string name, string sprite, float x, float y, float w, float h)
    {
        Image image = Picture(parent, name, sprite, x, y, w, h);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.highlightedColor = new Color(1.16f, 1.1f, 0.99f);
        colors.pressedColor = new Color(0.72f, 0.67f, 0.62f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        return button;
    }

    private static Button HitButton(Transform parent, string name, float x, float y, float w, float h)
    {
        Image image = Solid(parent, name, x, y, w, h, Color.clear);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    private static Text Label(Transform parent, string name, string text, float x, float y, float w, float h,
        int size, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        Text label = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
        label.font = heading;
        label.text = text;
        label.fontSize = size;
        label.color = new Color32(201, 193, 180, 255);
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        label.supportRichText = false;
        return label;
    }

    [MenuItem("DeadRail/UI/Capture supplied screens")]
    public static void CapturePages()
    {
        FrontendMenu menu = UnityEngine.Object.FindFirstObjectByType<FrontendMenu>();
        if (menu == null) throw new InvalidOperationException("Open a frontend scene first.");
        Directory.CreateDirectory("Temp/DeadRailUIReports");
        FrontendPage previous = menu.Page;
        Canvas canvas = menu.GetComponent<Canvas>();
        Camera camera = menu.backdrop.backgroundCamera;
        RenderTexture previousTarget = camera.targetTexture;
        float aspect = camera.aspect;
        RenderTexture previousActive = RenderTexture.active;
        var target = RenderTexture.GetTemporary(1536, 1024, 24);
        var background = RenderTexture.GetTemporary(1536, 1024, 24);
        RawImage blur = menu.backdrop.blurredView;
        Texture oldBlurTexture = blur.texture;
        Material oldBlurMaterial = blur.material;
        var blurMaterial = new Material(menu.backdrop.blurShader);
        blurMaterial.SetFloat("_Radius", 4f);
        float oldScale = canvas.scaleFactor;
        Texture2D output = new Texture2D(1536, 1024, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = 1.5f;
            canvas.enabled = false;
            camera.targetTexture = background;
            camera.Render();
            canvas.enabled = true;
            blur.texture = background;
            blur.material = blurMaterial;
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 0.4f;
            foreach (FrontendPage page in Enum.GetValues(typeof(FrontendPage)))
            {
                menu.SetPage(page);
                blur.gameObject.SetActive(page != FrontendPage.Title);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                output.ReadPixels(new Rect(0, 0, 1536, 1024), 0, 0);
                output.Apply();
                File.WriteAllBytes("Temp/DeadRailUIReports/" + page + ".png", output.EncodeToPNG());
            }
        }
        finally
        {
            menu.SetPage(previous);
            blur.gameObject.SetActive(previous != FrontendPage.Title && Application.isPlaying);
            blur.texture = oldBlurTexture;
            blur.material = oldBlurMaterial;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            canvas.scaleFactor = oldScale;
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = previousTarget;
            camera.aspect = aspect;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(blurMaterial);
            RenderTexture.ReleaseTemporary(target);
            RenderTexture.ReleaseTemporary(background);
        }
        Debug.Log("[Frontend] Four UI captures saved to Temp/DeadRailUIReports.");
    }
}
