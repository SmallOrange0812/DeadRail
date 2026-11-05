using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SearchlightSignboard : MonoBehaviour
{
    [Header("표지판에 숨겨진 구역/속도 브리핑 정보")]
    [TextArea(2, 4)]
    public string hiddenMessage = "▶ A구역 : 우측 선로\n⚡ 돌파 : 80km/h";
    public Color revealColor = new Color(0.2f, 1f, 0.8f, 1f); // 형광 민트/초록빛

    [Header("원거리 가독성 (글자 크기 조절)")]
    [Tooltip("멀리서도 잘 보이도록 글자 크기를 키웁니다 (기본 0.55)")]
    public float textSize = 0.55f;
    public int textFontSize = 100;

    [Header("탐조등 감지 민감도 설정")]
    [Tooltip("탐조등 사거리보다 얼마나 더 멀리서부터 빛을 감지할지 배율")]
    public float rangeMultiplier = 1.5f;
    public float angleTolerance = 10f;
    public float fadeInSpeed = 6f;
    public float fadeOutSpeed = 2f;

    [Header("연결 참조 (비워두면 자동 생성/연결됨)")]
    public TextMesh legacyTextMesh;
    public TMP_Text tmpText;
    public Renderer boardRenderer;

    private List<Light> _candidateLights = new List<Light>();
    private float _currentAlpha = 0f;
    private float _nextLightSearchTime = 0f;

    private void Start()
    {
        SetupTextComponents();
        RefreshLightList();
        ApplyVisualAlpha(0f);
    }

    [ContextMenu("🪧 표지판 3D 텍스트 자동 생성/갱신")]
    public void SetupTextComponents()
    {
        if (tmpText == null) tmpText = GetComponentInChildren<TMP_Text>(true);
        if (legacyTextMesh == null) legacyTextMesh = GetComponentInChildren<TextMesh>(true);

        if (tmpText == null && legacyTextMesh == null)
        {
            GameObject textObj = new GameObject("HiddenInfo_Text");
            textObj.transform.SetParent(transform, false);
            textObj.transform.localPosition = new Vector3(0f, 0f, -0.55f);
            textObj.transform.localEulerAngles = Vector3.zero;
            textObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);

            legacyTextMesh = textObj.AddComponent<TextMesh>();
        }

        if (legacyTextMesh != null)
        {
            legacyTextMesh.anchor = TextAnchor.MiddleCenter;
            legacyTextMesh.alignment = TextAlignment.Center;
            legacyTextMesh.fontStyle = FontStyle.Bold; // 멀리서도 선명한 굵은 글씨!
            legacyTextMesh.fontSize = textFontSize;
            legacyTextMesh.characterSize = textSize;
            legacyTextMesh.text = hiddenMessage;
        }

        if (tmpText != null)
        {
            tmpText.fontStyle = FontStyles.Bold;
            tmpText.text = hiddenMessage;
        }
    }

    private void RefreshLightList()
    {
        _candidateLights.Clear();
        Light[] allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);

        foreach (Light l in allLights)
        {
            if (l.type == LightType.Spot)
            {
                _candidateLights.Add(l);
            }
        }
    }

    private void Update()
    {
        if (Time.time >= _nextLightSearchTime)
        {
            RefreshLightList();
            _nextLightSearchTime = Time.time + 2f;
        }

        bool isIlluminated = CheckIfIlluminatedBySearchlight();

        float targetAlpha = isIlluminated ? 1f : 0f;
        float speed = isIlluminated ? fadeInSpeed : fadeOutSpeed;

        _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, Time.deltaTime * speed);
        ApplyVisualAlpha(_currentAlpha);
    }

    private bool CheckIfIlluminatedBySearchlight()
    {
        Vector3 boardPos = transform.position;

        foreach (Light spot in _candidateLights)
        {
            if (spot == null || !spot.isActiveAndEnabled || spot.intensity <= 0.1f)
                continue;

            Vector3 toBoard = boardPos - spot.transform.position;
            float distance = toBoard.magnitude;

            // 멀리서 다가올 때도 미리 빛을 감지하도록 사거리 여유(rangeMultiplier) 부여
            if (distance > spot.range * rangeMultiplier)
                continue;

            float angle = Vector3.Angle(spot.transform.forward, toBoard.normalized);
            float maxAllowedAngle = (spot.spotAngle * 0.5f) + angleTolerance;

            if (angle <= maxAllowedAngle)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyVisualAlpha(float alpha)
    {
        Color c = revealColor;
        c.a = alpha;

        if (tmpText != null)
        {
            tmpText.color = c;
            tmpText.gameObject.SetActive(alpha > 0.01f);
        }

        if (legacyTextMesh != null)
        {
            legacyTextMesh.color = c;
            legacyTextMesh.gameObject.SetActive(alpha > 0.01f);
        }

        if (boardRenderer != null && boardRenderer.material != null)
        {
            Color baseColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            boardRenderer.material.color = Color.Lerp(baseColor, revealColor * 0.5f, alpha);
        }
    }
}