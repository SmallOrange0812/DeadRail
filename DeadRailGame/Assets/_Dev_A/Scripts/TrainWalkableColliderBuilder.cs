using UnityEngine;

/// <summary>
/// Adds collision to the imported train geometry so the player can walk on
/// the deck, battery bay surfaces, stairs, and roof.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrainWalkableColliderBuilder : MonoBehaviour
{
    private const float RoofDeckLocalY = 1.95f;
    private const float HarpoonPlatformWidth = 3.8f;
    private const float HarpoonPlatformDepth = 4.5f;
    private const float LadderOpeningSize = 1.7f;

    [Tooltip("Small decorative meshes below this horizontal size are ignored.")]
    [SerializeField] private float minimumColliderFootprint = 0.18f;

    private void Awake()
    {
        // The old full-height root box blocked access through the train. Reuse it
        // as a thin, continuous roof deck so tiny gaps between FBX pieces cannot
        // drop the player through the top of the car.
        BoxCollider roofDeck = GetComponent<BoxCollider>();
        if (roofDeck == null)
        {
            roofDeck = gameObject.AddComponent<BoxCollider>();
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasRenderBounds = false;
        Bounds renderBounds = default;
        foreach (Renderer meshRenderer in renderers)
        {
            if (!meshRenderer.enabled)
            {
                continue;
            }

            if (!hasRenderBounds)
            {
                renderBounds = meshRenderer.bounds;
                hasRenderBounds = true;
            }
            else
            {
                renderBounds.Encapsulate(meshRenderer.bounds);
            }
        }

        Vector3 deckCenter = hasRenderBounds
            ? transform.InverseTransformPoint(renderBounds.center)
            : Vector3.zero;
        Vector3 deckSize = new Vector3(
            hasRenderBounds ? Mathf.Max(5f, renderBounds.size.x + 0.5f) : 5f,
            0.1f,
            hasRenderBounds ? Mathf.Max(20f, renderBounds.size.z + 0.5f) : 20f);
        roofDeck.center = new Vector3(deckCenter.x, RoofDeckLocalY, deckCenter.z);
        roofDeck.isTrigger = false;

        Bounds ladderLocalBounds;
        bool hasLadder = TryGetLadderLocalBounds(out ladderLocalBounds);
        if (hasLadder)
        {
            roofDeck.enabled = false;
            CreateRoofDeckAroundLadderOpening(roofDeck.center, deckSize, ladderLocalBounds.center);
        }
        else
        {
            roofDeck.size = deckSize;
            roofDeck.enabled = true;
        }

        foreach (Collider rootCollider in GetComponents<Collider>())
        {
            if (rootCollider != roofDeck)
            {
                rootCollider.enabled = false;
            }
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;

        CreateHarpoonRoofPlatform();

        int added = 0;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0 || filter.GetComponent<Collider>() != null)
            {
                continue;
            }

            Renderer meshRenderer = filter.GetComponent<Renderer>();
            if (meshRenderer == null)
            {
                continue;
            }

            Vector3 size = meshRenderer.bounds.size;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float secondLargest = size.x + size.y + size.z - largest - Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (largest < minimumColliderFootprint || secondLargest < minimumColliderFootprint)
            {
                continue;
            }

            if (hasLadder && IsThinRoofMeshCrossingOpening(meshRenderer, ladderLocalBounds.center, RoofDeckLocalY))
            {
                continue;
            }

            MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = false;
            added++;
        }

        Debug.Log($"기차 보행 충돌면 생성 완료: {added}개 메시, 지붕 데크 중심 {roofDeck.center}, 크기 {roofDeck.size}");
    }

    private bool TryGetLadderLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        Transform ladder = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name.Contains("Roof_Access_Ladder"))
            {
                ladder = child;
                break;
            }
        }

        if (ladder == null) return false;

        Renderer[] ladderRenderers = ladder.GetComponentsInChildren<Renderer>(true);
        if (ladderRenderers.Length == 0) return false;

        bool initialized = false;
        foreach (Renderer item in ladderRenderers)
        {
            Bounds b = item.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
                Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else localBounds.Encapsulate(localCorner);
            }
        }

        return initialized;
    }

    private void CreateRoofDeckAroundLadderOpening(Vector3 center, Vector3 size, Vector3 ladderCenter)
    {
        float minX = center.x - size.x * 0.5f;
        float maxX = center.x + size.x * 0.5f;
        float minZ = center.z - size.z * 0.5f;
        float maxZ = center.z + size.z * 0.5f;
        float halfOpening = LadderOpeningSize * 0.5f;
        float holeMinX = Mathf.Clamp(ladderCenter.x - halfOpening, minX + 0.1f, maxX - 0.1f);
        float holeMaxX = Mathf.Clamp(ladderCenter.x + halfOpening, holeMinX + 0.1f, maxX - 0.1f);
        float holeMinZ = Mathf.Clamp(ladderCenter.z - halfOpening, minZ + 0.1f, maxZ - 0.1f);
        float holeMaxZ = Mathf.Clamp(ladderCenter.z + halfOpening, holeMinZ + 0.1f, maxZ - 0.1f);

        AddRoofDeckSegment("RoofDeck_Left", (minX + holeMinX) * 0.5f, center.y,
            center.z, holeMinX - minX, size.z);
        AddRoofDeckSegment("RoofDeck_Right", (holeMaxX + maxX) * 0.5f, center.y,
            center.z, maxX - holeMaxX, size.z);
        AddRoofDeckSegment("RoofDeck_Front", ladderCenter.x, center.y,
            (minZ + holeMinZ) * 0.5f, holeMaxX - holeMinX, holeMinZ - minZ);
        AddRoofDeckSegment("RoofDeck_Back", ladderCenter.x, center.y,
            (holeMaxZ + maxZ) * 0.5f, holeMaxX - holeMinX, maxZ - holeMaxZ);
    }

    private void AddRoofDeckSegment(string objectName, float x, float y, float z, float width, float depth)
    {
        if (width <= 0.05f || depth <= 0.05f) return;
        GameObject segment = new GameObject(objectName);
        segment.transform.SetParent(transform, false);
        segment.transform.localPosition = new Vector3(x, y, z);
        segment.layer = gameObject.layer;
        BoxCollider collider = segment.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, 0.1f, depth);
    }

    private bool IsThinRoofMeshCrossingOpening(Renderer meshRenderer, Vector3 openingCenter, float deckCenterY)
    {
        Bounds world = meshRenderer.bounds;
        Vector3 localCenter = transform.InverseTransformPoint(world.center);
        Vector3 localSize = transform.InverseTransformVector(world.size);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        bool nearDeck = Mathf.Abs(localCenter.y - deckCenterY) <= 0.35f && localSize.y <= 0.45f;
        bool overlapsOpening = Mathf.Abs(localCenter.x - openingCenter.x) < (localSize.x + LadderOpeningSize) * 0.5f
            && Mathf.Abs(localCenter.z - openingCenter.z) < (localSize.z + LadderOpeningSize) * 0.5f;
        return nearDeck && overlapsOpening;
    }

    private void CreateHarpoonRoofPlatform()
    {
        Transform harpoonMount = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Contains("P3_Weapon_Mount_ROOT"))
            {
                harpoonMount = child;
                break;
            }
        }

        Vector3 localPosition = harpoonMount != null
            ? transform.InverseTransformPoint(harpoonMount.position)
            : Vector3.zero;
        localPosition.y = RoofDeckLocalY;

        var platform = new GameObject("Generated_Harpoon_Roof_Platform");
        platform.transform.SetParent(transform, false);
        platform.transform.localPosition = localPosition;
        platform.transform.localRotation = Quaternion.identity;
        platform.transform.localScale = Vector3.one;
        platform.layer = gameObject.layer;

        BoxCollider platformCollider = platform.AddComponent<BoxCollider>();
        platformCollider.size = new Vector3(HarpoonPlatformWidth, 0.12f, HarpoonPlatformDepth);
        platformCollider.isTrigger = false;
    }
}
