using UnityEngine;

public class ConsoleInteractable : MonoBehaviour
{
    public enum ConsoleType { P1_DriveCinsole, P4_RadarConsole }

    [Header("콘솔 종류 및 거리 설정")]
    public ConsoleType consoleType = ConsoleType.P1_DriveCinsole;
    [Tooltip("상호작용 가능한 최대 거리 (미터)")]
    public float interactDistance = 2.5f;

    private Transform _localPlayerTransform;
    private bool _isPlayerNear = false;
    private GUIStyle _promptstyle;

    private void Update()
    {
        //1. 내 로컬 플레이어 캐릭터 찾기 (최초 1회)
        if (_localPlayerTransform == null)
        {
            foreach (var player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (player.Object != null && player.Object.IsValid && player.IsLocalPlayer)
                {
                    _localPlayerTransform = player.transform;
                    break;
                }
            }
            return;
        }

        PlayerMovement localPlayer = _localPlayerTransform.GetComponent<PlayerMovement>();
        _isPlayerNear = localPlayer != null && IsThisNearestInteraction(localPlayer);

        // 여러 콘솔이나 장비가 범위에 겹치면 가장 가까운 대상 하나만 E를 받습니다.
        if (_isPlayerNear && Input.GetKeyDown(KeyCode.E))
        {
            if (TerminalUIManager.Instance != null)
            {
                if (consoleType == ConsoleType.P1_DriveCinsole)
                    TerminalUIManager.Instance.ToggleP1Hacking();
                else if (consoleType == ConsoleType.P4_RadarConsole)
                    TerminalUIManager.Instance.ToggleP4Radar();
            }
        }
    }

    public static bool IsConsoleNearestInteraction(PlayerMovement player)
    {
        if (player == null)
            return false;

        ConsoleInteractable nearestConsole = FindNearestConsole(player.transform, out float consoleDistance);
        if (nearestConsole == null)
            return false;

        EquipmentPivotController equipment = player.GetComponent<EquipmentPivotController>();
        float equipmentDistance = equipment != null
            ? equipment.GetNearestInteractionDistance()
            : float.PositiveInfinity;

        return consoleDistance <= equipmentDistance;
    }

    private bool IsThisNearestInteraction(PlayerMovement player)
    {
        ConsoleInteractable nearestConsole = FindNearestConsole(player.transform, out float consoleDistance);
        if (nearestConsole != this)
            return false;

        EquipmentPivotController equipment = player.GetComponent<EquipmentPivotController>();
        float equipmentDistance = equipment != null
            ? equipment.GetNearestInteractionDistance()
            : float.PositiveInfinity;

        return consoleDistance <= equipmentDistance;
    }

    private static ConsoleInteractable FindNearestConsole(Transform player, out float nearestDistance)
    {
        ConsoleInteractable nearest = null;
        nearestDistance = float.PositiveInfinity;

        foreach (ConsoleInteractable console in FindObjectsByType<ConsoleInteractable>(FindObjectsSortMode.None))
        {
            if (console == null || !console.isActiveAndEnabled)
                continue;

            float distance = Vector3.Distance(player.position, console.transform.position);
            if (distance <= console.interactDistance && distance < nearestDistance)
            {
                nearest = console;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    //4.가까이 다가갔을 때 화면에 [E] 상호작용 안내 문구 자동 표시
    private void OnGUI()
    {
        if (!_isPlayerNear) return;
        if (TerminalUIManager.Instance != null && TerminalUIManager.Instance.IsUiOpen) return;

        if (_promptstyle == null)
        {
            _promptstyle = new GUIStyle(GUI.skin.label);
            _promptstyle.fontSize = 24;
            _promptstyle.fontStyle = FontStyle.Bold;
            _promptstyle.alignment =TextAnchor.MiddleCenter;
            _promptstyle.normal.textColor = new Color(0.2f, 1f, 0.4f);
        }

        string consoleName = (consoleType == ConsoleType.P1_DriveCinsole)
            ? "P1 기관실 속도 제어 콘솔"
            : "P4 전방 레이더 관제 콘솔";

        float width = 500f;
        float height = 50f;
        Rect rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.75f, width, height);

        //글자 가독성을 위한 검은색 그림자
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), $"[E] 키를 눌러 {consoleName} 열기", _promptstyle);

        GUI.color = Color.white;
        GUI.Label(rect, $"[E] 키를 눌러 {consoleName} 열기", _promptstyle);
    }
}
