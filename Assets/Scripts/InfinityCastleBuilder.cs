using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class InfinityCastleBuilder : MonoBehaviour
{
    private Bounds localBounds;
    private Vector3 motionStart, motionEnd, pivotOffset;
    private Quaternion rotationStart, rotationEnd;
    private float elapsed, duration;
    private Renderer[] renderers;
    private InfiniteCastleGrid gravityZone;
    private bool gravityEnabled;
    public InfiniteCastleGrid MotionGravityZone => gravityZone;
    internal void ConfigureMotionGravity(InfiniteCastleGrid zone, bool enabled)
    { gravityZone = zone; gravityEnabled = enabled; }
    public bool IsMoving { get; private set; }
    public bool IsAnimating => IsMoving;
    public bool IsTranslating => IsMoving && (motionEnd - motionStart).sqrMagnitude > 0.00001f;
    public bool IsRotating => IsMoving && Quaternion.Angle(rotationStart, rotationEnd) > 0.01f;
    public float Visibility => 1f;
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
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = owner.PooledMaterial(materials[i]);
            renderer.sharedMaterials = materials;
            renderer.SetPropertyBlock(null);
        }
        IsMoving = false;
    }
    public void Place(Vector3 centre, Quaternion orientation)
    {
        IsMoving = false;
        transform.rotation = orientation;
        transform.position = centre - orientation * localBounds.center;
        if (gravityZone != null) gravityZone.gravityDirection = -transform.up;
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
        pivotOffset = Vector3.zero; rotationStart = rotationEnd = transform.rotation;
        elapsed = 0f; duration = Mathf.Max(0.5f, seconds);
        ReservedBounds = SweepTo(offset); IsMoving = true;
    }
    public void BeginRideableShift(Vector3 offset, float seconds) => BeginShift(offset, seconds);
    public void BeginRearrange(Vector3 pivot, Vector3 offset, Quaternion orientation, float seconds, Bounds reservation)
    {
        motionStart = pivot; motionEnd = pivot + offset;
        pivotOffset = Quaternion.Inverse(transform.rotation) * (pivot - transform.position);
        rotationStart = transform.rotation; rotationEnd = orientation;
        elapsed = 0f; duration = Mathf.Max(0.5f, seconds);
        ReservedBounds = reservation; IsMoving = true;
    }
    public Bounds SweepRearrange(Vector3 pivot, Vector3 offset, Quaternion orientation)
    {
        Quaternion start = transform.rotation;
        Vector3 localPivot = Quaternion.Inverse(start) * (pivot - transform.position);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(start, orientation) / 5f));
        Bounds swept = WorldBounds;
        for (int i = 1; i <= steps; i++)
        {
            float fraction = (float)i / steps;
            Quaternion rotation = Quaternion.Slerp(start, orientation, fraction);
            Vector3 position = pivot + offset * fraction - rotation * localPivot;
            swept.Encapsulate(CastleGeometry.TransformBounds(localBounds,
                Matrix4x4.TRS(position, rotation, transform.lossyScale)));
        }
        // Pad the sampled arc by the maximum corner travel between adjacent samples.
        float radius = WorldBounds.extents.magnitude + Vector3.Distance(WorldBounds.center, pivot);
        float padding = 2f * radius * Mathf.Sin(Quaternion.Angle(start, orientation) / steps * Mathf.Deg2Rad * 0.25f);
        swept.Expand(padding * 2f + 0.15f);
        return swept;
    }
    public void Tick(float dt, bool paused)
    {
        if (paused || !IsMoving) return;
        elapsed = Mathf.Min(duration, elapsed + dt);
        float t = elapsed / duration;
        t = t * t * (3f - 2f * t);
        Quaternion rotation = Quaternion.Slerp(rotationStart, rotationEnd, t);
        transform.SetPositionAndRotation(Vector3.LerpUnclamped(motionStart, motionEnd, t) - rotation * pivotOffset, rotation);
        if (gravityZone != null && Quaternion.Angle(rotationStart, rotationEnd) > 0.01f)
        { gravityZone.gravityDirection = -transform.up; gravityZone.enabled = gravityEnabled; }
        if (elapsed >= duration) { IsMoving = false; ReservedBounds = WorldBounds; }
    }
    public void StopShift() { IsMoving = false; ReservedBounds = WorldBounds; }
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
        Vector3 e = local.extents;
        Vector3 size = new(
            Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
            Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
            Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
        return new Bounds(matrix.MultiplyPoint3x4(local.center), size * 2f);
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
