using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class P4RadarUI : MonoBehaviour
{
    [Header("레이더 UI 연결")]
    public RectTransform radarCircle; // 레이더 원판 (크기 500x500 기준)
    public RectTransform sweepLine;   // 빙글빙글 도는 스캐너 바늘
    public Text briefingText;     // 우측 구역 브리핑 텍스트

    [Header("레이더 감지 설정")]
    public float radarRange = 100f;   // 실제 월드에서 감지할 최대 거리 (100m)
    public float sweepSpeed = 180f;   // 스캐너 회전 속도
    public Transform trainCenter;     // 기준점 (비워두면 0,0,0 기준으로 작동)

    // 레이더에 찍힐 점(Blip)들을 자동 관리하는 리스트
    private List<RectTransform> _blipPool = new List<RectTransform>();
    private float _radarRadius;

    private void Start()
    {
        if (radarCircle != null)
        {
            _radarRadius = radarCircle.rect.width / 2f;
        }

        // 초기 구역 브리핑 데이터 생성
        GenerateNewSectorBriefing();
    }

    private void Update()
    {
        // 패널이 켜져 있을 때만 작동
        if (!gameObject.activeInHierarchy) return;

        // 1. 스캐너 바늘 회전
        if (sweepLine != null)
        {
            sweepLine.Rotate(0f, 0f, -sweepSpeed * Time.deltaTime);
        }

        // 2. 주변 장애물 및 아이템 실시간 스캔 후 레이더에 표시
        UpdateRadarBlips();

        // [테스트용] R키를 누르면 새로운 갈림길 구역 정보를 랜덤 갱신
        if (Input.GetKeyDown(KeyCode.R))
        {
            GenerateNewSectorBriefing();
        }
    }

    private void UpdateRadarBlips()
    {
        if (radarCircle == null) return;

        Vector3 centerPos = (trainCenter != null) ? trainCenter.position : Vector3.zero;
        int blipIndex = 0;

        // 1) 씬에 있는 모든 TreadmillObstacle (장애물 = 빨간 점) 찾기
        var obstacles = FindObjectsByType<TreadmillObstacle>(FindObjectsSortMode.None);
        foreach (var obs in obstacles)
        {
            if (ShowOnRadar(obs.transform.position, centerPos, Color.red, ref blipIndex)) { }
        }

        // 2) 씬에 있는 모든 FarmableItem (아이템 = 노란/초록 점) 찾기
        var items = FindObjectsByType<FarmableItem>(FindObjectsSortMode.None);
        foreach (var item in items)
        {
            if (ShowOnRadar(item.transform.position, centerPos, Color.yellow, ref blipIndex)) { }
        }

        // 남은 점들은 숨기기
        for (int i = blipIndex; i < _blipPool.Count; i++)
        {
            _blipPool[i].gameObject.SetActive(false);
        }
    }

    private bool ShowOnRadar(Vector3 targetWorldPos, Vector3 centerPos, Color blipColor, ref int index)
    {
        Vector3 offset = targetWorldPos - centerPos;
        offset.y = 0f; // 높이는 무시하고 평면(X, Z) 거리만 계산

        float distance = offset.magnitude;
        if (distance > radarRange) return false; // 감지 범위 밖이면 패스

        // 월드 좌표를 레이더 원판 비율(-1 ~ 1)로 변환
        Vector2 normalizedPos = new Vector2(offset.x / radarRange, offset.z / radarRange);
        Vector2 blipPos = normalizedPos * _radarRadius;

        RectTransform blip = GetOrCreateBlip(index);
        blip.gameObject.SetActive(true);
        blip.anchoredPosition = blipPos;
        blip.GetComponent<Image>().color = blipColor;

        index++;
        return true;
    }

    private RectTransform GetOrCreateBlip(int index)
    {
        if (index < _blipPool.Count)
        {
            return _blipPool[index];
        }

        // 점(Blip) UI 오브젝트를 코드로 즉석 생성
        GameObject blipObj = new GameObject($"Radar_Blip_{index}");
        blipObj.transform.SetParent(radarCircle, false);

        RectTransform rect = blipObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(14f, 14f); // 점 크기

        Image img = blipObj.AddComponent<Image>();
        img.color = Color.red;

        _blipPool.Add(rect);
        return rect;
    }

    // ★ P4의 핵심 역할: 전방 갈림길 구역 브리핑 생성 (방향과 속도는 암호화!)
    public void GenerateNewSectorBriefing()
    {
        if (briefingText == null) return;

        string[] sectorNames = { "폐기된 군수공장", "오염된 지하터널", "고철 처리장", "무너진 교량 지대" };
        string sectorA = sectorNames[Random.Range(0, sectorNames.Length)];
        string sectorB = sectorNames[Random.Range(0, sectorNames.Length)];
        while (sectorA == sectorB) sectorB = sectorNames[Random.Range(0, sectorNames.Length)];

        briefingText.text =
            "<b>[ P4 전방 선로 관제 시스템 ]</b>\n" +
            "----------------------------------\n\n" +
            $"<color=#FF5555>■ 후보지 A : [{sectorA}]</color>\n" +
            " • 위험도 : ★★★★☆ (장애물 다수)\n" +
            " • 매장 자원 : 대형 배터리 및 연료 다량\n" +
            " • 진입 선로 : <color=#FFFF00>[암호화됨 - P2 표지판 확인]</color>\n" +
            " • 요구 속도 : <color=#FFFF00>[암호화됨 - P2 표지판 확인]</color>\n\n" +
            $"<color=#55FF55>■ 후보지 B : [{sectorB}]</color>\n" +
            " • 위험도 : ★☆☆☆☆ (안전 궤도)\n" +
            " • 매장 자원 : 소형 보급품 소량\n" +
            " • 진입 선로 : <color=#FFFF00>[암호화됨 - P2 표지판 확인]</color>\n" +
            " • 요구 속도 : <color=#FFFF00>[암호화됨 - P2 표지판 확인]</color>\n\n" +
            "----------------------------------\n" +
            "<size=20>* [R] 키를 눌러 전방 섹터 재스캔\n* 결정된 후보지를 P2(탐조등)에게 전달하세요!</size>";
    }
}
