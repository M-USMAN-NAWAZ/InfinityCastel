using System;
using System.IO;
using System.Linq;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class TanjiroProceduralSetup
{
    public const string ScenePath = "Assets/Scenes/Playground 1.unity";
    private const string OriginalScene = "Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity";
    private const string PlayerPrefab = "Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab";

    // Calibrate geometry and joint frames from the skin bind matrices. No
    // animation clip, Humanoid pose playback or motion samples are read here.
    public static TanjiroProceduralBody Configure(TanjiroSword sword)
    {
        var a = sword.GetComponent<Animator>();
        a.enabled = false; a.runtimeAnimatorController = null;
        sword.InitializeProcedural(() => { });
        TanjiroJointRigSetup.Upgrade(sword);
        Transform Bone(string name) => sword.body.bones.First(b => b.name == name);
        // This FBX's bind skeleton faces -Z. Canonicalize its root from the
        // actual head frame so Left/Right and forward mean the same as the motor.
        Vector3 bindForward = Vector3.ProjectOnPlane(Bone("Head").forward, sword.transform.up).normalized;
        Quaternion align = Quaternion.AngleAxis(Vector3.SignedAngle(bindForward, sword.transform.forward, sword.transform.up), sword.transform.up);
        Transform rigRoot = Bone("Root");
        rigRoot.SetPositionAndRotation(sword.transform.position + align * (rigRoot.position - sword.transform.position), align * rigRoot.rotation);
        var body = sword.GetComponent<TanjiroProceduralBody>() ?? sword.gameObject.AddComponent<TanjiroProceduralBody>();
        body.pelvis = Bone("Hips"); body.spine = Bone("Spine"); body.chest = Bone("Chest"); body.head = Bone("Head");
        body.leftShoulder = Bone("LeftShoulder"); body.rightShoulder = Bone("RightShoulder");
        body.leftToe = Bone("LeftToes"); body.rightToe = Bone("RightToes");
        body.leftToeBind = body.leftToe.localRotation; body.rightToeBind = body.rightToe.localRotation;
        body.leftToeAxis = body.leftToe.InverseTransformDirection(body.transform.right);
        body.rightToeAxis = body.rightToe.InverseTransformDirection(body.transform.right);
        TanjiroProceduralBody.Limb Limb(string side, string upper, string lower, string end)
        {
            var limb = new TanjiroProceduralBody.Limb { upper = Bone(side + upper), lower = Bone(side + lower), end = Bone(side + end) };
            // Elbows flex the forearm forward; knees fold the shin backward.
            // Their anatomical bend planes therefore have opposite signs.
            limb.Calibrate(a.transform, a.transform.forward * (upper == "UpperArm" ? 1 : -1)); return limb;
        }
        body.leftLeg = Limb("Left", "UpperLeg", "LowerLeg", "Foot");
        body.rightLeg = Limb("Right", "UpperLeg", "LowerLeg", "Foot");
        body.coatPanels = new[] { "CoatBack", "CoatLeft", "CoatRight" }.Select(name =>
            new TanjiroProceduralBody.CoatPanel {
                root = Bone(name + "Root"), hem = Bone(name + "Hem"),
                bindDown = Bone(name + "Root").InverseTransformDirection(-body.transform.up),
                back = name == "CoatBack", left = name == "CoatLeft"
            }).ToArray();
        body.leftArm = Limb("Left", "UpperArm", "LowerArm", "Hand");
        body.rightArm = Limb("Right", "UpperArm", "LowerArm", "Hand");
        var fingers = new System.Collections.Generic.List<TanjiroProceduralBody.FingerJoint>();
        var twists = new System.Collections.Generic.List<TanjiroProceduralBody.TwistJoint>();
        (Vector3 point, Quaternion rotation) Grip(bool left)
        {
            string side = left ? "Left" : "Right"; var hand = Bone(side + "Hand");
            Vector3 index = Bone(side + "IndexProximal").position, little = Bone(side + "LittleProximal").position;
            Vector3 forward = ((index + little) * .5f - hand.position).normalized, across = (index - little).normalized;
            Vector3 back = Vector3.Cross(forward, across) * (left ? 1 : -1);
            Quaternion frame = Quaternion.LookRotation(hand.InverseTransformDirection(back), hand.InverseTransformDirection(across));
            if (!left) frame *= Quaternion.AngleAxis(28, Vector3.forward);
            return (hand.InverseTransformPoint(Vector3.Lerp(hand.position, (index + little) * .5f, .65f)), frame);
        }
        foreach (bool left in new[] { false, true })
        {
            string side = left ? "Left" : "Right"; var arm = left ? body.leftArm : body.rightArm;
            Vector3 knuckles = (Bone(side + "IndexProximal").position + Bone(side + "LittleProximal").position) * .5f;
            arm.handForward = arm.end.InverseTransformDirection(knuckles - arm.end.position).normalized;
            Vector3 across = (Bone(side + "IndexProximal").position - Bone(side + "LittleProximal").position).normalized;
            Vector3 palm = -Vector3.Cross(knuckles - arm.end.position, across).normalized * (left ? 1 : -1);
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                string[] parts = { "Proximal", "Intermediate", "Distal" };
                var tip = Bone(side + finger + "Distal").Find(side + finger + "Tip");
                for (int i = 0; i < 3; i++)
                {
                    Transform joint = Bone(side + finger + parts[i]);
                    Vector3 along = (i < 2 ? Bone(side + finger + parts[i + 1]).position : tip.position) - joint.position;
                    fingers.Add(new TanjiroProceduralBody.FingerJoint { bone = joint, tip = tip, bind = joint.localRotation, left = left,
                        curlAxis = joint.InverseTransformDirection(Vector3.Cross(along.normalized, palm).normalized),
                        oppositionAxis = joint.InverseTransformDirection(palm), opposition = finger == "Thumb" && i == 0 ? (left ? -25 : 25) : 0,
                        relaxed = i == 0 ? 8 : i == 1 ? 14 : 9,
                        closed = finger == "Thumb" ? (i == 1 ? 55 : 40) : (i == 0 ? 52 : i == 1 ? 78 : 48) });
                }
            }
            foreach (bool forearm in new[] { false, true })
            {
                Transform source = forearm ? arm.lower : arm.upper, end = forearm ? arm.end : arm.lower;
                Vector3 axis = source.InverseTransformDirection(end.position - source.position).normalized;
                Transform driver = forearm ? arm.end : arm.upper;
                twists.Add(new TanjiroProceduralBody.TwistJoint { bone = Bone(side + (forearm ? "ForearmTwist" : "UpperArmTwist")),
                    driver = driver, driverBind = driver.localRotation, axis = axis,
                    driverAxis = driver.InverseTransformDirection(source.TransformDirection(axis)), amount = forearm ? .65f : -.4f });
            }
        }
        var actor = sword.GetComponentInParent<ThirdPersonController>().transform;
        body.leftLeg.soleHeight = Mathf.Clamp(Vector3.Dot(body.leftLeg.end.position - actor.position, actor.up), .02f, .16f);
        body.rightLeg.soleHeight = Mathf.Clamp(Vector3.Dot(body.rightLeg.end.position - actor.position, actor.up), .02f, .16f);
        var leftGrip = Grip(true); var rightGrip = Grip(false);
        body.leftPalmOffset = Vector3.Scale(leftGrip.point, body.leftArm.end.lossyScale);
        body.leftWristFromSword = Quaternion.Inverse(leftGrip.rotation);
        sword.handSocket.localRotation = rightGrip.rotation;
        sword.handSocket.localPosition = rightGrip.point - rightGrip.rotation * Vector3.up * (.10f * sword.swordScale / body.rightArm.end.lossyScale.x);
        body.fingers = fingers.ToArray(); body.twists = twists.ToArray();
        body.PoseNeutralArms();
        foreach (var arm in new[] { body.leftArm, body.rightArm })
        {
            arm.restEnd = body.transform.InverseTransformPoint(arm.end.position);
            arm.restPole = body.transform.InverseTransformPoint(arm.lower.position);
            arm.restRotation = Quaternion.Inverse(body.transform.rotation) * arm.end.rotation;
        }
        body.bones = sword.body.bones.Select(b => new TanjiroProceduralBody.BonePose {bone = b, position = b.localPosition, rotation = b.localRotation}).ToArray();
        sword.ReleaseProceduralControl();
        EditorUtility.SetDirty(body); EditorUtility.SetDirty(a);
        return body;
    }
    [MenuItem("Infinity Castle/Procedural Player/Install in Playground 1")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Install outside Play mode.");
        if (!File.Exists(ScenePath)) throw new FileNotFoundException("Save the new Playground 1 scene first.", ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sword = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TanjiroSword>(true))
            .FirstOrDefault(s => s.gameObject.activeInHierarchy && s.GetComponentInParent<ThirdPersonController>() != null);
        if (sword == null) throw new InvalidOperationException("Playground 1 has no active Tanjiro third-person player.");
        Configure(sword);
        // Persist only scene instance overrides; never apply these to the shared prefab.
        foreach (var component in sword.GetComponentsInParent<Transform>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        foreach (var component in sword.GetComponentsInChildren<Component>(true))
            if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }
    public static void BuildAndValidate()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run the isolated Procedural validation.");
        byte[] original = File.ReadAllBytes(OriginalScene), prefab = File.ReadAllBytes(PlayerPrefab);
        Install();
        if (!original.SequenceEqual(File.ReadAllBytes(OriginalScene)) || !prefab.SequenceEqual(File.ReadAllBytes(PlayerPrefab)))
            throw new InvalidOperationException("The original scene or player prefab changed.");
        Validate();
        EditorSceneManager.OpenScene(OriginalScene, OpenSceneMode.Single);
        TanjiroSwordReview.Run(false, false, false);
        TanjiroSwordReview.Run(false, false, true);
        File.AppendAllText(Path.GetFullPath("../procedural-player-tests.txt"),
            "PASS Original clip-based sword draw, sheath, run and sprint regression checks\n");
    }
    private static void Validate()
    {
        string output = Path.GetFullPath("../procedural-preview"); Directory.CreateDirectory(output);
        Directory.CreateDirectory(output + "/cycle");
        Directory.CreateDirectory(output + "/upper-front");
        using var cycleTrace = new StreamWriter(output + "/cycle-phases.csv") { AutoFlush = true };
        cycleTrace.WriteLine("mode,frame,leftStage,rightStage,leftContact,rightContact,phase,bodyLean,leftLoadPhase,rightLoadPhase");
        using var report = new StreamWriter(Path.GetFullPath("../procedural-player-tests.txt")) { AutoFlush = true };
        var failures = new System.Collections.Generic.List<string>();
        bool reviewTraversal = Environment.GetEnvironmentVariable("CASTLE_STAIR_JUMP_REVIEW") == "1";
        if (reviewTraversal) Directory.CreateDirectory(output + "/stair-jump");
        void Check(bool pass, string message)
        { report.WriteLine((pass ? "PASS " : "FAIL ") + message); if (!pass) failures.Add(message); }
        // Exercise the actual saved scene instance, including its 5/8.335 m/s
        // overrides. A slower prefab-only fixture missed the gameplay failure.
        var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var source = Object.FindFirstObjectByType<TanjiroProceduralBody>().GetComponentInParent<ThirdPersonController>().gameObject;
        var reviewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(reviewScene);
        var actor = Object.Instantiate(source);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, reviewScene);
        EditorSceneManager.CloseScene(originalScene, true);
        Physics.simulationMode = SimulationMode.Script;
        // Multiple manual camera renders in one editor tick are incompatible
        // with Forward+ binning jobs. Use Forward only in this isolated review;
        // this renderer asset is never copied back to the gameplay project.
        if (Application.isBatchMode)
        {
            var pipeline = new SerializedObject(GraphicsSettings.currentRenderPipeline);
            var renderers = pipeline.FindProperty("m_RendererDataList");
            for (int i = 0; renderers != null && i < renderers.arraySize; i++)
            {
                var renderer = new SerializedObject(renderers.GetArrayElementAtIndex(i).objectReferenceValue);
                var mode = renderer.FindProperty("m_RenderingMode");
                if (mode != null) { mode.intValue = 0; renderer.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }
        var camera = new GameObject("Procedural check camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.scene = reviewScene;
        camera.enabled = false; // Review views must not steer the movement test.
        camera.orthographic = true; camera.orthographicSize = 1.05f; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .09f, .11f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .7f;
        var light = new GameObject("Procedural check light").AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(35, 145, 0);
        GameObject Cube(string name, Vector3 position, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name;
            cube.transform.position = position; cube.transform.localScale = size; return cube;
        }
        var floor = Cube("Test floor", new Vector3(0, -.1f, 0), new Vector3(240, .2f, 240));
        var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        floorMaterial.SetColor("_BaseColor", new Color(.17f, .19f, .21f));
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        var markMaterial = new Material(floorMaterial); markMaterial.SetColor("_BaseColor", new Color(.32f, .35f, .38f));
        for (int mark = -10; mark < 120; mark++)
        {
            var stripe = Cube("Review distance marker", new Vector3(0, .001f, mark), new Vector3(240, .002f, .025f));
            Object.DestroyImmediate(stripe.GetComponent<Collider>()); stripe.GetComponent<Renderer>().sharedMaterial = markMaterial;
        }
        for (int step = 0; step < 9; step++)
            Cube("Tread " + step, new Vector3(0, (step + 1) * .17f * .5f, step * .55f + .275f), new Vector3(2.5f, (step + 1) * .17f, .55f)).layer = 31;
        // Match the game's separation: smooth capsule collision under rendered
        // stair treads, with the foot solver probing the actual tread surfaces.
        float incline = Mathf.Atan2(1.53f, 4.95f);
        var ramp = Cube("Motor stair ramp", new Vector3(0, .765f - .075f * Mathf.Cos(incline), 2.475f),
            new Vector3(2.5f, .15f, Mathf.Sqrt(1.53f * 1.53f + 4.95f * 4.95f)));
        ramp.transform.rotation = Quaternion.Euler(-incline * Mathf.Rad2Deg, 0, 0); ramp.GetComponent<Renderer>().enabled = false;
        bool ignoredTreads = Physics.GetIgnoreLayerCollision(actor.layer, 31);
        Physics.IgnoreLayerCollision(actor.layer, 31, true);
        actor.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);
        // The live castle adds this at startup; mirror that runtime dependency.
        if (actor.GetComponent<InfinityGravityBody>() == null) actor.AddComponent<InfinityGravityBody>();
        var sword = actor.GetComponentInChildren<TanjiroSword>(); var body = Configure(sword); body.Initialize();
        var motor = actor.GetComponent<ThirdPersonController>(); var input = actor.GetComponent<StarterAssetsInputs>();
        input.analogMovement = true; motor.GroundLayers = ~0;
        float sceneRunSpeed = motor.MoveSpeed, sceneSprintSpeed = motor.SprintSpeed;
        var armBones = new[] { body.rightArm.upper, body.rightArm.lower, body.rightArm.end, body.leftArm.upper, body.leftArm.lower, body.leftArm.end };
        float worst = 0, maxWrist = 0, heightDifference = 0, maxPelvis = 0, maxContactError = 0;
        int maxProbes = 0, contactSamples = 0, walkDoubleSupport = 0, runFlight = 0, offFloorRunSamples = 0;
        string stage = "stairs", worstAt = "", wristAt = "", contactAt = "";
        var strides = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector4>>();
        var runPoses = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector4>>();
        var torsoLeans = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<float>>();
        var bodyHeights = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector4>>();
        var supportPhases = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector2>>();
        var stanceMechanics = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector3>>();
        var armMechanics = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector4>>();
        var coordination = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector4>>();
        var unsupportedSeconds = new float[2];
        var longestUnsupported = new float[2];
        void Tick(int count, float dt = 1f / 60)
        {
            for (int frame = 0; frame < count; frame++)
            {
                var before = armBones.Select(b => b.localRotation).ToArray();
                motor.Simulate(dt); body.Simulate(dt); Physics.SyncTransforms(); Physics.Simulate(dt);
                for (int side = 0; side < 2; side++)
                {
                    bool supported = side == 0 ? body.LeftFootSupported : body.RightFootSupported;
                    bool continuousRun = motor.Grounded && input.move.sqrMagnitude > .01f && Vector3.ProjectOnPlane(motor.Motor.Velocity, actor.transform.up).magnitude > 3.5f;
                    unsupportedSeconds[side] = continuousRun && !supported ? unsupportedSeconds[side] + dt : 0;
                    longestUnsupported[side] = Mathf.Max(longestUnsupported[side], unsupportedSeconds[side]);
                }
                if (stage == "sprint" && (frame == 100 || frame == 140)) report.WriteLine("Sprint support diagnostic " + frame + ": " + body.DescribeGait());
                for (int i = 0; i < armBones.Length; i++)
                {
                    float change = Quaternion.Angle(before[i], armBones[i].localRotation);
                    if (change > worst) { worst = change; worstAt = stage + "/" + frame + "/" + armBones[i].name; }
                }
                var a = sword.GetComponent<Animator>();
                foreach (bool left in new[] { false, true })
                {
                    var hand = left ? body.leftArm.end : body.rightArm.end; var lower = left ? body.leftArm.lower : body.rightArm.lower;
                    Vector3 knuckles = (a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal).position +
                        a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal).position) * .5f;
                    float bend = Vector3.Angle(knuckles - hand.position, hand.position - lower.position);
                    if (bend > maxWrist)
                    {
                        maxWrist = bend; wristAt = stage + "/" + frame + (left ? "/Left" : "/Right");
                        if (bend > 85)
                        {
                            var upper = left ? body.leftArm.upper : body.rightArm.upper;
                            report.WriteLine($"Wrist limit: {wristAt}, bend={bend:F1}, shoulder={body.transform.InverseTransformPoint(upper.position):F3}, elbow={body.transform.InverseTransformPoint(lower.position):F3}, wrist={body.transform.InverseTransformPoint(hand.position):F3}, palm direction={body.transform.InverseTransformDirection((knuckles-hand.position).normalized):F3}");
                        }
                    }
                }
                maxProbes = Mathf.Max(maxProbes, body.ProbeCount);
                if (motor.Grounded && body.PlantedFeet > 0)
                {
                    if (body.PlantedFootError > maxContactError)
                    {
                        maxContactError = body.PlantedFootError; contactAt = stage + "/" + frame;
                        if (maxContactError > .005f) report.WriteLine($"Contact reach diagnostic at {actor.transform.position:F3}: {body.DescribeGait()}");
                    }
                    contactSamples++;
                }
                if (stage == "walk" && frame > 20 && body.PlantedFeet == 2) walkDoubleSupport++;
                if ((stage == "run" || stage == "sprint") && frame > 20 && body.PlantedFeet == 0) runFlight++;
                if ((stage == "walk" || stage == "run" || stage == "sprint") && frame > 60 && input.move.sqrMagnitude > .01f)
                {
                    if (!strides.TryGetValue(stage, out var samples)) strides[stage] = samples = new();
                    float Forward(Transform joint) => Vector3.Dot(joint.position - body.pelvis.position, actor.transform.forward);
                    samples.Add(new Vector4(Forward(body.leftLeg.end), Forward(body.rightLeg.end), Forward(body.leftArm.end), Forward(body.rightArm.end)));
                    if (stage == "run" || stage == "sprint")
                    {
                        if (!motor.Grounded) offFloorRunSamples++;
                        if (!runPoses.TryGetValue(stage, out var poses)) { runPoses[stage] = poses = new(); torsoLeans[stage] = new(); }
                        float Bend(TanjiroProceduralBody.Limb limb) => Vector3.Angle(limb.lower.position - limb.upper.position, limb.end.position - limb.lower.position);
                        float Thigh(TanjiroProceduralBody.Limb limb)
                        {
                            Vector3 d = limb.lower.position - limb.upper.position;
                            // Anatomical hip flexion excludes the body's ankle
                            // lean; world vertical would count that lean twice.
                            return Mathf.Atan2(Vector3.Dot(d, actor.transform.forward), -Vector3.Dot(d, actor.transform.up)) * Mathf.Rad2Deg - body.BodyLeanAngle * .90f;
                        }
                        poses.Add(new Vector4(Bend(body.leftArm), Mathf.Max(Bend(body.leftLeg), Bend(body.rightLeg)),
                            Mathf.Max(Thigh(body.leftLeg), Thigh(body.rightLeg)), Vector3.Dot(body.leftArm.end.position - body.pelvis.position, actor.transform.up)));
                        Vector3 torso = body.head.position - body.pelvis.position;
                        torsoLeans[stage].Add(Mathf.Atan2(Vector3.Dot(torso, actor.transform.forward), Vector3.Dot(torso, actor.transform.up)) * Mathf.Rad2Deg);
                        if (!bodyHeights.TryGetValue(stage, out var vertical)) bodyHeights[stage] = vertical = new();
                        vertical.Add(new Vector4(Vector3.Dot(body.pelvis.position - actor.transform.position, actor.transform.up),
                            Vector3.Dot(body.head.position - actor.transform.position, actor.transform.up), body.PlantedFeet, body.CurrentPelvisOffset));
                        if (!supportPhases.TryGetValue(stage, out var contactPhases)) supportPhases[stage] = contactPhases = new();
                        contactPhases.Add(new Vector2(body.LeftFootSupported ? body.LeftSupportProgress : -1, body.RightFootSupported ? body.RightSupportProgress : -1));
                        if (!stanceMechanics.TryGetValue(stage, out var mechanics)) stanceMechanics[stage] = mechanics = new();
                        foreach (bool left in new[] { true, false })
                        {
                            var arm = left ? body.leftArm : body.rightArm;
                            Vector3 upper = arm.lower.position - arm.upper.position;
                            float shoulder = Mathf.Atan2(Vector3.Dot(upper, actor.transform.forward), -Vector3.Dot(upper, actor.transform.up)) * Mathf.Rad2Deg + body.BodyLeanAngle;
                            string armKey = stage + (left ? "-left" : "-right");
                            if (!armMechanics.TryGetValue(armKey, out var armSamples)) armMechanics[armKey] = armSamples = new();
                            armSamples.Add(new Vector4(shoulder, Bend(arm), Vector3.Dot(arm.end.position-arm.upper.position, actor.transform.forward),
                                Vector3.Dot(arm.end.position-body.pelvis.position, actor.transform.right) * (left ? -1 : 1)));
                            if (!(left ? body.LeftFootSupported : body.RightFootSupported)) continue;
                            var leg = left ? body.leftLeg : body.rightLeg;
                            Vector3 thigh = leg.lower.position - leg.upper.position;
                            float extension = -Mathf.Atan2(Vector3.Dot(thigh, actor.transform.forward), -Vector3.Dot(thigh, actor.transform.up)) * Mathf.Rad2Deg;
                            mechanics.Add(new Vector3(left ? body.LeftSupportProgress : body.RightSupportProgress, Bend(leg), extension));
                        }
                        float SegmentForward(Transform a, Transform b) => Vector3.Dot((a.position-b.position).normalized, actor.transform.forward);
                        float Yaw(Vector3 right) => Mathf.Atan2(-Vector3.Dot(right,actor.transform.forward),Vector3.Dot(right,actor.transform.right))*Mathf.Rad2Deg;
                        if (!coordination.TryGetValue(stage,out var coordinated)) coordination[stage]=coordinated=new();
                        coordinated.Add(new Vector4(SegmentForward(body.rightLeg.lower,body.rightLeg.upper)-SegmentForward(body.leftLeg.lower,body.leftLeg.upper),
                            SegmentForward(body.leftArm.lower,body.leftArm.upper)-SegmentForward(body.rightArm.lower,body.rightArm.upper),
                            Yaw(body.rightLeg.upper.position-body.leftLeg.upper.position),Yaw(body.rightArm.upper.position-body.leftArm.upper.position)));
                    }
                }
                heightDifference = Mathf.Max(heightDifference, Mathf.Abs(Vector3.Dot(body.leftLeg.end.position - body.rightLeg.end.position, actor.transform.up)));
                maxPelvis = Mathf.Max(maxPelvis, Mathf.Abs(body.CurrentPelvisOffset));
            }
        }
        void Capture(string name, Vector3 view, bool worldView = false, GameObject fixedViewSubject = null)
        {
            bool fixedView = fixedViewSubject != null;
            if (!fixedView)
            {
                Vector3 centre = actor.transform.position + Vector3.up * .9f;
                camera.transform.position = centre + (worldView ? view : actor.transform.TransformDirection(view)) * 3;
                camera.transform.LookAt(centre);
            }
            camera.cullingMask = ~0;
            if (name == "idle-front")
                File.WriteAllText(output + "/review-render.txt", $"floor={floor.GetComponent<Renderer>().bounds}, scene={floor.scene.name}, renderer={floor.GetComponent<Renderer>().enabled}, material={floorMaterial.shader.name}, camera scene={camera.scene.name}, actor={actor.transform.position}, camera={camera.transform.position}, active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            var skins = (fixedView ? fixedViewSubject : actor).GetComponentsInChildren<SkinnedMeshRenderer>().Where(skin => skin.enabled).ToArray();
            var proxies = skins.Select(skin => {
                var mesh = new Mesh(); skin.BakeMesh(mesh); var proxy = new GameObject("Posed review mesh", typeof(MeshFilter), typeof(MeshRenderer));
                proxy.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                proxy.GetComponent<MeshFilter>().sharedMesh = mesh; proxy.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                skin.enabled = false; return proxy; }).ToArray();
            int width = fixedView ? 1280 : 720, height = fixedView ? 720 : 900;
            var target = RenderTexture.GetTemporary(width, height, 24); var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                if (worldView)
                {
                    // Diagnostic ground guide and actual ankle targets; these
                    // are annotations on the review, never gameplay geometry.
                    int groundY = Mathf.RoundToInt(camera.WorldToScreenPoint(new Vector3(actor.transform.position.x, 0, actor.transform.position.z)).y);
                    for (int x = 0; x < 720; x++) if (groundY >= 0 && groundY < 900) image.SetPixel(x, groundY, new Color(.24f, .30f, .34f));
                    foreach (bool left in new[] { true, false })
                    {
                        Vector3 targetPoint = left ? body.LeftFootTarget : body.RightFootTarget;
                        Vector3 pixel = camera.WorldToScreenPoint(targetPoint);
                        Color color = (left ? body.LeftFootSupported : body.RightFootSupported) ? Color.green : Color.cyan;
                        for (int n = -5; n <= 5; n++)
                        {
                            int px = Mathf.RoundToInt(pixel.x), py = Mathf.RoundToInt(pixel.y);
                            if (px + n >= 0 && px + n < 720 && py >= 0 && py < 900) image.SetPixel(px + n, py, color);
                            if (px >= 0 && px < 720 && py + n >= 0 && py + n < 900) image.SetPixel(px, py + n, color);
                        }
                    }
                    image.Apply();
                }
                File.WriteAllBytes(output + "/" + name + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                foreach (var proxy in proxies) { Object.DestroyImmediate(proxy.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(proxy); }
                foreach (var skin in skins) skin.enabled = true;
            }
        }
        void RecordMatchedRuns()
        {
            var clipScene = EditorSceneManager.OpenScene(OriginalScene, OpenSceneMode.Additive);
            var clipSource = clipScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ThirdPersonController>())
                .First(controller => controller.GetComponentInChildren<TanjiroSword>() != null);
            var clipActor = Object.Instantiate(clipSource.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clipActor, reviewScene);
            EditorSceneManager.CloseScene(clipScene, true);
            var clipSword = clipActor.GetComponentInChildren<TanjiroSword>();
            var clipAnimator = clipSword.GetComponent<Animator>();
            var clipMotor = clipActor.GetComponent<ThirdPersonController>();
            clipAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            clipSword.Initialize();
            clipAnimator.applyRootMotion = false;

            // Same floor, travel lane, camera and measured capsule velocity.
            // A fixed world camera gives both versions identical ground markers.
            Vector3 centre = new Vector3(4, .95f, 3.5f);
            Vector3 sideView = Vector3.right * 8 + Vector3.up * .55f;
            camera.transform.SetPositionAndRotation(centre + sideView, Quaternion.LookRotation(-sideView, Vector3.up));
            camera.orthographicSize = 2.45f;
            camera.farClipPlane = 10;
            Directory.CreateDirectory(output + "/matched-side");
            using var measured = new StreamWriter(output + "/matched-side/speed.csv");
            measured.WriteLine("mode,version,frame,z,metresPerSecond,torsoLean,leftAnkleHeight,rightAnkleHeight,pelvisYaw,shoulderYaw,rightUpperArmPitch,rightElbowBend,rightThighPitch,rightKneeBend");
            using var jointPositions = new StreamWriter(output + "/matched-side/joint-positions.csv");
            jointPositions.WriteLine("mode,version,frame,joint,x,y,z");
            foreach (bool useClips in new[] { false, true })
            {
                var subject = useClips ? clipActor : actor;
                var driver = useClips ? clipMotor : motor;
                var controls = subject.GetComponent<StarterAssetsInputs>();
                var hips = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.Hips) : body.pelvis;
                var head = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.Head) : body.head;
                var leftAnkle = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftFoot) : body.leftLeg.end;
                var rightAnkle = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightFoot) : body.rightLeg.end;
                var leftHip = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : body.leftLeg.upper;
                var rightHip = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : body.rightLeg.upper;
                var rightKnee = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg) : body.rightLeg.lower;
                var leftShoulder = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : body.leftArm.upper;
                var rightShoulder = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm) : body.rightArm.upper;
                var rightElbow = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm) : body.rightArm.lower;
                var rightWrist = useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.RightHand) : body.rightArm.end;
                var joints = new[] { hips, head, leftHip, rightHip, leftAnkle, rightAnkle, rightKnee, leftShoulder, rightShoulder, rightElbow, rightWrist,
                    useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) : body.leftLeg.lower,
                    useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : body.leftArm.lower,
                    useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.LeftHand) : body.leftArm.end,
                    useClips ? clipAnimator.GetBoneTransform(HumanBodyBones.Chest) : body.chest };
                (useClips ? actor : clipActor).transform.position = new Vector3(70, 0, 0);
                driver.MoveSpeed = 4;
                driver.SprintSpeed = sceneSprintSpeed;
                driver.SpeedChangeRate = motor.SpeedChangeRate;
                driver.GroundLayers = ~0;
                controls.analogMovement = true;
                foreach (bool fast in new[] { false, true })
                {
                    const float dt = 1f / 96;
                    string mode = fast ? "fast" : "run", version = useClips ? "third-person" : "procedural";
                    string directory = $"matched-side/{mode}-{version}";
                    Directory.CreateDirectory(output + "/" + directory);
                    subject.transform.SetPositionAndRotation(new Vector3(4, .04f, -18), Quaternion.identity);
                    driver.ResetFallVelocity();
                    controls.MoveInput(Vector2.zero);
                    controls.SprintInput(fast);
                    if (useClips)
                    {
                        clipAnimator.Rebind(); clipSword.ResetSword();
                        clipAnimator.Play("Idle Walk Run Blend", 0, 0); clipAnimator.Update(0);
                    }
                    void Step()
                    {
                        driver.Simulate(dt);
                        if (useClips)
                        {
                            clipSword.UpdateLocomotion(dt);
                            clipAnimator.Update(dt);
                            clipSword.ApplySheathPose();
                        }
                        else body.Simulate(dt);
                        Physics.SyncTransforms(); Physics.Simulate(dt);
                    }
                    for (int i = 0; i < 48; i++) Step();
                    Vector3 forward = driver.MovementForward;
                    controls.MoveInput(new Vector2(Vector3.Dot(Vector3.forward, Vector3.Cross(Vector3.up, forward)), forward.z));
                    for (int i = 0; i < 650 && subject.transform.position.z < 0; i++) Step();
                    float speed = fast ? driver.SprintSpeed : driver.MoveSpeed;
                    float firstZ = subject.transform.position.z, maxSpeedError = 0;
                    int frames = fast ? 21 : 42;
                    int leftPlants = body.LeftFootPlants, rightPlants = body.RightFootPlants;
                    int previousSide = 0, plants = 0, repeats = 0;
                    float previousPhase = 0, timingError = 0;
                    float kneeSpacing = float.PositiveInfinity, ankleSpacing = float.PositiveInfinity;
                    if (!useClips) Directory.CreateDirectory(output + "/" + directory + "-front");
                    for (int i = 0; i < frames; i++)
                    {
                        float z = subject.transform.position.z;
                        float actual = driver.Motor.Velocity.z;
                        maxSpeedError = Mathf.Max(maxSpeedError, Mathf.Abs(actual - speed));
                        Vector3 torso = head.position - hips.position;
                        float lean = Mathf.Atan2(torso.z, torso.y) * Mathf.Rad2Deg;
                        Vector3 hipLine = rightHip.position - leftHip.position;
                        Vector3 shoulderLine = rightShoulder.position - leftShoulder.position;
                        Vector3 upperArm = rightElbow.position - rightShoulder.position;
                        Vector3 thigh = rightKnee.position - rightHip.position;
                        float Yaw(Vector3 across) => Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
                        float Pitch(Vector3 down) => Mathf.Atan2(down.z, -down.y) * Mathf.Rad2Deg;
                        float elbow = Vector3.Angle(upperArm, rightWrist.position - rightElbow.position);
                        float knee = Vector3.Angle(thigh, rightAnkle.position - rightKnee.position);
                        measured.WriteLine(FormattableString.Invariant($"{mode},{version},{i},{z:F4},{actual:F4},{lean:F2},{leftAnkle.position.y:F4},{rightAnkle.position.y:F4},{Yaw(hipLine):F2},{Yaw(shoulderLine):F2},{Pitch(upperArm):F2},{elbow:F2},{Pitch(thigh):F2},{knee:F2}"));
                        foreach (var joint in joints)
                        {
                            Vector3 local = subject.transform.InverseTransformPoint(joint.position);
                            jointPositions.WriteLine(FormattableString.Invariant($"{mode},{version},{i},{joint.name},{local.x:F5},{local.y:F5},{local.z:F5}"));
                        }
                        Capture($"{directory}/{i:D4}", Vector3.zero, fixedViewSubject: subject);
                        if (!useClips)
                        {
                            camera.orthographicSize = 1.06f;
                            Capture($"{directory}-front/{i:D4}", Vector3.forward);
                            camera.orthographicSize = 2.45f;
                            camera.transform.SetPositionAndRotation(centre + sideView, Quaternion.LookRotation(-sideView, Vector3.up));
                        }
                        for (int tick = 0; tick < 4; tick++)
                        {
                            Step();
                            if (useClips) continue;
                            Vector3 right = subject.transform.right;
                            ankleSpacing = Mathf.Min(ankleSpacing, Vector3.Dot(rightAnkle.position - leftAnkle.position, right));
                            kneeSpacing = Mathf.Min(kneeSpacing, Vector3.Dot(rightKnee.position - body.leftLeg.lower.position, right));
                            int side = body.LeftFootPlants != leftPlants ? -1 : body.RightFootPlants != rightPlants ? 1 : 0;
                            leftPlants = body.LeftFootPlants; rightPlants = body.RightFootPlants;
                            if (side == 0) continue;
                            if (side == previousSide) repeats++;
                            if (previousSide != 0)
                                timingError = Mathf.Max(timingError, Mathf.Abs(Mathf.Repeat(body.GaitPhase - previousPhase, 1) - .5f));
                            previousSide = side; previousPhase = body.GaitPhase; plants++;
                        }
                    }
                    Check(firstZ >= 0 && firstZ < .10f && maxSpeedError < .02f,
                        $"Matched fixed side view {mode}/{version} at {speed:F3} m/s (maximum speed error {maxSpeedError:F4} m/s)");
                    if (!useClips)
                    {
                        Check(ankleSpacing >= .20f && kneeSpacing >= .21f,
                            $"{mode} gives both legs room to pass (ankles {ankleSpacing:F3} m, knees {kneeSpacing:F3} m)");
                        Check(plants >= 3 && repeats == 0 && timingError < .10f,
                            $"{mode} alternates actual contacts around a half stride apart ({plants} plants, {repeats} repeats, max phase error {timingError:F3})");
                    }
                }
            }
            Object.DestroyImmediate(clipActor);
        }
        Tick(30); worst = 0;
        float standingHeight = Vector3.Dot(body.pelvis.position - actor.transform.position, actor.transform.up);
        // Isolate skin binding from the motion rules: move only one thigh.
        // Its influence must not reach the abdomen or directly pull the coat.
        var fixedSkin = sword.body.sharedMesh;
        var originalSkin = AssetDatabase.LoadAssetAtPath<Mesh>(TanjiroJointRigSetup.Folder + "/Tanjiro_AnatomicalSkin.asset");
        var skinWeights = fixedSkin.boneWeights; var skinVertices = fixedSkin.vertices; var garmentMask = fixedSkin.colors;
        var legIndices = sword.body.bones.Select((bone, id) => (bone, id)).Where(p => p.bone.name.Contains("UpperLeg") || p.bone.name.Contains("LowerLeg")).Select(p => p.id).ToArray();
        float LegWeight(BoneWeight w) => (legIndices.Contains(w.boneIndex0) ? w.weight0 : 0) + (legIndices.Contains(w.boneIndex1) ? w.weight1 : 0) +
            (legIndices.Contains(w.boneIndex2) ? w.weight2 : 0) + (legIndices.Contains(w.boneIndex3) ? w.weight3 : 0);
        float BindHeight(int i) => Vector3.Dot(sword.body.transform.TransformPoint(skinVertices[i]) - actor.transform.position, actor.transform.up);
        Check(skinVertices.SequenceEqual(originalSkin.vertices) && fixedSkin.uv.SequenceEqual(originalSkin.uv) && fixedSkin.triangles.SequenceEqual(originalSkin.triangles),
            "Waist/coat repair preserves original geometry, topology and UVs");
        var abdomen = Enumerable.Range(0, skinVertices.Length).Where(i => BindHeight(i) > .951f && BindHeight(i) < 1.16f).ToArray();
        var coat = Enumerable.Range(0, skinVertices.Length).Where(i => garmentMask[i].b > .99f && BindHeight(i) < .88f).ToArray();
        Check(abdomen.Length > 200 && abdomen.Max(i => LegWeight(skinWeights[i])) < .0001f,
            "Thigh/shin weights stop below the waist and cannot pull the abdomen");
        Check(coat.Length > 1000 && coat.Max(i => LegWeight(skinWeights[i])) < .01f && body.coatPanels.Length == 3,
            "Lower coat uses three independent two-bone panels instead of the leg bones");
        var beforeSkin = new Mesh(); sword.body.BakeMesh(beforeSkin);
        Quaternion thighRotation = body.leftLeg.upper.localRotation;
        body.leftLeg.upper.rotation = Quaternion.AngleAxis(-45, body.transform.right) * body.leftLeg.upper.rotation;
        var afterSkin = new Mesh(); sword.body.BakeMesh(afterSkin);
        Vector3[] beforeVertices = beforeSkin.vertices, afterVertices = afterSkin.vertices;
        float MaximumShift(int[] ids) => ids.Max(i => Vector3.Distance(beforeVertices[i], afterVertices[i]));
        Check(MaximumShift(abdomen) < .001f && MaximumShift(coat) < .001f,
            "Lifting one thigh leaves the abdomen and lower coat within 1 mm of their own independent pose");
        var trousers = Enumerable.Range(0, skinVertices.Length).Where(i => BindHeight(i) < .75f && garmentMask[i].b < .01f).ToArray();
        float Side(int i) => actor.transform.InverseTransformPoint(sword.body.transform.TransformPoint(skinVertices[i])).x;
        int leftHipIndex = Array.IndexOf(sword.body.bones, body.leftLeg.upper);
        Vector3 leftBindHip = sword.body.transform.TransformPoint(fixedSkin.bindposes[leftHipIndex].inverse.GetPosition());
        float leftSign = Mathf.Sign(actor.transform.InverseTransformPoint(leftBindHip).x);
        var rightTrouser = trousers.Where(i => Side(i) * leftSign < -.055f).ToArray();
        var leftTrouser = trousers.Where(i => Side(i) * leftSign > .055f).ToArray();
        float rightDrag = MaximumShift(rightTrouser);
        Vector3[] localSkin = skinVertices.Select(v => actor.transform.InverseTransformPoint(sword.body.transform.TransformPoint(v))).ToArray();
        var seamPairs = TanjiroJointRigSetup.LowerSeamPairs(fixedSkin.triangles, localSkin).ToArray();
        float SeamGap() => seamPairs.Select(pair => Vector3.Distance(afterVertices[pair.first], afterVertices[pair.second])).DefaultIfEmpty(0).Max();
        float gap = SeamGap();
        body.leftLeg.upper.localRotation = thighRotation;
        Quaternion rightThighRotation = body.rightLeg.upper.localRotation;
        body.rightLeg.upper.rotation = Quaternion.AngleAxis(-45, body.transform.right) * body.rightLeg.upper.rotation;
        sword.body.BakeMesh(afterSkin);
        afterVertices = afterSkin.vertices;
        float leftDrag = MaximumShift(leftTrouser);
        gap = Mathf.Max(gap, SeamGap());
        body.rightLeg.upper.localRotation = rightThighRotation;
        Check(seamPairs.Length > 100 && gap < .0005f, $"Lower mesh seams stay closed through either thigh lift ({gap:F5} m maximum gap)");
        Check(leftDrag < .001f && rightDrag < .001f,
            $"Opposite thigh isolation under a 45 degree lift (left trousers {leftDrag:F4} m, right trousers {rightDrag:F4} m)");
        var triangles = fixedSkin.triangles;
        int mixedFaces = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (Mathf.Max(BindHeight(a), BindHeight(b), BindHeight(c)) >= .82f) continue;
            float low = Mathf.Min(garmentMask[a].b, garmentMask[b].b, garmentMask[c].b);
            float high = Mathf.Max(garmentMask[a].b, garmentMask[b].b, garmentMask[c].b);
            if (low < .05f && high > .95f) mixedFaces++;
        }
        Check(mixedFaces == 0, $"Lower garment faces stay on one set of bones ({mixedFaces} conflicting faces)");
        body.leftLeg.upper.rotation = Quaternion.AngleAxis(-45, body.transform.right) * body.leftLeg.upper.rotation;
        Capture("waist-after-leg-lift-front", Vector3.forward); Capture("waist-after-leg-lift-back", Vector3.back); Capture("waist-after-leg-lift-side", Vector3.right);
        var comparisonSkin = Object.Instantiate(fixedSkin); comparisonSkin.boneWeights = originalSkin.boneWeights; sword.body.sharedMesh = comparisonSkin;
        Capture("waist-before-leg-lift-front", Vector3.forward); Capture("waist-before-leg-lift-back", Vector3.back); Capture("waist-before-leg-lift-side", Vector3.right);
        sword.body.sharedMesh = fixedSkin; body.leftLeg.upper.localRotation = thighRotation;
        Object.DestroyImmediate(comparisonSkin); Object.DestroyImmediate(beforeSkin); Object.DestroyImmediate(afterSkin);
        Capture("idle-front", Vector3.forward); Capture("idle-side", Vector3.right); Capture("idle-back", Vector3.back);
        if (Environment.GetEnvironmentVariable("CASTLE_RIG_REVIEW_ONLY") == "1")
        {
            Physics.IgnoreLayerCollision(actor.layer, 31, ignoredTreads); Object.DestroyImmediate(actor);
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            report.WriteLine("WAIST AND COAT SKIN CHECKS PASSED"); return;
        }
        foreach (var arm in new[] { body.leftArm, body.rightArm })
        {
            Vector3 wrist = body.transform.InverseTransformPoint(arm.end.position), elbow = body.transform.InverseTransformPoint(arm.lower.position);
            Check(Mathf.Abs(wrist.x) > .25f && Mathf.Abs(elbow.x) < Mathf.Abs(wrist.x), "Neutral arm hangs outside the hip without an outward elbow/inward wrist bow");
        }
        Check(body.fingers.Length == 30 && body.twists.Length == 4, "Thirty finger joints and four weighted arm-twist joints are configured");
        var skinRenderer = sword.body;
        foreach (string name in new[] { "LeftThumbDistal", "RightThumbDistal", "LeftForearmTwist", "RightForearmTwist", "LeftUpperArmTwist", "RightUpperArmTwist" })
        {
            int id = Array.FindIndex(skinRenderer.bones, b => b.name == name);
            int affected = skinRenderer.sharedMesh.boneWeights.Count(w => w.boneIndex0 == id && w.weight0 > .01f || w.boneIndex1 == id && w.weight1 > .01f ||
                w.boneIndex2 == id && w.weight2 > .01f || w.boneIndex3 == id && w.weight3 > .01f);
            Check(affected > 3, name + " deforms real mesh vertices (" + affected + ")");
        }
        foreach (var joint in body.fingers.Where(j => j.bone.name.Contains("ThumbDistal")))
        {
            var beforeMesh = new Mesh(); var afterMesh = new Mesh(); skinRenderer.BakeMesh(beforeMesh);
            Quaternion saved = joint.bone.localRotation;
            joint.bone.localRotation = saved * Quaternion.AngleAxis(35, joint.curlAxis); skinRenderer.BakeMesh(afterMesh);
            float displacement = beforeMesh.vertices.Zip(afterMesh.vertices, Vector3.Distance).Max();
            joint.bone.localRotation = saved;
            Check(displacement / beforeMesh.bounds.size.y > .0002f, joint.bone.name + " visibly bends the weighted fingertip surface");
            Object.DestroyImmediate(beforeMesh); Object.DestroyImmediate(afterMesh);
        }
        Check(!body.UsesAnimationClips && sword.GetComponent<Animator>().runtimeAnimatorController == null, "All pose playback is scripted; Animator/clip evaluation is disabled");
        input.MoveInput(Vector2.up * (1.7f / motor.MoveSpeed)); Tick(180); input.MoveInput(Vector2.zero); Tick(30);
        report.WriteLine($"Stair sample: actor={actor.transform.position}, foot height difference={heightDifference:F3}, pelvis travel={maxPelvis:F3}, plants={body.FootPlants}");
        Capture("stairs-side", Vector3.right); Capture("stairs-front", Vector3.forward);
        Check(motor.Grounded && actor.transform.position.y > .5f, "Existing motor climbs the game-style ramp collision while feet target separate stair treads");
        Check(body.FootPlants >= 4 && heightDifference > .10f && maxPelvis > .02f, "Separate foot contacts and pelvis weight transfer adapt to individual treads");
        Check(maxProbes <= 3, "Foot placement uses at most three non-allocating probes per frame");
        // Flat-floor action and running tests are independent of the stair fixture.
        stage = "flat reset"; actor.transform.position = new Vector3(4, .08f, -2); motor.ResetFallVelocity(); Tick(30);
        foreach (var tread in reviewScene.GetRootGameObjects().Where(g => g.name.StartsWith("Tread ")))
            tread.GetComponent<Renderer>().enabled = false;
        stage = "walk"; input.MoveInput(Vector2.up * (1.3f / motor.MoveSpeed)); Tick(240); Capture("walk-side", Vector3.right);
        input.MoveInput(Vector2.zero); Tick(45);
        stage = "draw"; sword.ToggleSword(); Tick(39); Capture("draw-grip", new Vector3(.6f, 0, 1));
        Tick(65); Capture("draw-extraction", new Vector3(.6f, 0, 1));
        Tick(140); Capture("ready-front", Vector3.forward); Capture("ready-side", Vector3.right);
        Check(sword.IsDrawn && !sword.IsBusy && sword.sword.parent == sword.handSocket, "Scripted draw completes through the existing R/mobile button interface");
        stage = "run"; input.MoveInput(Vector2.up); Tick(180); Capture("run-side", Vector3.right); Capture("run-front", Vector3.forward);
        stage = "sprint"; input.SprintInput(true); Tick(180); Capture("sprint-side", Vector3.right); Capture("sprint-front", Vector3.forward);
        foreach (var stride in strides)
        {
            var samples = stride.Value;
            File.WriteAllLines(output + "/" + stride.Key + "-stroke.csv", new[] { "leftAnkle,rightAnkle,leftWrist,rightWrist" }.Concat(samples.Select(s =>
                string.Join(",", new[] {s.x,s.y,s.z,s.w}.Select(v => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))))));
            double leftMean = samples.Average(s => s.x), rightMean = samples.Average(s => s.y), covariance = 0, leftVariance = 0, rightVariance = 0;
            float opposite = 0, total = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                double left = samples[i].x - leftMean, right = samples[i].y - rightMean;
                covariance += left * right; leftVariance += left * left; rightVariance += right * right;
                if (i == 0) continue;
                float dl = samples[i].x - samples[i - 1].x, dr = samples[i].y - samples[i - 1].y;
                float movement = Mathf.Min(Mathf.Abs(dl), Mathf.Abs(dr));
                total += movement; if (dl * dr < 0) opposite += movement;
            }
            double correlation = covariance / Math.Sqrt(leftVariance * rightVariance);
            float opposition = opposite / Mathf.Max(.00001f, total);
            report.WriteLine($"Actual {stride.Key} ankle strokes: correlation={correlation:F3}, opposing movement={opposition:P1}, samples={samples.Count}");
            // Contact and recovery have different durations: identical inverse
            // ankle waves are not a running-biomechanics acceptance criterion.
            // During walking double support, both world-planted ankles travel
            // backward relative to the pelvis. That is not a phase-sync defect.
            Check(samples.Count > 15 && correlation < -.45 && opposition > (stride.Key == "walk" ? .45f : .55f),
                stride.Key + " retains opposed overall leg travel across unequal support and recovery phases");
            if (stride.Key != "walk")
            {
                double worstWindow = -1;
                for (int start = 0; start + 36 <= samples.Count; start++)
                {
                    var window = samples.Skip(start).Take(36).ToArray();
                    double lm = window.Average(s => s.x), rm = window.Average(s => s.y);
                    double cov = window.Sum(s => (s.x - lm) * (s.y - rm));
                    double lv = window.Sum(s => Math.Pow(s.x - lm, 2)), rv = window.Sum(s => Math.Pow(s.y - rm, 2));
                    worstWindow = Math.Max(worstWindow, cov / Math.Sqrt(lv * rv));
                }
                report.WriteLine($"Actual {stride.Key} worst 0.6-second ankle correlation={worstWindow:F3}");
                Check(worstWindow < -.30, stride.Key + " keeps opposed actual ankle strokes in every rolling stride window");
            }
        }
        foreach (var trace in bodyHeights)
        {
            var samples = trace.Value;
            float pelvisRange = samples.Max(s => s.x) - samples.Min(s => s.x), headRange = samples.Max(s => s.y) - samples.Min(s => s.y);
            float maximumStep = samples.Skip(1).Select((s, i) => Mathf.Abs(s.x - samples[i].x)).Max();
            report.WriteLine($"Actual {trace.Key} vertical motion: pelvis range={pelvisRange:F4}m, head range={headRange:F4}m, maximum pelvis step={maximumStep:F4}m");
            File.WriteAllLines(output + "/" + trace.Key + "-height.csv", new[] { "pelvisHeight,headHeight,contacts,reportedPelvisOffset" }.Concat(samples.Select(s =>
                string.Join(",", new[] {s.x,s.y,s.z,s.w}.Select(v => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture))))));
            Check(pelvisRange < .065f && headRange < .065f && maximumStep * 60 < 1.25f,
                trace.Key + " keeps support/flight excursion below 6.5 cm and vertical speed below 1.25 m/s");
            float lean = torsoLeans[trace.Key].Average();
            float leanArcHeight = standingHeight * Mathf.Cos(lean * Mathf.Deg2Rad);
            report.WriteLine($"Actual {trace.Key} mean pelvis={samples.Average(s => s.x):F4}m, standing={standingHeight:F4}m, lean arc={leanArcHeight:F4}m");
            Check(samples.Average(s => s.x) > leanArcHeight - .03f,
                trace.Key + " maintains leg extension while leaning rather than lowering into a sustained squat");
        }
        foreach (var pose in runPoses)
        {
            bool fast = pose.Key == "sprint";
            var samples = pose.Value;
            float elbowMin = samples.Min(s => s.x), elbowMax = samples.Max(s => s.x), kneeMax = samples.Max(s => s.y);
            float thighMax = samples.Max(s => s.z), handMax = samples.Max(s => s.w), lean = torsoLeans[pose.Key].Average();
            report.WriteLine($"Actual {pose.Key} pose: elbow={elbowMin:F1}..{elbowMax:F1}, knee max={kneeMax:F1}, thigh lift max={thighMax:F1}, hand above pelvis={handMax:F3}m, torso lean={lean:F1}");
            Check(elbowMax - elbowMin > 15 && elbowMax < (fast ? 90 : 82) && handMax < .40f,
                pose.Key + " varies the elbow while keeping the free hand below the chest");
            Check(kneeMax < (fast ? 100 : 90) && thighMax < (fast ? 60 : 50),
                pose.Key + " recovers the heel behind the hip without a marching knee pose");
            Check(lean > (fast ? 8 : 5), pose.Key + " has visible forward torso lean at gameplay speed");
        }
        foreach (var arm in armMechanics)
        {
            var samples = arm.Value;
            report.WriteLine($"Actual {arm.Key} arm: shoulder={samples.Min(s=>s.x):F1}..{samples.Max(s=>s.x):F1}, elbow={samples.Min(s=>s.y):F1}..{samples.Max(s=>s.y):F1}, wrist behind shoulder={-samples.Min(s=>s.z):F3}m, lateral clearance={samples.Min(s=>s.w):F3}m");
            Check(samples.Min(s=>s.x) < -18 && samples.Max(s=>s.x) > 12 && samples.Min(s=>s.z) < -.035f,
                arm.Key + " drives the elbow and wrist behind the ribs instead of holding both hands at the stomach");
            Check(samples.Max(s=>s.y)-samples.Min(s=>s.y) > 25 && samples.Max(s=>s.y) < 100 && samples.Min(s=>s.w) > .08f,
                arm.Key + " varies elbow bend while keeping the hand outside the torso");
        }
        foreach (var motion in coordination)
        {
            var rows=motion.Value;
            double Corr(System.Func<Vector4,float> a,System.Func<Vector4,float> b)
            {
                double ma=rows.Average(s=>a(s)),mb=rows.Average(s=>b(s));
                return rows.Sum(s=>(a(s)-ma)*(b(s)-mb))/Math.Sqrt(rows.Sum(s=>Math.Pow(a(s)-ma,2))*rows.Sum(s=>Math.Pow(b(s)-mb,2)));
            }
            float agreement=rows.Count(s=>Mathf.Abs(s.x)>.25f && s.x*s.y>0)/(float)Mathf.Max(1,rows.Count(s=>Mathf.Abs(s.x)>.25f));
            float twistRange=rows.Max(s=>s.w)-rows.Min(s=>s.w);
            double worldOpposition = Corr(s => s.z, s => s.w);
            double relativeOpposition = Corr(s => s.z, s => s.w - s.z);
            report.WriteLine($"Actual {motion.Key} coordination: opposite-thigh/arm correlation={Corr(s=>s.x,s=>s.y):F3}, directional agreement={agreement:P1}, pelvis/shoulder yaw correlation={worldOpposition:F3}, relative torso correlation={relativeOpposition:F3}, shoulder yaw range={twistRange:F2}deg");
            File.WriteAllLines(output+"/"+motion.Key+"-coordination.csv",new[]{"oppositeThighLead,armLead,pelvisYaw,shoulderYaw"}.Concat(rows.Select(s=>FormattableString.Invariant($"{s.x:F6},{s.y:F6},{s.z:F6},{s.w:F6}"))));
            Check(Corr(s=>s.x,s=>s.y)>.90 && agreement>.90f,motion.Key+" keeps shoulder-driven arms aligned with the opposite thighs, not just the ankles");
            // Check the twist through the spine relative to the hips as well
            // as the shoulders' overall direction in the character frame.
            Check(worldOpposition < 0 && relativeOpposition < -.75 && twistRange > 8 && twistRange < 28,
                motion.Key + " has visible, restrained shoulder counter-rotation opposite the pelvis");
        }
        foreach (var support in supportPhases)
        {
            float fraction = support.Value.Count(s => s.x >= 0 || s.y >= 0) / (float)support.Value.Count;
            float propulsion = support.Value.Max(s => Mathf.Max(s.x, s.y));
            report.WriteLine($"Actual {support.Key} support: grounded gait fraction={fraction:P1}, latest loaded phase={propulsion:F3}");
            Check(fraction > (support.Key == "run" ? .25f : .20f) && fraction < .85f && propulsion > .7f,
                support.Key + " has meaningful loading/push-off before flight rather than prematurely releasing every contact");
            var heights = bodyHeights[support.Key];
            float[] loading = support.Value.Select((phase, i) => (phase, i)).Where(p => Mathf.Max(p.phase.x, p.phase.y) > .15f && Mathf.Max(p.phase.x, p.phase.y) < .5f).Select(p => heights[p.i].x).ToArray();
            float[] pushing = support.Value.Select((phase, i) => (phase, i)).Where(p => Mathf.Max(p.phase.x, p.phase.y) > .70f).Select(p => heights[p.i].x).ToArray();
            var mechanics = stanceMechanics[support.Key];
            var bent = mechanics.Where(p => p.x > .15f && p.x < .55f).ToArray();
            var extended = mechanics.Where(p => p.x > .70f).ToArray();
            report.WriteLine($"Actual {support.Key} loading/push height={loading.DefaultIfEmpty().Average():F4}/{pushing.DefaultIfEmpty().Average():F4}m; knee={bent.Select(p=>p.y).DefaultIfEmpty().Average():F1}/{extended.Select(p=>p.y).DefaultIfEmpty().Average():F1}; late hip extension={extended.Select(p=>p.z).DefaultIfEmpty().Average():F1}deg");
            report.WriteLine($"Actual {support.Key} loading knee peak={bent.Select(p=>p.y).DefaultIfEmpty().Max():F1}, push knee minimum={extended.Select(p=>p.y).DefaultIfEmpty().Min():F1}, push hip extension peak={extended.Select(p=>p.z).DefaultIfEmpty().Max():F1}");
            Check(loading.Length > 3 && pushing.Length > 3 && pushing.Average() > loading.Average() + .005f,
                support.Key + " raises the core from loaded support into propulsion rather than keeping it level");
            Check(bent.Length > 3 && extended.Length > 3 && extended.Min(p => p.y) < bent.Max(p => p.y) - 8 && extended.Max(p => p.z) > 8,
                support.Key + " extends the loaded trailing leg and hip before recovery");
        }
        Check(offFloorRunSamples == 0, "Full-speed running samples stay on the flat-floor fixture; flight is in the visual gait, not a fall off the test floor");
        Check(walkDoubleSupport > 2 && runFlight > 2, $"Walking includes double support ({walkDoubleSupport} samples); running includes flight ({runFlight} samples)");
        report.WriteLine($"Actual scene speeds: run={sceneRunSpeed:F3}, sprint={sceneSprintSpeed:F3}; planted ankle error={body.PlantedFootError:F4}");
        Check(body.PlantedFootError < .035f, "At gameplay sprint speed, planted ankles remain within 3.5 cm of their contacts");
        stage = "turns";
        input.SprintInput(false); Tick(150); Capture("fast-to-run-side", Vector3.right);
        Check(motor.Grounded && body.PlantedFootError < .005f, "Fast run returns to normal run without losing reachable support");
        foreach (Vector2 direction in new[] {Vector2.right, Vector2.down, Vector2.left, Vector2.up})
        {
            input.MoveInput(direction); Tick(30);
            Check(body.PlantedFootError < .035f, "Turning preserves reachable foot contacts for " + direction);
        }
        stage = "stop"; input.MoveInput(Vector2.zero); input.SprintInput(false); Tick(90); Capture("stop-side", Vector3.right);
        // Release at different stride points: preserve the final supporting foot
        // and its stagger instead of returning both legs to a default stance.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            actor.transform.SetPositionAndRotation(new Vector3(4, .04f, -6), Quaternion.identity); motor.ResetFallVelocity(); Tick(30);
            input.MoveInput(Vector2.up); input.SprintInput(attempt == 2); Tick(90 + attempt * 11);
            Vector3 released = actor.transform.position;
            input.MoveInput(Vector2.zero); Tick(45);
            if (body.PlantedFeet != 2) report.WriteLine("Incomplete stop " + attempt + ": " + body.DescribeGait());
            var lead = body.BrakingSupportFoot;
            Check(lead != null && body.PlantedFeet == 2, "A completed forward braking step establishes two real support contacts");
            Vector3 leadPosition = lead.position, leftPosition = body.leftLeg.end.position, rightPosition = body.rightLeg.end.position;
            float stagger = Mathf.Abs(Vector3.Dot(leftPosition - rightPosition, actor.transform.forward));
            float carried = Vector3.Dot(actor.transform.position - released, actor.transform.forward);
            Tick(60);
            report.WriteLine($"Stop {attempt}: forward deceleration={carried:F3}m, final stagger={stagger:F3}m, lead drift={Vector3.Distance(leadPosition, lead.position):F4}m");
            Check(carried > .15f && stagger > .06f && Vector3.Distance(leadPosition, lead.position) < .01f &&
                Vector3.Distance(leftPosition, body.leftLeg.end.position) < .01f && Vector3.Distance(rightPosition, body.rightLeg.end.position) < .01f,
                "Stopping holds its leading contact and slightly asymmetric stance without a repeated foot reset");
            input.MoveInput(Vector2.up); Tick(20); input.MoveInput(Vector2.zero); Tick(60);
        }
        stage = "sheathe"; sword.ToggleSword(); Tick(184);
        Check(!sword.IsDrawn && !sword.IsBusy && sword.sword.parent == sword.sheathSocket, "Scripted sheath completes without animation events or clips");
        report.WriteLine($"Maximum arm step at 60 Hz={worst:F2} degrees ({worstAt}); wrist bend={maxWrist:F2} degrees ({wristAt})");
        Check(worst < 55 && maxWrist < 85, "Procedural actions avoid arm flips and extreme wrist folding");
        stage = "authored stair_2";
        var stairs = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/InfinityCastle/CastleModules/RuntimeMobile/PackSwitchbackStair.prefab"));
        stairs.transform.position = new Vector3(8, 13.151064f, 0);
        var route = stairs.GetComponent<CastleStairRoute>().points;
        actor.transform.SetPositionAndRotation(stairs.transform.TransformPoint(route[0]) + Vector3.up * .04f, Quaternion.identity);
        motor.ResetFallVelocity(); Physics.SyncTransforms(); Tick(20);
        float startHeight = actor.transform.position.y;
        float ascentLean = 0;
        int TraverseStairs(bool descending)
        {
            int waypoint = 1, captureFrame = 0;
            string name = descending ? "down" : "up";
            var contacts = new System.Collections.Generic.HashSet<int>();
            var downhillLead = new System.Collections.Generic.List<float>();
            int treadPacingFrames = 0;
            float leanSum = 0;
            for (int frame = 0; frame < 1500 && waypoint < route.Length; frame++)
            {
                Vector3 goal = stairs.transform.TransformPoint(route[descending ? route.Length - 1 - waypoint : waypoint]);
                Vector3 delta = Vector3.ProjectOnPlane(goal - actor.transform.position, Vector3.up);
                if (delta.magnitude < .23f) { waypoint++; continue; }
                float targetSpeed = Mathf.Clamp(delta.magnitude * 5, 1.4f, 4);
                input.MoveInput(new Vector2(delta.x, delta.z).normalized * (targetSpeed / motor.MoveSpeed)); Tick(1);
                if (motor.StairSpeedLimit > 0 && body.PlantedFeet > 0)
                {
                    treadPacingFrames++;
                    leanSum += body.BodyLeanAngle;
                    int segment = route.Length - 1 - waypoint;
                    if (descending && route[segment + 1].y - route[segment].y > .1f && delta.magnitude > .45f)
                    {
                        Vector3 downhill = delta.normalized;
                        float left = body.LeftFootSupported ? Vector3.Dot(body.LeftSupportPoint, downhill) : float.NegativeInfinity;
                        float right = body.RightFootSupported ? Vector3.Dot(body.RightSupportPoint, downhill) : float.NegativeInfinity;
                        downhillLead.Add(Vector3.Dot(body.chest.position, downhill) - Mathf.Max(left, right));
                    }
                }
                if (body.LeftFootSupported) contacts.Add(Mathf.RoundToInt(body.LeftSupportPoint.y * 20));
                if (body.RightFootSupported) contacts.Add(Mathf.RoundToInt(body.RightSupportPoint.y * 20));
                if (reviewTraversal && frame % 3 == 0 && captureFrame < 130)
                    Capture($"stair-jump/stairs-{name}-{captureFrame++:D4}", new Vector3(.94f, .18f, .3f));
            }
            Check(contacts.Count >= 30 && treadPacingFrames > 180,
                $"Authored stair {name} uses tread pacing and plants across both flights ({contacts.Count} heights, {treadPacingFrames} paced support frames)");
            float averageLean = leanSum / Mathf.Max(1, treadPacingFrames);
            if (!descending) ascentLean = averageLean;
            else Check(ascentLean > averageLean + 3 && averageLean > -3,
                $"Ascent leans farther forward than descent ({ascentLean:F1}° versus {averageLean:F1}°)");
            if (descending)
            {
                downhillLead.Sort();
                float lead95 = downhillLead.Count > 0 ? downhillLead[Mathf.FloorToInt((downhillLead.Count - 1) * .95f)] : float.PositiveInfinity;
                Check(lead95 < .20f, $"Downhill chest remains close to the leading planted foot (95th percentile {lead95:F3} m)");
            }
            return waypoint;
        }
        int waypoint = TraverseStairs(false);
        input.MoveInput(Vector2.zero); Tick(30); Capture("authored-stairs-top", Vector3.right);
        report.WriteLine($"Authored stair_2 waypoints={waypoint}/{route.Length}, rise={actor.transform.position.y-startHeight:F3}, maximum contact error={maxContactError:F4}, actor={actor.transform.position}, grounded={motor.Grounded}, block={motor.Motor.LastStepBlock}, facing={motor.MovementForward}");
        Check(waypoint == route.Length && actor.transform.position.y - startHeight > 4.5f && motor.Grounded,
            "The motor and procedural feet climb both stair_2 flights and brake safely on the top landing");
        float topHeight = actor.transform.position.y;
        int returnWaypoint = TraverseStairs(true);
        input.MoveInput(Vector2.zero); Tick(30);
        Check(returnWaypoint == route.Length && topHeight - actor.transform.position.y > 4.5f && motor.Grounded,
            "The motor and procedural feet descend both stair_2 flights and settle on the bottom landing");
        report.WriteLine($"Contact samples={contactSamples}, worst planted-ankle error={maxContactError:F4}m ({contactAt})");
        Check(contactSamples > 50 && maxContactError < .005f, "Planted feet stay within 5 mm of their contacts, including both authored stair flights and braking");
        actor.transform.position = new Vector3(4, 6, -2); motor.ResetFallVelocity(); Tick(1);
        actor.GetComponent<InfinityGravityBody>().SetGravity(Vector3.right, 22, true, 8); Tick(1);
        float initialError = body.GravityVisualError; Tick(60);
        Check(initialError > 35 && body.GravityVisualError < 2, "Gravity keeps immediate motor collision while the visual core turns progressively");
        Check(!body.UsesAnimationClips, "Gravity and aerial posing remain clip-independent");
        stage = "30 Hz gravity";
        foreach (Vector3 direction in new[] { Vector3.up, Vector3.forward, Vector3.left, Vector3.back, Vector3.down })
        {
            actor.transform.position = new Vector3(4, 30, -2); motor.ResetFallVelocity(); Tick(1, 1f / 30);
            actor.GetComponent<InfinityGravityBody>().SetGravity(direction, 22, true, 8); Tick(30, 1f / 30);
            Check(body.GravityVisualError < 2 && float.IsFinite(body.rightArm.end.position.x),
                "30 FPS gravity transition settles safely for direction " + direction);
        }
        Capture("gravity-side", Vector3.right);
        var heightMeans = new System.Collections.Generic.List<float>();
        var kneeSpeeds = new System.Collections.Generic.List<float>();
        var armRanges = new System.Collections.Generic.List<Vector2>();
        foreach (int fps in new[] { 30, 60, 120 })
        {
            stage = "height " + fps + " FPS";
            actor.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
            input.MoveInput(Vector2.zero); input.SprintInput(false); sword.ResetSword();
            actor.transform.SetPositionAndRotation(new Vector3(4, .04f, -6), Quaternion.identity); motor.ResetFallVelocity(); Tick(fps / 2, 1f / fps);
            input.MoveInput(Vector2.up); Tick(fps * 2, 1f / fps);
            var heights = new System.Collections.Generic.List<float>(); float contactError = 0, kneeSpeed = 0;
            var shoulders = new System.Collections.Generic.List<float>(); var elbows = new System.Collections.Generic.List<float>();
            float KneeAngle() => Vector3.Angle(body.rightLeg.lower.position - body.rightLeg.upper.position, body.rightLeg.end.position - body.rightLeg.lower.position);
            float previousKnee = KneeAngle(); int previousPlants = body.FootPlants;
            for (int frame = 0; frame < fps * 2; frame++)
            {
                Tick(1, 1f / fps);
                float floorHeight = motor.Motor.GroundHit.collider != null ? Vector3.Dot(motor.Motor.GroundHit.point, actor.transform.up) : 0;
                heights.Add(Vector3.Dot(body.pelvis.position, actor.transform.up) - floorHeight);
                contactError = Mathf.Max(contactError, body.PlantedFootError);
                float knee = KneeAngle(); kneeSpeed = Mathf.Max(kneeSpeed, Mathf.Abs(knee - previousKnee) * fps); previousKnee = knee;
                Vector3 upper = body.leftArm.lower.position-body.leftArm.upper.position;
                shoulders.Add(Mathf.Atan2(Vector3.Dot(upper,actor.transform.forward),-Vector3.Dot(upper,actor.transform.up))*Mathf.Rad2Deg+body.BodyLeanAngle);
                elbows.Add(Vector3.Angle(upper,body.leftArm.end.position-body.leftArm.lower.position));
            }
            float range = heights.Max() - heights.Min(); heightMeans.Add(heights.Average());
            report.WriteLine($"Support height at {fps} FPS: mean={heights.Average():F4}m, range={range:F4}m, contact error={contactError:F4}m");
            Check(range < .05f && contactError < .035f, "Support height and fixed foot contacts remain stable at " + fps + " FPS");
            kneeSpeeds.Add(kneeSpeed);
            var armRange = new Vector2(shoulders.Max()-shoulders.Min(),elbows.Max()-elbows.Min()); armRanges.Add(armRange);
            report.WriteLine($"Arm motion at {fps} FPS: shoulder excursion={armRange.x:F1}deg, elbow excursion={armRange.y:F1}deg");
            Check(armRange.x > 45 && armRange.y > 25, "Shoulder and elbow motion remain present at " + fps + " FPS");
            report.WriteLine($"Motion continuity at {fps} FPS: knee peak speed={kneeSpeed:F1}deg/s, actual plants={body.FootPlants-previousPlants}");
            Check(body.FootPlants - previousPlants >= 5 && body.FootPlants - previousPlants <= 12, "Real alternating contacts persist at " + fps + " FPS");
        }
        Check(heightMeans.Max() - heightMeans.Min() < .015f, "Support height does not drift with render-frame rate");
        Check(armRanges.Max(r=>r.x)-armRanges.Min(r=>r.x)<8 && armRanges.Max(r=>r.y)-armRanges.Min(r=>r.y)<8,
            "Arm swing amplitude remains consistent across 30/60/120 FPS");
        Check(kneeSpeeds.Max() / Mathf.Max(1, kneeSpeeds.Min()) < 2, "Knee speed converges across 30/60/120 FPS rather than hiding a fixed pose jump in each frame");
        // Export the actual joint-rule motion for visual review, rather than
        // treating numerical checks as proof of believable animation.
        var frames = Path.Combine(output, "motion"); Directory.CreateDirectory(frames);
        actor.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
        input.MoveInput(Vector2.zero); input.SprintInput(false); sword.ResetSword();
        floor.transform.localScale = new Vector3(240, .2f, 240);
        actor.transform.SetPositionAndRotation(new Vector3(4, .04f, -6), Quaternion.identity); motor.ResetFallVelocity(); Tick(45);
        stage = "motion preview";
        bool exportMotion = failures.Count == 0;
        using var gaitTrace = new StreamWriter(output + "/motion-phases.csv") { AutoFlush = true };
        gaitTrace.WriteLine("frame,leftStage,rightStage,leftContact,rightContact,leftZ,rightZ,leftHeight,rightHeight,phase,plantedError,leftPlants,rightPlants,bodyLean,leftLoadPhase,rightLoadPhase,pelvisHeight,headHeight");
        int lastLeftPlants = body.LeftFootPlants, lastRightPlants = body.RightFootPlants, lastPlantSide = -1;
        int alternatingPlants = 0, repeatedPlants = 0, plantedFrames = 0, unsupportedFrames = 0, stanceBackward = 0, stanceSamples = 0;
        bool previousLeftSupported = body.LeftFootSupported, previousRightSupported = body.RightFootSupported;
        float lastLeftZ = 0, lastRightZ = 0, maxLegStep = 0, maxTravelAdjustedStep = 0, maxSwingClearance = 0, minKneeBend = 180, steadyReachCorrection = 0;
        string maximumLegAt = "", maximumReachAt = "";
        float supportDrift = 0;
        Vector3 previousLeftAnchor = body.LeftSupportPoint, previousRightAnchor = body.RightSupportPoint;
        for (int frame = 0; frame < 552; frame++)
        {
            // Idle -> run -> fast -> run -> stop; fast -> stop;
            // then repeatedly interrupt both acceleration and braking.
            bool moving = frame >= 24 && frame < 216 || frame >= 252 && frame < 324 ||
                frame >= 360 && frame < 378 || frame >= 390 && frame < 414 || frame >= 426 && frame < 468 || frame >= 480 && frame < 504;
            input.MoveInput(moving ? Vector2.up : Vector2.zero);
            input.SprintInput(frame >= 96 && frame < 156 || frame >= 252 && frame < 324 || frame >= 426 && frame < 468);
            for (int substep = 0; substep < 4; substep++)
            {
                var joints = new[] { body.leftLeg.upper, body.leftLeg.lower, body.rightLeg.upper, body.rightLeg.lower };
                var beforeJoints = joints.Select(j => j.localRotation).ToArray();
                Vector3 beforeLeft = body.leftLeg.end.position, beforeRight = body.rightLeg.end.position;
                Vector3 beforeActor = actor.transform.position;
                Vector3 beforeLeftHip = body.leftLeg.upper.position, beforeRightHip = body.rightLeg.upper.position;
                Vector3 beforeLeftKnee = body.leftLeg.lower.position, beforeRightKnee = body.rightLeg.lower.position;
                float phaseBefore = body.GaitPhase;
                Tick(1, 1f / 96);
                if (body.LeftFootSupported && previousLeftSupported) supportDrift = Mathf.Max(supportDrift, Vector3.Distance(previousLeftAnchor, body.LeftSupportPoint));
                if (body.RightFootSupported && previousRightSupported) supportDrift = Mathf.Max(supportDrift, Vector3.Distance(previousRightAnchor, body.RightSupportPoint));
                previousLeftAnchor = body.LeftSupportPoint; previousRightAnchor = body.RightSupportPoint;
                for (int j = 0; j < joints.Length; j++)
                {
                    float change = Quaternion.Angle(beforeJoints[j], joints[j].localRotation) * 96f / 60f;
                    // World-planted stance joints extend faster when the same
                    // body travels at 8.335 rather than 5 m/s. Test continuity
                    // per travelled distance, and separately test FPS convergence.
                    float travelSpeed = Vector3.ProjectOnPlane(actor.transform.position - beforeActor, actor.transform.up).magnitude * 96;
                    maxTravelAdjustedStep = Mathf.Max(maxTravelAdjustedStep, change / Mathf.Max(1, travelSpeed / sceneRunSpeed));
                    if (change > maxLegStep)
                    {
                        maxLegStep = change; maximumLegAt = frame + "/" + substep + "/" + joints[j].name +
                            $" phase={phaseBefore:F3}->{body.GaitPhase:F3}, L={body.LeftGaitStage} R={body.RightGaitStage}, ankle step={Vector3.Distance(beforeLeft, body.leftLeg.end.position):F3}/{Vector3.Distance(beforeRight, body.rightLeg.end.position):F3}, hip step={Vector3.Distance(beforeLeftHip, body.leftLeg.upper.position):F3}/{Vector3.Distance(beforeRightHip, body.rightLeg.upper.position):F3}, knee step={Vector3.Distance(beforeLeftKnee, body.leftLeg.lower.position):F3}/{Vector3.Distance(beforeRightKnee, body.rightLeg.lower.position):F3}, R bend={Vector3.Angle(beforeRightKnee-beforeRightHip,beforeRight-beforeRightKnee):F1}->{Vector3.Angle(body.rightLeg.lower.position-body.rightLeg.upper.position,body.rightLeg.end.position-body.rightLeg.lower.position):F1} " + body.DescribeGait();
                    }
                }
                bool steady = frame >= 60 && frame < 216 || frame >= 276 && frame < 324;
                if (steady)
                {
                    if (body.SwingReachCorrection > steadyReachCorrection)
                    { steadyReachCorrection = body.SwingReachCorrection; maximumReachAt = frame + "/" + substep + "/" + body.GaitPhase.ToString("F3") + " " + body.DescribeGait(); }
                    bool[] support = { body.LeftFootSupported, body.RightFootSupported };
                    float[] z = { body.leftLeg.end.position.z - actor.transform.position.z, body.rightLeg.end.position.z - actor.transform.position.z };
                    bool[] previous = { previousLeftSupported, previousRightSupported }; float[] previousZ = { lastLeftZ, lastRightZ };
                    if (body.PlantedFeet > 0) plantedFrames++; else unsupportedFrames++;
                    for (int side = 0; side < 2; side++)
                    {
                        if (support[side] && previous[side]) { stanceSamples++; if (z[side] < previousZ[side] + .001f) stanceBackward++; }
                        var leg = side == 0 ? body.leftLeg : body.rightLeg;
                        float knee = Vector3.Angle(leg.lower.position - leg.upper.position, leg.end.position - leg.lower.position);
                        minKneeBend = Mathf.Min(minKneeBend, knee);
                        if (!support[side]) maxSwingClearance = Mathf.Max(maxSwingClearance, leg.end.position.y - actor.transform.position.y - leg.soleHeight);
                        int plants = side == 0 ? body.LeftFootPlants : body.RightFootPlants;
                        int prior = side == 0 ? lastLeftPlants : lastRightPlants;
                        if (plants > prior) { if (lastPlantSide == side) repeatedPlants++; else alternatingPlants++; lastPlantSide = side; }
                    }
                }
                else lastPlantSide = -1;
                lastLeftPlants = body.LeftFootPlants; lastRightPlants = body.RightFootPlants;
                previousLeftSupported = body.LeftFootSupported; previousRightSupported = body.RightFootSupported;
                lastLeftZ = body.leftLeg.end.position.z - actor.transform.position.z; lastRightZ = body.rightLeg.end.position.z - actor.transform.position.z;
                string cycleMode = frame >= 60 && frame < 84 ? "run" : frame >= 120 && frame < 144 ? "sprint" : null;
                if (cycleMode != null)
                {
                    int sampleFrame = frame * 4 + substep;
                    if (exportMotion) Capture("cycle/" + cycleMode + "-" + sampleFrame.ToString("D4"), Vector3.right, true);
                    if (exportMotion && substep == 0) Capture("upper-front/" + cycleMode + "-" + sampleFrame.ToString("D4"), Vector3.forward, true);
                    cycleTrace.WriteLine(FormattableString.Invariant($"{cycleMode},{sampleFrame},{body.LeftGaitStage},{body.RightGaitStage},{body.LeftFootSupported},{body.RightFootSupported},{body.GaitPhase:F5},{body.BodyLeanAngle:F4},{body.LeftSupportProgress:F4},{body.RightSupportProgress:F4}"));
                }
            }
            if (exportMotion) Capture("motion/" + frame.ToString("D4"), Vector3.right, true);
            Vector3 l = body.leftLeg.end.position - actor.transform.position, r = body.rightLeg.end.position - actor.transform.position;
            gaitTrace.WriteLine(FormattableString.Invariant($"{frame},{body.LeftGaitStage},{body.RightGaitStage},{body.LeftFootSupported},{body.RightFootSupported},{l.z:F5},{r.z:F5},{l.y:F5},{r.y:F5},{body.GaitPhase:F5},{body.PlantedFootError:F5},{body.LeftFootPlants},{body.RightFootPlants},{body.BodyLeanAngle:F4},{body.LeftSupportProgress:F4},{body.RightSupportProgress:F4},{body.pelvis.position.y-actor.transform.position.y:F5},{body.head.position.y-actor.transform.position.y:F5}"));
            if (frame == 251 || frame == 359 || frame == 551)
                Check(body.PlantedFeet == 2, "Settled preview stop " + frame + " establishes both real contacts instead of leaving a recovery foot suspended");
        }
        report.WriteLine($"Phase sequence: alternating plants={alternatingPlants}, repeated={repeatedPlants}, support frames={plantedFrames}, flight frames={unsupportedFrames}, backward support={stanceBackward}/{stanceSamples}, clearance={maxSwingClearance:F3}m, minimum knee={minKneeBend:F1}, maximum leg change normalized to 60Hz={maxLegStep:F1} at {maximumLegAt}");
        Check(alternatingPlants > 20 && repeatedPlants == 0 && plantedFrames > 120 && unsupportedFrames > 120,
            "Both normal and repeated fast starts retain alternating real contacts and distinct flight");
        Check(stanceSamples > 50 && stanceBackward >= stanceSamples * .97f && maxSwingClearance > .10f && minKneeBend > 12,
            "Support feet sweep backward under the advancing pelvis; swing clears the ground and knees do not lock");
        report.WriteLine($"Maximum leg change per normal-run travel interval={maxTravelAdjustedStep:F1}deg");
        Check(maxTravelAdjustedStep < 25, "Starts, Run/Fast Run changes, stopping and interruptions preserve leg continuity per distance travelled");
        report.WriteLine($"Maximum steady swing reach clipping={steadyReachCorrection:F4}m at {maximumReachAt}");
        Check(steadyReachCorrection < .01f, "The planned steady swing stays within the leg envelope without hard reach clipping");
        report.WriteLine($"Maximum static-floor support-anchor drift={supportDrift:F5}m");
        Check(supportDrift < .001f, "Loaded contact anchors remain fixed on the floor through the full transition/stopping sequence");
        report.WriteLine($"Longest unsupported interval while grounded and running: left={longestUnsupported[0]:F3}s, right={longestUnsupported[1]:F3}s");
        Check(longestUnsupported.All(seconds => seconds < .75f), "Neither leg hangs through a complete extra stride during starts, running, speed changes or interrupted stops");
        foreach (int jumpMode in new[] { 0, 1, 2 })
        {
            bool stationary = jumpMode == 0, fast = jumpMode == 2;
            string name = stationary ? "standing-jump" : fast ? "super" : "jump";
            stage = name + " recovery";
            actor.transform.SetPositionAndRotation(new Vector3(4, .04f, -6), Quaternion.identity); motor.ResetFallVelocity();
            input.SprintInput(fast); input.MoveInput(stationary ? Vector2.zero : Vector2.up);
            int captureFrame = 0;
            void CaptureJump() => Capture($"stair-jump/{name}-{captureFrame++:D4}", new Vector3(.96f, .14f, .25f));
            Tick(reviewTraversal ? 78 : 90);
            if (reviewTraversal)
                for (int i = 0; i < 4; i++) { CaptureJump(); Tick(3); }
            float floorY = actor.transform.position.y;
            float standingPelvis = body.pelvis.position.y - floorY, windupDrop = 0;
            if (fast) input.UltraJumpInput(true); else input.JumpInput(true);
            Tick(2); input.JumpInput(false);
            bool leftGround = !motor.Grounded;
            float tuckedHeight = 0, readyHeight = float.PositiveInfinity;
            float upwardReach = float.NegativeInfinity;
            bool sawRequestedJump = fast ? motor.IsUltraJumping : motor.VerticalSpeed > 0 && !motor.IsUltraJumping;
            for (int i = 0; i < 200; i++)
            {
                Tick(1); leftGround |= !motor.Grounded;
                sawRequestedJump |= !motor.Grounded && (fast ? motor.IsUltraJumping : motor.VerticalSpeed > 0 && !motor.IsUltraJumping);
                if (motor.Grounded && !leftGround) windupDrop = Mathf.Max(windupDrop, standingPelvis - (body.pelvis.position.y - actor.transform.position.y));
                float ankleHeight = Mathf.Min(body.leftLeg.end.position.y, body.rightLeg.end.position.y) - actor.transform.position.y;
                if (!motor.Grounded && motor.VerticalSpeed > 0)
                    upwardReach = Mathf.Max(upwardReach, Mathf.Min(body.leftArm.end.position.y, body.rightArm.end.position.y) - body.head.position.y);
                if (!motor.Grounded && Mathf.Abs(motor.VerticalSpeed) < 2) tuckedHeight = Mathf.Max(tuckedHeight, ankleHeight);
                if (!motor.Grounded && motor.VerticalSpeed < -1 && actor.transform.position.y - floorY < .35f)
                    readyHeight = Mathf.Min(readyHeight, ankleHeight);
                if (reviewTraversal && i % 3 == 0) CaptureJump();
                if (motor.Grounded && leftGround) break;
            }
            Check(sawRequestedJump && tuckedHeight > .18f && tuckedHeight - readyHeight > .045f,
                $"{stage} tucks in flight and lowers the feet before landing ({tuckedHeight:F3} m tuck, {readyHeight:F3} m landing)");
            if (stationary) Check(windupDrop > .08f, $"Standing jump visibly loads both legs before takeoff ({windupDrop:F3} m crouch)");
            Check(upwardReach > .015f, $"{stage} sweeps both hands up past the head during ascent ({upwardReach:F3} m)");
            int lp = body.LeftFootPlants, rp = body.RightFootPlants;
            if (reviewTraversal)
                for (int i = 0; i < 10; i++) { CaptureJump(); Tick(3); }
            Tick(reviewTraversal ? 60 : 90);
            bool recovered = stationary ? body.PlantedFeet == 2 : body.LeftFootPlants - lp >= 2 && body.RightFootPlants - rp >= 2;
            Check(leftGround && motor.Grounded && recovered,
                stage + (stationary ? " settles onto both feet" : " reacquires alternating contacts with both legs"));
        }
        stage = "ledge fall";
        input.MoveInput(Vector2.zero); input.SprintInput(false);
        actor.transform.SetPositionAndRotation(new Vector3(4, 4, -6), Quaternion.identity); motor.ResetFallVelocity();
        float fallSpread = 0, fallReach = 0;
        bool fell = false;
        for (int frame = 0; frame < 110; frame++)
        {
            Tick(1);
            if (!motor.Grounded && motor.VerticalSpeed < -1 && actor.transform.position.y > .7f)
            {
                fell = true;
                fallSpread = Mathf.Max(fallSpread, Vector3.Dot(body.rightArm.end.position - body.leftArm.end.position, actor.transform.right));
                fallReach = Mathf.Max(fallReach, Mathf.Max(body.leftArm.end.position.y - body.head.position.y, body.rightArm.end.position.y - body.head.position.y));
            }
            if (reviewTraversal && frame % 3 == 0 && frame < 75)
                Capture($"stair-jump/fall-{frame / 3:D4}", new Vector3(.82f, .10f, .57f));
        }
        Check(fell && motor.Grounded && body.PlantedFeet == 2 && fallSpread > .64f && fallReach < .04f,
            $"A ledge fall opens the arms into the descent pose and settles on both feet ({fallSpread:F3} m hand spread)");
        input.MoveInput(Vector2.zero); input.SprintInput(false);
        if (failures.Count == 0 && Environment.GetEnvironmentVariable("CASTLE_MATCHED_LOCOMOTION") == "1") RecordMatchedRuns();
        report.WriteLine("PASS Original Playground and shared player prefab remain byte-identical");
        if (failures.Count == 0) report.WriteLine("ALL PROCEDURAL PLAYER CHECKS PASSED");
        Physics.IgnoreLayerCollision(actor.layer, 31, ignoredTreads);
        Object.DestroyImmediate(actor);
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
}
