using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class P4RadarUI : MonoBehaviour
{
    [Header("레이더 UI 연결 (Legacy)")]
    public RectTransform radarCircle;
    public RectTransform sweepLine;
    public Text briefingText; // Legacy Text 유지!

    [Header("레이더 감지 설정")]
    public float radarRange = 100f;
    public float sweepSpeed = 180f;
    public Transform trainCenter;

    private List<RectTransform> _blipPool = new List<RectTransform>();
    private float _radarRadius;
    private const float UiRefreshInterval = 0.1f;
    private float _nextUiRefreshTime;
    private Text _candidateAMarker;
    private Text _candidateBMarker;
    private Text _straightMarker;

    private void Start()
    {
        if (radarCircle != null)
        {
            _radarRadius = radarCircle.rect.width / 2f;
            _candidateAMarker = CreateNodeMarker("Radar_Node_A", new Color(1f, 0.25f, 0.2f));
            _candidateBMarker = CreateNodeMarker("Radar_Node_B", new Color(0.3f, 1f, 0.4f));
            _straightMarker = CreateNodeMarker("Radar_Node_Straight", Color.cyan);
        }

        if (trainCenter == null)
        {
            GameObject trainObject = GameObject.FindGameObjectWithTag("Train");
            if (trainObject != null) trainCenter = trainObject.transform;
        }
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy) return;

        // 1. 스캐너 바늘 회전
        if (sweepLine != null)
        {
            sweepLine.Rotate(0f, 0f, -sweepSpeed * Time.deltaTime);
        }

        // 레이더 오브젝트 검색과 브리핑 갱신은 10Hz면 충분합니다.
        // 매 프레임 전체 오브젝트 검색과 문자열 재생성을 하지 않도록 제한합니다.
        if (Time.unscaledTime < _nextUiRefreshTime) return;
        _nextUiRefreshTime = Time.unscaledTime + UiRefreshInterval;

        // 2. 주변 장애물 및 아이템 스캔
        UpdateRadarBlips();

        // 3. 네트워크 분기 후보와 남은 거리 브리핑 갱신
        RefreshNetworkBriefingText();
        UpdateRouteNodeMarkers();
    }

    private void RefreshNetworkBriefingText()
    {
        if (briefingText == null) return;
        RailRouteLoop route = RailRouteLoop.Instance;
        TreadmillManager train = TreadmillManager.Instance;
        if (route != null && route.Ready && train != null && train.isSpawned)
        {
            MapNodeManager nodes = MapNodeManager.Instance;
            SectorNodeData left = nodes.GetRouteNode(RailRoute.Left);
            SectorNodeData right = nodes.GetRouteNode(RailRoute.Right);
            briefingText.text = $"<b>[ P4 선로 관제 · 구간 {route.VisitNumber} ]</b>\n" +
                $"{route.TravelStatus()}\n" +
                $"연료 {train.Fuel:F1}/{train.FuelCapacity:F0} · 배터리 {train.Battery:F1}/{train.BatteryCapacity:F0}\n" +
                $"치료품 {train.MedicalKits} · 수리품 {train.RepairKits} · 탄약 {train.Ammunition}\n탑승 {route.PlayersAboard}/{route.PlayerCount}\n\n" +
                $"◀ {left.sectorName}\n연료 최대 용량 +{route.capacityUpgradeAmount:F0}\n" +
                $"▶ {right.sectorName}\n배터리 최대 용량 +{route.capacityUpgradeAmount:F0}\n\n" +
                "안전 진입 속도는 P2 탐조등 표지판에서 확인\n속도 오차 5 초과~15 km/h: 화물문 손상\n미선택 또는 15 km/h 초과: 블랙아웃\n" +
                "모든 맵에서 치료품·수리품·탄약·자원 파밍";
            return;
        }

        if (MapNodeManager.Instance == null ||
            MapNodeManager.Instance.Object == null ||
            !MapNodeManager.Instance.Object.IsValid ||
            !MapNodeManager.Instance.IsNodeDataReady)
        {
            briefingText.text = "<b>[ P4 전방 선로 관제 시스템 ]</b>\n\n네트워크 노드 데이터 수신 대기 중...";
            return;
        }

        var mgr = MapNodeManager.Instance;
        var nodeA = mgr.CandidateA;
        var nodeB = mgr.CandidateB;

        // 트리 진행도 시각화 (예: [●]ㅡ[●]ㅡ[▶]ㅡ[○]ㅡ[★])
        string treeProgress = "";
        for (int i = 1; i <= 5; i++)
        {
            if (i < mgr.CurrentStage) treeProgress += "<color=#55FF55>[■]</color>";
            else if (i == mgr.CurrentStage) treeProgress += "<color=#FFFF00>[▶]</color>";
            else treeProgress += "<color=#555555>[□]</color>";
            if (i < 5) treeProgress += "─";
        }

        string starsA = new string('★', nodeA.dangerLevel) + new string('☆', 5 - nodeA.dangerLevel);
        string starsB = new string('★', nodeB.dangerLevel) + new string('☆', 5 - nodeB.dangerLevel);
        string switchDir = mgr.IsLeftSwitchSelected ? "<color=#00FFFF>◀ 왼쪽(LEFT) 선로</color>" : "<color=#FFA500>▶ 오른쪽(RIGHT) 선로</color>";
        TreadmillManager trainManager = TreadmillManager.Instance;
        string resourceStatus = trainManager != null && trainManager.isSpawned
            ? $"연료: <color=#FFAA44>{trainManager.Fuel:F0}/{trainManager.FuelCapacity:F0}</color>  |  " +
              $"배터리: <color=#66CCFF>{trainManager.Battery:F0}/{trainManager.BatteryCapacity:F0}</color>"
            : "연료·배터리 데이터 수신 대기 중...";
        string travelStatus = $"분기점 도달까지 남은 거리 : <color=#FFFF00><b>{mgr.RemainingDistance:F0} m</b></color>";
        if (trainManager != null && trainManager.isSpawned &&
            trainManager.FarmingStop == TreadmillManager.FarmingStopPhase.Stopped)
        {
            travelStatus = "<color=#FFFF00><b>임시 파밍장 정차</b></color>  |  P1에서 재출발";
        }

        briefingText.text =
            $"<b>[ P4 노선 트리 관제 ]</b>  경로: {treeProgress} (Stage {mgr.CurrentStage}/5)\n" +
            $"{travelStatus}\n" +
            $"{resourceStatus}\n" +
            $"현재 선로 스위치 방향 : {switchDir}\n" +
            "--------------------------------------------------\n" +
            $"<color=#FF6666><b>■ 후보지 A : [{nodeA.sectorName}] (고위험·고보상)</b></color>\n" +
            $" • 위험도 : {starsA}  |  자원 : {nodeA.rewardDesc}\n" +
            $" • 방향 및 요구 속도 : <color=#FFFF00>[암호화됨 - P2 외부 표지판 확인 필수!]</color>\n\n" +
            $"<color=#66FF66><b>■ 후보지 B : [{nodeB.sectorName}] (안전 우회로)</b></color>\n" +
            $" • 위험도 : {starsB}  |  자원 : {nodeB.rewardDesc}\n" +
            $" • 방향 및 요구 속도 : <color=#FFFF00>[암호화됨 - P2 외부 표지판 확인 필수!]</color>\n" +
            "--------------------------------------------------\n" +
            "<size=20>* P2는 탐조등으로 표지판의 [실제 방향]과 [제한 속도]를 식별하세요!\n" +
            "* P1은 요구 속도를 해킹하고, P3는 스위치 방향을 사격해 맞추세요!</size>";
    }

    private void UpdateRouteNodeMarkers()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        if (route != null && route.Ready && _candidateAMarker != null && _candidateBMarker != null && _straightMarker != null)
        {
            RailRoute selected = route.Phase == RailTravelPhase.Cruise ? route.SelectedRoute : route.LockedRoute;
            float y = _radarRadius * 0.7f * Mathf.Clamp01(route.RemainingDistance / route.cruisingDistance);
            UpdateNodeMarker(_candidateAMarker, "연료↑", true, selected == RailRoute.Left, _radarRadius * 0.5f, y);
            UpdateNodeMarker(_candidateBMarker, "배터리↑", false, selected == RailRoute.Right, _radarRadius * 0.5f, y);
            _straightMarker.gameObject.SetActive(true);
            _straightMarker.text = selected == RailRoute.Straight
                ? (route.Phase == RailTravelPhase.Cruise ? "*미선택" : "*블랙아웃")
                : "블랙아웃";
            _straightMarker.rectTransform.anchoredPosition = new Vector2(0f, y + 28f);
            return;
        }
        if (_straightMarker != null) _straightMarker.gameObject.SetActive(false);
        MapNodeManager manager = MapNodeManager.Instance;
        bool hasNodeData = manager != null &&
                           manager.Object != null &&
                           manager.Object.IsValid &&
                           manager.IsNodeDataReady;

        if (!hasNodeData || _candidateAMarker == null || _candidateBMarker == null)
        {
            if (_candidateAMarker != null) _candidateAMarker.gameObject.SetActive(false);
            if (_candidateBMarker != null) _candidateBMarker.gameObject.SetActive(false);
            return;
        }

        bool candidateAIsLeft = (bool)manager.IsLeftTrackCandidateA;
        bool selectedIsLeft = (bool)manager.IsLeftSwitchSelected;
        float distanceRatio = Mathf.Clamp01(
            manager.RemainingDistance / Mathf.Max(manager.distancePerStage, 0.01f));

        // 레이더 위쪽은 먼 노드, 중앙은 기차에 가까운 노드를 나타냅니다.
        float forwardOffset = _radarRadius * 0.7f * distanceRatio;
        float sideOffset = _radarRadius * 0.32f;

        UpdateNodeMarker(
            _candidateAMarker,
            "A",
            candidateAIsLeft,
            selectedIsLeft == candidateAIsLeft,
            sideOffset,
            forwardOffset);

        bool candidateBIsLeft = !candidateAIsLeft;
        UpdateNodeMarker(
            _candidateBMarker,
            "B",
            candidateBIsLeft,
            selectedIsLeft == candidateBIsLeft,
            sideOffset,
            forwardOffset);
    }

    private void UpdateNodeMarker(
        Text marker,
        string nodeLabel,
        bool isLeft,
        bool isSelected,
        float sideOffset,
        float forwardOffset)
    {
        marker.gameObject.SetActive(true);
        marker.text = $"{(isSelected ? "*" : "")}{nodeLabel} {(isLeft ? "L" : "R")}";
        marker.rectTransform.anchoredPosition = new Vector2(
            isLeft ? -sideOffset : sideOffset,
            forwardOffset);
    }

    private Text CreateNodeMarker(string objectName, Color markerColor)
    {
        GameObject markerObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        markerObject.transform.SetParent(radarCircle, false);

        Text marker = markerObject.GetComponent<Text>();
        marker.font = briefingText != null
            ? briefingText.font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (marker.font == null)
            marker.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        marker.fontSize = 18;
        marker.fontStyle = FontStyle.Bold;
        marker.alignment = TextAnchor.MiddleCenter;
        marker.color = markerColor;
        marker.raycastTarget = false;
        marker.horizontalOverflow = HorizontalWrapMode.Overflow;
        marker.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform markerRect = marker.rectTransform;
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = new Vector2(54f, 28f);

        return marker;
    }

    private void UpdateRadarBlips()
    {
        if (radarCircle == null) return;

        Vector3 centerPos = (trainCenter != null) ? trainCenter.position : Vector3.zero;
        int blipIndex = 0;

        var obstacles = FindObjectsByType<TreadmillObstacle>(FindObjectsSortMode.None);
        foreach (var obs in obstacles)
        {
            ShowOnRadar(obs.transform.position, centerPos, Color.red, ref blipIndex);
        }

        // FarmableItem is not present in the current branch. Farmable pickups
        // are already identified by the Item tag in HarpoonFarming, so use the
        // same project-level marker for radar detection.
        GameObject[] items = GameObject.FindGameObjectsWithTag("Item");
        foreach (GameObject item in items)
        {
            if (item != null)
                ShowOnRadar(item.transform.position, centerPos, Color.yellow, ref blipIndex);
        }

        for (int i = blipIndex; i < _blipPool.Count; i++)
        {
            _blipPool[i].gameObject.SetActive(false);
        }
    }

    private void ShowOnRadar(Vector3 targetWorldPos, Vector3 centerPos, Color blipColor, ref int index)
    {
        Vector3 offset = targetWorldPos - centerPos;
        offset.y = 0f;

        if (offset.magnitude > radarRange) return;

        Vector2 normalizedPos = new Vector2(offset.x / radarRange, offset.z / radarRange);
        Vector2 blipPos = normalizedPos * _radarRadius;

        RectTransform blip = GetOrCreateBlip(index);
        blip.gameObject.SetActive(true);
        blip.anchoredPosition = blipPos;
        blip.GetComponent<Image>().color = blipColor;

        index++;
    }

    private RectTransform GetOrCreateBlip(int index)
    {
        if (index < _blipPool.Count) return _blipPool[index];

        GameObject blipObj = new GameObject($"Radar_Blip_{index}");
        blipObj.transform.SetParent(radarCircle, false);

        RectTransform rect = blipObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(14f, 14f);

        Image img = blipObj.AddComponent<Image>();
        img.color = Color.red;

        _blipPool.Add(rect);
        return rect;
    }
}
