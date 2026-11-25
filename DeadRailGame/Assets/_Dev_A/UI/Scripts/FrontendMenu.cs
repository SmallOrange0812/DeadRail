using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static FrontendLanguage;

public enum FrontendPage { Title, Lobby, CreateRoom, Loading, Room, Settings, HowToPlay, Credits }

public sealed class FrontendMenu : MonoBehaviour
{
    public const string TitleScene = "Assets/_Dev_A/Title/Scene_Title.unity";
    public const string LobbyScene = "Assets/_Dev_A/_Scenes/Scene_Lobby.unity";

    public GameObject titlePage, lobbyPage, createPage, loadingPage;
    public Button playButton, createButton, joinButton, lobbyBackButton, createBackButton;
    public Button refreshButton, resetButton, minusButton, plusButton, submitButton, cancelButton;
    public InputField roomNameInput;
    public Text playerCountText, lobbyStatus, createStatus, loadingStatus, loadingDetail;
    public Image loadingFill;
    public Transform roomContent;
    public FrontendRoomRow rowTemplate;
    public LobbyManager networking;
    public FrontendBackdrop backdrop;
    public EventSystem menuEvents;
    public FrontendPage Page { get; private set; }
    public int PlayerLimit { get; private set; } = 4;

    private readonly List<FrontendRoomRow> rows = new List<FrontendRoomRow>();
    private string selectedRoom;
    private int operation;
    private bool transitioning, connecting, finishing;
    private Font roomNameFont;
    private FrontendAdditionalPages additionalPages;
    private Button howToPlayButton, creditsButton;
    internal FrontendLanguage Language { get; private set; }

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        roomNameFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR" }, 28);
        if (roomNameFont != null) roomNameInput.textComponent.font = roomNameFont;
        Language = new FrontendLanguage(roomNameFont);
        BindMenuLanguage();
        playButton.onClick.AddListener(OpenLobby);
        createButton.onClick.AddListener(OpenCreateRoom);
        joinButton.onClick.AddListener(JoinSelectedRoom);
        lobbyBackButton.onClick.AddListener(BackToTitle);
        createBackButton.onClick.AddListener(() => SetPage(FrontendPage.Lobby));
        refreshButton.onClick.AddListener(RefreshRooms);
        resetButton.onClick.AddListener(ResetCreateRoom);
        minusButton.onClick.AddListener(() => ChangePlayerLimit(-1));
        plusButton.onClick.AddListener(() => ChangePlayerLimit(1));
        submitButton.onClick.AddListener(CreateRoom);
        cancelButton.onClick.AddListener(CancelConnection);
        networking.SessionsChanged += DisplayRooms;
        networking.LobbyStatusChanged += SetLobbyStatus;
        networking.ProgressChanged += SetProgress;
        networking.GameSceneReady += GameSceneReady;
        networking.ConnectionLost += ConnectionLost;
        networking.RoomMessageReceived += ReceiveRoomMessage;
        additionalPages = new FrontendAdditionalPages(this, roomNameFont);
        Button settings = titlePage.transform.Find("Settings")?.GetComponent<Button>();
        if (settings != null) settings.onClick.AddListener(() => SetPage(FrontendPage.Settings));
        howToPlayButton = titlePage.transform.Find("How to play")?.GetComponent<Button>();
        if (howToPlayButton != null) howToPlayButton.onClick.AddListener(() => SetPage(FrontendPage.HowToPlay));
        creditsButton = titlePage.transform.Find("Credits")?.GetComponent<Button>();
        if (creditsButton != null) creditsButton.onClick.AddListener(() => SetPage(FrontendPage.Credits));
        SceneManager.sceneLoaded += SceneLoaded;
        SetPage(gameObject.scene.path == TitleScene ? FrontendPage.Title : FrontendPage.Lobby);
        UpdateCount();
    }

    private void BindMenuLanguage()
    {
        Language.BindArtwork(playButton.targetGraphic, "Play", new Rect(.45f, .15f, .36f, .7f), .88f, 43);
        Language.BindArtwork(titlePage.transform.Find("Settings")?.GetComponent<Image>(), "Settings",
            new Rect(.38f, .15f, .49f, .7f), .92f, 31);
        Language.BindArtwork(titlePage.transform.Find("How to play")?.GetComponent<Image>(), "How to play",
            new Rect(.39f, .15f, .54f, .7f), .95f, 30);
        Language.Bind(titlePage.transform.Find("Credits label")?.GetComponent<Text>(), "CREDITS");

        Transform lobby = lobbyPage.transform.Find("Lobby list");
        Language.BindArtwork(lobby?.Find("Header")?.GetComponent<Image>(), "Lobby list",
            new Rect(.03f, .12f, .43f, .74f), .62f, 49);
        BindLabels(lobby, ("Room column", "ROOM NAME"), ("Players column", "PLAYERS"),
            ("Map column", "MAP"), ("Ping column", "PING"));
        Language.BindArtwork(createButton.targetGraphic, "Create room", new Rect(.35f, .12f, .59f, .76f), .30f, 29);
        Language.BindArtwork(joinButton.targetGraphic, "Join room", new Rect(.4f, .12f, .53f, .76f), .90f, 29);
        BindBack(lobbyBackButton);
        Language.BindArtwork(rowTemplate.joinButton.targetGraphic, "Join", new Rect(.11f, .14f, .78f, .72f), .90f, 23);

        Transform create = createPage.transform.Find("Create room settings");
        Language.BindArtwork(create?.Find("Header")?.GetComponent<Image>(), "Create room",
            new Rect(.03f, .12f, .45f, .74f), .65f, 47);
        BindLabels(create, ("Room name label", "ROOM NAME"), ("Player count label", "PLAYER COUNT"));
        Language.Bind(roomNameInput.placeholder as Text, "Enter room name...");
        Language.BindArtwork(submitButton.targetGraphic, "Create room", new Rect(.37f, .14f, .48f, .73f), .88f, 40);
        BindBack(createBackButton);

        Language.BindArtwork(loadingPage.transform.Find("Joining room/Header")?.GetComponent<Image>(), "Joining room",
            new Rect(.23f, .12f, .55f, .74f), .86f, 44);
        Language.BindArtwork(cancelButton.targetGraphic, "Cancel", new Rect(.41f, .14f, .50f, .72f), .91f, 35);

        Language.Bind(lobbyStatus, "No rooms found. Create a room to begin.");
        Language.Bind(createStatus, "2 or 4 players");
        Language.Bind(loadingStatus, "Connecting to server...");
        Language.Bind(loadingDetail, "Preparing game...");
    }

    private void BindLabels(Transform root, params (string objectName, string label)[] labels)
    {
        foreach (var label in labels)
            Language.Bind(root?.Find(label.objectName)?.GetComponent<Text>(), label.label);
    }

    private void BindBack(Button button) =>
        Language.BindArtwork(button.targetGraphic, "Back", new Rect(.41f, .15f, .48f, .7f), .91f, 27);

    internal void ChangeLanguage(int option)
    {
        if (Korean == (option == 1)) return;
        Set(option);
        Language.Refresh();
        StartCoroutine(RefreshLanguagePage());
    }

    private IEnumerator RefreshLanguagePage()
    {
        yield return null;
        if (Page == FrontendPage.Settings) additionalPages.Show(Page);
    }

    private async void Start()
    {
        if (Page == FrontendPage.Lobby) await networking.BrowseAsync();
    }

    public void SetPage(FrontendPage page)
    {
        FrontendPage previous = Page;
        Page = page;
        titlePage.SetActive(page == FrontendPage.Title);
        lobbyPage.SetActive(page == FrontendPage.Lobby);
        createPage.SetActive(page == FrontendPage.CreateRoom);
        loadingPage.SetActive(page == FrontendPage.Loading);
        additionalPages?.Show(page);
        if (Application.isPlaying) backdrop.SetBlurred(page != FrontendPage.Title);
        if (menuEvents != null)
        {
            Button focus = page == FrontendPage.HowToPlay ? additionalPages.GuideBack :
                page == FrontendPage.Credits ? additionalPages.CreditsBack : null;
            if (page == FrontendPage.Title && previous == FrontendPage.HowToPlay) focus = howToPlayButton;
            if (page == FrontendPage.Title && previous == FrontendPage.Credits) focus = creditsButton;
            menuEvents.SetSelectedGameObject(focus != null ? focus.gameObject : null);
        }
    }

    public void OpenLobby()
    {
        if (transitioning) return;
        transitioning = true;
        StartCoroutine(LoadFrontendScene(LobbyScene));
    }

    private IEnumerator LoadFrontendScene(string path)
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(path);
        if (load != null) yield return load;
    }

    public void OpenCreateRoom()
    {
        if (connecting) return;
        createStatus.text = Translate("2 or 4 players");
        SetPage(FrontendPage.CreateRoom);
    }

    public void ChangePlayerLimit(int amount)
    {
        if (amount != 0) PlayerLimit = amount < 0 ? 2 : 4;
        UpdateCount();
    }

    private void UpdateCount()
    {
        playerCountText.text = PlayerLimit.ToString();
        minusButton.interactable = PlayerLimit > 2;
        plusButton.interactable = PlayerLimit < 4;
    }

    public void ResetCreateRoom()
    {
        roomNameInput.text = "";
        PlayerLimit = 4;
        UpdateCount();
        createStatus.text = Translate("2 or 4 players");
    }

    public async void RefreshRooms()
    {
        if (connecting || transitioning) return;
        refreshButton.interactable = false;
        await networking.BrowseAsync(true);
        if (this != null) refreshButton.interactable = true;
    }

    private void DisplayRooms(IReadOnlyList<SessionInfo> sessions)
    {
        if (this == null) return;
        foreach (FrontendRoomRow row in rows)
        {
            row.gameObject.SetActive(false);
            Destroy(row.gameObject);
        }
        rows.Clear();
        foreach (SessionInfo session in sessions)
        {
            FrontendRoomRow row = Instantiate(rowTemplate, roomContent);
            row.gameObject.SetActive(true);
            row.Bind(session, SelectRoom, JoinRow);
            if (roomNameFont != null)
            {
                row.roomName.font = roomNameFont;
                if (Korean) row.map.font = roomNameFont;
            }
            row.SetSelected(session.Name == selectedRoom);
            rows.Add(row);
        }
        joinButton.interactable = rows.Any(row => row.Session.Name == selectedRoom && row.CanJoin);
    }

    private void SelectRoom(FrontendRoomRow selected)
    {
        selectedRoom = selected.Session.Name;
        foreach (FrontendRoomRow row in rows) row.SetSelected(row == selected);
        joinButton.interactable = selected.CanJoin;
    }

    private void JoinRow(FrontendRoomRow row)
    {
        if (!row.CanJoin) return;
        SelectRoom(row);
        JoinSelectedRoom();
    }

    public void JoinSelectedRoom()
    {
        FrontendRoomRow row = rows.FirstOrDefault(item => item.Session.Name == selectedRoom);
        if (row == null || !row.CanJoin) return;
        BeginConnection(GameMode.Client, row.Session.Name, row.Session.MaxPlayers);
    }

    public void CreateRoom()
    {
        string name = roomNameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            createStatus.text = Translate("Enter a room name to continue.");
            roomNameInput.ActivateInputField();
            return;
        }
        BeginConnection(GameMode.Host, name, PlayerLimit);
    }

    private async void BeginConnection(GameMode mode, string roomName, int limit)
    {
        if (connecting || transitioning) return;
        connecting = true;
        finishing = false;
        int attempt = ++operation;
        SetPage(FrontendPage.Loading);
        loadingFill.fillAmount = 0f;
        cancelButton.interactable = true;
        loadingDetail.text = Translate(mode == GameMode.Host ? "Creating room..." : "Joining room...");
        SetProgress(0.05f, "Connecting to server...");
        DontDestroyOnLoad(gameObject);
        bool connected = await networking.StartRoomAsync(mode, roomName, limit, waitInRoom: true);
        if (this == null || attempt != operation) return;
        if (connected)
        {
            if (!finishing && !SceneManager.GetSceneByPath(LobbyManager.GameScenePath).isLoaded)
            {
                additionalPages.EnterRoom(mode == GameMode.Host, roomName);
                SetPage(FrontendPage.Room);
            }
        }
        else
        {
            string error = networking.LastError ?? "Could not join the room. Please try again.";
            connecting = false;
            finishing = false;
            if (!RestoreLobbyOwnership()) return;
            SetPage(mode == GameMode.Host ? FrontendPage.CreateRoom : FrontendPage.Lobby);
            createStatus.text = Translate(error);
            lobbyStatus.text = Translate(error);
            await networking.BrowseAsync();
        }
    }

    private void SetProgress(float value, string status)
    {
        if (this == null) return;
        if (Page == FrontendPage.Room && value >= 0.7f) SetPage(FrontendPage.Loading);
        loadingFill.fillAmount = Mathf.Max(loadingFill.fillAmount, value);
        loadingStatus.text = Translate(status);
        // Fusion cannot safely roll back a Unity scene activation already in progress.
        // Cancellation is available during server connection, before scene loading starts.
        cancelButton.interactable = loadingFill.fillAmount < 0.7f;
        if (value >= 0.7f) loadingDetail.text = Translate("Preparing game...");
    }

    private void GameSceneReady()
    {
        if (!connecting || finishing) return;
        finishing = true;
        StartCoroutine(FinishWhenPlayerIsReady(operation));
    }

    private IEnumerator FinishWhenPlayerIsReady(int attempt)
    {
        NetworkRunner runner = networking.Runner;
        while (attempt == operation && runner != null &&
               (!runner.IsRunning || !runner.TryGetPlayerObject(runner.LocalPlayer, out _))) yield return null;
        if (attempt != operation || runner == null || !runner.IsRunning) yield break;
        loadingFill.fillAmount = 1f;
        loadingStatus.text = Translate("Ready");
        loadingDetail.text = Translate("Entering game...");
        yield return new WaitForSecondsRealtime(0.15f);
        if (attempt != operation) yield break;
        networking.HandOffToGame();
        Destroy(gameObject);
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (connecting && scene.path == LobbyManager.GameScenePath && menuEvents != null)
            menuEvents.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (Page == FrontendPage.Room) additionalPages.UpdateRoom();
        if ((Page == FrontendPage.Settings || Page == FrontendPage.HowToPlay || Page == FrontendPage.Credits) &&
            Input.GetKeyDown(KeyCode.Escape))
            SetPage(FrontendPage.Title);
    }

    internal void StartMatch()
    {
        if (Page != FrontendPage.Room || !networking.StartWaitingMatch()) return;
        SetPage(FrontendPage.Loading);
        SetProgress(0.7f, "Loading game scene...");
    }

    private void ReceiveRoomMessage(string message)
    {
        if (this != null && Page == FrontendPage.Room) additionalPages.AddChatLine(message);
    }

    internal async void LeaveRoom()
    {
        if (Page == FrontendPage.Room) await ReturnToLobbyAsync();
    }

    public async void CancelConnection()
    {
        if (connecting && cancelButton.interactable) await ReturnToLobbyAsync();
    }

    private async System.Threading.Tasks.Task ReturnToLobbyAsync()
    {
        if (!connecting) return;
        ++operation;
        connecting = false;
        cancelButton.interactable = false;
        SetPage(FrontendPage.Loading);
        loadingStatus.text = Translate("Cancelling...");
        await networking.CancelAsync();
        if (this == null) return;
        finishing = false;
        if (!RestoreLobbyOwnership()) return;
        menuEvents.gameObject.SetActive(true);
        loadingFill.fillAmount = 0f;
        SetPage(FrontendPage.Lobby);
        await networking.BrowseAsync();
    }

    private bool RestoreLobbyOwnership()
    {
        Scene lobby = SceneManager.GetSceneByPath(LobbyScene);
        if (lobby.IsValid() && lobby.isLoaded && SceneManager.GetActiveScene().path == LobbyScene)
        {
            SceneManager.MoveGameObjectToScene(gameObject, lobby);
            return true;
        }
        SceneManager.LoadSceneAsync(LobbyScene);
        Destroy(gameObject);
        return false;
    }

    public async void BackToTitle()
    {
        if (transitioning || connecting) return;
        transitioning = true;
        await networking.CancelAsync();
        if (this != null) StartCoroutine(LoadFrontendScene(TitleScene));
    }

    private void SetLobbyStatus(string status) { if (this != null) lobbyStatus.text = Translate(status); }

    private void ConnectionLost(string message)
    {
        if (this == null) return;
        if (connecting && !networking.IsStarting)
        {
            connecting = false;
            finishing = false;
            ++operation;
            if (!RestoreLobbyOwnership()) return;
            SetPage(FrontendPage.Lobby);
        }
        lobbyStatus.text = Translate(message);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        additionalPages?.ReleaseArtworkMaterials();
        if (roomNameFont != null) Destroy(roomNameFont);
        if (networking == null) return;
        networking.SessionsChanged -= DisplayRooms;
        networking.LobbyStatusChanged -= SetLobbyStatus;
        networking.ProgressChanged -= SetProgress;
        networking.GameSceneReady -= GameSceneReady;
        networking.ConnectionLost -= ConnectionLost;
        networking.RoomMessageReceived -= ReceiveRoomMessage;
    }
}
