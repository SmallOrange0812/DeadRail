using Fusion;
using UnityEngine;

public struct PlayerControlInput : INetworkInput
{
    public Vector2 Move;
    public Vector2 Look;
    public bool JumpPressed;
    public bool InteractPressed;
    public bool SearchlightPressed;
    public bool SprintHeld;
}

public partial class PlayerMovement : NetworkBehaviour
{
    public bool IsLocalPlayer =>
        Runner.GameMode == GameMode.Shared 
        ? HasStateAuthority 
        : HasInputAuthority;

    private CharacterController _controller;

    public float moveSpeed = 5f;

    [Header("점프 설정")]
    [Tooltip("캐릭터가 점프할 때 적용하는 위쪽 속도")]
    public float jumpImpulse = 5.5f;

    // ★ 1. 사라졌던 UI 조작 상태 변수 복구!
    [HideInInspector] public bool isUsingTerminal = false;

    [Networked] public int InventoryMedicalKits { get; private set; }
    [Networked] public int InventoryRepairKits { get; private set; }
    [Networked] public int InventoryAmmunition { get; private set; }

    internal bool TryConsumeRepairKit()
    {
        if (!HasStateAuthority || InventoryRepairKits <= 0) return false;
        InventoryRepairKits--;
        return true;
    }

    internal void RecordInventoryPickup(FarmingSupplyKind kind)
    {
        switch (kind)
        {
            case FarmingSupplyKind.MedicalKit:
                InventoryMedicalKits++;
                break;
            case FarmingSupplyKind.RepairKit:
                InventoryRepairKits++;
                break;
            case FarmingSupplyKind.Ammunition:
                InventoryAmmunition += TreadmillManager.AmmunitionPerPickup;
                break;
        }
    }

    [Header("마우스 회전 설정")]
    public float mouseSensitivity = 2f;
    [Networked] public float CameraPitch { get; set; }

    [Header("분기점 연출 및 탐조등 카메라 전환 연동용")]
    [HideInInspector] public float ExternalCameraRoll = 0f;
    [HideInInspector] public Vector3 ExternalCameraShake = Vector3.zero;
    [HideInInspector] public Transform CameraOverrideTarget = null;

    [Header("카메라 망원 줌(Zoom) 설정 (마우스 휠 또는 Z키)")]
    public float normalFOV = 60f;
    public float minZoomFOV = 20f; // 숫자가 작을수록 멀리 있는 표지판이 크게 확대됨!
    public float zoomSpeed = 10f;
    private float _targetFOV = 60f;

    [Header("중력 설정")]
    public float gravity = -9.81f;
    private float _velocityY;

    [Header("애니메이션")]
    public Animator anim;

    [Header("카메라 설정")]
    public Camera fpsCamera;
    private Camera _mainCamera;

    private Vector3 _initFpsCamLocalPos;
    private bool _isClimbingLadder;
    private bool _ladderControllerWasEnabled;
    private Transform _ladderClimbReference;
    private Vector3 _ladderClimbLocalPosition;
    private Vector3 _ladderClimbInwardLocal;
    private float _ladderBottomLocalY;
    private float _ladderTopLocalY;
    [SerializeField] private float ladderClimbSpeed = 2.2f;
    public bool IsClimbingLadder => _isClimbingLadder;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // 이동은 CharacterController가 처리하므로 Animator Root Motion은 끕니다.
        // Root Motion이 켜져 있으면 걷기 클립의 루트 흔들림이 플레이어와 카메라에 더해집니다.
        if (anim != null)
        {
            anim.applyRootMotion = false;
        }

        // The networked flashlight starts off for every player.
        foreach (Light playerLight in GetComponentsInChildren<Light>(true))
        {
            playerLight.enabled = false;
        }
    }

    private void AdvanceLadderClimb(float verticalInput, float deltaTime)
    {
        if (_ladderClimbReference == null)
        {
            ReleaseLadder();
            return;
        }

        // W/S control vertical movement while attached, like a conventional
        // ladder. The route is train-local so it follows the moving train.
        if (Mathf.Abs(verticalInput) < 0.01f)
            return;

        float nextY = _ladderClimbLocalPosition.y + verticalInput * ladderClimbSpeed * deltaTime;
        if (nextY >= _ladderTopLocalY)
        {
            _ladderClimbLocalPosition.y = _ladderTopLocalY + 0.15f;
            _ladderClimbLocalPosition += _ladderClimbInwardLocal * 0.65f;
            transform.position = _ladderClimbReference.TransformPoint(_ladderClimbLocalPosition);
            ReleaseLadder();
            return;
        }

        if (nextY <= _ladderBottomLocalY)
        {
            // Step off at the bottom onto the inside floor, not outside the car.
            _ladderClimbLocalPosition.y = _ladderBottomLocalY + 0.1f;
            _ladderClimbLocalPosition += _ladderClimbInwardLocal * 0.65f;
            transform.position = _ladderClimbReference.TransformPoint(_ladderClimbLocalPosition);
            ReleaseLadder();
            return;
        }

        _ladderClimbLocalPosition.y = nextY;
        transform.position = _ladderClimbReference.TransformPoint(_ladderClimbLocalPosition);
    }

    private void ReleaseLadder()
    {
        _isClimbingLadder = false;
        if (_controller != null)
            _controller.enabled = _ladderControllerWasEnabled;
        _ladderClimbReference = null;
        _velocityY = -2f;
    }

    public bool BeginLadderClimb(
        Transform movingReference,
        Vector3 ladderCenterLocal,
        Vector3 inwardLocal,
        float bottomLocalY,
        float topLocalY)
    {
        if (!HasStateAuthority || _isClimbingLadder || _controller == null || movingReference == null
            || topLocalY <= bottomLocalY)
            return false;

        _ladderClimbReference = movingReference;
        _ladderClimbInwardLocal = inwardLocal;
        _ladderBottomLocalY = bottomLocalY;
        _ladderTopLocalY = topLocalY;

        float playerLocalY = movingReference.InverseTransformPoint(transform.position).y;
        // inwardLocal points toward the train body. Keep the player on that
        // side of the ladder throughout the climb instead of placing them outside.
        _ladderClimbLocalPosition = ladderCenterLocal + inwardLocal * 0.45f;

        // Attach at the end of the ladder nearest the player's current level:
        // climb up from the bottom, or start descending from the roof at the top.
        const float endRungOffset = 0.15f;
        bool startingAtTop = playerLocalY > (bottomLocalY + topLocalY) * 0.5f;
        _ladderClimbLocalPosition.y = startingAtTop
            ? Mathf.Clamp(topLocalY - endRungOffset, bottomLocalY, topLocalY)
            : Mathf.Clamp(bottomLocalY + endRungOffset, bottomLocalY, topLocalY);
        transform.position = movingReference.TransformPoint(_ladderClimbLocalPosition);

        _ladderControllerWasEnabled = _controller.enabled;
        // Train mesh colliders block movement in the ladder shaft. Keep the
        // controller disabled while attached, then restore it at an endpoint.
        _controller.enabled = false;
        _isClimbingLadder = true;
        _velocityY = 0f;
        return true;
    }

    private void OnDisable()
    {
        if (_controller != null && _isClimbingLadder)
            _controller.enabled = _ladderControllerWasEnabled;
        _ladderClimbReference = null;
        _isClimbingLadder = false;
    }

    public override void Spawned()
    {
        InitializeCombat();
        if (IsLocalPlayer)
        {
            _mainCamera = Camera.main;

            if (fpsCamera != null)
            {
                _initFpsCamLocalPos = fpsCamera.transform.localPosition;
                fpsCamera.nearClipPlane = 0.25f;
                normalFOV = fpsCamera.fieldOfView;
                _targetFOV = normalFOV;
                fpsCamera.gameObject.SetActive(true);
            }

            if (_mainCamera != null && _mainCamera != fpsCamera)
            {
                _mainCamera.gameObject.SetActive(false);
            }

            // (Cursor 상태는 TerminalUIManager가 제어하므로 여기서는 기본 잠금 처리)
            if (!GamePauseMenu.BlocksGameplayInput)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        else
        {
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(false);
        }
    }

    public override void Render()
    {
        RenderCombat();
        if (!IsLocalPlayer) return;

        // 터미널 조작 중에는 시점 조작과 줌 입력을 무시합니다.
        if (isUsingTerminal || GamePauseMenu.BlocksGameplayInput) return;

        // 🔭 마우스 휠 스크롤 또는 Z키로 멀리 있는 표지판 확대(Zoom-In) 기능!
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _targetFOV = Mathf.Clamp(_targetFOV - scroll * 35f, minZoomFOV, normalFOV);
        }
        if (Input.GetKeyDown(KeyCode.Z))
        {
            // Z키를 누르면 즉시 최대 확대(20도) <-> 기본 화각(60도) 토글!
            _targetFOV = (_targetFOV > minZoomFOV + 5f) ? minZoomFOV : normalFOV;
        }

        if (fpsCamera != null)
        {
            fpsCamera.fieldOfView = Mathf.Lerp(fpsCamera.fieldOfView, _targetFOV, Time.deltaTime * zoomSpeed);

            if (CameraOverrideTarget != null)
            {
                fpsCamera.transform.position = CameraOverrideTarget.position + CameraOverrideTarget.forward * 0.4f + ExternalCameraShake;
                Quaternion lookRot = Quaternion.LookRotation(CameraOverrideTarget.up, CameraOverrideTarget.forward);
                fpsCamera.transform.rotation = lookRot * Quaternion.Euler(0f, 0f, ExternalCameraRoll);
            }
            else
            {
                fpsCamera.transform.localPosition = _initFpsCamLocalPos + ExternalCameraShake;
                fpsCamera.transform.localEulerAngles = new Vector3(CameraPitch, 0f, ExternalCameraRoll);
            }
        }
    }

    public PlayerControlInput ReadLocalInput()
    {
        if (GamePauseMenu.BlocksGameplayInput) return default;
        if (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready && RailRouteLoop.Instance.IsTransition)
            return default;
        if (IsDowned || isUsingTerminal ||
            (TerminalUIManager.Instance != null && TerminalUIManager.Instance.BlocksPlayerInput))
        {
            return default;
        }

        float zoomRatio = fpsCamera != null ? fpsCamera.fieldOfView / normalFOV : 1f;
        float sensitivity = mouseSensitivity * Mathf.Clamp(zoomRatio, 0.35f, 1f);
        bool interactPressed = Input.GetKeyDown(KeyCode.F);
        bool searchlightPressed = false;
        if (interactPressed && TerminalUIManager.Instance != null && TerminalUIManager.Instance.TryInteractWithAimedTarget(this, out searchlightPressed))
        {
            if (isUsingTerminal) return default;
            interactPressed = false;
        }

        if (interactPressed && !IsClimbingLadder && CanUsePersonalEquipment &&
            (TryRequestRevive() || ManualCrankGenerator.TryInteract(this) ||
             (RailRouteLoop.Instance != null && (RailRouteLoop.Instance.TryInteractCargo(this) ||
                RailRouteLoop.Instance.TryCollectNearbySupply(this)))))
            interactPressed = false;

        return new PlayerControlInput
        {
            Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            Look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * sensitivity,
            JumpPressed = Input.GetKeyDown(KeyCode.Space),
            InteractPressed = interactPressed,
            SearchlightPressed = searchlightPressed,
            SprintHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
        };
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        AdvanceReload();
        if (IsDowned)
        {
            if (_isClimbingLadder) ReleaseLadder();
            if (anim != null) anim.SetFloat("Speed", 0f);
            return;
        }

        PlayerControlInput controls = default;

        if (Runner.GameMode == GameMode.Shared)
        {
            controls = ReadLocalInput();
        }
        else
        {
            GetInput(out controls);
        }

        if (_isClimbingLadder)
        {
            if (controls.InteractPressed)
            {
                // F releases the ladder from the player's current rung.
                _ladderClimbLocalPosition += _ladderClimbInwardLocal * 0.65f;
                if (_ladderClimbReference != null)
                    transform.position = _ladderClimbReference.TransformPoint(_ladderClimbLocalPosition);
                ReleaseLadder();
            }
            else
            {
                AdvanceLadderClimb(controls.Move.y, Runner.DeltaTime);
            }

            if (anim != null) anim.SetFloat("Speed", 0f);
            return;
        }

        if (controls.SearchlightPressed && TryGetComponent(out EquipmentPivotController searchlightOperator) &&
            searchlightOperator.CurrentMode == EquipmentPivotController.EquipmentMode.None &&
            (RailRouteLoop.Instance == null || !RailRouteLoop.Instance.Ready || !RailRouteLoop.Instance.IsTransition) &&
            searchlightOperator.TryToggleMode(EquipmentPivotController.EquipmentMode.P2_Searchlight))
            return;

        if (controls.InteractPressed)
        {
            EquipmentPivotController equipment = GetComponent<EquipmentPivotController>();
            // At roof height, give the harpoon priority over the ladder.
            if (equipment != null && equipment.TryInteractWithHarpoonMount())
                return;

            if (equipment != null && equipment.TryBeginLadderClimb())
                return;

            if (equipment != null && equipment.TryInteractWithEquipment())
                return;
        }

        transform.Rotate(Vector3.up * controls.Look.x);
        CameraPitch = Mathf.Clamp(CameraPitch - controls.Look.y, -85f, 85f);

        Vector3 move = transform.right * controls.Move.x
                     + transform.forward * controls.Move.y;
        Vector3 velocity = move.normalized * UpdateSprint(controls);

        if (controls.JumpPressed && _controller.isGrounded)
        {
            _velocityY = jumpImpulse;
        }
        else if (_controller.isGrounded)
        {
            _velocityY = -2f;
        }
        else
        {
            _velocityY += gravity * Runner.DeltaTime;
        }

        velocity.y = _velocityY;
        _controller.Move(velocity * Runner.DeltaTime);

        if (anim != null)
        {
            anim.SetFloat("Speed", controls.Move.sqrMagnitude);
        }
    }
}
