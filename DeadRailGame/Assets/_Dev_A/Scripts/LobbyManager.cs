using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

    public async Task<bool> StartRoomAsync(GameMode mode, string roomName, int playerCount)
    {
        if (_isStarting || (_runner != null && _runner.IsRunning)) return false;
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
            ProgressChanged?.Invoke(0.15f, "Connecting to server...");
            var sceneManager = _runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null) sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = roomName.Trim(),
                Scene = SceneRef.FromIndex(sceneIndex),
                SceneManager = sceneManager,
                PlayerCount = mode == GameMode.Host ? Mathf.Clamp(playerCount, 2, 4) : (int?)null,
                IsVisible = true,
                IsOpen = true,
                EnableClientSessionCreation = false,
                SessionProperties = mode == GameMode.Host
                    ? new Dictionary<string, SessionProperty> { { "map", "Depot" } } : null,
                StartGameCancellationToken = _cancellation.Token
            });
            if (generation != _generation || this == null) return false;
            connected = result.Ok;
            if (!connected) LastError = FriendlyFailure(result.ShutdownReason);
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
        _events.OnSessionListUpdate.AddListener(ReceiveSessions);
        _events.OnConnectedToServer.AddListener(ConnectedToServer);
        _events.OnSceneLoadStart.AddListener(SceneLoadStarted);
        _events.OnSceneLoadDone.AddListener(SceneLoadFinished);
        _events.OnShutdown.AddListener(RunnerStopped);
        if (added) _runner.AddCallbacks(_events);
    }

    private void ReceiveSessions(NetworkRunner runner, List<SessionInfo> sessions)
    {
        if (runner != _runner) return;
        _sessions.Clear();
        _sessions.AddRange(sessions.Where(session => session.IsVisible)
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
        if (runner != _runner) return;
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
