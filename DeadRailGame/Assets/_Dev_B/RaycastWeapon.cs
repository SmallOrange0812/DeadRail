using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaycastWeapon : NetworkBehaviour
{
    [Header("Weapon Status")]
    [Tooltip("장비 획득 여부 (체크하면 즉시 사격 가능)")]
    public bool hasWeapon = false;

    [Header("Weapon Spec (P3 거치포 / P1·P4 보조권총 공용 모듈)")]
    public string weaponName = "MountedTurret";
    public int weaponDamage = 10;
    public float attackRange = 80f;
    private Transform firePoint;

    [Header("Aim Assist")]
    public float hitRadius = 0.5f;
    [Tooltip("비워두거나 Everything으로 두면 모든 레이어(장애물/스위치)를 감지합니다")]
    public LayerMask targetLayer = ~0;

    private Camera[] playerCameras;

    public override void Spawned()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            if (child.name == "FirePoint")
            {
                firePoint = child;
                break;
            }
        }
        if (firePoint == null) firePoint = this.transform;

        playerCameras = GetComponentsInChildren<Camera>(true);
    }

    private Camera GetActiveCamera()
    {
        if (playerCameras != null)
        {
            foreach (Camera cam in playerCameras)
            {
                if (cam != null && cam.isActiveAndEnabled && cam.gameObject.activeInHierarchy)
                    return cam;
            }
        }
        return Camera.main;
    }

    private void Update()
    {
        if (!HasStateAuthority) return;
        if (!hasWeapon) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            FireWeapon();
        }
    }

    private void FireWeapon()
    {
        if (!hasWeapon || firePoint == null) return;

        Camera activeCam = GetActiveCamera();
        Vector3 rayOrigin = activeCam != null ? activeCam.transform.position : firePoint.position;
        Vector3 rayDir = activeCam != null ? activeCam.transform.forward : firePoint.forward;

        Debug.DrawRay(rayOrigin, rayDir * attackRange, Color.red, 2f);

        Collider hitCollider = null;

        // 1. 초근접 검사: 캐릭터와 겹친 타겟 감지
        Collider[] closeHits = Physics.OverlapSphere(firePoint.position, hitRadius, targetLayer);
        foreach (Collider col in closeHits)
        {
            if (col.transform.root == this.transform.root) continue;
            if (col.CompareTag("Obstacle") || col.CompareTag("TrackSwitch"))
            {
                hitCollider = col;
                break;
            }
        }

        // 2. 전방 SphereCast 발사 (본인 캐릭터 제외)
        if (hitCollider == null)
        {
            RaycastHit[] hits = Physics.SphereCastAll(rayOrigin, hitRadius, rayDir, attackRange, targetLayer);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.root == this.transform.root) continue;
                hitCollider = hit.collider;
                break;
            }
        }

        // 3. 타격 결과 처리 (장애물 파괴 or 선로 변환기 작동)
        if (hitCollider != null)
        {
            if (hitCollider.CompareTag("TrackSwitch"))
            {
                Debug.Log($"🔀 [{weaponName}] 선로 변환기(TrackSwitch) 명중! 궤도 변경 신호 전송!");
                hitCollider.SendMessageUpwards("OnSwitchHit", SendMessageOptions.DontRequireReceiver);
                hitCollider.SendMessageUpwards("Rpc_ToggleSwitch", SendMessageOptions.DontRequireReceiver);
            }
            else
            {
                TreadmillObstacle target = hitCollider.GetComponentInParent<TreadmillObstacle>();
                if (target != null)
                {
                    Debug.Log($"🎯 [{weaponName}] 장애물 명중! (데미지: {weaponDamage}) -> {hitCollider.name}");
                    target.Rpc_TakeDamage();
                }
                else
                {
                    Debug.Log($"🎯 [{weaponName}] 일반 오브젝트 타격: {hitCollider.name}");
                }
            }
        }
        else
        {
            Debug.Log($"💨 [{weaponName}] 허공에 빗나갔습니다.");
        }
    }

    public void AcquireWeapon()
    {
        hasWeapon = true;
        Debug.Log($"🔫 [{weaponName}] 장비 활성화 완료! 이제 사격이 가능합니다.");
    }

    private void OnGUI()
    {
        if (!HasStateAuthority || !hasWeapon) return;

        float x = Screen.width / 2f;
        float y = Screen.height / 2f;
        float size = 12f;
        float thickness = 2f;

        GUI.color = Color.red; // 사격 무기 활성화 시 붉은색 십자선으로 구분!
        GUI.DrawTexture(new Rect(x - size / 2f, y - thickness / 2f, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y - size / 2f, thickness, size), Texture2D.whiteTexture);
    }
}