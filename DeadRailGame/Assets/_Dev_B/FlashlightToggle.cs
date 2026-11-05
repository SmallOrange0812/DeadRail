using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;

public class FlashlightToggle : NetworkBehaviour
{
    private Light spotlight;

    // 네트워크로 모든 플레이어에게 동기화되는 조명 상태 변수
    [Networked] public NetworkBool IsLightOn { get; set; }

    public override void Spawned()
    {
        // 네트워크 상에 캐릭터가 스폰될 때 자식 오브젝트에서 Light를 찾습니다.
        spotlight = GetComponentInChildren<Light>(true);

        if (spotlight != null)
        {
            // 내가 조작하는 캐릭터라면 처음에 꺼진 상태로 시작
            if (HasStateAuthority)
            {
                IsLightOn = false;
            }
            spotlight.enabled = IsLightOn;
            Debug.Log("💡 조명 자동 찾기 및 네트워크 연동 성공!");
        }
        else
        {
            Debug.LogWarning("❌ 조명을 찾을 수 없습니다. 플레이어 자식으로 Light가 있는지 확인해주세요!");
        }
    }

    private void Update()
    {
        // 1. 내 캐릭터(권한이 있는 플레이어)가 아니면 키 입력을 받지 않음 (다른 사람 조명을 내가 끄면 안 되니까!)
        if (!HasStateAuthority) return;

        // 2. 키보드 연결 확인 및 F키 입력 감지
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            IsLightOn = !IsLightOn;
            Debug.Log("🔦 [네트워크] 현재 조명 상태 변경: " + IsLightOn);
        }
    }

    public override void Render()
    {
        // 매 프레임 렌더링 시점에 네트워크 변수(IsLightOn) 상태와 실제 전구(spotlight.enabled) 상태를 일치시킴
        // (내 화면뿐만 아니라 다른 플레이어 화면에서도 즉시 반영됨)
        if (spotlight != null && spotlight.enabled != IsLightOn)
        {
            spotlight.enabled = IsLightOn;
        }
    }
}