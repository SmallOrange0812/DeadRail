using Fusion;
using UnityEngine;

public class TreadmillObstacle : NetworkBehaviour
{
    [Header("Obstacle Settings")]
    [Tooltip("기차와 충돌했을 때 입힐 데미지")]
    public int damage = 10;

    public override void FixedUpdateNetwork()
    {
        // 1. 매니저가 없거나 속도가 0이면 정지
        if (TreadmillManager.Instance == null || TreadmillManager.Instance.CurrentSpeed <= 0f) return;

        // 2. 트레드밀 매니저의 속도에 맞춰 -Z 방향으로 이동
        float speed = TreadmillManager.Instance.CurrentSpeed;
        transform.Translate(Vector3.back * speed * Runner.DeltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 권한이 있는 방장만 기차 충돌(데미지)을 처리합니다.
        if (!HasStateAuthority) return;

        if (other.CompareTag("Train"))
        {
            Debug.Log($"🚨 기차에 충돌했습니다! 기차 체력 -{damage} 감소!");
            Runner.Despawn(Object);
        }
    }

    // [이곳이 방금 요청하신 코드가 완벽하게 교체된 자리입니다!]
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_TakeDamage()
    {
        Debug.Log("💥 적/장애물 파괴 완료! (방장 권한으로 안전하게 삭제)");

        // [추가된 안전장치] 삭제되기 전에 물리 충돌 박스를 즉시 꺼버려서 기차를 때리지 못하게 만듭니다.
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Runner.Despawn(Object);
    }
}