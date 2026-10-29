using UnityEngine;

public class TerminalUIManager : MonoBehaviour
{
    public static TerminalUIManager Instance;

    [Header("UI Panels")]
    public GameObject p1HackingPanel;
    public GameObject p4RadarPanel;

    private PlayerMovement _localPlayer;
    private bool _isUiOpen = false;
    public bool IsUiOpen => _isUiOpen;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 시작할 때는 두 모니터 화면을 모두 꺼둡니다.
        if (p1HackingPanel != null) p1HackingPanel.SetActive(false);
        if (p4RadarPanel != null) p4RadarPanel.SetActive(false);
    }

    private void Update()
    {
        // [개발자 테스트용 단축키 유지] 1번:P1 해킹창, 2번: P4 레이더창, ESC(닫기)
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ToggleP1Hacking();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            ToggleP4Radar();
        }
        else if (_isUiOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseAllTerminals();
        }
    }

    //콘솔 오브젝트에서 직접 호출할 함수들
    public void ToggleP1Hacking() => ToggleTerminal(p1HackingPanel);
    public void ToggleP4Radar() => ToggleTerminal(p4RadarPanel);
    public void ToggleTerminal(GameObject targetPanel)
    {
        if (targetPanel == null) return;

        bool willOpen = !targetPanel.activeSelf;

        // 일단 모든 창을 닫고, 열어야 하는 창만 켭니다.
        if (p1HackingPanel != null) p1HackingPanel.SetActive(false);
        if (p4RadarPanel != null) p4RadarPanel.SetActive(false);

        targetPanel.SetActive(willOpen);
        _isUiOpen = willOpen;

        UpdatePlayerAndCursorState(_isUiOpen);
    }

    public void CloseAllTerminals()
    {
        if (p1HackingPanel != null) p1HackingPanel.SetActive(false);
        if (p4RadarPanel != null) p4RadarPanel.SetActive(false);
        _isUiOpen = false;

        UpdatePlayerAndCursorState(false);
    }

    private void UpdatePlayerAndCursorState(bool isUsing)
    {
        // 내 로컬 플레이어 캐릭터 찾기
        if (_localPlayer == null)
        {
            foreach (var player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (player.HasStateAuthority)
                {
                    _localPlayer = player;
                    break;
                }
            }
        }

        // 캐릭터 이동/시점 회전 멈춤 설정
        if (_localPlayer != null)
        {
            _localPlayer.isUsingTerminal = isUsing;
        }

        // UI가 열리면 마우스 커서를 보이게 하고, 닫히면 다시 FPS 시점으로 잠급니다.
        Cursor.lockState = isUsing ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isUsing;
    }
}
