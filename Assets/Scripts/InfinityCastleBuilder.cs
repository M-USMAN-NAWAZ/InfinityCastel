using UnityEngine;

public class InfinityCastleBuilder : MonoBehaviour
{
    [Header("Runtime State")]
    [SerializeField] private Vector3 assignedCenter;
    [SerializeField] private float assignedRadius = 8f;
    [SerializeField] private CastleMotionMode motionMode = CastleMotionMode.Still;

    [Header("One Way Arrival")]
    [SerializeField] private float arrivalDuration = 1.2f;

    private DynamicInfinityCastle manager;
    private Quaternion baseRotation;
    private Quaternion fixedRotation = Quaternion.identity;
    private Vector3 arrivalStart;
    private float arrivalProgress;
    private bool isArriving;
    private Vector3 exitStart;
    private Vector3 exitEnd;
    private float exitProgress;
    private bool isExiting;

    public Vector3 AssignedCenter => assignedCenter;
    public float AssignedRadius => assignedRadius;

    public void InitializePooledPiece(DynamicInfinityCastle owningManager)
    {
        manager = owningManager;
        baseRotation = transform.rotation;
        EnsureSolidCollider();
    }

    public void AssignSlot(Vector3 center, float radius, CastleMotionMode mode, float speed, float maxMotionRadius, Vector3 arrivalOffset)
    {
        assignedCenter = center;
        assignedRadius = radius;
        motionMode = mode;
        baseRotation = fixedRotation;
        transform.rotation = baseRotation;

        if (arrivalOffset.sqrMagnitude > 0.01f)
        {
            arrivalStart = assignedCenter + arrivalOffset;
            arrivalProgress = 0f;
            isArriving = true;
            isExiting = false;
            transform.position = arrivalStart;
        }
        else
        {
            isArriving = false;
            isExiting = false;
            transform.position = assignedCenter;
        }
    }

    public void BeginExit(Vector3 exitOffset, float duration)
    {
        arrivalDuration = Mathf.Max(0.05f, duration);
        exitStart = transform.position;
        exitEnd = transform.position + exitOffset;
        exitProgress = 0f;
        isArriving = false;
        isExiting = true;
    }

    public void SetFixedRotation(Quaternion rotation)
    {
        fixedRotation = rotation;
        baseRotation = rotation;
    }

    private void Update()
    {
        if (isExiting)
        {
            exitProgress += Time.deltaTime / Mathf.Max(0.05f, arrivalDuration);
            float exitT = Mathf.Clamp01(exitProgress);
            transform.position = Vector3.Lerp(exitStart, exitEnd, exitT);
            transform.rotation = baseRotation;

            if (exitT >= 1f)
                isExiting = false;

            return;
        }

        if (!isArriving)
        {
            transform.position = assignedCenter;
            transform.rotation = baseRotation;
            return;
        }

        arrivalProgress += Time.deltaTime / Mathf.Max(0.05f, arrivalDuration);
        float arrivalT = Mathf.Clamp01(arrivalProgress);
        transform.position = Vector3.Lerp(arrivalStart, assignedCenter, arrivalT);
        transform.rotation = baseRotation;

        if (arrivalT >= 1f)
            isArriving = false;
    }

    private void EnsureSolidCollider()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider candidate in colliders)
        {
            if (!candidate.isTrigger)
                return;
        }

        BoxCollider collider = gameObject.AddComponent<BoxCollider>();
        Bounds bounds = CalculateRendererBounds();
        Vector3 localSize = transform.InverseTransformVector(bounds.size);

        collider.center = transform.InverseTransformPoint(bounds.center);
        collider.size = new Vector3(
            Mathf.Max(1f, Mathf.Abs(localSize.x)),
            Mathf.Max(1f, Mathf.Abs(localSize.y)),
            Mathf.Max(1f, Mathf.Abs(localSize.z)));
    }

    private Bounds CalculateRendererBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(transform.position, Vector3.one * assignedRadius);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(assignedCenter, assignedRadius);

        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.4f);
        Gizmos.DrawLine(arrivalStart, assignedCenter);
    }
}

public enum CastleMotionMode
{
    Still
}
