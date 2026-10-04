using Cinemachine;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController), typeof(StarterAssetsInputs))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Player")]
        public float MoveSpeed = 3.5f;
        public float SprintSpeed = 6f;
        [Range(0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 12f;
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;
        public float JumpHeight = 1.4f;
        public float Gravity = -22f;
        [HideInInspector] public bool UseExternalGravity;
        [HideInInspector] public Vector3 ExternalGravityDirection = Vector3.down;
        public float JumpTimeout = 0.15f;
        public float FallTimeout = 0.15f;
        [Header("Player Grounded")]
        public bool Grounded;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.28f;
        public LayerMask GroundLayers = ~0;
        [Header("Cinemachine")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 70f;
        public float BottomClamp = -40f;
        public float CameraAngleOverride;
        public bool LockCameraPosition;
        [Header("Jump Forgiveness")]
        public float CoyoteTime = 0.12f;
        public float JumpBuffer = 0.15f;
        private StarterAssetsInputs input;
        private InfinityGravityBody gravity;
        private InfinityCharacterMotor motor;
        private Animator animator;
        private AudioSource movementAudio;
        private Transform cameraTransform, cameraUp, previousWorldUp;
        private CinemachineBrain brain;
        private Vector3 cameraForward = Vector3.forward;
        private Vector3 lastUp = Vector3.up;
        private float pitch, speed, verticalSpeed, groundedTime, jumpBufferTime, jumpCooldown, airborneTime;
        private bool jumping, initialized, alignmentReady;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput playerInput;
#endif
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int FallId = Animator.StringToHash("FreeFall");
        private static readonly int MotionId = Animator.StringToHash("MotionSpeed");
        public float VerticalSpeed => verticalSpeed;
        public InfinityCharacterMotor Motor => motor;
        public Vector3 MovementForward
        {
            get
            {
                Vector3 forward = Vector3.ProjectOnPlane(cameraTransform != null ? cameraTransform.forward : cameraForward, lastUp);
                return (forward.sqrMagnitude < 0.01f ? cameraForward : forward).normalized;
            }
        }
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            input = GetComponent<StarterAssetsInputs>(); gravity = GetComponent<InfinityGravityBody>();
            motor = GetComponent<InfinityCharacterMotor>();
            if (motor == null) motor = gameObject.AddComponent<InfinityCharacterMotor>();
            motor.Initialize(GetComponent<CharacterController>());
            animator = GetComponent<Animator>();
            if (animator != null)
            {
                movementAudio = GetComponent<AudioSource>();
                if (movementAudio == null) movementAudio = gameObject.AddComponent<AudioSource>();
                movementAudio.playOnAwake = false; movementAudio.spatialBlend = 1f;
            }
            cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (CinemachineCameraTarget != null) cameraForward = CinemachineCameraTarget.transform.forward;
            cameraForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
            if (cameraForward.sqrMagnitude < 0.01f) cameraForward = Vector3.forward;
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
            if (cameraTransform != null)
            {
                brain = cameraTransform.GetComponent<CinemachineBrain>();
                if (brain != null)
                {
                    cameraUp = new GameObject("Gravity Camera Up").transform;
                    previousWorldUp = brain.m_WorldUpOverride; brain.m_WorldUpOverride = cameraUp;
                }
            }
        }
        private void Start() => Initialize();
        private void Update() => Simulate(Time.deltaTime);
        public void Simulate(float dt)
        {
            Initialize();
            if (dt <= 0f) return;
            if (gravity == null) gravity = GetComponent<InfinityGravityBody>();
            if (gravity != null) gravity.ResolveGravity();
            Vector3 targetUp = gravity != null ? gravity.Up : UseExternalGravity ? -ExternalGravityDirection.normalized : Vector3.up;
            float acceleration = gravity != null ? gravity.Strength : Mathf.Abs(Gravity);
            Vector3 supportMotion = Grounded ? motor.SupportMotion() : Vector3.zero;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, targetUp);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(transform.right, targetUp);
            bool changingGravity = Vector3.Dot(lastUp, targetUp) < 0.999f;
            if (changingGravity)
            {
                cameraForward = Quaternion.FromToRotation(lastUp, targetUp) * cameraForward;
                verticalSpeed = 0f; lastUp = targetUp;
            }
            Quaternion aligned = Quaternion.LookRotation(forward.normalized, targetUp);
            if (changingGravity && alignmentReady) motor.AlignToGravity(aligned, gravity != null ? gravity.CurrentZone : null);
            else transform.rotation = aligned;
            alignmentReady = true;
            if (changingGravity) { motor.ClearSupport(); supportMotion = Vector3.zero; }
            Physics.SyncTransforms();
            motor.Depenetrate();
            Grounded = verticalSpeed <= 0.1f && motor.CheckGround(targetUp, GroundLayers);
            groundedTime = Grounded ? CoyoteTime : Mathf.Max(0f, groundedTime - dt);
            jumpCooldown = Mathf.Max(0f, jumpCooldown - dt); jumpBufferTime = Mathf.Max(0f, jumpBufferTime - dt);
            if (input.jump) { jumpBufferTime = JumpBuffer; input.jump = false; }
            if (Grounded) { verticalSpeed = -2f; airborneTime = 0f; jumping = false; }
            else airborneTime += dt;
            if (jumpBufferTime > 0f && groundedTime > 0f && jumpCooldown <= 0f)
            {
                verticalSpeed = Mathf.Sqrt(2f * acceleration * JumpHeight);
                groundedTime = 0f; jumpBufferTime = 0f;
                jumpCooldown = Mathf.Max(0.05f, JumpTimeout); Grounded = false; jumping = true;
            }
            verticalSpeed = Mathf.Max(-45f, verticalSpeed - acceleration * dt);
            Vector2 move = Vector2.ClampMagnitude(input.move, 1f);
            float magnitude = input.analogMovement ? move.magnitude : move.sqrMagnitude > 0f ? 1f : 0f;
            speed = Mathf.MoveTowards(speed, (input.sprint ? SprintSpeed : MoveSpeed) * magnitude, SpeedChangeRate * dt);
            Vector3 viewForward = MovementForward;
            Vector3 viewRight = Vector3.Cross(targetUp, viewForward).normalized;
            Vector3 direction = viewForward * move.y + viewRight * move.x;
            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, targetUp),
                    1f - Mathf.Exp(-dt / Mathf.Max(0.01f, RotationSmoothTime)));
            }
            motor.Move((direction * speed + targetUp * verticalSpeed) * dt + supportMotion, targetUp, Grounded, dt);
            if (motor.HitCeiling && verticalSpeed > 0f) verticalSpeed = 0f;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetFloat(SpeedId, speed, 0.1f, dt); animator.SetFloat(MotionId, magnitude);
                animator.SetBool(GroundedId, Grounded); animator.SetBool(JumpId, jumping);
                animator.SetBool(FallId, !Grounded && airborneTime > FallTimeout);
            }
        }
        private void LateUpdate()
        {
            if (!initialized || CinemachineCameraTarget == null) return;
            bool mouse = false;
#if ENABLE_INPUT_SYSTEM
            mouse = playerInput != null && playerInput.currentControlScheme == "KeyboardMouse";
#endif
            if (!LockCameraPosition)
            {
                float multiplier = mouse && !input.touchLook ? 1f : Time.deltaTime;
                cameraForward = Quaternion.AngleAxis(input.look.x * multiplier, lastUp) * cameraForward;
                pitch = Mathf.Clamp(pitch + input.look.y * multiplier, BottomClamp, TopClamp);
            }
            Quaternion basis = Quaternion.LookRotation(cameraForward, lastUp);
            CinemachineCameraTarget.transform.rotation = basis * Quaternion.Euler(pitch + CameraAngleOverride, 0f, 0f);
            if (cameraUp != null) cameraUp.rotation = Quaternion.Slerp(cameraUp.rotation, basis, 1f - Mathf.Exp(-Time.deltaTime * 8f));
        }
        public void ResetFallVelocity()
        {
            verticalSpeed = 0f; speed = 0f; jumping = false; Grounded = false; groundedTime = 0f;
            alignmentReady = false;
            if (motor != null) motor.ClearSupport();
        }
        private void OnDestroy()
        {
            if (brain != null && brain.m_WorldUpOverride == cameraUp) brain.m_WorldUpOverride = previousWorldUp;
            if (cameraUp != null) Destroy(cameraUp.gameObject);
        }
        private void OnFootstep(AnimationEvent evt)
        {
            if (evt.animatorClipInfo.weight > 0.5f && FootstepAudioClips != null && FootstepAudioClips.Length > 0)
                movementAudio.PlayOneShot(FootstepAudioClips[Random.Range(0, FootstepAudioClips.Length)], FootstepAudioVolume);
        }
        private void OnLand(AnimationEvent evt)
        {
            if (evt.animatorClipInfo.weight > 0.5f && LandingAudioClip != null) movementAudio.PlayOneShot(LandingAudioClip, FootstepAudioVolume);
        }
    }
}
