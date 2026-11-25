using UnityEngine;

public class TerminalUIManager : MonoBehaviour
{
    public static TerminalUIManager Instance;

    [Header("UI Panels")]
    public GameObject p1HackingPanel;
    public GameObject p4RadarPanel;
    [SerializeField] private Material aimOutlineMaterial;

    private PlayerMovement _localPlayer;
    private ConsoleInteractable _aimedConsole;
    private TrainDoorSwitch _aimedDoorSwitch;
    private AimOutline _aimOutline;
    private string _equipmentPrompt;
    private bool _isUiOpen;
    private bool _inventoryOpen;
    private int _openedFrame;
    private int _closedFrame = -1;
    private GUIStyle _promptStyle;
    private GUIStyle _inventoryTitleStyle;
    private GUIStyle _inventoryLabelStyle;
    private GUIStyle _inventoryCountStyle;
    private GUIStyle _inventoryHintStyle;

    public bool IsUiOpen => _isUiOpen;
    internal bool BlocksPlayerInput => GamePauseMenu.BlocksGameplayInput || _isUiOpen || Time.frameCount == _closedFrame;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _aimOutline = new AimOutline(aimOutlineMaterial);
        SetPanels(null, false);
    }

    private void OnDisable() => _aimOutline?.Show(null, false);
    private void OnDestroy() => _aimOutline?.Dispose();

    private void Update()
    {
        if (GamePauseMenu.BlocksGameplayInput || _closedFrame == Time.frameCount) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (_inventoryOpen) CloseAllTerminals();
            else if (CanOpenUi()) SetPanels(null, true);
        }
        else if (_isUiOpen && !_inventoryOpen && Input.GetKeyDown(KeyCode.R))
        {
            if (_localPlayer != null && _localPlayer.HasRole(TrainRole.Radar) &&
                MapNodeManager.Instance != null && MapNodeManager.Instance.IsNodeDataReady)
                MapNodeManager.Instance.Rpc_ForceRescanBranch();
        }
        else if (_isUiOpen && Time.frameCount != _openedFrame
            && (Input.GetKeyDown(KeyCode.Escape) || (!_inventoryOpen && Input.GetKeyDown(KeyCode.F))))
        {
            CloseAllTerminals();
        }
    }

    private void LateUpdate()
    {
        _aimedConsole = null;
        _aimedDoorSwitch = null;
        _equipmentPrompt = null;
        Transform equipmentOutline = null;
        bool canAim = !_isUiOpen && CanOpenUi(allowActiveEquipment: true);
        bool onFoot = canAim && _localPlayer.CanUsePersonalEquipment;
        Transform harpoonItem = null;
        Transform hitSurface = null;
        if (canAim && ConsoleInteractable.TryGetAimedHit(_localPlayer, out RaycastHit hit, includeTaggedItems: !onFoot))
        {
            if (onFoot)
            {
                hitSurface = hit.collider.transform;
                if (RailRouteLoop.CanReachTrainInterior(_localPlayer.transform.position))
                {
                    _aimedConsole = ConsoleInteractable.GetConsoleFromHit(_localPlayer, hit);
                    TrainDoorSwitch button = hit.collider.GetComponent<TrainDoorSwitch>();
                    if (button != null && button.IsInReach(_localPlayer.transform.position))
                        _aimedDoorSwitch = button;
                }
            }
            else if (hit.transform.CompareTag("Item") && _localPlayer.TryGetComponent(out HarpoonFarming harpoon) &&
                     harpoon.CanHighlightPickup(hit.distance))
                harpoonItem = hit.transform;
        }
        if (canAim && _aimedConsole == null && _aimedDoorSwitch == null && RailRouteLoop.CanReachTrainInterior(_localPlayer.transform.position))
        {
            if (_localPlayer.TryGetComponent(out EquipmentPivotController equipment))
                _equipmentPrompt = equipment.GetEquipmentPrompt(out equipmentOutline);
            if (_equipmentPrompt == null && ManualCrankGenerator.Instance != null)
                _equipmentPrompt = ManualCrankGenerator.Instance.GetInteractionPrompt(_localPlayer, out equipmentOutline);
        }
        Transform target = _aimedDoorSwitch != null ? _aimedDoorSwitch.transform :
            _aimedConsole != null ? _aimedConsole.transform : harpoonItem != null ? harpoonItem : equipmentOutline;
        bool includeChildren = _aimedDoorSwitch == null;
        if (RailRouteLoop.Instance != null)
        {
            Transform supply = RailRouteLoop.Instance.UpdateSupplyFocus(onFoot ? _localPlayer : null,
                target == null ? hitSurface : null, out bool useSupplyChildren);
            if (supply != null)
            {
                target = supply;
                includeChildren = useSupplyChildren;
            }
        }
        _aimOutline?.Show(target, includeChildren);
    }

    internal bool TryInteractWithAimedTarget(PlayerMovement player, out bool useSearchlight)
    {
        useSearchlight = false;
        if (_isUiOpen || !CanOpenUi() || player != _localPlayer ||
            !RailRouteLoop.CanReachTrainInterior(player.transform.position))
            return false;
        if (_aimedDoorSwitch != null && _aimedDoorSwitch.IsInReach(player.transform.position))
        {
            _aimedDoorSwitch.Interact();
            return true;
        }

        // Use the same target the player saw on the last rendered frame.
        ConsoleInteractable console = _aimedConsole;
        if (console == null || !console.isActiveAndEnabled || !player.HasRole(console.RequiredRole))
            return false;
        if (console.consoleType == ConsoleInteractable.ConsoleType.P2_SearchlightConsole)
        {
            useSearchlight = true;
            return true;
        }

        GameObject panel = GetConsolePanel(console);
        if (panel == null)
            return false;

        SetPanels(panel, false);
        return true;
    }

    public void ToggleP1Hacking() => ToggleTerminal(p1HackingPanel);
    public void ToggleP4Radar() => ToggleTerminal(p4RadarPanel);

    public void ToggleTerminal(GameObject targetPanel)
    {
        if (targetPanel == null || !CanOpenUi()) return;
        if ((targetPanel == p1HackingPanel && !_localPlayer.HasRole(TrainRole.Driver)) ||
            (targetPanel == p4RadarPanel && !_localPlayer.HasRole(TrainRole.Radar))) return;
        SetPanels(targetPanel.activeSelf ? null : targetPanel, false);
    }

    public void CloseAllTerminals()
    {
        SetPanels(null, false);
    }

    internal bool TryCloseForEscape()
    {
        if (_isUiOpen)
        {
            CloseAllTerminals();
            return true;
        }
        if (_localPlayer == null || _localPlayer.Object == null || !_localPlayer.Object.IsValid ||
            !_localPlayer.TryGetComponent(out EquipmentPivotController station) ||
            station.CurrentMode == EquipmentPivotController.EquipmentMode.None) return false;
        station.Rpc_LeaveStation(station.CurrentMode);
        _closedFrame = Time.frameCount;
        return true;
    }

    private GameObject GetConsolePanel(ConsoleInteractable console) => console.consoleType switch
    {
        ConsoleInteractable.ConsoleType.P1_DriveCinsole => p1HackingPanel,
        ConsoleInteractable.ConsoleType.P4_RadarConsole => p4RadarPanel,
        _ => null
    };

    private bool CanOpenUi(bool allowActiveEquipment = false)
    {
        if (GamePauseMenu.BlocksGameplayInput) return false;
        RailRouteLoop route = RailRouteLoop.Instance;
        if (route != null && route.Ready && route.IsTransition) return false;

        if (_localPlayer == null || _localPlayer.Object == null || !_localPlayer.Object.IsValid)
        {
            foreach (PlayerMovement player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (player.Object == null || !player.Object.IsValid || !player.IsLocalPlayer) continue;
                _localPlayer = player;
                break;
            }
        }

        if (_localPlayer == null || _localPlayer.Object == null || !_localPlayer.Object.IsValid
            || _localPlayer.IsDowned || (!allowActiveEquipment && _localPlayer.IsClimbingLadder) || (_localPlayer.isUsingTerminal && !_isUiOpen))
            return false;

        return allowActiveEquipment || !_localPlayer.TryGetComponent(out EquipmentPivotController equipment)
            || equipment.CurrentMode == EquipmentPivotController.EquipmentMode.None;
    }

    private void SetPanels(GameObject terminal, bool showInventory)
    {
        if (p1HackingPanel != null) p1HackingPanel.SetActive(terminal == p1HackingPanel);
        if (p4RadarPanel != null) p4RadarPanel.SetActive(terminal == p4RadarPanel);

        bool wasOpen = _isUiOpen;
        _inventoryOpen = showInventory;
        _isUiOpen = terminal != null || showInventory;

        if (_isUiOpen) _openedFrame = Time.frameCount;
        if (wasOpen == _isUiOpen) return;
        if (!_isUiOpen) _closedFrame = Time.frameCount;
        if (_localPlayer != null) _localPlayer.isUsingTerminal = _isUiOpen;

        Cursor.lockState = _isUiOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _isUiOpen;
    }

    private void OnGUI()
    {
        if (_inventoryOpen && _localPlayer != null && _localPlayer.Object != null && _localPlayer.Object.IsValid)
        {
            DrawInventory();
        }
        else if (!_isUiOpen)
        {
            if (_aimedDoorSwitch != null) DrawInteractionPrompt(_aimedDoorSwitch.Prompt);
            else if (_aimedConsole != null)
                DrawInteractionPrompt($"[F]  {_aimedConsole.DisplayName}");
            else DrawInteractionPrompt(_equipmentPrompt);
        }
    }

    private void DrawInteractionPrompt(string promptText)
    {
        if (string.IsNullOrEmpty(promptText)) return;
        _promptStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 16,
            normal = { textColor = new Color(1f, 0.87f, 0.63f) }
        };

        GUIContent label = new GUIContent(promptText);
        float width = _promptStyle.CalcSize(label).x + 24f;
        Rect prompt = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 18f, width, 32f);
        DrawFill(prompt, new Color(0.045f, 0.055f, 0.065f, 0.88f));
        GUI.Label(prompt, label, _promptStyle);
    }

    private void DrawInventory()
    {
        if (_inventoryTitleStyle == null)
        {
            _inventoryTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _inventoryLabelStyle = new GUIStyle(_inventoryTitleStyle) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
            _inventoryCountStyle = new GUIStyle(_inventoryLabelStyle)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.8f, 0.47f) }
            };
            _inventoryHintStyle = new GUIStyle(_inventoryCountStyle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.72f, 0.77f, 0.8f) }
            };
        }

        int previousDepth = GUI.depth;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.depth = -10;
        DrawFill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.015f, 0.02f, 0.025f, 0.71f));

        const float width = 440f;
        const float height = 246f;
        float scale = Mathf.Min(1f, Screen.width / (width + 24f), Screen.height / (height + 24f));
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), center);
        Rect panel = new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
        DrawFill(panel, new Color(0.075f, 0.095f, 0.11f, 0.98f));
        DrawFill(new Rect(panel.x, panel.y, width, 3f), new Color(0.83f, 0.59f, 0.33f));
        GUI.Label(new Rect(panel.x + 20f, panel.y + 16f, 220f, 42f), "인벤토리", _inventoryTitleStyle);
        GUI.Label(new Rect(panel.xMax - 163f, panel.y + 20f, 142f, 34f), "TAB / ESC  닫기", _inventoryHintStyle);

        DrawInventoryRow(panel, 67f, "치료품", _localPlayer.InventoryMedicalKits);
        DrawInventoryRow(panel, 120f, "수리품", _localPlayer.InventoryRepairKits);
        DrawInventoryRow(panel, 173f, "탄약", _localPlayer.InventoryAmmunition);
        GUI.matrix = previousMatrix;
        GUI.depth = previousDepth;
    }

    private void DrawInventoryRow(Rect panel, float offset, string label, int count)
    {
        Rect row = new Rect(panel.x + 20f, panel.y + offset, panel.width - 40f, 44f);
        DrawFill(row, new Color(0.12f, 0.15f, 0.17f));
        GUI.Label(new Rect(row.x + 12f, row.y, row.width - 24f, row.height), label, _inventoryLabelStyle);
        GUI.Label(new Rect(row.x + 12f, row.y, row.width - 24f, row.height), $"× {count}", _inventoryCountStyle);
    }

    private static void DrawFill(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }
}
