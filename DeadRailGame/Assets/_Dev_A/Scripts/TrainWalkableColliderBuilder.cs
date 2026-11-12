using UnityEngine;

/// <summary>
/// Adds collision to the imported train geometry so the player can walk on
/// the deck, battery bay surfaces, stairs, and roof.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrainWalkableColliderBuilder : MonoBehaviour
{
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
        roofDeck.center = new Vector3(deckCenter.x, 1.95f, deckCenter.z);
        roofDeck.size = new Vector3(
            hasRenderBounds ? Mathf.Max(5f, renderBounds.size.x + 0.5f) : 5f,
            0.1f,
            hasRenderBounds ? Mathf.Max(20f, renderBounds.size.z + 0.5f) : 20f);
        roofDeck.isTrigger = false;

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

            MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = false;
            added++;
        }

        Debug.Log($"기차 보행 충돌면 생성 완료: {added}개 메시, 지붕 데크 중심 {roofDeck.center}, 크기 {roofDeck.size}");
    }
}
