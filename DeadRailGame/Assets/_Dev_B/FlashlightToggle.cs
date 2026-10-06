using UnityEngine;
using UnityEngine.InputSystem;

public class FlashlightToggle : MonoBehaviour
{
    [Header("탐조등 조명")]
    public Light spotlight;

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
                Debug.Log("❌ 인스펙터에 조명이 연결되지 않았습니다! 빈칸을 확인해주세요.");
            }
        }
    }
}