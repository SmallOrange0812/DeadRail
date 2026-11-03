using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class P1HackingUI : MonoBehaviour
{
    [Header("기본 UI 연결 (Legacy)")]
    public Text statusText;
    public Slider speedSlider;
    public Button startHackButton;
    public RectTransform puzzleContainer;

    private enum PuzzleType { None, TypeA_Command, TypeB_Timing, TypeC_Nodes }
    private PuzzleType _currentPuzzle = PuzzleType.None;

    private float _targetSpeed = 30f;
    private float _timeRemaining = 0f;
    private bool _isPlaying = false;
    private Font _defaultFont;

    // --- [타입 A: 방향키 커맨드 변수] ---
    private List<KeyCode> _cmdSequence = new List<KeyCode>();
    private int _cmdIndex = 0;
    private Text _cmdDisplayText;

    // --- [타입 B: 타이밍 록픽 변수] ---
    private RectTransform _timingCursor;
    private float _timingPos = 0f; // 0 ~ 1
    private float _timingSpeed = 1f;
    private float _zoneMin = 0.4f, _zoneMax = 0.6f;
    private int _timingHitsNeeded = 3;
    private int _timingCurrentHits = 0;

    // --- [타입 C: 숫자 노드 순서 클릭 변수] ---
    private int _nextNodeNum = 1;
    private int _totalNodes = 6;

    private void Awake()
    {
        // 유니티 기본 내장 폰트 자동 로드 (Legacy Text 생성용)
        _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_defaultFont == null) _defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void Start()
    {
        if (speedSlider != null)
        {
            speedSlider.onValueChanged.AddListener(OnSpeedSliderChanged);
            _targetSpeed = speedSlider.value;
        }

        if (startHackButton != null)
        {
            startHackButton.onClick.AddListener(StartRandomPuzzle);
        }

        UpdateIdleStatus();
    }

    private void OnEnable()
    {
        if (!_isPlaying) UpdateIdleStatus();
    }

    private void OnSpeedSliderChanged(float val)
    {
        _targetSpeed = val;
        if (!_isPlaying) UpdateIdleStatus();
    }

    private void UpdateIdleStatus()
    {
        if (statusText == null) return;

        float currentSpd = (TreadmillManager.Instance != null) ? TreadmillManager.Instance.CurrentSpeed : 0f;
        string diffStr = _targetSpeed < 40f ? "<color=#55FF55>하 (Easy)</color>" :
                         (_targetSpeed < 75f ? "<color=#FFFF55>중 (Normal)</color>" : "<color=#FF5555>상 (Hard)</color>");

        statusText.text = $"<b>[ P1 기관실 속도 제어 시스템 ]</b>\n" +
                          $"현재 기차 속도 : {currentSpd:F0} km/h  |  목표 설정 속도 : <color=#00FFFF>{_targetSpeed:F0} km/h</color>\n" +
                          $"예상 해킹 난이도 : {diffStr} (슬라이더로 조절 후 해킹 시작 버튼을 누르세요)";
    }

    public void StartRandomPuzzle()
    {
        if (_isPlaying) return;

        ClearContainer();
        _isPlaying = true;
        if (startHackButton != null) startHackButton.interactable = false;
        if (speedSlider != null) speedSlider.interactable = false;

        // 0 ~ 1 사이의 난이도 비율 계산 (속도 10일 때 0.0, 속도 100일 때 1.0)
        float diffRatio = Mathf.Clamp01((_targetSpeed - 10f) / 90f);

        // 3가지 퍼즐 중 랜덤 선택
        int rand = Random.Range(0, 3);
        if (rand == 0) SetupTypeA_Command(diffRatio);
        else if (rand == 1) SetupTypeB_Timing(diffRatio);
        else SetupTypeC_Nodes(diffRatio);
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy || !_isPlaying) return;

        // 제한시간 감소
        _timeRemaining -= Time.deltaTime;
        if (_timeRemaining <= 0f)
        {
            PuzzleResult(false, "시간 초과! 해킹에 실패했습니다.");
            return;
        }

        // 현재 진행 중인 퍼즐 업데이트
        if (_currentPuzzle == PuzzleType.TypeA_Command) UpdateTypeA();
        else if (_currentPuzzle == PuzzleType.TypeB_Timing) UpdateTypeB();
        else if (_currentPuzzle == PuzzleType.TypeC_Nodes) UpdateTypeC();
    }

    // =========================================================================
    // [타입 A] 방향키 / WASD 커맨드 입력 퍼즐
    // =========================================================================
    private void SetupTypeA_Command(float diff)
    {
        _currentPuzzle = PuzzleType.TypeA_Command;
        int length = Mathf.RoundToInt(Mathf.Lerp(4f, 9f, diff)); // 속도에 따라 4개 ~ 9개
        _timeRemaining = Mathf.Lerp(10f, 6f, diff);

        KeyCode[] pool = { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow };
        _cmdSequence.Clear();
        for (int i = 0; i < length; i++) _cmdSequence.Add(pool[Random.Range(0, pool.Length)]);
        _cmdIndex = 0;

        _cmdDisplayText = CreateText(puzzleContainer, Vector2.zero, new Vector2(800, 200), 55);
        RefreshCommandText();
    }

    private void UpdateTypeA()
    {
        statusText.text = $"<b>[타입 A : 보안 커맨드 입력]</b> 제한시간: <color=#FFFF00>{_timeRemaining:F1}초</color>\n" +
                          "화면에 표시된 방향키(또는 WASD)를 순서대로 입력하세요!";

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) CheckCommandKey(KeyCode.UpArrow);
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) CheckCommandKey(KeyCode.DownArrow);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) CheckCommandKey(KeyCode.LeftArrow);
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) CheckCommandKey(KeyCode.RightArrow);
    }

    private void CheckCommandKey(KeyCode pressed)
    {
        if (pressed == _cmdSequence[_cmdIndex])
        {
            _cmdIndex++;
            RefreshCommandText();
            if (_cmdIndex >= _cmdSequence.Count)
            {
                PuzzleResult(true, "커맨드 해킹 완료!");
            }
        }
        else
        {
            // 틀리면 처음부터 다시 입력!
            _cmdIndex = 0;
            RefreshCommandText();
        }
    }

    private void RefreshCommandText()
    {
        string result = "";
        for (int i = 0; i < _cmdSequence.Count; i++)
        {
            string arrow = _cmdSequence[i] == KeyCode.UpArrow ? "▲" :
                           _cmdSequence[i] == KeyCode.DownArrow ? "▼" :
                           _cmdSequence[i] == KeyCode.LeftArrow ? "◀" : "▶";

            if (i < _cmdIndex) result += $"<color=#55FF55>{arrow}</color> ";
            else if (i == _cmdIndex) result += $"<color=#FFFF00>[{arrow}]</color> ";
            else result += $"<color=#777777>{arrow}</color> ";
        }
        _cmdDisplayText.text = result;
    }

    // =========================================================================
    // [타입 B] 타이밍 록픽 퍼즐 (스페이스바)
    // =========================================================================
    private void SetupTypeB_Timing(float diff)
    {
        _currentPuzzle = PuzzleType.TypeB_Timing;
        _timeRemaining = 12f;
        _timingSpeed = Mathf.Lerp(1.2f, 3.2f, diff); // 속도가 높을수록 바늘이 엄청 빨라짐
        float zoneWidth = Mathf.Lerp(0.28f, 0.12f, diff); // 속도가 높을수록 타겟 구역이 좁아짐

        _timingHitsNeeded = diff > 0.5f ? 3 : 2;
        _timingCurrentHits = 0;

        // 배경 바 생성 (폭 600)
        RectTransform bgBar = CreateImage(puzzleContainer, Vector2.zero, new Vector2(600, 50), new Color(0.2f, 0.2f, 0.2f));

        // 타겟 구역 설정
        _zoneMin = 0.5f - zoneWidth / 2f;
        _zoneMax = 0.5f + zoneWidth / 2f;
        CreateImage(bgBar, new Vector2(0, 0), new Vector2(600f * zoneWidth, 50), new Color(0.1f, 0.8f, 0.2f));

        // 움직이는 커서 생성
        _timingCursor = CreateImage(bgBar, new Vector2(-300, 0), new Vector2(12, 70), Color.yellow);
        _timingPos = 0f;
    }

    private void UpdateTypeB()
    {
        statusText.text = $"<b>[타입 B : 주파수 타이밍 동기화]</b> 제한시간: <color=#FFFF00>{_timeRemaining:F1}초</color>\n" +
                          $"노란 바늘이 초록색 구역에 들어왔을 때 <b>[Space바]</b>를 누르세요! ({_timingCurrentHits} / {_timingHitsNeeded})";

        // 좌우 왕복 이동 (PingPong)
        _timingPos = Mathf.PingPong(Time.time * _timingSpeed, 1f);
        if (_timingCursor != null)
        {
            _timingCursor.anchoredPosition = new Vector2(Mathf.Lerp(-300f, 300f, _timingPos), 0f);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_timingPos >= _zoneMin && _timingPos <= _zoneMax)
            {
                _timingCurrentHits++;
                if (_timingCurrentHits >= _timingHitsNeeded)
                {
                    PuzzleResult(true, "주파수 동기화 성공!");
                }
            }
            else
            {
                PuzzleResult(false, "타이밍 미스! 주파수 동기화 실패.");
            }
        }
    }

    // =========================================================================
    // [타입 C] 숫자 노드 순서대로 클릭하기 (1 ~ N)
    // =========================================================================
    private void SetupTypeC_Nodes(float diff)
    {
        _currentPuzzle = PuzzleType.TypeC_Nodes;
        _totalNodes = Mathf.RoundToInt(Mathf.Lerp(5f, 9f, diff)); // 5개 ~ 9개
        _timeRemaining = Mathf.Lerp(10f, 6.5f, diff);
        _nextNodeNum = 1;

        // 겹치지 않게 랜덤 위치에 숫자 버튼 배치
        List<Vector2> usedPositions = new List<Vector2>();
        for (int i = 1; i <= _totalNodes; i++)
        {
            int num = i;
            Vector2 randPos = Vector2.zero;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                randPos = new Vector2(Random.Range(-350f, 350f), Random.Range(-130f, 130f));
                bool overlap = false;
                foreach (var p in usedPositions)
                {
                    if (Vector2.Distance(randPos, p) < 95f) { overlap = true; break; }
                }
                if (!overlap) break;
            }
            usedPositions.Add(randPos);

            RectTransform btnRect = CreateImage(puzzleContainer, randPos, new Vector2(80, 80), new Color(0.1f, 0.4f, 0.3f));
            Button btn = btnRect.gameObject.AddComponent<Button>();
            Text btnTxt = CreateText(btnRect, Vector2.zero, new Vector2(80, 80), 32);
            btnTxt.text = num.ToString();

            btn.onClick.AddListener(() => OnNodeClicked(num, btnRect.gameObject));
        }
    }

    private void UpdateTypeC()
    {
        statusText.text = $"<b>[타입 C : 보안 노드 순차 해제]</b> 제한시간: <color=#FFFF00>{_timeRemaining:F1}초</color>\n" +
                          $"숫자 <b>1번부터 {_totalNodes}번까지</b> 순서대로 마우스로 빠르게 클릭하세요! (다음 숫자: <color=#00FFFF>{_nextNodeNum}</color>)";
    }

    private void OnNodeClicked(int num, GameObject btnObj)
    {
        if (!_isPlaying) return;

        if (num == _nextNodeNum)
        {
            btnObj.SetActive(false); // 맞춘 버튼은 숨김
            _nextNodeNum++;
            if (_nextNodeNum > _totalNodes)
            {
                PuzzleResult(true, "모든 보안 노드 해제 완료!");
            }
        }
        else
        {
            PuzzleResult(false, "잘못된 번호를 클릭했습니다! 해킹 실패.");
        }
    }

    // =========================================================================
    // 공통 결과 처리 및 UI 생성 헬퍼 함수
    // =========================================================================
    private void PuzzleResult(bool success, string message)
    {
        _isPlaying = false;
        _currentPuzzle = PuzzleType.None;
        ClearContainer();

        if (startHackButton != null) startHackButton.interactable = true;
        if (speedSlider != null) speedSlider.interactable = true;

        if (success)
        {
            // ★ 퍼즐 성공 시 포톤 네트워크를 통해 실제 기차 속도 변경!
            if (TreadmillManager.Instance != null)
            {
                TreadmillManager.Instance.Rpc_SetTrainSpeed(_targetSpeed);
            }
            statusText.text = $"<color=#55FF55><b>★ {message}</b></color>\n" +
                              $"기차 속도가 <b>{_targetSpeed:F0} km/h</b>로 변경되었습니다!";
        }
        else
        {
            statusText.text = $"<color=#FF5555><b>✖ {message}</b></color>\n" +
                              "다시 시도하려면 [속도 변경 해킹] 버튼을 누르세요.";
        }
    }

    private void ClearContainer()
    {
        if (puzzleContainer == null) return;
        for (int i = puzzleContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(puzzleContainer.GetChild(i).gameObject);
        }
    }

    private RectTransform CreateImage(RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject obj = new GameObject("UI_Img");
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        Image img = obj.AddComponent<Image>();
        img.color = color;
        return rect;
    }

    private Text CreateText(RectTransform parent, Vector2 pos, Vector2 size, int fontSize)
    {
        GameObject obj = new GameObject("UI_Txt");
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        Text txt = obj.AddComponent<Text>();
        txt.font = _defaultFont;
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.supportRichText = true;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        return rect.GetComponent<Text>();
    }
}