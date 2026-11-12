using Fusion;
using UnityEngine;

public struct PlayerControlInput : INetworkInput
{
    public Vector2 Move;
    public Vector2 Look;
}

public class PlayerMovement : NetworkBehaviour
{
    public bool IsLocalPlayer =>
        Runner.GameMode == GameMode.Shared 
        ? HasStateAuthority 
        : HasInputAuthority;

    private CharacterController _controller;

    public float moveSpeed = 5f;

    // ★ 1. 사라졌던 UI 조작 상태 변수 복구!
    [HideInInspector] public bool isUsingTerminal = false;

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
    private bool _isFpsMode = true;

    private Vector3 _initFpsCamLocalPos;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void Spawned()
    {
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
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(false);
        }
    }

    public override void Render()
    {
        if (!IsLocalPlayer) return;

        // ★ 2. 터미널 조작 중일 때는 시점 전환(V) 및 줌(Z) 입력 무시
        if (isUsingTerminal) return;

        // V키를 누르면 1인칭 <-> 3인칭 전환
        if (Input.GetKeyDown(KeyCode.V))
        {
            _isFpsMode = !_isFpsMode;
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(_isFpsMode);
            if (_mainCamera != null && _mainCamera != fpsCamera) _mainCamera.gameObject.SetActive(!_isFpsMode);
        }

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
        if (!HasStateAuthority)
            return default;

        if (isUsingTerminal ||
            (TerminalUIManager.Instance != null &&
            TerminalUIManager.Instance.IsUiOpen))
        {
            return default;
        }

        float zoomRatio = fpsCamera != null
            ? fpsCamera.fieldOfView / normalFOV
            : 1f;

        float sensitivity =
            mouseSensitivity * Mathf.Clamp(zoomRatio, 0.35f, 1f);

        return new PlayerControlInput
        {
            Move = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical")),
            Look = new Vector2(
                Input.GetAxis("Mouse X"),
                Input.GetAxis("Mouse Y")) * sensitivity
        };
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        PlayerControlInput controls = default;

        if (Runner.GameMode == GameMode.Shared)
        {
            controls = ReadLocalInput();
        }
        else
        {
            GetInput(out controls);
        }

        transform.Rotate(Vector3.up * controls.Look.x);
        CameraPitch = Mathf.Clamp(CameraPitch - controls.Look.y, -85f, 85f);

        Vector3 move = transform.right * controls.Move.x
                     + transform.forward * controls.Move.y;
        Vector3 velocity = move.normalized * moveSpeed;

        if (_controller.isGrounded)
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