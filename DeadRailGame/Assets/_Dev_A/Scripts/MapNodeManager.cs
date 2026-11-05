using Fusion;
using UnityEngine;
using System;

//개별 구역(노드)의 데이터 구조체
[Serializable]
public struct SectorNodeData
{
    public string sectorName;
    public int dangerLevel;     //위험도 (1 ~ 5)
    public string rewardDesc;   //매장 자원 설명
    public int requiredSpeed;   //통과 요구 속도 (km/h) - P4에겐 암호화, P2 표지판에 공개!
}

public class MapNodeManager : NetworkBehaviour
{
    public static MapNodeManager Instance { get; private set; }

    [Header("갈림길(Node) 거리 설정")]
    [Tooltip("다음 갈림길까지 가야 하는 거리 (미터)")]
    public float distancePerStage = 300f;

    //*중요* 포톤 네트워크 동기화 변수들 (모든 플레이어가 동일한 맵과 진행도를 공유!)
    [Networked] public int CurrentStage { get; set; }
    [Networked] public int RandomSeed { get; set; }
    [Networked] public float RemainingDistance { get; set; }
    [Networked] public NetworkBool IsLeftSwitchSelected { get; set; }
    [Networked] public NetworkBool IsLeftTrackCandidateA { get; set; }

    //현재 스테이지의 후보지 A, B 데이터 (시드 기반으로 모든 클라이언트가 동일하게 계산)
    public SectorNodeData CandidateA { get; private set; }
    public SectorNodeData CandidateB { get; private set; }

    //UI 갱신 알림용 이벤트
    public event Action OnNodeUpdated;

    private int _lastGeneratedSeed = -1;

    private static readonly string[] PoolNamesHigh = { "폐기된 군수공장", "오염된 지하터널", "붕괴된 연구단지", "블랙마켓 화물역" };
    private static readonly string[] PoolNamesLow = { "외곽 우회 선로", "버려진 간이역", "고철 처리장", "건조한 평야 지대" };

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
            BuildLocalBranchData(RandomSeed);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        //기차 속도에 비례해 다음 갈림길까지 남은 거리 차감
        if (TreadmillManager.Instance != null && TreadmillManager.Instance.isSpawned)
        {
            //시속 (km/h) 체감을 위해 적절한 비율 (0.5f)로 거리 차감
            float moveDelta = TreadmillManager.Instance.CurrentSpeed * 0.5f * Runner.DeltaTime;
            RemainingDistance -= moveDelta;
        }

        //갈림길 도착 시 판정 후 다음 스테이지(노드)로 이동!
        if (RemainingDistance <= 0)
        {
            EvaluateBranchArrival();
        }
    }

    public override void Render()
    {
        if (Object == null || !Object.IsValid) return;

        //방장이 새로운 갈림길 시드를 생성하면 모든 팀원의 화면 데이터도 즉시 동기화 갱신
        if (_lastGeneratedSeed != RandomSeed && RandomSeed != 0)
        {
            BuildLocalBranchData(RandomSeed);
            OnNodeUpdated?.Invoke();
        }
    }

    //[방장 전용] 새로운 갈림길 (A/B 후보지) 생성
    public void GenerateNextBranch()
    {
        if (!HasStateAuthority)  return;

        RandomSeed = UnityEngine.Random.Range(1000, 99999);
        IsLeftTrackCandidateA = (UnityEngine.Random.value > 0.5f);
        BuildLocalBranchData(RandomSeed);
        OnNodeUpdated?.Invoke();
    }

    //시드(Seed) 번호를 이용해 모든 플레이어 컴퓨터에서 100% 똑같은 후보지 A, B를 생성
    private void BuildLocalBranchData(int seed)
    {
        _lastGeneratedSeed = seed;
        UnityEngine.Random.InitState(seed);

        //후보지 A : 고위험 고보상 (요구 속도 높음 : 60 ~ 90 km/h)
        CandidateA = new SectorNodeData
        {
            sectorName = PoolNamesHigh[UnityEngine.Random.Range(0, PoolNamesHigh.Length)],
            dangerLevel = UnityEngine.Random.Range(4, 6),
            rewardDesc = "대형 배터리팩 & 고순도 연료 다량",
            requiredSpeed = UnityEngine.Random.Range(6, 10) * 10
        };

        //후보지 B : 저위험 저보상 (요구 속도 낮음 : 20 ~ 50 km/h)
        CandidateB = new SectorNodeData
        {
            sectorName = PoolNamesLow[UnityEngine.Random.Range(0, PoolNamesLow.Length)],
            dangerLevel = UnityEngine.Random.Range(1, 3),
            rewardDesc = "소형 수리 키트 및 보급품 소량",
            requiredSpeed = UnityEngine.Random.Range(2, 6) * 10
        };
    }

    //갈림길 도달 시 : P3가 맞춰놓은 선로 방향과 P1이 맞춘 현재 기차 속도를 체크!
    private void EvaluateBranchArrival()
    {
        bool enteredA = ((bool)IsLeftSwitchSelected == (bool)IsLeftTrackCandidateA);
        SectorNodeData chosenNode = enteredA ? CandidateA : CandidateB;

        float currentSpeed = TreadmillManager.Instance != null ? TreadmillManager.Instance.CurrentSpeed : 0f;

        //요구 속도 오차 범위(+-15km/h) 또눈 관성 돌파 성공 여부 체크
        if (currentSpeed >= chosenNode.requiredSpeed - 10f)
        {
            Debug.Log($"✅ [Stage {CurrentStage} 돌파 성공!] '{chosenNode.sectorName}' 구역 진입! (현재속도: {currentSpeed} / 요구속도: {chosenNode.requiredSpeed})");
        }
        else
        {
            Debug.Log($"⚠️ [관성 부족 패널티!] 속도가 너무 느려({currentSpeed}km/h < {chosenNode.requiredSpeed}km/h) 다크 노드(위험 지대)로 미끄러졌습니다!");
        }

        //다음 스테이지 전진
        CurrentStage++;
        RemainingDistance = distancePerStage;
        GenerateNextBranch();
    }

    // *중요* P3가 총으로 선로 변환기(TrackSwitch)를 쐈을 때 호출할 네트워크 함수!
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_ToggleTrackSwitch()
    {
        IsLeftSwitchSelected = !IsLeftSwitchSelected;
        string dir = IsLeftSwitchSelected ? "◀ 왼쪽(LEFT) 선로" : "▶ 오른쪽(RIGHT) 선로";
        Debug.Log($"🔀 [선로 변환기 작동] 현재 기차 진행 궤도가 [{dir}]로 변경되었습니다!");
    }

    // [테스트 및 재스캔용 RPC]
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_ForceRescanBranch()
    {
        GenerateNextBranch();
    }
}
