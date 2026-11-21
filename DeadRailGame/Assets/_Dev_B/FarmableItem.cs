using UnityEngine;

public class FarmableItem : MonoBehaviour
{
    public bool isPulled = false; // 작살에 맞았는지 여부
    public Transform pullTarget;  // 끌려갈 목표 지점 (P2)
    public float pullSpeed = 15f; // 끌려오는 속도

    void Update()
    {
        if (isPulled && pullTarget != null)
        {
            // 플레이어(도착점)를 향해 이동
            transform.position = Vector3.MoveTowards(transform.position, pullTarget.position, pullSpeed * Time.deltaTime);

            // 플레이어에게 충분히 가까워지면 파밍 완료(삭제)
            if (Vector3.Distance(transform.position, pullTarget.position) < 1.0f)
            {
                Debug.Log("📦 아이템 획득 완료!");
                Destroy(gameObject);
            }
        }
    }
}