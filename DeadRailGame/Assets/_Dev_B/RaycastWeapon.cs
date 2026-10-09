using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaycastWeapon : NetworkBehaviour
{
    [Header("Weapon Status")]
    [Tooltip("장비 획득 여부 (체크하면 쏠 수 있습니다)")]
    public bool hasWeapon = false; // 기본값은 false (무기 없음)

    [Header("Weapon Settings")]
    public float attackRange = 50f;
    private Transform firePoint;

    [Header("Aim Assist")]
    public float hitRadius = 0.5f;
    public LayerMask targetLayer;

    public override void Spawned()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>();
        foreach (Transform child in allChildren)
        {
            if (child.name == "FirePoint")
            {
                firePoint = child;
                break;
            }
        }
        if (firePoint == null) firePoint = this.transform;
    }

    private void Update()
    {
        if (!HasStateAuthority) return;

        // 장비를 얻지 않았다면 여기서 입력을 완전히 차단합니다.
        if (!hasWeapon) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            FireWeapon();
        }
    }

    private void FireWeapon()
    {
        // 이중 안전장치
        if (!hasWeapon || firePoint == null) return;

        Debug.DrawRay(firePoint.position, firePoint.forward * attackRange, Color.red, 2f);

        Collider hitCollider = null;
        RaycastHit hit;

        // 1. 초근접 검사: 캐릭터와 겹칠 정도로 가까운 거리에 있는 장애물 감지
        Collider[] closeHits = Physics.OverlapSphere(firePoint.position, hitRadius, targetLayer);
        if (closeHits.Length > 0)
        {
            hitCollider = closeHits[0];
        }
        // 2. 겹친 게 없다면 기존처럼 전방으로 SphereCast 발사
        else if (Physics.SphereCast(firePoint.position, hitRadius, firePoint.forward, out hit, attackRange, targetLayer))
        {
            hitCollider = hit.collider;
        }

        // 결과 처리
        if (hitCollider != null)
        {
            Debug.Log($"🎯 명중! 맞은 오브젝트: {hitCollider.name}");

            TreadmillObstacle target = hitCollider.GetComponent<TreadmillObstacle>();
            if (target != null) target.Rpc_TakeDamage();
        }
        else
        {
            Debug.Log("💨 허공에 빗나갔습니다.");
        }
    }

    // 아이템을 먹었을 때 호출해줄 함수
    public void AcquireWeapon()
    {
        hasWeapon = true;
        Debug.Log("🔫 장비 획득 완료! 이제 사격이 가능합니다.");
    }

    private void OnGUI()
    {
        // 권한이 없거나, 장비가 없으면 십자선을 그리지 않고 종료
        if (!HasStateAuthority || !hasWeapon) return;

        float x = Screen.width / 2f;
        float y = Screen.height / 2f;
        float size = 10f;
        float thickness = 2f;

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x - size / 2f, y - thickness / 2f, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y - size / 2f, thickness, size), Texture2D.whiteTexture);
    }
}