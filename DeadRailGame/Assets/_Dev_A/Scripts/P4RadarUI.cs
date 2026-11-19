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

    private void Start()
    {
        if (radarCircle != null)
        {
            _radarRadius = radarCircle.rect.width / 2f;
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

        // 2. 주변 장애물 및 아이템 실시간 스캔
        UpdateRadarBlips();

        // 3. MapNodeManager의 실시간 갈림길 트리 및 남은 거리 브리핑 갱신
        RefreshNetworkBriefingText();

        // [테스트용] R키를 누르면 방장에게 요청해 새로운 갈림길 데이터 강제 갱신
        if (Input.GetKeyDown(KeyCode.R) && MapNodeManager.Instance != null)
        {
            MapNodeManager.Instance.Rpc_ForceRescanBranch();
        }
    }

    private void RefreshNetworkBriefingText()
    {
        if (briefingText == null) return;

        if (MapNodeManager.Instance == null || MapNodeManager.Instance.Object == null || !MapNodeManager.Instance.Object.IsValid)
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

        briefingText.text =
            $"<b>[ P4 노선 트리 관제 ]</b>  경로: {treeProgress} (Stage {mgr.CurrentStage}/5)\n" +
            $"분기점 도달까지 남은 거리 : <color=#FFFF00><b>{mgr.RemainingDistance:F0} m</b></color>\n" +
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
