using Fusion;
using UnityEngine;

public class TreadmillManager : NetworkBehaviour
{
    public static TreadmillManager Instance { get; private set; }

    [Header("Treadmill Settings")]
    public float maxSpeed = 20f;

    [Header("열차 자원 설정")]
    [Min(1f)] public float fuelCapacity = 100f;
    [Min(1f)] public float batteryCapacity = 100f;
    [Min(0f)] public float fuelUnitsPerSecondAt100Speed = 0.1f;
    [Min(0f)] public float batteryUnitsPerSecondPerLight = 0.15f;

    // 포톤 네트워크 동기화 속도 변수 (내부용)
    [Networked] private float _networkedSpeed { get; set; }
    [Networked] public float Fuel { get; set; }
    [Networked] public float Battery { get; set; }

    // ★ 외부 스크립트(Scenery, UI 등)에서 읽을 때 에러가 나지 않도록 안전장치 적용
    public float CurrentSpeed => (Object != null && Object.IsValid) ? _networkedSpeed : 0f;

    // 기존 스크립트들과의 호환성을 위한 스폰 확인 프로퍼티
    public bool isSpawned => (Object != null && Object.IsValid);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void Spawned()
    {
        // 방장은 기지에서 정지한 상태와 자원 초기값을 한 번만 설정합니다.
        if (HasStateAuthority)
        {
            _networkedSpeed = 0f;
            Fuel = Mathf.Max(0f, fuelCapacity);
            Battery = Mathf.Max(0f, batteryCapacity);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float deltaTime = Runner.DeltaTime;
        float speed = CurrentSpeed;

        if (speed > 0f && Fuel > 0f)
        {
            float speedRatio = Mathf.Clamp01(speed / 100f);
            Fuel = Mathf.Max(0f, Fuel - fuelUnitsPerSecondAt100Speed * speedRatio * deltaTime);

            if (Fuel <= 0f)
            {
                _networkedSpeed = 0f;
                Debug.LogWarning("⛽ 연료가 바닥나 열차가 멈췄습니다.");
            }
        }

        if (Battery <= 0f || batteryUnitsPerSecondPerLight <= 0f) return;

        int activeLightCount = 0;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject))
                continue;

            if (playerObject.TryGetComponent(out FlashlightToggle flashlight) &&
                flashlight.IsLightOn)
            {
                activeLightCount++;
            }
        }

        if (activeLightCount > 0)
        {
            float previousBattery = Battery;
            Battery = Mathf.Max(
                0f,
                Battery - batteryUnitsPerSecondPerLight * activeLightCount * deltaTime);

            if (previousBattery > 0f && Battery <= 0f)
            {
                Debug.LogWarning("🔋 배터리가 바닥나 조명이 꺼집니다.");
            }
        }
    }

    // ★ 추가됨: P1 해킹 퍼즐 성공 시 호출되어 기차 속도를 실제로 변경하는 네트워크 함수
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetTrainSpeed(float newSpeed)
    {
        if (Fuel <= 0f && newSpeed > 0f)
        {
            _networkedSpeed = 0f;
            Debug.LogWarning("⛽ 연료가 없어 열차를 출발시킬 수 없습니다.");
            return;
        }

        _networkedSpeed = Mathf.Clamp(newSpeed, 0f, 100f);
        Debug.Log($"🚂 [TreadmillManager] 기차 속도가 {_networkedSpeed}km/h로 변경되었습니다!");
    }
}
