using System;
using StarterAssets;
using UnityEngine;

// The motor owns collision/input; this scene-only driver owns every visual bone.
[DefaultExecutionOrder(-80), DisallowMultipleComponent, RequireComponent(typeof(TanjiroSword))]
public sealed class TanjiroProceduralBody : MonoBehaviour
{
    private const float MinimumLegBend = 10;
    public enum GaitStage { Contact, Loading, Propulsion, ToeOff, Recovery, Passing, Placement, Airborne, Balance }
    [Serializable] public struct BonePose
    {
        public Transform bone;
        public Vector3 position;
        public Quaternion rotation;
    }
    [Serializable] public struct FingerJoint
    {
        public Transform bone, tip;
        public Quaternion bind;
        public Vector3 curlAxis, oppositionAxis;
        public float relaxed, closed, opposition;
        public bool left;
    }
    [Serializable] public struct TwistJoint
    {
        public Transform bone, driver;
        public Quaternion driverBind;
        public Vector3 driverAxis, axis;
        public float amount;
        public void Pose()
        {
            Quaternion q = Quaternion.Inverse(driverBind) * driver.localRotation;
            Vector3 v = Vector3.Project(new Vector3(q.x, q.y, q.z), driverAxis);
            float signed = 2 * Mathf.Atan2(Vector3.Dot(v, driverAxis), q.w) * Mathf.Rad2Deg;
            signed = Mathf.DeltaAngle(0, signed);
            bone.localRotation = Quaternion.AngleAxis(Mathf.Clamp(signed, -100, 100) * amount, axis);
        }
    }
    [Serializable] public sealed class CoatPanel
    {
        public Transform root, hem;
        public Vector3 bindDown;
        public bool back, left;
        [NonSerialized] public float angle, angularVelocity, splay, splayVelocity;
    }
    [Serializable] public sealed class Limb
    {
        public Transform upper, lower, end;
        public Quaternion upperBasis, lowerBasis, restRotation, restLocalRotation;
        public Vector3 restEnd, restPole;
        public Vector3 handForward;
        public float soleHeight = .045f;
        [NonSerialized] private Vector3 previousPole;
        public void Calibrate(Transform model, Vector3 bend)
        {
            Vector3 normal = Vector3.Cross(lower.position - upper.position, bend).normalized;
            upperBasis = Quaternion.Inverse(Quaternion.Inverse(upper.rotation) * Quaternion.LookRotation(lower.position - upper.position, normal));
            lowerBasis = Quaternion.Inverse(Quaternion.Inverse(lower.rotation) * Quaternion.LookRotation(end.position - lower.position, normal));
            restEnd = model.InverseTransformPoint(end.position); restPole = model.InverseTransformPoint(lower.position);
            restRotation = Quaternion.Inverse(model.rotation) * end.rotation;
            restLocalRotation = end.localRotation;
        }
        public void PoseDirections(Vector3 upperDirection, Vector3 lowerDirection)
        {
            Vector3 normal = Vector3.Cross(upperDirection, lowerDirection).normalized;
            upper.rotation = Quaternion.LookRotation(upperDirection, normal) * upperBasis;
            lower.rotation = Quaternion.LookRotation(lowerDirection, normal) * lowerBasis;
            end.localRotation = restLocalRotation;
        }
        public void Solve(Vector3 target, Quaternion rotation, Vector3 hint, float dt, float wristWeight = 1, float poseWeight = 1, float minimumBend = 0)
        {
            if (poseWeight <= 0) return;
            Quaternion beforeUpper = upper.localRotation, beforeLower = lower.localRotation, beforeEnd = end.localRotation;
            Vector3 start = upper.position, delta = target - start;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, end.position);
            if (a < .001f || b < .001f || delta.sqrMagnitude < .000001f) return;
            Vector3 direction = delta.normalized;
            float extension = Mathf.Sqrt(a * a + b * b + 2 * a * b * Mathf.Cos(minimumBend * Mathf.Deg2Rad));
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .002f, extension - .002f);
            Vector3 pole = Vector3.ProjectOnPlane(hint - start, direction);
            Vector3 old = Vector3.ProjectOnPlane(upper.parent.TransformDirection(previousPole), direction);
            if (pole.sqrMagnitude < .00001f) pole = old.sqrMagnitude > .00001f ? old : Vector3.ProjectOnPlane(upper.up, direction);
            pole.Normalize();
            if (old.sqrMagnitude > .00001f) pole = Vector3.Slerp(old.normalized, pole, 1 - Mathf.Exp(-18f * dt)).normalized;
            float along = (a * a + distance * distance - b * b) / (2 * distance);
            float radius = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            if (handForward.sqrMagnitude > .1f && wristWeight > 0)
            {
                // Select the elbow on the two-bone reach circle that preserves
                // a usable wrist angle. The hand must not fold to satisfy a pole.
                Vector3 forward = rotation * handForward;
                Vector3 best = -Vector3.ProjectOnPlane(forward, direction).normalized;
                float Bend(Vector3 p) => Vector3.Angle(direction * (distance - along) - p * radius, forward);
                if (best.sqrMagnitude > .1f && Bend(pole) > 65)
                {
                    float lo = 0, hi = 1;
                    for (int i = 0; i < 7; i++)
                    {
                        float t = (lo + hi) * .5f;
                        if (Bend(Vector3.Slerp(pole, best, t)) > 65) lo = t; else hi = t;
                    }
                    pole = Vector3.Slerp(pole, best, hi * Mathf.SmoothStep(0, 1, wristWeight)).normalized;
                }
            }
            previousPole = upper.parent.InverseTransformDirection(pole);
            Vector3 elbow = start + direction * along + pole * radius;
            Vector3 finish = start + direction * distance;
            Vector3 normal = Vector3.Cross(elbow - start, finish - elbow).normalized;
            if (normal.sqrMagnitude < .0001f) return;
            upper.rotation = Quaternion.LookRotation(elbow - start, normal) * upperBasis;
            lower.rotation = Quaternion.LookRotation(finish - elbow, normal) * lowerBasis;
            end.rotation = Quaternion.Slerp(lower.rotation * restLocalRotation, rotation, wristWeight);
            upper.localRotation = Quaternion.Slerp(beforeUpper, upper.localRotation, poseWeight);
            lower.localRotation = Quaternion.Slerp(beforeLower, lower.localRotation, poseWeight);
            end.localRotation = Quaternion.Slerp(beforeEnd, end.localRotation, poseWeight);
        }
    }
    private sealed class Foot
    {
        public Vector3 position, start, target, normal, localContact, requested;
        public Transform support;
        public float progress = 1;
        public bool planted;
        public float soleHeight;
        public bool gaitContact;
        public Quaternion plantedRotation;
        public Vector3 airLocal;
        public Vector3 groundPoint;
        public float pitch, contactPitch, terrain;
        public Vector3 velocity, stepVelocity;
        public float stepDuration = .25f, stepLift = .05f;
        public float phase, load;
        public RaycastHit landing;
        public Vector3 landingLocal;
        public bool landingReady;
        public bool swingActive, swingJustStarted, cycleReady;
        public float contactDuty = .3f, contactElapsed, contactSeconds, swingElapsed, swingDuration;
        public float swingStartPhase, contactStartPhase, contactPivot;
        public Quaternion swingFrame;
        public float landingFootError, landingReachError;
        public Vector3 swingStartLocal, swingStartVelocity;
        public float swingStartHeight, swingStartPitch;
        public int plants;
        public GaitStage stage;
        public Vector3 takeoffDelta;
        public float takeoffReach, takeoffProgress;
    }

    [Header("Scene-calibrated rig (no runtime animation clips)")]
    public BonePose[] bones = Array.Empty<BonePose>();
    public Limb leftLeg = new(), rightLeg = new(), leftArm = new(), rightArm = new();
    public Transform pelvis, spine, chest, head;
    public Transform leftShoulder, rightShoulder, leftToe, rightToe;
    public FingerJoint[] fingers = Array.Empty<FingerJoint>();
    public TwistJoint[] twists = Array.Empty<TwistJoint>();
    public CoatPanel[] coatPanels = Array.Empty<CoatPanel>();
    public Quaternion leftToeBind, rightToeBind;
    public Vector3 leftToeAxis, rightToeAxis;
    public Vector3 leftPalmOffset;
    public Quaternion leftWristFromSword = Quaternion.identity;
    [Header("Contact and balance")]
    [Tooltip("Duration of a resting-foot adjustment; running has a speed-adapted cadence.")]
    [Range(.1f, .45f)] public float stepSeconds = .30f;
    [Range(.05f, .3f)] public float footLift = .14f;
    [Range(.15f, .6f)] public float stepDistance = .32f;
    [Range(.03f, .2f)] public float coreResponse = .08f;
    [Range(.05f, .3f)] public float limbResponse = .16f;
    public float swordActionSeconds = 3f;
    [Header("Editor diagnostics")]
    public bool drawGaitDiagnostics;
    public bool IsInitialized { get; private set; }
    public int FootPlants { get; private set; }
    public int ProbeCount { get; private set; }
    public float CurrentPelvisOffset => pelvisOffset.y;
    public Vector3 DisplayUp => transform.up;
    public float GravityVisualError { get; private set; }
    public float PlantedFootError { get; private set; }
    public int PlantedFeet => (leftFoot.planted ? 1 : 0) + (rightFoot.planted ? 1 : 0);
    public bool UsesAnimationClips => animator != null && animator.enabled;
    public Transform BrakingSupportFoot => stopLead == null ? null : stopLead == leftFoot ? leftLeg.end : rightLeg.end;
    public bool LeftFootSupported => leftFoot.planted;
    public bool RightFootSupported => rightFoot.planted;
    public int LeftFootPlants => leftFoot.plants;
    public int RightFootPlants => rightFoot.plants;
    public GaitStage LeftGaitStage => leftFoot.stage;
    public GaitStage RightGaitStage => rightFoot.stage;
    public float GaitPhase => distancePhase / (Mathf.PI * 2);
    public Vector3 LeftFootTarget => leftFoot.position;
    public Vector3 RightFootTarget => rightFoot.position;
    public float SwingReachCorrection { get; private set; }
    public float BodyLeanAngle => leanAngle;
    public float LeftSupportProgress => Mathf.Clamp01(leftFoot.contactElapsed / Mathf.Max(.025f, leftFoot.contactSeconds));
    public float RightSupportProgress => Mathf.Clamp01(rightFoot.contactElapsed / Mathf.Max(.025f, rightFoot.contactSeconds));
    public Vector3 LeftSupportPoint => leftFoot.groundPoint;
    public Vector3 RightSupportPoint => rightFoot.groundPoint;

    private TanjiroSword sword;
    private Animator animator;
    private ThirdPersonController motor;
    private StarterAssetsInputs input;
    private Transform actor;
    private AudioSource audioSource;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly Foot leftFoot = new(), rightFoot = new();
    private Vector3 previousPosition, lastUp, velocity, pelvisOffset, pelvisVelocity, bodyOffset, travelDirection;
    private Vector3 pelvisWorldPoint, carriedDelta;
    private float pelvisHeightVelocity;
    private bool pelvisHeightReady;
    private Vector3 rightWrist, leftWrist, rightWristVelocity, leftWristVelocity;
    private Vector3 modelLocalPosition, sheathLocalPosition;
    private Quaternion modelLocalRotation, displayRotation, sheathLocalRotation;
    private Quaternion rightWristRotation, leftWristRotation;
    private Transform motionSupport;
    private Vector3 supportPlayerPoint;
    private float distancePhase, speed, carry, actionTime, previousAction, airborneSeconds, landingCompression, locomotion, stanceWeight, stairWeight, running, turnRate, poseTime, duty = .62f;
    private float locomotionVelocity, runningVelocity, cadence, cycleDistance, cycleDistanceVelocity;
    private float previousRunHeight, unsupportedTime;
    private float airPoseWeight, airLanding, airReach, jumpBrace;
    private bool airLeadLeft, airLaunched, airNormalJump;
    private Foot stairSwing, stairLoadFoot;
    private float stairDirection, stairMemory;
    private float stairLoadProgress, treadWeight, treadDrive, treadDriveStart, treadGrade;
    private float lastSpeed, acceleration, leanAngle, leanVelocity, sprintAmount, stopHold, stopSpacing;
    private Foot stopLead, stopTrail;
    private Vector3 stopDirection;
    private bool stopTrailPlaced, hadMoveIntent;
    private bool gaitNeedsEntry = true;
    private Quaternion torsoFrame = Quaternion.identity;
    private Quaternion chestToModel;
    private float upperBodyDrive, upperBodyVelocity, armDrive;
    private float phaseOffset, phaseOffsetVelocity, phaseOffsetTarget;
    private Vector3 coatDown;
    private float strideYaw, strideYawVelocity;
    private float leftShoulderFlex, rightShoulderFlex, leftElbowFlex = 12, rightElbowFlex = 12;
    private float leftShoulderVelocity, rightShoulderVelocity, leftElbowVelocity, rightElbowVelocity;
    private Vector3 previousForward;
    private bool nextLeft, groundedLast, action, sheathing;

    private void Awake() => Initialize();
    public void Initialize()
    {
        if (IsInitialized) return;
        animator = GetComponent<Animator>(); sword = GetComponent<TanjiroSword>();
        motor = GetComponentInParent<ThirdPersonController>();
        if (motor == null || bones.Length == 0 || pelvis == null || leftLeg.end == null || rightArm.end == null)
        { Debug.LogError("Calibrate the procedural scene with TanjiroProceduralSetup first.", this); enabled = false; return; }
        actor = motor.transform;
        input = actor.GetComponent<StarterAssetsInputs>();
        modelLocalPosition = transform.localPosition; modelLocalRotation = transform.localRotation;
        displayRotation = transform.rotation; lastUp = actor.up; previousPosition = actor.position;
        previousForward = actor.forward;
        coatDown = -actor.up;
        sheathLocalPosition = sword.sheathSocket.localPosition; sheathLocalRotation = sword.sheathSocket.localRotation;
        animator.enabled = false; // No Animator evaluation can overwrite the procedural bones.
        RestorePose(); chestToModel = Quaternion.Inverse(chest.rotation) * transform.rotation; motor.Initialize();
        motor.Motor.useTreadLanding = true;
        motor.ProceduralGroundBraking = true;
        motor.GroundedJumpWindup = .22f;
        sword.InitializeProcedural(BeginSwordAction);
        audioSource = actor.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = actor.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false; audioSource.spatialBlend = 1;
        rightWrist = rightArm.restEnd; leftWrist = leftArm.restEnd;
        rightWristRotation = rightArm.restRotation; leftWristRotation = leftArm.restRotation;
        ResetFeet(); IsInitialized = true;
    }
    private void RestorePose()
    {
        foreach (var pose in bones)
            if (pose.bone != null) pose.bone.SetLocalPositionAndRotation(pose.position, pose.rotation);
    }
    private void ResetFeet()
    {
        leftFoot.position = leftLeg.end.position; rightFoot.position = rightLeg.end.position;
        leftFoot.soleHeight = leftLeg.soleHeight; rightFoot.soleHeight = rightLeg.soleHeight;
        leftFoot.progress = rightFoot.progress = 1; leftFoot.planted = rightFoot.planted = false;
        leftFoot.support = rightFoot.support = null;
        leftFoot.gaitContact = rightFoot.gaitContact = false;
        leftFoot.airLocal = leftLeg.restEnd; rightFoot.airLocal = rightLeg.restEnd;
        leftFoot.velocity = rightFoot.velocity = Vector3.zero;
        leftFoot.landingReady = rightFoot.landingReady = false;
        leftFoot.swingActive = rightFoot.swingActive = false;
        leftFoot.cycleReady = rightFoot.cycleReady = false;
        stopLead = stopTrail = null; stopHold = 0;
        gaitNeedsEntry = true;
        pelvisHeightReady = false;
        previousRunHeight = unsupportedTime = 0;
        phaseOffset = phaseOffsetVelocity = phaseOffsetTarget = 0;
        airPoseWeight = airLanding = airReach = jumpBrace = 0;
        airLaunched = airNormalJump = false;
        stairSwing = stairLoadFoot = null;
        stairLoadProgress = treadWeight = treadDrive = treadDriveStart = treadGrade = 0;
        stairDirection = stairMemory = stairWeight = 0;
        motor.StairSpeedLimit = 0;
    }
    private void LateUpdate() { if (Application.isPlaying) Simulate(Time.deltaTime); }
    public void Simulate(float dt)
    {
        Initialize(); if (!IsInitialized || dt <= 0) return;
        float frameTime = dt;
        dt = Mathf.Min(dt, .1f); ProbeCount = 0;
        poseTime += dt;
        Vector3 up = actor.up;
        bool changedGravity = Vector3.Dot(lastUp, up) < .99f;
        Vector3 displacement = actor.position - previousPosition;
        bool teleported = displacement.sqrMagnitude > 4f;
        Transform support = motor.Motor != null ? motor.Motor.GroundHit.collider?.transform : null;
        carriedDelta = Vector3.zero;
        if (support != null && support == motionSupport)
        {
            carriedDelta = support.TransformPoint(supportPlayerPoint) - previousPosition;
            displacement -= carriedDelta;
        }
        if (changedGravity || teleported) { displacement = Vector3.zero; ResetFeet(); }
        if (teleported)
        {
            // Wrist gestures are already stored in the body frame. A world
            // relocation must not pull an armed hand back to the neutral pose.
            rightWristVelocity = leftWristVelocity = velocity = Vector3.zero;
            displayRotation = actor.rotation * modelLocalRotation;
        }
        motionSupport = support;
        if (support != null) supportPlayerPoint = support.InverseTransformPoint(actor.position);
        previousPosition = actor.position; lastUp = up;
        velocity = Vector3.Lerp(velocity, displacement / frameTime, 1 - Mathf.Exp(-12f * dt));
        Vector3 planarVelocity = Vector3.ProjectOnPlane(displacement / frameTime, up);
        speed = planarVelocity.magnitude;
        float change = changedGravity || teleported ? 0 : (speed - lastSpeed) / frameTime;
        acceleration = Mathf.Lerp(acceleration, Mathf.Clamp(change, -20, 20), 1 - Mathf.Exp(-dt * 12));
        lastSpeed = speed;
        travelDirection = speed > .05f ? planarVelocity / speed : transform.forward;

        turnRate = Mathf.Lerp(turnRate, Vector3.SignedAngle(previousForward, actor.forward, up) / frameTime, 1 - Mathf.Exp(-dt * 10));
        previousForward = actor.forward;
        Vector3 displayUp = Vector3.Slerp(displayRotation * Vector3.up, up, 1 - Mathf.Exp(-dt / coreResponse));
        Vector3 facing = Vector3.ProjectOnPlane(actor.forward, displayUp).normalized;
        if (facing.sqrMagnitude < .1f) facing = Vector3.ProjectOnPlane(actor.right, displayUp).normalized;
        displayRotation = Quaternion.LookRotation(facing, displayUp) * modelLocalRotation;
        transform.SetLocalPositionAndRotation(modelLocalPosition, Quaternion.Inverse(transform.parent.rotation) * displayRotation);
        RestorePose();
        GravityVisualError = Vector3.Angle(transform.up, up);
        bool grounded = motor.Grounded && GravityVisualError < 12;
        grounded = UpdateStairSupport(grounded, up, dt);
        bool landed = grounded && !groundedLast && airborneSeconds > .08f;
        if (!grounded) airborneSeconds += dt;
        else
        {
            if (landed)
                landingCompression = Smooth(.75f, 5.5f, -Vector3.Dot(velocity, up)) * Mathf.Lerp(.235f, .115f, Smooth(.3f, 3.5f, speed));
            airborneSeconds = 0;
        }
        if (!landed) landingCompression = Mathf.MoveTowards(landingCompression, 0, dt * .70f);
        float anticipation = JumpLoad;
        jumpBrace = Mathf.Max(anticipation, Smooth(.015f, .19f, landingCompression));
        airPoseWeight = Mathf.MoveTowards(airPoseWeight, grounded ? 0 : 1, dt * (grounded ? 10 : 8));
        float sprint = Mathf.InverseLerp(motor.MoveSpeed, motor.SprintSpeed, speed);
        sprintAmount = sprint;
        float moving = Mathf.Clamp01(speed / 1.1f);
        if (stopLead != null && input.move.sqrMagnitude < .0025f) moving *= Mathf.Clamp01(speed / 2.6f);
        locomotion = Mathf.SmoothDamp(locomotion, moving, ref locomotionVelocity, .13f, 10, dt);
        running = Mathf.SmoothDamp(running, Smooth(1.8f, 3.2f, speed), ref runningVelocity, .18f, 10, dt);
        duty = Mathf.Lerp(Mathf.Lerp(.64f, .56f, Mathf.Clamp01(speed / 1.5f)), Mathf.Lerp(.29f, .22f, sprint), running);
        stanceWeight = Mathf.MoveTowards(stanceWeight, sword.IsDrawn && !action ? 1 - locomotion : 0, dt * 4);
        float legLength = LegLength(leftLeg);
        float walkingCadence = Mathf.Lerp(.7f, 1.55f, Mathf.Clamp01(speed / 1.5f));
        // Leave time for a full recovery between contacts. Stance distance
        // still comes from the leg's reachable sweep in RunningDuty.
        float runningDistance = legLength * (1.62f + .93f * speed / Mathf.Sqrt(9.81f * legLength));
        float targetDistance = Mathf.Lerp(Mathf.Max(legLength * .5f, speed / walkingCadence), runningDistance, running);
        targetDistance = Mathf.Lerp(targetDistance, Mathf.Max(legLength, speed * .39f), stairWeight);
        if (cycleDistance <= 0) cycleDistance = targetDistance;
        cycleDistance = Mathf.SmoothDamp(cycleDistance, targetDistance, ref cycleDistanceVelocity, .12f, 20, dt);
        cadence = Mathf.Max(.5f, speed / Mathf.Max(.2f, cycleDistance));
        // Actual travel advances the stride. A blocked capsule cannot keep
        // pedalling on a time oscillator; braking shortens travel continuously.
        float advance = speed * frameTime / Mathf.Max(.2f, cycleDistance) * Mathf.PI * 2;
        distancePhase = Mathf.Repeat(distancePhase + advance, Mathf.PI * 2);
        float accelerationLean = Mathf.Atan2(acceleration, Mathf.Max(9.81f, Mathf.Abs(motor.Gravity))) * Mathf.Rad2Deg;
        float leanTarget = locomotion * Mathf.Lerp(3, Mathf.Lerp(9, 12, sprint), running) + Mathf.Clamp(accelerationLean * .35f, -4, 4) * locomotion;
        float stairLean = Mathf.Lerp(7, Mathf.Clamp(5 + 13 * treadGrade, 1.5f, 14), treadWeight);
        leanTarget = Mathf.Lerp(leanTarget, stairLean * locomotion, stairWeight);
        leanTarget += 32 * jumpBrace;
        leanAngle = Mathf.SmoothDamp(leanAngle, leanTarget, ref leanVelocity, Mathf.Lerp(.12f, .045f, jumpBrace), Mathf.Lerp(160, 240, jumpBrace), dt);
        float counter = strideYaw;
        float bank = Mathf.Clamp(turnRate / 70, -7, 7) * locomotion;
        pelvis.rotation = Quaternion.AngleAxis(leanAngle * .45f, transform.right) * Quaternion.AngleAxis(counter, transform.up) * pelvis.rotation;
        spine.rotation = Quaternion.AngleAxis(leanAngle * .35f, transform.right) * spine.rotation;
        chest.rotation = Quaternion.AngleAxis(leanAngle * .20f, transform.right) * chest.rotation;
        spine.rotation = Quaternion.AngleAxis(-bank, transform.forward) * spine.rotation;
        chest.rotation = Quaternion.AngleAxis(Mathf.Sin(poseTime * 1.4f) * .35f * (1 - locomotion), transform.right) * chest.rotation;
        head.rotation = Quaternion.AngleAxis(-leanAngle * .55f, transform.right) * head.rotation;
        // Core turns first; upper body and extremities retain a little inertia.
        Quaternion lag = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(up, transform.up), 30);
        chest.rotation = Quaternion.Slerp(Quaternion.identity, lag, .22f) * chest.rotation;
        head.rotation = Quaternion.Slerp(Quaternion.identity, lag, .25f) * head.rotation;

        float beforeEntry = distancePhase;
        bool moveIntent = input.move.sqrMagnitude > .0025f;
        PrepareGait(grounded, moveIntent);
        // Keep the upper body continuous if a restart rebases the foot schedule.
        if (distancePhase != beforeEntry)
        {
            phaseOffset = locomotion < .12f ? 0 : Mathf.DeltaAngle(distancePhase * Mathf.Rad2Deg,
                (beforeEntry + phaseOffset) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            phaseOffsetTarget = 0;
        }
        bool leftContact = leftFoot.planted, rightContact = rightFoot.planted;
        bool leftStance = leftFoot.gaitContact, rightStance = rightFoot.gaitContact;
        UpdateFeet(grounded, up, dt, sprint, moveIntent);
        if (grounded && stairWeight < .35f && running > .6f && (moveIntent || stopLead == null))
        {
            ObserveStrideEvent(leftFoot, leftLeg, leftContact, leftStance, advance);
            ObserveStrideEvent(rightFoot, rightLeg, rightContact, rightStance, advance);
        }
        else if (!grounded || stairWeight >= .35f || speed < 2.6f) phaseOffsetTarget = 0;
        phaseOffset = Mathf.SmoothDamp(phaseOffset, phaseOffsetTarget, ref phaseOffsetVelocity,
            .28f / Mathf.Max(1, cadence), Mathf.PI * Mathf.Max(1, cadence), dt);
        // The thigh reaches forward in recovery. Lead the contact phase so
        // the opposite shoulder carries through landing instead of starting there.
        float runDrive = -Mathf.Cos(distancePhase + phaseOffset + .21f * 2 * Mathf.PI);
        float runWeight = grounded ? running * Smooth(1.8f, 3.2f, speed) * (1 - stairWeight) : 0;
        CoordinateUpperBody(counter, runDrive, runWeight, dt);
        if (grounded && !groundedLast && motor.LandingAudioClip != null)
            audioSource.PlayOneShot(motor.LandingAudioClip, motor.FootstepAudioVolume);
        groundedLast = grounded;
        UpdateSword(dt, moving);
        UpdateArms(dt, moving, grounded);
        ApplyFingers();
        foreach (var twist in twists) twist.Pose();
        UpdateCoat(up, dt);
    }
    private void UpdateCoat(Vector3 up, float dt)
    {
        coatDown = Vector3.Slerp(coatDown, -up, 1 - Mathf.Exp(-dt / .14f)).normalized;
        foreach (var panel in coatPanels)
        {
            if (panel.root == null || panel.hem == null) continue;
            // The hem hangs from its own support. Gravity and body inertia
            // drive it; leg volume supplies a clearance constraint only when
            // the baggy trousers would intersect the hanging panel.
            float opening = 0;
            for (int side = 0; side < 2; side++)
            {
                if (!panel.back && panel.left != (side == 0)) continue;
                Limb leg = side == 0 ? leftLeg : rightLeg;
                for (int sample = 0; sample < 3; sample++)
                {
                    float t = .2f + sample * .25f;
                    Vector3 point = Vector3.Lerp(leg.upper.position, leg.lower.position, t) - panel.root.position;
                    float down = -Vector3.Dot(point, up);
                    if (down < .06f || down > .44f) continue;
                    float radius = Mathf.Lerp(.12f, .10f, t);
                    float overlap = panel.back ? -Vector3.Dot(point, transform.forward) + radius - .065f :
                        Vector3.Dot(point, transform.right) * (panel.left ? -1 : 1) + radius - .035f;
                    if (!panel.back && Mathf.Abs(Vector3.Dot(point, transform.forward)) > .23f) continue;
                    opening = Mathf.Max(opening, Mathf.Atan2(Mathf.Max(0, overlap), down) * Mathf.Rad2Deg);
                }
            }
            opening = Mathf.Min(opening, panel.back ? 38 : 24);
            float clearance = (panel.back ? 1 : -.5f) * Mathf.Min(5, speed * .7f);
            float inertial = Mathf.Clamp(acceleration * .30f, -4, 5) * locomotion;
            float target = Mathf.Clamp(clearance + inertial, panel.back ? -3 : -25, panel.back ? 27 : 5);
            if (panel.back) target = Mathf.Max(target, opening);
            panel.angle = Mathf.SmoothDamp(panel.angle, target, ref panel.angularVelocity, .12f, 140, dt);
            if (panel.back) panel.angle = Mathf.Max(panel.angle, opening);
            Quaternion hanging = Quaternion.FromToRotation(panel.root.TransformDirection(panel.bindDown), coatDown);
            panel.root.rotation = Quaternion.RotateTowards(Quaternion.identity, hanging, 45) * panel.root.rotation;
            panel.root.rotation = Quaternion.AngleAxis(panel.angle, transform.right) * panel.root.rotation;
            if (!panel.back)
            {
                panel.splay = Mathf.SmoothDamp(panel.splay, Mathf.Max(5, opening), ref panel.splayVelocity, .12f, 140, dt);
                panel.splay = Mathf.Max(panel.splay, opening);
                panel.root.rotation = Quaternion.AngleAxis(panel.splay * (panel.left ? -1 : 1), transform.forward) * panel.root.rotation;
            }
            // The lower segment drapes back towards gravity after the upper
            // fabric has opened, rather than extending as a rigid paddle.
            panel.hem.rotation = Quaternion.AngleAxis(-panel.angle * .55f, transform.right) * panel.hem.rotation;
        }
    }
    private bool Probe(Vector3 wanted, Vector3 up, out RaycastHit nearest, bool tread = false, float extraDepth = 0, bool stairSearch = false)
    {
        if (ProbeCount >= (stairSearch ? 8 : 3)) { nearest = default; return false; }
        ProbeCount++;
        int layers = motor.GroundLayers.value == 0 ? ~0 : motor.GroundLayers.value;
        int count = tread
            ? Physics.RaycastNonAlloc(wanted + up * .6f, -up, hits, 1.35f + extraDepth, layers, QueryTriggerInteraction.Ignore)
            : Physics.SphereCastNonAlloc(wanted + up * .6f, .045f, -up, hits, 1.35f + extraDepth, layers, QueryTriggerInteraction.Ignore);
        nearest = default; float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(actor) || Vector3.Dot(hit.normal, up) < .65f || hit.distance >= distance) continue;
            nearest = hit; distance = hit.distance;
        }
        return nearest.collider != null;
    }
    private void PrepareGait(bool grounded, bool moveIntent)
    {
        if (moveIntent && !hadMoveIntent && stairSwing == null)
        {
            // Resume from the contacts we actually have, including an
            // interrupted balancing step. The schedule selects support below;
            // no foot or joint is reset to a neutral pose.
            for (int i = 0; i < 2; i++)
            {
                Foot foot = i == 0 ? leftFoot : rightFoot;
                foot.gaitContact = foot.planted;
                foot.swingActive = foot.cycleReady = false;
                foot.landingReady = false;
                foot.progress = 1;
            }
            stopLead = stopTrail = null; stopHold = 0;
            gaitNeedsEntry = true;
        }
        hadMoveIntent = moveIntent;
        if (grounded && moveIntent && gaitNeedsEntry && stairSwing == null)
        {
            // Select the real supporting/leading foot without moving either
            // target. Only the virtual stride schedule is rebased on a start.
            bool leftSupport = leftFoot.planted != rightFoot.planted ? leftFoot.planted :
                Vector3.Dot(leftFoot.position - rightFoot.position, travelDirection) >= 0;
            distancePhase = leftSupport ? 0 : Mathf.PI;
            leftFoot.cycleReady = rightFoot.cycleReady = false;
            leftFoot.swingActive = rightFoot.swingActive = false;
            leftFoot.landingReady = rightFoot.landingReady = false;
            gaitNeedsEntry = false;
        }
    }
    private void UpdateFeet(bool grounded, Vector3 up, float dt, float sprint, bool moveIntent)
    {
        Vector3 oldLeft = leftFoot.position, oldRight = rightFoot.position;
        bool treadMotion = false;
        if (!grounded)
        {
            stairSwing = stairLoadFoot = null;
            UpdateAirFeet(up, dt);
        }
        else if (UpdateTreadStep(up, dt, moveIntent))
        {
            treadMotion = true;
            gaitNeedsEntry = true;
        }
        else if (!moveIntent && speed < 2.6f && stopLead == null && (speed > .08f || locomotion > .1f))
        {
            BeginStop(up); SettleStop(up, dt);
        }
        else if (stopLead != null && !moveIntent)
        {
            SettleStop(up, dt);
        }
        else if (locomotion > .08f && (moveIntent || speed > .08f))
        {
            // At the real 5/8.335 m/s game speeds, a running foot has a brief
            // contact followed by recovery/flight. It cannot remain pinned for
            // half a second while the capsule travels several leg lengths.
            GaitFoot(leftFoot, leftLeg, true, Mathf.Repeat(distancePhase / (2 * Mathf.PI), 1), up, dt);
            GaitFoot(rightFoot, rightLeg, false, Mathf.Repeat(distancePhase / (2 * Mathf.PI) + .5f, 1), up, dt);
        }
        else
        {
            MaintainIdle(up, dt);
        }
        treadWeight = Mathf.MoveTowards(treadWeight, treadMotion ? 1 : 0, dt * 7);
        if (stairWeight > .35f)
        {
            ReleaseOutOfReachStairContact(leftFoot, leftLeg, up);
            ReleaseOutOfReachStairContact(rightFoot, rightLeg, up);
        }
        SolvePelvis(grounded, up, dt);
        // Swing clearance cannot pull the torso down. Adapt the unloaded leg
        // to its reachable envelope; planted contacts remain fixed in the world.
        leftFoot.requested = leftFoot.position; rightFoot.requested = rightFoot.position;
        SwingReachCorrection = Mathf.Max(FitSwing(leftFoot, leftLeg), FitSwing(rightFoot, rightLeg));
        Quaternion leftRotation = FootRotation(leftFoot, leftLeg, grounded), rightRotation = FootRotation(rightFoot, rightLeg, grounded);
        float leftKneeWidth = grounded ? KneeWidth(leftFoot) : .10f;
        float rightKneeWidth = grounded ? KneeWidth(rightFoot) : .10f;
        leftLeg.Solve(leftFoot.position, leftRotation, leftLeg.upper.position + transform.forward * .5f - transform.right * leftKneeWidth, dt, 1, 1, MinimumLegBend);
        rightLeg.Solve(rightFoot.position, rightRotation, rightLeg.upper.position + transform.forward * .5f + transform.right * rightKneeWidth, dt, 1, 1, MinimumLegBend);
        if (leftToe != null) leftToe.localRotation = leftToeBind * Quaternion.AngleAxis(ToeFlex(leftFoot), leftToeAxis);
        if (rightToe != null) rightToe.localRotation = rightToeBind * Quaternion.AngleAxis(ToeFlex(rightFoot), rightToeAxis);
        PlantedFootError = Mathf.Max(leftFoot.planted ? Vector3.Distance(leftLeg.end.position, leftFoot.position) : 0,
            rightFoot.planted ? Vector3.Distance(rightLeg.end.position, rightFoot.position) : 0);
        leftFoot.velocity = (leftFoot.position - oldLeft) / dt; rightFoot.velocity = (rightFoot.position - oldRight) / dt;
        float separation = Vector3.Dot((rightLeg.lower.position - rightLeg.upper.position) - (leftLeg.lower.position - leftLeg.upper.position), transform.forward);
        float yawTarget = -Mathf.Atan2(separation, Vector3.Distance(leftLeg.upper.position, rightLeg.upper.position)) * Mathf.Rad2Deg * Mathf.Lerp(.10f, .13f, sprintAmount);
        yawTarget = Mathf.Lerp(yawTarget, -3.5f * treadDrive, treadWeight) * locomotion;
        strideYaw = Mathf.SmoothDamp(strideYaw, yawTarget, ref strideYawVelocity, .08f, 100, dt);
    }

    private bool UpdateStairSupport(bool grounded, Vector3 up, float dt)
    {
        bool stairs = false;
        stairMemory = Mathf.Max(0, stairMemory - dt);
        if (grounded && motor.Motor.GroundHit.collider != null)
        {
            RaycastHit ground = motor.Motor.GroundHit;
            stairs = Vector3.Dot(ground.normal, up) < .98f;
            if (speed > .1f && Probe(actor.position + travelDirection * .52f, up, out var ahead, true))
            {
                float rise = Vector3.Dot(ahead.point - ground.point, up);
                var route = ahead.collider.GetComponentInParent<CastleStairRoute>();
                if (route != null && route.hasAuthoredTreads && Probe(actor.position, up, out var under, true))
                    rise = Vector3.Dot(ahead.point - under.point, up);
                if (Mathf.Abs(rise) > .075f && Mathf.Abs(rise) < .42f)
                {
                    stairs = true;
                    if (route != null && route.hasAuthoredTreads)
                    {
                        stairDirection = Mathf.Sign(rise);
                        stairMemory = .32f;
                    }
                }
            }
        }
        else if (stairMemory > 0 && GravityVisualError < 12 && airborneSeconds < .16f &&
            motor.VerticalSpeed <= 0 && motor.VerticalSpeed > -5.5f && Probe(actor.position, up, out var tread, true))
        {
            // A short drop to the next tread keeps the stepping pose. The
            // capsule still falls and resolves contact through the motor.
            float gap = Vector3.Dot(actor.position - tread.point, up);
            grounded = stairs = gap >= -.05f && gap < .40f;
        }
        if (!grounded && motor.VerticalSpeed > .1f) stairMemory = 0;
        motor.StairSpeedLimit = grounded && stairMemory > 0 ? 1.65f : 0;
        stairs |= grounded && (stairMemory > 0 || stairSwing != null);
        stairWeight = Mathf.MoveTowards(stairWeight, stairs ? 1 : 0, dt * (stairs ? 7 : 4));
        return grounded;
    }

    private void ReleaseOutOfReachStairContact(Foot foot, Limb leg, Vector3 up)
    {
        if (!foot.planted || Vector3.ProjectOnPlane(foot.position - PredictedHip(leg, up), up).magnitude < LegReach(leg) - .06f) return;
        foot.planted = false;
        foot.support = null;
    }

    private bool UpdateTreadStep(Vector3 up, float dt, bool moveIntent)
    {
        stairLoadProgress = Mathf.Min(1, stairLoadProgress + dt / .09f);
        if (stairSwing != null)
        {
            Foot support = stairSwing == leftFoot ? rightFoot : leftFoot;
            Limb swingLeg = stairSwing == leftFoot ? leftLeg : rightLeg;
            support.pitch = Mathf.MoveTowards(support.pitch, 0, dt * 160);
            if (support.pitch == 0) support.gaitContact = false;
            TrackFoot(support);
            TrackFoot(stairSwing);
            AdvanceTreadStep(up, dt);
            if (stairSwing.planted)
            {
                stairLoadFoot = stairSwing;
                stairLoadProgress = 0;
            }
            if (!stairSwing.planted && stairSwing.progress >= 1 &&
                (!moveIntent || Vector3.Dot(stairSwing.target - actor.position, travelDirection) < -.30f) &&
                Vector3.Distance(PredictedHip(swingLeg, up), stairSwing.target) > LegReach(swingLeg) + .06f)
                stairSwing.support = null;
            if (stairSwing.planted || stairSwing.support == null) stairSwing = null;
            return true;
        }
        if (!moveIntent || stairMemory <= 0 || speed < .05f) return false;
        if (stairLoadProgress < .52f && leftFoot.planted && rightFoot.planted)
        {
            TrackFoot(leftFoot); TrackFoot(rightFoot);
            return true;
        }

        // Keep the leading foot loaded and move the other to the first tread
        // beyond it. Once chosen, the landing stays in the tread's own frame.
        bool leftLeads = leftFoot.planted && (!rightFoot.planted ||
            Vector3.Dot(leftFoot.groundPoint - rightFoot.groundPoint, travelDirection) > 0);
        Foot lead = leftLeads ? leftFoot : rightFoot;
        Foot swing = leftLeads ? rightFoot : leftFoot;
        if (!lead.planted) return false;
        TrackFoot(lead);
        if (!lead.planted) return false;
        Vector3 lateral = Vector3.Cross(up, travelDirection);
        Limb leg = leftLeads ? rightLeg : leftLeg;
        Vector3 lane = actor.position + lateral * leg.restEnd.x;
        float leadDistance = Vector3.Dot(lead.groundPoint - lane, travelDirection);
        if (leadDistance > .30f) return true;
        for (int i = 0; i < 5; i++)
        {
            Vector3 sample = lane + travelDirection * (leadDistance + .14f + i * .12f);
            if (!Probe(sample, up, out var tread, true, stairSearch: true)) continue;
            float rise = Vector3.Dot(tread.point - lead.groundPoint, up) * stairDirection;
            if (rise < .065f) continue;
            if (rise > .40f || tread.collider.GetComponentInParent<CastleStairRoute>() == null) break;
            // Step into the tread, away from the nosing used to detect it.
            if (Probe(sample + travelDirection * .075f, up, out var centre, true, stairSearch: true) &&
                Mathf.Abs(Vector3.Dot(centre.point - tread.point, up)) < .035f) tread = centre;
            float depth = Vector3.Dot(tread.point - lead.groundPoint, travelDirection);
            float height = Vector3.Dot(tread.point - lead.groundPoint, up);
            treadGrade = height / Mathf.Max(.15f, depth);
            float travel = depth * (height < 0 ? .56f : .64f) + .30f * Mathf.Max(0, leadDistance);
            float pace = Mathf.Max(.75f, Vector3.Dot(velocity, travelDirection));
            float seconds = travel / pace + Mathf.Min(.022f, Mathf.Abs(height) * .12f);
            StartStep(swing, tread, Mathf.Clamp(seconds, .145f, .31f), Mathf.Clamp(.027f + Mathf.Abs(height) * .14f, .035f, .065f));
            swing.swingStartPitch = swing.pitch;
            treadDriveStart = treadWeight > 0 ? treadDrive : upperBodyDrive;
            stairSwing = swing;
            AdvanceTreadStep(up, dt);
            return true;
        }
        return false;
    }

    private void AdvanceTreadStep(Vector3 up, float dt)
    {
        AdvanceFoot(stairSwing, up, dt);
        float passing = Smoother(Mathf.InverseLerp(.18f, .80f, stairSwing.progress));
        treadDrive = Mathf.Lerp(treadDriveStart, stairSwing == leftFoot ? -.65f : .65f, passing);
    }

    private float JumpLoad => Smooth(0, .62f, motor.JumpPreparation) * (1 - .8f * Smooth(.62f, 1, motor.JumpPreparation));

    private void UpdateAirFeet(Vector3 up, float dt)
    {
        gaitNeedsEntry = true;
        stopLead = stopTrail = null; stopHold = 0;
        if (groundedLast)
        {
            leftFoot.airLocal = transform.InverseTransformPoint(leftFoot.position);
            rightFoot.airLocal = transform.InverseTransformPoint(rightFoot.position);
            airLeadLeft = leftFoot.airLocal.z >= rightFoot.airLocal.z;
            airLanding = 0;
            airLaunched = motor.VerticalSpeed > .4f;
            airNormalJump = airLaunched && !motor.IsUltraJumping;
        }
        float vertical = motor.VerticalSpeed;
        // Jump up into the reach, then lower into the same fall pose used
        // when stepping off a ledge. Long jumps relax the overhead reach.
        airReach = airLaunched ? Smooth(.01f, .18f, airborneSeconds) *
            (1 - Smooth(-.15f, 3.4f, -vertical)) * Mathf.Lerp(1, .55f, Smooth(.34f, .80f, airborneSeconds)) : 0;
        float landing = 0;
        if (vertical < -.2f && GravityVisualError < 12)
        {
            Vector3 ahead = actor.position + travelDirection * (speed * .065f);
            if (Probe(ahead, up, out var floor, extraDepth: -vertical * .22f))
            {
                float height = Vector3.Dot(actor.position - floor.point, up);
                if (height >= 0) landing = 1 - Smooth(.12f, Mathf.Max(.55f, -vertical * .22f), height);
            }
        }
        airLanding = Mathf.MoveTowards(airLanding, landing, dt * 10);
        float falling = Smooth(.6f, 6, -vertical);
        float tuck = Smooth(.03f, .24f, airborneSeconds) * (1 - airLanding) * Mathf.Lerp(1, .86f, falling);
        float travel = Smooth(.15f, 3f, speed);
        void PoseFoot(Foot foot, bool left)
        {
            bool lead = left == airLeadLeft;
            foot.planted = foot.gaitContact = foot.swingActive = false;
            foot.progress = 1; foot.support = null; foot.stage = GaitStage.Airborne;
            Vector3 spread = FootRest(left);
            spread.z += (lead ? .11f : -.09f) * travel;
            float kneeRecovery = lead ? .27f : .22f;
            if (airNormalJump) kneeRecovery = Mathf.Lerp(.24f, kneeRecovery, travel);
            Vector3 folded = spread + new Vector3((left ? -1 : 1) * .035f, kneeRecovery,
                Mathf.Lerp((lead ? .055f : -.06f) * travel, -.15f, airReach));
            Vector3 target = Vector3.Lerp(spread, folded, tuck);
            foot.airLocal = Vector3.Lerp(foot.airLocal, target, 1 - Mathf.Exp(-dt / .075f));
            foot.position = transform.TransformPoint(foot.airLocal);
            float ankleRecovery = lead ? 4 : 18;
            if (airNormalJump) ankleRecovery = Mathf.Lerp(10, ankleRecovery, travel);
            float pitch = Mathf.Lerp(ankleRecovery, -7, airLanding);
            foot.pitch = Mathf.Lerp(foot.pitch, pitch, 1 - Mathf.Exp(-dt / .08f));
        }
        PoseFoot(leftFoot, true); PoseFoot(rightFoot, false);
    }
    private float SupportLoad(Foot foot)
    {
        if (!foot.planted) return 0;
        if (stairSwing != null || stopLead != null || locomotion < .1f || !foot.gaitContact) return 1;
        float contact = Mathf.Clamp01(foot.contactElapsed / Mathf.Max(.025f, foot.contactSeconds));
        return .2f + .8f * Smooth(0, .22f, contact) * (1 - Smooth(.65f, 1, contact));
    }
    private Vector3 PredictedHip(Limb leg, Vector3 up)
    {
        Vector3 horizontal = transform.TransformVector(new Vector3(pelvisOffset.x, 0, pelvisOffset.z));
        float height = pelvisHeightReady ? Vector3.Dot(pelvisWorldPoint + carriedDelta - pelvis.position - horizontal, up) : pelvisOffset.y;
        return leg.upper.position + horizontal + up * height;
    }
    private void SolvePelvis(bool grounded, Vector3 up, float dt)
    {
        Vector3 neutral = pelvis.position;
        leftFoot.load = SupportLoad(leftFoot); rightFoot.load = SupportLoad(rightFoot);
        if (grounded && treadWeight > 0 && stairLoadFoot != null && leftFoot.planted && rightFoot.planted)
        {
            float incoming = Smoother(stairLoadProgress);
            float leftShare = stairLoadFoot == leftFoot ? incoming : 1 - incoming;
            leftFoot.load = Mathf.Lerp(leftFoot.load, leftShare, treadWeight);
            rightFoot.load = Mathf.Lerp(rightFoot.load, 1 - leftShare, treadWeight);
        }
        float load = leftFoot.load + rightFoot.load;
        float forward = 0;
        if (grounded && load > 0)
        {
            Vector3 support = (leftFoot.position * leftFoot.load + rightFoot.position * rightFoot.load) / load;
            forward = Mathf.Clamp(Vector3.Dot(support - neutral, transform.forward) * Mathf.Lerp(.12f, .20f, stairWeight), -.05f, .08f);
        }
        if (grounded && stopLead != null && stopLead.planted)
            forward = Mathf.Clamp(Vector3.Dot(stopLead.position - actor.position, transform.forward) * .35f, -.02f, .075f);
        float alignmentHeight = Mathf.Max(.3f, Vector3.Dot(neutral - actor.position, up) + pelvisOffset.y - (leftFoot.soleHeight + rightFoot.soleHeight) * .5f);
        // Most steady lean is above the hips. Translating the hips through the
        // entire lean arc used to exhaust trailing-leg reach before push-off.
        forward += Mathf.Sin(leanAngle * Mathf.Deg2Rad) * alignmentHeight * .18f;
        forward += Mathf.Clamp(-acceleration * .0015f, -.025f, .025f) * locomotion;
        float runningStance = grounded ? LevelRunWeight : 0;
        forward -= .044f * runningStance;
        if (grounded) forward -= .075f * jumpBrace;
        float lateralLoad = grounded ? (rightFoot.load - leftFoot.load) * Mathf.Lerp(.020f, .038f, treadWeight) : 0;
        Vector3 balanceTarget = new Vector3(lateralLoad, 0, forward);
        if (grounded && load > 0 && treadGrade < 0 && treadWeight > 0)
        {
            // The capsule can move downhill while the leading foot is still
            // reaching for the next tread. Keep the visual pelvis over the
            // frontmost actual contact until the lower foot takes the load.
            float front = float.NegativeInfinity;
            if (leftFoot.planted) front = Vector3.Dot(leftFoot.groundPoint - neutral, travelDirection);
            if (rightFoot.planted) front = Mathf.Max(front, Vector3.Dot(rightFoot.groundPoint - neutral, travelDirection));
            Vector3 downhill = transform.InverseTransformDirection(Vector3.ProjectOnPlane(travelDirection, transform.up).normalized);
            float ahead = Vector3.Dot(balanceTarget, downhill) + speed * .05f - front;
            balanceTarget -= downhill * (Mathf.Clamp(ahead - .085f, 0, .40f) * treadWeight);
        }
        Vector3 horizontal = Vector3.SmoothDamp(new Vector3(pelvisOffset.x, 0, pelvisOffset.z),
            balanceTarget, ref pelvisVelocity, .09f, 10, dt);
        Vector3 shift = transform.TransformVector(horizontal);
        float terrain = 0, maximum = float.PositiveInfinity, restDrop = 0, legLength = 0;
        for (int i = 0; i < 2; i++)
        {
            Foot foot = i == 0 ? leftFoot : rightFoot; Limb leg = i == 0 ? leftLeg : rightLeg;
            float length = Vector3.Distance(leg.upper.position, leg.lower.position) + Vector3.Distance(leg.lower.position, leg.end.position);
            float neutralExtension = Vector3.Dot(leg.upper.position - transform.TransformPoint(leg.restEnd), up);
            legLength += length * .5f;
            float walkSpan = stepDistance * .78f * locomotion;
            float safeReach = LegReach(leg);
            float walkingExtension = Mathf.Sqrt(Mathf.Max(.001f, safeReach * safeReach - walkSpan * walkSpan));
            // Walking reach remains terrain-adapted. Running height comes from
            // loading, propulsion and flight below, rather than a fixed squat.
            restDrop += Mathf.Max(0, neutralExtension - walkingExtension) * .5f;
            bool incomingStep = !foot.planted && !foot.swingActive && foot.support != null;
            float swing = SwingProgress(foot);
            bool incomingTread = stairWeight > .35f && swing > .58f && foot.landingReady && foot.landing.collider != null;
            bool placingTread = incomingStep && foot == stairSwing;
            if (!grounded || !foot.planted && !incomingStep && !incomingTread) continue;
            if (placingTread && foot.progress < .57f) continue;
            if (foot.planted) terrain += Vector3.Dot(foot.groundPoint - actor.position, up) * foot.load;
            Vector3 contact = incomingStep ? foot.target : foot.position;
            if (incomingTread)
            {
                contact = foot.landing.collider.transform.TransformPoint(foot.landingLocal) + up * foot.soleHeight;
                contact -= ProjectedTravel((1 - foot.phase) / Mathf.Max(.5f, cadence));
            }
            float reach = safeReach;
            float anticipation = foot.planted && stairWeight > .1f ? Mathf.Min(.035f, foot.gaitContact ? Mathf.Max(0, foot.contactSeconds - foot.contactElapsed) : .015f) : 0;
            Vector3 relativeFootVelocity = foot.velocity - carriedDelta / Mathf.Max(.001f, dt);
            Vector3 anticipatedContact = contact + Vector3.ClampMagnitude(relativeFootVelocity, 2) * anticipation;
            float sideways = Vector3.ProjectOnPlane(anticipatedContact - leg.upper.position - shift - travelDirection * speed * anticipation, up).sqrMagnitude;
            float limit = Vector3.Dot(contact - leg.upper.position, up) + Mathf.Sqrt(Mathf.Max(.001f, reach * reach - sideways));
            if ((incomingStep || incomingTread) && pelvisHeightReady)
            {
                float incomingHeight = Vector3.Dot(pelvisWorldPoint + carriedDelta - neutral - shift, up);
                float transfer = incomingTread ? Smooth(.58f, .98f, swing) * stairWeight :
                    placingTread ? Smooth(.57f, 1, foot.progress) : Smooth(.20f, .70f, foot.progress);
                limit = Mathf.Lerp(incomingHeight, limit, transfer);
            }
            maximum = Mathf.Min(maximum, limit);
        }
        if (grounded)
            terrain = load > 0 ? terrain / load : motor.Motor.GroundHit.collider != null ? Vector3.Dot(motor.Motor.GroundHit.point - actor.position, up) : 0;
        if (grounded && stairSwing != null && treadGrade < 0)
            terrain -= Mathf.Min(.055f, -treadGrade * .085f) * Smooth(.45f, .92f, stairSwing.progress);
        // The supporting legs compress under load and extend as that load is
        // released. During visual flight, no raised swing-foot height enters
        // this solve. Actual jumping/falling still follows the capsule's gravity.
        float compression = Mathf.Clamp01(load) * legLength * Mathf.Lerp(.006f, .009f, sprintAmount) * locomotion;
        unsupportedTime = load > 0 ? 0 : unsupportedTime + dt;
        float runEffort = running * Smooth(1.8f, 3.2f, speed);
        float gaitHeight = grounded ? RunSupportHeight() * runEffort * locomotion * (1 - stairWeight) *
            (1 - Smooth(.5f / cadence, 1 / cadence, unsupportedTime)) : 0;
        float windup = JumpLoad;
        float desired = grounded ? terrain - (restDrop * locomotion + compression) * (1 - runEffort) - .075f * stanceWeight - landingCompression - .235f * windup : 0;
        desired -= .014f * runningStance;
        desired = Mathf.Min(desired, maximum - gaitHeight);
        if (!pelvisHeightReady || !grounded)
        {
            pelvisWorldPoint = neutral + shift + up * pelvisOffset.y;
            if (!pelvisHeightReady) pelvisHeightVelocity = 0;
            pelvisHeightReady = true;
        }
        else pelvisWorldPoint += carriedDelta;
        // Integrate the actual world height once. Capsule step corrections are
        // absorbed here instead of adding a second, unsmoothed reach drop.
        float current = Vector3.Dot(pelvisWorldPoint - neutral - shift, up) - previousRunHeight;
        float response = windup > 0 || landingCompression > .015f ? .045f : .09f;
        float height = Mathf.SmoothDamp(current, desired, ref pelvisHeightVelocity, response, 3, dt) + gaitHeight;
        height = Mathf.Min(height, maximum + .003f);
        previousRunHeight = gaitHeight;
        pelvis.position = neutral + shift + up * height;
        pelvisWorldPoint = pelvis.position;
        pelvisOffset = bodyOffset = transform.InverseTransformVector(pelvis.position - neutral);
    }
    private static float LegLength(Limb leg) => Vector3.Distance(leg.upper.position, leg.lower.position) + Vector3.Distance(leg.lower.position, leg.end.position);
    private float RunningDuty(Limb leg)
    {
        float length = LegLength(leg);
        float pitch = Mathf.Lerp(32, 40, sprintAmount) * Mathf.Deg2Rad;
        float rockerTravel = .16f * (1 - Mathf.Cos(pitch)) + leg.soleHeight * Mathf.Sin(pitch);
        float supportTravel = length * Mathf.Lerp(.25f + .44f, .28f + .49f, sprintAmount) + rockerTravel;
        return Mathf.Clamp(supportTravel / Mathf.Max(.5f, speed) * cadence, .20f, .38f);
    }
    private float RunSupportHeight()
    {
        // The support impulse and ballistic flight share their endpoint heights
        // and velocities. This is not a sine added to an otherwise flat body.
        float length = LegLength(leftLeg), stance = RunningDuty(leftLeg);
        float cycle = 1 / Mathf.Max(1.4f, cadence), contactSeconds = stance * cycle, flightSeconds = (.5f - stance) * cycle;
        float neutralHip = Vector3.Dot(leftLeg.upper.position - actor.position, actor.up);
        float lead = length * Mathf.Lerp(.25f, .28f, sprintAmount), trail = length * Mathf.Lerp(.44f, .49f, sprintAmount);
        float pitch = Mathf.Lerp(32, 40, sprintAmount) * Mathf.Deg2Rad;
        float landingReach = length * Mathf.Cos(25 * Mathf.Deg2Rad * .5f);
        float takeoffReach = Mathf.Min(LegReach(leftLeg), length * Mathf.Cos(20 * Mathf.Deg2Rad * .5f));
        float landing = leftLeg.soleHeight + Mathf.Sqrt(landingReach * landingReach - lead * lead) - neutralHip;
        float takeoff = leftLeg.soleHeight * Mathf.Cos(pitch) + .16f * Mathf.Sin(pitch) +
            Mathf.Sqrt(takeoffReach * takeoffReach - trail * trail) - neutralHip;
        const float gravity = 9.81f;
        float upward = (landing - takeoff) / flightSeconds + .5f * gravity * flightSeconds;
        float downward = upward - gravity * flightSeconds;
        float phase = Mathf.Repeat(distancePhase / (2 * Mathf.PI), .5f);
        if (phase >= stance)
        {
            float t = (phase - stance) * cycle;
            return takeoff + upward * t - .5f * gravity * t * t;
        }
        float u = phase / stance, u2 = u * u, u3 = u2 * u;
        return landing * (2 * u3 - 3 * u2 + 1) + takeoff * (-2 * u3 + 3 * u2) +
            downward * contactSeconds * (u3 - 2 * u2 + u) + upward * contactSeconds * (u3 - u2);
    }
    private static float FitSwing(Foot foot, Limb leg)
    {
        if (foot.planted) return 0;
        float reach = LegReach(leg);
        Vector3 delta = foot.position - leg.upper.position;
        float correction = Mathf.Max(0, delta.magnitude - reach);
        if (correction > 0) foot.position = leg.upper.position + delta.normalized * reach;
        return correction;
    }
    private void BeginStop(Vector3 up)
    {
        stopDirection = Vector3.ProjectOnPlane(travelDirection, up).normalized;
        stopLead = Vector3.Dot(leftFoot.position - rightFoot.position, stopDirection) >= 0 ? leftFoot : rightFoot;
        stopTrail = stopLead == leftFoot ? rightFoot : leftFoot;
        stopTrailPlaced = false; stopHold = 0;
        float remaining = Mathf.Min(.30f, motor.BrakingDistance);
        stopSpacing = Mathf.Clamp(Vector3.Dot(stopLead.position - stopTrail.position, stopDirection) * .30f + remaining * .2f, .16f, .32f);
        Vector3 finish = actor.position + stopDirection * remaining;
        float ahead = Vector3.Dot(stopLead.position - finish, stopDirection);
        if (stopLead.planted && ahead > .025f && ahead < .24f) return;
        bool left = stopLead == leftFoot;
        Vector3 wanted = finish + stopDirection * Mathf.Clamp(ahead * .35f + speed * .035f, .07f, .18f) +
            transform.right * (left ? leftLeg.restEnd.x : rightLeg.restEnd.x);
        bool supported = Probe(wanted, up, out var hit);
        if (!supported) supported = Probe(actor.position + transform.right * (left ? leftLeg.restEnd.x : rightLeg.restEnd.x), up, out hit);
        if (supported) StartStep(stopLead, hit, .20f, .055f);
    }
    private void SettleStop(Vector3 up, float dt)
    {
        TrackFoot(leftFoot); TrackFoot(rightFoot);
        if (!stopTrailPlaced && stopTrail.planted)
        {
            Limb trailLeg = stopTrail == leftFoot ? leftLeg : rightLeg;
            float reach = LegReach(trailLeg);
            Vector3 nextHip = PredictedHip(trailLeg, up) + stopDirection * speed * dt;
            if (Vector3.Distance(nextHip, stopTrail.position) > reach) PlaceStopTrail(up);
        }
        leftFoot.pitch = Mathf.MoveTowards(leftFoot.pitch, 0, dt * 120);
        rightFoot.pitch = Mathf.MoveTowards(rightFoot.pitch, 0, dt * 120);
        AdvanceFoot(leftFoot, up, dt); AdvanceFoot(rightFoot, up, dt);
        if (leftFoot.planted) leftFoot.stage = GaitStage.Balance;
        if (rightFoot.planted) rightFoot.stage = GaitStage.Balance;
        if (!stopLead.planted)
        {
            if (stopLead.progress >= 1 && stopLead.support == null)
            {
                Vector3 wanted = stopLead.position + stopDirection * Mathf.Min(.1f, speed * .06f);
                if (Probe(wanted, up, out var hit)) StartStep(stopLead, hit, .16f, .04f);
            }
            return;
        }
        if (!stopTrailPlaced && stopTrail.progress >= 1)
        {
            float behind = Vector3.Dot(stopTrail.position - actor.position, stopDirection);
            float gap = Vector3.Dot(stopLead.position - stopTrail.position, stopDirection);
            if (stopTrail.planted && behind >= -.25f && behind <= .015f && gap > .10f && gap < .42f)
                stopTrailPlaced = true;
            else
                PlaceStopTrail(up);
        }
        if (stopTrailPlaced && stopTrail.planted) stopHold += dt;
        if (stopHold > .3f) AdjustIdleContacts(up);
    }
    private void PlaceStopTrail(Vector3 up)
    {
        bool left = stopTrail == leftFoot;
        Vector3 finish = actor.position + stopDirection * Mathf.Min(.30f, motor.BrakingDistance);
        Vector3 lead = stopLead.planted ? stopLead.position : stopLead.target;
        float offset = Mathf.Clamp(Vector3.Dot(lead - finish, stopDirection) - stopSpacing, -.22f, -.06f);
        Vector3 wanted = finish + stopDirection * offset + transform.right * (left ? leftLeg.restEnd.x : rightLeg.restEnd.x);
        if (Probe(wanted, up, out var hit)) { StartStep(stopTrail, hit, .24f, .045f); stopTrailPlaced = true; }
    }
    private void MaintainIdle(Vector3 up, float dt)
    {
        TrackFoot(leftFoot); TrackFoot(rightFoot);
        leftFoot.pitch = Mathf.MoveTowards(leftFoot.pitch, 0, dt * 120);
        rightFoot.pitch = Mathf.MoveTowards(rightFoot.pitch, 0, dt * 120);
        AdjustIdleContacts(up);
        AdvanceFoot(leftFoot, up, dt); AdvanceFoot(rightFoot, up, dt);
        if (leftFoot.planted) leftFoot.stage = GaitStage.Balance;
        if (rightFoot.planted) rightFoot.stage = GaitStage.Balance;
    }
    private void AdjustIdleContacts(Vector3 up)
    {
        if (leftFoot.progress < 1 || rightFoot.progress < 1) return;
        bool left = nextLeft; Foot foot = left ? leftFoot : rightFoot;
        nextLeft = !nextLeft;
        if (!foot.planted && foot.support != null) return;
        Vector3 local = transform.InverseTransformPoint(foot.position);
        float side = Mathf.Lerp(left ? leftLeg.restEnd.x : rightLeg.restEnd.x, left ? -.26f : .26f, stanceWeight);
        bool widenGuard = stanceWeight > .8f && Mathf.Abs(local.x - side) > .08f;
        bool turn = Quaternion.Angle(foot.plantedRotation, transform.rotation) > 35;
        bool unreachable = Vector3.ProjectOnPlane(foot.position - actor.position, up).magnitude > .43f;
        if (foot.planted && !widenGuard && !turn && !unreachable) return;
        // Keep the existing fore/aft stagger. Only missing support, an actual
        // turn, reach limits, or an intentional sword guard can request a step.
        Vector3 wanted = foot.position;
        if (widenGuard || turn || unreachable)
            wanted = transform.TransformPoint(new Vector3(side, local.y, Mathf.Clamp(local.z, -.23f, .23f)));
        if (Probe(wanted, up, out var hit)) StartStep(foot, hit, stepSeconds, Mathf.Min(footLift, .06f));
    }
    private void StartStep(Foot foot, RaycastHit hit, float duration, float lift)
    {
        foot.start = foot.position; foot.stepVelocity = Vector3.ClampMagnitude(foot.velocity, 2.5f);
        foot.target = hit.point + hit.normal * foot.soleHeight; foot.normal = hit.normal;
        foot.support = hit.collider.transform; foot.groundPoint = hit.point;
        foot.localContact = foot.support.InverseTransformPoint(hit.point);
        foot.stepDuration = duration; foot.stepLift = lift; foot.progress = 0; foot.planted = false;
        foot.swingActive = foot.gaitContact = false; foot.stage = GaitStage.Balance;
    }
    private Vector3 FootRest(bool left)
    {
        Vector3 rest = left ? leftLeg.restEnd : rightLeg.restEnd;
        return new Vector3(Mathf.Lerp(rest.x, left ? -.26f : .26f, stanceWeight), rest.y,
            stanceWeight * (left ? -.12f : .16f) + stairWeight * (1 - locomotion) * (left ? -.24f : .24f));
    }
    private Quaternion StrideFrame(Vector3 up) => Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, up).normalized, up);
    private static float LegReach(Limb leg)
    {
        float a = Vector3.Distance(leg.upper.position, leg.lower.position), b = Vector3.Distance(leg.lower.position, leg.end.position);
        // Keep a bend reserve in the same envelope used by the IK solver.
        // Contact planning must not demand a straighter leg than IK can supply.
        return Mathf.Sqrt(a * a + b * b + 2 * a * b * Mathf.Cos(MinimumLegBend * Mathf.Deg2Rad)) - .006f;
    }
    private float ContactDuty(Limb leg, Vector3 up)
    {
        float flat = Mathf.Lerp(Mathf.Lerp(.64f, .56f, Mathf.Clamp01(speed / 1.5f)), RunningDuty(leg), running);
        return Mathf.Lerp(flat, Mathf.Clamp(.46f / Mathf.Max(.3f, cycleDistance), .17f, .60f), stairWeight);
    }
    private void BeginSwing(Foot foot, Limb leg, float phase, Vector3 up)
    {
        Quaternion frame = StrideFrame(up);
        foot.swingStartLocal = Quaternion.Inverse(frame) * (foot.position - actor.position);
        foot.swingFrame = frame;
        foot.swingStartHeight = Vector3.Dot(foot.position, up);
        foot.swingStartVelocity = Quaternion.Inverse(frame) * (foot.velocity - travelDirection * speed);
        foot.swingDuration = (1 - phase) / Mathf.Max(.5f, cadence);
        foot.swingElapsed = 0;
        foot.swingStartPhase = phase;
        foot.swingJustStarted = true; foot.swingStartPitch = foot.pitch;
        foot.swingActive = true; foot.landingReady = false;
        foot.planted = foot.gaitContact = false; foot.progress = 1; foot.support = null;
        foot.stage = GaitStage.ToeOff;
    }
    private Vector3 LandingHip(Limb leg, Vector3 up, float dt) => PredictedHip(leg, up) +
        transform.TransformVector(pelvisVelocity) * dt + up * Mathf.Max(0, pelvisHeightVelocity) * dt;
    private void ScheduleContact(Foot foot, Limb leg, Vector3 up)
    {
        float nominal = ContactDuty(leg, up);
        bool late = foot.phase > nominal * .5f;
        foot.contactStartPhase = late ? foot.phase : 0;
        // A terrain/turn recovery may reach support a frame after the nominal
        // contact window. Let it load briefly, then rejoin the SAME cycle;
        // never cancel the final few milliseconds of a real landing step.
        foot.contactDuty = late ? Mathf.Min(.49f, foot.phase + Mathf.Min(nominal, .07f * cadence)) : nominal;
        foot.contactElapsed = Mathf.Max(0, foot.phase - foot.contactStartPhase) / Mathf.Max(.5f, cadence);
        foot.contactSeconds = (foot.contactDuty - foot.contactStartPhase) / Mathf.Max(.5f, cadence);
        foot.contactPitch = foot.pitch; foot.gaitContact = true;
    }
    private bool TouchDown(Foot foot, Limb leg, Vector3 up, float dt)
    {
        RaycastHit hit = foot.landing;
        bool found = foot.landingReady && hit.collider != null && hit.collider.gameObject.activeInHierarchy;
        if (found) hit.point = hit.collider.transform.TransformPoint(foot.landingLocal);
        if (!found) return false;
        Vector3 ankle = hit.point + up * foot.soleHeight;
        float reach = LegReach(leg);
        foot.landingReachError = Vector3.Distance(LandingHip(leg, up, dt), ankle) - reach;
        foot.landingFootError = Vector3.Distance(foot.position, ankle);
        if (foot.landingReachError > .003f || foot.landingFootError > .012f) return false;
        Plant(foot, hit); foot.swingActive = foot.landingReady = false;
        ScheduleContact(foot, leg, up); foot.stage = GaitStage.Contact;
        StepSound(foot); return true;
    }
    private void GaitFoot(Foot foot, Limb leg, bool left, float phase, Vector3 up, float dt)
    {
        bool wrapped = foot.cycleReady && phase + .5f < foot.phase;
        foot.phase = phase;
        if (wrapped)
        {
            // Finish on the same floor point approached during placement.
            // A failed contact is retried against current terrain below, never
            // held forever at the preceding cycle's unreachable destination.
            if (foot.swingActive && foot.landingReady && foot.landing.collider != null)
            {
                Vector3 landing = foot.landing.collider.transform.TransformPoint(foot.landingLocal) + up * foot.soleHeight;
                foot.landingFootError = Vector3.Distance(foot.position, landing);
                if (Vector3.Distance(foot.position, landing) <= .035f)
                {
                    Vector3 before = foot.position;
                    foot.position = landing;
                    if (!TouchDown(foot, leg, up, dt)) foot.position = before;
                }
            }
            foot.swingActive = false;
            foot.landingReady = false;
        }
        if (!foot.cycleReady)
        {
            foot.cycleReady = true; foot.contactDuty = ContactDuty(leg, up);
            foot.contactStartPhase = 0;
            foot.contactSeconds = foot.contactDuty / Mathf.Max(.5f, cadence);
            foot.contactPitch = foot.pitch;
            // The rear foot starts its recovery from its actual standing pose.
            // Assigning it the other half of the shared clock must not first
            // jump its flat sole to a late toe-off rocker pose.
            if (phase >= .5f) BeginSwing(foot, leg, phase, up);
        }
        foot.contactElapsed = Mathf.Max(0, phase - foot.contactStartPhase) / Mathf.Max(.5f, cadence);
        foot.contactSeconds = (foot.contactDuty - foot.contactStartPhase) / Mathf.Max(.5f, cadence);
        bool completingLanding = foot.progress < 1 && foot.support != null && phase < .42f;
        if (!foot.planted && !foot.swingActive && (phase < foot.contactDuty || completingLanding))
        {
            // If turning/terrain invalidated the planned landing, recover the
            // floor beneath this hip. Chasing a new forward landing every
            // frame can keep a leg airborne across several otherwise valid
            // support windows, especially during a sudden direction reversal.
            Vector3 reacquire = PredictedHip(leg, up);
            reacquire -= up * Vector3.Dot(reacquire - actor.position, up);
            Vector3 right = StrideFrame(up) * Vector3.right;
            reacquire += right * (StrideSide(leg) - Vector3.Dot(reacquire - actor.position, right));
            if (Probe(reacquire, up, out var hit, stairWeight > .45f))
            {
                foot.landing = hit; foot.landingLocal = hit.collider.transform.InverseTransformPoint(hit.point); foot.landingReady = true;
                if (TouchDown(foot, leg, up, dt)) return;
                if (foot.progress >= 1 && foot.support == null) StartStep(foot, hit, Mathf.Max(.06f, (foot.contactDuty - phase) / Mathf.Max(.5f, cadence)), .025f);
                else { foot.target = hit.point + hit.normal * foot.soleHeight; foot.support = hit.collider.transform; foot.localContact = foot.support.InverseTransformPoint(hit.point); }
            }
            AdvanceFoot(foot, up, dt);
            if (foot.planted) ScheduleContact(foot, leg, up);
            else return;
        }
        if (foot.planted)
        {
            TrackFoot(foot);
            foot.contactDuty = Mathf.Max(foot.contactStartPhase + .035f * cadence, Mathf.Min(foot.contactDuty, foot.contactStartPhase + ContactDuty(leg, up)));
            float length = LegLength(leg), lead = length * Mathf.Lerp(.25f, .28f, sprintAmount);
            float trail = Mathf.Lerp(length * Mathf.Lerp(.44f, .49f, sprintAmount), .29f, stairWeight);
            float toeOffPitch = Mathf.Lerp(Mathf.Lerp(32, 40, sprintAmount), 13, stairWeight);
            float pitch = toeOffPitch * Mathf.Deg2Rad;
            float rockerTravel = .16f * (1 - Mathf.Cos(pitch)) + foot.soleHeight * Mathf.Sin(pitch);
            float passed = Vector3.Dot(PredictedHip(leg, up) - foot.groundPoint, travelDirection);
            float spatialContact = Mathf.InverseLerp(-lead, trail + rockerTravel, passed);
            float contact = Mathf.Max(Mathf.Clamp01((phase - foot.contactStartPhase) / Mathf.Max(.025f, foot.contactDuty - foot.contactStartPhase)), spatialContact * running);
            foot.contactElapsed = contact * foot.contactSeconds;
            foot.stage = contact < .18f ? GaitStage.Contact : contact < .55f ? GaitStage.Loading : contact < .80f ? GaitStage.Propulsion : GaitStage.ToeOff;
            // Extend the loaded thigh before releasing the heel. Early heel
            // rise shortened the leg and turned propulsion into knee recovery.
            foot.pitch = contact < .22f ? Mathf.Lerp(foot.contactPitch, 0, Smoother(contact / .22f)) : Mathf.Lerp(0, toeOffPitch, Smoother(Mathf.InverseLerp(.55f, 1, contact)));
            // Transfer the pivot onto the forefoot while the sole is still
            // flat. Ramping pivot and heel pitch together suppresses almost all
            // ankle lift until the last instant, then snaps the recovery knee.
            float pivot = Mathf.Lerp(-.045f, 0, Smoother(Mathf.Clamp01(contact / .22f))) + .16f * Smoother(Mathf.InverseLerp(.22f, .50f, contact));
            foot.contactPivot = pivot;
            Quaternion surface = Quaternion.FromToRotation(foot.plantedRotation * Vector3.up, foot.normal) * foot.plantedRotation;
            Quaternion rocker = surface * Quaternion.AngleAxis(foot.pitch, Vector3.right);
            foot.position = foot.groundPoint + surface * Vector3.forward * pivot - rocker * new Vector3(0, -foot.soleHeight, pivot);
            float reach = LegReach(leg);
            // Leave enough room for the trailing ankle to decelerate and lift;
            // waiting until the leg is already fully extended forces swing IK
            // to clip its first recovery segment.
            bool reachable = Vector3.Distance(foot.position, PredictedHip(leg, up)) < reach + .003f;
            bool spentSweep = Vector3.Dot(PredictedHip(leg, up) - foot.position, travelDirection) > trail + .025f;
            if (reachable && !spentSweep && phase < foot.contactDuty) return;
            foot.takeoffDelta = PredictedHip(leg, up) - foot.position;
            foot.takeoffReach = reach; foot.takeoffProgress = contact;
            BeginSwing(foot, leg, phase, up);
        }
        else if (!foot.swingActive)
        {
            BeginSwing(foot, leg, phase, up);
        }
        SwingFoot(foot, leg, left, up, dt);
    }
    private static float Smoother(float t)
    {
        t = Mathf.Clamp01(t); return t * t * t * (10 + t * (-15 + 6 * t));
    }
    private static Vector3 SwingCurve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 e,
        Vector3 startVelocity, Vector3 endVelocity, float seconds, float progress, float first, float second, float third)
    {
        // Clamped cubic spline: solve all three internal velocities together.
        // Unlike independent ease-in/out segments, position, velocity and
        // acceleration remain continuous without stopping acceleration at every
        // anatomical waypoint. End velocities come from take-off and the floor.
        float h0 = first, h1 = second - first, h2 = third - second, h3 = 1 - third;
        Vector3 v0 = startVelocity * seconds, v4 = endVelocity * seconds;
        Vector3 r1 = 3 * (h1 * (b - a) / h0 + h0 * (c - b) / h1) - h1 * v0;
        Vector3 r2 = 3 * (h2 * (c - b) / h1 + h1 * (d - c) / h2);
        Vector3 r3 = 3 * (h3 * (d - c) / h2 + h2 * (e - d) / h3) - h2 * v4;
        float m1 = 2 * (h0 + h1), factor = h2 / m1;
        float m2 = 2 * (h1 + h2) - factor * h0; r2 -= factor * r1;
        factor = h3 / m2;
        float m3 = 2 * (h2 + h3) - factor * h1; r3 -= factor * r2;
        Vector3 v3 = r3 / m3, v2 = (r2 - h1 * v3) / m2, v1 = (r1 - h0 * v2) / m1;
        Vector3 Cubic(Vector3 p, Vector3 q, Vector3 pv, Vector3 qv, float time, float span)
        {
            float t = Mathf.Clamp01(time / span), t2 = t * t, t3 = t2 * t;
            return p * (2 * t3 - 3 * t2 + 1) + q * (-2 * t3 + 3 * t2) +
                pv * span * (t3 - 2 * t2 + t) + qv * span * (t3 - t2);
        }
        if (progress < first) return Cubic(a, b, v0, v1, progress, h0);
        if (progress < second) return Cubic(b, c, v1, v2, progress - first, h1);
        if (progress < third) return Cubic(c, d, v2, v3, progress - second, h2);
        return Cubic(d, e, v3, v4, progress - third, h3);
    }
    private float ProjectedSpeed(float seconds) => Mathf.Max(0, speed + Mathf.Min(0, acceleration) * seconds);
    private Vector3 ProjectedTravel(float seconds)
    {
        float braking = Mathf.Min(0, acceleration);
        float time = braking < -.01f ? Mathf.Min(seconds, speed / -braking) : seconds;
        return travelDirection * (speed * time + .5f * braking * time * time);
    }
    private float StrideEffort => motor == null ? 1 : Mathf.Lerp(1, Mathf.Clamp(ProjectedSpeed(.10f) / Mathf.Max(1, motor.MoveSpeed), .35f, 1), running);
    private float LevelRunWeight => running * Smooth(2.5f, 3.8f, speed) * (1 - sprintAmount) * (1 - stairWeight) *
        (1 - Smooth(45, 120, Mathf.Abs(turnRate)));
    // Leave room for the trousers and sandals when the feet pass. The
    // reference skeleton's narrower track intersects this character's mesh.
    private float StrideSide(Limb leg) => leg.restEnd.x * Mathf.Lerp(1, .58f, LevelRunWeight);
    private static float SwingProgress(Foot foot) => foot.swingActive ? foot.swingElapsed / Mathf.Max(.06f, foot.swingDuration) : 0;
    private float ToeFlex(Foot foot)
    {
        // The planted forefoot bends under load, then relaxes after release.
        float released = Smooth(0, .20f, SwingProgress(foot)) * LevelRunWeight;
        return -Mathf.Max(0, foot.pitch) * .65f * (1 - released);
    }
    private float KneeWidth(Foot foot)
    {
        // Let the recovering knee open slightly without widening foot placement.
        float swing = SwingProgress(foot);
        float opening = Smooth(.03f, .22f, swing) * (1 - Smooth(.5f, .90f, swing));
        return Mathf.Lerp(.10f, .02f + .03f * opening, LevelRunWeight);
    }
    private Vector3 LandingPoint(Limb leg, bool left, Vector3 up, float seconds)
    {
        Quaternion frame = StrideFrame(up); Vector3 forward = frame * Vector3.forward, right = frame * Vector3.right;
        Vector3 hip = PredictedHip(leg, up);
        float reach = LegReach(leg);
        float height = Vector3.Dot(hip - actor.position, up) - (left ? leftFoot.soleHeight : rightFoot.soleHeight);
        float span = Mathf.Sqrt(Mathf.Max(.001f, reach * reach - height * height));
        float walkingLead = Mathf.Min(span * .92f, speed * duty / Mathf.Max(.5f, cadence) * .5f);
        float runningLead = LegLength(leg) * Mathf.Lerp(.25f, .28f, sprintAmount) * Mathf.Clamp01(ProjectedSpeed(seconds) / Mathf.Max(.1f, speed));
        float lead = Mathf.Lerp(walkingLead, runningLead, running);
        lead = Mathf.Lerp(lead, Mathf.Min(span * .85f, .14f), stairWeight);
        float side = StrideSide(leg);
        return actor.position + right * side + forward * (Vector3.Dot(hip - actor.position, forward) + lead) + ProjectedTravel(seconds);
    }
    private void SwingFoot(Foot foot, Limb leg, bool left, Vector3 up, float dt)
    {
        Quaternion frame = StrideFrame(up), inverse = Quaternion.Inverse(frame);
        bool firstFrame = foot.swingJustStarted;
        foot.swingJustStarted = false;
        // Both legs read one stride clock, exactly half a cycle apart. There
        // are no independently integrated recovery clocks that can drift into
        // a same-leg rhythm after a missed contact or a speed transition.
        float span = Mathf.Max(.04f, 1 - foot.swingStartPhase);
        foot.swingDuration = span / Mathf.Max(.5f, cadence);
        foot.swingElapsed = Mathf.Max(0, foot.phase - foot.swingStartPhase) / Mathf.Max(.5f, cadence);
        float progress = firstFrame ? 0 : Mathf.Clamp01((foot.phase - foot.swingStartPhase) / span);
        foot.stage = progress < .22f ? GaitStage.Recovery : progress < .42f ? GaitStage.Passing : GaitStage.Placement;
        float remaining = (1 - foot.phase) / Mathf.Max(.5f, cadence);
        bool preparing = progress > Mathf.Lerp(.42f, .18f, stairWeight);
        Vector3 probe = preparing ? LandingPoint(leg, left, up, remaining) : foot.position;
        if (Probe(probe, up, out var ground, preparing && stairWeight > .45f))
        {
            foot.normal = ground.normal;
            foot.terrain = Mathf.Lerp(foot.terrain, Vector3.Dot(ground.point - actor.position, up), 1 - Mathf.Exp(-30 * dt));
            if (preparing && (!foot.landingReady || progress < .94f))
            { foot.landing = ground; foot.landingLocal = ground.collider.transform.InverseTransformPoint(ground.point); foot.landingReady = true; }
        }
        float thighLength = Vector3.Distance(leg.upper.position, leg.lower.position);
        float shinLength = Vector3.Distance(leg.lower.position, leg.end.position);
        float length = thighLength + shinLength;
        Vector3 hip = inverse * (PredictedHip(leg, up) - actor.position);
        float side = StrideSide(leg);
        float floor = foot.terrain + foot.soleHeight;
        // Take-off history belongs to the direction in which it happened.
        // Rotating that history with the character makes an airborne foot orbit
        // the hips during a turn and miss its scheduled floor contact.
        Quaternion takeoffToCurrent = inverse * foot.swingFrame;
        Vector3 start = takeoffToCurrent * foot.swingStartLocal;
        foot.swingStartHeight += Vector3.Dot(carriedDelta, up);
        start.y = foot.swingStartHeight - Vector3.Dot(actor.position, up);
        float effort = StrideEffort;
        // Recover behind the hip after push-off, then bring the foot under
        // the body. A low heel here makes even the sprint look like a jog.
        float runRecovery = running * effort;
        float heelHeight = Mathf.Lerp(.10f, Mathf.Lerp(.286f, .387f, sprintAmount) * effort, running);
        float heelBehind = Mathf.Lerp(.35f, Mathf.Lerp(.52f, .64f, sprintAmount), running) * Mathf.Lerp(.55f, 1, effort);
        Vector3 heel = new Vector3(hip.x, floor + length * heelHeight, hip.z - length * heelBehind);
        // During normal running the thigh is still behind the hip as the
        // knee folds. Build the waypoint from those two links so lifting the
        // heel cannot pull the knee forward into a marching pose.
        float fold = LevelRunWeight * effort;
        const float thighBack = 16 * Mathf.Deg2Rad, shinBack = 79 * Mathf.Deg2Rad;
        Vector3 foldedHeel = hip - Vector3.up * (thighLength * Mathf.Cos(thighBack) + shinLength * Mathf.Cos(shinBack))
            - Vector3.forward * (thighLength * Mathf.Sin(thighBack) + shinLength * Mathf.Sin(shinBack));
        foldedHeel.y = Mathf.Max(foldedHeel.y, heel.y);
        foldedHeel.x = side;
        heel = Vector3.Lerp(heel, foldedHeel, fold);
        Vector3 pass = new Vector3(side, floor + length * Mathf.Lerp(.07f, Mathf.Lerp(.10f, .14f, sprintAmount) * effort, running), hip.z + length * .02f);
        Vector3 end = inverse * (LandingPoint(leg, left, up, 0) - actor.position); end.y = floor;
        if (foot.landingReady && foot.landing.collider != null)
        {
            Vector3 point = foot.landing.collider.transform.TransformPoint(foot.landingLocal) + up * foot.soleHeight;
            end = inverse * (point - actor.position - ProjectedTravel(remaining));
            end.y = Vector3.Dot(point - actor.position, up);
        }
        // Lift above the higher tread before passing over its riser. On a
        // descent the same path stays level until the foot is over the lower tread.
        float clearance = Mathf.Max(start.y, end.y);
        heel = Vector3.Lerp(heel, new Vector3(side, clearance + .09f, Mathf.Lerp(start.z, end.z, .16f)), stairWeight);
        float passHeight = Mathf.Lerp(clearance, end.y, .55f * Smooth(.035f, .20f, start.y - end.y));
        pass = Vector3.Lerp(pass, new Vector3(side, passHeight + .07f, Mathf.Lerp(start.z, end.z, .60f)), stairWeight);
        float duration = Mathf.Max(.06f, foot.swingDuration);
        float preparationHeight = floor + length * Mathf.Lerp(.025f, Mathf.Lerp(.04f, .06f, sprintAmount), running);
        preparationHeight = Mathf.Lerp(preparationHeight, end.y + .035f, stairWeight);
        float safeSpan = Mathf.Sqrt(Mathf.Max(.001f, LegReach(leg) * LegReach(leg) - Mathf.Pow(hip.y - preparationHeight, 2)));
        // Reach before touchdown, then retract the foot while matching its
        // world speed to the supporting floor. Braking over the final third of
        // swing avoids the old late kick/IK extension immediately before contact.
        Vector3 reach = new Vector3(side, preparationHeight,
            Mathf.Min(hip.z + safeSpan * .90f, end.z + speed * duration * .22f * .35f));
        Vector3 ev = inverse * (-travelDirection * ProjectedSpeed(remaining)); ev.y = 0;
        Vector3 startVelocity = takeoffToCurrent * foot.swingStartVelocity;
        float backsweepEnd = Mathf.Lerp(.18f, .29f, runRecovery) - .025f * fold;
        float passTime = Mathf.Lerp(.48f, .52f, runRecovery);
        Vector3 local = SwingCurve(start, heel, pass, reach, end, startVelocity, ev, duration, progress, backsweepEnd, passTime, .78f);
        // The ankle reverses its backsweep before it reaches maximum heel
        // clearance. Giving height the same very short turnaround as fore/aft
        // travel folds the knee almost instantaneously just after toe-off.
        float heelLiftEnd = Mathf.Lerp(.28f, .31f, runRecovery) - .025f * fold;
        local.y = SwingCurve(Vector3.up * start.y, Vector3.up * heel.y, Vector3.up * pass.y, Vector3.up * reach.y, Vector3.up * end.y,
            Vector3.up * startVelocity.y, Vector3.zero, duration, progress, heelLiftEnd, .58f, .80f).y;
        // Preserve fore/aft velocity through backsweep. If the ankle needs more
        // clearance to fit the hip's reachable envelope, lift it here instead
        // of shortening the stride with a radial IK clamp afterwards.
        float plannedReach = LegReach(leg) - .006f;
        float horizontal = (local.x - hip.x) * (local.x - hip.x) + (local.z - hip.z) * (local.z - hip.z);
        if (horizontal < plannedReach * plannedReach)
        {
            float minimumHeight = hip.y - Mathf.Sqrt(plannedReach * plannedReach - horizontal);
            float blend = Mathf.Max(0, .01f - Mathf.Abs(local.y - minimumHeight)) / .01f;
            local.y = Mathf.Max(local.y, minimumHeight) + .0025f * blend * blend;
        }
        foot.position = actor.position + frame * local;
        // Carry the toe-off point through heel recovery, then bring the sole
        // forward for placement. Walking keeps its shorter ankle roll.
        float walkPitch = Mathf.Lerp(foot.swingStartPitch, -5, Smoother(Mathf.Clamp01(progress / .35f)));
        float pointedPitch = Mathf.Lerp(Mathf.Lerp(48, 56, sprintAmount), 68, fold);
        float recoveryPitch = Mathf.Lerp(foot.swingStartPitch, pointedPitch,
            Smoother(Mathf.Clamp01(progress / heelLiftEnd)));
        recoveryPitch = Mathf.Lerp(recoveryPitch, -5, Smoother(Mathf.InverseLerp(heelLiftEnd, .72f, progress)));
        foot.pitch = Mathf.Lerp(walkPitch, recoveryPitch, runRecovery);
        foot.pitch = Mathf.Lerp(foot.pitch, Mathf.Lerp(foot.swingStartPitch, 0, Smoother(Mathf.Clamp01(progress / .55f))), stairWeight);
        foot.planted = false; foot.progress = 1; foot.support = null; foot.gaitContact = false;
    }

    private void TrackFoot(Foot foot)
    {
        if (foot.support == null || !foot.support.gameObject.activeInHierarchy) { foot.planted = false; foot.support = null; return; }
        foot.groundPoint = foot.support.TransformPoint(foot.localContact);
        if (foot.planted)
        {
            Quaternion surface = Quaternion.FromToRotation(foot.plantedRotation * Vector3.up, foot.normal) * foot.plantedRotation;
            Quaternion rocker = surface * Quaternion.AngleAxis(foot.pitch, Vector3.right);
            float pivot = foot.gaitContact ? foot.contactPivot : 0;
            foot.position = foot.groundPoint + surface * Vector3.forward * pivot - rocker * new Vector3(0, -foot.soleHeight, pivot);
        }
        else foot.target = foot.groundPoint + foot.normal * foot.soleHeight;
    }
    private void Plant(Foot foot, RaycastHit hit)
    {
        foot.position = hit.point + actor.up * foot.soleHeight; foot.normal = hit.normal;
        foot.support = hit.collider.transform; foot.groundPoint = hit.point;
        foot.localContact = foot.support.InverseTransformPoint(hit.point);
        foot.progress = 1; foot.planted = true;
        foot.plantedRotation = transform.rotation;
    }
    private void AdvanceFoot(Foot foot, Vector3 up, float dt)
    {
        if (foot.progress >= 1 && (foot.planted || foot.support == null || foot.swingActive)) return;
        foot.progress = Mathf.Min(1, foot.progress + dt / foot.stepDuration);
        float t = foot.progress, t2 = t * t, t3 = t2 * t;
        float blend = t3 * (10 + t * (-15 + 6 * t));
        float initialVelocity = t - 6 * t3 + 8 * t3 * t - 3 * t3 * t2;
        float lift = foot.stepLift + Mathf.Max(0, Vector3.Dot(foot.target - foot.start, up)) * .5f;
        if (foot == stairSwing)
        {
            Vector3 displacement = foot.target - foot.start;
            float rise = Vector3.Dot(displacement, up);
            // Up: clear the riser early. Down: reach out over the nosing,
            // then lower the sole onto the locked contact.
            float vertical = rise >= 0 ? Smoother(t / .57f) : Smoother(Mathf.InverseLerp(.40f, 1, t));
            float forward = rise >= 0 ? blend : Smoother(t / .94f);
            float arc = Mathf.Sin(Mathf.PI * t);
            Vector3 entryVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(foot.stepVelocity, up), 1.4f);
            foot.position = foot.start + Vector3.ProjectOnPlane(displacement, up) * forward + entryVelocity * foot.stepDuration * initialVelocity +
                up * (rise * vertical + foot.stepLift * arc * arc);
            foot.pitch = Mathf.Lerp(foot.swingStartPitch, 0, Smoother(t / .42f)) + (rise >= 0 ? -7 : 9) * arc * arc;
        }
        else
        {
            foot.position = Vector3.Lerp(foot.start, foot.target, blend) + foot.stepVelocity * foot.stepDuration * initialVelocity +
                up * (lift * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 2));
        }
        if (foot.progress >= 1)
        {
            Limb leg = foot == leftFoot ? leftLeg : rightLeg;
            float reach = LegReach(leg);
            Vector3 landingHip = LandingHip(leg, up, dt);
            // Planning leaves 6 mm inside the IK envelope. Half that reserve
            // tolerates the pelvis integrator approaching the contact limit;
            // requiring exact inequality made a valid step retry forever.
            if (Vector3.Distance(landingHip, foot.target) > reach + .003f)
            {
                // Keep a close pending landing supported while the pelvis
                // finishes loading. Restarting its lift each frame caused a
                // braking foot to bounce forever just above the same floor.
                if (foot != stairSwing && Vector3.Distance(landingHip, foot.target) > reach + .04f &&
                    Probe(actor.position + Vector3.ProjectOnPlane(landingHip - actor.position, up), up, out var recovery))
                    StartStep(foot, recovery, .16f, .025f);
                return;
            }
            foot.planted = true; foot.plantedRotation = transform.rotation; foot.stage = GaitStage.Balance; StepSound(foot);
        }
    }
    private void StepSound(Foot foot)
    {
        FootPlants++; foot.plants++;
        if (speed > .1f && motor.FootstepAudioClips != null && motor.FootstepAudioClips.Length > 0)
            audioSource.PlayOneShot(motor.FootstepAudioClips[FootPlants % motor.FootstepAudioClips.Length], motor.FootstepAudioVolume);
    }
    private Quaternion FootRotation(Foot foot, Limb limb, bool grounded) =>
        grounded && foot.planted ? Quaternion.FromToRotation(foot.plantedRotation * Vector3.up, foot.normal) * foot.plantedRotation * Quaternion.AngleAxis(foot.pitch, Vector3.right) * limb.restRotation :
        transform.rotation * Quaternion.AngleAxis(foot.pitch, Vector3.right) * limb.restRotation;

    private void BeginSwordAction()
    {
        sheathing = sword.IsDrawn; action = true; actionTime = previousAction = 0;
    }
    private void UpdateSword(float dt, float moving)
    {
        if (action)
        {
            previousAction = actionTime; actionTime = Mathf.Min(swordActionSeconds, actionTime + dt);
            float p = actionTime / swordActionSeconds;
            if (!sheathing && previousAction < .20f * swordActionSeconds && p >= .20f) sword.OnSwordDrawGrip();
            if (sheathing && previousAction < .80f * swordActionSeconds && p >= .80f) sword.OnSwordSheatheGrip();
            if (actionTime >= swordActionSeconds)
            { action = false; if (sheathing) sword.OnSwordSheatheFinished(); else sword.OnSwordDrawFinished(); }
        }
        carry = Mathf.MoveTowards(carry, sword.IsDrawn && !action ? moving : 0, dt * 4);
        sword.sheathSocket.SetLocalPositionAndRotation(sheathLocalPosition, sheathLocalRotation);
    }
    private static float Smooth(float from, float to, float phase) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, phase));
    private static Quaternion BladeRotation(Vector3 axis) => Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, axis).normalized, axis);
    private Vector3 PosePoint(Vector3 local) => transform.TransformPoint(local + bodyOffset);
    private static Vector3 ArmDirection(bool left, float flex, float abduct, float air = 0)
    {
        flex *= Mathf.Deg2Rad; abduct *= Mathf.Deg2Rad;
        return new Vector3((left ? -1 : 1) * Mathf.Sin(abduct) * Mathf.Lerp(Mathf.Cos(flex), 1, air),
            -Mathf.Cos(abduct) * Mathf.Cos(flex), Mathf.Sin(flex)).normalized;
    }
    public void PoseNeutralArms()
    {
        torsoFrame = transform.rotation;
        PoseArm(leftArm, true, 0); PoseArm(rightArm, false, 0);
        ApplyFingers();
        foreach (var twist in twists) twist.Pose();
    }
    private void ObserveStrideEvent(Foot foot, Limb leg, bool wasPlanted, bool wasStance, float advance)
    {
        bool touchdown = foot.planted && foot.gaitContact && !wasPlanted;
        bool takeoff = wasStance && wasPlanted && foot.swingActive && !foot.planted;
        if (!touchdown && !takeoff) return;
        float expected = touchdown ? 0 : RunningDuty(leg) * 360;
        float observed = foot.phase * 360 - advance * Mathf.Rad2Deg * .5f;
        float error = Mathf.DeltaAngle(observed, expected);
        // Ignore recovery plants far outside the running window. Small timing
        // corrections are spread over the following step, never snapped.
        if (Mathf.Abs(error) < 45)
            phaseOffsetTarget = Mathf.Lerp(phaseOffsetTarget, Mathf.Clamp(error, -20, 20) * Mathf.Deg2Rad, touchdown ? .6f : .35f);
    }

    private void CoordinateUpperBody(float pelvisYaw, float runDrive, float runWeight, float dt)
    {
        // Thigh feedback handles walking and balance. During running, the
        // contact-timed drive leads both arms and the chest against the pelvis.
        float separation = Vector3.Dot((rightLeg.lower.position-rightLeg.upper.position)-(leftLeg.lower.position-leftLeg.upper.position), transform.forward);
        float scale = LegLength(leftLeg) * .46f;
        float lead = separation / Mathf.Sqrt(separation * separation + scale * scale);
        float response = Mathf.Clamp(.05f / Mathf.Max(1, cadence), .018f, .035f);
        upperBodyDrive = Mathf.SmoothDamp(upperBodyDrive, lead, ref upperBodyVelocity, response, 30, dt);
        float drive = Mathf.Lerp(upperBodyDrive, runDrive * Mathf.Lerp(.68f, .75f, sprintAmount), runWeight);
        drive = Mathf.Lerp(drive, treadDrive, treadWeight);
        float effort = locomotion * StrideEffort;
        // A forward right thigh brings the left shoulder forward: positive
        // shoulder yaw, opposite the existing negative pelvis yaw. Distribute
        // the relative twist through the spine instead of hinging the chest.
        float shoulderYaw = drive * Mathf.Lerp(22f, 21f, sprintAmount) * effort * .5f - pelvisYaw * .75f;
        float relativeYaw = shoulderYaw - pelvisYaw;
        // Pitch the rib cage above the pelvis so the sprint can drive forward
        // without tipping the hip joints further into the support leg.
        float chestLean = Mathf.Lerp(7.5f, 15f, sprintAmount) * running * effort * (1 - stairWeight) * (1 - airPoseWeight);
        spine.rotation = Quaternion.AngleAxis(relativeYaw * .35f, transform.up) *
            Quaternion.AngleAxis(chestLean * .45f, transform.right) * spine.rotation;
        chest.rotation = Quaternion.AngleAxis(relativeYaw * .65f, transform.up) *
            Quaternion.AngleAxis(chestLean * .55f, transform.right) * chest.rotation;
        head.rotation = Quaternion.AngleAxis(-shoulderYaw * .85f, transform.up) *
            Quaternion.AngleAxis(-chestLean * .55f, transform.right) * head.rotation;
        torsoFrame = chest.rotation * chestToModel;
        armDrive = drive;
    }
    private float LegDrive(bool left) => left ? armDrive : -armDrive;
    private void PoseArm(Limb arm, bool left, float dt)
    {
        // Follow the cross-body drive already used for the torso.
        float drive = LegDrive(left) * locomotion * StrideEffort * Mathf.Lerp(1, .52f, stairWeight);
        // The upper arm must pass the ribs before the forearm recovers.
        // Small shoulder travel plus a bent elbow kept both wrists in front.
        float sprintCarry = 8 * sprintAmount * locomotion;
        float targetFlex = 2 * (1 - locomotion) + sprintCarry + 40 * (drive - .12f * drive * drive);
        targetFlex -= 7 * Smooth(0, .55f, -drive) * LevelRunWeight;
        ref float flex = ref (left ? ref leftShoulderFlex : ref rightShoulderFlex);
        ref float flexVelocity = ref (left ? ref leftShoulderVelocity : ref rightShoulderVelocity);
        ref float elbow = ref (left ? ref leftElbowFlex : ref rightElbowFlex);
        ref float elbowVelocity = ref (left ? ref leftElbowVelocity : ref rightElbowVelocity);
        // Let the elbow open as the shoulder drives back. The sprint carries
        // a tighter bend while the arm comes forward to balance the longer step.
        float swingBend = Mathf.Lerp(54, 68, sprintAmount) + drive * Mathf.Lerp(38, 29, sprintAmount);
        float backswing = Mathf.Max(0, -drive);
        swingBend -= backswing * backswing * 18 * sprintAmount;
        float targetElbow = Mathf.Lerp(12 + 8 * locomotion, swingBend, running * locomotion);
        targetElbow = Mathf.Lerp(targetElbow, 30 + drive * 15, stairWeight);
        float preparation = motor != null ? motor.JumpPreparation : 0;
        float driveUp = Smooth(.52f, 1, preparation);
        targetFlex = Mathf.Lerp(targetFlex, airNormalJump ? 38 : 48, jumpBrace);
        targetElbow = Mathf.Lerp(targetElbow, airNormalJump ? 27 : 69, jumpBrace);
        if (preparation > 0)
        {
            float windup = Smooth(0, .38f, preparation);
            targetFlex = Mathf.Lerp(targetFlex, Mathf.Lerp(-38, 122, driveUp), windup);
            targetElbow = Mathf.Lerp(targetElbow, Mathf.Lerp(17, 48, driveUp), windup);
        }
        float airFlex = Mathf.Lerp(Mathf.Lerp(69, 126, airReach), 42, airLanding);
        float airBend = Mathf.Lerp(Mathf.Lerp(61, 26, airReach), 66, airLanding);
        float airSpread = Mathf.Lerp(Mathf.Lerp(56, 30, airReach), 25, airLanding);
        if (airNormalJump && airPoseWeight > 0)
        {
            // Hands rise together as the legs extend, peak just after launch,
            // then travel down and outward before the feet take the load.
            float crest = Smooth(0, .09f, airborneSeconds) * (1 - Smooth(.12f, .43f, airborneSeconds));
            float lowerHands = groundedLast ? 1 : Mathf.Max(airLanding, Smooth(.18f, .42f, airborneSeconds));
            airFlex = Mathf.Lerp(Mathf.Lerp(119, 147, crest), 29, lowerHands);
            airBend = Mathf.Lerp(43, 26, lowerHands);
            airSpread = Mathf.Lerp(Mathf.Lerp(5, -8, crest), 33, lowerHands);
            float launch = groundedLast ? 0 : 1 - Smooth(.05f, .17f, airborneSeconds);
            targetFlex = Mathf.Lerp(targetFlex, airFlex, launch);
            targetElbow = Mathf.Lerp(targetElbow, airBend, launch);
        }
        targetFlex = Mathf.Lerp(targetFlex, airFlex, airPoseWeight);
        targetElbow = Mathf.Lerp(targetElbow, airBend, airPoseWeight);
        if (dt > 0)
        {
            // The shared torso/arm signal is already smooth. Filtering the
            // shoulder again delayed it relative to the thighs and chest.
            flexVelocity = (targetFlex-flex) / dt; flex = targetFlex;
            float response = Mathf.Clamp(1 / Mathf.Max(1, cadence * 15), .025f, .055f);
            elbow = Mathf.SmoothDamp(elbow, targetElbow, ref elbowVelocity, response, 700, dt);
        }
        else { flex = targetFlex; elbow = targetElbow; }
        float abduct = Mathf.Lerp(Mathf.Lerp(20, 12, locomotion), airSpread, airPoseWeight);
        Transform shoulder = left ? leftShoulder : rightShoulder;
        if (shoulder != null) shoulder.rotation = Quaternion.AngleAxis((left ? 1 : -1) * flex * .08f * locomotion, torsoFrame * Vector3.up) *
            Quaternion.AngleAxis(flex * .10f * locomotion, torsoFrame * Vector3.right) * shoulder.rotation;
        arm.PoseDirections(torsoFrame * ArmDirection(left, flex, abduct, airPoseWeight),
            torsoFrame * ArmDirection(left, flex + Mathf.Clamp(elbow, 8, 90), abduct, airPoseWeight));
        arm.end.localRotation = arm.restLocalRotation * Quaternion.AngleAxis(Mathf.Clamp(-flexVelocity * .015f, -5, 5), Vector3.right);
    }
    private void UpdateArms(float dt, float moving, bool grounded)
    {
        float swing = LegDrive(false) * moving;
        PoseArm(leftArm, true, dt); PoseArm(rightArm, false, dt);
        Vector3 rightGoal = rightArm.end.position, leftGoal = leftArm.end.position;
        Quaternion rightRotation = rightArm.end.rotation, leftRotation = leftArm.end.rotation;
        float grip = 0, guide = 0;
        if (sword.IsDrawn || action)
        {
            float phase = action ? actionTime / swordActionSeconds : 1;
            if (sheathing && action) phase = 1 - phase;
            float engage = Smooth(0, .20f, phase), clear = Smooth(.82f, .98f, phase);
            Vector3 idleOrigin = transform.InverseTransformPoint(sword.sheathSocket.position) - bodyOffset;
            Vector3 idleAxis = transform.InverseTransformDirection(sword.sheathSocket.up).normalized;
            Vector3 idleMouth = idleOrigin + idleAxis * (.16f * sword.swordScale);
            Vector3 drawAxis = new Vector3(-1, -.025f, .06f).normalized;
            Vector3 mouth = Vector3.Lerp(idleMouth, new Vector3(-.08f, 1.025f, .235f), engage);
            mouth += Vector3.left * Mathf.Lerp(0, .14f, Mathf.InverseLerp(.20f, .76f, phase));
            Vector3 axis = Vector3.Slerp(idleAxis, drawAxis, engage).normalized;
            Quaternion sheathRotation = BladeRotation(axis);
            Vector3 sheathOrigin = mouth - axis * (.16f * sword.swordScale);
            sword.sheathSocket.SetPositionAndRotation(PosePoint(Vector3.Lerp(sheathOrigin, idleOrigin, clear)),
                Quaternion.Slerp(transform.rotation * sheathRotation, sword.sheathSocket.rotation, clear));
            float extraction = Smooth(.20f, .78f, phase) * .51f;
            Vector3 palm = mouth - drawAxis * ((.16f - .10f) * sword.swordScale + extraction);
            if (phase < .20f) palm += new Vector3(.03f, .065f, .16f) * Mathf.Sin(Mathf.PI * engage);
            palm = Vector3.Lerp(palm, new Vector3(0, 1.035f, .31f), clear);
            Quaternion blade = Quaternion.Slerp(BladeRotation(drawAxis) * Quaternion.AngleAxis(65, Vector3.up),
                BladeRotation(new Vector3(0, .995f, .10f).normalized) * Quaternion.AngleAxis(45, Vector3.up), clear);
            blade = Quaternion.Slerp(sheathRotation, blade, Smooth(.12f, .30f, phase));
            Vector3 origin = palm - blade * Vector3.up * (.10f * sword.swordScale);
            Quaternion carryBlade = BladeRotation(new Vector3(0, -.16f, .987f).normalized) * Quaternion.AngleAxis(-50, Vector3.up);
            // Carry has no external hand contact: let the blade follow the
            // forearm, with a limited wrist adjustment towards the low guard.
            // A world-fixed blade made IK re-bend an otherwise natural arm.
            Quaternion naturalCarry = Quaternion.Inverse(transform.rotation) * rightArm.end.rotation * sword.handSocket.localRotation;
            carryBlade = Quaternion.RotateTowards(naturalCarry, carryBlade, 30);
            Quaternion carryHand = transform.rotation * carryBlade * Quaternion.Inverse(sword.handSocket.localRotation);
            Vector3 carryOrigin = transform.InverseTransformPoint(rightArm.end.position + carryHand * Vector3.Scale(sword.handSocket.localPosition, rightArm.end.lossyScale)) - bodyOffset;
            origin = Vector3.Lerp(origin, carryOrigin, carry);
            blade = Quaternion.Slerp(blade, carryBlade, carry);
            origin = Vector3.Lerp(sheathOrigin, origin, Smooth(.12f, .24f, phase));
            Quaternion handRotation = transform.rotation * blade * Quaternion.Inverse(sword.handSocket.localRotation);
            Vector3 socketOffset = Vector3.Scale(sword.handSocket.localPosition, rightArm.end.lossyScale);
            rightGoal = Vector3.Lerp(rightGoal, PosePoint(origin) - handRotation * socketOffset, engage);
            rightRotation = Quaternion.Slerp(rightRotation, handRotation, engage);
            grip = engage;
            Vector3 sheathPalm = PosePoint(mouth - drawAxis * .08f);
            Quaternion guideRotation = transform.rotation * BladeRotation(-axis) * leftWristFromSword;
            Vector3 readyPalm = PosePoint(origin + blade * Vector3.up * (-.04f * sword.swordScale));
            Vector3 leftPalm = Vector3.Lerp(sheathPalm, readyPalm, clear);
            Quaternion readyLeft = transform.rotation * BladeRotation(blade * Vector3.up) * Quaternion.AngleAxis(-50, Vector3.up) * leftWristFromSword;
            Quaternion goalLeft = Quaternion.Slerp(guideRotation, readyLeft, clear);
            guide = engage * (1 - carry);
            leftGoal = Vector3.Lerp(leftGoal, leftPalm - goalLeft * leftPalmOffset, guide);
            leftRotation = Quaternion.Slerp(leftRotation, goalLeft, guide);
        }
        float response = action ? .055f : limbResponse;
        // Gesture damping lives in the model frame. World-space caches fold
        // the arms through the torso when the player turns or changes gravity.
        rightWrist = Vector3.SmoothDamp(rightWrist, transform.InverseTransformPoint(rightGoal) - bodyOffset, ref rightWristVelocity, response, 20, dt);
        leftWrist = Vector3.SmoothDamp(leftWrist, transform.InverseTransformPoint(leftGoal) - bodyOffset, ref leftWristVelocity, response, 20, dt);
        rightWristRotation = Quaternion.Slerp(rightWristRotation, Quaternion.Inverse(transform.rotation) * rightRotation, 1 - Mathf.Exp(-2 * dt / response));
        leftWristRotation = Quaternion.Slerp(leftWristRotation, Quaternion.Inverse(transform.rotation) * leftRotation, 1 - Mathf.Exp(-2 * dt / response));
        Vector3 rightReadyPole = rightArm.upper.position + transform.right * .30f - transform.up * .20f + transform.forward * .20f;
        // Preserve the shoulder-driven elbow when carrying the sword instead
        // of pinning its IK bend plane behind one fixed point beside the ribs.
        Vector3 rightCarryPole = rightArm.lower.position;
        Vector3 rightFreePole = rightArm.upper.position + transform.right * .25f - transform.up * .20f + transform.forward * (.03f + swing * .06f);
        Vector3 leftFreePole = leftArm.upper.position - transform.right * .25f - transform.up * .20f + transform.forward * (.03f - swing * .06f);
        Vector3 rightPole = Vector3.Lerp(rightFreePole,
            Vector3.Lerp(rightReadyPole, rightCarryPole, carry), grip);
        Vector3 leftPole = Vector3.Lerp(leftFreePole,
            leftArm.upper.position - transform.right * .25f - transform.up * .24f + transform.forward * .15f, guide);
        if (!grounded) { rightPole += transform.right * .08f; leftPole -= transform.right * .08f; }
        // Gesture damping is useful for reaching the sheath, but applying it
        // again to the already coordinated carry arm suppresses its stride.
        rightArm.Solve(Vector3.Lerp(transform.TransformPoint(rightWrist + bodyOffset), rightGoal, carry),
            Quaternion.Slerp(transform.rotation * rightWristRotation, rightRotation, carry), rightPole, dt, 1, grip);
        leftArm.Solve(transform.TransformPoint(leftWrist + bodyOffset), transform.rotation * leftWristRotation, leftPole, dt, 1, guide);
        rightGrip = grip; leftGrip = guide;
    }
    private float rightGrip, leftGrip;
    private void ApplyFingers()
    {
        foreach (var finger in fingers)
        {
            float grip = finger.left ? leftGrip : rightGrip;
            grip = Mathf.Max(grip, .65f * Mathf.Max(airPoseWeight, jumpBrace));
            float rest = finger.relaxed + locomotion * 6 + running * locomotion * 18 + Mathf.Sin(poseTime * 1.4f) * .7f * (1 - locomotion);
            float curl = Mathf.Lerp(rest, finger.closed, grip);
            finger.bone.localRotation = finger.bind * Quaternion.AngleAxis(finger.opposition * grip, finger.oppositionAxis) *
                Quaternion.AngleAxis(curl, finger.curlAxis);
        }
    }
    private void OnDisable()
    {
        if (!IsInitialized) return;
        RestorePose(); transform.SetLocalPositionAndRotation(modelLocalPosition, modelLocalRotation);
        sword.sheathSocket.SetLocalPositionAndRotation(sheathLocalPosition, sheathLocalRotation);
        IsInitialized = false;
        if (motor != null && motor.Motor != null) motor.Motor.useTreadLanding = false;
        if (motor != null) motor.ProceduralGroundBraking = false;
        if (motor != null)
        {
            motor.GroundedJumpWindup = 0;
            motor.StairSpeedLimit = 0;
        }
        if (sword != null) sword.ReleaseProceduralControl(false);
    }
#if UNITY_EDITOR
    public string DescribeGait()
    {
        string FootState(Foot foot, Limb leg) => $"{foot.stage} planted={foot.planted} balance={foot.progress:F3}/{foot.stepDuration:F3} support={foot.support != null} swing={foot.swingElapsed:F3}/{foot.swingDuration:F3} contact={foot.contactElapsed:F3}/{foot.contactSeconds:F3} rawHipDelta={Quaternion.Inverse(StrideFrame(actor.up)) * (foot.requested - leg.upper.position):F3} start={foot.swingStartLocal:F3} velocity={foot.swingStartVelocity:F3} ground={foot.terrain:F3} takeoffDelta={foot.takeoffDelta:F3} reach={foot.takeoffReach:F3} phase={foot.takeoffProgress:F3} landingMiss={foot.landingFootError:F3}/reach={foot.landingReachError:F3}";
        return "L " + FootState(leftFoot, leftLeg) + "; R " + FootState(rightFoot, rightLeg);
    }
    private void OnDrawGizmosSelected()
    {
        if (!drawGaitDiagnostics || !IsInitialized) return;
        for (int i = 0; i < 2; i++)
        {
            Foot foot = i == 0 ? leftFoot : rightFoot;
            Limb leg = i == 0 ? leftLeg : rightLeg;
            Gizmos.color = foot.planted ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(foot.position, .035f);
            Gizmos.DrawLine(leg.upper.position, foot.position);
            Gizmos.DrawLine(leg.end.position, foot.position);
            if (foot.planted) Gizmos.DrawRay(foot.groundPoint, foot.normal * .18f);
            UnityEditor.Handles.Label(foot.position + transform.up * .08f,
                $"{(i == 0 ? "L" : "R")} {foot.stage} {foot.phase:F2}  load {foot.load:F2}");
        }
        Vector3 balance = pelvis.position + transform.up * .12f;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(balance, .045f);
        Gizmos.DrawLine(balance, balance - transform.up * Vector3.Dot(balance - actor.position, transform.up));
        UnityEditor.Handles.Label(balance, $"Balance centre (approx.)  phase {GaitPhase:F2}");
    }
#endif
}
