using UnityEngine;
using Fusion;

/// <summary>
/// Local interaction and UI for the train's manual backup generator.
/// Shared progress and blackout state are owned by the scene TreadmillManager.
/// </summary>
public sealed class ManualCrankGenerator : MonoBehaviour
{
    private const float MaximumCrankInteractionDistance = 1.35f;

    [Header("상호작용")]
    [SerializeField] private Transform interactionPoint;
    [SerializeField, Min(0.5f)] private float interactionDistance = 2.2f;

    [Header("크랭크 시각 애니메이션")]
    [SerializeField] private Transform crankPivot;
    [SerializeField] private Transform crankHandle;
    [SerializeField, Min(0f)] private float crankHandSurfaceOffset = 0.07f;
    [SerializeField] private Vector3 crankLocalAxis = Vector3.forward;
    [SerializeField, Range(5f, 90f)] private float crankStrokeAngle = 48f;
    [SerializeField, Min(0.02f)] private float crankAnimationSmoothTime = 0.09f;

    private TreadmillManager _powerManager;
    private PlayerMovement _nearbyLocalPlayer;
    private PlayerMovement _activePlayer;
    private EquipmentPivotController _activeEquipment;
    private PlayerRef _observedCrankUser = PlayerRef.None;
    private EquipmentPivotController _observedCrankEquipment;
    private bool _wasUsingTerminal;
    private GUIStyle _promptStyle;
    private GUIStyle _detailStyle;
    private Quaternion _crankRestRotation;
    private int _lastObservedStrokeSequence = -1;
    private float _crankCurrentAngle;
    private float _crankTargetAngle;
    private float _crankTargetTimer;
    private float _crankAngleVelocity;

    private void Awake()
    {
        if (interactionPoint == null)
            interactionPoint = transform;

        FindCrankPivot();
        if (crankHandle == null && crankPivot != null)
            crankHandle = crankPivot.Find("Crank_Handle");
        _powerManager = TreadmillManager.Instance;
    }

    private void Update()
    {
        if (_powerManager == null)
            _powerManager = TreadmillManager.Instance;

        UpdateCrankAnimation();

        if (_powerManager == null || !_powerManager.HasSpawned)
            return;

        UpdateNetworkCrankPose();

        if (_activePlayer != null)
        {
            if (_activePlayer.IsDowned || !_powerManager.IsBlackout ||
                _powerManager.BackupPowerRemaining > 0f ||
                Vector3.Distance(_activePlayer.transform.position, interactionPoint.position) > GetCrankInteractionDistance())
            {
                EndMinigame();
                return;
            }

            if (!GamePauseMenu.BlocksGameplayInput || Input.GetKeyDown(KeyCode.Escape))
                UpdateCrankInput();
            return;
        }

        _nearbyLocalPlayer = FindNearbyLocalPlayer();
    }

    internal static bool TryInteract(PlayerMovement player)
    {
        foreach (ManualCrankGenerator crank in FindObjectsByType<ManualCrankGenerator>(FindObjectsSortMode.None))
        {
            if (crank._nearbyLocalPlayer != player || crank._activePlayer != null ||
                crank._powerManager == null || !crank._powerManager.HasSpawned ||
                !crank._powerManager.IsBlackout || crank._powerManager.BackupPowerRemaining > 0f ||
                (crank._powerManager.CrankUser != PlayerRef.None &&
                 GetPlayerReference(player) != crank._powerManager.CrankUser))
                continue;
            crank.BeginMinigame(player);
            return true;
        }
        return false;
    }

    private PlayerMovement FindNearbyLocalPlayer()
    {
        PlayerMovement nearest = null;
        float nearestDistance = GetCrankInteractionDistance();

        foreach (PlayerMovement player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (player == null || player.Object == null || !player.Object.IsValid || !player.IsLocalPlayer)
                continue;

            float distance = Vector3.Distance(player.transform.position, interactionPoint.position);
            if (distance <= nearestDistance)
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void BeginMinigame(PlayerMovement player)
    {
        // The wider legacy range allowed starting from the nearby doorway,
        // leaving the character's body away from the crank while the IK hand
        // stretched to the handle. Require a close approach and face the unit.
        Vector3 towardCrank = interactionPoint.position - player.transform.position;
        towardCrank.y = 0f;
        if (towardCrank.sqrMagnitude > 0.001f)
            player.transform.rotation = Quaternion.LookRotation(towardCrank.normalized, Vector3.up);

        _activePlayer = player;
        _wasUsingTerminal = player.isUsingTerminal;
        player.isUsingTerminal = true;

        _activeEquipment = player.GetComponent<EquipmentPivotController>();
        if (player.Object != null && player.Object.IsValid)
            _powerManager.Rpc_SetCrankUser(GetPlayerReference(player), true);
        if (_activeEquipment != null && crankHandle != null)
            _activeEquipment.BeginCrankPose(crankHandle, GetVisibleGripPoint(player, crankHandle));
    }

    private void UpdateCrankInput()
    {
        if (_activePlayer == null)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            EndMinigame();
            return;
        }

        if (_powerManager.BackupPowerRemaining > 0f)
        {
            EndMinigame();
            return;
        }

        if (Input.GetKeyDown(KeyCode.A))
            _powerManager.Rpc_RegisterCrankStroke(false);
        else if (Input.GetKeyDown(KeyCode.D))
            _powerManager.Rpc_RegisterCrankStroke(true);
    }

    private void FindCrankPivot()
    {
        if (crankPivot == null)
        {
            Transform[] candidates = transform.root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in candidates)
            {
                if (candidate.name == "Generator_CrankPivot")
                {
                    crankPivot = candidate;
                    break;
                }
            }
        }

        if (crankPivot == null)
        {
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (candidate.name == "Generator_CrankPivot")
                {
                    crankPivot = candidate;
                    break;
                }
            }
        }

        if (crankPivot != null)
            _crankRestRotation = crankPivot.localRotation;
    }

    private void UpdateNetworkCrankPose()
    {
        PlayerRef crankUser = _powerManager.CrankUser;
        if (crankUser == _observedCrankUser &&
            (crankUser == PlayerRef.None || _observedCrankEquipment != null))
            return;

        if (_observedCrankEquipment != null)
            _observedCrankEquipment.EndCrankPose();

        _observedCrankUser = crankUser;
        _observedCrankEquipment = null;
        if (crankUser == PlayerRef.None || crankHandle == null)
            return;

        foreach (PlayerMovement player in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (player == null || player.Object == null || !player.Object.IsValid ||
                GetPlayerReference(player) != crankUser)
                continue;

            _observedCrankEquipment = player.GetComponent<EquipmentPivotController>();
            if (_observedCrankEquipment != null)
                _observedCrankEquipment.BeginCrankPose(crankHandle, GetVisibleGripPoint(player, crankHandle));
            break;
        }
    }

    private void UpdateCrankAnimation()
    {
        if (_powerManager != null && _powerManager.HasSpawned)
        {
            int sequence = _powerManager.CrankStrokeSequence;
            if (_lastObservedStrokeSequence < 0)
            {
                _lastObservedStrokeSequence = sequence;
            }
            else if (sequence < _lastObservedStrokeSequence)
            {
                // The manager resets the sequence when a new blackout starts.
                _lastObservedStrokeSequence = sequence;
            }
            else if (sequence > _lastObservedStrokeSequence)
            {
                _lastObservedStrokeSequence = sequence;
                _crankTargetAngle = _powerManager.LastCrankClockwise
                    ? crankStrokeAngle
                    : -crankStrokeAngle;
                _crankTargetTimer = 0.22f;
            }
        }

        if (_crankTargetTimer > 0f)
            _crankTargetTimer -= Time.deltaTime;
        else
            _crankTargetAngle = 0f;

        _crankCurrentAngle = Mathf.SmoothDamp(
            _crankCurrentAngle,
            _crankTargetAngle,
            ref _crankAngleVelocity,
            crankAnimationSmoothTime);

        if (crankPivot != null)
        {
            Vector3 axis = crankLocalAxis.sqrMagnitude > 0.001f
                ? crankLocalAxis.normalized
                : Vector3.forward;
            crankPivot.localRotation = _crankRestRotation * Quaternion.AngleAxis(_crankCurrentAngle, axis);
        }
    }

    private void EndMinigame()
    {
        if (_activePlayer != null && _activePlayer.Object != null && _activePlayer.Object.IsValid &&
            _powerManager != null && _powerManager.HasSpawned)
            _powerManager.Rpc_SetCrankUser(GetPlayerReference(_activePlayer), false);

        if (_activeEquipment != null)
            _activeEquipment.EndCrankPose();

        if (_activePlayer != null)
            _activePlayer.isUsingTerminal = _wasUsingTerminal;

        _activePlayer = null;
        _activeEquipment = null;
        _nearbyLocalPlayer = null;
    }

    private void OnDisable()
    {
        if (_activePlayer != null && _activePlayer.Object != null && _activePlayer.Object.IsValid &&
            _powerManager != null && _powerManager.HasSpawned)
            _powerManager.Rpc_SetCrankUser(GetPlayerReference(_activePlayer), false);

        if (_activeEquipment != null)
            _activeEquipment.EndCrankPose();

        if (_observedCrankEquipment != null)
            _observedCrankEquipment.EndCrankPose();

        if (_activePlayer != null)
            _activePlayer.isUsingTerminal = _wasUsingTerminal;

        _activePlayer = null;
        _activeEquipment = null;
        _observedCrankEquipment = null;

        if (crankPivot != null)
            crankPivot.localRotation = _crankRestRotation;
        _crankCurrentAngle = 0f;
        _crankTargetAngle = 0f;
    }

    private void OnGUI()
    {
        if (_powerManager == null)
            _powerManager = TreadmillManager.Instance;

        if (_powerManager == null || !_powerManager.HasSpawned || !_powerManager.IsBlackout)
            return;

        if (_powerManager.BackupPowerRemaining > 0f)
        {
            PlayerMovement nearbyPlayer = FindNearbyLocalPlayer();
            if (nearbyPlayer != null)
                DrawPrompt($"임시 전력 공급 중  {_powerManager.BackupPowerRemaining:F0}초");
            return;
        }

        if (_activePlayer == null)
        {
            if (_nearbyLocalPlayer != null && !_nearbyLocalPlayer.isUsingTerminal)
                DrawPrompt("[F] 수동 크랭크 발전기 사용");
            return;
        }

        DrawMinigame();
    }

    private void DrawPrompt(string message)
    {
        EnsureStyles();
        float width = Mathf.Min(620f, Screen.width - 24f);
        const float height = 50f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.75f, width, height);
        DrawOutlinedLabel(rect, message, _promptStyle);
    }

    private void DrawMinigame()
    {
        EnsureStyles();
        float width = Mathf.Min(620f, Screen.width - 32f);
        const float height = 210f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.005f, 0.045f, 0.025f, 0.94f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(0.15f, 1f, 0.38f, 0.95f);
        DrawBorder(panel, 2f);
        GUI.color = previousColor;

        DrawOutlinedLabel(new Rect(panel.x + 16f, panel.y + 12f, width - 32f, 38f),
            "수동 크랭크 발전기", _promptStyle);

        string nextKey = _powerManager.NextCrankClockwise ? "D" : "A";
        DrawOutlinedLabel(new Rect(panel.x + 16f, panel.y + 54f, width - 32f, 42f),
            $"[A] / [D] 번갈아 입력  ·  다음: [{nextKey}]",
            _detailStyle);

        Rect bar = new Rect(panel.x + 32f, panel.y + 108f, width - 64f, 26f);
        GUI.color = new Color(0f, 0.02f, 0.01f, 1f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.15f, 1f, 0.38f, 0.95f);
        DrawBorder(bar, 1f);
        Rect fill = new Rect(bar.x + 3f, bar.y + 3f,
            (bar.width - 6f) * Mathf.Clamp01(_powerManager.CrankProgress), bar.height - 6f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = previousColor;

        DrawOutlinedLabel(new Rect(panel.x + 16f, panel.y + 145f, width - 32f, 32f),
            $"발전 진행  {Mathf.RoundToInt(_powerManager.CrankProgress * 100f)}%", _detailStyle);
        DrawOutlinedLabel(new Rect(panel.x + 16f, panel.y + 174f, width - 32f, 28f),
            "[Esc] 중단", _detailStyle);
    }

    private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle style)
    {
        Color previousColor = GUI.color;
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        GUI.color = Color.white;
        GUI.Label(rect, text, style);
        GUI.color = previousColor;
    }

    private static void DrawBorder(Rect rect, float thickness)
    {
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
    }

    private void EnsureStyles()
    {
        if (_promptStyle == null)
        {
            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 1f, 0.4f) }
            };
        }

        if (_detailStyle == null)
        {
            _detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 1f, 0.4f) }
            };
        }
    }

    private static Vector3 GetVisualCenter(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return target.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private Vector3 GetVisibleGripPoint(PlayerMovement player, Transform handle)
    {
        Vector3 center = GetVisualCenter(handle);
        Vector3 viewerPosition = player.fpsCamera != null
            ? player.fpsCamera.transform.position
            : player.transform.position + Vector3.up * 1.3f;
        Vector3 towardViewer = viewerPosition - center;
        if (towardViewer.sqrMagnitude < 0.001f)
            towardViewer = -handle.forward;
        towardViewer.Normalize();

        float halfExtent = GetProjectedVisualExtent(handle, towardViewer);
        float surfaceDistance = Mathf.Clamp(halfExtent, 0.01f, 0.12f) + crankHandSurfaceOffset;
        return center + towardViewer * surfaceDistance;
    }

    private static float GetProjectedVisualExtent(Transform target, Vector3 direction)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 extents = bounds.extents;
        return Mathf.Abs(direction.x) * extents.x
            + Mathf.Abs(direction.y) * extents.y
            + Mathf.Abs(direction.z) * extents.z;
    }

    private static PlayerRef GetPlayerReference(PlayerMovement player)
    {
        return player.Runner.GameMode == GameMode.Shared
            ? player.Object.StateAuthority
            : player.Object.InputAuthority;
    }

    private float GetCrankInteractionDistance()
    {
        return Mathf.Min(interactionDistance, MaximumCrankInteractionDistance);
    }
}
