using Fusion;
using UnityEngine;

public class TreadmillManager : NetworkBehaviour
{
    public enum FarmingStopPhase { Pending, Stopped, Departed, Cleared }

    public static TreadmillManager Instance { get; private set; }

    [Header("Treadmill Settings")]
    public float maxSpeed = 20f;
    [SerializeField, Min(0.1f)] private float accelerationKmhPerSecond = 6f;

    [Header("열차 자원 설정")]
    [Min(1f)] public float fuelCapacity = 100f;
    [Min(1f)] public float batteryCapacity = 100f;
    [Min(0f)] public float fuelUnitsPerSecondAt100Speed = 0.1f;
    [Min(0f)] public float batteryUnitsPerSecondPerLight = 0.15f;

    // 포톤 네트워크 동기화 속도 변수 (내부용)
    [Networked] private float _networkedSpeed { get; set; }
    [Networked] private float TargetSpeed { get; set; }
    [Networked] private int ClosedEntryDoors { get; set; }
    [Networked] private Vector2 DoorClosure { get; set; }
    [Networked] public float Fuel { get; set; }
    [Networked] public float Battery { get; set; }
    [Networked] private float _fuelCapacity { get; set; }
    [Networked] private float _batteryCapacity { get; set; }
    [Networked] public int MedicalKits { get; private set; }
    [Networked] public int RepairKits { get; private set; }
    [Networked] public int Ammunition { get; private set; }
    public float FuelCapacity => isSpawned ? _fuelCapacity : fuelCapacity;
    public float BatteryCapacity => isSpawned ? _batteryCapacity : batteryCapacity;
    [Networked] public FarmingStopPhase FarmingStop { get; private set; }

    [Header("임시 전력 / 블랙아웃")]
    [Tooltip("발전기 미니게임 성공 후 임시 전력이 유지되는 시간")]
    [SerializeField] private float backupPowerDuration = 60f;
    [Networked] public NetworkBool IsBlackout { get; private set; }
    [Networked] public float BackupPowerRemaining { get; private set; }
    [Networked] public float CrankProgress { get; private set; }
    [Networked] public NetworkBool NextCrankClockwise { get; private set; }
    [Networked] public int CrankStrokeSequence { get; private set; }
    [Networked] public NetworkBool LastCrankClockwise { get; private set; }
    [Networked] public PlayerRef CrankUser { get; private set; }

    private bool _hasSpawned;

    // Object.IsValid may become true before Fusion has initialized Networked
    // properties. Keep a separate lifecycle flag for safe UI/read access.
    public bool HasSpawned => _hasSpawned && Object != null && Object.IsValid;

    public bool HasPower => !HasSpawned || !IsBlackout || BackupPowerRemaining > 0f;

    // ★ 외부 스크립트(Scenery, UI 등)에서 읽을 때 에러가 나지 않도록 안전장치 적용
    public float CurrentSpeed => HasSpawned ? _networkedSpeed : 0f;

    // 기존 스크립트들과의 호환성을 위한 스폰 확인 프로퍼티
    public bool isSpawned => HasSpawned;
    public RailRouteLoop RouteLoop { get; private set; }
    private TrainEntryDoors entryDoors;
    private AudioSource trainSound, powerDown, rechargeSound;
    private bool hadPower;
    internal bool EntryDoorsLocked => CurrentSpeed > 0f || (HasSpawned && TargetSpeed > 0f);
    internal bool CanUseDoorSwitch(int index, Vector3 position) =>
        entryDoors != null && entryDoors.CanUseSwitch(index, position);

    private void Awake()
    {
        RouteLoop = GetComponent<RailRouteLoop>();
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    internal float BrakeForFarmingStop(float remainingDistance)
    {
        const float brakingDeceleration = 24f;
        float brakingSpeed = Mathf.Sqrt(2f * brakingDeceleration * remainingDistance);
        _networkedSpeed = Mathf.Min(_networkedSpeed, Mathf.Max(1f, brakingSpeed));
        return _networkedSpeed;
    }

    internal float ApplyAutomaticStopSpeed(float remainingDistance, float deltaTime)
    {
        const float brakingDeceleration = 4.8f; // m/s²
        const float approachSpeed = 28f; // km/h, used if the train entered at low speed
        float brakingSpeed = Mathf.Sqrt(2f * brakingDeceleration * Mathf.Max(0f, remainingDistance)) * 3.6f;
        float approachTarget = Mathf.Min(brakingSpeed, Mathf.Max(_networkedSpeed, approachSpeed));
        if (Fuel > 0f) _networkedSpeed = Mathf.MoveTowards(_networkedSpeed, approachTarget, accelerationKmhPerSecond * deltaTime);
        _networkedSpeed = Mathf.Min(_networkedSpeed, brakingSpeed);
        return _networkedSpeed;
    }

    internal void ArriveAtFarmingStop()
    {
        _networkedSpeed = 0f;
        TargetSpeed = 0f;
        FarmingStop = FarmingStopPhase.Stopped;
        Debug.Log("[TreadmillManager] 임시 파밍장에 정차했습니다.");
    }

    internal void ClearFarmingStop()
    {
        FarmingStop = FarmingStopPhase.Cleared;
    }

    internal void ResetFarmingStop()
    {
        FarmingStop = FarmingStopPhase.Pending;
    }

    public override void Spawned()
    {
        if (RouteLoop != null && RouteLoop.trainTransform != null)
            entryDoors = RouteLoop.trainTransform.GetComponent<TrainEntryDoors>();
        // 방장은 기지에서 정지한 상태와 자원 초기값을 한 번만 설정합니다.
        if (HasStateAuthority)
            ResetTrainForRun();

        _hasSpawned = true;
        GameObject train = RouteLoop != null && RouteLoop.trainTransform != null ? RouteLoop.trainTransform.gameObject : gameObject;
        trainSound = GameAudio.Attach(train, "train", .45f, 10f, 75f, true);
        powerDown = GameAudio.Attach(train, "power-down", .8f, 10f, 55f);
        rechargeSound = GameAudio.Attach(train, "recharge-sound", .65f, 6f, 38f);
        hadPower = HasPower;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (trainSound != null) Destroy(trainSound);
        if (powerDown != null) Destroy(powerDown);
        if (rechargeSound != null) Destroy(rechargeSound);
        _hasSpawned = false;
    }

    internal const int AmmunitionPerPickup = 30;

    internal void StopForDefeat()
    {
        if (!HasStateAuthority) return;
        _networkedSpeed = 0f;
        TargetSpeed = 0f;
    }

    internal void ResetTrainForRun()
    {
        if (!HasStateAuthority) return;
        StopForDefeat();
        ClosedEntryDoors = 0;
        DoorClosure = Vector2.zero;
        Fuel = Mathf.Max(0f, fuelCapacity);
        Battery = Mathf.Max(0f, batteryCapacity);
        _fuelCapacity = Mathf.Max(1f, fuelCapacity);
        _batteryCapacity = Mathf.Max(1f, batteryCapacity);
        MedicalKits = RepairKits = Ammunition = 0;
        FarmingStop = FarmingStopPhase.Pending;
        IsBlackout = false;
        BackupPowerRemaining = 0f;
        CrankProgress = 0f;
        NextCrankClockwise = true;
        CrankStrokeSequence = 0;
        LastCrankClockwise = true;
        CrankUser = PlayerRef.None;
    }

    internal float LoseCargoFuel(float units)
    {
        if (!HasStateAuthority) return 0f;
        float lost = Mathf.Min(Fuel, units);
        Fuel -= lost;
        return lost;
    }

    internal void RecordRepairKitUsed()
    {
        if (HasStateAuthority) RepairKits = Mathf.Max(0, RepairKits - 1);
    }

    internal bool HasRoomForSupply(FarmingSupplyKind kind) => kind switch
    {
        FarmingSupplyKind.Fuel => Fuel < FuelCapacity,
        FarmingSupplyKind.Battery => Battery < BatteryCapacity,
        _ => true
    };

    internal bool CollectSupply(FarmingSupplyKind kind, float amount)
    {
        if (!HasStateAuthority || amount <= 0f || !HasRoomForSupply(kind)) return false;
        switch (kind)
        {
            case FarmingSupplyKind.FuelCapacity:
                _fuelCapacity += amount;
                break;
            case FarmingSupplyKind.BatteryCapacity:
                _batteryCapacity += amount;
                break;
            case FarmingSupplyKind.MedicalKit:
                MedicalKits++;
                break;
            case FarmingSupplyKind.RepairKit:
                RepairKits++;
                break;
            case FarmingSupplyKind.Ammunition:
                Ammunition += AmmunitionPerPickup;
                break;
            case FarmingSupplyKind.Fuel:
                Fuel = Mathf.Min(FuelCapacity, Fuel + amount);
                break;
            case FarmingSupplyKind.Battery:
                Battery = Mathf.Min(BatteryCapacity, Battery + amount);
                Rpc_PlayRechargeSound();
                break;
            default:
                return false;
        }
        return true;
    }

    private void UpdateTrainResources(float deltaTime)
    {
        float speed = CurrentSpeed;

        if (speed > 0f && Fuel > 0f)
        {
            float speedRatio = Mathf.Clamp01(speed / 100f);
            Fuel = Mathf.Max(0f, Fuel - fuelUnitsPerSecondAt100Speed * speedRatio * deltaTime);

            if (Fuel <= 0f)
            {
                _networkedSpeed = 0f;
                TargetSpeed = 0f;
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

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        float deltaTime = Runner.DeltaTime;
        if (entryDoors != null)
        {
            float step = deltaTime / TrainEntryDoors.SlideSeconds;
            DoorClosure = new Vector2(
                Mathf.MoveTowards(DoorClosure.x, (ClosedEntryDoors & 1) != 0 ? 1f : 0f, step),
                Mathf.MoveTowards(DoorClosure.y, (ClosedEntryDoors & 2) != 0 ? 1f : 0f, step));
            entryDoors.Present(DoorClosure);
        }
        if (entryDoors == null || (DoorClosure.x >= 1f && DoorClosure.y >= 1f))
        {
            if (RouteLoop == null || !RouteLoop.Ready || (!RouteLoop.IsTransition && RouteLoop.Phase != RailTravelPhase.Approach))
                _networkedSpeed = Mathf.MoveTowards(_networkedSpeed, TargetSpeed, accelerationKmhPerSecond * deltaTime);
        }
        UpdateTrainResources(deltaTime);

        if (IsBlackout && BackupPowerRemaining > 0f)
            BackupPowerRemaining = Mathf.Max(0f, BackupPowerRemaining - deltaTime);
    }

    public override void Render()
    {
        if (entryDoors != null) entryDoors.Present(DoorClosure);
        if (!HasSpawned) return;
        trainSound.pitch = Mathf.Lerp(.82f, 1.12f, Mathf.Clamp01(CurrentSpeed / 100f));
        GameAudio.SetLoop(trainSound, CurrentSpeed > .1f);
        if (hadPower && !HasPower) powerDown.Play();
        hadPower = HasPower;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetEntryDoor(int index, NetworkBool open, RpcInfo info = default)
    {
        if (index < 0 || index > 1 || entryDoors == null || EntryDoorsLocked ||
            (RouteLoop != null && RouteLoop.Ready && RouteLoop.IsTransition)) return;
        PlayerRef sender = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        if (!Runner.TryGetPlayerObject(sender, out NetworkObject player) ||
            !entryDoors.CanUseSwitch(index, player.transform.position)) return;

        int bit = 1 << index;
        ClosedEntryDoors = open ? ClosedEntryDoors & ~bit : ClosedEntryDoors | bit;
    }

    /// <summary>Called by the authoritative gameplay system that detects a blackout.</summary>
    internal void SetBlackoutFromGameplay(bool isBlackout)
    {
        if (!HasStateAuthority || (bool)IsBlackout == isBlackout)
            return;

        IsBlackout = isBlackout;
        BackupPowerRemaining = 0f;
        CrankProgress = 0f;
        NextCrankClockwise = true;
        CrankStrokeSequence = 0;
        LastCrankClockwise = true;
        CrankUser = PlayerRef.None;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RegisterCrankStroke(NetworkBool clockwise)
    {
        if (!IsBlackout || BackupPowerRemaining > 0f)
            return;

        if (clockwise.Equals(NextCrankClockwise))
        {
            LastCrankClockwise = clockwise;
            CrankStrokeSequence++;
            CrankProgress = Mathf.Min(1f, CrankProgress + 1f / 16f);
            NextCrankClockwise = !NextCrankClockwise;

            if (CrankProgress >= 1f)
            {
                BackupPowerRemaining = Mathf.Max(1f, backupPowerDuration);
                Rpc_PlayRechargeSound();
                CrankProgress = 0f;
                Debug.Log("수동 크랭크 발전기: 임시 전력이 복구되었습니다.");
            }
        }
        else
        {
            CrankProgress = Mathf.Max(0f, CrankProgress - 0.04f);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayRechargeSound() => rechargeSound.Play();

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetCrankUser(PlayerRef player, NetworkBool isUsing)
    {
        if (!HasStateAuthority)
            return;

        if (isUsing)
        {
            if (CrankUser == PlayerRef.None || CrankUser == player)
                CrankUser = player;
        }
        else if (CrankUser == player)
        {
            CrankUser = PlayerRef.None;
        }
    }

    [ContextMenu("테스트/블랙아웃 시작")]
    private void DebugStartBlackout()
    {
        SetBlackoutFromGameplay(true);
    }

    [ContextMenu("테스트/블랙아웃 해제")]
    private void DebugClearBlackout()
    {
        SetBlackoutFromGameplay(false);
    }

    // ★ 추가됨: P1 해킹 퍼즐 성공 시 호출되어 기차 속도를 실제로 변경하는 네트워크 함수
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetTrainSpeed(float newSpeed, RpcInfo info = default)
    {
        if (!PlayerMovement.CanPlayerUseRole(Runner, info, TrainRole.Driver)) return;
        if (float.IsNaN(newSpeed) || float.IsInfinity(newSpeed)) return;
        newSpeed = Mathf.Clamp(newSpeed, 0f, 100f);
        if (Fuel <= 0f && newSpeed > 0f)
        {
            _networkedSpeed = 0f;
            TargetSpeed = 0f;
            Debug.LogWarning("⛽ 연료가 없어 열차를 출발시킬 수 없습니다.");
            return;
        }

        if (RouteLoop != null && RouteLoop.Ready && !RouteLoop.TrySetSpeed(newSpeed)) return;
        TargetSpeed = newSpeed;
        if (newSpeed > 0f) ClosedEntryDoors = 3;
        if (newSpeed < _networkedSpeed) _networkedSpeed = newSpeed;
        if (FarmingStop == FarmingStopPhase.Stopped && newSpeed > 0f)
        {
            FarmingStop = FarmingStopPhase.Departed;
            Debug.Log("[TreadmillManager] 임시 파밍장에서 출발했습니다.");
        }
        Debug.Log($"🚂 [TreadmillManager] 목표 속도 {TargetSpeed}km/h");
    }
}
