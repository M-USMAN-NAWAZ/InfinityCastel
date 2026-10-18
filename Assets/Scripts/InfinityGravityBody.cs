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
    [Min(.05f)] public float cornerExitDistance = .45f;
    private readonly HashSet<InfiniteCastleGrid> zones = new();
    private Rigidbody body;
    private bool originalUseGravity;
    private bool hasManualGravity;
    private Vector3 manualDirection;
    private float manualStrength;
    private bool airborneOverride;
    private InfiniteCastleGrid selectedZone;
    private InfiniteCastleGrid alignedZone;
    private Vector3 alignedLocalCentre;
    private Vector3 centreOffset = Vector3.up * .9f;
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
        var settings = GetComponent<CharacterController>();
        if (settings != null) centreOffset = settings.center;
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
        alignedZone = null;
        hasManualGravity = true;
        manualDirection = direction;
        manualStrength = strength;
        AlignmentSpeed = alignmentSpeed;
        ResolveGravity();
    }
    public void SetAirborneGravity(Vector3 down)
    {
        airborneOverride = true;
        SetGravity(down, defaultGravityStrength, true, defaultAlignmentSpeed);
    }
    public void ReleaseAirborneGravity() { airborneOverride = false; ResolveGravity(); }
    public void RestoreDefaultGravity() { alignedZone = null; airborneOverride = false; hasManualGravity = false; hasRetainedGravity = false; ResolveGravity(); }
    public void ConfirmAlignment()
    {
        alignedZone = selectedZone;
        if (alignedZone != null) alignedLocalCentre = alignedZone.transform.InverseTransformPoint(transform.TransformPoint(centreOffset));
    }
    private bool Occupies(InfiniteCastleGrid zone, bool committed = false)
    {
        if (!committed && zone.cornerEntryOnly && Vector3.Dot(transform.up, -zone.gravityDirection.normalized) > .99f) return false;
        Vector3 centre = transform.TransformPoint(centreOffset);
        float allowance = zone.cornerTransition ? 0.8f : 0.05f;
        if (zone.surfaceBound && Vector3.Dot(centre - zone.SurfacePoint, -zone.gravityDirection.normalized) < -allowance) return false;
        return zone.Contains(transform.position) || zone.Contains(centre);
    }
    private float SurfaceDistance(InfiniteCastleGrid zone) => zone.DistanceToSurface(transform.TransformPoint(centreOffset));
    public void ResolveGravity()
    {
        if (airborneOverride)
        { selectedZone = null; Direction = manualDirection.normalized; Strength = Mathf.Max(.1f, manualStrength); return; }
        Vector3 centre = transform.TransformPoint(centreOffset);
        bool committed = alignedZone != null && alignedZone.isActiveAndEnabled && zones.Contains(alignedZone) && Occupies(alignedZone, true) &&
            (centre - alignedZone.transform.TransformPoint(alignedLocalCentre)).sqrMagnitude < cornerExitDistance * cornerExitDistance;
        if (!committed) alignedZone = null;
        InfiniteCastleGrid best = null;
        InfiniteCastleGrid sameDirection = null;
        float nearest = float.PositiveInfinity;
        float sameDistance = float.PositiveInfinity;
        foreach (InfiniteCastleGrid zone in zones)
        {
            if (zone == null || !zone.isActiveAndEnabled || !Occupies(zone)) continue;
            // A capsule pivot is not a request to cross back over the same seam.
            if (committed && Vector3.Dot(zone.gravityDirection.normalized, alignedZone.gravityDirection.normalized) < .99f) continue;
            float distance = SurfaceDistance(zone);
            if (Vector3.Dot(zone.gravityDirection.normalized, Direction) > .99f &&
                (sameDirection == null || zone.priority > sameDirection.priority || zone.priority == sameDirection.priority && distance < sameDistance))
            { sameDirection = zone; sameDistance = distance; }
            if (best == null || zone.priority > best.priority || (zone.priority == best.priority && distance < nearest))
            { best = zone; nearest = distance; }
        }
        if (sameDirection != null && best != null && best.priority == sameDirection.priority && sameDistance <= nearest + .25f)
            best = sameDirection;
        if (best == null && committed) best = alignedZone;
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
        zones.Clear(); selectedZone = null; alignedZone = null; hasRetainedGravity = false; airborneOverride = false;
        if (body != null) body.useGravity = originalUseGravity;
    }
}
