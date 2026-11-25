using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class AimOutline
{
    private readonly Material material;
    private readonly Dictionary<Transform, Renderer[]> targets = new();
    private readonly Dictionary<Mesh, Mesh> outlineMeshes = new();
    private readonly List<Transform> removedTargets = new();
    private Renderer[] activeRenderers;
    private Transform activeTarget;

    public AimOutline(Material material)
    {
        this.material = material;
    }

    public void Show(Transform target, bool includeChildren)
    {
        if (material == null || ReferenceEquals(activeTarget, target)) return;

        SetVisible(activeRenderers, false);
        activeTarget = target;
        activeRenderers = null;
        if (target == null) return;

        if (!targets.TryGetValue(target, out activeRenderers))
        {
            // Supply models can be replaced between stops or destroyed when reeled in.
            foreach (Transform cached in targets.Keys)
                if (cached == null) removedTargets.Add(cached);
            foreach (Transform removed in removedTargets) targets.Remove(removed);
            removedTargets.Clear();
            MeshFilter[] sources = includeChildren
                ? target.GetComponentsInChildren<MeshFilter>()
                : target.GetComponents<MeshFilter>();
            if (includeChildren && target.parent != null &&
                (target.name == "Roof_Access_Ladder" || target.name.StartsWith("Roof_Access_Ladder.")))
            {
                // The train model imports rails and rungs as sibling meshes.
                var ladderParts = new List<MeshFilter>();
                foreach (Transform part in target.parent)
                {
                    string name = part.name;
                    if (name == "Roof_Access_Ladder" || name.StartsWith("Roof_Access_Ladder.") ||
                        name == "Ladder_Rung" || name.StartsWith("Ladder_Rung."))
                        ladderParts.AddRange(part.GetComponentsInChildren<MeshFilter>());
                }
                sources = ladderParts.ToArray();
            }
            var outlines = new List<Renderer>(sources.Length);
            foreach (MeshFilter source in sources)
            {
                if (source.name.StartsWith("Operator_Seat") || source.name.StartsWith("Seat_") ||
                    source.name.StartsWith("CRT_Title") || source.name.StartsWith("CRT_Status") ||
                    source.name.StartsWith("CRT_Power"))
                    continue;
                Mesh original = source.sharedMesh;
                if (original == null || !source.TryGetComponent(out MeshRenderer surface) || !surface.enabled) continue;

                var outlineObject = new GameObject("Aim outline") { layer = source.gameObject.layer };
                outlineObject.transform.SetParent(source.transform, false);
                var filter = outlineObject.AddComponent<MeshFilter>();
                filter.sharedMesh = GetOutlineMesh(original);
                var renderer = outlineObject.AddComponent<MeshRenderer>();
                var slots = new Material[original.subMeshCount];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                outlines.Add(renderer);
            }
            activeRenderers = outlines.ToArray();
            targets.Add(target, activeRenderers);
        }
        SetVisible(activeRenderers, true);
    }

    private Mesh GetOutlineMesh(Mesh original)
    {
        if (!original.isReadable) return original;
        if (outlineMeshes.TryGetValue(original, out Mesh mesh)) return mesh;

        Vector3[] vertices = original.vertices;
        Vector3[] normals = original.normals;
        if (normals.Length != vertices.Length) return original;
        var weldedNormals = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            weldedNormals.TryGetValue(vertices[i], out Vector3 normal);
            weldedNormals[vertices[i]] = normal + normals[i];
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 merged = weldedNormals[vertices[i]];
            if (merged.sqrMagnitude > 0.0001f) normals[i] = merged.normalized;
        }

        mesh = Object.Instantiate(original);
        mesh.normals = normals;
        outlineMeshes.Add(original, mesh);
        return mesh;
    }

    private static void SetVisible(Renderer[] renderers, bool visible)
    {
        if (renderers == null) return;
        foreach (Renderer renderer in renderers)
            if (renderer != null) renderer.enabled = visible;
    }

    public void Dispose()
    {
        foreach (Renderer[] renderers in targets.Values)
            foreach (Renderer renderer in renderers)
                if (renderer != null) Object.Destroy(renderer.gameObject);
        foreach (Mesh mesh in outlineMeshes.Values)
            if (mesh != null) Object.Destroy(mesh);
    }
}
