using UnityEngine;
using Fusion;
using TMPro;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class LobbyManager : MonoBehaviour
{
    [Header("네트워크 러너 프리팹")]
    public NetworkRunner runnerPrefab;

    [Header("UI 연결")]
    public TMP_InputField roomNameInput;

    private NetworkRunner _runner;

    private bool _isStarting;

    public async void OnClickCreateRoom()
    {
        await StartGame(GameMode.Host); // 방장으로 접속
    }

    public async void OnClickJoinRoom()
    {
        await StartGame(GameMode.Client); // 손님으로 접속
    }

    private async Task StartGame(GameMode mode)
    {
        if (_isStarting || (_runner != null && _runner.IsRunning))
        {
            return;
        }

        const string gameScenePath = "Assets/_Dev_A/_Scenes/Dev_A_Scene.unity";

        int gameSceneIndex = SceneUtility.GetBuildIndexByScenePath(gameScenePath);

        if (gameSceneIndex < 0)
        {
            Debug.LogError($"빌드 씬 목록에 게임 씬이 없습니다. {gameScenePath}");
            return;
        }

        // 1. 방 이름이 비어있으면 "DefaultRoom"으로 자동 설정
        string roomName = string.IsNullOrEmpty(roomNameInput.text)
            ? "DefaultRoom"
            : roomNameInput.text;

        _isStarting = true;
        bool connected = false;

        try
        {
            _runner = Instantiate(runnerPrefab);

            var sceneManager = _runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null)
            {
                sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = roomName,
                Scene = SceneRef.FromIndex(gameSceneIndex),
                SceneManager = sceneManager
            });

            connected = result.Ok;
            if (!connected)
            {
                Debug.LogError($"방 접속 실패 ({mode}) — {result.ShutdownReason}");
            }
        }
        finally
        {
            if (!connected)
            {
                if (_runner != null)
                {
                    Destroy(_runner.gameObject);
                }

                _runner = null;
            }

            _isStarting = false;
        }
    }
}
