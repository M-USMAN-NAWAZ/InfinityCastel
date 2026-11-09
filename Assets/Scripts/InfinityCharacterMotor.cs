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
    private bool forwardTreadSupport;
    public Vector3 Velocity { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public bool HitCeiling { get; private set; }
    public Collider CeilingCollider { get; private set; }
    [System.NonSerialized] public bool useTreadLanding;
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
    private bool Cast(Vector3 position, Vector3 direction, float distance, out RaycastHit nearest, bool ignoreLevelFloor = false)
    {
        Ends(position, out Vector3 top, out Vector3 bottom);
        int count = Physics.CapsuleCastNonAlloc(top, bottom, radius - skin, direction, hits, distance + skin, ~0, QueryTriggerInteraction.Ignore);
        nearest = default;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!IsObstacle(collider) || hits[i].distance >= closest) continue;
            if (ignoreLevelFloor && Vector3.Dot(hits[i].normal, transform.up) > .995f) continue;
            // Concave meshes can report a synthetic initial-overlap hit even when there is no resolvable surface contact.
            if (hits[i].distance <= 0.00001f && collider is MeshCollider mesh && !mesh.convex &&
                !Physics.ComputePenetration(capsule, position, transform.rotation, collider, collider.transform.position,
                    collider.transform.rotation, out _, out _)) continue;
            closest = hits[i].distance; nearest = hits[i];
        }
        return closest < float.PositiveInfinity;
    }
    public bool CheckGround(Vector3 up, LayerMask mask, Vector3 treadDirection = default)
    {
        Physics.SyncTransforms();
        forwardTreadSupport = false;
        Ends(transform.position, out _, out Vector3 bottom);
        int count = Physics.SphereCastNonAlloc(bottom + up * 0.08f, radius - skin, -up, hits,
            0.19f + skin, mask.value == 0 ? ~0 : mask.value, QueryTriggerInteraction.Ignore);
        RaycastHit best = default;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (IsObstacle(hits[i].collider) && Vector3.Dot(hits[i].normal, up) >= slopeDot && hits[i].distance < distance)
            { distance = hits[i].distance; best = hits[i]; }
        if (treadDirection.sqrMagnitude > .5f &&
            (best.collider == null || Vector3.Dot(transform.position - best.point, up) > .07f))
        {
            // A stepped capsule can still be behind the nosing. Keep the
            // new tread's support until the capsule reaches it horizontally.
            Vector3 origin = transform.position + treadDirection * (radius + skin * 2f) + up * .12f;
            count = Physics.RaycastNonAlloc(origin, -up, hits, .19f, mask.value == 0 ? ~0 : mask.value, QueryTriggerInteraction.Ignore);
            float forwardDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!IsObstacle(hit.collider) || hit.distance >= forwardDistance || Vector3.Dot(hit.normal, up) < .995f ||
                    !hit.collider.TryGetComponent<CastleStairRoute>(out var route) || !route.hasAuthoredTreads) continue;
                best = hit; forwardDistance = hit.distance;
            }
            forwardTreadSupport = forwardDistance < float.PositiveInfinity;
        }
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
    public bool HasBrakingSupport(Vector3 displacement, Vector3 up, LayerMask mask)
    {
        // A released stick may finish a braking step, but must not carry the
        // capsule over an edge. This query never changes the active support.
        Vector3 origin = transform.position + displacement + up * (stepHeight + .10f);
        int count = Physics.RaycastNonAlloc(origin, -up, hits, stepHeight + .25f,
            mask.value == 0 ? ~0 : mask.value, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (IsObstacle(hits[i].collider) && Vector3.Dot(hits[i].normal, up) >= slopeDot) return true;
        return false;
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
            // A return corner completes the pivot onto the real roof, inside its open rail gate.
            Vector3 tangent = Vector3.ProjectOnPlane(transform.position - surface.SurfacePoint, transform.up);
            if (surface.cornerSnapDistance > 0f && tangent.magnitude <= surface.cornerSnapDistance)
                transform.position -= tangent;
        }
        ClearSupport();
    }
    public void Move(Vector3 displacement, Vector3 up, bool allowStep, float deltaTime, bool treadPaced = false)
    {
        Vector3 start = transform.position, position = start;
        HitCeiling = false;
        CeilingCollider = null;
        int steps = Mathf.Clamp(Mathf.CeilToInt(displacement.magnitude / Mathf.Max(0.1f, radius)), 1, 32);
        Vector3 step = displacement / steps;
        for (int n = 0; n < steps; n++)
        {
            if (treadPaced && allowStep)
            {
                // Combining downward adhesion with forward travel lets a low
                // stair nosing cancel both components of a slow walking move.
                Vector3 planar = Vector3.ProjectOnPlane(step, up);
                Vector3 before = position;
                Sweep(planar, true);
                if (Vector3.Dot(position - before, planar) < planar.sqrMagnitude * .25f)
                    TryStep(ref position, planar, up, treadOnly: true);
                if (!forwardTreadSupport && Vector3.Dot(position - before, up) < .045f)
                    Sweep(step - planar, false);
            }
            else Sweep(step, allowStep);
        }
        transform.position = position; Depenetrate();
        Velocity = (transform.position - start) / Mathf.Max(0.0001f, deltaTime);
        if (support != null) supportPoint = support.InverseTransformPoint(transform.position);

        void Sweep(Vector3 remaining, bool canStep)
        {
            for (int iteration = 0; iteration < 5 && remaining.sqrMagnitude > 0.000001f; iteration++)
            {
                if (!Cast(position, remaining.normalized, remaining.magnitude, out RaycastHit hit, treadPaced && canStep))
                { position += remaining; break; }
                float travel = Mathf.Max(0f, hit.distance - skin);
                position += remaining.normalized * travel;
                remaining -= remaining.normalized * travel;
                float normalUp = Vector3.Dot(hit.normal, up);
                if (normalUp < -0.5f) { HitCeiling = true; CeilingCollider = hit.collider; }
                if (canStep && normalUp < 0.99f && TryStep(ref position, Vector3.ProjectOnPlane(remaining, up), up)) break;
                remaining = Vector3.ProjectOnPlane(remaining, hit.normal);
            }
        }
    }
    private bool TryStep(ref Vector3 position, Vector3 forward, Vector3 up, bool treadOnly = false)
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
        if (useTreadLanding)
        {
            // A capsule landing sweep can hit the previous tread's edge and
            // return the old height. Sample the leading sole instead, after
            // the full raised capsule has passed both clearance checks.
            float soleLead = radius + (treadOnly ? .13f : skin * 2f);
            Vector3 sole = across + forward.normalized * soleLead + up * .02f;
            int found = Physics.RaycastNonAlloc(sole, -up, hits, rise + .12f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity; RaycastHit tread = default;
            for (int i = 0; i < found; i++)
                if (IsObstacle(hits[i].collider) && Vector3.Dot(hits[i].normal, up) >= slopeDot && hits[i].distance < nearest)
                { tread = hits[i]; nearest = tread.distance; }
            if (tread.collider != null)
            {
                float bottomOffset = Vector3.Dot(transform.rotation * centre, up) - height * .5f;
                Vector3 landing = across + up * (Vector3.Dot(tread.point - across, up) + skin - bottomOffset);
                float gain = Vector3.Dot(landing - position, up);
                if (gain > .005f && gain <= rise + .02f)
                { position = landing; stepFailure = 0; stepBlocker = null; return true; }
            }
        }
        if (treadOnly) return false;
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
