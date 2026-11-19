using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

public class NetworkManager : MonoBehaviour
{
    private NetworkRunner _runner;
    public NetworkPrefabRef playerPrefab;

    [Header("차량기지 플레이어 스폰")]
    [SerializeField] private Transform[] yardSpawnPoints;

    [Tooltip("스폰 마커는 바닥에 두고, 플레이어 캡슐 중심을 바닥에서 올리는 높이")]
    [SerializeField] private float playerSpawnHeight = 1f;

    private NetworkEvents _playerEvents;

    private PlayerControlInput _pendingInput;

    private readonly HashSet<PlayerRef> _playersBeingSpawned = new HashSet<PlayerRef>();

    async void Start()
    {
        // 1. 씬에 이미 통신 관리자(러너)가 있는지 확인
        NetworkRunner existingRunner = FindObjectOfType<NetworkRunner>();

        // [상황 A] 로비에서 접속을 완료하고 게임 씬으로 넘어온 경우
        if (existingRunner != null && existingRunner.IsRunning)
        {
            _runner = existingRunner;

            _runner.ProvideInput = true;
            RegisterPlayerEvents();

            if (_runner.IsServer || _runner.GameMode == GameMode.Single)
            {
                foreach (PlayerRef player in _runner.ActivePlayers)
                {
                    SpawnPlayer(_runner, player);
                }
            }

            return;
        }

        // [상황 B] 로비를 거치지 않고 게임 씬에서 바로 Play를 누른 경우 (테스트용)
        // Fusion reloads the requested scene during StartGame. Do not host the
        // runner on this scene's NetworkManager GameObject: that object would
        // survive with the runner while a second NetworkManager is created by
        // the reloaded scene, causing duplicate player spawn/scene registration.
        var runnerObject = new GameObject("LocalNetworkRunner");
        DontDestroyOnLoad(runnerObject);
        _runner = runnerObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var localSceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        var result = await _runner.StartGame(new StartGameArgs()
        {
            // Directly pressing Play in the editor should be a real local test session.
            // The lobby path above continues to use the already-running Shared session.
            GameMode = GameMode.Single,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = localSceneManager
        });

        // StartGame can reload this scene, destroying this NetworkManager and
        // creating a replacement. Only the replacement should register input
        // callbacks and spawn players after that reload.
        if (this == null)
        {
            return;
        }

        // In the editor test path, spawn the local player without requiring a Photon room.
        if (result.Ok)
        {
            RegisterPlayerEvents();
            StartCoroutine(EnsureLocalPlayerSpawnedAfterTick(_runner));
        }
        else
        {
            Debug.LogError("방 접속 실패: " + result.ShutdownReason);
            Destroy(runnerObject);
        }
    }

    private IEnumerator EnsureLocalPlayerSpawnedAfterTick(NetworkRunner runner)
    {
        // Fusion dispatches PlayerJoined before the first simulation tick completes.
        // Only backfill after that tick so the local player is registered first.
        var startupTick = runner.Tick;
        yield return new WaitUntil(() => runner == null || !runner.IsRunning || runner.Tick != startupTick);
        if (runner != null && runner.IsRunning && !runner.TryGetPlayerObject(runner.LocalPlayer, out _))
            SpawnPlayer(runner, runner.LocalPlayer);
    }

    private void RegisterPlayerEvents()
    {
        _playerEvents = _runner.GetComponent<NetworkEvents>();
        bool addedComponent = _playerEvents == null;

        if (addedComponent)
        {
            _playerEvents = _runner.gameObject.AddComponent<NetworkEvents>();
        }

        _playerEvents.OnInput ??= new NetworkEvents.InputEvent();
        _playerEvents.OnInput.AddListener(SubmitInput);

        if (_runner.IsServer)
        {
            _playerEvents.PlayerJoined ??= new NetworkEvents.PlayerEvent();
            _playerEvents.PlayerLeft ??= new NetworkEvents.PlayerEvent();
            _playerEvents.PlayerJoined.AddListener(SpawnPlayer);
            _playerEvents.PlayerLeft.AddListener(DespawnPlayer);
        }

        if (addedComponent)
        {
            _runner.AddCallbacks(_playerEvents);
        }
    }

    private async void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (runner.TryGetPlayerObject(player, out var existingPlayer))
        {
            Debug.LogWarning(
                $"[스폰 건너뜀] 기존 플레이어 위치: {existingPlayer.transform.position}"
            );
            return;
        }

        if (!_playersBeingSpawned.Add(player))
        {
            Debug.LogWarning($"[스폰 건너뜀] 이미 생성 중인 플레이어: {player}");
            return;
        }

        try
        {
            Transform spawnPoint = GetYardSpawnPoint(runner, player);

            if (spawnPoint == null)
            {
                Debug.LogError(
                    "차량기지 스폰 포인트가 없습니다. NetworkManager의 Yard Spawn Points를 연결하세요."
                );
                return;
            }

            Vector3 spawnPosition =
                spawnPoint.position + Vector3.up * playerSpawnHeight;

            Debug.LogWarning(
                $"[기지 스폰 요청] 플레이어={player}, " +
                $"마커={spawnPoint.name}, 마커위치={spawnPoint.position}, " +
                $"최종위치={spawnPosition}"
            );

            Quaternion spawnRotation = spawnPoint.rotation;



            var playerObject = await runner.SpawnAsync(
                playerPrefab,
                spawnPosition,
                spawnRotation,
                player
            );

            if (runner == null || !runner.IsRunning || playerObject == null)
            {
                return;
            }

            if (this == null || 
                (player != runner.LocalPlayer && !runner.IsPlayerCommitted(player)))
            {
                Debug.LogWarning($"생성한 캐릭터를 제거합니다 — {player}");
                runner.Despawn(playerObject);
                return;
            }

            runner.SetPlayerObject(player, playerObject);
            StartCoroutine(ApplySpawnPositionAfterNetworkInitialization(
                playerObject,
                spawnPosition,
                spawnRotation
            ));
            Debug.Log($"플레이어 생성 완료 — {player}, 입력 권한: {playerObject.InputAuthority}");
        }
        catch (NetworkObjectSpawnException exception)
        {
            Debug.LogError($"플레이어 {player} 생성 실패 — {exception.Message}");
        }
        finally
        {
            _playersBeingSpawned.Remove(player);
        }
    }

    private IEnumerator ApplySpawnPositionAfterNetworkInitialization(
        NetworkObject playerObject,
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        // SpawnAsync 직후에는 Fusion의 첫 네트워크 상태 적용이 아직 끝나지
        // 않았을 수 있으므로, 첫 FixedUpdate 이후 권한자의 Transform을 확정합니다.
        yield return new WaitForFixedUpdate();

        if (playerObject == null || !playerObject.IsValid)
        {
            yield break;
        }

        if (!playerObject.HasStateAuthority)
        {
            Debug.LogError(
                $"[기지 스폰] 위치 확정 권한이 없습니다. " +
                $"HasStateAuthority={playerObject.HasStateAuthority}"
            );
            yield break;
        }

        if (!playerObject.TryGetComponent<NetworkTransform>(
                out var networkTransform))
        {
            Debug.LogError(
                "[기지 스폰] Player_A에 NetworkTransform이 없습니다."
            );
            yield break;
        }

        networkTransform.Teleport(spawnPosition, spawnRotation);

        // 다음 프레임에서 위치가 유지되는지 확인하는 진단 로그입니다.
        yield return null;
        Debug.LogWarning(
            $"[기지 스폰 확정] 실제 위치={playerObject.transform.position}, " +
            $"목표 위치={spawnPosition}"
        );
    }

    private Transform GetYardSpawnPoint(NetworkRunner runner, PlayerRef player)
    {
        if (yardSpawnPoints == null || yardSpawnPoints.Length == 0)
        {
            return null;
        }

        // 현재 접속 플레이어 목록에서 내 순번을 찾습니다.
        int playerIndex = 0;

        foreach (PlayerRef activePlayer in runner.ActivePlayers)
        {
            if (activePlayer == player)
            {
                break;
            }

            playerIndex++;
        }

        // 순번부터 살펴보되, 연결되지 않은 마커는 건너뜁니다.
        for (int offset = 0; offset < yardSpawnPoints.Length; offset++)
        {
            int index = (playerIndex + offset) % yardSpawnPoints.Length;
            Transform candidate = yardSpawnPoints[index];

            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private void DespawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (runner.TryGetPlayerObject(player, out var playerObject))
        {
            runner.Despawn(playerObject);
        }
    }

    private void Update()
    {
        if (_playerEvents == null || _runner == null || !_runner.IsRunning)
        {
            return;
        }

        if (!_runner.TryGetPlayerObject(_runner.LocalPlayer, out var playerObject) ||
        !playerObject.TryGetComponent<PlayerMovement>(out var movement))
        {
            _pendingInput = default;
            return;
        }

        PlayerControlInput currentInput = movement.ReadLocalInput();
        _pendingInput.Move = currentInput.Move;
        _pendingInput.Look += currentInput.Look;
        // Spacebar 입력은 다음 네트워크 입력 전송까지 보관합니다.
        _pendingInput.JumpPressed |= currentInput.JumpPressed;
        _pendingInput.InteractPressed |= currentInput.InteractPressed;
    }

    private void SubmitInput(NetworkRunner runner, NetworkInput input)
    {
        input.Set(_pendingInput);
        _pendingInput.Look = Vector2.zero;
        _pendingInput.JumpPressed = false;
        _pendingInput.InteractPressed = false;
    }

    private void OnDestroy()
    {
        if (_playerEvents == null) return;

        _playerEvents.OnInput?.RemoveListener(SubmitInput);
        _playerEvents.PlayerJoined?.RemoveListener(SpawnPlayer);
        _playerEvents.PlayerLeft?.RemoveListener(DespawnPlayer);
    }
}
