using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-110)]
[DisallowMultipleComponent]
public class InfinityGravityBody : MonoBehaviour
{
    public Vector3 defaultGravityDirection = Vector3.down;
    public float defaultGravityStrength = 22f;
    public bool alignToGravity = true;
    public float defaultAlignmentSpeed = 8f;
    public bool retainZoneGravityInAir;
    private readonly HashSet<InfiniteCastleGrid> zones = new();
    private Rigidbody body;
    private bool originalUseGravity;
    private bool hasManualGravity;
    private Vector3 manualDirection;
    private float manualStrength;
    private InfiniteCastleGrid selectedZone;
    private bool hasRetainedGravity;
    private Vector3 retainedDirection;
    private float retainedStrength;
    public Vector3 Direction { get; private set; } = Vector3.down;
    public float Strength { get; private set; } = 22f;
    public Vector3 Up => -Direction;
    public float AlignmentSpeed { get; private set; } = 8f;
    public InfiniteCastleGrid CurrentZone => selectedZone;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body != null) { originalUseGravity = body.useGravity; body.useGravity = false; }
        ResolveGravity();
    }
    private void Update() => ResolveGravity();
    private void OnEnable()
    {
        if (body != null) body.useGravity = false;
        ResolveGravity();
    }
    private void FixedUpdate()
    {
        if (body != null && !body.isKinematic) body.AddForce(Direction * Strength, ForceMode.Acceleration);
    }
    public void EnterZone(InfiniteCastleGrid zone) { if (zones.Add(zone)) ResolveGravity(); }
    public void LeaveZone(InfiniteCastleGrid zone) { if (zones.Remove(zone)) ResolveGravity(); }
    public void SetGravity(Vector3 direction, float strength, bool shouldAlign, float alignmentSpeed)
    {
        hasManualGravity = true;
        manualDirection = direction;
        manualStrength = strength;
        AlignmentSpeed = alignmentSpeed;
        ResolveGravity();
    }
    public void RestoreDefaultGravity() { hasManualGravity = false; hasRetainedGravity = false; ResolveGravity(); }
    private bool Occupies(InfiniteCastleGrid zone)
    {
        Vector3 centre = transform.position + transform.up * 0.9f;
        float allowance = zone.cornerTransition ? 0.8f : 0.05f;
        if (zone.surfaceBound && Vector3.Dot(centre - zone.SurfacePoint, -zone.gravityDirection.normalized) < -allowance) return false;
        return zone.Contains(transform.position) || zone.Contains(centre);
    }
    private float SurfaceDistance(InfiniteCastleGrid zone) => zone.DistanceToSurface(transform.position + transform.up * 0.9f);
    public void ResolveGravity()
    {
        InfiniteCastleGrid best = null;
        float nearest = float.PositiveInfinity;
        foreach (InfiniteCastleGrid zone in zones)
        {
            if (zone == null || !zone.isActiveAndEnabled || !Occupies(zone)) continue;
            float distance = SurfaceDistance(zone);
            if (best == null || zone.priority > best.priority || (zone.priority == best.priority && distance < nearest))
            { best = zone; nearest = distance; }
        }
        // Keep the current zone on ties instead of flickering between overlapping triggers.
        if (selectedZone != null && selectedZone.isActiveAndEnabled && Occupies(selectedZone) &&
            best != null && best.priority == selectedZone.priority &&
            SurfaceDistance(selectedZone) <= nearest + 0.15f) best = selectedZone;
        selectedZone = best;
        if (best != null)
        { retainedDirection = best.gravityDirection; retainedStrength = best.gravityStrength; hasRetainedGravity = true; }
        bool retain = retainZoneGravityInAir && hasRetainedGravity;
        Vector3 direction = best != null ? best.gravityDirection : hasManualGravity ? manualDirection : retain ? retainedDirection : defaultGravityDirection;
        Direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.down;
        Strength = Mathf.Max(0.1f, best != null ? best.gravityStrength : hasManualGravity ? manualStrength : retain ? retainedStrength : defaultGravityStrength);
        if (best != null) AlignmentSpeed = best.alignmentSpeed;
        else if (!hasManualGravity) AlignmentSpeed = defaultAlignmentSpeed;
    }
    private void OnDisable()
    {
        zones.Clear(); selectedZone = null; hasRetainedGravity = false;
        if (body != null) body.useGravity = originalUseGravity;
    }
}
