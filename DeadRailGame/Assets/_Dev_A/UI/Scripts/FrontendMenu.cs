using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum FrontendPage { Title, Lobby, CreateRoom, Loading }

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

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        roomNameFont = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 28);
        roomNameInput.textComponent.font = roomNameFont;
#endif
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
        SceneManager.sceneLoaded += SceneLoaded;
        SetPage(gameObject.scene.path == TitleScene ? FrontendPage.Title : FrontendPage.Lobby);
        UpdateCount();
    }

    private async void Start()
    {
        if (Page == FrontendPage.Lobby) await networking.BrowseAsync();
    }

    public void SetPage(FrontendPage page)
    {
        Page = page;
        titlePage.SetActive(page == FrontendPage.Title);
        lobbyPage.SetActive(page == FrontendPage.Lobby);
        createPage.SetActive(page == FrontendPage.CreateRoom);
        loadingPage.SetActive(page == FrontendPage.Loading);
        if (Application.isPlaying) backdrop.SetBlurred(page != FrontendPage.Title);
        if (menuEvents != null) menuEvents.SetSelectedGameObject(null);
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
        createStatus.text = "2 - 4 players";
        SetPage(FrontendPage.CreateRoom);
    }

    public void ChangePlayerLimit(int amount)
    {
        PlayerLimit = Mathf.Clamp(PlayerLimit + amount, 2, 4);
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
        createStatus.text = "2 - 4 players";
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
            if (roomNameFont != null) row.roomName.font = roomNameFont;
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
            createStatus.text = "Enter a room name to continue.";
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
        loadingDetail.text = mode == GameMode.Host ? "Creating room..." : "Joining room...";
        SetProgress(0.05f, "Connecting to server...");
        DontDestroyOnLoad(gameObject);
        bool connected = await networking.StartRoomAsync(mode, roomName, limit);
        if (this == null || attempt != operation) return;
        if (!connected)
        {
            string error = networking.LastError ?? "Could not join the room. Please try again.";
            connecting = false;
            finishing = false;
            if (!RestoreLobbyOwnership()) return;
            SetPage(mode == GameMode.Host ? FrontendPage.CreateRoom : FrontendPage.Lobby);
            createStatus.text = error;
            lobbyStatus.text = error;
            await networking.BrowseAsync();
        }
    }

    private void SetProgress(float value, string status)
    {
        if (this == null) return;
        loadingFill.fillAmount = Mathf.Max(loadingFill.fillAmount, value);
        loadingStatus.text = status;
        // Fusion cannot safely roll back a Unity scene activation already in progress.
        // Cancellation is available during server connection, before scene loading starts.
        cancelButton.interactable = loadingFill.fillAmount < 0.7f;
        if (value >= 0.7f) loadingDetail.text = "Preparing game...";
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
        loadingStatus.text = "Ready";
        loadingDetail.text = "Entering game...";
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

    public async void CancelConnection()
    {
        if (!connecting || !cancelButton.interactable) return;
        ++operation;
        cancelButton.interactable = false;
        loadingStatus.text = "Cancelling...";
        await networking.CancelAsync();
        if (this == null) return;
        connecting = false;
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

    private void SetLobbyStatus(string status) { if (this != null) lobbyStatus.text = status; }

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
        lobbyStatus.text = message;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (roomNameFont != null) Destroy(roomNameFont);
        if (networking == null) return;
        networking.SessionsChanged -= DisplayRooms;
        networking.LobbyStatusChanged -= SetLobbyStatus;
        networking.ProgressChanged -= SetProgress;
        networking.GameSceneReady -= GameSceneReady;
        networking.ConnectionLost -= ConnectionLost;
    }
}
