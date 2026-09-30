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
    private readonly HashSet<InfiniteCastleGrid> zones = new();
    private Rigidbody body;
    private bool originalUseGravity;
    private bool hasManualGravity;
    private Vector3 manualDirection;
    private float manualStrength;
    private InfiniteCastleGrid selectedZone;
    public Vector3 Direction { get; private set; } = Vector3.down;
    public float Strength { get; private set; } = 22f;
    public Vector3 Up => -Direction;
    public float AlignmentSpeed { get; private set; } = 8f;

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
    public void EnterZone(InfiniteCastleGrid zone) { zones.Add(zone); ResolveGravity(); }
    public void LeaveZone(InfiniteCastleGrid zone) { zones.Remove(zone); ResolveGravity(); }
    public void SetGravity(Vector3 direction, float strength, bool shouldAlign, float alignmentSpeed)
    {
        hasManualGravity = true;
        manualDirection = direction;
        manualStrength = strength;
        AlignmentSpeed = alignmentSpeed;
        ResolveGravity();
    }
    public void RestoreDefaultGravity() { hasManualGravity = false; ResolveGravity(); }
    public void ResolveGravity()
    {
        InfiniteCastleGrid best = null;
        float nearest = float.PositiveInfinity;
        foreach (InfiniteCastleGrid zone in zones)
        {
            if (zone == null || !zone.isActiveAndEnabled || !zone.Contains(transform.position)) continue;
            float distance = (zone.SurfacePoint - transform.position).sqrMagnitude;
            if (best == null || zone.priority > best.priority || (zone.priority == best.priority && distance < nearest - 0.25f))
            { best = zone; nearest = distance; }
        }
        // Keep the current zone on ties instead of flickering between overlapping triggers.
        if (selectedZone != null && selectedZone.isActiveAndEnabled && selectedZone.Contains(transform.position) &&
            best != null && best.priority == selectedZone.priority &&
            (selectedZone.SurfacePoint - transform.position).sqrMagnitude <= nearest + 0.25f) best = selectedZone;
        selectedZone = best;
        Vector3 direction = best != null ? best.gravityDirection : hasManualGravity ? manualDirection : defaultGravityDirection;
        Direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.down;
        Strength = Mathf.Max(0.1f, best != null ? best.gravityStrength : hasManualGravity ? manualStrength : defaultGravityStrength);
        if (best != null) AlignmentSpeed = best.alignmentSpeed;
        else if (!hasManualGravity) AlignmentSpeed = defaultAlignmentSpeed;
    }
    private void OnDisable()
    {
        zones.Clear(); selectedZone = null;
        if (body != null) body.useGravity = originalUseGravity;
    }
}
