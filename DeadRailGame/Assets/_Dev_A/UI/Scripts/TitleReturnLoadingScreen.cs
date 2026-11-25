using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class TitleReturnLoadingScreen : MonoBehaviour
{
    private static TitleReturnLoadingScreen current;
    private Font font;

    internal static TitleReturnLoadingScreen Show()
    {
        if (current != null) return current;
        var root = new GameObject("Returning to title");
        DontDestroyOnLoad(root);
        current = root.AddComponent<TitleReturnLoadingScreen>();
        return current;
    }

    private void Awake()
    {
        // Fusion removes the player camera before the title scene can render.
        var camera = gameObject.AddComponent<Camera>();
        camera.depth = -100;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.075f, .055f, .047f);
        camera.cullingMask = 0;

        var screen = new GameObject("Loading screen", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
        screen.transform.SetParent(transform, false);
        Canvas canvas = screen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        CanvasScaler scaler = screen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1536, 1024);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        screen.GetComponent<Image>().color = camera.backgroundColor;

        var label = new GameObject("Loading status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(screen.transform, false);
        label.rectTransform.sizeDelta = new Vector2(760, 100);
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR" }, 38);
        label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 38;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(.96f, .92f, .87f);
        label.text = FrontendLanguage.Translate("Returning to title...");
        label.raycastTarget = false;
    }

    internal async Awaitable LoadTitle(int buildIndex)
    {
        try
        {
            await SceneManager.LoadSceneAsync(buildIndex);
            // Keep the cover through the first full title frame, including its canvas rebuild.
            await Awaitable.NextFrameAsync();
            await Awaitable.EndOfFrameAsync();
        }
        finally
        {
            if (this != null) Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        if (font != null) Destroy(font);
    }
}
