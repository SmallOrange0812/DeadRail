using Fusion;
using UnityEngine;
using System;
using System.Collections;

[Serializable]
public struct SectorNodeData
{
    public int mapIndex;
    public string sectorName;
    public int dangerLevel;
    public string rewardDesc;
    public int requiredSpeed;
}

public class MapNodeManager : NetworkBehaviour
{
    public static MapNodeManager Instance { get; private set; }

    [Header("갈림길(Node) 거리 설정")]
    public float distancePerStage = 1500f;

    [Header("기차 곡선 주행 연출 연결")]
    [Tooltip("하이어라키의 Train 오브젝트 연결 (분기점 통과 시 회전 연출)")]
    public Transform trainTransform;

    [Networked] public int CurrentStage { get; set; }
    [Networked] public int RandomSeed { get; set; }
    [Networked] public float RemainingDistance { get; set; }
    [Networked] public NetworkBool IsLeftSwitchSelected { get; set; }
    [Networked] public NetworkBool IsLeftTrackCandidateA { get; set; }

    public bool IsHighRiskRouteSelected => GetRouteNode(IsLeftSwitchSelected ? RailRoute.Left : RailRoute.Right).dangerLevel >= 4;

    // 분기점 회전 연출 트리거용 카운터
    [Networked] public int TurnSequenceCount { get; set; }

    [Networked] public NetworkBool lastTurnWasLeft { get; set; }

    public SectorNodeData CandidateA { get; private set; }
    public SectorNodeData CandidateB { get; private set; }
    public SectorNodeData BlackoutCandidate { get; private set; }
    public bool IsNodeDataReady => RandomSeed != 0 && _lastGeneratedSeed == RandomSeed;

    public event Action OnNodeUpdated;

    private int _lastGeneratedSeed = -1;
    private int _lastTurnSequence = 0;

    private static readonly string[] PoolNamesHigh = { "폐기된 군수공장", "붕괴된 연구단지" };
    private static readonly string[] PoolNamesLow = { "외곽 우회 선로", "버려진 간이역", "고철 처리장", "건조한 평야 지대" };
    private static readonly string[] PoolNamesBlackout = { "오염된 지하터널", "블랙마켓 화물역" };
    public const int NormalMapCount = 6;
    public const int MapCount = 8;

    public static string GetMapName(int index)
    {
        if (index < 0 || index >= MapCount) return "미확인 구역";
        if (index < PoolNamesLow.Length) return PoolNamesLow[index];
        if (index < NormalMapCount) return PoolNamesHigh[index - PoolNamesLow.Length];
        return PoolNamesBlackout[index - NormalMapCount];
    }

    public SectorNodeData GetRouteNode(RailRoute route)
    {
        EnsureBranchData();
        if (route == RailRoute.Straight) return BlackoutCandidate;
        bool candidateA = (route == RailRoute.Left) == (bool)IsLeftTrackCandidateA;
        return candidateA ? CandidateA : CandidateB;
    }

    public void EnsureBranchData()
    {
        if (Object != null && Object.IsValid && RandomSeed != 0 && _lastGeneratedSeed != RandomSeed)
            BuildLocalBranchData(RandomSeed);
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentStage = 1;
            RemainingDistance = distancePerStage;
            IsLeftSwitchSelected = true;
            GenerateNextBranch();
        }
        else
        {
            if (RandomSeed != 0)
            {
                BuildLocalBranchData(RandomSeed);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready) return;

        if (TreadmillManager.Instance != null && TreadmillManager.Instance.isSpawned)
        {
            float moveDelta = TreadmillManager.Instance.CurrentSpeed * 0.5f * Runner.DeltaTime;
            RemainingDistance -= moveDelta;

            if (RemainingDistance <= 0f)
            {
                EvaluateBranchArrival();
            }
        }
    }

    public override void Render()
    {
        if (Object == null || !Object.IsValid) return;

        if (_lastGeneratedSeed != RandomSeed && RandomSeed != 0)
        {
            BuildLocalBranchData(RandomSeed);
            OnNodeUpdated?.Invoke();
        }

        // ★ 분기점 통과 순간 모든 플레이어의 화면과 기차에 곡선 회전 애니메이션 재생!
        if (_lastTurnSequence != TurnSequenceCount)
        {
            _lastTurnSequence = TurnSequenceCount;
            bool isLeftTurn = (bool)lastTurnWasLeft;
            StopAllCoroutines();
            StartCoroutine(PlayTurnAnimationCoroutine(isLeftTurn));
        }
    }

    // ★ 핵심 연출: 선로를 따라 기차와 플레이어 화면(Roll/Shake)이 회전하는 애니메이션
    private IEnumerator PlayTurnAnimationCoroutine(bool isLeft)
    {
        // 내 로컬 플레이어 찾기 (PlayerMovement의 ExternalCameraRoll 연동용)
        PlayerMovement localPlayer = null;
        foreach (var p in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (p.Object != null && p.Object.IsValid && p.IsLocalPlayer) 
            { 
                localPlayer = p; break; 
            }
        }

        float duration = 2.5f; // 2.5초 동안 곡선 선로 통과 연출
        float elapsed = 0f;
        float dirSign = isLeft ? -1f : 1f; // 좌회전(-), 우회전(+)

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // 0 -> 1 -> 0 으로 부드럽게 꺾였다가 다시 직선으로 돌아오는 사인파 곡선
            float curveFactor = Mathf.Sin((elapsed / duration) * Mathf.PI);

            // 1) 기차(Train) 자체의 Y축 회전(Yaw) 및 Z축 기울기(Roll) 연출
            if (trainTransform != null)
            {
                float yawAngle = dirSign * 12f * curveFactor;  // 좌우 12도 꺾임
                float rollAngle = -dirSign * 4f * curveFactor; // 원심력 기울기
                trainTransform.rotation = Quaternion.Euler(0f, yawAngle, rollAngle);
            }

            // 2) 플레이어 1인칭 카메라 기울임(ExternalCameraRoll) 및 덜컹거림(Shake) 연동!
            if (localPlayer != null)
            {
                localPlayer.ExternalCameraRoll = -dirSign * 7f * curveFactor;
                localPlayer.ExternalCameraShake = UnityEngine.Random.insideUnitSphere * (0.04f * curveFactor);
            }

            yield return null;
        }

        // 연출 종료 후 원상복구
        if (trainTransform != null) trainTransform.rotation = Quaternion.identity;
        if (localPlayer != null)
        {
            localPlayer.ExternalCameraRoll = 0f;
            localPlayer.ExternalCameraShake = Vector3.zero;
        }
    }

    public void ResetTrackSwitchDirection()
    {
        // 네트워크 방향 값은 StateAuthority에서만 변경합니다.
        if (!HasStateAuthority) return;

        IsLeftSwitchSelected = true;
    }

    public void BeginRouteStage(int stage)
    {
        if (!HasStateAuthority) return;
        CurrentStage = stage;
        IsLeftSwitchSelected = true;
        GenerateNextBranch();
    }

    public void GenerateNextBranch()
    {
        if (!HasStateAuthority) return;

        int seed = UnityEngine.Random.Range(1000, 99999);
        if (seed == RandomSeed) seed = seed == 99998 ? 1000 : seed + 1;
        RandomSeed = seed;
        IsLeftTrackCandidateA = (UnityEngine.Random.value > 0.5f);
        BuildLocalBranchData(RandomSeed);
        OnNodeUpdated?.Invoke();
    }

    private void BuildLocalBranchData(int seed)
    {
        if (seed == 0)
        {
            CandidateA = default;
            CandidateB = default;
            BlackoutCandidate = default;
            _lastGeneratedSeed = -1;
            return;
        }

        _lastGeneratedSeed = seed;
        // Map node generation must not change Unity's shared random sequence.
        // Every peer gets the same candidates from the replicated seed.
        var nodeRandom = new System.Random(seed);

        int first = nodeRandom.Next(NormalMapCount);
        int second = (first + 1 + nodeRandom.Next(NormalMapCount - 1)) % NormalMapCount;
        CandidateA = CreateNode(first, nodeRandom);
        CandidateB = CreateNode(second, nodeRandom);
        BlackoutCandidate = CreateNode(NormalMapCount + nodeRandom.Next(PoolNamesBlackout.Length), nodeRandom);
    }

    private static SectorNodeData CreateNode(int mapIndex, System.Random random)
    {
        bool highRisk = mapIndex >= PoolNamesLow.Length;
        return new SectorNodeData
        {
            mapIndex = mapIndex,
            sectorName = GetMapName(mapIndex),
            dangerLevel = highRisk ? random.Next(4, 6) : random.Next(1, 4),
            rewardDesc = "치료품 · 수리품 · 탄약 · 연료 · 배터리",
            requiredSpeed = mapIndex >= NormalMapCount ? 0 : highRisk ? random.Next(6, 10) * 10 : random.Next(2, 6) * 10
        };
    }

    private void EvaluateBranchArrival()
    {
        bool enteredA = ((bool)IsLeftSwitchSelected == (bool)IsLeftTrackCandidateA);
        SectorNodeData chosenNode = enteredA ? CandidateA : CandidateB;

        float currentSpeed = TreadmillManager.Instance != null ? TreadmillManager.Instance.CurrentSpeed : 0f;

        if (currentSpeed >= chosenNode.requiredSpeed - 10f)
        {
            Debug.Log($"✅ [Stage {CurrentStage} 돌파 성공!] '{chosenNode.sectorName}' 구역 진입! (현재속도: {currentSpeed} / 요구속도: {chosenNode.requiredSpeed})");
        }
        else
        {
            Debug.Log($"⚠️ [관성 부족 패널티!] 속도가 너무 느려({currentSpeed}km/h < {chosenNode.requiredSpeed}km/h) 다크 노드로 미끄러졌습니다!");
        }

        lastTurnWasLeft = IsLeftSwitchSelected;

        // ★ 분기점 통과 회전 애니메이션 트리거!
        TurnSequenceCount++;

        CurrentStage++;
        RemainingDistance = distancePerStage;
        GenerateNextBranch();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_ToggleTrackSwitch()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        if (route != null && route.Ready)
        {
            route.CycleRoute();
            IsLeftSwitchSelected = route.SelectedRoute == RailRoute.Left;
            return;
        }
        IsLeftSwitchSelected = !(bool)IsLeftSwitchSelected;
        string dir = IsLeftSwitchSelected ? "◀ 왼쪽(LEFT) 선로" : "▶ 오른쪽(RIGHT) 선로";
        Debug.Log($"🔀 [선로 변환기 작동] 현재 기차 진행 궤도가 [{dir}]로 변경되었습니다!");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_ForceRescanBranch()
    {
        GenerateNextBranch();
    }

    private void Update()
    {
        if (!Debug.isDebugBuild) return;
        if (Object == null || !Object.IsValid) return;

        if (Input.GetKeyDown(KeyCode.F8))
        {
            Rpc_ToggleTrackSwitch();
        }
    }
}
