using UnityEngine;

/// <summary>Places the authored scenery tiles using the same route distance on every peer.</summary>
public sealed class RailSideScenery : MonoBehaviour
{
    public const float TileLength = 64f;
    public const float StartDistance = 136f;
    private const float BackRange = 56f;
    private const float JunctionBlend = 32f;

    public void Present(RailRouteLoop route)
    {
        int count = transform.childCount;
        if (count == 0) return;

        float distance = route.TravelledDistance;
        int firstTile = Mathf.Max(0, Mathf.FloorToInt((distance - BackRange - StartDistance) / TileLength));
        for (int i = 0; i < count; i++)
        {
            int index = firstTile + i;
            Transform tile = transform.GetChild(index % count);
            float z = StartDistance + index * TileLength - distance;
            bool visible = route.Phase == RailTravelPhase.Cruise &&
                z + TileLength <= route.RemainingDistance - JunctionBlend;
            if (visible) tile.localPosition = Vector3.forward * z;
            if (tile.gameObject.activeSelf != visible) tile.gameObject.SetActive(visible);
        }
    }
}
