using System;
using Fusion;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

public enum RailRoute { Left, Straight, Right }
public enum RailEntryOutcome { Normal, CargoDamaged, Blackout }
public enum FarmingSupplyKind { RouteCapacity, FuelCapacity, BatteryCapacity, MedicalKit, RepairKit, Ammunition, Fuel, Battery }
public enum RailTravelPhase { Cruise, Junction, EntryTransition, Approach, Farming, MissedStop, ExitTransition, ReapproachTransition, Departing }

/// <summary>Authority owns route progress. Each peer presents the same authored track around a stationary train.</summary>
public sealed partial class RailRouteLoop : NetworkBehaviour
{
    [Header("Authored layout")]
    public Transform junctionGeometry;
    public Transform farmingGeometry;
    public StartingRailYard startingYard;
    [SerializeField] private RailSideScenery sideScenery;
    public Transform trainTransform;
    public PlayableDirector transitionDirector;
    public GameObject[] cinematicObjects;
    public GameObject referenceLayout;
    public Transform[] cutMarkers;
    public Vector3[] leftPath;
    public Vector3[] straightPath;
    public Vector3[] rightPath;
    public Transform[] supplyPoints;
    public Renderer[] supplyRenderers;
    public TextMesh[] supplyLabels;
    public FarmingSupplyKind[] supplyKinds;
    public FarmingMapSet farmingMaps;
    public GameObject centralRoadblock;

    [Header("Supply models")]
    [SerializeField] private GameObject largeFuelCan;
    [SerializeField] private GameObject largeBattery;
    [SerializeField] private GameObject firstAidKit;
    [SerializeField] private GameObject ammoBox;
    [SerializeField] private GameObject smallFuelCan;
    [SerializeField] private GameObject smallBattery;
    [SerializeField] private Material ammoBoxBody;
    [SerializeField] private Material repairBoxBody;

    [Header("Distances in metres")]
    [Min(100f)] public float cruisingDistance = 1500f;
    [Min(40f)] public float junctionRevealDistance = 150f;
    [Min(30f)] public float approachDistance = 85f;
    [Min(3f)] public float stopZoneHalfLength = 14f;
    [Min(1f)] public float supplyAmount = 25f;
    [Min(1f)] public float capacityUpgradeAmount = 25f;
    public Bounds boardingBounds = new Bounds(new Vector3(0f, 4f, -6f), new Vector3(4.8f, 4.4f, 17f));
    [Min(0.1f)] public float fadeSeconds = 0.65f;

    [Networked] public RailTravelPhase Phase { get; private set; }
    [Networked] public RailRoute SelectedRoute { get; private set; }
    [Networked] public RailRoute LockedRoute { get; private set; }
    [Networked] public float PhaseDistance { get; private set; }
    [Networked] public float TravelledDistance { get; private set; }
    [Networked] public float TransitionTime { get; private set; }
    [Networked] public int VisitNumber { get; private set; }
    [Networked] public int CollectedSupplies { get; private set; }
    [Networked] public int PlayersAboard { get; private set; }
    [Networked] public int PlayerCount { get; private set; }
    [Networked] public NetworkBool DepartureWaiting { get; private set; }
    [Networked] public int ActiveMapIndex { get; private set; }
    [Networked] private int ActiveMapDanger { get; set; }
    [Networked] public NetworkBool BranchEntryFailed { get; private set; }
    [Networked] private RailEntryOutcome EntryOutcome { get; set; }

    public static RailRouteLoop Instance { get; private set; }
    public bool Ready => Object != null && Object.IsValid;
    public bool IsTransition => Phase == RailTravelPhase.EntryTransition || Phase == RailTravelPhase.ExitTransition || Phase == RailTravelPhase.ReapproachTransition;
    public bool IsStopZone => Phase == RailTravelPhase.Approach && Mathf.Abs(PhaseDistance) <= stopZoneHalfLength;
    public bool CanSelectRoute => Ready && Phase == RailTravelPhase.Cruise && PhaseDistance > 0f;
    public float RemainingDistance => Phase == RailTravelPhase.Cruise ? Mathf.Max(0f, PhaseDistance) : 0f;
    public float WorldSpeed => TreadmillManager.Instance.CurrentSpeed / 3.6f;
    public bool IsBlackout => LockedRoute == RailRoute.Straight;
    public bool UsesTunnelCinematic => IsBlackout && Phase != RailTravelPhase.ReapproachTransition;
    public bool ShowStraightTracks => Phase != RailTravelPhase.Junction &&
        (Phase != RailTravelPhase.EntryTransition || TransitionTime >= fadeSeconds + CutsceneSeconds);
    public static string RouteName(RailRoute route) => route == RailRoute.Left ? "왼쪽 · 연료 최대 용량 증가" : route == RailRoute.Right ? "오른쪽 · 배터리 최대 용량 증가" : "미선택 · 중앙 블랙아웃";

    private const float CutEdgeFade = 0.2f;
    private const float DepartureRunSeconds = 3f;
    private const float OutdoorDepartureShotSeconds = 2.25f;
    private Image fadeImage;
    private Text hudText;
    private TreadmillScenery[] straightTrackTiles;
    private RailTravelPhase presentedPhase = (RailTravelPhase)(-1);
    private Camera[] playerCameras;
    private bool[] cameraStates;
    private AudioListener[] suspendedListeners;
    private bool cinematicPlaying;
    private Camera departureCamera;
    private bool UsesOutdoorDepartureShot => Phase == RailTravelPhase.ExitTransition && !IsBlackout;
    private float[] leftDistances, straightDistances, rightDistances;
    private MaterialPropertyBlock supplyProperties;
    private (FarmingSupplyKind kind, GameObject instance)[] supplyVisuals;
    private int nearbySupply = -1;

    private float CutsceneSeconds => UsesTunnelCinematic && transitionDirector != null && transitionDirector.playableAsset != null
        ? Mathf.Clamp((float)transitionDirector.duration, 0.5f, 8f) : UsesOutdoorDepartureShot ? OutdoorDepartureShotSeconds : 0.25f;
    private float TransitionDuration => fadeSeconds * 2f + CutsceneSeconds;

    private void Awake()
    {
        Instance = this;
        straightTrackTiles = FindObjectsByType<TreadmillScenery>(FindObjectsSortMode.None);
        if (referenceLayout != null) referenceLayout.SetActive(false);
        if (farmingGeometry != null) farmingGeometry.gameObject.SetActive(false);
        if (cinematicObjects != null) foreach (GameObject obj in cinematicObjects) if (obj != null) obj.SetActive(false);
        leftDistances = BuildDistances(leftPath);
        straightDistances = BuildDistances(straightPath);
        rightDistances = BuildDistances(rightPath);
        supplyProperties = new MaterialPropertyBlock();
        supplyVisuals = new (FarmingSupplyKind, GameObject)[supplyPoints.Length];
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            ResetRouteForRun();
        BuildOverlay();
        Render();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        TreadmillManager train = TreadmillManager.Instance;
        UpdateDefeat(train);
        if (Defeated) return;
        RefreshCollisionGeometry();
        UpdateCargoCarrier();
        SimulateZombies();
        CountPlayers();
        float deltaTime = Runner.DeltaTime;
        if (IsTransition)
        {
            if (UsesOutdoorDepartureShot && TransitionTime < fadeSeconds + CutsceneSeconds)
            {
                float shotDistance = WorldSpeed * Mathf.Min(deltaTime, fadeSeconds + CutsceneSeconds - TransitionTime);
                PhaseDistance -= shotDistance;
                TravelledDistance += shotDistance;
            }
            TransitionTime += deltaTime;
            if (TransitionTime < TransitionDuration) return;
            if (Phase == RailTravelPhase.ExitTransition)
            {
                train.SetBlackoutFromGameplay(false);
                VisitNumber++;
                SelectedRoute = RailRoute.Straight;
                Phase = RailTravelPhase.Cruise;
                PhaseDistance = cruisingDistance;
                CollectedSupplies = 0;
                ActiveMapIndex = -1;
                BranchEntryFailed = false;
                TransitionTime = 0f;
                train.ResetFarmingStop();
                MapNodeManager.Instance?.BeginRouteStage(VisitNumber);
            }
            else
            {
                Phase = RailTravelPhase.Approach;
                PhaseDistance = approachDistance;
            }
            return;
        }

        float distance = Phase == RailTravelPhase.Approach
            ? Mathf.Min(Mathf.Max(0f, PhaseDistance), train.ApplyAutomaticStopSpeed(PhaseDistance, deltaTime) / 3.6f * deltaTime)
            : WorldSpeed * deltaTime;
        TravelledDistance += distance;
        switch (Phase)
        {
            case RailTravelPhase.Cruise:
                PhaseDistance -= distance;
                if (PhaseDistance <= 0f)
                {
                    MapNodeManager nodes = MapNodeManager.Instance;
                    SectorNodeData selected = nodes.GetRouteNode(SelectedRoute);
                    EntryOutcome = EvaluateEntry(SelectedRoute, train.CurrentSpeed, selected.requiredSpeed);
                    BranchEntryFailed = EntryOutcome == RailEntryOutcome.Blackout;
                    if (EntryOutcome == RailEntryOutcome.CargoDamaged) DamageCargoDoor(35);
                    LockedRoute = BranchEntryFailed ? RailRoute.Straight : SelectedRoute;
                    SectorNodeData destination = nodes.GetRouteNode(LockedRoute);
                    ActiveMapIndex = destination.mapIndex;
                    ActiveMapDanger = destination.dangerLevel;
                    Phase = RailTravelPhase.Junction;
                    PhaseDistance = 0f;
                }
                break;
            case RailTravelPhase.Junction:
                PhaseDistance += distance;
                if (PhaseDistance >= PathLength(LockedRoute))
                {
                    if (IsBlackout) train.SetBlackoutFromGameplay(true);
                    StartTransition(RailTravelPhase.EntryTransition);
                }
                break;
            case RailTravelPhase.Approach:
                PhaseDistance -= distance;
                if (PhaseDistance <= .025f)
                {
                    TravelledDistance += Mathf.Max(0f, PhaseDistance);
                    PhaseDistance = 0f;
                    Phase = RailTravelPhase.Farming;
                    train.ArriveAtFarmingStop();
                }
                break;
            case RailTravelPhase.Departing:
                PhaseDistance -= distance;
                if (distance > 0f)
                {
                    TransitionTime += deltaTime;
                    if (TransitionTime >= DepartureRunSeconds) StartTransition(RailTravelPhase.ExitTransition, fadeSeconds);
                }
                else if (train.Fuel <= 0f)
                {
                    StartTransition(RailTravelPhase.Farming);
                    train.ArriveAtFarmingStop();
                }
                break;
            case RailTravelPhase.MissedStop:
                PhaseDistance -= distance;
                break;
        }
        if (MapNodeManager.Instance != null)
            MapNodeManager.Instance.RemainingDistance = RemainingDistance;
        RefreshCollisionGeometry();
    }

    public bool TrySetSpeed(float speed)
    {
        if (Defeated || IsTransition || Phase == RailTravelPhase.Approach) return false;
        if (Phase == RailTravelPhase.Departing) return speed > 0f;
        if (Phase != RailTravelPhase.Farming || speed <= 0f) return true;
        if (FirstDefensePending()) return false;
        CountPlayers();
        DepartureWaiting = PlayersAboard < PlayerCount;
        if (DepartureWaiting) return false;
        StartTransition(RailTravelPhase.Departing);
        return true;
    }

    private void StartTransition(RailTravelPhase phase, float initialTime = 0f)
    {
        Phase = phase;
        TransitionTime = initialTime;
        DepartureWaiting = false;
    }

    public void CycleRoute()
    {
        if (!HasStateAuthority || !CanSelectRoute) return;
        SelectedRoute = SelectedRoute switch
        {
            RailRoute.Left => RailRoute.Straight,
            RailRoute.Straight => RailRoute.Right,
            _ => RailRoute.Left
        };
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_Reapproach(RpcInfo info = default)
    {
        if (Phase != RailTravelPhase.MissedStop ||
            !PlayerMovement.CanPlayerUseRole(Runner, info, TrainRole.Driver)) return;
        StartTransition(RailTravelPhase.ReapproachTransition);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_CollectSupply(int index, RpcInfo info = default)
    {
        if (Phase != RailTravelPhase.Farming || index < 0 || index >= supplyPoints.Length || index >= 30) return;
        if ((CollectedSupplies & (1 << index)) != 0) return;
        PlayerRef sender = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        if (!Runner.TryGetPlayerObject(sender, out NetworkObject player)) return;
        if (IsAboard(player.transform.position) || Vector3.Distance(player.transform.position, supplyPoints[index].position) > 3f) return;
        TreadmillManager train = TreadmillManager.Instance;
        FarmingSupplyKind kind = GetSupplyKind(index);
        bool personalSupply = kind is FarmingSupplyKind.MedicalKit or FarmingSupplyKind.RepairKit or FarmingSupplyKind.Ammunition;
        if (!player.TryGetComponent(out PlayerMovement collector) || collector.IsDowned ||
            (personalSupply && !collector.HasStateAuthority)) return;
        if (VisitNumber == 1 && kind == FarmingSupplyKind.Fuel)
        {
            if (CargoFuelCarrier != PlayerRef.None || FirstFuelDelivered) return;
            CargoFuelCarrier = sender;
            if (!FuelDefenseTriggered)
            {
                FuelDefenseTriggered = true;
                DefenseDelay = TickTimer.CreateFromSeconds(Runner, 10f);
            }
            CollectedSupplies |= 1 << index;
            collector.Rpc_PlayPickupSound();
            return;
        }
        float amount = kind == FarmingSupplyKind.FuelCapacity || kind == FarmingSupplyKind.BatteryCapacity ? capacityUpgradeAmount : supplyAmount;
        if (train == null || !train.CollectSupply(kind, amount)) return;
        if (personalSupply) collector.RecordInventoryPickup(kind);
        CollectedSupplies |= 1 << index;
        collector.Rpc_PlayPickupSound();
    }

    public FarmingSupplyKind GetSupplyKind(int index)
    {
        FarmingSupplyKind kind = supplyKinds != null && index < supplyKinds.Length ? supplyKinds[index] : FarmingSupplyKind.RouteCapacity;
        if (kind != FarmingSupplyKind.RouteCapacity) return kind;
        if (IsBlackout) return FarmingSupplyKind.Ammunition;
        return LockedRoute == RailRoute.Left ? FarmingSupplyKind.FuelCapacity : FarmingSupplyKind.BatteryCapacity;
    }

    public string SupplyName(int index)
    {
        switch (GetSupplyKind(index))
        {
            case FarmingSupplyKind.FuelCapacity: return $"연료 최대 용량 +{capacityUpgradeAmount:F0}";
            case FarmingSupplyKind.BatteryCapacity: return $"배터리 최대 용량 +{capacityUpgradeAmount:F0}";
            case FarmingSupplyKind.MedicalKit: return "치료품 +1";
            case FarmingSupplyKind.RepairKit: return "수리품 +1";
            case FarmingSupplyKind.Ammunition: return "탄약 +30";
            case FarmingSupplyKind.Fuel: return VisitNumber == 1 ? "운반용 연료통" : $"연료 +{supplyAmount:F0}";
            case FarmingSupplyKind.Battery: return $"배터리 +{supplyAmount:F0}";
            default: return "보급품";
        }
    }

    private static Color SupplyColor(FarmingSupplyKind kind)
    {
        switch (kind)
        {
            case FarmingSupplyKind.FuelCapacity: return new Color(1f, 0.45f, 0.1f);
            case FarmingSupplyKind.BatteryCapacity: return Color.cyan;
            case FarmingSupplyKind.MedicalKit: return new Color(0.2f, 0.9f, 0.3f);
            case FarmingSupplyKind.RepairKit: return Color.yellow;
            case FarmingSupplyKind.Fuel: return new Color(0.65f, 0.3f, 0.1f);
            case FarmingSupplyKind.Battery: return new Color(0.2f, 0.5f, 0.8f);
            default: return new Color(0.8f, 0.8f, 0.7f);
        }
    }

    private bool ShowSupplyModel(int index, FarmingSupplyKind kind)
    {
        var visual = supplyVisuals[index];
        if (visual.instance != null && visual.kind == kind) return true;

        GameObject prefab = kind switch
        {
            FarmingSupplyKind.FuelCapacity => largeFuelCan,
            FarmingSupplyKind.BatteryCapacity => largeBattery,
            FarmingSupplyKind.MedicalKit => firstAidKit,
            FarmingSupplyKind.RepairKit => ammoBox,
            FarmingSupplyKind.Ammunition => ammoBox,
            FarmingSupplyKind.Fuel => smallFuelCan,
            FarmingSupplyKind.Battery => smallBattery,
            _ => null
        };
        if (visual.instance != null)
        {
            visual.instance.SetActive(false);
            Destroy(visual.instance);
        }
        if (prefab == null)
        {
            supplyVisuals[index] = default;
            return false;
        }

        Transform point = supplyPoints[index];
        GameObject model = Instantiate(prefab, point.position, point.rotation);
        if (kind == FarmingSupplyKind.MedicalKit) model.transform.localScale *= 1.7f;
        model.transform.SetParent(point, true);
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                if (kind == FarmingSupplyKind.RepairKit && ammoBoxBody != null && repairBoxBody != null)
                {
                    Material[] materials = renderer.sharedMaterials;
                    int bodyIndex = Array.IndexOf(materials, ammoBoxBody);
                    if (bodyIndex >= 0)
                    {
                        materials[bodyIndex] = repairBoxBody;
                        renderer.sharedMaterials = materials;
                    }
                }
            }
            // The anchor is the center of the former unit cube. Keep the model on its bottom plane.
            Vector3 ground = point.TransformPoint(Vector3.down * 0.5f);
            Vector3 modelBottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position += ground - modelBottom;
        }
        supplyVisuals[index] = (kind, model);
        return true;
    }

    public bool IsAboard(Vector3 worldPosition) => trainTransform != null && boardingBounds.Contains(trainTransform.InverseTransformPoint(worldPosition));

    private void CountPlayers()
    {
        int aboard = 0, total = 0;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            total++;
            if (Runner.TryGetPlayerObject(player, out NetworkObject playerObject) && IsAboard(playerObject.transform.position)) aboard++;
        }
        PlayersAboard = aboard;
        PlayerCount = total;
    }

    public string TravelStatus()
    {
        switch (Phase)
        {
            case RailTravelPhase.Cruise:
                string distance = PhaseDistance >= 1000f ? $"{PhaseDistance / 1000f:0.##} km" : $"{PhaseDistance:F0} m";
                if (SelectedRoute == RailRoute.Straight)
                    return $"분기까지 {distance} · {RouteName(SelectedRoute)}";
                SectorNodeData selected = MapNodeManager.Instance.GetRouteNode(SelectedRoute);
                return $"분기까지 {distance} · {RouteName(SelectedRoute)}\n{selected.sectorName} · 안전 속도는 탐조등 표지판 확인";
            case RailTravelPhase.Junction:
                if (BranchEntryFailed)
                {
                    string reason = SelectedRoute == RailRoute.Straight ? "선로 미선택" : "진입 속도 오차 15 km/h 초과";
                    return $"{reason} · 중앙 블랙아웃 → {MapNodeManager.GetMapName(ActiveMapIndex)}";
                }
                return $"{MapNodeManager.GetMapName(ActiveMapIndex)} 진입 · " +
                    (EntryOutcome == RailEntryOutcome.CargoDamaged ? "속도 오차로 화물문 손상" : RouteName(LockedRoute));
            case RailTravelPhase.Approach:
                if (TreadmillManager.Instance.Fuel <= 0f) return "연료 부족 · 연료 보충 후 자동 정차를 재개합니다";
                return $"{MapNodeManager.GetMapName(ActiveMapIndex)} 자동 정차 · 정차 지점까지 {PhaseDistance:F0} m";
            case RailTravelPhase.Farming:
                return $"{MapNodeManager.GetMapName(ActiveMapIndex)} 정차 · {(IsBlackout ? "블랙아웃 지하 파밍 · 터널 내부" : RouteName(LockedRoute))}\n탑승 {PlayersAboard}/{PlayerCount}" + (DepartureWaiting ? " · 팀원 탑승 후 출발 가능" : " · 보급품 근처 [F]로 회수");
            case RailTravelPhase.Departing:
                return $"{MapNodeManager.GetMapName(ActiveMapIndex)} 출발 중";
            case RailTravelPhase.MissedStop: return "정차 구간을 지났습니다 · P1 재접근 버튼";
            default: return "구간 전환 중";
        }
    }

    public override void Render()
    {
        if (!Ready || junctionGeometry == null || farmingGeometry == null) return;
        RefreshCollisionGeometry();
        RenderZombies();
        RenderCarriedFuel();
        RenderCargoDoorMarker();
        RenderEntrySpeedSign();
        RenderRunResult();
        for (int i = 0; farmingGeometry.gameObject.activeInHierarchy && i < supplyPoints.Length; i++)
        {
            bool available = (CollectedSupplies & (1 << i)) == 0;
            supplyPoints[i].gameObject.SetActive(available);
            if (!available) continue;

            FarmingSupplyKind kind = GetSupplyKind(i);
            bool hasModel = ShowSupplyModel(i, kind);
            if (i < supplyRenderers.Length && supplyRenderers[i] != null)
            {
                supplyRenderers[i].enabled = !hasModel;
                if (!hasModel)
                {
                    supplyProperties.SetColor("_BaseColor", SupplyColor(kind));
                    supplyRenderers[i].SetPropertyBlock(supplyProperties);
                }
            }
            if (supplyLabels != null && i < supplyLabels.Length && supplyLabels[i] != null) supplyLabels[i].text = SupplyName(i);
        }
        PresentTransition();
        if (hudText != null)
        {
            hudText.text = $"구간 {VisitNumber} · {TravelStatus()}";
            if (Phase == RailTravelPhase.Farming || CargoDoorHealth < CargoDoorMaxHealth)
                hudText.text += $"\n{CargoStatus()}";
            string cargoPrompt = CargoPrompt();
            if (!string.IsNullOrEmpty(cargoPrompt)) hudText.text += $"\n{cargoPrompt}";
            if (Phase == RailTravelPhase.Farming && nearbySupply >= 0 && (CollectedSupplies & (1 << nearbySupply)) == 0)
                hudText.text += $"\n[F] {SupplyName(nearbySupply)} {(VisitNumber == 1 && GetSupplyKind(nearbySupply) == FarmingSupplyKind.Fuel ? "들기" : "회수")}";
            TreadmillManager train = TreadmillManager.Instance;
            if (train != null && train.HasSpawned && train.IsBlackout)
            {
                hudText.text += train.BackupPowerRemaining > 0f
                    ? $"\n임시 전력 공급 중 · {train.BackupPowerRemaining:F0}초 남음"
                    : "\n<color=#FF8E72>정전 발생 · 수동 크랭크 발전기로 전력을 복구하세요</color>";
            }
        }
    }

    public void RefreshCollisionGeometry()
    {
        if (!Ready || junctionGeometry == null || farmingGeometry == null) return;
        foreach (TreadmillScenery tile in straightTrackTiles)
            if (tile != null) tile.Present(this);
        if (startingYard != null) startingYard.Present(TravelledDistance);
        if (sideScenery != null) sideScenery.Present(this);
        bool beforeCut = IsTransition && TransitionTime < fadeSeconds;
        bool afterCut = IsTransition && TransitionTime >= fadeSeconds + CutsceneSeconds;
        bool showJunction = (Phase == RailTravelPhase.Cruise && PhaseDistance <= junctionRevealDistance) || Phase == RailTravelPhase.Junction || (Phase == RailTravelPhase.EntryTransition && beforeCut);
        bool showFarm = Phase == RailTravelPhase.Approach || Phase == RailTravelPhase.Farming || Phase == RailTravelPhase.MissedStop || Phase == RailTravelPhase.Departing ||
            (Phase == RailTravelPhase.ExitTransition && (beforeCut || (UsesOutdoorDepartureShot && !afterCut))) || (Phase == RailTravelPhase.ReapproachTransition && beforeCut) ||
            ((Phase == RailTravelPhase.EntryTransition || Phase == RailTravelPhase.ReapproachTransition) && afterCut);
        junctionGeometry.gameObject.SetActive(showJunction);
        if (centralRoadblock != null) centralRoadblock.SetActive(Phase == RailTravelPhase.Cruise || !BranchEntryFailed);
        if (showJunction)
        {
            if (Phase == RailTravelPhase.Cruise)
                junctionGeometry.SetPositionAndRotation(Vector3.forward * PhaseDistance, Quaternion.identity);
            else
            {
                SamplePath(LockedRoute, Phase == RailTravelPhase.Junction ? PhaseDistance : PathLength(LockedRoute), out Vector3 point, out Vector3 tangent);
                Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(tangent));
                junctionGeometry.SetPositionAndRotation(-(rotation * point), rotation);
            }
        }
        if (showFarm)
            farmingGeometry.position = Vector3.forward * (afterCut ? approachDistance : PhaseDistance);
        if (farmingMaps != null) farmingMaps.Present(ActiveMapIndex, showFarm, supplyPoints);
        farmingGeometry.gameObject.SetActive(showFarm);
    }

    private void Update()
    {
        // Standalone test scenes can run the proximity pickup without a terminal HUD.
        if (TerminalUIManager.Instance != null && TerminalUIManager.Instance.isActiveAndEnabled) return;
        nearbySupply = -1;
        if (Ready && Phase == RailTravelPhase.Farming && Runner.TryGetPlayerObject(Runner.LocalPlayer, out var actor) &&
            actor.TryGetComponent<PlayerMovement>(out var player))
            UpdateSupplyFocus(player, null, out _);
    }

    internal Transform UpdateSupplyFocus(PlayerMovement player, Transform hit, out bool includeChildren)
    {
        includeChildren = false;
        nearbySupply = -1;
        if (player == null || !Ready || Phase != RailTravelPhase.Farming || player.IsDowned || IsAboard(player.transform.position))
            return null;
        for (int i = 0; i < supplyPoints.Length && i < 30; i++)
        {
            if (!CanTakeSupply(i, player.transform.position)) continue;
            if (nearbySupply < 0) nearbySupply = i;
            if (hit == null || !hit.IsChildOf(supplyPoints[i])) continue;
            nearbySupply = i;
            GameObject model = supplyVisuals[i].instance;
            includeChildren = model != null;
            return model != null ? model.transform : supplyPoints[i];
        }
        return null;
    }

    private bool CanTakeSupply(int index, Vector3 position)
    {
        if ((CollectedSupplies & (1 << index)) != 0 || (supplyPoints[index].position - position).sqrMagnitude > 9f)
            return false;
        FarmingSupplyKind kind = GetSupplyKind(index);
        if (VisitNumber == 1 && kind == FarmingSupplyKind.Fuel)
            return CargoFuelCarrier == PlayerRef.None && !FirstFuelDelivered;
        TreadmillManager train = TreadmillManager.Instance;
        return train != null && train.HasSpawned && train.HasRoomForSupply(kind);
    }

    internal bool TryCollectNearbySupply(PlayerMovement player)
    {
        if (!Ready || Phase != RailTravelPhase.Farming || nearbySupply < 0 ||
            IsAboard(player.transform.position) || !CanTakeSupply(nearbySupply, player.transform.position))
            return false;

        Rpc_CollectSupply(nearbySupply);
        return true;
    }

    private void PresentTransition()
    {
        if (presentedPhase != Phase)
        {
            presentedPhase = Phase;
            if (IsTransition) TerminalUIManager.Instance?.CloseAllTerminals();
        }
        float alpha = Phase == RailTravelPhase.Departing ?
            Mathf.InverseLerp(DepartureRunSeconds - fadeSeconds, DepartureRunSeconds, TransitionTime) : 0f;
        bool inCinematic = false;
        if (IsTransition)
        {
            float cutTime = TransitionTime - fadeSeconds;
            inCinematic = (UsesOutdoorDepartureShot || (UsesTunnelCinematic && transitionDirector != null && transitionDirector.playableAsset != null)) &&
                cutTime >= 0f && cutTime < CutsceneSeconds;
            if (TransitionTime < fadeSeconds) alpha = TransitionTime / fadeSeconds;
            else if (inCinematic)
                alpha = Mathf.Max(1f - cutTime / CutEdgeFade, 1f - (CutsceneSeconds - cutTime) / CutEdgeFade);
            else alpha = 1f - (cutTime - CutsceneSeconds) / fadeSeconds;
            if (inCinematic)
            {
                if (!cinematicPlaying) BeginCinematic();
                if (UsesOutdoorDepartureShot) PresentDepartureCamera(cutTime / CutsceneSeconds);
                else
                {
                    transitionDirector.time = cutTime;
                    transitionDirector.Evaluate();
                }
            }
        }
        if (!inCinematic && cinematicPlaying) EndCinematic();
        if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    private void BeginCinematic()
    {
        cinematicPlaying = true;
        playerCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        cameraStates = new bool[playerCameras.Length];
        suspendedListeners = new AudioListener[playerCameras.Length];
        for (int i = 0; i < playerCameras.Length; i++)
        {
            cameraStates[i] = playerCameras[i].enabled;
            if (playerCameras[i].TryGetComponent(out AudioListener listener) && listener.enabled)
            {
                suspendedListeners[i] = listener;
                listener.enabled = false;
            }
            playerCameras[i].enabled = false;
        }
        if (UsesOutdoorDepartureShot)
        {
            if (departureCamera == null)
            {
                var cameraObject = new GameObject("Farming departure camera");
                cameraObject.transform.SetParent(transform, false);
                departureCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                departureCamera.fieldOfView = 50f;
            }
            departureCamera.gameObject.SetActive(true);
            departureCamera.enabled = true;
        }
        else
        {
            if (referenceLayout != null) referenceLayout.SetActive(true);
            transitionDirector.RebuildGraph();
        }
    }

    private void PresentDepartureCamera(float progress)
    {
        Vector3 cameraPosition = trainTransform.TransformPoint(new Vector3(-7.4f, 3.8f, Mathf.Lerp(7f, 2.5f, progress)));
        Vector3 focus = trainTransform.TransformPoint(new Vector3(0f, 1.8f, Mathf.Lerp(1.4f, 3.8f, progress)));
        departureCamera.transform.SetPositionAndRotation(cameraPosition, Quaternion.LookRotation(focus - cameraPosition));
    }

    private void EndCinematic()
    {
        if (departureCamera != null) departureCamera.gameObject.SetActive(false);
        if (transitionDirector != null) transitionDirector.Stop();
        if (referenceLayout != null) referenceLayout.SetActive(false);
        foreach (GameObject obj in cinematicObjects) if (obj != null) obj.SetActive(false);
        for (int i = 0; i < playerCameras.Length; i++)
        {
            if (playerCameras[i] != null) playerCameras[i].enabled = cameraStates[i];
            if (suspendedListeners[i] != null) suspendedListeners[i].enabled = true;
        }
        cinematicPlaying = false;
    }

    private void BuildOverlay()
    {
        GameObject overlay = new GameObject("Route status and transition", typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(transform, false);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        GameObject label = new GameObject("Route status", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(overlay.transform, false);
        hudText = label.GetComponent<Text>();
        hudText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudText.fontSize = 18;
        hudText.alignment = TextAnchor.UpperCenter;
        hudText.color = Color.white;
        hudText.raycastTarget = false;
        RectTransform rect = hudText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -18f);
        rect.sizeDelta = new Vector2(950f, 155f);
        label.AddComponent<Outline>().effectColor = Color.black;
        GameObject fade = new GameObject("Blackout fade", typeof(RectTransform), typeof(Image));
        fade.transform.SetParent(overlay.transform, false);
        fadeImage = fade.GetComponent<Image>();
        fadeImage.raycastTarget = false;
        fadeImage.rectTransform.anchorMin = Vector2.zero;
        fadeImage.rectTransform.anchorMax = Vector2.one;
        fadeImage.rectTransform.offsetMin = Vector2.zero;
        fadeImage.rectTransform.offsetMax = Vector2.zero;
    }

    private static float[] BuildDistances(Vector3[] points)
    {
        if (points == null) return Array.Empty<float>();
        var distances = new float[points.Length];
        for (int i = 1; i < points.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        return distances;
    }

    public float PathLength(RailRoute route)
    {
        float[] distances = GetDistances(route);
        return distances.Length > 0 ? distances[distances.Length - 1] : 0f;
    }

    private float[] GetDistances(RailRoute route) => route == RailRoute.Left ? leftDistances : route == RailRoute.Right ? rightDistances : straightDistances;

    private void SamplePath(RailRoute route, float distance, out Vector3 point, out Vector3 tangent)
    {
        Vector3[] points = route == RailRoute.Left ? leftPath : route == RailRoute.Right ? rightPath : straightPath;
        float[] distances = GetDistances(route);
        point = Vector3.zero;
        tangent = Vector3.forward;
        if (points == null || points.Length < 2) return;
        int segment = Array.BinarySearch(distances, distance);
        if (segment < 0) segment = ~segment;
        segment = Mathf.Clamp(segment, 1, points.Length - 1);
        float t = Mathf.InverseLerp(distances[segment - 1], distances[segment], distance);
        point = Vector3.Lerp(points[segment - 1], points[segment], t);
        tangent = (points[segment] - points[segment - 1]).normalized;
        tangent.y = 0f;
        if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.forward;
    }

    private void OnDestroy()
    {
        DestroyCombatVisuals();
        if (cinematicPlaying) EndCinematic();
        if (Instance == this) Instance = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (junctionGeometry == null || cutMarkers == null) return;
        foreach (Transform marker in cutMarkers)
        {
            if (marker == null) continue;
            Gizmos.color = marker.name.StartsWith("Left") ? Color.red : marker.name.StartsWith("Right") ? Color.blue : Color.yellow;
            Gizmos.DrawWireSphere(marker.position + Vector3.up, 1.5f);
        }
        if (trainTransform != null)
        {
            Gizmos.color = Color.green;
            Gizmos.matrix = trainTransform.localToWorldMatrix;
            Gizmos.DrawWireCube(boardingBounds.center, boardingBounds.size);
        }
    }
}
