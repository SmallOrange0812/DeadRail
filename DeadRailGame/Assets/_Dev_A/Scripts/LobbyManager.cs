using UnityEngine;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;

public class LobbyManager : MonoBehaviour
{
    [Header("네트워크 러너 프리팹")]
    public NetworkRunner runnerPrefab;

    [Header("UI 연결")]
    public TMP_InputField roomNameInput;

    public const string GameScenePath = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";
    public event Action<IReadOnlyList<SessionInfo>> SessionsChanged;
    public event Action<string> LobbyStatusChanged;
    public event Action<float, string> ProgressChanged;
    public event Action GameSceneReady;
    public event Action<string> ConnectionLost;
    public event Action<string> RoomMessageReceived;

    public NetworkRunner Runner => _runner;
    public bool IsStarting => _isStarting;
    public IReadOnlyList<SessionInfo> Sessions => _sessions;
    public string LastError { get; private set; }

    private NetworkRunner _runner;
    private NetworkEvents _events;
    private readonly List<SessionInfo> _sessions = new List<SessionInfo>();
    private Task _browseTask;
    private Task _stopTask;
    private CancellationTokenSource _cancellation;
    private int _generation;
    private bool _isStarting;
    private bool _inLobby;
    private bool _handedOff;
    private bool _waitingRoom;
    private bool _startingMatch;
    private int _messageSequence;
    private const int ChatChannel = 0x44524348;
    private const int RosterChannel = 0x44525354;
    internal LobbyRoleRoster RoleRoster { get; private set; }
    private float _nextRosterCheck;

    public async void OnClickCreateRoom()
    {
        await StartRoomAsync(GameMode.Host, LegacyRoomName(), 4);
    }

    public async void OnClickJoinRoom()
    {
        await StartRoomAsync(GameMode.Client, LegacyRoomName(), 4);
    }

    private string LegacyRoomName() => roomNameInput != null && !string.IsNullOrWhiteSpace(roomNameInput.text)
        ? roomNameInput.text.Trim() : "DefaultRoom";

    public Task BrowseAsync(bool refresh = false)
    {
        if (_isStarting || (_runner != null && _runner.IsRunning)) return Task.CompletedTask;
        if (_browseTask != null && !_browseTask.IsCompleted) return _browseTask;
        _browseTask = BrowseCoreAsync(refresh);
        return _browseTask;
    }

    private async Task BrowseCoreAsync(bool refresh)
    {
        int generation = _generation;
        try
        {
            if (refresh) await StopRunnerAsync();
            if (generation != _generation || this == null) return;
            if (_inLobby) { PublishSessions(); return; }
            LobbyStatusChanged?.Invoke("Connecting to lobby...");
            EnsureRunner();
            var result = await _runner.JoinSessionLobby(SessionLobby.ClientServer,
                cancellationToken: _cancellation.Token);
            if (generation != _generation || this == null) return;
            _inLobby = result.Ok;
            if (!result.Ok)
            {
                LastError = "Could not connect to the lobby. Please refresh to retry.";
                LobbyStatusChanged?.Invoke(LastError);
                await StopRunnerAsync();
            }
            else PublishSessions();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation && this != null)
            {
                LastError = "Could not connect to the lobby. Please refresh to retry.";
                LobbyStatusChanged?.Invoke(LastError);
                Debug.LogWarning($"[Lobby] {exception.Message}");
                await StopRunnerAsync();
            }
        }
    }

    public async Task<bool> StartRoomAsync(GameMode mode, string roomName, int playerCount, bool waitInRoom = false)
    {
        if (_isStarting || (_runner != null && _runner.IsRunning)) return false;
        if (mode == GameMode.Host && playerCount != 2 && playerCount != 4)
        {
            LastError = "Choose a room for 2 or 4 players.";
            return false;
        }
        _isStarting = true;
        LastError = null;
        int generation = _generation;
        bool connected = false;
        try
        {
            if (_browseTask != null) await _browseTask;
            if (generation != _generation || this == null) return false;
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath(GameScenePath);
            if (sceneIndex < 0) throw new InvalidOperationException("The game scene is not in Build Settings.");
            EnsureRunner();
            _waitingRoom = waitInRoom;
            _startingMatch = false;
            ProgressChanged?.Invoke(0.15f, "Connecting to server...");
            var sceneManager = _runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null) sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = roomName.Trim(),
                SceneManager = sceneManager,
                PlayerCount = mode == GameMode.Host ? playerCount : (int?)null,
                IsVisible = true,
                IsOpen = true,
                EnableClientSessionCreation = false,
                SessionProperties = mode == GameMode.Host
                    ? new Dictionary<string, SessionProperty> { { "map", "Depot" } } : null,
                StartGameCancellationToken = _cancellation.Token
            };
            if (!waitInRoom) args.Scene = SceneRef.FromIndex(sceneIndex);
            var result = await _runner.StartGame(args);
            if (generation != _generation || this == null) return false;
            connected = result.Ok;
            if (!connected) LastError = FriendlyFailure(result.ShutdownReason);
            else if (waitInRoom)
            {
                _nextRosterCheck = 0f;
                if (_runner.IsServer) RefreshRoleRoster(true);
                else SendSeatRequest(0);
            }
            return connected;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            if (generation == _generation && this != null)
            {
                LastError = "Could not join the room. Please try again.";
                Debug.LogWarning($"[Lobby] {exception.Message}");
            }
            return false;
        }
        finally
        {
            if (generation == _generation)
            {
                _isStarting = false;
                if (!connected && !_handedOff) await StopRunnerAsync();
            }
        }
    }

    public bool StartWaitingMatch()
    {
        if (!_waitingRoom || _startingMatch || _runner == null || !_runner.IsRunning || !_runner.IsServer)
            return false;
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath(GameScenePath);
        if (sceneIndex < 0) return false;

        RefreshRoleRoster(false);
        _startingMatch = true;
        try
        {
            _runner.SessionInfo.IsOpen = false;
            _runner.LoadScene(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
            return true;
        }
        catch (Exception exception)
        {
            _startingMatch = false;
            _runner.SessionInfo.IsOpen = true;
            Debug.LogWarning($"[Lobby] {exception.Message}");
            return false;
        }
    }

    private void Update()
    {
        if (!_waitingRoom || _startingMatch || _runner == null || !_runner.IsRunning || !_runner.IsServer ||
            Time.unscaledTime < _nextRosterCheck) return;
        _nextRosterCheck = Time.unscaledTime + 0.2f;
        RefreshRoleRoster(false);
    }

    internal void SelectRoleCard(int card)
    {
        if (!_waitingRoom || _startingMatch || _runner == null || !_runner.IsRunning || card < 1 || card > 4)
            return;
        if (_runner.IsServer) MovePlayerToCard(_runner.LocalPlayer, card);
        else SendSeatRequest(card);
    }

    private void SendSeatRequest(int card)
    {
        _runner.SendReliableDataToServer(ReliableKey.FromInts(RosterChannel, ++_messageSequence, 0, 0),
            new[] { (byte)card });
    }

    private void MovePlayerToCard(PlayerRef player, int card)
    {
        RefreshRoleRoster(false);
        int limit = _runner.SessionInfo.MaxPlayers;
        if (card == 0)
        {
            BroadcastRoleRoster();
            return;
        }
        if ((limit == 2 || limit == 4) && RoleRoster.SeatOf(player) != 0 &&
            RoleRoster.TryMove(player, LobbyRoleRoster.SeatForCard(card, limit), limit))
            BroadcastRoleRoster();
    }

    private void RefreshRoleRoster(bool force)
    {
        int limit = _runner.SessionInfo.MaxPlayers;
        if (limit != 2 && limit != 4) return;
        bool changed = RoleRoster.Reconcile(_runner.ActivePlayers.ToArray(), _runner.LocalPlayer, limit);
        if (changed || force) BroadcastRoleRoster();
    }

    private void BroadcastRoleRoster()
    {
        byte[] data = RoleRoster.Serialize();
        foreach (PlayerRef player in _runner.ActivePlayers)
            if (player != _runner.LocalPlayer)
                _runner.SendReliableDataToPlayer(player, ReliableKey.FromInts(RosterChannel, ++_messageSequence, 1, 0), data);
    }

    public void SendRoomMessage(string message)
    {
        if (!_waitingRoom || _startingMatch || _runner == null || !_runner.IsRunning) return;
        message = CleanMessage(message);
        if (message.Length == 0) return;
        if (_runner.IsServer) BroadcastRoomMessage(_runner.LocalPlayer, message);
        else _runner.SendReliableDataToServer(NextMessageKey(), Encoding.UTF8.GetBytes(message));
    }

    private static string CleanMessage(string text) =>
        new string(text.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();

    private ReliableKey NextMessageKey(bool broadcast = false) =>
        ReliableKey.FromInts(ChatChannel, ++_messageSequence, broadcast ? 1 : 0, 0);

    private void ReceiveRoomMessage(NetworkRunner runner, PlayerRef sender, ReliableKey key, byte[] data)
    {
        key.GetInts(out int channel, out _, out int broadcast, out _);
        if (runner != _runner || !_waitingRoom || _startingMatch ||
            data == null || data.Length == 0 || data.Length > 512)
            return;
        if (channel == RosterChannel)
        {
            if (runner.IsServer)
            {
                if (broadcast == 0 && data.Length == 1 && data[0] <= 4 && runner.ActivePlayers.Contains(sender))
                    MovePlayerToCard(sender, data[0]);
            }
            else if (broadcast == 1 && IsHostMessage(runner, sender))
            {
                RoleRoster.ApplySnapshot(data);
            }
            return;
        }
        if (channel != ChatChannel) return;
        string message = Encoding.UTF8.GetString(data);
        if (!runner.IsServer)
        {
            if (broadcast == 1 && IsHostMessage(runner, sender))
                RoomMessageReceived?.Invoke(message);
            return;
        }
        if (broadcast != 0 || !runner.ActivePlayers.Contains(sender)) return;
        message = CleanMessage(message);
        if (message.Length > 0) BroadcastRoomMessage(sender, message);
    }

    private static bool IsHostMessage(NetworkRunner runner, PlayerRef sender) =>
        sender == PlayerRef.None || sender == runner.ActivePlayers.OrderBy(player => player.PlayerId).FirstOrDefault();

    private void BroadcastRoomMessage(PlayerRef sender, string message)
    {
        if (RoleRoster.SeatOf(sender) == 0) RefreshRoleRoster(false);
        int slot = RoleRoster.SeatOf(sender);
        string roleName = LobbyRoleRoster.RoleDisplayName(LobbyRoleRoster.RolesForSeat(slot, _runner.SessionInfo.MaxPlayers));
        if (roleName == null) return;
        string line = $"{roleName}: {message}";
        RoomMessageReceived?.Invoke(line);
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        foreach (PlayerRef player in _runner.ActivePlayers)
            if (player != _runner.LocalPlayer) _runner.SendReliableDataToPlayer(player, NextMessageKey(true), bytes);
    }

    public async Task CancelAsync()
    {
        ++_generation;
        _cancellation?.Cancel();
        _isStarting = false;
        await StopRunnerAsync();
        // A cancelled lobby join may still be unwinding when Shutdown completes.
        // Do not let BrowseAsync return that old task instead of opening a fresh lobby.
        if (_browseTask != null) await _browseTask;
        _browseTask = null;
        _sessions.Clear();
        _inLobby = false;
        _waitingRoom = false;
        _startingMatch = false;
    }

    public void HandOffToGame()
    {
        _handedOff = true;
        DetachListeners();
    }

    private void EnsureRunner()
    {
        if (_runner != null && !_runner.IsShutdown) return;
        if (runnerPrefab == null) throw new InvalidOperationException("The NetworkRunner prefab is not assigned.");
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _runner = Instantiate(runnerPrefab);
        RoleRoster = _runner.gameObject.AddComponent<LobbyRoleRoster>();
        _runner.name = "DeadRail Session";
        _runner.ProvideInput = true;
        _events = _runner.GetComponent<NetworkEvents>();
        bool added = _events == null;
        if (added) _events = _runner.gameObject.AddComponent<NetworkEvents>();
        _events.OnSessionListUpdate ??= new NetworkEvents.SessionListUpdateEvent();
        _events.OnConnectedToServer ??= new NetworkEvents.RunnerEvent();
        _events.OnSceneLoadStart ??= new NetworkEvents.RunnerEvent();
        _events.OnSceneLoadDone ??= new NetworkEvents.RunnerEvent();
        _events.OnShutdown ??= new NetworkEvents.ShutdownEvent();
        _events.OnReliableData ??= new NetworkEvents.ReliableDataEvent();
        _events.OnSessionListUpdate.AddListener(ReceiveSessions);
        _events.OnConnectedToServer.AddListener(ConnectedToServer);
        _events.OnSceneLoadStart.AddListener(SceneLoadStarted);
        _events.OnSceneLoadDone.AddListener(SceneLoadFinished);
        _events.OnShutdown.AddListener(RunnerStopped);
        _events.OnReliableData.AddListener(ReceiveRoomMessage);
        if (added) _runner.AddCallbacks(_events);
    }

    private void ReceiveSessions(NetworkRunner runner, List<SessionInfo> sessions)
    {
        if (runner != _runner) return;
        _sessions.Clear();
        _sessions.AddRange(sessions.Where(session => session.IsVisible && (session.MaxPlayers == 2 || session.MaxPlayers == 4))
            .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase));
        PublishSessions();
    }

    private void PublishSessions()
    {
        SessionsChanged?.Invoke(_sessions);
        LobbyStatusChanged?.Invoke(_sessions.Count == 0 ? "No rooms found. Create a room to begin." : "");
    }

    private void ConnectedToServer(NetworkRunner runner)
    {
        if (runner == _runner && _isStarting) ProgressChanged?.Invoke(0.4f, "Joining room...");
    }

    private void SceneLoadStarted(NetworkRunner runner)
    {
        if (runner == _runner) ProgressChanged?.Invoke(0.7f, "Loading game scene...");
    }

    private void SceneLoadFinished(NetworkRunner runner)
    {
        if (runner != _runner || !SceneManager.GetSceneByPath(GameScenePath).isLoaded) return;
        ProgressChanged?.Invoke(0.9f, "Preparing your player...");
        GameSceneReady?.Invoke();
    }

    private void RunnerStopped(NetworkRunner runner, ShutdownReason reason)
    {
        if (runner != _runner || _handedOff) return;
        if (_isStarting) LastError = FriendlyFailure(reason);
        else ConnectionLost?.Invoke("Disconnected. Refresh the lobby to reconnect.");
    }

    private Task StopRunnerAsync()
    {
        if (_stopTask != null && !_stopTask.IsCompleted) return _stopTask;
        _stopTask = StopRunnerCoreAsync();
        return _stopTask;
    }

    private async Task StopRunnerCoreAsync()
    {
        NetworkRunner runner = _runner;
        DetachListeners();
        _runner = null;
        RoleRoster = null;
        _inLobby = false;
        if (runner == null) return;
        try { await runner.Shutdown(); }
        catch (Exception exception) { Debug.LogWarning($"[Lobby cleanup] {exception.Message}"); }
    }

    private void DetachListeners()
    {
        if (_events == null) return;
        _events.OnSessionListUpdate?.RemoveListener(ReceiveSessions);
        _events.OnConnectedToServer?.RemoveListener(ConnectedToServer);
        _events.OnSceneLoadStart?.RemoveListener(SceneLoadStarted);
        _events.OnSceneLoadDone?.RemoveListener(SceneLoadFinished);
        _events.OnShutdown?.RemoveListener(RunnerStopped);
        _events.OnReliableData?.RemoveListener(ReceiveRoomMessage);
        // The gameplay NetworkManager reuses NetworkEvents. Do not unregister its callbacks.
        _events = null;
    }

    private static string FriendlyFailure(ShutdownReason reason)
    {
        string value = reason.ToString();
        if (value.Contains("Full")) return "This room is full. Choose another room.";
        if (value.Contains("NotFound") || value.Contains("DoesNotExist")) return "This room is no longer available. Please refresh.";
        if (value.Contains("AlreadyExists")) return "That room name is already in use. Choose another name.";
        return "Could not connect to the room. Please try again.";
    }

    private async void OnDestroy()
    {
        DetachListeners();
        if (_handedOff) return;
        _cancellation?.Cancel();
        await StopRunnerAsync();
        _cancellation?.Dispose();
    }
}
