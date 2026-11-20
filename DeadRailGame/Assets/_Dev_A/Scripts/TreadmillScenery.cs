using UnityEngine;

public class TreadmillScenery : MonoBehaviour
{
    private Renderer[] trackRenderers;
    private Collider trackGround;
    private Renderer trackGroundRenderer;
    private Vector3 initialPosition;

    private void Awake()
    {
        trackRenderers = GetComponentsInChildren<Renderer>(true);
        Transform ground = transform.Find("Track ballast");
        if (ground != null)
        {
            trackGround = ground.GetComponent<Collider>();
            trackGroundRenderer = ground.GetComponent<Renderer>();
        }
        initialPosition = transform.position;
    }
    [Header("트랙 루프 설정 (8m 선로 x 9개 = 총 72m 기준)")]
    [Tooltip("기차 뒤로 완전히 넘어갔다고 판단할 Z 위치")]
    public float despawnZ = -24f;

    [Tooltip("전체 트랙 세트의 총 길이 (8m 선로 9개면 72)")]
    public float totalTrackLength = 72f;

    [Header("분기점(Turnout) 연출용 자식 오브젝트 (선택 사항)")]
    [Tooltip("이 선로 조각이 분기점 역할을 할 때 켜질 표지판/스위치 세트")]
    public GameObject branchEventProp;

    void Update()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        if (route != null && route.Ready)
        {
            return;
        }
        if (TreadmillManager.Instance == null || !TreadmillManager.Instance.isSpawned || TreadmillManager.Instance.CurrentSpeed <= 0f)
            return;

        float speed = TreadmillManager.Instance.CurrentSpeed;
        transform.Translate(Vector3.back * speed * Time.deltaTime, Space.World);

        // 기차 뒤(despawnZ)로 넘어가면 정확히 전체 트랙 길이(72m)만큼 앞으로 이동해 빈틈없이 이어 붙임!
        if (transform.position.z <= despawnZ)
        {
            transform.position += new Vector3(0f, 0f, totalTrackLength);
            CheckBranchSpawn();
        }
    }

    internal void Present(RailRouteLoop route)
    {
        float z = despawnZ + Mathf.Repeat(initialPosition.z - despawnZ - route.TravelledDistance, Mathf.Max(0.01f, totalTrackLength));
        transform.position = new Vector3(initialPosition.x, initialPosition.y, z);
        bool visible = route.ShowStraightTracks;
        foreach (Renderer trackRenderer in trackRenderers)
            trackRenderer.enabled = visible && (route.Phase != RailTravelPhase.Cruise || trackRenderer.bounds.min.z <= route.RemainingDistance - 24f);
        if (trackGround != null)
            trackGround.enabled = trackGroundRenderer.enabled;
    }

    private void CheckBranchSpawn()
    {
        if (branchEventProp == null || MapNodeManager.Instance == null) return;

        // 갈림길까지 남은 거리가 60m ~ 90m 사이일 때 앞으로 리스폰되는 선로에 분기점 기믹(표지판/스위치) 활성화!
        float remain = MapNodeManager.Instance.RemainingDistance;
        bool shouldShowBranch = (remain > 40f && remain <= 90f);

        if (branchEventProp.activeSelf != shouldShowBranch)
        {
            branchEventProp.SetActive(shouldShowBranch);
        }
    }
}
