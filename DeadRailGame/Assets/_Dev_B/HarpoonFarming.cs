using System.Collections;
using UnityEngine;

public class HarpoonFarming : MonoBehaviour
{
    public Camera p2Camera;
    public LineRenderer lineRenderer;
    public float pullSpeed = 15f;
    public float maxDistance = 500f;

    [Header("Harpoon Status")]
    public bool hasHarpoon = false; // 작살 무기 획득 여부

    private bool isFarming = false;

    void Start()
    {
        if (p2Camera == null) p2Camera = Camera.main;
        if (lineRenderer == null) lineRenderer = GetComponentInChildren<LineRenderer>();

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
            lineRenderer.positionCount = 2;
        }
    }

    void Update()
    {
        // 마우스 클릭 시 Raycast 사격
        if (Input.GetMouseButtonDown(0) && !isFarming)
        {
            TryFarming();
        }
    }

    void TryFarming()
    {
        // 🎯 1. 기존 중앙 고정이 아닌, 현재 마우스 위치에서 광선(Ray)을 쏩니다.
        Ray ray = p2Camera.ScreenPointToRay(Input.mousePosition);
        Vector3 myBody = transform.position + Vector3.up * 1.0f;

        // 장애물 코앞 겹침 대비용으로 RaycastAll 적용
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool hitSomething = false;

        foreach (RaycastHit hit in hits)
        {
            // 플레이어 자신은 충돌에서 통과시킴
            if (hit.transform.root == this.transform.root) continue;

            hitSomething = true;

            if (hit.transform.CompareTag("Item"))
            {
                Debug.Log("명중: 아이템 당기기 시작!");
                StartCoroutine(PullItem(hit.transform));
                return;
            }
            else if (hit.transform.CompareTag("Obstacle"))
            {
                if (!hasHarpoon) return;
                Debug.Log("명중: 장애물 파괴!");
                StartCoroutine(HitObstacle(hit.transform, hit.point));
                return;
            }
            else
            {
                if (!hasHarpoon) return;
                Debug.Log("빗나감: 일반 벽/바닥 맞춤");
                StartCoroutine(DrawLineShort(myBody, hit.point));
                return;
            }
        }

        if (!hitSomething)
        {
            if (!hasHarpoon) return;
            Debug.Log("빗나감: 허공을 쏨");
            Vector3 missPoint = ray.origin + ray.direction * maxDistance;
            StartCoroutine(DrawLineShort(myBody, missPoint));
        }
    }

    IEnumerator PullItem(Transform targetItem)
    {
        isFarming = true;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
        }

        Collider[] colliders = targetItem.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        Rigidbody itemRb = targetItem.GetComponent<Rigidbody>();
        if (itemRb != null) itemRb.isKinematic = true;

        float timeout = 10.0f;
        Vector3 myBody = transform.position + Vector3.up * 1.0f;

        while (targetItem != null && Vector3.Distance(targetItem.position, myBody) > 0.1f && timeout > 0)
        {
            timeout -= Time.deltaTime;
            myBody = transform.position + Vector3.up * 1.0f;

            if (lineRenderer != null)
            {
                lineRenderer.SetPosition(0, myBody);
                lineRenderer.SetPosition(1, targetItem.position);
            }

            targetItem.position = Vector3.MoveTowards(targetItem.position, myBody, pullSpeed * Time.deltaTime);
            yield return null;
        }

        if (targetItem != null)
        {
            Destroy(targetItem.gameObject);
            Debug.Log("아이템 획득 완료!");

            hasHarpoon = true; // 아이템을 먹었으므로 무기 활성화
        }

        if (lineRenderer != null) lineRenderer.enabled = false;
        isFarming = false;
    }

    IEnumerator HitObstacle(Transform obstacle, Vector3 hitPoint)
    {
        isFarming = true;
        Vector3 myBody = transform.position + Vector3.up * 1.0f;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.SetPosition(0, myBody);
            lineRenderer.SetPosition(1, hitPoint);
        }

        yield return new WaitForSeconds(0.15f);

        if (obstacle != null)
        {
            Destroy(obstacle.gameObject);
            Debug.Log("장애물 파괴 완료!");
        }
        if (lineRenderer != null) lineRenderer.enabled = false;
        isFarming = false;
    }

    IEnumerator DrawLineShort(Vector3 start, Vector3 end)
    {
        isFarming = true;
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
        }
        yield return new WaitForSeconds(0.1f);
        if (lineRenderer != null) lineRenderer.enabled = false;
        isFarming = false;
    }

    // 🎯 2. 화면 중앙이 아니라 마우스 위치에 조준점(십자선)을 그려줍니다.
    private void OnGUI()
    {
        if (!hasHarpoon) return;

        // 마우스의 현재 위치를 GUI 좌표로 가져옵니다 (유니티 GUI는 Y축이 반대이므로 변환)
        float mouseX = Input.mousePosition.x;
        float mouseY = Screen.height - Input.mousePosition.y;

        float size = 16f;      // 조준선 크기 살짝 키움
        float thickness = 2f;  // 두께

        // 마우스 포인터를 숨기거나, 조준점과 겹쳐서 보이게 할 수 있습니다.
        // 마우스 커서를 아예 안 보이게 하려면 게임 시작 시 Cursor.visible = false; 처리를 해주면 좋습니다.

        GUI.color = Color.white;
        // 가로선
        GUI.DrawTexture(new Rect(mouseX - size / 2f, mouseY - thickness / 2f, size, thickness), Texture2D.whiteTexture);
        // 세로선
        GUI.DrawTexture(new Rect(mouseX - thickness / 2f, mouseY - size / 2f, thickness, size), Texture2D.whiteTexture);
    }
}