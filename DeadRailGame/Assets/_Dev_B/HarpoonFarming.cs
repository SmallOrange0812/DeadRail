using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;

public class HarpoonFarming : NetworkBehaviour
{
    [Header("References")]
    public LineRenderer lineRenderer;
    [Tooltip("작살 줄이 시작될 위치 (없으면 캐릭터 가슴 높이에서 발사)")]
    public Transform harpoonMuzzle;

    [Header("Magnetic Net / Harpoon Settings (광역 그물 작살)")]
    [Tooltip("주행 중 파밍을 쉽게 하기 위한 그물/자기장 판정 반경")]
    public float netRadius = 1.5f;
    public float basePullSpeed = 20f;
    public float maxDistance = 500f;
    public int harpoonDamage = 1;

    [Header("Harpoon Status")]
    [Tooltip("체크되어 있으면 처음부터 허공/장애물/스위치 사격 가능")]
    public bool hasHarpoon = true;

    // --- Photon Fusion 네트워크 동기화 변수 (LineRenderer 공유용) ---
    [Networked] private NetworkBool IsLineActive { get; set; }
    [Networked] private Vector3 LineStart { get; set; }
    [Networked] private Vector3 LineEnd { get; set; }

    private bool isFarming = false;
    private Camera[] playerCameras;
    private PlayerMovement playerMovement;

    public override void Spawned()
    {
        // ✅ 게임 스폰 시 무조건 작살을 장착한 상태로 강제 초기화
        hasHarpoon = true;

        if (lineRenderer == null) lineRenderer = GetComponentInChildren<LineRenderer>(true);

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
        }

        playerCameras = GetComponentsInChildren<Camera>(true);
        playerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || playerMovement == null || !playerMovement.IsLocalPlayer) return;
        if ((TerminalUIManager.Instance != null && TerminalUIManager.Instance.IsUiOpen) ||
            (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready && RailRouteLoop.Instance.IsTransition)) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !isFarming)
        {
            TryFarming();
        }
    }

    public override void Render()
    {
        if (lineRenderer == null) return;

        lineRenderer.enabled = IsLineActive;
        if (IsLineActive)
        {
            lineRenderer.SetPosition(0, LineStart);
            lineRenderer.SetPosition(1, LineEnd);
        }
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

    private Vector3 GetOriginPos()
    {
        return harpoonMuzzle != null ? harpoonMuzzle.position : transform.position + Vector3.up * 1.2f;
    }

    // 기차 주행 속도를 고려한 최종 견인 속도 계산
    private float GetEffectivePullSpeed()
    {
        float trainSpeed = 0f;
        if (TreadmillManager.Instance != null)
        {
            // trainSpeed = TreadmillManager.Instance.CurrentSpeed;
        }
        return basePullSpeed + trainSpeed;
    }

    private void TryFarming()
    {
        Camera activeCam = GetActiveCamera();
        if (activeCam == null)
        {
            Debug.LogWarning("❌ [HarpoonFarming] 활성화된 카메라를 찾을 수 없습니다!");
            return;
        }

        Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        Ray ray = activeCam.ScreenPointToRay(screenCenter);
        Rpc_FireHarpoon(ray.origin, ray.direction);
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_FireHarpoon(Vector3 origin, Vector3 direction)
    {
        EquipmentPivotController equipment = GetComponent<EquipmentPivotController>();
        if (isFarming || (equipment != null && equipment.CurrentMode != EquipmentPivotController.EquipmentMode.P3_HarpoonMount)) return;
        if (!float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z) ||
            !float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z)) return;
        if (Vector3.Distance(origin, transform.position) > 6f || direction.sqrMagnitude < 0.5f) return;
        if (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready && RailRouteLoop.Instance.IsTransition) return;
        Ray ray = new Ray(origin, direction.normalized);
        Vector3 startPos = GetOriginPos();
        if (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready)
        {
            RailRouteLoop.Instance.RefreshCollisionGeometry();
            Physics.SyncTransforms();
        }

        RaycastHit[] hits = Physics.SphereCastAll(ray, netRadius, maxDistance);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.root == this.transform.root) continue;

            if (hit.transform.CompareTag("Item"))
            {
                Debug.Log($"🎣 [그물 작살 명중] 보급품 포착: {hit.transform.name}");
                StartCoroutine(PullItemCoroutine(hit.transform));
                return;
            }
        }

        RaycastHit[] directHits = Physics.RaycastAll(ray, maxDistance);
        System.Array.Sort(directHits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit directHit in directHits)
        {
            RailRouteLoop route = RailRouteLoop.Instance;
            if (route != null && route.Ready && route.trainTransform != null &&
                directHit.transform.IsChildOf(route.trainTransform)) continue;
            if (directHit.transform.root != this.transform.root)
            {
                if (directHit.transform.CompareTag("TrackSwitch"))
                {
                    Debug.Log("🔀 [작살 명중] 선로 변환기(TrackSwitch) 타격!");
                    StartCoroutine(HitSwitchOrObstacleCoroutine(directHit.transform, directHit.point, isSwitch: true));
                    return;
                }
                else if (directHit.transform.CompareTag("Obstacle"))
                {
                    if (!hasHarpoon) return;
                    Debug.Log("💥 [작살 명중] 전방 장애물 타격!");
                    StartCoroutine(HitSwitchOrObstacleCoroutine(directHit.transform, directHit.point, isSwitch: false));
                    return;
                }
                else
                {
                    if (!hasHarpoon) return;
                    Debug.Log($"🧱 [작살 빗나감] 지형/사물 맞춤: {directHit.transform.name}");
                    StartCoroutine(DrawLineShortCoroutine(startPos, directHit.point));
                    return;
                }
            }
        }

        if (hasHarpoon)
        {
            Debug.Log("💨 [작살 빗나감] 허공 발사!");
            Vector3 missPoint = ray.origin + ray.direction * maxDistance;
            StartCoroutine(DrawLineShortCoroutine(startPos, missPoint));
        }
    }

    private IEnumerator PullItemCoroutine(Transform targetItem)
    {
        isFarming = true;
        IsLineActive = true;

        Collider[] colliders = targetItem.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders) col.enabled = false;

        Rigidbody itemRb = targetItem.GetComponent<Rigidbody>();
        if (itemRb != null) itemRb.isKinematic = true;

        float timeout = 10.0f;

        while (targetItem != null && Vector3.Distance(targetItem.position, GetOriginPos()) > 0.3f && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            Vector3 currentStart = GetOriginPos();
            float currentPullSpeed = GetEffectivePullSpeed();

            // ✅ 오류 수정: * 기호 추가
            targetItem.position = Vector3.MoveTowards(targetItem.position, currentStart, currentPullSpeed * Time.deltaTime);

            LineStart = currentStart;
            LineEnd = targetItem.position;

            yield return null;
        }

        if (targetItem != null)
        {
            RaycastWeapon weapon = GetComponent<RaycastWeapon>();
            if (weapon != null) weapon.AcquireWeapon();

            hasHarpoon = true;
            Destroy(targetItem.gameObject);
            Debug.Log("✅ 보급품 견인 및 획득 완료!");
        }

        IsLineActive = false;
        isFarming = false;
    }

    private IEnumerator HitSwitchOrObstacleCoroutine(Transform targetTransform, Vector3 hitPoint, bool isSwitch)
    {
        isFarming = true;
        IsLineActive = true;
        LineStart = GetOriginPos();
        LineEnd = hitPoint;

        if (isSwitch)
        {
            TrackSwitch targetSwitch = targetTransform.GetComponentInParent<TrackSwitch>();
            if (targetSwitch != null)
                targetSwitch.OnSwitchHit();
            else
                Debug.LogWarning("TrackSwitch 태그가 있는 충돌체에서 TrackSwitch 컴포넌트를 찾지 못했습니다.");
        }
        else
        {
            TreadmillObstacle obstacle = targetTransform.GetComponentInParent<TreadmillObstacle>();
            if (obstacle != null)
            {
                obstacle.Rpc_TakeDamage();
            }
            else
            {
                Destroy(targetTransform.gameObject);
            }
        }

        yield return new WaitForSeconds(0.15f);

        IsLineActive = false;
        isFarming = false;
    }

    private IEnumerator DrawLineShortCoroutine(Vector3 start, Vector3 end)
    {
        isFarming = true;
        IsLineActive = true;
        LineStart = start;
        LineEnd = end;

        yield return new WaitForSeconds(0.1f);

        IsLineActive = false;
        isFarming = false;
    }

    private void OnGUI()
    {
        if (Object == null || !Object.IsValid || playerMovement == null || !playerMovement.IsLocalPlayer) return;

        RaycastWeapon weapon = GetComponent<RaycastWeapon>();
        if (weapon != null && weapon.hasWeapon) return;

        float x = Screen.width / 2f;
        float y = Screen.height / 2f;
        float size = 14f;
        float thickness = 2f;

        GUI.color = hasHarpoon ? Color.cyan : Color.white;

        // ✅ 오류 수정: 줄바꿈으로 끊어졌던 코드 한 줄로 복구
        GUI.DrawTexture(new Rect(x - size / 2f, y - thickness / 2f, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y - size / 2f, thickness, size), Texture2D.whiteTexture);
    }
}
