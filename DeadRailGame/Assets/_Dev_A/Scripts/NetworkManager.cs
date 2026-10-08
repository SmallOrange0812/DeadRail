using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
public class NetworkManager : MonoBehaviour
{
    private NetworkRunner _runner;
    public NetworkPrefabRef playerPrefab;

    async void Start()
    {
        // 1. 씬에 이미 통신 관리자(러너)가 있는지 확인
        NetworkRunner existingRunner = FindObjectOfType<NetworkRunner>();

        // [상황 A] 로비에서 접속을 완료하고 게임 씬으로 넘어온 경우
        if (existingRunner != null && existingRunner.IsRunning)
        {
            _runner = existingRunner; // 로비에서 가져온 러너를 그대로 사용

            // 씬 로딩 안정화를 위해 1초 대기 후 내 캐릭터 스폰
            await System.Threading.Tasks.Task.Delay(1000);
            _runner.Spawn(playerPrefab, new Vector3(0, 1, 0), Quaternion.identity, _runner.LocalPlayer);

            return; // ★ 접속은 로비에서 이미 했으므로 아래쪽 [상황 B] 코드는 무시하고 마칩니다.
        }

        // [상황 B] 로비를 거치지 않고 게임 씬에서 바로 Play를 누른 경우 (테스트용)
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = "DeadRail_Room",
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        // 방 접속에 성공하면 1초 뒤 스폰
        if (result.Ok)
        {
            await System.Threading.Tasks.Task.Delay(1000);
            _runner.Spawn(playerPrefab, new Vector3(0, 1, 0), Quaternion.identity, _runner.LocalPlayer);
        }
        else
        {
            Debug.LogError("방 접속 실패: " + result.ShutdownReason);
        }
    }
}
