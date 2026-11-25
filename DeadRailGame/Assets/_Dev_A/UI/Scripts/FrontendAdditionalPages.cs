using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using static FrontendLanguage;

internal sealed class FrontendAdditionalPages
{
    // Screen coordinates are measured against the supplied 1536 × 1024 artwork.
    private static readonly Color Panel = new Color(0.045f, 0.036f, 0.033f, 0.88f);
    private static readonly Color Surface = new Color(0.095f, 0.083f, 0.075f, 0.94f);
    private static readonly Color Red = new Color(0.61f, 0.09f, 0.08f, 0.97f);
    private static readonly Color SelectedCard = new Color(0.20f, 0.033f, 0.026f, 0.94f);
    private static readonly Color Ink = new Color(0.96f, 0.92f, 0.87f);
    private static readonly int[] CardPositions = { 128, 458, 785, 1117 };
    private const string Preferences = "DeadRail.Frontend.";
    private const int DefaultFrameRate = 60;
    private readonly FrontendMenu menu;
    private readonly Font font;
    private readonly Transform parent;
    private readonly FrontendLanguage language;
    private readonly FrontendBackdrop backdrop;
    private readonly Action<int> changeLanguage;
    private readonly GameObject roomPage, settingsPage;
    private GameObject guidePage, creditsPage;
    internal Button GuideBack { get; private set; }
    internal Button CreditsBack { get; private set; }
    private readonly RectTransform settings;
    private readonly Text roomName, roomCount, chatHistory, roomStatus;
    private readonly Text[] slots = new Text[4];
    private readonly Text[] slotRoles = new Text[4];
    private readonly RawImage[][] slotArtwork = new RawImage[4][];
    private readonly Button[] slotButtons = new Button[4];
    private int roomCardLimit;
    private readonly Button startMatch;
    private readonly InputField chat;
    private readonly ScrollRect chatScroll;
    private readonly Queue<string> messages = new Queue<string>();
    private readonly Slider roomVolume;
    private readonly Text roomVolumePercent, roomMute;
    private readonly RawImage[] tabArtwork = new RawImage[4];
    private readonly Material neutralArtwork;
    private readonly Material selectedTabArtwork;
    private readonly Material selectedCardArtwork;
    private RectTransform settingsContent;
    private int settingsTab;
    private float volume;
    private bool muted;
    private bool highContrast;
    private bool largerText;
    private float nextRosterRefresh;
    private float blurRadius;

    internal FrontendAdditionalPages(FrontendMenu menu, Font font)
        : this(menu.transform, menu.Language, font, () => menu.SetPage(FrontendPage.Title), menu.ChangeLanguage,
            menu.backdrop, menu.gameObject.scene.path == FrontendMenu.TitleScene)
    {
        this.menu = menu;
        if (neutralArtwork != null) selectedCardArtwork = MakeSelectedArtwork(neutralArtwork.shader, true);
        AddLogo(settings);
        roomPage = CreatePage("MainLobby", out RectTransform room);
        AddLogo(room);
        Text lobbyTitle = Label(room, "Lobby title", 565, 20, 404, 64, 52, TextAnchor.MiddleCenter);
        lobbyTitle.fontStyle = FontStyle.Bold;
        menu.Language.Bind(lobbyTitle, "LOBBY");
        roomStatus = Label(room, "Room status", 571, 87, 390, 40, 25, TextAnchor.MiddleCenter);
        roomStatus.gameObject.AddComponent<Shadow>().effectColor = Color.black;
        RectTransform roomInfo = ArtworkPanel(room, "Room information", "RoomInfo_UI", 1135, 18, 353, 102);
        RawImage roomLabels = ArtworkSlice(roomInfo, "Labels", "RoomInfo_UI", new Rect(12, 9, 333, 43));
        menu.Language.BindArtwork(roomLabels, "ROOM", new Rect(.028f, .18f, .25f, .7f), .36f, 22);
        menu.Language.BindArtwork(roomLabels, "PLAYERS", new Rect(.63f, .18f, .34f, .7f), .93f, 22);
        Box(roomInfo, "Room info divider", 207, 48, 1, 35, new Color(1f, 1f, 1f, 0.22f));
        roomName = Label(room, "Room name", 1159, 67, 173, 39, 27);
        roomName.resizeTextForBestFit = true;
        roomName.resizeTextMinSize = 15;
        roomName.resizeTextMaxSize = 27;
        roomCount = Label(room, "Player count", 1360, 67, 114, 39, 27);
        for (int i = 0; i < slots.Length; i++)
        {
            string asset = $"Player_{i + 1}";
            RectTransform slot = ArtworkPanel(room, $"Player {i + 1} card", asset, CardPositions[i], 170, 293, 254);
            Texture2D card = LoadArtwork(asset);
            if (card != null)
            {
                // Sample the blank side of the header so the exported PLAYER label is omitted.
                PlaceArtwork(Rect(slot, "Header artwork", 6, 6, 281, 52), card, new Rect(8, 6, 70, 53));
                ArtworkSlice(slot, "Player portrait", asset, new Rect(6, 59, card.width - 12, 139));
            }
            slotArtwork[i] = slot.GetComponentsInChildren<RawImage>(true);
            foreach (RawImage piece in slotArtwork[i]) piece.material = neutralArtwork;
            int cardNumber = i + 1;
            slotButtons[i] = ConnectButton(slot.GetComponent<Image>(), () => menu.networking.SelectRoleCard(cardNumber));
            slotRoles[i] = Label(slot, "Role", 6, 6, 281, 52, 27, TextAnchor.MiddleCenter);
            slotRoles[i].fontStyle = FontStyle.Bold;
            slots[i] = Label(slot, "Status", 20, 198, 253, 44, 25, TextAnchor.MiddleCenter);
        }
        menu.Language.Bind(Label(room, "Seat selection help", 320, 442, 896, 40, 22, TextAnchor.MiddleCenter),
            "Click a free seat to select its role");
        RectTransform chatPanel = ArtworkPanel(room, "Chat panel", "Chat_UI", 49, 536, 815, 362);
        menu.Language.BindArtwork(ArtworkSlice(chatPanel, "Chat header", "Chat_UI", new Rect(6, 6, 808, 48)),
            "CHAT", new Rect(.012f, .08f, .13f, .83f), .26f, 26);
        RectTransform transcript = Box(room, "Chat transcript", 57, 593, 793, 209, Color.clear).rectTransform;
        transcript.GetComponent<Image>().raycastTarget = true;
        chatScroll = transcript.gameObject.AddComponent<ScrollRect>();
        chatScroll.horizontal = false;
        chatScroll.movementType = ScrollRect.MovementType.Clamped;
        RectTransform chatViewport = Rect(transcript, "Chat viewport", 8, 4, 776, 197);
        chatViewport.gameObject.AddComponent<RectMask2D>();
        chatHistory = Label(chatViewport, "Messages", 0, 0, 776, 0, 22, TextAnchor.UpperLeft);
        chatHistory.rectTransform.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        chatScroll.viewport = chatViewport;
        chatScroll.content = chatHistory.rectTransform;
        FramedBox(room, "Message field", 62, 813, 684, 67, Surface);
        RectTransform field = Rect(room, "Type a message", 67, 814, 674, 65);
        Image fieldHit = field.gameObject.AddComponent<Image>();
        fieldHit.color = new Color(1, 1, 1, 0);
        chat = field.gameObject.AddComponent<InputField>();
        chat.targetGraphic = fieldHit;
        chat.characterLimit = 120;
        chat.textComponent = Label(field, "Message", 10, 0, 654, 65, 24);
        chat.placeholder = Label(field, "Placeholder", 10, 0, 654, 65, 24);
        menu.Language.Bind((Text)chat.placeholder, "Type a message...");
        chat.placeholder.color = new Color(0.72f, 0.68f, 0.63f);
        chat.onEndEdit.AddListener(_ =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SendMessage();
        });
        ArtworkButton(room, "Send chat message", "Chat_UI", 757, 813, 91, 67, SendMessage, new Rect(709, 278, 92, 69));
        Button leaveRoom = ArtworkButton(room, "Leave room", "Leave_BT", 49, 908, 231, 74, menu.LeaveRoom);
        menu.Language.BindArtwork(leaveRoom.targetGraphic, "Leave room", new Rect(.30f, .18f, .65f, .66f), .95f, 28);
        startMatch = ArtworkButton(room, "Start game", "Start_BT", 877, 893, 612, 91, menu.StartMatch);
        menu.Language.BindArtwork(startMatch.targetGraphic, "Start game", new Rect(.39f, .13f, .53f, .72f), .94f, 43);

        RectTransform audioPanel = ArtworkPanel(room, "Audio panel", "Audio_UI", 877, 536, 612, 158);
        menu.Language.BindArtwork(ArtworkSlice(audioPanel, "Audio title and icon", "Audio_UI", new Rect(12, 11, 161, 64)),
            "Audio", new Rect(.44f, .19f, .50f, .72f), .96f, 24);
        ArtworkSlice(audioPanel, "Output device frame", "Audio_UI", new Rect(18, 85, 578, 57));
        roomVolume = Range(room, "Audio volume", 1061, 566, 220, 29, 0f, 1f, volume, ChangeVolume);
        roomVolumePercent = Label(room, "Volume percent", 1288, 555, 57, 50, 24);
        roomMute = DrawButton(room, "Mute audio", 1364, 559, 108, 43, "ON", Surface, 23, ToggleMute)
            .GetComponentInChildren<Text>(true);
        Box(room, "Output device value background", 952, 628, 507, 41, Surface);
        menu.Language.Bind(Label(room, "Output device", 960, 624, 495, 48, 23), "System default output");
        RectTransform micPanel = ArtworkPanel(room, "Microphone panel", "Mic_UI", 877, 701, 612, 183);
        menu.Language.BindArtwork(ArtworkSlice(micPanel, "Microphone title and icon", "Mic_UI", new Rect(12, 12, 205, 65)),
            "Microphone", new Rect(.29f, .18f, .66f, .71f), .97f, 24);
        ArtworkSlice(micPanel, "Microphone device frame", "Mic_UI", new Rect(18, 94, 578, 63));
        menu.Language.Bind(Label(room, "Microphone state", 1095, 722, 365, 53, 23, TextAnchor.MiddleRight),
            "Voice chat unavailable");
        Box(room, "Microphone device value background", 952, 803, 507, 42, Surface);
        menu.Language.Bind(Label(room, "Microphone device", 960, 798, 495, 50, 23), "Microphone inactive");
        UpdateRoomAudio();
    }

    internal FrontendAdditionalPages(Transform parent, FrontendLanguage language, Font font, Action backToMenu,
        Action<int> changeLanguage, FrontendBackdrop backdrop = null, bool restoreDisplay = false)
    {
        this.parent = parent;
        this.language = language;
        this.changeLanguage = changeLanguage;
        this.backdrop = backdrop;
        this.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Shader selectionShader = Resources.Load<Shader>("DeadRailFrontend/SelectionArtwork");
        if (selectionShader != null)
        {
            neutralArtwork = new Material(selectionShader);
            selectedTabArtwork = MakeSelectedArtwork(selectionShader, false);
        }
        volume = PlayerPrefs.GetFloat(Preferences + "Volume", 0.75f);
        muted = PlayerPrefs.GetInt(Preferences + "Muted", 0) != 0;
        highContrast = PlayerPrefs.GetInt(Preferences + "Contrast", 0) != 0;
        largerText = PlayerPrefs.GetInt(Preferences + "LargeText", 0) != 0;
        blurRadius = PlayerPrefs.GetFloat(Preferences + "MenuBlur", 4f);
        backdrop?.SetBlurRadius(blurRadius);
        ApplyAudio();
        int quality = PlayerPrefs.GetInt(Preferences + "Quality", QualitySettings.GetQualityLevel());
        if (quality >= 0 && quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(quality);
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(Preferences + "VSync", QualitySettings.vSyncCount);
        Application.targetFrameRate = PlayerPrefs.GetInt(Preferences + "FrameRate", DefaultFrameRate);
        Application.runInBackground = PlayerPrefs.GetInt(Preferences + "Background", Application.runInBackground ? 1 : 0) != 0;
        if (!Application.isEditor && restoreDisplay &&
            PlayerPrefs.HasKey(Preferences + "DisplayMode"))
        {
            var displayMode = (FullScreenMode)PlayerPrefs.GetInt(Preferences + "DisplayMode");
            int width = PlayerPrefs.GetInt(Preferences + "Width", Screen.width);
            int height = PlayerPrefs.GetInt(Preferences + "Height", Screen.height);
            Screen.SetResolution(width, height, displayMode);
        }

        settingsPage = CreatePage("Settings", out settings);
        RectTransform settingsPanel = ArtworkPanel(settings, "Settings panel", "Settings_UI", 594, 109, 875, 644);
        language.BindArtwork(ArtworkSlice(settingsPanel, "Settings header", "Settings_UI", new Rect(6, 6, 867, 89)),
            "Settings", new Rect(.02f, .10f, .32f, .75f), .47f, 49);
        ArtworkButton(settings, "Reset settings", "Settings_UI", 1380, 126, 66, 60, ResetSettings,
            new Rect(789, 18, 66, 60));
        Button back = ArtworkButton(settings, "Back to title", "Back_BT", 48, 921, 137, 60,
            backToMenu);
        language.BindArtwork(back.targetGraphic, "Back", new Rect(.39f, .14f, .50f, .7f), .91f, 26);
        RectTransform tabFrame = ArtworkPanel(settings, "Settings tabs background", "ButtonBack_UI", 594, 751, 875, 162);
        foreach (RawImage piece in tabFrame.GetComponentsInChildren<RawImage>(true)) piece.material = neutralArtwork;
        int[] tabX = { 606, 815, 1025, 1239 };
        int[] tabWidth = { 194, 194, 198, 216 };
        string[] tabNames = { "Graphics / Display", "Audio", "Controls", "Accessibility" };
        string[] tabAssets = { "GraphicsDisplay_BT", "Audio_BT", "Controls_BT", "Access_BT" };
        for (int i = 0; i < tabNames.Length; i++)
        {
            int tab = i;
            Button tabButton = ArtworkButton(settings, tabNames[i], tabAssets[i], tabX[i], 761, tabWidth[i], 139,
                () => BuildSettings(tab));
            tabArtwork[i] = tabButton.targetGraphic as RawImage;
            if (tabArtwork[i] != null) tabArtwork[i].material = neutralArtwork;
            language.BindArtwork(tabButton.targetGraphic, tabNames[i], new Rect(.045f, .51f, .91f, .37f), .94f, 23);
        }
    }

    internal void Show(FrontendPage page)
    {
        if (roomPage != null) roomPage.SetActive(page == FrontendPage.Room);
        settingsPage.SetActive(page == FrontendPage.Settings);
        if (page == FrontendPage.Settings) BuildSettings(settingsTab);
        if (page == FrontendPage.HowToPlay && guidePage == null) BuildGuide();
        if (guidePage != null) guidePage.SetActive(page == FrontendPage.HowToPlay);
        if (page == FrontendPage.Credits && creditsPage == null) BuildCredits();
        if (creditsPage != null) creditsPage.SetActive(page == FrontendPage.Credits);
    }

    private RectTransform InformationPanel(RectTransform frame, string title, float width, float height)
    {
        RectTransform panel = ArtworkPanel(frame, title, "Settings_UI", 0, 0, width, height);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;

        Box(panel, "Header stripe", 8, 8, 8, 94, Red);
        GuideText(panel, title, 40, 12, width - 90, 80, 48, true);
        Box(panel, "Header rule", 40, 101, width - 80, 2, Surface);
        return panel;
    }

    private void BuildCredits()
    {
        creditsPage = CreatePage("Credits", out RectTransform frame);
        RectTransform panel = InformationPanel(frame, "CREDITS", 1100, 560);
        CreditRow(panel, 133, "20241257_안성훈", "Developer A  /  Network, systems & UI",
            "Photon matchmaking, P1/P4 terminal and radar UI, blackout transitions and resource consumption.");
        CreditRow(panel, 299, "20241233_박준혁", "Developer B  /  Physics, action & animation",
            "Scrolling track, P2/P3 hitscan and harpoon, Animator and IK setup, battery pickup and drop physics.");
        CreditsBack = InformationBack(panel, 920, 474);
    }

    private void CreditRow(RectTransform panel, float y, string name, string role, string detail)
    {
        RectTransform row = FramedBox(panel, name, 40, y, 1020, 143, Surface).rectTransform;
        GuideText(row, name, 24, 34, 325, 60, 32, true);
        Box(row, "Divider", 362, 20, 2, 103, Panel);
        GuideText(row, role, 391, 16, 609, 44, 25, true);
        GuideText(row, detail, 391, 64, 599, 75, 21);
    }

    private void BuildGuide()
    {
        guidePage = CreatePage("How to play", out RectTransform frame);
        RectTransform panel = InformationPanel(frame, "How to play", 1300, 820);
        GuideText(panel, "Keep the train running", 40, 120, 696, 54, 32, true);
        GuideText(panel, "Choose a role in the lobby. Check the routes on the radar and keep the train supplied with fuel and power between stops.",
            40, 175, 676, 100, 23);

        GuideStep(panel, 282, "Approach the junction",
            "Read the speed sign with the searchlight. The driver matches that speed while the gunner hits the track switch with the roof harpoon to select a branch.");
        GuideStep(panel, 440, "Supply stop",
            "At the first stop, press F to pick up the fuel can. This brings zombies to the train. Guard the rear cargo door while the carrier steps aboard and presses F there to load it.");
        GuideStep(panel, 598, "Get everyone aboard",
            "Press F near a downed crewmate to revive them. If everyone goes down, the run ends. After the defense, regroup on the train so the driver can depart.");

        RectTransform controls = FramedBox(panel, "Controls panel", 755, 125, 505, 588, Surface).rectTransform;
        GuideText(controls, "Controls", 24, 10, 455, 54, 32, true);
        GuideControl(controls, 0, "W A S D / Mouse", "Move / look");
        GuideControl(controls, 1, "Shift / Space", "Sprint / jump");
        GuideControl(controls, 2, "F", "Interact");
        GuideControl(controls, 3, "1 / 2", "Equip weapon / flashlight");
        GuideControl(controls, 4, "Left click", "Fire");
        GuideControl(controls, 5, "R", "Reload / console rescan");
        GuideControl(controls, 6, "Z", "Toggle zoom");
        GuideControl(controls, 7, "Tab", "Inventory");
        GuideControl(controls, 8, "Q / G", "Drop / install battery");
        GuideControl(controls, 9, "Esc", "Close console / pause");

        GuideBack = InformationBack(panel, 1120, 734);
    }

    private Button InformationBack(RectTransform panel, float x, float y)
    {
        Button back = ArtworkButton(panel, "Back", "Back_BT", x, y, 140, 60, () => menu.SetPage(FrontendPage.Title));
        language.BindArtwork(back.targetGraphic, "Back", new Rect(.39f, .14f, .50f, .7f), .91f, 26);
        return back;
    }

    private void GuideStep(RectTransform panel, float y, string heading, string body)
    {
        Box(panel, heading + " stripe", 40, y + 7, 5, 31, Red);
        GuideText(panel, heading, 57, y, 665, 44, 28, true);
        GuideText(panel, body, 57, y + 48, 659, 108, 23);
    }

    private void GuideControl(RectTransform controls, int row, string key, string action)
    {
        float y = 75 + row * 50;
        RectTransform cap = FramedBox(controls, key, 20, y, 190, 41, Panel).rectTransform;
        language.Bind(Label(cap, "Key", 3, 0, 184, 41, 20, TextAnchor.MiddleCenter), key);
        language.Bind(Label(controls, action, 224, y, 270, 42, 21), action);
    }

    private void GuideText(RectTransform panel, string copy, float x, float y, float w, float h, int size, bool bold = false)
    {
        Text text = Label(panel, copy, x, y, w, h, size, TextAnchor.UpperLeft);
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        language.Bind(text, copy);
    }

    internal void EnterRoom(bool host, string name)
    {
        roomName.text = name;
        startMatch.interactable = host;
        roomStatus.text = Translate(host ? "Waiting for players..." : "Waiting for the host...");
        messages.Clear();
        AddChatLine(host ? "[System] Room created. Waiting for players..." : "[System] You joined the room.");
        chat.text = "";
        nextRosterRefresh = 0f;
        UpdateRoom();
    }

    internal void UpdateRoom()
    {
        NetworkRunner runner = menu.networking.Runner;
        if (runner == null || !runner.IsRunning || Time.unscaledTime < nextRosterRefresh) return;
        nextRosterRefresh = Time.unscaledTime + 0.2f;
        int limit = runner.SessionInfo.MaxPlayers;
        if (limit != 2 && limit != 4) return;
        SetRoomCardLayout(limit);
        PlayerRef[] players = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
        LobbyRoleRoster roster = menu.networking.RoleRoster;
        roomCount.text = $"{players.Length}/{limit}";
        PlayerRef host = players.FirstOrDefault();
        for (int i = 0; i < limit; i++)
        {
            int seat = LobbyRoleRoster.SeatForCard(i + 1, limit);
            PlayerRef owner = roster != null ? roster.OwnerOfSeat(seat) : PlayerRef.None;
            bool occupied = owner != PlayerRef.None;
            bool isLocal = occupied && owner == runner.LocalPlayer;
            slots[i].text = !occupied ? Translate("Select seat") : isLocal ? Translate("You") :
                owner == host ? Translate("Host") : string.Format(Translate("Player {0}"), Array.IndexOf(players, owner) + 1);
            slotButtons[i].targetGraphic.color = isLocal ? SelectedCard : Panel;
            foreach (RawImage piece in slotArtwork[i])
                piece.material = isLocal ? selectedCardArtwork : neutralArtwork;
            slotButtons[i].interactable = roster != null && roster.SeatOf(runner.LocalPlayer) != 0 && !occupied;
        }
    }

    private void SetRoomCardLayout(int limit)
    {
        if (roomCardLimit == limit) return;
        roomCardLimit = limit;
        for (int i = 0; i < slotButtons.Length; i++)
        {
            RectTransform card = (RectTransform)slotButtons[i].transform;
            card.gameObject.SetActive(i < limit);
            if (i >= limit) continue;

            Vector2 position = card.anchoredPosition;
            position.x = CardPositions[limit == 2 ? i + 1 : i];
            card.anchoredPosition = position;
            slotRoles[i].text = Translate(LobbyRoleRoster.RoleDisplayName(LobbyRoleRoster.RolesForSeat(i + 1, limit)));
        }
    }

    internal void AddChatLine(string line)
    {
        messages.Enqueue(line);
        while (messages.Count > 40) messages.Dequeue();
        chatHistory.text = string.Join("\n", messages.Select(ChatLine));
        LayoutRebuilder.ForceRebuildLayoutImmediate(chatHistory.rectTransform);
        chatScroll.verticalNormalizedPosition = 0f;
    }

    private void SendMessage()
    {
        string message = chat.text.Trim();
        if (message.Length == 0) return;
        menu.networking.SendRoomMessage(message);
        chat.text = "";
    }

    private void BuildSettings(int tab)
    {
        settingsTab = tab;
        if (settingsContent != null)
        {
            settingsContent.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(settingsContent.gameObject);
        }
        for (int i = 0; i < tabArtwork.Length; i++)
            if (tabArtwork[i] != null)
                tabArtwork[i].material = i == tab ? selectedTabArtwork : neutralArtwork;
        language.Refresh();
        settingsContent = Rect(settings, "Settings options", 0, 0, 1536, 1024);
        for (int i = 0; i < 6; i++)
            Box(settingsContent, "Row divider", 637, RowY(i) + 63, 795, 1, new Color(1f, 1f, 1f, 0.12f));

        switch (tab)
        {
            case 0:
                Resolution[] modes = Screen.resolutions.GroupBy(x => (x.width, x.height)).Select(x => x.Last()).ToArray();
                if (modes.Length > 0)
                {
                    int selected = Array.FindIndex(modes, mode => mode.width == Screen.width && mode.height == Screen.height);
                    Select(0, "Resolution", modes.Select(mode => $"{mode.width} × {mode.height}").ToArray(),
                        Mathf.Max(0, selected), index => SetDisplay(modes[index].width, modes[index].height, Screen.fullScreenMode));
                }
                else Info(0, "Resolution", $"{Screen.width} × {Screen.height}");
                int frameRate = Mathf.Clamp(Application.targetFrameRate > 0 ? Application.targetFrameRate : DefaultFrameRate, 30, 240);
                SliderSetting(1, "Frame rate limit (VSync off)", 30, 240, frameRate, value =>
                {
                    Application.targetFrameRate = Mathf.RoundToInt(value);
                    Save("FrameRate", Application.targetFrameRate);
                }, value => $"{Mathf.RoundToInt(value)} FPS", true);
                ToggleSetting(2, "Vertical sync", QualitySettings.vSyncCount > 0, value =>
                {
                    QualitySettings.vSyncCount = value ? 1 : 0;
                    Save("VSync", QualitySettings.vSyncCount);
                });
                FullScreenMode[] displayModes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
                Select(3, "Display mode", new[] { "Windowed", "Borderless", "Fullscreen" },
                    Mathf.Max(0, Array.IndexOf(displayModes, Screen.fullScreenMode)),
                    index => SetDisplay(Screen.width, Screen.height, displayModes[index]));
                SliderSetting(4, "Menu background blur", 0f, 8f, blurRadius, value =>
                {
                    blurRadius = value;
                    backdrop?.SetBlurRadius(value);
                    PlayerPrefs.SetFloat(Preferences + "MenuBlur", value);
                }, value => $"{Mathf.RoundToInt(value / 8f * 100f)}%");
                ToggleSetting(5, "Run in background", Application.runInBackground, value =>
                {
                    Application.runInBackground = value;
                    Save("Background", value ? 1 : 0);
                });
                Select(6, "Graphics quality", QualitySettings.names, QualitySettings.GetQualityLevel(), index =>
                {
                    int sync = QualitySettings.vSyncCount;
                    QualitySettings.SetQualityLevel(index);
                    QualitySettings.vSyncCount = sync;
                    Save("Quality", index);
                });
                break;
            case 1:
                SliderSetting(0, "Master volume", 0f, 1f, volume, ChangeVolume, value => $"{Mathf.RoundToInt(value * 100)}%");
                ToggleSetting(1, "Mute audio", muted, ToggleMute);
                Info(2, "Audio output", "System default");
                Info(3, "Microphone", "Voice chat unavailable");
                break;
            case 2:
                Info(0, "Move / look", "W A S D / Mouse");
                Info(1, "Sprint / jump", "Shift / Space");
                Info(2, "Interact", "F");
                Info(3, "Equip weapon / flashlight", "1 / 2");
                Info(4, "Fire", "Left mouse button");
                Info(5, "Reload / console rescan", "R");
                Info(6, "Inventory", "Tab");
                break;
            case 3:
                Select(0, "Language", new[] { "English", "한국어" }, Korean ? 1 : 0, changeLanguage);
                ToggleSetting(1, "Higher-contrast settings text", highContrast, value =>
                {
                    highContrast = value;
                    Save("Contrast", value ? 1 : 0);
                    BuildSettings(settingsTab);
                });
                ToggleSetting(2, "Larger settings text", largerText, value =>
                {
                    largerText = value;
                    Save("LargeText", value ? 1 : 0);
                    BuildSettings(settingsTab);
                });
                break;
        }
    }

    private void ResetSettings()
    {
        volume = 0.75f;
        muted = highContrast = largerText = false;
        if (QualitySettings.names.Length > 0) QualitySettings.SetQualityLevel(Mathf.Min(2, QualitySettings.names.Length - 1));
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = DefaultFrameRate;
        Application.runInBackground = false;
        blurRadius = 4f;
        backdrop?.SetBlurRadius(blurRadius);
        PlayerPrefs.SetFloat(Preferences + "MenuBlur", blurRadius);
        Resolution native = Screen.currentResolution;
        SetDisplay(native.width > 0 ? native.width : Screen.width,
            native.height > 0 ? native.height : Screen.height, FullScreenMode.FullScreenWindow);
        PlayerPrefs.SetFloat(Preferences + "Volume", volume);
        Save("Muted", 0);
        Save("Contrast", 0);
        Save("LargeText", 0);
        Save("VSync", 1);
        Save("FrameRate", DefaultFrameRate);
        Save("Background", 0);
        Save("Quality", QualitySettings.GetQualityLevel());
        ApplyAudio();
        UpdateRoomAudio();
        BuildSettings(settingsTab);
    }

    private static void SetDisplay(int width, int height, FullScreenMode mode)
    {
        Screen.SetResolution(width, height, mode);
        PlayerPrefs.SetInt(Preferences + "Width", width);
        PlayerPrefs.SetInt(Preferences + "Height", height);
        Save("DisplayMode", (int)mode);
    }

    private void ChangeVolume(float value)
    {
        volume = value;
        PlayerPrefs.SetFloat(Preferences + "Volume", volume);
        PlayerPrefs.Save();
        ApplyAudio();
        UpdateRoomAudio();
    }

    private void ToggleMute() => ToggleMute(!muted);
    private void ToggleMute(bool value)
    {
        muted = value;
        Save("Muted", muted ? 1 : 0);
        ApplyAudio();
        UpdateRoomAudio();
    }

    private void ApplyAudio() => AudioListener.volume = muted ? 0f : volume;

    private void UpdateRoomAudio()
    {
        if (roomVolume == null) return;
        roomVolume.SetValueWithoutNotify(volume);
        roomVolumePercent.text = $"{Mathf.RoundToInt(volume * 100)}%";
        roomMute.text = Translate(muted ? "OFF" : "ON");
        roomMute.transform.parent.GetComponent<Image>().color = muted ? Panel : new Color(0.09f, 0.46f, 0.19f);
    }

    private static void Save(string name, int value)
    {
        PlayerPrefs.SetInt(Preferences + name, value);
        PlayerPrefs.Save();
    }

    private static int RowY(int row) => 230 + row * 72;

    private void SettingName(int row, string title)
    {
        Text text = Label(settingsContent, title, 650, RowY(row), 368, 50, largerText ? 29 : 25);
        text.text = Translate(title);
        if (highContrast) text.color = Color.white;
    }

    private void Info(int row, string title, string value)
    {
        SettingName(row, title);
        Text text = Label(settingsContent, title + " value", 1040, RowY(row), 378, 50, largerText ? 28 : 24);
        text.text = Translate(value);
        if (highContrast) text.color = Color.white;
    }

    private void ToggleSetting(int row, string title, bool value, Action<bool> change)
    {
        SettingName(row, title);
        RectTransform root = Rect(settingsContent, title + " checkbox", 1042, RowY(row) + 7, 36, 36);
        Image frame = root.gameObject.AddComponent<Image>();
        frame.color = new Color(0.50f, 0.45f, 0.42f);
        Box(root, "Inset", 2, 2, 32, 32, Panel);
        Toggle checkbox = root.gameObject.AddComponent<Toggle>();
        checkbox.targetGraphic = frame;
        checkbox.graphic = Box(root, "Check", 7, 7, 22, 22, Red);
        checkbox.SetIsOnWithoutNotify(value);
        checkbox.onValueChanged.AddListener(next => change(next));
    }

    private void SliderSetting(int row, string title, float min, float max, float value, Action<float> change,
        Func<float, string> format, bool wholeNumbers = false)
    {
        SettingName(row, title);
        Text output = Label(settingsContent, title + " value", 1352, RowY(row), 70, 50, 21, TextAnchor.MiddleRight);
        output.text = format(value);
        Range(settingsContent, title, 1040, RowY(row) + 11, 300, 28, min, max, value, next =>
        {
            output.text = format(next);
            change(next);
        }).wholeNumbers = wholeNumbers;
    }

    private void Select(int row, string title, string[] options, int initial, Action<int> change)
    {
        SettingName(row, title);
        if (options.Length == 0) return;
        RectTransform root = Rect(settingsContent, title + " list", 1038, RowY(row), 385, 50);
        Image surface = root.gameObject.AddComponent<Image>();
        surface.color = new Color(0.17f, 0.15f, 0.14f);
        Dropdown dropdown = root.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = surface;
        dropdown.captionText = Label(root, "Current choice", 13, 0, 329, 50, 24);
        Label(root, "Dropdown arrow", 346, 0, 36, 50, 23, TextAnchor.MiddleCenter).text = "▾";

        float height = Mathf.Min(6, options.Length) * 39f;
        RectTransform template = Rect(root, "Choices", 0, 50, 385, height);
        template.gameObject.AddComponent<Image>().color = Panel;
        ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        RectTransform viewport = Stretch(template, "Viewport");
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect(viewport, "Choices content", 0, 0, 385, 39);
        RectTransform item = Rect(content, "Choice", 0, 0, 385, 39);
        Image itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.2f, 0.16f, 0.15f);
        Toggle toggle = item.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = itemBackground;
        toggle.graphic = Box(item, "Selection", 0, 0, 4, 39, Red);
        dropdown.itemText = Label(item, "Choice label", 14, 0, 354, 39, 22);
        scroll.viewport = viewport;
        scroll.content = content;
        dropdown.template = template;
        dropdown.AddOptions(options.Select(Translate).ToList());
        dropdown.SetValueWithoutNotify(Mathf.Clamp(initial, 0, options.Length - 1));
        dropdown.onValueChanged.AddListener(index => change(index));
        template.gameObject.SetActive(false);
    }

    private static Slider Range(Transform parent, string name, float x, float y, float width, float height,
        float min, float max, float value, Action<float> change)
    {
        RectTransform root = Rect(parent, name, x, y, width, height);
        Image raycast = root.gameObject.AddComponent<Image>();
        raycast.color = Color.clear;
        Slider slider = root.gameObject.AddComponent<Slider>();
        Box(root, "Track", 0, height / 2 - 5, width, 10, new Color(0.28f, 0.25f, 0.24f));
        RectTransform fillArea = Rect(root, "Fill area", 0, height / 2 - 5, width, 10);
        slider.fillRect = Stretch(fillArea, "Fill");
        slider.fillRect.gameObject.AddComponent<Image>().color = Red;
        RectTransform handleArea = Stretch(root, "Thumb area");
        Image handle = Box(handleArea, "Thumb", 0, 0, 18, height, Ink);
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(next => change(next));
        return slider;
    }

    private GameObject CreatePage(string name, out RectTransform frame)
    {
        RectTransform container = Stretch(parent, name + " page");
        container.gameObject.SetActive(false);
        frame = Stretch(container, "1536 × 1024 layout");
        frame.pivot = new Vector2(0.5f, 0.5f);
        var aspect = frame.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1.5f;
        return container.gameObject;
    }

    private void AddLogo(Transform page)
    {
        Image original = menu.titlePage.transform.Find("DeadRail logo")?.GetComponent<Image>();
        if (original == null || original.sprite == null) return;
        Image logo = Box(page, "DeadRail logo", 49, 37, 421, 109, Color.white);
        logo.sprite = original.sprite;
        logo.preserveAspect = true;
    }

    private Text LabelWithCopy(Transform parent, string name, float x, float y, float w, float h, int size, string copy)
    {
        Text text = Label(parent, name, x, y, w, h, size, TextAnchor.MiddleCenter);
        text.text = copy;
        return text;
    }

    private Text Label(Transform parent, string name, float x, float y, float w, float h, int size,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        Text text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Ink;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private static Texture2D LoadArtwork(string asset) =>
        Resources.Load<Texture2D>("DeadRailFrontend/" + asset);

    private static Material MakeSelectedArtwork(Shader shader, bool preservePortrait)
    {
        var material = new Material(shader);
        material.SetFloat("_Selected", 1f);
        material.SetFloat("_Card", preservePortrait ? 1f : 0f);
        return material;
    }

    internal void ReleaseArtworkMaterials()
    {
        UnityEngine.Object.Destroy(neutralArtwork);
        UnityEngine.Object.Destroy(selectedTabArtwork);
        UnityEngine.Object.Destroy(selectedCardArtwork);
    }

    private static RectTransform ArtworkPanel(Transform parent, string name, string asset,
        float x, float y, float width, float height)
    {
        RectTransform panel = FramedBox(parent, name, x, y, width, height, Panel).rectTransform;
        Texture2D texture = LoadArtwork(asset);
        if (texture == null) return panel;
        const float edge = 6f;
        ArtworkSlice(panel, "Top border", asset, new Rect(0, 0, texture.width, edge));
        ArtworkSlice(panel, "Bottom border", asset, new Rect(0, texture.height - edge, texture.width, edge));
        ArtworkSlice(panel, "Left border", asset, new Rect(0, edge, edge, texture.height - 2f * edge));
        ArtworkSlice(panel, "Right border", asset,
            new Rect(texture.width - edge, edge, edge, texture.height - 2f * edge));
        return panel;
    }

    // Source rectangles are in pixels from the top left of the exported PNG.
    private static RawImage ArtworkSlice(RectTransform panel, string name, string asset, Rect source)
    {
        Texture2D texture = LoadArtwork(asset);
        if (texture == null) return null;
        float sx = panel.rect.width / texture.width;
        float sy = panel.rect.height / texture.height;
        RectTransform element = Rect(panel, name, source.x * sx, source.y * sy, source.width * sx, source.height * sy);
        return PlaceArtwork(element, texture, source);
    }

    private static RawImage PlaceArtwork(RectTransform element, Texture2D texture, Rect? source = null)
    {
        RawImage image = element.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        if (texture != null && source.HasValue)
        {
            Rect rect = source.Value;
            image.uvRect = new Rect(rect.xMin / texture.width, 1f - rect.yMax / texture.height,
                rect.width / texture.width, rect.height / texture.height);
        }
        return image;
    }

    private Button ArtworkButton(Transform parent, string name, string asset, float x, float y, float w, float h,
        Action action, Rect? source = null)
    {
        Texture2D texture = LoadArtwork(asset);
        Graphic graphic;
        if (texture != null)
        {
            graphic = PlaceArtwork(Rect(parent, name, x, y, w, h), texture, source);
        }
        else
        {
            graphic = FramedBox(parent, name, x, y, w, h, Surface);
            LabelWithCopy(graphic.transform, "Label", 0, 0, w, h, 24, name);
        }
        return ConnectButton(graphic, action);
    }

    private Button DrawButton(Transform parent, string name, float x, float y, float w, float h, string copy,
        Color color, int fontSize, Action action)
    {
        Image image = FramedBox(parent, name, x, y, w, h, color);
        if (copy.Length > 0)
            LabelWithCopy(image.transform, "Label", 0, 0, w, h, fontSize, copy).fontStyle = FontStyle.Bold;
        return ConnectButton(image, action);
    }

    private static Button ConnectButton(Graphic graphic, Action action)
    {
        graphic.raycastTarget = true;
        Button button = graphic.gameObject.AddComponent<Button>();
        button.targetGraphic = graphic;
        button.onClick.AddListener(() => action());
        return button;
    }

    private static Image FramedBox(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        Image image = Box(parent, name, x, y, w, h, color);
        Outline outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.76f, 0.69f, 0.62f);
        outline.effectDistance = new Vector2(1f, 1f);
        return image;
    }

    private static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        Image image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rect = Rect(parent, name, 0, 0, 0, 0);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
}
