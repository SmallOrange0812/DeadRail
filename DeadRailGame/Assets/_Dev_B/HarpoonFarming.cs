using UnityEngine;
using UnityEngine.InputSystem;

public class HarpoonFarming : MonoBehaviour
{
    [Header("파밍 설정")]
    public Camera p2Camera;
    public float harpoonRange = 100f;
    public Transform farmTarget;

    [Header("시각 효과 (밧줄)")]
    public LineRenderer harpoonLine; // 추가된 선 그리기 도구
    public Transform firePoint;      // 작살이 발사되는 시작점 (총구)

    private FarmableItem pulledItem; // 현재 끌려오고 있는 아이템 기억하기

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryFarming();
        }

        // 매 프레임마다 밧줄의 위치를 업데이트
        UpdateHarpoonLine();
    }

    void TryFarming()
    {
        Ray ray = new Ray(p2Camera.transform.position, p2Camera.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, harpoonRange))
        {
            FarmableItem item = hit.collider.GetComponent<FarmableItem>();

            if (item != null && !item.isPulled)
            {
                item.pullTarget = farmTarget;
                item.isPulled = true;

                // 성공적으로 맞춘 아이템을 기억해둠
                pulledItem = item;
                Debug.Log("🎯 작살 명중! 밧줄을 연결합니다.");
            }
        }
    }

    void UpdateHarpoonLine()
    {
        if (harpoonLine == null || firePoint == null) return;

        // 끌려오는 아이템이 남아있다면 밧줄을 표시
        if (pulledItem != null)
        {
            harpoonLine.enabled = true;
            harpoonLine.SetPosition(0, firePoint.position);            // 밧줄 시작: 내 총구
            harpoonLine.SetPosition(1, pulledItem.transform.position); // 밧줄 끝: 날아오는 아이템
        }
        else // 아이템이 도착해서 삭제되었거나(null), 아직 안 쐈다면 밧줄 숨기기
        {
            harpoonLine.enabled = false;
        }
    }
}