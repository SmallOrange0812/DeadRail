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
        // 1. 방 이름이 비어있으면 "DefaultRoom"으로 자동 설정
        string roomName = string.IsNullOrEmpty(roomNameInput.text) ? "DefaultRoom" : roomNameInput.text;

        // 2. 네트워크 러너(통신 담당자) 생성
        if (_runner == null)
        {
            _runner = Instantiate(runnerPrefab);
        }

        // 3. 씬 전환을 위한 매니저 부착 (Fusion 필수)
        _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        Debug.Log($"{roomName} 방에 {mode} 모드로 접속 중...");

        // 4. 포톤 서버 접속 및 다음 씬(게임 씬)으로 이동
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex + 1)
        });
    }
}
