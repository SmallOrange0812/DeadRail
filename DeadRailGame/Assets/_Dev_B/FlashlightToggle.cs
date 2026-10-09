using UnityEngine;
using UnityEngine.InputSystem;

public class FlashlightToggle : MonoBehaviour
{
    // public을 private으로 변경하여 인스펙터에서 아예 숨깁니다 (꼬임 방지)
    private Light spotlight;

    private void Start()
    {
        // 게임 시작 시, 내 하위 오브젝트들 중에서 Light(전구)를 무조건 스스로 찾아냅니다.
        spotlight = GetComponentInChildren<Light>();

        if (spotlight != null)
        {
            spotlight.enabled = false; // 시작할 때는 꺼두기
            Debug.Log("💡 조명 자동 찾기 성공!");
        }
    }

    private void Update()
    {
        // 1. 키보드 연결 자체가 안 되어있으면 무시
        if (Keyboard.current == null) return;

        // 2. F키를 눌렀을 때
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            Debug.Log("✅ 키보드 F키가 정상적으로 눌렸습니다!");

            if (spotlight != null)
            {
                spotlight.enabled = !spotlight.enabled;
                Debug.Log("🔦 현재 조명 상태: " + spotlight.enabled);
            }
            else
            {
                Debug.Log("❌ 조명을 찾을 수 없습니다. 플레이어(프리팹) 자식으로 Spotlight가 있는지 확인해주세요!");
            }
        }
    }
}