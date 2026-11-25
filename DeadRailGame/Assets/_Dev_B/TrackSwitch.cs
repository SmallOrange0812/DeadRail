using UnityEngine;
using Fusion;

public class TrackSwitch : NetworkBehaviour
{
    // 실제 방향만 표현합니다. 좌우가 안전/위험을 뜻하지는 않습니다.
    public enum TrackDirection { Left = 0, Right = 1, Straight = 2 }

    [Header("표시 연출용 레버와 표시등")]
    public Transform leverPivot;
    public Light statusLight;
    public Renderer lampRenderer;

    [Header("3방향 전환기 모델 표시등 (선택)")]
    [SerializeField] private Renderer leftRouteLamp;
    [SerializeField] private Renderer rightRouteLamp;
    [SerializeField] private Renderer straightRouteLamp;
    [SerializeField] private Material inactiveRouteLamp;
    [SerializeField] private Material leftRouteLampOn;
    [SerializeField] private Material rightRouteLampOn;

    [Header("레버 회전 각도")]
    public Vector3 leftAngle = new Vector3(0f, 0f, -35f);
    public Vector3 rightAngle = new Vector3(0f, 0f, 35f);
    public float rotateSpeed = 8f;

    private MapNodeManager mapNodeManager;
    private Quaternion leverRestRotation = Quaternion.identity;
    private TrackDirection? renderedDirection;

    // 방향을 따로 저장하지 않고 MapNodeManager의 네트워크 값을 읽습니다.
    public TrackDirection CurrentDirection
    {
        get
        {
            RailRouteLoop route = RailRouteLoop.Instance;
            if (route != null && route.Ready)
            {
                RailRoute selected = route.Phase == RailTravelPhase.Cruise ? route.SelectedRoute : route.LockedRoute;
                return selected == RailRoute.Left ? TrackDirection.Left : selected == RailRoute.Right ? TrackDirection.Right : TrackDirection.Straight;
            }
            MapNodeManager manager = GetMapNodeManager();

            if (manager == null)
                return TrackDirection.Left;

            return (bool)manager.IsLeftSwitchSelected
                ? TrackDirection.Left
                : TrackDirection.Right;
        }
    }

    public override void Spawned()
    {
        GetMapNodeManager();
        if (leverPivot != null)
            leverRestRotation = leverPivot.localRotation;
        renderedDirection = null;
    }

    private MapNodeManager GetMapNodeManager()
    {
        if (mapNodeManager == null)
        {
            mapNodeManager = MapNodeManager.Instance;

            if (mapNodeManager == null)
                mapNodeManager = FindFirstObjectByType<MapNodeManager>();
        }

        return mapNodeManager;
    }

    // 무기 적중이나 상호작용 코드에서 호출할 진입점
    public void OnSwitchHit()
    {
        MapNodeManager manager = GetMapNodeManager();

        if (manager == null)
        {
            Debug.LogWarning("[TrackSwitch] MapNodeManager를 찾지 못했습니다.");
            return;
        }

        // 실제 방향 변경은 StateAuthority에서 처리합니다.
        manager.ApplyTrackSwitchHit();
    }

    public override void Render()
    {
        TrackDirection direction = CurrentDirection;
        bool isLeft = direction == TrackDirection.Left;

        if (leverPivot != null)
        {
            Vector3 targetAngle = direction == TrackDirection.Straight ? Vector3.zero : isLeft ? leftAngle : rightAngle;
            Quaternion targetRotation = Quaternion.Euler(targetAngle) * leverRestRotation;

            leverPivot.localRotation = Quaternion.Slerp(
                leverPivot.localRotation,
                targetRotation,
                Time.deltaTime * rotateSpeed
            );
        }

        if (renderedDirection == direction) return;
        renderedDirection = direction;

        if (leftRouteLamp != null)
            leftRouteLamp.sharedMaterial = isLeft ? leftRouteLampOn : inactiveRouteLamp;
        if (rightRouteLamp != null)
            rightRouteLamp.sharedMaterial = direction == TrackDirection.Right ? rightRouteLampOn : inactiveRouteLamp;
        if (straightRouteLamp != null)
            straightRouteLamp.sharedMaterial = direction == TrackDirection.Straight ? leftRouteLampOn : inactiveRouteLamp;

        // 예시 색상: 초록=왼쪽, 주황=오른쪽
        Color directionColor = isLeft
            ? Color.green
            : new Color(1f, 0.3f, 0f);

        if (statusLight != null)
            statusLight.color = directionColor;

        if (lampRenderer != null && lampRenderer.material != null)
            lampRenderer.material.color = directionColor;
    }
}
