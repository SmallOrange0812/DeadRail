using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaycastWeapon : NetworkBehaviour
{
    [Header("Weapon Settings")]
    public float attackRange = 50f;
    public Transform firePoint;

    [Header("Aim Assist")]
    public float hitRadius = 0.5f;

    [Tooltip("총알이 맞을 레이어 (예: Enemy, Obstacle)")]
    public LayerMask targetLayer;

    private void Update()
    {
        if (!HasStateAuthority) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            FireWeapon();
        }
    }

    private void FireWeapon()
    {
        Debug.DrawRay(firePoint.position, firePoint.forward * attackRange, Color.red, 2f);
        Debug.DrawRay(firePoint.position + (Vector3.up * hitRadius), firePoint.forward * attackRange, Color.yellow, 2f);
        Debug.DrawRay(firePoint.position + (Vector3.down * hitRadius), firePoint.forward * attackRange, Color.yellow, 2f);

        // 광선을 쏠 때 targetLayer(장애물)만 골라서 맞추도록 제한
        if (Physics.SphereCast(firePoint.position, hitRadius, firePoint.forward, out RaycastHit hit, attackRange, targetLayer))
        {
            Debug.Log($"🎯 명중! 맞은 오브젝트: {hit.collider.name}");

            TreadmillObstacle target = hit.collider.GetComponent<TreadmillObstacle>();
            if (target != null)
            {
                target.Rpc_TakeDamage();
            }
        }
        else
        {
            Debug.Log("💨 허공에 빗나갔습니다.");
        }
    }

    private void OnGUI()
    {
        if (!HasStateAuthority) return;

        float x = Screen.width / 2;
        float y = Screen.height / 2;
        int size = 10;
        int thickness = 2;

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x - thickness / 2, y - size / 2, thickness, size), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - size / 2, y - thickness / 2, size, thickness), Texture2D.whiteTexture);
    }
}