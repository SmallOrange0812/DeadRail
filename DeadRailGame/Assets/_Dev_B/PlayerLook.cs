using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : NetworkBehaviour
{
    [Header("Look Settings")]
    public float mouseSensitivity = 15f;
    public Transform playerBody;

    private float xRotation = 0f;
    private bool isFirstFrame = true; // 추가: 첫 프레임 마우스 튐 현상 방지 플래그

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        // 팁: 에디터에서 잡아둔 카메라의 기본 각도를 그대로 가져와서 시작하도록 보정
        xRotation = transform.localRotation.eulerAngles.x;
        if (xRotation > 180f) xRotation -= 360f;
    }

    private void Update()
    {
        if (!HasStateAuthority) return;
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        // [핵심] 커서가 중앙으로 강제 이동하며 발생하는 첫 프레임의 비정상적인 쓰레기값을 무시합니다.
        if (isFirstFrame)
        {
            isFirstFrame = false;
            return;
        }

        float mouseX = mouseDelta.x * mouseSensitivity * Time.deltaTime;
        float mouseY = mouseDelta.y * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}