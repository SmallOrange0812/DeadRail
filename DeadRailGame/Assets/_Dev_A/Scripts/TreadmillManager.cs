using Fusion;
using UnityEngine;

public class TreadmillManager : NetworkBehaviour
{
    public static TreadmillManager Instance { get; private set; }

    [Header("Treadmill Settings")]
    [Tooltip("현재 트레드밀(기차)의 이동 속도")]
    [Networked] public float CurrentSpeed { get; set; }

    public float maxSpeed = 20f;

    // 추가: 네트워크 스폰 완료 여부를 확인하는 플래그
    public bool isSpawned = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // 추가: Fusion 엔진이 네트워크 상에 이 객체를 완전히 생성(Spawn)했을 때 자동 실행됨
    public override void Spawned()
    {
        isSpawned = true;
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            CurrentSpeed = maxSpeed;
        }
    }
}