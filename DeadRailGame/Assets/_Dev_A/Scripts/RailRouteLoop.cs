using System;
using Fusion;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

public enum RailRoute { Left, Straight, Right }
public enum FarmingSupplyKind { RouteCapacity, FuelCapacity, BatteryCapacity, MedicalKit, RepairKit, Ammunition, Fuel, Battery }
public enum RailTravelPhase { Cruise, Junction, EntryTransition, Approach, Farming, MissedStop, ExitTransition, ReapproachTransition }

/// <summary>Authority owns route progress. Each peer presents the same authored track around a stationary train.</summary>
public sealed class RailRouteLoop : NetworkBehaviour
{
    [Header("Authored layout")]
    public Transform junctionGeometry;
    public Transform farmingGeometry;
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

    [Header("Distances in metres")]
    [Min(100f)] public float cruisingDistance = 300f;
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
    [Networked] public NetworkBool BranchEntryFailed { get; private set; }

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
    public static string RouteName(RailRoute route) => route == RailRoute.Left ? "왼쪽 · 연료 최대 용량 증가" : route == RailRoute.Right ? "오른쪽 · 배터리 최대 용량 증가" : "중앙 · 진입 실패 시 블랙아웃";

    private const float CutEdgeFade = 0.2f;
    private Image fadeImage;
    private Text hudText;
    private RailTravelPhase presentedPhase = (RailTravelPhase)(-1);
    private Camera[] playerCameras;
    private bool[] cameraStates;
    private bool cinematicPlaying;
    private float[] leftDistances, straightDistances, rightDistances;
    private MaterialPropertyBlock supplyProperties;
    private int nearbySupply = -1;

    private float CutsceneSeconds => UsesTunnelCinematic && transitionDirector != null && transitionDirector.playableAsset != null
        ? Mathf.Clamp((float)transitionDirector.duration, 0.5f, 8f) : 0.25f;
    private float TransitionDuration => fadeSeconds * 2f + CutsceneSeconds;

    private void Awake()
    {
        Instance = this;
        if (referenceLayout != null) referenceLayout.SetActive(false);
        if (cinematicObjects != null) foreach (GameObject obj in cinematicObjects) if (obj != null) obj.SetActive(false);
        leftDistances = BuildDistances(leftPath);
        straightDistances = BuildDistances(straightPath);
        rightDistances = BuildDistances(rightPath);
        supplyProperties = new MaterialPropertyBlock();
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            Phase = RailTravelPhase.Cruise;
            PhaseDistance = cruisingDistance;
            SelectedRoute = RailRoute.Left;
            VisitNumber = 1;
            ActiveMapIndex = -1;
            LockedRoute = RailRoute.Left;
        }
        BuildOverlay();
        Render();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        RefreshCollisionGeometry();
        CountPlayers();
        TreadmillManager train = TreadmillManager.Instance;
        if (IsTransition)
        {
            TransitionTime += Runner.DeltaTime;
            if (TransitionTime < TransitionDuration) return;
            if (Phase == RailTravelPhase.ExitTransition)
            {
                VisitNumber++;
                SelectedRoute = RailRoute.Left;
                Phase = RailTravelPhase.Cruise;
                PhaseDistance = cruisingDistance;
                CollectedSupplies = 0;
                ActiveMapIndex = -1;
                BranchEntryFailed = false;
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

        float distance = WorldSpeed * Runner.DeltaTime;
        TravelledDistance += distance;
        switch (Phase)
        {
            case RailTravelPhase.Cruise:
                PhaseDistance -= distance;
                if (PhaseDistance <= 0f)
                {
                    MapNodeManager nodes = MapNodeManager.Instance;
                    SectorNodeData selected = nodes.GetRouteNode(SelectedRoute);
                    BranchEntryFailed = train.CurrentSpeed < selected.requiredSpeed;
                    LockedRoute = BranchEntryFailed ? RailRoute.Straight : SelectedRoute;
                    ActiveMapIndex = nodes.GetRouteNode(LockedRoute).mapIndex;
                    Phase = RailTravelPhase.Junction;
                    PhaseDistance = 0f;
                }
                break;
            case RailTravelPhase.Junction:
                PhaseDistance += distance;
                if (PhaseDistance >= PathLength(LockedRoute))
                {
                    StartTransition(RailTravelPhase.EntryTransition);
                }
                break;
            case RailTravelPhase.Approach:
                PhaseDistance -= distance;
                if (train.CurrentSpeed <= 0f && Mathf.Abs(PhaseDistance) <= stopZoneHalfLength)
                {
                    Phase = RailTravelPhase.Farming;
                    train.ArriveAtFarmingStop();
                }
                else if (PhaseDistance < -stopZoneHalfLength)
                {
                    Phase = RailTravelPhase.MissedStop;
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
        if (IsTransition) return false;
        if (Phase != RailTravelPhase.Farming || speed <= 0f) return true;
        CountPlayers();
        DepartureWaiting = PlayersAboard < PlayerCount;
        if (DepartureWaiting) return false;
        StartTransition(RailTravelPhase.ExitTransition);
        return true;
    }

    private void StartTransition(RailTravelPhase phase)
    {
        Phase = phase;
        TransitionTime = 0f;
        DepartureWaiting = false;
    }

    public void CycleRoute()
    {
        if (!HasStateAuthority || !CanSelectRoute) return;
        SelectedRoute = SelectedRoute == RailRoute.Left ? RailRoute.Right : RailRoute.Left;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_Reapproach()
    {
        if (Phase != RailTravelPhase.MissedStop) return;
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
        float amount = kind == FarmingSupplyKind.FuelCapacity || kind == FarmingSupplyKind.BatteryCapacity ? capacityUpgradeAmount : supplyAmount;
        if (!train.CollectSupply(kind, amount)) return;
        CollectedSupplies |= 1 << index;
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
            case FarmingSupplyKind.Fuel: return $"연료 +{supplyAmount:F0}";
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
                SectorNodeData selected = MapNodeManager.Instance.GetRouteNode(SelectedRoute);
                return $"분기까지 {PhaseDistance:F0} m · {RouteName(SelectedRoute)}\n{selected.sectorName} · 요구 속도 {selected.requiredSpeed} km/h" +
                    (PhaseDistance <= junctionRevealDistance ? " · P3 적중으로 좌우 전환 / 속도 부족 시 블랙아웃" : "");
            case RailTravelPhase.Junction:
                return BranchEntryFailed ? $"진입 속도 부족 · 중앙 블랙아웃 → {MapNodeManager.GetMapName(ActiveMapIndex)}" : $"{MapNodeManager.GetMapName(ActiveMapIndex)} 진입 · {RouteName(LockedRoute)}";
            case RailTravelPhase.Approach:
                return IsStopZone ? "정차 가능 구간 · P1 정차 버튼으로 0 km/h" : $"파밍장 정차 구간까지 {Mathf.Max(0f, PhaseDistance - stopZoneHalfLength):F0} m · 일찍 멈추면 저속 이동";
            case RailTravelPhase.Farming:
                return $"{MapNodeManager.GetMapName(ActiveMapIndex)} 정차 · {(IsBlackout ? "블랙아웃 지하 파밍 · 터널 내부" : RouteName(LockedRoute))}\n탑승 {PlayersAboard}/{PlayerCount}" + (DepartureWaiting ? " · 팀원 탑승 후 출발 가능" : " · 보급품 근처 [E]로 회수");
            case RailTravelPhase.MissedStop: return "정차 구간을 지났습니다 · P1 재접근 버튼";
            default: return "구간 전환 중";
        }
    }

    public override void Render()
    {
        if (!Ready || junctionGeometry == null || farmingGeometry == null) return;
        RefreshCollisionGeometry();
        for (int i = 0; i < supplyPoints.Length; i++)
        {
            bool available = (CollectedSupplies & (1 << i)) == 0;
            supplyPoints[i].gameObject.SetActive(available);
            Color color = SupplyColor(GetSupplyKind(i));
            supplyProperties.SetColor("_BaseColor", color);
            if (i < supplyRenderers.Length && supplyRenderers[i] != null) supplyRenderers[i].SetPropertyBlock(supplyProperties);
            if (supplyLabels != null && i < supplyLabels.Length && supplyLabels[i] != null) supplyLabels[i].text = SupplyName(i);
        }
        PresentTransition();
        if (hudText != null)
        {
            hudText.text = $"구간 {VisitNumber} · {TravelStatus()}";
            if (Phase == RailTravelPhase.Farming && nearbySupply >= 0 && (CollectedSupplies & (1 << nearbySupply)) == 0)
                hudText.text += $"\n[E] {SupplyName(nearbySupply)} 회수";
        }
    }

    public void RefreshCollisionGeometry()
    {
        if (!Ready || junctionGeometry == null || farmingGeometry == null) return;
        bool beforeCut = IsTransition && TransitionTime < fadeSeconds;
        bool afterCut = IsTransition && TransitionTime >= fadeSeconds + CutsceneSeconds;
        bool showJunction = (Phase == RailTravelPhase.Cruise && PhaseDistance <= junctionRevealDistance) || Phase == RailTravelPhase.Junction || (Phase == RailTravelPhase.EntryTransition && beforeCut);
        bool showFarm = Phase == RailTravelPhase.Approach || Phase == RailTravelPhase.Farming || Phase == RailTravelPhase.MissedStop ||
            (Phase == RailTravelPhase.ExitTransition && beforeCut) || (Phase == RailTravelPhase.ReapproachTransition && beforeCut) ||
            ((Phase == RailTravelPhase.EntryTransition || Phase == RailTravelPhase.ReapproachTransition) && afterCut);
        junctionGeometry.gameObject.SetActive(showJunction);
        farmingGeometry.gameObject.SetActive(showFarm);
        if (centralRoadblock != null) centralRoadblock.SetActive(Phase == RailTravelPhase.Cruise || !BranchEntryFailed);
        if (farmingMaps != null) farmingMaps.Present(ActiveMapIndex, showFarm);
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
    }

    private void Update()
    {
        nearbySupply = -1;
        if (!Ready || Phase != RailTravelPhase.Farming || (TerminalUIManager.Instance != null && TerminalUIManager.Instance.IsUiOpen)) return;
        PlayerRef local = Runner.LocalPlayer;
        if (!Runner.TryGetPlayerObject(local, out NetworkObject player) || IsAboard(player.transform.position)) return;
        for (int i = 0; i < supplyPoints.Length; i++)
        {
            if ((CollectedSupplies & (1 << i)) != 0 || Vector3.Distance(player.transform.position, supplyPoints[i].position) > 3f) continue;
            nearbySupply = i;
            if (Input.GetKeyDown(KeyCode.E)) Rpc_CollectSupply(i);
            break;
        }
    }

    private void PresentTransition()
    {
        if (presentedPhase != Phase)
        {
            presentedPhase = Phase;
            if (IsTransition) TerminalUIManager.Instance?.CloseAllTerminals();
        }
        float alpha = 0f;
        bool inCinematic = false;
        if (IsTransition)
        {
            float cutTime = TransitionTime - fadeSeconds;
            inCinematic = UsesTunnelCinematic && cutTime >= 0f && cutTime < CutsceneSeconds;
            if (TransitionTime < fadeSeconds) alpha = TransitionTime / fadeSeconds;
            else if (inCinematic)
                alpha = Mathf.Max(1f - cutTime / CutEdgeFade, 1f - (CutsceneSeconds - cutTime) / CutEdgeFade);
            else alpha = 1f - (cutTime - CutsceneSeconds) / fadeSeconds;
            if (inCinematic && transitionDirector != null)
            {
                if (!cinematicPlaying) BeginCinematic();
                transitionDirector.time = cutTime;
                transitionDirector.Evaluate();
            }
        }
        if (!inCinematic && cinematicPlaying) EndCinematic();
        if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    private void BeginCinematic()
    {
        cinematicPlaying = true;
        if (referenceLayout != null) referenceLayout.SetActive(true);
        playerCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        cameraStates = new bool[playerCameras.Length];
        for (int i = 0; i < playerCameras.Length; i++)
        {
            cameraStates[i] = playerCameras[i].enabled;
            playerCameras[i].enabled = false;
        }
        transitionDirector.RebuildGraph();
    }

    private void EndCinematic()
    {
        transitionDirector.Stop();
        if (referenceLayout != null) referenceLayout.SetActive(false);
        foreach (GameObject obj in cinematicObjects) if (obj != null) obj.SetActive(false);
        for (int i = 0; i < playerCameras.Length; i++) if (playerCameras[i] != null) playerCameras[i].enabled = cameraStates[i];
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
        rect.sizeDelta = new Vector2(950f, 100f);
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
