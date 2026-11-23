using UnityEngine;

/// <summary>Scrolls nearby depot scenery while keeping the distant horizon and route ground continuous.</summary>
public sealed class StartingRailYard : MonoBehaviour
{
    [SerializeField] private Transform scenery;
    [SerializeField, Min(1f)] private float clearDistance = 350f;
    [SerializeField] private Transform rearDistrict;
    [SerializeField] private Transform outdoorBackdrop;
    private Vector3 startingPosition;
    private Vector3 rearStartingPosition;
    private Transform legacyTracks;
    private Vector3 legacyTrackPosition;
    private bool initialized;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        if (scenery == null) return;
        initialized = true;
        startingPosition = scenery.localPosition;
        if (rearDistrict != null) rearStartingPosition = rearDistrict.localPosition;
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
        Initialize();
        if (!initialized) return;
        distance = Mathf.Max(0f, distance);
        bool visible = distance < clearDistance;
        if (scenery.gameObject.activeSelf != visible) scenery.gameObject.SetActive(visible);
        if (visible) scenery.localPosition = startingPosition + Vector3.back * distance;
        if (rearDistrict != null)
        {
            if (rearDistrict.gameObject.activeSelf != visible) rearDistrict.gameObject.SetActive(visible);
            if (visible) rearDistrict.localPosition = rearStartingPosition + Vector3.back * distance;
        }

        // The horizon is not part of the scrolling depot and never switches at the old 120 m boundary.
        // Farming maps own their terrain; hand over behind the existing transition fade.
        RailRouteLoop route = RailRouteLoop.Instance;
        bool outdoors = route == null || !route.Ready || route.farmingGeometry == null ||
            !route.farmingGeometry.gameObject.activeSelf;
        if (outdoorBackdrop != null && outdoorBackdrop.gameObject.activeSelf != outdoors)
            outdoorBackdrop.gameObject.SetActive(outdoors);

        // Lighting/fog belong to the scene and FarmingMapSet, not to a distance threshold.
        if (legacyTracks == null) return;
        legacyTracks.gameObject.SetActive(visible);
        if (visible) legacyTracks.localPosition = legacyTrackPosition + Vector3.back * distance;
    }
}
