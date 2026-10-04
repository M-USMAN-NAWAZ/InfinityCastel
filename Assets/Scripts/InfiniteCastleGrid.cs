using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class InfiniteCastleGrid : MonoBehaviour
{
    public Vector3 gravityDirection = Vector3.down;
    public float gravityStrength = 22f;
    public bool alignPlayerToGravity = true;
    public float alignmentSpeed = 8f;
    public int priority;
    public bool surfaceBound;
    public bool cornerTransition;
    private BoxCollider trigger;
    private readonly Dictionary<InfinityGravityBody, HashSet<Collider>> occupants = new();
    public Vector3 SurfacePoint => transform.position;
    public float DistanceToSurface(Vector3 point)
    {
        if (trigger == null) return float.PositiveInfinity;
        Vector3 local = transform.InverseTransformPoint(point);
        Vector3 edge = trigger.size * 0.5f;
        Vector3 nearest = new(Mathf.Clamp(local.x, trigger.center.x - edge.x, trigger.center.x + edge.x),
            0f, Mathf.Clamp(local.z, trigger.center.z - edge.z, trigger.center.z + edge.z));
        return Vector3.Distance(point, transform.TransformPoint(nearest));
    }
    private void Awake()
    {
        foreach (BoxCollider box in GetComponents<BoxCollider>())
            if (box.isTrigger) { trigger = box; break; }
        if (trigger == null) trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
    }
    public void Configure(Vector3 down, Vector3 centre, Vector3 size, int zonePriority = 0, bool onSurface = false, bool atCorner = false)
    {
        if (trigger == null) Awake();
        gravityDirection = down.normalized;
        trigger.center = centre; trigger.size = size; priority = zonePriority; surfaceBound = onSurface; cornerTransition = atCorner;
    }
    public bool Contains(Vector3 point)
    {
        if (trigger == null || !trigger.enabled) return false;
        Vector3 local = transform.InverseTransformPoint(point) - trigger.center;
        Vector3 half = trigger.size * 0.5f + Vector3.one * 0.05f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }
    private void OnTriggerEnter(Collider other) => Register(other);
    private void OnTriggerStay(Collider other) => Register(other);
    private void Register(Collider other)
    {
        InfinityGravityBody gravity = other.GetComponentInParent<InfinityGravityBody>();
        if (gravity == null) return;
        if (!occupants.TryGetValue(gravity, out HashSet<Collider> colliders))
        { colliders = new HashSet<Collider>(); occupants.Add(gravity, colliders); }
        colliders.Add(other); gravity.EnterZone(this);
    }
    private void OnTriggerExit(Collider other)
    {
        InfinityGravityBody gravity = other.GetComponentInParent<InfinityGravityBody>();
        if (gravity == null) return;
        if (!occupants.TryGetValue(gravity, out HashSet<Collider> colliders)) return;
        colliders.Remove(other); if (colliders.Count > 0) return;
        occupants.Remove(gravity); gravity.LeaveZone(this);
    }
    private void OnDisable()
    {
        foreach (InfinityGravityBody gravity in occupants.Keys) if (gravity != null) gravity.LeaveZone(this);
        occupants.Clear();
        if (trigger != null) trigger.enabled = false;
    }
    private void OnEnable() { if (trigger != null) trigger.enabled = true; }
}
