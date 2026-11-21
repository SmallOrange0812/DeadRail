using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement; // 씬 관리를 위해 추가

public class DevB_TestStarter : MonoBehaviour
{
    private NetworkRunner _runner;

    async void Start()
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        _runner.ProvideInput = true;

        Debug.Log("로컬 테스트용 Fusion 네트워크 시작 중...");

        // 추가: 현재 활성화된 씬의 번호(Build Index)를 가져옵니다.
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;

        // 경고 방지: 씬 번호가 -1(등록 안 됨)이면 경고 로그 띄우기
        if (currentSceneIndex < 0)
        {
            Debug.LogError("현재 씬이 Build Settings에 등록되지 않았습니다! File > Build Settings에서 Add Open Scenes를 눌러주세요.");
            return;
        }

        // 씬 정보를 포함하여 방을 생성합니다.
        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Single,
            SessionName = "DevB_LocalTest",
            SceneManager = sceneManager,
            Scene = SceneRef.FromIndex(currentSceneIndex) // 추가: Fusion에게 현재 씬을 네트워크 씬으로 쓰라고 명시
        });

        Debug.Log("네트워크 연결 및 씬 로드 완료! TreadmillManager가 정상 작동합니다.");
    }
}