using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
public class NetworkManager : MonoBehaviour
{
    private NetworkRunner _runner;
    public NetworkPrefabRef playerPrefab;

    async void Start()
    {
        // 1. 네트워크 러너(엔진) 생성
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        // 2. Shared 모드로 방 생성 및 접속
        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = "DeadRail_Room",
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        // 3. 방 접속에 성공하면 딜레이 후 내 캐릭터 스폰
        if (result.Ok)
        {
            // 수정된 부분: Task 앞에 System.Threading.Tasks. 를 붙여서 명확하게 해줍니다.
            await System.Threading.Tasks.Task.Delay(1000);

            _runner.Spawn(playerPrefab, new Vector3(0, 1, 0), Quaternion.identity, _runner.LocalPlayer);
        }
        else
        {
            Debug.LogError("방 접속 실패: " + result.ShutdownReason);
        }
    }
}
