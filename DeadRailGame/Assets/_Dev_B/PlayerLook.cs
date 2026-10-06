using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{
    [Header("카메라 세팅")]
    public float mouseSensitivity = 10f;
    public Transform playerBody;

    private float xRotation = 0f;

    // 1프레임이 아닌 시작 직후 0.1초 동안 넉넉하게 마우스 입력 무시
    private float ignoreInputTime = 0.1f;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        // 현재 유니티 에디터에 세팅된 카메라의 위아래 각도를 그대로 가져와서 시작값으로 맞춤
        xRotation = transform.localRotation.eulerAngles.x;
        if (xRotation > 180f) xRotation -= 360f; // 0~360도 체계를 -180~180도로 변환
    }

    private void Update()
    {
        // 0.1초가 지나기 전까지는 튐 현상이 끝날 때까지 기다림(return)
        if (ignoreInputTime > 0f)
        {
            ignoreInputTime -= Time.deltaTime;
            return;
        }

        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX = mouseDelta.x * mouseSensitivity * Time.deltaTime;
        float mouseY = mouseDelta.y * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * mouseX);
        }
    }
}