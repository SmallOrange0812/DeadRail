using Fusion;
using UnityEngine;

public class TreadmillManager : NetworkBehaviour
{
    public static TreadmillManager Instance { get; private set; }

    [Header("Treadmill Settings")]
    public float maxSpeed = 20f;

    // 포톤 네트워크 동기화 속도 변수 (내부용)
    [Networked] private float _networkedSpeed { get; set; }

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
        // 방장(StateAuthority)이 처음 스폰되었을 때 딱 한 번 기본 속도로 출발!
        if (HasStateAuthority)
        {
            _networkedSpeed = maxSpeed;
        }
    }

    // ★ 추가됨: P1 해킹 퍼즐 성공 시 호출되어 기차 속도를 실제로 변경하는 네트워크 함수
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetTrainSpeed(float newSpeed)
    {
        _networkedSpeed = Mathf.Clamp(newSpeed, 0f, 100f);
        Debug.Log($"🚂 [TreadmillManager] 기차 속도가 {_networkedSpeed}km/h로 변경되었습니다!");
    }
}