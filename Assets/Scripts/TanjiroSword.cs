using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Animation events transfer one katana between its hip and hand sockets.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Animator))]
public sealed class TanjiroSword : MonoBehaviour
{
    public RuntimeAnimatorController actionController;
    public Transform sword, handSocket, sheathSocket;
    public Mesh fullSwordMesh, stowedHiltMesh, drawableBodyMesh;
    public SkinnedMeshRenderer body;
    public Transform sheath;
    [HideInInspector] public Vector3 sheathPosition;
    [HideInInspector] public Quaternion sheathRotation = Quaternion.identity;
    [HideInInspector] public Vector3 rightWrist,leftWrist,rightElbow,leftElbow;
    [HideInInspector] public Quaternion rightWristRotation=Quaternion.identity,leftWristRotation=Quaternion.identity;
    [HideInInspector] public float handIKWeight;
    public int IKCalls { get; private set; }
    public float GoalSpaceOffsetDegrees { get; private set; }
    [Tooltip("Use the approved one-handed sword carry while running or sprinting with the katana drawn.")]
    public bool enableSwordLocomotion = true;
    [Min(.1f)] public float swordScale = .58f;
    public bool IsDrawn { get; private set; }
    public bool IsBusy { get; private set; }
    private Animator animator;
    private MeshFilter swordFilter;
    private int layer = -1;
    private int locomotionLayer = -1;
    private int freeArmLayer = -1;
    private bool initialized;
    private StarterAssets.ThirdPersonController movement;
    private bool pendingSheathe;
    private static readonly int StrideId=Animator.StringToHash("SwordStride");
    private static readonly int SpeedId=Animator.StringToHash("Speed");
    private Vector3 idleSheathPosition;
    private Quaternion idleSheathRotation;
    private Quaternion rightGoalRotation,leftGoalRotation;
    private Vector3 rightGoalPosition,leftGoalPosition;
    private bool calibrating;
    private Vector3 calibrationRightPosition,calibrationLeftPosition;
    private Quaternion calibrationRightRotation,calibrationLeftRotation;
    private ArmFrame rightArm;
    private Transform chest,hips,freeUpper,freeLower;
    private float idleFreeElbowAngle;

    // A fixed bend plane avoids Humanoid IK choosing the opposite elbow/twist
    // branch when the sword grip passes close to a shoulder during a stride.
    private sealed class ArmFrame
    {
        public Transform upper,lower,hand;
        private Quaternion upperBasis,lowerBasis;
        public ArmFrame(Animator animator,bool left)
        {
            var upperId=left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            var lowerId=left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            var handId=left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            upper=animator.GetBoneTransform(upperId);lower=animator.GetBoneTransform(lowerId);hand=animator.GetBoneTransform(handId);
            Vector3 normal=Vector3.Cross(lower.position-upper.position,hand.position-lower.position).normalized;
            upperBasis=Quaternion.Inverse(Quaternion.Inverse(upper.rotation)*Quaternion.LookRotation(lower.position-upper.position,normal));
            lowerBasis=Quaternion.Inverse(Quaternion.Inverse(lower.rotation)*Quaternion.LookRotation(hand.position-lower.position,normal));
        }
        public void Solve(Vector3 wrist,Quaternion rotation,Vector3 hint,float weight)
        {
            Vector3 start=upper.position;float a=Vector3.Distance(start,lower.position),b=Vector3.Distance(lower.position,hand.position);
            Vector3 direction=(wrist-start).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(start,wrist),Mathf.Abs(a-b)+.001f,a+b-.001f);
            Vector3 pole=Vector3.ProjectOnPlane(hint-start,direction).normalized;
            float along=(a*a+distance*distance-b*b)/(2*distance);
            Vector3 elbow=start+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            Vector3 end=start+direction*distance,normal=Vector3.Cross(elbow-start,end-elbow).normalized;
            upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.LookRotation(elbow-start,normal)*upperBasis,weight);
            lower.rotation=Quaternion.Slerp(lower.rotation,Quaternion.LookRotation(end-elbow,normal)*lowerBasis,weight);
            hand.rotation=Quaternion.Slerp(hand.rotation,rotation,weight);
        }
    }

    private void Awake()
    {
        if (Application.isPlaying && GetComponentInParent<StarterAssets.ThirdPersonController>() != null) Initialize();
    }

    public void Initialize()
    {
        if (initialized) return;
        animator = GetComponent<Animator>();
        if (actionController == null || sword == null || handSocket == null || sheathSocket == null || body == null ||
            fullSwordMesh == null || stowedHiltMesh == null || drawableBodyMesh == null) return;
        initialized = true;
        movement = GetComponentInParent<StarterAssets.ThirdPersonController>();
        animator.runtimeAnimatorController = actionController;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        layer = animator.GetLayerIndex("Sword Actions");
        locomotionLayer = animator.GetLayerIndex("Sword Locomotion");
        freeArmLayer = animator.GetLayerIndex("Sword Free Arm");
        if(locomotionLayer>=0) animator.SetLayerWeight(locomotionLayer,0);
        if(freeArmLayer>=0) animator.SetLayerWeight(freeArmLayer,0);
        swordFilter = sword.GetComponentInChildren<MeshFilter>();
        body.sharedMesh = drawableBodyMesh;
        sword.gameObject.SetActive(true);
        if (sheath != null) sheath.gameObject.SetActive(true);
        sheathPosition = transform.InverseTransformPoint(sheathSocket.position); sheathRotation = Quaternion.Inverse(transform.rotation)*sheathSocket.rotation;
        idleSheathPosition = sheathPosition; idleSheathRotation = sheathRotation;
        // Measure the Avatar's actual solved effector frame, not the source
        // animation's hand-T goals (which can differ after retargeting).
        handIKWeight=0;animator.Rebind();animator.Play("Idle Walk Run Blend",0,0);
        animator.SetLayerWeight(layer,1);animator.Play("Sword Actions.Stowed",layer,0);calibrating=true;animator.Update(0);
        void SolvedOffset(HumanBodyBones bone,Vector3 position,Quaternion rotation,out Vector3 offset,out Quaternion orientation)
        { var hand=animator.GetBoneTransform(bone);offset=Quaternion.Inverse(hand.rotation)*(position-hand.position);orientation=Quaternion.Inverse(hand.rotation)*rotation; }
        SolvedOffset(HumanBodyBones.RightHand,calibrationRightPosition,calibrationRightRotation,out rightGoalPosition,out rightGoalRotation);
        SolvedOffset(HumanBodyBones.LeftHand,calibrationLeftPosition,calibrationLeftRotation,out leftGoalPosition,out leftGoalRotation);
        GoalSpaceOffsetDegrees=Quaternion.Angle(Quaternion.identity,rightGoalRotation);
        calibrating=false;animator.Rebind();ResetSword();animator.Update(0);
        // The temporary IK calibration deliberately changes the hand frames.
        // Capture arm bend/twist bases only after returning to the natural idle.
        rightArm=new ArmFrame(animator,false);
        chest=animator.GetBoneTransform(HumanBodyBones.Chest);hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        freeUpper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);freeLower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Vector3 idleArm=freeLower.position-freeUpper.position;
        idleFreeElbowAngle=Mathf.Atan2(Vector3.Dot(idleArm,transform.forward),-Vector3.Dot(idleArm,transform.up))*Mathf.Rad2Deg;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ToggleSword();
#endif
        if (initialized && layer >= 0)
        {
            UpdateLocomotion(Time.deltaTime);
        }
    }

    // Use the motor's smoothed speed, not the sprint button alone. This also
    // works with the phone joystick and with the character's local gravity.
    public void UpdateLocomotion(float dt)
    {
        if (!initialized || dt<=0) return;
        float target=0;
        if (enableSwordLocomotion && IsDrawn && !IsBusy && movement!=null && movement.enabled && movement.Grounded)
        {
            float speed=animator.GetFloat(SpeedId);
            target=speed<=movement.MoveSpeed ? Mathf.InverseLerp(.15f,Mathf.Max(.16f,movement.MoveSpeed),speed) :
                1+Mathf.InverseLerp(movement.MoveSpeed,Mathf.Max(movement.MoveSpeed+.01f,movement.SprintSpeed),speed);
        }
        float stride=Mathf.MoveTowards(animator.GetFloat(StrideId),target,dt*2);
        animator.SetFloat(StrideId,stride);
        // Native masked layers carry the sword and restore the free arm from
        // the base layer's synchronized gait, without a second Animator.
        float carry=LocomotionWeight;
        if(locomotionLayer>=0) animator.SetLayerWeight(locomotionLayer,carry);
        if(freeArmLayer>=0) animator.SetLayerWeight(freeArmLayer,carry);
        float actionWeight=IsBusy || IsDrawn ? 1 : 0;
        animator.SetLayerWeight(layer,Mathf.MoveTowards(animator.GetLayerWeight(layer),actionWeight,dt*8));
        if (pendingSheathe && stride<=.001f)
        {
            pendingSheathe=false;
            animator.CrossFadeInFixedTime("Sword Actions.Sheathe",.15f,layer);
        }
    }

    public void ToggleSword()
    {
        Initialize();
        if (!initialized || layer < 0 || IsBusy || !isActiveAndEnabled || !animator.isActiveAndEnabled) return;
        IsBusy = true;
        // Bring the trailing blade back to the guard before reversing the draw.
        if (IsDrawn && animator.GetFloat(StrideId)>.001f) { pendingSheathe=true;return; }
        animator.CrossFadeInFixedTime("Sword Actions." + (IsDrawn ? "Sheathe" : "Draw"), .12f, layer);
    }

    public void OnSwordDrawGrip() => Mount(handSocket, fullSwordMesh);
    public void OnSwordDrawFinished() { IsDrawn = true; IsBusy = false; }
    public void OnSwordSheatheGrip() => Mount(sheathSocket, stowedHiltMesh);
    public void OnSwordSheatheFinished() { IsDrawn = false; IsBusy = false; }

    public void ApplySheathPose()
    {
        if (!initialized || sheathSocket == null) return;
        sheathSocket.SetPositionAndRotation(transform.TransformPoint(sheathPosition),transform.rotation*sheathRotation.normalized);
        float trailing=TrailingWeight;
        if(trailing>0)
        {
            // Counter-swing the whole chain about the shoulder, so the upper
            // arm moves too. The wrist partially levels the carried blade.
            float angle=CarrySwingAngle;
            Quaternion swing=Quaternion.AngleAxis(angle,transform.right);
            Vector3 shoulder=rightArm.upper.position;
            Vector3 wrist=shoulder+swing*(transform.TransformPoint(rightWrist)-shoulder);
            Vector3 hint=shoulder+swing*(transform.TransformPoint(RunningElbow)-shoulder);
            Quaternion grip=Quaternion.AngleAxis((angle-CarryRearAngle)*.20f,transform.right)*transform.rotation*rightWristRotation.normalized;
            rightArm.Solve(wrist,grip,hint,trailing);
        }
    }
    private float LocomotionWeight => IsDrawn && !IsBusy || pendingSheathe ? Mathf.SmoothStep(0,1,animator.GetFloat(StrideId)) : 0;
    // The checked, lowered elbow is the rear stop. Compensate for the more
    // upright run torso; all of the gait's movement goes forward from here.
    private float CarryRearAngle => Mathf.Lerp(-23,0,Mathf.Clamp01(animator.GetFloat(StrideId)-1))*LocomotionWeight;
    private float CarrySwingAngle
    {
        get
        {
            float weight=LocomotionWeight;
            if(weight<=0 || chest==null) return 0;
            Vector3 up=(chest.position-hips.position).normalized;
            Vector3 forward=Vector3.ProjectOnPlane(transform.forward,up).normalized;
            Vector3 arm=freeLower.position-freeUpper.position;
            float gait=Mathf.Atan2(Vector3.Dot(arm,forward),-Vector3.Dot(arm,up))*Mathf.Rad2Deg;
            float delta=Mathf.DeltaAngle(idleFreeElbowAngle,gait);
            float forwardStroke=.5f-.5f*(float)System.Math.Tanh(delta/20);
            return CarryRearAngle-42*forwardStroke*weight;
        }
    }
    // Swing the bend hint around the outside, not through the arm's axis,
    // when moving from the forward guard to the rearward one-hand carry.
    private Vector3 RunningElbow => rightElbow+Vector3.right*(.30f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(animator.GetFloat(StrideId))));
    private float TrailingWeight => IsDrawn && !IsBusy || pendingSheathe ? Mathf.SmoothStep(0,1,animator.GetFloat(StrideId)/.3f) : 0;
    private void LateUpdate() { if (Application.isPlaying) ApplySheathPose(); }
    private void OnAnimatorIK(int layerIndex)
    {
        if (!initialized || layerIndex != layer) return;
        IKCalls++;
        if (calibrating)
        {
            void Goal(AvatarIKGoal goal,HumanBodyBones bone,out Vector3 position,out Quaternion rotation)
            {
                var hand=animator.GetBoneTransform(bone);position=hand.position;rotation=hand.rotation;
                animator.SetIKPositionWeight(goal,1);animator.SetIKRotationWeight(goal,1);
                animator.SetIKPosition(goal,position);animator.SetIKRotation(goal,rotation);
            }
            Goal(AvatarIKGoal.RightHand,HumanBodyBones.RightHand,out calibrationRightPosition,out calibrationRightRotation);
            Goal(AvatarIKGoal.LeftHand,HumanBodyBones.LeftHand,out calibrationLeftPosition,out calibrationLeftRotation);
            return;
        }
        void Hand(AvatarIKGoal goal,AvatarIKHint hint,Vector3 wrist,Quaternion rotation,Vector3 elbow)
        {
            float weight=Mathf.Clamp01(handIKWeight);
            float trailing=TrailingWeight;
            bool right=goal==AvatarIKGoal.RightHand;
            float release=right ? trailing : LocomotionWeight;
            animator.SetIKPositionWeight(goal,weight*(1-release));
            // Both hands use baked, continuous wrist targets. The guide hand
            // blends into the lower handle grip only after the blade is clear.
            animator.SetIKRotationWeight(goal,weight*(1-release));
            Quaternion boneRotation=transform.rotation*rotation.normalized;
            animator.SetIKPosition(goal,transform.TransformPoint(wrist)+boneRotation*(right ? rightGoalPosition : leftGoalPosition));
            animator.SetIKRotation(goal,boneRotation*(right ? rightGoalRotation : leftGoalRotation));
            animator.SetIKHintPositionWeight(hint,weight*(1-release));animator.SetIKHintPosition(hint,transform.TransformPoint(elbow));
        }
        Hand(AvatarIKGoal.RightHand,AvatarIKHint.RightElbow,rightWrist,rightWristRotation,rightElbow);
        Hand(AvatarIKGoal.LeftHand,AvatarIKHint.LeftElbow,leftWrist,leftWristRotation,leftElbow);
    }

    private void Mount(Transform socket, Mesh mesh)
    {
        if (!initialized || socket == null) return;
        sword.SetParent(socket, false);
        sword.localPosition = Vector3.zero; sword.localRotation = Quaternion.identity;
        Vector3 scale = socket.lossyScale;
        sword.localScale = new Vector3(swordScale / Mathf.Abs(scale.x), swordScale / Mathf.Abs(scale.y), swordScale / Mathf.Abs(scale.z));
        swordFilter.sharedMesh = mesh;
    }

    public void ResetSword()
    {
        IsDrawn = IsBusy = false;
        pendingSheathe=false;
        handIKWeight=0;
        if (!initialized) return;
        animator.SetFloat(StrideId,0);
        sheathPosition = idleSheathPosition; sheathRotation = idleSheathRotation; ApplySheathPose();
        Mount(sheathSocket, stowedHiltMesh);
        if (layer >= 0) { animator.SetLayerWeight(layer, 0); animator.Play("Sword Actions.Stowed", layer, 0); }
        if (locomotionLayer>=0) animator.SetLayerWeight(locomotionLayer,0);
        if (freeArmLayer>=0) animator.SetLayerWeight(freeArmLayer,0);
    }
    private void OnDisable() => ResetSword();
}
