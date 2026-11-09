using System;
using System.IO;
using StarterAssets;
using UnityEditor;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Invoked through Unity MCP. Exercises the actual player in the actual running
// castle, with normal Update/LateUpdate and rendering (no manual simulation).
public static class TanjiroProceduralLiveReview
{
    private static TanjiroProceduralBody body;
    private static ThirdPersonController motor;
    private static StarterAssetsInputs input;
    private static Vector3 startPosition;
    private static Quaternion startRotation;
    private static float started, nextCapture, worstContact;
    private static int stage = -1, shot, contacts;
    private static double queuedAt;
    private static bool returning;
    private static readonly float[] unsupported = new float[2], longestUnsupported = new float[2];
    private static readonly int[] observedPlants = new int[2];
    private static int sampledFrame, initialLeftPlants, initialRightPlants;
    private static StreamWriter trace, captureTimes;
    private static UnityEditor.Media.MediaEncoder videoEncoder;
    private static RenderTexture videoTarget;
    private static bool captureBridgeWasEnabled;
    private const int VideoWidth = 1280, VideoHeight = 600;
    private static Camera[] gameplayCameras;
    private static bool[] cameraEnabled;
    private static GameObject sideCamera;
    private static double groundedSince = -1;
    private const string ArmedReview = "TanjiroProceduralLiveReview.Armed";
    private const string FrontReview = "TanjiroProceduralLiveReview.Front";
#if ENABLE_INPUT_SYSTEM
    private static PlayerInput playerInput;
    private static bool inputWasEnabled;
    private static bool inputCaptured;
#endif
    private static string Output => SessionState.GetBool(FrontReview,false) ? "Temp/coordination-front-live" : "Temp/coordination-live";
    private static readonly float[] Ends = { 1, 4, 6, 8, 10, 12, 14, 14.5f, 15, 16, 17, 20.4f, 22.4f, 23.4f, 26.8f };
    private static readonly string[] Names = { "idle", "run", "fast", "fast-to-run", "run-stop", "fast-restart", "fast-stop", "restart", "interrupt", "restart-fast", "stop", "draw", "sword-run", "guard", "sheathe" };
    [InitializeOnLoadMethod]
    private static void ResumeArmedReview()
    {
        if (SessionState.GetBool(ArmedReview, false)) PrepareQueue();
    }
    public static string ArmAndPlay() => Arm(false);
    public static string ArmFrontAndPlay() => Arm(true);
    private static string Arm(bool front)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Arm outside Play mode.";
        SessionState.SetBool(FrontReview,front);
        SessionState.SetBool(ArmedReview, true); PrepareQueue();
        EditorApplication.isPlaying = true;
        return "Review armed before Play-mode reload; input is held until the castle is grounded and ready. It exits Play mode when complete.";
    }
    public static string Queue()
    {
        if (!EditorApplication.isPlaying) return "Enter Play mode, then queue the review.";
        PrepareQueue();
        return "Native review queued; it will start automatically when castle preparation finishes.";
    }
    private static void PrepareQueue()
    {
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/review.txt", "Waiting for the real castle to unlock controls and establish grounded support.\n");
        queuedAt = EditorApplication.timeSinceStartup;
        groundedSince = -1;
        EditorApplication.update -= WaitForCastle; EditorApplication.update += WaitForCastle;
    }
    private static void WaitForCastle()
    {
        if ((!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) || EditorApplication.timeSinceStartup - queuedAt > 120)
        {
            EditorApplication.update -= WaitForCastle;
            File.AppendAllText(Output + "/review.txt", "Review did not start: Play stopped or castle preparation exceeded 120 seconds.\n"); Finish(); return;
        }
        if (!EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        var candidate = UnityEngine.Object.FindFirstObjectByType<TanjiroProceduralBody>();
        if (candidate == null) return;
        var controller = candidate.GetComponentInParent<ThirdPersonController>();
        CaptureInput(controller);
        var controls = controller.GetComponent<StarterAssetsInputs>();
        controls.MoveInput(Vector2.zero); controls.LookInput(Vector2.zero); controls.JumpInput(false); controls.SprintInput(false); controls.UltraJumpInput(false);
        var castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        if (!candidate.IsInitialized || !controller.enabled || castle != null && !castle.IsPlayerReady || !controller.Grounded)
        { groundedSince = -1; return; }
        if (groundedSince < 0) groundedSince = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - groundedSince < .25) return;
        EditorApplication.update -= WaitForCastle; Begin();
    }
    private static void CaptureInput(ThirdPersonController controller)
    {
#if ENABLE_INPUT_SYSTEM
        if (!inputCaptured)
        {
            playerInput = controller.GetComponent<PlayerInput>(); inputWasEnabled = playerInput != null && playerInput.enabled; inputCaptured = true;
        }
        if (playerInput != null) playerInput.enabled = false;
#endif
    }
    public static string Begin()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        body = UnityEngine.Object.FindFirstObjectByType<TanjiroProceduralBody>();
        motor = body.GetComponentInParent<ThirdPersonController>(); input = motor.GetComponent<StarterAssetsInputs>();
        if (!motor.enabled) return "Castle is still preparing; retry when controls unlock.";
        startPosition = motor.transform.position; startRotation = motor.transform.rotation;
        CaptureInput(motor);
        gameplayCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        cameraEnabled = Array.ConvertAll(gameplayCameras, camera => camera.enabled);
        foreach (var camera in gameplayCameras) camera.enabled = false;
        sideCamera = new GameObject("Temporary fixed-side gait review", typeof(Camera));
        var reviewCamera = sideCamera.GetComponent<Camera>(); reviewCamera.orthographic = true; reviewCamera.orthographicSize = 1.12f;
        sideCamera.tag = "MainCamera";
        reviewCamera.nearClipPlane = .05f; reviewCamera.farClipPlane = 500;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += PositionSideCamera;
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/review.txt", "Native Play-mode review in " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + "\n");
        File.AppendAllText(Output + "/review.txt", "Skin=" + body.GetComponent<TanjiroSword>().body.sharedMesh.name + "; independent coat panels=" + body.coatPanels.Length + "\n");
        File.AppendAllText(Output + "/review.txt", "Fixed " + (SessionState.GetBool(FrontReview,false) ? "front" : "side") + " camera: constant orientation/scale, follows the motor root (not the pelvis).\n");
        File.AppendAllText(Output + "/review.txt", $"Runtime speeds after castle setup: Run={motor.MoveSpeed:F3}, Fast Run={motor.SprintSpeed:F3} m/s.\n");
        started = Time.unscaledTime; nextCapture = 0; stage = -1; shot = contacts = 0; worstContact = 0;
        Array.Clear(unsupported, 0, 2); Array.Clear(longestUnsupported, 0, 2);
        initialLeftPlants = body.LeftFootPlants; initialRightPlants = body.RightFootPlants; sampledFrame = -1;
        observedPlants[0] = initialLeftPlants; observedPlants[1] = initialRightPlants;
        trace?.Dispose(); trace = new StreamWriter(Output + "/motion.csv") { AutoFlush = true };
        trace.WriteLine("time,stage,grounded,leftContact,rightContact,leftPlants,rightPlants,phase,lean,pelvisHeight,leftZ,rightZ,contactError,speed,sprintInput");
        captureTimes?.Dispose(); captureTimes = new StreamWriter(Output + "/capture-times.csv") { AutoFlush = true };
        captureTimes.WriteLine("time,frame");
        videoTarget = new RenderTexture(VideoWidth, VideoHeight, 0, RenderTextureFormat.ARGB32); videoTarget.Create();
        videoEncoder = new UnityEditor.Media.MediaEncoder(Path.GetFullPath(Output + "/side-native.mp4"),
            new UnityEditor.Media.VideoTrackAttributes { width = VideoWidth, height = VideoHeight, frameRate = new UnityEditor.Media.MediaRational(24), includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High });
        captureBridgeWasEnabled = UnityEngine.Rendering.CameraCaptureBridge.enabled;
        UnityEngine.Rendering.CameraCaptureBridge.enabled = true;
        UnityEngine.Rendering.CameraCaptureBridge.AddCaptureAction(reviewCamera, RecordCamera);
        returning = false; Application.runInBackground = true;
        EditorApplication.update -= Update; EditorApplication.update += Update;
        return "Started native gameplay review; captures and contacts are saved in " + Output;
    }
    private static void Update()
    {
        if (!EditorApplication.isPlaying || body == null) { Finish(); return; }
        EditorApplication.QueuePlayerLoopUpdate();
        float elapsed = Time.unscaledTime - started;
        int current = 0; while (current < Ends.Length && elapsed >= Ends[current]) current++;
        if (current == Ends.Length) { Finish(); return; }
        if (current != stage)
        {
            stage = current;
            input.SprintInput(stage == 2 || stage == 5 || stage == 9);
            if (stage == 11 || stage == 14) body.GetComponent<TanjiroSword>().ToggleSword();
            File.AppendAllText(Output + "/review.txt", Names[stage] + " position=" + motor.transform.position + "\n");
            File.AppendAllText(Output + "/review.txt", body.DescribeGait() + "\n");
        }
        Vector3 back = Vector3.ProjectOnPlane(startPosition - motor.transform.position, motor.transform.up);
        if (back.magnitude > 2f) returning = true; else if (back.magnitude < .45f) returning = false;
        Vector3 viewForward = motor.MovementForward, viewRight = Vector3.Cross(motor.transform.up, viewForward);
        Vector3 forward = startRotation * Vector3.forward;
        Vector2 move = stage == 1 || stage == 2 || stage == 3 || stage == 5 || stage == 7 || stage == 9 || stage == 12 ?
            new Vector2(Vector3.Dot(forward, viewRight), Vector3.Dot(forward, viewForward)) : Vector2.zero;
        if (returning && move != Vector2.zero) move = new Vector2(Vector3.Dot(back.normalized, viewRight), Vector3.Dot(back.normalized, viewForward));
        input.MoveInput(move);
        // Focus callbacks can release inputs while screenshots/MCP are active.
        // Keep the intended review input asserted, as movement already is.
        input.SprintInput(stage == 2 || stage == 5 || stage == 9);
        if (motor.Grounded && body.PlantedFeet > 0) { worstContact = Mathf.Max(worstContact, body.PlantedFootError); contacts++; }
        if (sampledFrame != Time.frameCount)
        {
            sampledFrame = Time.frameCount;
            bool running = motor.Grounded && move != Vector2.zero && Vector3.ProjectOnPlane(motor.Motor.Velocity, motor.transform.up).magnitude > 3.5f;
            for (int side = 0; side < 2; side++)
            {
                bool support = side == 0 ? body.LeftFootSupported : body.RightFootSupported;
                int plants = side == 0 ? body.LeftFootPlants : body.RightFootPlants;
                // Editor callbacks can skip short support frames while a
                // screenshot is saved. Cumulative actual plants still record
                // those contacts; do not report them as a suspended foot.
                unsupported[side] = running && !support && plants == observedPlants[side] ? unsupported[side] + Time.deltaTime : 0;
                observedPlants[side] = plants;
                longestUnsupported[side] = Mathf.Max(longestUnsupported[side], unsupported[side]);
            }
            float Forward(Transform joint) => Vector3.Dot(joint.position - motor.transform.position, motor.transform.forward);
            trace.WriteLine(FormattableString.Invariant($"{elapsed:F4},{Names[stage]},{motor.Grounded},{body.LeftFootSupported},{body.RightFootSupported},{body.LeftFootPlants},{body.RightFootPlants},{body.GaitPhase:F5},{body.BodyLeanAngle:F3},{Vector3.Dot(body.pelvis.position-motor.transform.position,motor.transform.up):F4},{Forward(body.leftLeg.end):F4},{Forward(body.rightLeg.end):F4},{body.PlantedFootError:F5},{Vector3.ProjectOnPlane(motor.Motor.Velocity,motor.transform.up).magnitude:F4},{input.sprint}"));
        }
    }
    private static void RecordCamera(UnityEngine.Rendering.RenderTargetIdentifier source, UnityEngine.Rendering.CommandBuffer commands)
    {
        float elapsed = Time.unscaledTime - started;
        if (videoEncoder != null && elapsed >= nextCapture)
        {
            nextCapture = Mathf.Max(nextCapture + 1f / 24, elapsed + .001f);
            // Copy the rendered camera asynchronously; PNG compression on the
            // gameplay thread was reducing capture to single-digit frame rates.
            commands.Blit(source, videoTarget);
            commands.RequestAsyncReadback(videoTarget, 0, TextureFormat.RGBA32, request =>
            {
                if (videoEncoder == null) return;
                if (request.hasError) { Debug.LogError("Side-review GPU capture failed."); return; }
                bool added = videoEncoder.AddFrame(VideoWidth, VideoHeight, VideoWidth * 4, TextureFormat.RGBA32, request.GetData<byte>(),
                    new UnityEditor.Media.MediaTime((long)(elapsed * 1000000), 1000000, 1));
                if (!added) Debug.LogError("Side-review video encoder rejected a frame.");
                else captureTimes.WriteLine(FormattableString.Invariant($"{elapsed:F6},{++shot}"));
            });
        }
    }
    private static void PositionSideCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (sideCamera == null || camera.gameObject != sideCamera || motor == null) return;
        Vector3 up = startRotation * Vector3.up, view = startRotation * (SessionState.GetBool(FrontReview,false) ? Vector3.forward : Vector3.right);
        sideCamera.transform.SetPositionAndRotation(motor.transform.position + up * .84f + view * 1.25f, Quaternion.LookRotation(-view, up));
    }
    private static void Finish()
    {
        EditorApplication.update -= Update;
        if (input != null) { input.MoveInput(Vector2.zero); input.SprintInput(false); }
        if (motor != null && EditorApplication.isPlaying)
        {
            motor.transform.SetPositionAndRotation(startPosition, startRotation); motor.ResetFallVelocity();
            File.AppendAllText(Output + "/review.txt", $"Completed: contact samples={contacts}, worst ankle error={worstContact:F4}m, animator playing={body.UsesAnimationClips}\n");
            File.AppendAllText(Output + "/review.txt", $"Actual plants: left={body.LeftFootPlants-initialLeftPlants}, right={body.RightFootPlants-initialRightPlants}; longest unsupported while running: left={longestUnsupported[0]:F3}s, right={longestUnsupported[1]:F3}s\n");
        }
        trace?.Dispose(); trace = null;
        if (videoTarget != null)
        {
            if (sideCamera != null) UnityEngine.Rendering.CameraCaptureBridge.RemoveCaptureAction(sideCamera.GetComponent<Camera>(), RecordCamera);
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            videoEncoder?.Dispose(); videoEncoder = null;
            videoTarget.Release(); UnityEngine.Object.DestroyImmediate(videoTarget); videoTarget = null;
            UnityEngine.Rendering.CameraCaptureBridge.enabled = captureBridgeWasEnabled;
            File.AppendAllText(Output + "/review.txt", $"Native video encoded frames={shot}; capture uses asynchronous GPU readback.\n");
        }
        captureTimes?.Dispose(); captureTimes = null;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= PositionSideCamera;
        if (sideCamera != null) UnityEngine.Object.DestroyImmediate(sideCamera);
        if (gameplayCameras != null) for (int i = 0; i < gameplayCameras.Length; i++)
            if (gameplayCameras[i] != null) gameplayCameras[i].enabled = cameraEnabled[i];
#if ENABLE_INPUT_SYSTEM
        if (playerInput != null) playerInput.enabled = inputWasEnabled;
        inputCaptured = false;
#endif
        bool exitPlay = SessionState.GetBool(ArmedReview, false);
        SessionState.EraseBool(ArmedReview);
        SessionState.EraseBool(FrontReview);
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
    }
}

