using UnityEngine;
using UnityEngine.UIElements;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    // 수정된 부분: Y를 3.5 높이로, Z를 -6 거리로 빼서 더 멀고 넓게 봅니다.
    public Vector3 offset = new Vector3(0, 3.5f, -6f);
    public float smoothSpeed = 10f;

    void LateUpdate()
    {
        if (target != null)
        {
            Vector3 desiredPos = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * smoothSpeed);
            transform.LookAt(target);
        }
    }
}
