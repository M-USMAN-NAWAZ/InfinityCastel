using UnityEngine;
using StarterAssets;

[DisallowMultipleComponent]
public class InfinityGravityBody : MonoBehaviour
{
    [Header("Gravity")]
    public Vector3 defaultGravityDirection = Vector3.down;
    public float defaultGravityStrength = 28f;
    public bool alignToGravity = true;
    public float defaultAlignmentSpeed = 8f;

    private CharacterController characterController;
    private ThirdPersonController starterController;
    private Rigidbody body;
    private Vector3 activeGravityDirection;
    private float activeGravityStrength;
    private bool activeAlignment;
    private float activeAlignmentSpeed;
    private Vector3 velocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        starterController = GetComponent<ThirdPersonController>();
        body = GetComponent<Rigidbody>();
        RestoreDefaultGravity();
    }

    private void FixedUpdate()
    {
        if (body != null && characterController == null)
            body.AddForce(activeGravityDirection * activeGravityStrength, ForceMode.Acceleration);
    }

    private void Update()
    {
        if (characterController != null)
            MoveCharacterWithCustomGravity();

        if (activeAlignment)
            AlignTransformToGravity();
    }

    public void SetGravity(Vector3 direction, float strength, bool shouldAlign, float alignmentSpeed)
    {
        if (direction.sqrMagnitude < 0.01f)
            direction = defaultGravityDirection;

        activeGravityDirection = direction.normalized;
        activeGravityStrength = Mathf.Max(0f, strength);
        activeAlignment = shouldAlign;
        activeAlignmentSpeed = Mathf.Max(0.1f, alignmentSpeed);
        velocity = Vector3.zero;

        if (starterController != null)
        {
            starterController.UseExternalGravity = true;
            starterController.ExternalGravityDirection = activeGravityDirection;
        }
    }

    public void RestoreDefaultGravity()
    {
        activeGravityDirection = defaultGravityDirection.sqrMagnitude > 0.01f ? defaultGravityDirection.normalized : Vector3.down;
        activeGravityStrength = defaultGravityStrength;
        activeAlignment = alignToGravity;
        activeAlignmentSpeed = defaultAlignmentSpeed;
        velocity = Vector3.zero;

        if (starterController != null)
        {
            starterController.UseExternalGravity = false;
            starterController.ExternalGravityDirection = activeGravityDirection;
        }
    }

    private void MoveCharacterWithCustomGravity()
    {
        Vector3 localDown = activeGravityDirection.normalized;
        bool groundedAlongGravity = IsGroundedAlongGravity(localDown);

        if (groundedAlongGravity && Vector3.Dot(velocity, localDown) > 0f)
            velocity = localDown * 2f;

        velocity += localDown * activeGravityStrength * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private bool IsGroundedAlongGravity(Vector3 localDown)
    {
        Vector3 center = transform.TransformPoint(characterController.center);
        float checkDistance = Mathf.Max(0.05f, characterController.height * 0.5f - characterController.radius + 0.08f);
        Vector3 checkPosition = center + localDown * checkDistance;

        return Physics.CheckSphere(
            checkPosition,
            characterController.radius * 0.95f,
            ~0,
            QueryTriggerInteraction.Ignore);
    }

    private void AlignTransformToGravity()
    {
        Vector3 up = -activeGravityDirection.normalized;
        if (up.sqrMagnitude < 0.01f)
            return;

        Quaternion targetRotation = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * activeAlignmentSpeed);
    }
}
