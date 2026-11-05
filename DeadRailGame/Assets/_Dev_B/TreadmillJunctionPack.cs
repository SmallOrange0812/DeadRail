using UnityEngine;
using Fusion;

public class TreadmillJunctionPack : NetworkBehaviour
{
    [Header("트레드밀 이동 설정")]
    [Tooltip("기차가 달리는 반대 방향(기본: -Z)으로 오브젝트가 다가옵니다.")]
    public Vector3 moveDirection = new Vector3(0f, 0f, -1f);
    [Tooltip("TreadmillManager가 없을 때 사용할 기본 다가오는 속도")]
    public float fallbackSpeed = 15f;

    [Header("반복 테스트(Loop) 설정")]
    [Tooltip("체크하면 기차 뒤(despawnZ)로 지나갔을 때 다시 앞(respawnZ)으로 와서 무한 테스트가 가능합니다!")]
    public bool loopForTesting = true;
    public float despawnZ = -60f;
    public float respawnZ = 180f;

    [Header("내부 기믹 자동 연결")]
    public SearchlightSignboard signboard;
    public TrackSwitch trackSwitch;
    public JunctionBranchDirector junctionDirector;

    private void Awake()
    {
        AutoLinkChildren();
    }

    [ContextMenu("🔗 자식 기믹(표지판/스위치/디렉터) 자동 연결")]
    public void AutoLinkChildren()
    {
        if (signboard == null) signboard = GetComponentInChildren<SearchlightSignboard>(true);
        if (trackSwitch == null) trackSwitch = GetComponentInChildren<TrackSwitch>(true);
        if (junctionDirector == null) junctionDirector = GetComponentInChildren<JunctionBranchDirector>(true);

        if (junctionDirector != null && trackSwitch != null)
        {
            junctionDirector.targetSwitch = trackSwitch;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 멀티플레이 시 방장(StateAuthority)이 위치를 이동시켜 동기화 (단독 테스트 시에도 작동)
        if (Object != null && !HasStateAuthority) return;

        float currentSpeed = GetTreadmillSpeed();
        float dt = Runner != null ? Runner.DeltaTime : Time.deltaTime;

        transform.position += moveDirection.normalized * (currentSpeed * dt);

        // 기차 뒤로 완전히 지나갔을 때 다시 전방에서 다가오도록 리셋 (반복 테스트용)
        if (loopForTesting && transform.position.z <= despawnZ)
        {
            Vector3 pos = transform.position;
            pos.z = respawnZ;
            transform.position = pos;

            // 스위치 방향도 기본값(왼쪽 안전 선로)으로 초기화
            if (trackSwitch != null)
            {
                trackSwitch.CurrentDirection = TrackSwitch.TrackDirection.Left_SafeZone;
            }

            Debug.Log("🔄 [TreadmillJunctionPack] 분기점 세트가 전방에 재배치되었습니다!");
        }
    }

    private float GetTreadmillSpeed()
    {
        if (TreadmillManager.Instance != null)
        {
            var type = TreadmillManager.Instance.GetType();
            var field = type.GetField("currentSpeed") ?? type.GetField("scrollSpeed") ?? type.GetField("speed");
            if (field != null && field.FieldType == typeof(float))
                return (float)field.GetValue(TreadmillManager.Instance);

            var prop = type.GetProperty("CurrentSpeed") ?? type.GetProperty("ScrollSpeed");
            if (prop != null && prop.PropertyType == typeof(float))
                return (float)prop.GetValue(TreadmillManager.Instance);
        }
        return fallbackSpeed;
    }
}