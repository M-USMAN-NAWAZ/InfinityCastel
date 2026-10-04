using UnityEngine;

// PhysX capsule sweeps allow a gravity-relative capsule instead of a world-Y CharacterController.
[DisallowMultipleComponent]
public sealed class InfinityCharacterMotor : MonoBehaviour
{
    private readonly RaycastHit[] hits = new RaycastHit[48];
    private readonly Collider[] overlaps = new Collider[48];
    private CapsuleCollider capsule;
    private float radius, height, stepHeight, skin, slopeDot;
    private Vector3 centre;
    private Transform support;
    private Vector3 supportPoint;
    public Vector3 Velocity { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public bool HitCeiling { get; private set; }
    public Collider CeilingCollider { get; private set; }
    private int stepFailure;
    private Collider stepBlocker;
    public string LastStepBlock => stepFailure == 0 ? "clear" :
        (stepFailure == 1 ? "Raised capsule: " : stepFailure == 2 ? "Across: " : "Landing: ") + (stepBlocker != null ? stepBlocker.name : "none");
    public void Initialize(CharacterController settings)
    {
        if (capsule != null) return;
        radius = settings.radius; height = Mathf.Max(settings.height, radius * 2f); centre = settings.center;
        stepHeight = Mathf.Clamp(settings.stepOffset, 0f, 0.4f);
        skin = Mathf.Clamp(settings.skinWidth, 0.005f, 0.04f);
        slopeDot = Mathf.Cos(settings.slopeLimit * Mathf.Deg2Rad);
        settings.enabled = false;
        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = radius; capsule.height = height; capsule.center = centre;
        Rigidbody rigidbody = GetComponent<Rigidbody>();
        if (rigidbody == null) rigidbody = gameObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true; rigidbody.useGravity = false;
    }
    private void Ends(Vector3 position, out Vector3 top, out Vector3 bottom)
    {
        Vector3 middle = position + transform.rotation * centre;
        float half = height * 0.5f - radius;
        top = middle + transform.up * half; bottom = middle - transform.up * half;
    }
    private bool IsObstacle(Collider collider) => collider != null && !collider.transform.IsChildOf(transform) &&
        !Physics.GetIgnoreLayerCollision(gameObject.layer, collider.gameObject.layer);
    private bool Cast(Vector3 position, Vector3 direction, float distance, out RaycastHit nearest)
    {
        Ends(position, out Vector3 top, out Vector3 bottom);
        int count = Physics.CapsuleCastNonAlloc(top, bottom, radius - skin, direction, hits, distance + skin, ~0, QueryTriggerInteraction.Ignore);
        nearest = default;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!IsObstacle(collider) || hits[i].distance >= closest) continue;
            // Concave meshes can report a synthetic initial-overlap hit even when there is no resolvable surface contact.
            if (hits[i].distance <= 0.00001f && collider is MeshCollider mesh && !mesh.convex &&
                !Physics.ComputePenetration(capsule, position, transform.rotation, collider, collider.transform.position,
                    collider.transform.rotation, out _, out _)) continue;
            closest = hits[i].distance; nearest = hits[i];
        }
        return closest < float.PositiveInfinity;
    }
    public bool CheckGround(Vector3 up, LayerMask mask)
    {
        Physics.SyncTransforms();
        Ends(transform.position, out _, out Vector3 bottom);
        int count = Physics.SphereCastNonAlloc(bottom + up * 0.08f, radius - skin, -up, hits,
            0.19f + skin, mask.value == 0 ? ~0 : mask.value, QueryTriggerInteraction.Ignore);
        RaycastHit best = default;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (IsObstacle(hits[i].collider) && Vector3.Dot(hits[i].normal, up) >= slopeDot && hits[i].distance < distance)
            { distance = hits[i].distance; best = hits[i]; }
        GroundHit = best;
        if (best.collider == null) { support = null; return false; }
        support = best.collider.transform; supportPoint = support.InverseTransformPoint(transform.position);
        return true;
    }
    public Vector3 SupportMotion()
    {
        if (support == null || !support.gameObject.activeInHierarchy) return Vector3.zero;
        return support.TransformPoint(supportPoint) - transform.position;
    }
    public void ClearSupport() { support = null; GroundHit = default; }
    public void AlignToGravity(Quaternion rotation, InfiniteCastleGrid surface = null)
    {
        // Turn around the capsule centre, not its feet, so a floor-to-wall turn does not bury it in the floor.
        Vector3 middle = transform.position + transform.rotation * centre;
        transform.rotation = rotation;
        transform.position = middle - rotation * centre;
        if (surface != null && surface.cornerTransition)
        {
            // A convex turn pivots onto the next face rather than leaving the feet behind its collider.
            float gap = Vector3.Dot(transform.position - surface.SurfacePoint, transform.up);
            if (gap < skin && gap > -height) transform.position += transform.up * (skin - gap);
        }
        ClearSupport();
    }
    public void Move(Vector3 displacement, Vector3 up, bool allowStep, float deltaTime)
    {
        Vector3 start = transform.position, position = start;
        HitCeiling = false;
        CeilingCollider = null;
        int steps = Mathf.Clamp(Mathf.CeilToInt(displacement.magnitude / Mathf.Max(0.1f, radius)), 1, 32);
        Vector3 step = displacement / steps;
        for (int n = 0; n < steps; n++)
        {
            Vector3 remaining = step;
            for (int iteration = 0; iteration < 5 && remaining.sqrMagnitude > 0.000001f; iteration++)
            {
                if (!Cast(position, remaining.normalized, remaining.magnitude, out RaycastHit hit)) { position += remaining; break; }
                float travel = Mathf.Max(0f, hit.distance - skin);
                position += remaining.normalized * travel;
                remaining -= remaining.normalized * travel;
                float normalUp = Vector3.Dot(hit.normal, up);
                if (normalUp < -0.5f) { HitCeiling = true; CeilingCollider = hit.collider; }
                if (allowStep && normalUp < 0.99f && TryStep(ref position, Vector3.ProjectOnPlane(remaining, up), up)) break;
                remaining = Vector3.ProjectOnPlane(remaining, hit.normal);
            }
        }
        transform.position = position; Depenetrate();
        Velocity = (transform.position - start) / Mathf.Max(0.0001f, deltaTime);
        if (support != null) supportPoint = support.InverseTransformPoint(transform.position);
    }
    private bool TryStep(ref Vector3 position, Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 0.00001f || stepHeight <= 0f) return false;
        float rise = stepHeight + skin * 2f;
        Vector3 raised = position + up * rise;
        // Test the raised capsule for headroom; an upward sweep can mistake a step lip for a ceiling.
        Ends(raised, out Vector3 top, out Vector3 bottom);
        int count = Physics.OverlapCapsuleNonAlloc(top, bottom, radius - skin, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (IsObstacle(overlaps[i])) { stepFailure = 1; stepBlocker = overlaps[i]; return false; }
        if (Cast(raised, forward.normalized, forward.magnitude, out RaycastHit wall)) { stepFailure = 2; stepBlocker = wall.collider; return false; }
        Vector3 across = raised + forward;
        if (!Cast(across, -up, rise + 0.08f, out RaycastHit ground) || Vector3.Dot(ground.normal, up) < slopeDot)
        { stepFailure = 3; stepBlocker = ground.collider; return false; }
        stepFailure = 0; stepBlocker = null;
        position = across - up * Mathf.Max(0f, ground.distance - skin);
        return true;
    }
    public void Depenetrate()
    {
        if (capsule == null) return;
        for (int pass = 0; pass < 3; pass++)
        {
            Ends(transform.position, out Vector3 top, out Vector3 bottom);
            int count = Physics.OverlapCapsuleNonAlloc(top, bottom, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            bool corrected = false;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = overlaps[i];
                if (!IsObstacle(obstacle)) continue;
                if (Physics.ComputePenetration(capsule, transform.position, transform.rotation, obstacle,
                    obstacle.transform.position, obstacle.transform.rotation, out Vector3 direction, out float distance))
                { transform.position += direction * (distance + skin); corrected = true; }
            }
            if (!corrected) break;
        }
    }
}
