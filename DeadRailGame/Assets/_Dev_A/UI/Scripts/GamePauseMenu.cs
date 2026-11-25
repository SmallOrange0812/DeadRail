using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static FrontendLanguage;

[DefaultExecutionOrder(-100)]
public sealed class GamePauseMenu : MonoBehaviour
{
    private enum Page { Closed, Menu, Settings, ConfirmLeave, Leaving }

    private static readonly Color Panel = new Color(.075f, .055f, .047f, .98f);
    private static readonly Color Surface = new Color(.18f, .14f, .12f);
    private static readonly Color Red = new Color(.62f, .095f, .08f);
    private static GamePauseMenu instance;
    internal static bool BlocksGameplayInput =>
        instance != null && (instance.page != Page.Closed || Time.frameCount == instance.closedFrame);

    private NetworkManager network;
    private Page page;
    private int closedFrame = -1;
    private GameObject overlay, main, confirmation, leaving;
    private Button continueButton, cancelLeaveButton;
    private Text leaveError;
    private Font font;
    private FrontendLanguage language;
    private FrontendAdditionalPages settings;

    internal void Initialize(NetworkManager manager)
    {
        network = manager;
        instance = this;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    private void HandleEscape()
    {
        if (page == Page.Leaving) return;
        switch (page)
        {
            case Page.Closed:
                var terminal = TerminalUIManager.Instance;
                if (terminal != null && terminal.IsUiOpen)
                {
                    terminal.CloseAllTerminals();
                    return;
                }
                if (ManualCrankGenerator.TryCloseLocalMinigame()) return;
                if (network == null || !network.HasRunningSession) return;
                if (overlay == null) BuildMenu();
                Show(Page.Menu);
                break;
            case Page.Menu:
                Show(Page.Closed);
                break;
            default:
                Show(Page.Menu);
                break;
        }
    }

    private void BuildMenu()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR" }, 28);
        language = new FrontendLanguage(font);
        overlay = new GameObject("Game pause overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        overlay.transform.SetParent(transform, false);
        overlay.SetActive(false);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1536, 1024);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var dimmer = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
        dimmer.transform.SetParent(overlay.transform, false);
        Stretch(dimmer.GetComponent<RectTransform>());
        dimmer.GetComponent<Image>().color = new Color(0f, 0f, 0f, .68f);

        RectTransform menu = MakePanel("Pause menu", 420, 330);
        main = menu.gameObject;
        AddLabel(menu, "Pause menu", 24, 23, 372, 52, 34);
        continueButton = AddButton(menu, "Continue", 30, 91, 360, 59, Red, () => Show(Page.Closed));
        AddButton(menu, "Settings", 30, 163, 360, 59, Surface, OpenSettings);
        AddButton(menu, "Leave game", 30, 235, 360, 59, Surface, () =>
        {
            leaveError.gameObject.SetActive(false);
            Show(Page.ConfirmLeave);
        });

        RectTransform confirm = MakePanel("Confirm leaving", 530, 300);
        confirmation = confirm.gameObject;
        AddLabel(confirm, "Leave the game?", 22, 24, 486, 49, 31);
        AddLabel(confirm, network.IsHostingMultiplayer ? "Leaving as host ends the session for everyone." :
            "You will leave the current session and return to the title.", 28, 83, 474, 85, 22);
        cancelLeaveButton = AddButton(confirm, "Cancel", 30, 181, 225, 56, Surface, () => Show(Page.Menu));
        AddButton(confirm, "Leave game", 275, 181, 225, 56, Red, ConfirmLeave);
        leaveError = AddLabel(confirm, "Could not leave the session. Please try again.", 18, 247, 494, 36, 19);
        leaveError.color = new Color(1f, .59f, .49f);
        leaveError.gameObject.SetActive(false);

        RectTransform closing = MakePanel("Leaving", 420, 128);
        leaving = closing.gameObject;
        AddLabel(closing, "Leaving game...", 20, 35, 380, 58, 28);
    }

    private void OpenSettings()
    {
        if (settings == null)
            settings = new FrontendAdditionalPages(overlay.transform, language, font, () => Show(Page.Menu), ChangeLanguage);
        Show(Page.Settings);
    }

    private void ChangeLanguage(int choice)
    {
        if (Korean == (choice == 1)) return;
        Set(choice);
        language.Refresh();
        StartCoroutine(RefreshSettings());
    }

    private IEnumerator RefreshSettings()
    {
        yield return null;
        if (page == Page.Settings) settings.Show(FrontendPage.Settings);
    }

    private async void ConfirmLeave()
    {
        if (page != Page.ConfirmLeave) return;
        Show(Page.Leaving);
        bool left = await network.LeaveCurrentGame();
        if (this == null || left) return;
        leaveError.gameObject.SetActive(true);
        Show(Page.ConfirmLeave);
    }

    private void Show(Page next)
    {
        page = next;
        bool open = next != Page.Closed;
        if (!open) closedFrame = Time.frameCount;
        main.SetActive(next == Page.Menu);
        confirmation.SetActive(next == Page.ConfirmLeave);
        leaving.SetActive(next == Page.Leaving);
        overlay.SetActive(open);
        settings?.Show(next == Page.Settings ? FrontendPage.Settings : FrontendPage.Title);
        bool resultVisible = RailRouteLoop.Instance != null && RailRouteLoop.Instance.HasRunResult;
        Cursor.lockState = open || resultVisible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open || resultVisible;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(next == Page.Menu ? continueButton.gameObject :
                next == Page.ConfirmLeave ? cancelLeaveButton.gameObject : null);
    }

    private RectTransform MakePanel(string name, float width, float height)
    {
        RectTransform panel = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        panel.SetParent(overlay.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(width, height);
        panel.GetComponent<Image>().color = Panel;
        Outline frame = panel.gameObject.AddComponent<Outline>();
        frame.effectColor = new Color(.64f, .50f, .41f);
        frame.effectDistance = new Vector2(1f, 1f);
        return panel;
    }

    private Button AddButton(Transform parent, string label, float x, float y, float w, float h, Color color, Action action)
    {
        RectTransform rect = Rect(parent, label + " button", x, y, w, h);
        Image face = rect.gameObject.AddComponent<Image>();
        face.color = color;
        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.onClick.AddListener(() => action());
        AddLabel(rect, label, 8, 0, w - 16, h, 25);
        return button;
    }

    private Text AddLabel(Transform parent, string label, float x, float y, float w, float h, int size)
    {
        Text text = Rect(parent, label, x, y, w, h).gameObject.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(.96f, .92f, .87f);
        text.raycastTarget = false;
        language.Bind(text, label);
        return text;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        settings?.ReleaseArtworkMaterials();
        if (font != null) Destroy(font);
    }
}
