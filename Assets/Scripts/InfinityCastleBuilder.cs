using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class InfinityCastleBuilder : MonoBehaviour
{
    private Bounds localBounds;
    private Vector3 motionStart, motionEnd;
    private float elapsed, duration;
    private Renderer[] renderers;
    private MaterialPropertyBlock properties;
    private bool revealing;
    private float dissolve;
    private static readonly int VisibilityId = Shader.PropertyToID("_CastleVisibility");
    public bool IsMoving { get; private set; }
    public bool HasShifted { get; private set; }
    public bool ReadyToPool => HasShifted && !IsMoving && dissolve >= 1f;
    public Bounds ReservedBounds { get; private set; }
    public Bounds WorldBounds => CastleGeometry.TransformBounds(localBounds, transform.localToWorldMatrix);
    public Vector3 AssignedCenter => transform.position;
    public float AssignedRadius => WorldBounds.extents.magnitude;

    public void InitializePooledPiece(DynamicInfinityCastle owner)
    {
        foreach (FloatingStructure legacy in GetComponentsInChildren<FloatingStructure>(true)) legacy.enabled = false;
        foreach (InfiniteCastleGrid zone in GetComponentsInChildren<InfiniteCastleGrid>(true)) zone.enabled = false;
        foreach (MeshCollider collider in GetComponentsInChildren<MeshCollider>(true))
            if (!collider.isTrigger) collider.convex = false;
        localBounds = CastleGeometry.RendererBoundsInRoot(transform);
        renderers = GetComponentsInChildren<Renderer>(true);
        properties ??= new MaterialPropertyBlock();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = owner.PooledMaterial(materials[i]);
            renderer.sharedMaterials = materials;
        }
        IsMoving = false;
        HasShifted = false;
        dissolve = 0f; revealing = false; SetVisibility(1f);
    }
    public void Place(Vector3 centre, Quaternion orientation)
    {
        IsMoving = false;
        HasShifted = false;
        dissolve = 0f; revealing = false; SetVisibility(1f);
        transform.rotation = orientation;
        transform.position = centre - orientation * localBounds.center;
        ReservedBounds = WorldBounds;
    }
    public Bounds SweepTo(Vector3 offset)
    {
        Bounds swept = WorldBounds;
        Bounds end = swept; end.center += offset;
        swept.Encapsulate(end); swept.Expand(0.15f);
        return swept;
    }
    public void BeginShift(Vector3 offset, float seconds)
    {
        motionStart = transform.position; motionEnd = motionStart + offset;
        elapsed = 0f; duration = Mathf.Max(0.5f, seconds);
        ReservedBounds = SweepTo(offset); IsMoving = true;
        HasShifted = true;
        dissolve = 0f; revealing = false;
    }
    public void BeginReveal(Vector3 offset, float seconds)
    {
        BeginShift(offset, seconds); revealing = true; HasShifted = false; SetVisibility(0f);
    }
    public void Tick(float dt, bool paused)
    {
        if (paused) return;
        if (!IsMoving)
        {
            if (HasShifted) { dissolve = Mathf.Min(1f, dissolve + dt / 0.8f); SetVisibility(1f - dissolve); }
            return;
        }
        elapsed = Mathf.Min(duration, elapsed + dt);
        float t = elapsed / duration;
        t = t * t * (3f - 2f * t);
        transform.position = Vector3.LerpUnclamped(motionStart, motionEnd, t);
        if (revealing) SetVisibility(Mathf.Min(1f, t * 2f));
        if (elapsed >= duration) { IsMoving = false; ReservedBounds = WorldBounds; }
    }
    public void StopShift() { IsMoving = false; ReservedBounds = WorldBounds; }
    private void SetVisibility(float amount)
    {
        if (renderers == null) return;
        properties.SetFloat(VisibilityId, amount);
        foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(amount >= 1f ? null : properties);
    }
}

public enum CastleMotionMode { Still }

public static class CastleGeometry
{
    public static Bounds RendererBoundsInRoot(Transform root)
    {
        Bounds bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            bool childActive = true;
            for (Transform ancestor = renderer.transform; ancestor != root; ancestor = ancestor.parent)
                if (!ancestor.gameObject.activeSelf) { childActive = false; break; }
            // Include an inactive pooled root, but never include disabled alternative stair meshes.
            if (!childActive) continue;
            Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Bounds part = TransformBounds(renderer.localBounds, matrix);
            if (!found) { bounds = part; found = true; } else bounds.Encapsulate(part);
        }
        return found ? bounds : new Bounds(Vector3.zero, Vector3.one);
    }
    public static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
    {
        Bounds bounds = new(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            bounds.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return bounds;
    }
    public static Quaternion Orientation(Vector3 up, float yaw = 0f)
    {
        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
        return Quaternion.AngleAxis(yaw, up) * Quaternion.LookRotation(forward.normalized, up);
    }
    public static Bounds Sweep(Bounds bounds, Vector3 offset, float clearance = 0.1f)
    {
        Bounds end = bounds; end.center += offset; bounds.Encapsulate(end); bounds.Expand(clearance); return bounds;
    }
}
