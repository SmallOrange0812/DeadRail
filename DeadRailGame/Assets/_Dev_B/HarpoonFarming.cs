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

    public override void Spawned()
    {
        if (lineRenderer == null) lineRenderer = GetComponentInChildren<LineRenderer>(true);

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
        }

        playerCameras = GetComponentsInChildren<Camera>(true);
    }

    private void Update()
    {
        if (!HasStateAuthority) return;

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
        // TreadmillManager가 존재하고 속도 변수가 있다면 반영 (추후 팀원 코드의 변수명에 맞춰 자동 연동)
        if (TreadmillManager.Instance != null)
        {
            // 만약 팀원의 TreadmillManager 속도 변수명이 다르다면 이 부분만 맞춰주면 됩니다.
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

        // 1. 화면 정중앙에서 광선 생성
        Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);
        Ray ray = activeCam.ScreenPointToRay(screenCenter);
        Vector3 startPos = GetOriginPos();

        // 2. 주행 중 파밍 개선: 얇은 RaycastAll 대신 넉넉한 반경(netRadius)의 SphereCastAll 사용!
        RaycastHit[] hits = Physics.SphereCastAll(ray, netRadius, maxDistance);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // 1순위: 범위 안에 아이템(Item)이 있으면 무조건 아이템부터 최우선으로 낚아챔!
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

        // 2순위: 아이템이 없을 때는 정확한 중심선(Raycast) 기준으로 스위치/장애물/벽 타격 판정
        if (Physics.Raycast(ray, out RaycastHit directHit, maxDistance))
        {
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

        // 3순위: 허공 발사
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
            // 선로 변환기(TrackSwitch) 스크립트 연동 (컴포넌트가 없어도 SendMessage로 유연하게 호출)
            targetTransform.SendMessageUpwards("OnSwitchHit", SendMessageOptions.DontRequireReceiver);
            targetTransform.SendMessageUpwards("Rpc_ToggleSwitch", SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            // 장애물(TreadmillObstacle) Rpc_TakeDamage 호출 통일
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
        if (!HasStateAuthority) return;

        RaycastWeapon weapon = GetComponent<RaycastWeapon>();
        if (weapon != null && weapon.hasWeapon) return;

        float x = Screen.width / 2f;
        float y = Screen.height / 2f;
        float size = 14f;
        float thickness = 2f;

        GUI.color = hasHarpoon ? Color.cyan : Color.white;
        GUI.DrawTexture(new Rect(x - size / 2f, y - thickness / 2f, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - thickness / 2f, y - size / 2f, thickness, size), Texture2D.whiteTexture);
    }
}