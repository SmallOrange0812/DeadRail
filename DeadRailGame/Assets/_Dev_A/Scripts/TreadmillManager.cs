using Fusion;
using UnityEngine;

public class TreadmillManager : NetworkBehaviour
{
    public enum FarmingStopPhase { Pending, Stopped, Departed, Cleared }

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
    [Networked] private float _fuelCapacity { get; set; }
    [Networked] private float _batteryCapacity { get; set; }
    [Networked] public int MedicalKits { get; private set; }
    [Networked] public int RepairKits { get; private set; }
    [Networked] public int Ammunition { get; private set; }
    public float FuelCapacity => isSpawned ? _fuelCapacity : fuelCapacity;
    public float BatteryCapacity => isSpawned ? _batteryCapacity : batteryCapacity;
    [Networked] public FarmingStopPhase FarmingStop { get; private set; }

    // ★ 외부 스크립트(Scenery, UI 등)에서 읽을 때 에러가 나지 않도록 안전장치 적용
    public float CurrentSpeed => (Object != null && Object.IsValid) ? _networkedSpeed : 0f;

    // 기존 스크립트들과의 호환성을 위한 스폰 확인 프로퍼티
    public bool isSpawned => (Object != null && Object.IsValid);
    public RailRouteLoop RouteLoop { get; private set; }

    [Header("임시 전력 / 블랙아웃")]
    [Tooltip("게임 시작 후 첫 정전까지 걸리는 시간")]
    [SerializeField, Min(1f)] private float initialPowerDuration = 180f;
    [Tooltip("발전기 미니게임 성공 후 임시 전력이 유지되는 시간")]
    [SerializeField] private float backupPowerDuration = 60f;
    [Networked] public NetworkBool IsBlackout { get; private set; }
    [Networked] public float TimeUntilBlackout { get; private set; }
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

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _hasSpawned = false;
    }

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

    internal void ArriveAtFarmingStop()
    {
        _networkedSpeed = 0f;
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
        // 방장은 기지에서 정지한 상태와 자원 초기값을 한 번만 설정합니다.
        if (HasStateAuthority)
        {
            _networkedSpeed = 0f;
            Fuel = Mathf.Max(0f, fuelCapacity);
            Battery = Mathf.Max(0f, batteryCapacity);
            _fuelCapacity = Mathf.Max(1f, fuelCapacity);
            _batteryCapacity = Mathf.Max(1f, batteryCapacity);
            IsBlackout = false;
            TimeUntilBlackout = Mathf.Max(1f, initialPowerDuration);
            BackupPowerRemaining = 0f;
            CrankProgress = 0f;
            NextCrankClockwise = true;
            CrankStrokeSequence = 0;
            LastCrankClockwise = true;
            CrankUser = PlayerRef.None;
        }

        _hasSpawned = true;
    }

    internal bool CollectSupply(FarmingSupplyKind kind, float amount)
    {
        if (!HasStateAuthority || amount <= 0f) return false;
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
                Ammunition += 30;
                break;
            case FarmingSupplyKind.Fuel:
                if (Fuel >= FuelCapacity) return false;
                Fuel = Mathf.Min(FuelCapacity, Fuel + amount);
                break;
            case FarmingSupplyKind.Battery:
                if (Battery >= BatteryCapacity) return false;
                Battery = Mathf.Min(BatteryCapacity, Battery + amount);
                break;
            default:
                return false;
        }
        return true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        UpdateBackupPower();

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

            if ((playerObject.TryGetComponent(out PlayerMovement movement) && movement.IsPersonalFlashlightOn) ||
                (playerObject.TryGetComponent(out FlashlightToggle flashlight) && flashlight.IsLightOn))
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

    private void UpdateBackupPower()
    {
        if (!IsBlackout)
        {
            TimeUntilBlackout = Mathf.Max(0f, TimeUntilBlackout - Runner.DeltaTime);
            if (TimeUntilBlackout <= 0f)
                SetBlackoutFromGameplay(true);
            return;
        }

        if (BackupPowerRemaining > 0f)
            BackupPowerRemaining = Mathf.Max(0f, BackupPowerRemaining - Runner.DeltaTime);
    }

    /// <summary>Called by the authoritative gameplay system that detects a blackout.</summary>
    public void SetBlackoutFromGameplay(bool isBlackout)
    {
        if (!HasStateAuthority)
            return;

        if (isBlackout && !IsBlackout)
            Debug.Log("[열차 전력] 정전 발생 · 수동 크랭크 발전기로 전력을 복구하세요.");
        IsBlackout = isBlackout;
        TimeUntilBlackout = isBlackout ? 0f : Mathf.Max(1f, initialPowerDuration);
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
                CrankProgress = 0f;
                Debug.Log("수동 크랭크 발전기: 임시 전력이 복구되었습니다.");
            }
        }
        else
        {
            CrankProgress = Mathf.Max(0f, CrankProgress - 0.04f);
        }
    }

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

    private void OnGUI()
    {
        if (!HasSpawned)
            return;

        string message = IsBlackout
            ? BackupPowerRemaining > 0f
                ? $"임시 전력 공급 중  {BackupPowerRemaining:F0}초"
                : "정전 발생! 수동 크랭크 발전기로 전력을 복구하세요."
            : $"전력 정상  ·  정전까지 {TimeUntilBlackout:F0}초";

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = IsBlackout && BackupPowerRemaining <= 0f
            ? new Color(1f, 0.35f, 0.25f)
            : Color.white;
        float width = Mathf.Min(520f, Screen.width - 24f);
        style.wordWrap = true;
        GUI.Box(new Rect((Screen.width - width) * 0.5f, 64f, width, 44f), message, style);
    }

    // ★ 추가됨: P1 해킹 퍼즐 성공 시 호출되어 기차 속도를 실제로 변경하는 네트워크 함수
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SetTrainSpeed(float newSpeed)
    {
        if (float.IsNaN(newSpeed) || float.IsInfinity(newSpeed)) return;
        newSpeed = Mathf.Clamp(newSpeed, 0f, 100f);
        if (Fuel <= 0f && newSpeed > 0f)
        {
            _networkedSpeed = 0f;
            Debug.LogWarning("⛽ 연료가 없어 열차를 출발시킬 수 없습니다.");
            return;
        }

        if (RouteLoop != null && RouteLoop.Ready && !RouteLoop.TrySetSpeed(newSpeed)) return;
        _networkedSpeed = Mathf.Clamp(newSpeed, 0f, 100f);
        if (FarmingStop == FarmingStopPhase.Stopped && _networkedSpeed > 0f)
        {
            FarmingStop = FarmingStopPhase.Departed;
            Debug.Log("[TreadmillManager] 임시 파밍장에서 출발했습니다.");
        }
        Debug.Log($"🚂 [TreadmillManager] 기차 속도가 {_networkedSpeed}km/h로 변경되었습니다!");
    }
}
