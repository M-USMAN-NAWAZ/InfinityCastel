using UnityEngine;

public class InfiniteCastleGrid : MonoBehaviour
{
    [Header("Gravity Zone")]
    public Vector3 gravityDirection = Vector3.down;
    public float gravityStrength = 28f;
    public bool alignPlayerToGravity = true;
    public float alignmentSpeed = 8f;

    private Collider zoneCollider;

    private void Awake()
    {
        zoneCollider = FindTriggerCollider();
        if (zoneCollider == null)
            zoneCollider = CreateGravityTrigger();

        zoneCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        ApplyGravity(other);
    }

    private void OnTriggerStay(Collider other)
    {
        ApplyGravity(other);
    }

    private void OnTriggerExit(Collider other)
    {
        InfinityGravityBody gravityBody = other.GetComponentInParent<InfinityGravityBody>();
        if (gravityBody != null)
            gravityBody.RestoreDefaultGravity();
    }

    private void ApplyGravity(Collider other)
    {
        InfinityGravityBody gravityBody = other.GetComponentInParent<InfinityGravityBody>();
        if (gravityBody == null)
            return;

        gravityBody.SetGravity(gravityDirection, gravityStrength, alignPlayerToGravity, alignmentSpeed);
    }

    private Collider FindTriggerCollider()
    {
        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider candidate in colliders)
        {
            if (candidate.isTrigger)
                return candidate;
        }

        return null;
    }

    private Collider CreateGravityTrigger()
    {
        BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
        Bounds bounds = CalculateRendererBounds();
        Vector3 localSize = transform.InverseTransformVector(bounds.size + Vector3.one * 4f);

        trigger.center = transform.InverseTransformPoint(bounds.center);
        trigger.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        return trigger;
    }

    private Bounds CalculateRendererBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(transform.position, Vector3.one * 10f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 direction = gravityDirection.sqrMagnitude > 0.01f ? gravityDirection.normalized : Vector3.down;
        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(transform.position, direction * 4f);
    }
}
