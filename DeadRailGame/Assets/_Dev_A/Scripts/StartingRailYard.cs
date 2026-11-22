using UnityEngine;

/// <summary>Moves the opening scenery in the same direction as the treadmill tracks.</summary>
public sealed class StartingRailYard : MonoBehaviour
{
    [SerializeField] private Transform scenery;
    [SerializeField, Min(1f)] private float clearDistance = 120f;
    private Vector3 startingPosition;
    private Transform legacyTracks;
    private Vector3 legacyTrackPosition;
    private float fogEnd;
    private bool outdoorFog;

    private void Awake()
    {
        startingPosition = scenery.localPosition;
        fogEnd = RenderSettings.fogEndDistance;
        outdoorFog = RenderSettings.fog;
        legacyTracks = transform.parent != null ? transform.parent.Find("YardTrack_Static") : null;
        if (legacyTracks != null) legacyTrackPosition = legacyTracks.localPosition;
    }

    private void LateUpdate()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        if (route == null || !route.Ready || route.startingYard != this)
            Present(route != null && route.Ready ? route.TravelledDistance : 0f);
    }

    public void Present(float distance)
    {
        bool visible = distance < clearDistance;
        if (scenery.gameObject.activeSelf != visible)
        {
            scenery.gameObject.SetActive(visible);
            RenderSettings.fog = outdoorFog;
            RenderSettings.fogEndDistance = visible ? fogEnd : fogEnd * 3f;
        }
        if (visible)
        {
            scenery.localPosition = startingPosition + Vector3.back * distance;
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, fogEnd * 3f, distance / clearDistance);
        }
        if (legacyTracks == null) return;
        legacyTracks.gameObject.SetActive(visible);
        if (visible) legacyTracks.localPosition = legacyTrackPosition + Vector3.back * distance;
    }
}
