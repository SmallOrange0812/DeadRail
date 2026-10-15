using Fusion;
using UnityEngine;

public class PlayerMovement : NetworkBehaviour
{
    private CharacterController _controller;
    public float moveSpeed = 5f;

    [Header("마우스 회전 설정")]
    public float mouseSensitivity = 2f; // 마우스 감도
    private float _cameraPitch = 0f; // 카메라 위아래 각도

    [Header("중력 설정")]
    public float gravity = -9.81f;
    private float _velocityY;

    [Header("애니메이션")]
    public Animator anim;

    [Header("카메라 설정")]
    public Camera fpsCamera;
    private Camera _mainCamera;
    private bool _isFpsMode = false;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void Spawned()
    {
        // 내 캐릭터일 때만 카메라 및 마우스 세팅
        if (HasStateAuthority)
        {
            _mainCamera = Camera.main;
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(false);

            // 마우스 커서를 숨기고 화면 중앙에 고정 (FPS 기본 세팅)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // 남의 카메라는 끕니다.
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(false);
        }
    }

    public override void Render()
    {
        if (HasStateAuthority == false) return;

        // V키를 누르면 1인칭 <-> 3인칭 전환
        if (Input.GetKeyDown(KeyCode.V))
        {
            _isFpsMode = !_isFpsMode;
            if (fpsCamera != null) fpsCamera.gameObject.SetActive(_isFpsMode);
            if (_mainCamera != null) _mainCamera.gameObject.SetActive(!_isFpsMode);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority == false) return;

        // 1. 마우스 회전 (화면 돌리기)
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 좌우 회전: 캐릭터 전체를 Y축 기준으로 회전시킵니다.
        transform.Rotate(Vector3.up * mouseX);

        // 위아래 회전: 1인칭 모드일 때만 카메라 자체를 X축 기준으로 회전시킵니다.
        if (fpsCamera != null && _isFpsMode)
        {
            _cameraPitch -= mouseY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, -85f, 85f); // 목이 완전히 뒤로 꺾이지 않도록 각도 제한
            fpsCamera.transform.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
        }

        // 2. 이동 방향 계산 (캐릭터가 바라보는 앞/옆 기준)
        float moveX = Input.GetAxisRaw("Horizontal"); // A, D
        float moveZ = Input.GetAxisRaw("Vertical");   // W, S

        // transform.right(오른쪽)와 transform.forward(앞)를 기준으로 이동 벡터를 만듭니다.
        Vector3 move = (transform.right * moveX + transform.forward * moveZ).normalized;
        Vector3 playerVelocity = move * moveSpeed;

        // 3. 중력 적용
        if (_controller.isGrounded)
        {
            _velocityY = -2f;
        }
        else
        {
            _velocityY += gravity * Runner.DeltaTime;
        }

        // 4. 최종 이동 처리
        Vector3 finalVelocity = playerVelocity;
        finalVelocity.y = _velocityY;

        _controller.Move(finalVelocity * Runner.DeltaTime);

        // 애니메이션 속도 전달 (입력값이 있으면 걷기/뛰기 재생)
        if (anim != null)
        {
            anim.SetFloat("Speed", new Vector2(moveX, moveZ).sqrMagnitude);
        }
    }
}