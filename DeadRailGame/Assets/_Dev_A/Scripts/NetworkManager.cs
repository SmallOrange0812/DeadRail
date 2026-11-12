using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class NetworkManager : MonoBehaviour
{
    private NetworkRunner _runner;
    public NetworkPrefabRef playerPrefab;

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

            if (_runner.IsServer)
            {
                foreach (PlayerRef player in _runner.ActivePlayers)
                {
                    SpawnPlayer(_runner, player);
                }
            }

            return;
        }

        // [상황 B] 로비를 거치지 않고 게임 씬에서 바로 Play를 누른 경우 (테스트용)
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var result = await _runner.StartGame(new StartGameArgs()
        {
            // Directly pressing Play in the editor should be a real local test session.
            // The lobby path above continues to use the already-running Shared session.
            GameMode = GameMode.Single,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        // In the editor test path, spawn the local player without requiring a Photon room.
        if (result.Ok)
        {
            RegisterPlayerEvents();
            SpawnPlayer(_runner, _runner.LocalPlayer);
        }
        else
        {
            Debug.LogError("방 접속 실패: " + result.ShutdownReason);
        }
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
        if (runner.TryGetPlayerObject(player, out _) ||
            !_playersBeingSpawned.Add(player))
        {
            return;
        }

        try
        {
            var playerObject = await runner.SpawnAsync(
                playerPrefab,
                GetTrainSpawnPosition(),
                Quaternion.identity,
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

    private static Vector3 GetTrainSpawnPosition()
    {
        GameObject train = GameObject.FindGameObjectWithTag("Train");
        if (train == null)
        {
            return new Vector3(0f, 1.5f, 0f);
        }

        Collider[] trainColliders = train.GetComponentsInChildren<Collider>();
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Collider trainCollider in trainColliders)
        {
            if (!trainCollider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = trainCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(trainCollider.bounds);
            }
        }

        if (!hasBounds)
        {
            return train.transform.position + Vector3.up * 3f;
        }

        // CharacterController height is 2m: place its center just above the train deck.
        return new Vector3(bounds.center.x, bounds.max.y + 1.1f, bounds.center.z);
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
    }

    private void SubmitInput(NetworkRunner runner, NetworkInput input)
    {
        input.Set(_pendingInput);
        _pendingInput.Look = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (_playerEvents == null) return;

        _playerEvents.OnInput?.RemoveListener(SubmitInput);
        _playerEvents.PlayerJoined?.RemoveListener(SpawnPlayer);
        _playerEvents.PlayerLeft?.RemoveListener(DespawnPlayer);
    }
}
