using System;
using System.IO;
using StarterAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

public static class InfinityCastlePreviewRunner
{
    private static Camera inspection;
    private static DynamicInfinityCastle castle;
    private static ThirdPersonController controller;
    private static double started;
    private static float capturedAt;
    private static Color32[] overviewPixels;
    private static Color32[] playerPixels;
    private static int changedPixels;
    private static int architectureChangedPixels;
    private static bool keyboardVerified;
    private static bool touchVerified;
    private static int touchPhase;
    private static float touchAt, touchBase, touchPeak;
    private static Vector3 touchStart;
    private static Quaternion touchCameraStart;
    private static UIVirtualJoystick touchMove;
    private static InfinityScreenLook touchLook;
    private static UIVirtualButton touchJump;
    private static UnityEngine.EventSystems.PointerEventData moveFinger, lookFinger;
    private static bool shiftsCaptured;
    private static bool motionCaptured;
    private static bool nearMotionCaptured;
    private static float nearMotionAt = -1f;
    private static float motionAt = -1f;
    private static int fallStage, fallGeneration;
    private static float fallAt;
    private static float rebuildAt;
    private static Vector3 fallStart;
    private static Material assignedSkybox;
#if ENABLE_INPUT_SYSTEM
    private static Keyboard keyboard;
    private static int inputPhase;
    private static float inputAt, jumpBase, jumpPeak;
    private static Vector3 moveStart;
    private static Vector3 stairArrival;
#endif
    public static void Begin()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Preview is only for an isolated batch-mode project.");
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        assignedSkybox = RenderSettings.skybox;
#if ENABLE_INPUT_SYSTEM
        // A hidden batch editor has no focused Game view; route synthetic device events to the game.
        InputSystem.settings = UnityEngine.Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - started > 240)
        { Debug.LogError("Play-mode preview timed out."); Finish(1); return; }
        if (!EditorApplication.isPlaying || Time.time < 1f) return;
        if (inspection != null)
        {
            if (!shiftsCaptured)
            {
                if (!nearMotionCaptured && castle.TryGetNearFieldMovingSurface(out _, out _, out _))
                {
                    if (nearMotionAt < 0f) nearMotionAt = Time.time;
                    if (Time.time - nearMotionAt > 0.5f)
                    {
                        if (!Capture(Camera.main, "castle-playground-near-shifting.png")) return;
                        nearMotionCaptured = true;
                    }
                }
                if (castle.IsRebuilding && !motionCaptured)
                {
                    if (motionAt < 0f) motionAt = Time.time;
                    if (Time.time - motionAt > 0.7f)
                    {
                        if (!Capture(inspection, "castle-playground-moving.png")) return;
                        motionCaptured = true;
                    }
                }
                if (castle.ShiftCommandsIssued == 0 || castle.RebuildGeneration == 0 || castle.IsRebuilding || !nearMotionCaptured ||
                    castle.NearbyMovingCount > 0 || Time.time - capturedAt < 8f) return;
                if (!Capture(inspection, "castle-playground-shifted.png")) return;
                if (!Capture(Camera.main, "castle-playground-player-shifted.png")) return;
                shiftsCaptured = true; fallGeneration = castle.LandingGeneration;
                controller.transform.position += castle.randomNearbyCastle ?
                    new Vector3(castle.LayerSpacing * 1.73f, -castle.LayerSpacing * 7.4f, castle.LayerSpacing * 0.59f) :
                    new Vector3(castle.RouteSpacing * 0.48f, 3f, castle.RouteSpacing * 0.55f);
                controller.ResetFallVelocity(); fallStart = controller.transform.position; fallAt = Time.time; fallStage = 1;
                return;
            }
            if (Time.time - fallAt > 15f)
            {
                Debug.LogError("Playground fall check: stage=" + fallStage + ", grounded=" + controller.Grounded +
                    ", position=" + controller.transform.position + ", start=" + fallStart + ", gravity=" +
                    controller.GetComponent<InfinityGravityBody>().Up + ", generation=" + castle.LandingGeneration +
                    ", previous generation=" + fallGeneration + ", buildings=" + castle.ActiveBuildingCount);
                Finish(1); return;
            }
            if (fallStage == 1 && castle.LandingGeneration > fallGeneration &&
                castle.TryGetLandingTarget(out Vector3 target, out Vector3 up))
            {
                inspection.transform.position = target + new Vector3(160f, 450f, -200f);
                inspection.transform.LookAt(target + up * 14f);
                rebuildAt = Time.time; fallStage = 2;
            }
            else if (fallStage == 1 && Time.time - fallAt > 0.5f && controller.Grounded &&
                castle.IsCastleSurface(controller.Motor.GroundHit.collider))
            {
                // The streamed neighborhood can catch the player before an emergency landing is needed.
                inspection.transform.position = castle.player.position + new Vector3(160f, 450f, -200f);
                inspection.transform.LookAt(castle.player.position);
                rebuildAt = Time.time; fallStage = 2;
            }
            if (fallStage == 2 && Time.time - rebuildAt >= 0.7f)
            { if (!Capture(inspection, "castle-playground-rebuilding.png")) return; fallStage = 3; }
            if (fallStage < 3) return;
            if (Time.time - fallAt < 0.5f || !controller.Grounded) return;
            bool castleLanding = castle.IsCastleSurface(controller.Motor.GroundHit.collider) &&
                fallStart.y - controller.transform.position.y <= castle.LayerSpacing * 3.1f;
            inspection.transform.position = castle.player.position + new Vector3(90f, 140f, -120f);
            inspection.transform.LookAt(castle.player.position + Vector3.up * 7f);
            if (!Capture(inspection, "castle-playground-landed.png")) return;
            bool clear = castle.ValidateNoBuildingOverlaps(out string error);
            bool skyboxPreserved = assignedSkybox != null && RenderSettings.skybox == assignedSkybox && Camera.main.clearFlags == CameraClearFlags.Skybox;
            File.WriteAllText(Path.GetFullPath("../castle-playmode.txt"),
                $"Grounded={controller.Grounded}\nInitialized={castle.IsInitialized}\nKeyboard movement and jump={keyboardVerified}\nTouch movement, look and jump={touchVerified}\nNo overlaps={clear}\nSkybox preserved={skyboxPreserved}\nLanded on castle={castleLanding}\nLanding district rebuilt={fallStage == 3}\nTimed rebuilds={castle.RebuildGeneration}\nRebuilt connected buildings={castle.RebuiltRouteCount}\nCoordinated wings={castle.RebuiltDistrictCount}\nPhysically moved modules={castle.LastRebuildMovedCount}\nRotated modules={castle.LastRebuildRotatedCount}\nPhysical motion captured={motionCaptured}\nClose motion captured={nearMotionCaptured}\nNear-field motion commands={castle.NearFieldMotionCount}\nGravity-corner motion commands={castle.NearbyCornerMotionCount}\nRebuilt adjacent buildings={castle.LastRebuildNearMovedCount}\nChanged pixels={changedPixels}\nFixed-view architecture changes={architectureChangedPixels}\n{error}\n");
            if (!clear || !castleLanding || !skyboxPreserved || fallStage != 3 || changedPixels < 1000 ||
                !motionCaptured || !nearMotionCaptured || castle.NearFieldMotionCount == 0 || castle.NearbyCornerMotionCount == 0 ||
                castle.LastRebuildNearMovedCount == 0 || castle.RebuiltRouteCount == 0 || castle.RebuiltDistrictCount == 0 || architectureChangedPixels < 1000)
            { Debug.LogError("Play-mode validation failed: " + error); Finish(1); return; }
            Debug.Log("Play-mode castle preview and clearance checks passed.");
            Finish(0); return;
        }
        castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        controller = castle != null ? castle.player.GetComponent<ThirdPersonController>() : null;
        if (castle == null || controller == null || !castle.IsInitialized)
        { Debug.LogError("Play-mode castle did not initialize on solid ground."); Finish(1); return; }
        if (!keyboardVerified || !touchVerified) { castle.simultaneousShifts = 0; castle.periodicRebuilding = false; }
        if (!VerifyKeyboard()) return;
        if (!VerifyTouch()) return;
        castle.simultaneousShifts = 6; castle.periodicRebuilding = true;
        Camera camera = Camera.main;
        if (!Capture(camera, "castle-playground-player.png")) return;
        var go = new GameObject("Castle Inspection Camera");
        inspection = go.AddComponent<Camera>(); inspection.CopyFrom(camera);
        inspection.farClipPlane = Mathf.Max(1800f, inspection.farClipPlane);
        inspection.enabled = false;
        inspection.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        inspection.transform.position = castle.player.position + new Vector3(160f, 450f, -200f);
        inspection.transform.LookAt(castle.player.position);
        if (!Capture(inspection, "castle-playground-overview.png")) return;
        inspection.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        if (!Capture(inspection, "castle-playground-nearby.png")) return;
        inspection.transform.position = castle.player.position + new Vector3(160f, 450f, -200f);
        inspection.transform.LookAt(castle.player.position);
        capturedAt = Time.time;
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
    private static bool VerifyKeyboard()
    {
        if (keyboardVerified) return true;
#if ENABLE_INPUT_SYSTEM
        if (inputPhase == 0)
        {
            if (!controller.Grounded)
            { Debug.LogError("Playground player did not start grounded."); Finish(1); return false; }
            if (!castle.ValidateArchitectureOrientation(out string orientation) || !Capture(Camera.main, "castle-base-player.png"))
            { Debug.LogError("Playground building base orientation failed: " + orientation); Finish(1); return false; }
            PlayerInput playerInput = controller.GetComponent<PlayerInput>();
            if (playerInput == null || playerInput.currentActionMap == null || !playerInput.currentActionMap.enabled)
            { Debug.LogError("Playground player has no active input action map."); Finish(1); return false; }
            keyboard = InputSystem.AddDevice<Keyboard>("Castle Validation Keyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("Castle Validation Mouse");
            playerInput.SwitchCurrentControlScheme("KeyboardMouse", keyboard, mouse);
            moveStart = controller.transform.position; inputAt = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); inputPhase = 1; return false;
        }
        if (inputPhase == 1)
        {
            if (Time.time - inputAt < 0.6f) return false;
            if (Vector3.ProjectOnPlane(controller.transform.position - moveStart, Vector3.up).magnitude < 0.6f)
            {
                Debug.LogError("Playground WASD binding did not move the player: input=" + controller.GetComponent<StarterAssetsInputs>().move +
                    ", key=" + keyboard.wKey.isPressed + ", position=" + controller.transform.position + ", start=" + moveStart);
                Finish(1); return false;
            }
            jumpBase = jumpPeak = controller.transform.position.y; inputAt = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); inputPhase = 2; return false;
        }
        if (inputPhase == 2)
        {
            jumpPeak = Mathf.Max(jumpPeak, controller.transform.position.y);
            if (Time.time - inputAt < 1.2f) return false;
            if (jumpPeak - jumpBase < 0.8f || !controller.Grounded)
            { Debug.LogError("Playground Space binding did not jump and land."); Finish(1); return false; }
            moveStart = controller.transform.position; inputAt = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); inputPhase = 3; return false;
        }
        if (!castle.TryGetNearbyJump(out _, out stairArrival, out _))
        { Debug.LogError("Playground has no initial connected stair destination."); Finish(1); return false; }
        if (Vector3.ProjectOnPlane(controller.transform.position - stairArrival, Vector3.up).magnitude > 0.6f && Time.time - inputAt < 8f) return false;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        if (!controller.Grounded || Mathf.Abs(controller.transform.position.y - moveStart.y) < 3f ||
            Vector3.ProjectOnPlane(controller.transform.position - moveStart, Vector3.up).magnitude < 5f ||
            Vector3.ProjectOnPlane(controller.transform.position - stairArrival, Vector3.up).magnitude > 1f)
        { Debug.LogError("Playground W could not walk the original stair flight onto the next building: " + controller.transform.position); Finish(1); return false; }
#endif
        keyboardVerified = true;
        Debug.Log("Playground actual WASD, stair walking and Space bindings passed."); return true;
    }
    private static bool VerifyTouch()
    {
        if (touchVerified) return true;
        if (touchPhase == 0)
        {
            castle.previewTouchControls = true;
            var mobile = castle.GetComponent<InfinityCastleMobileRuntime>(); mobile.Configure(castle);
            var canvas = mobile.Controls.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = Camera.main; canvas.planeDistance = 1f;
            touchLook = mobile.ScreenLook;
            foreach (UIVirtualJoystick joystick in mobile.Controls.GetComponentsInChildren<UIVirtualJoystick>())
                if (joystick.name.Contains("Move")) touchMove = joystick;
            foreach (UIVirtualButton button in mobile.Controls.GetComponentsInChildren<UIVirtualButton>())
                if (button.name.Contains("Jump")) touchJump = button;
            Canvas.ForceUpdateCanvases();
            var eventSystem = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            moveFinger = new UnityEngine.EventSystems.PointerEventData(eventSystem) { pointerId = 101,
                pointerPressRaycast = new UnityEngine.EventSystems.RaycastResult { module = raycaster },
                position = RectTransformUtility.WorldToScreenPoint(Camera.main, touchMove.containerRect.position) + Vector2.up * 60f };
            lookFinger = new UnityEngine.EventSystems.PointerEventData(eventSystem) { pointerId = 102,
                pointerPressRaycast = new UnityEngine.EventSystems.RaycastResult { module = raycaster },
                pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = touchLook.gameObject },
                position = new Vector2(Screen.width * 0.45f, Screen.height * 0.6f) };
            touchAt = Time.time; touchPhase = 1; return false;
        }
        if (touchPhase == 1)
        {
            // Let the keyboard release and canvas layout settle before a different input source takes ownership.
            if (Time.time - touchAt < 0.25f) return false;
            if (!VerifyTouchRaycasts()) return false;
            moveFinger.position = RectTransformUtility.WorldToScreenPoint(Camera.main, touchMove.containerRect.position) + Vector2.up * 60f;
            lookFinger.position = new Vector2(Screen.width * 0.45f, Screen.height * 0.6f);
            touchStart = controller.transform.position; touchAt = Time.time;
            touchMove.OnPointerDown(moveFinger); touchPhase = 2; return false;
        }
        if (touchPhase == 2)
        {
            if (Time.time - touchAt < 0.4f) return false;
            Vector2 moveInput = controller.GetComponent<StarterAssetsInputs>().move;
            touchMove.OnPointerUp(moveFinger);
            if (Vector3.Distance(touchStart, controller.transform.position) < 0.5f)
            { Debug.LogError("Playground touch joystick did not move the player: input=" + moveInput + ", rect=" + touchMove.containerRect.rect +
                ", start=" + touchStart + ", end=" + controller.transform.position + ", block=" + controller.Motor.LastStepBlock); Finish(1); return false; }
            touchCameraStart = controller.CinemachineCameraTarget.transform.rotation;
            touchBase = touchPeak = Vector3.Dot(controller.transform.position, castle.PlayerUp); touchAt = Time.time;
            touchJump.OnPointerDown(moveFinger); touchLook.OnPointerDown(lookFinger); touchPhase = 3; return false;
        }
        touchPeak = Mathf.Max(touchPeak, Vector3.Dot(controller.transform.position, castle.PlayerUp));
        lookFinger.position += Vector2.right * (Screen.height * 0.12f * Time.deltaTime); touchLook.OnDrag(lookFinger);
        if (Time.time - touchAt < 1.2f) return false;
        touchLook.OnPointerUp(lookFinger); touchJump.OnPointerUp(moveFinger);
        if (touchPeak - touchBase < 0.8f || !controller.Grounded ||
            Quaternion.Angle(touchCameraStart, controller.CinemachineCameraTarget.transform.rotation) < 10f)
        { Debug.LogError("Playground touch jump, landing or camera look failed."); Finish(1); return false; }
        if (!Capture(Camera.main, "castle-android-controls.png") ||
            !Capture(Camera.main, "castle-android-controls-wide.png", 1600, 720)) return false;
        castle.GetComponent<InfinityCastleMobileRuntime>().Controls.gameObject.SetActive(false);
        controller.GetComponent<StarterAssetsInputs>().touchLook = false;
        touchVerified = true; return true;
    }
    private static bool VerifyTouchRaycasts()
    {
        Canvas canvas = touchLook.GetComponentInParent<Canvas>();
        var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        foreach (Vector2 point in new[] { new Vector2(0.08f, 0.65f), new Vector2(0.92f, 0.65f), new Vector2(0.15f, 0.9f), new Vector2(0.85f, 0.9f) })
        {
            lookFinger.position = new Vector2(point.x * Screen.width, point.y * Screen.height);
            hits.Clear(); raycaster.Raycast(lookFinger, hits);
            if (hits.Count > 0 && hits[0].gameObject == touchLook.gameObject) continue;
            Debug.LogError("Full-screen camera drag is blocked in free screen area " + point + "; first=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
            Finish(1); return false;
        }
        foreach (Component control in castle.GetComponent<InfinityCastleMobileRuntime>().Controls.GetComponentsInChildren<MonoBehaviour>())
        {
            if (!(control is UIVirtualJoystick) && !(control is UIVirtualButton)) continue;
            RectTransform rect = control is UIVirtualJoystick joystick ? joystick.containerRect : control.GetComponent<RectTransform>();
            lookFinger.position = RectTransformUtility.WorldToScreenPoint(Camera.main, rect.position);
            hits.Clear(); raycaster.Raycast(lookFinger, hits);
            if (hits.Count > 0 && hits[0].gameObject != touchLook.gameObject) continue;
            Debug.LogError("UI control does not own its screen area: " + control.name); Finish(1); return false;
        }
        lookFinger.position = new Vector2(Screen.width * 0.45f, Screen.height * 0.6f);
        Debug.Log("Full-screen camera raycasts and UI exclusion passed."); return true;
    }
    private static bool Capture(Camera camera, string name, int width = 1280, int height = 720)
    {
        try
        {
        RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture previousTarget = camera.targetTexture;
        float previousAspect = camera.aspect;
        camera.targetTexture = target; camera.aspect = (float)width / height;
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        Texture2D image = new(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        Color32[] pixels = image.GetPixels32();
        long brightness = 0;
        int visiblePixels = 0;
        foreach (Color32 pixel in pixels)
        { int light = pixel.r + pixel.g + pixel.b; brightness += light; if (light > 30) visiblePixels++; }
        if (brightness < pixels.Length * 3 || visiblePixels < pixels.Length * 0.05f)
            throw new InvalidOperationException("Castle preview rendered blank or the inspection camera is inside a surface.");
        if (name == "castle-playground-overview.png") overviewPixels = pixels;
        if (name == "castle-playground-player.png") playerPixels = pixels;
        if (name == "castle-playground-shifted.png" && overviewPixels != null)
            for (int i = 0; i < pixels.Length; i++)
                if (Math.Abs(pixels[i].r - overviewPixels[i].r) + Math.Abs(pixels[i].g - overviewPixels[i].g) + Math.Abs(pixels[i].b - overviewPixels[i].b) > 15)
                { changedPixels++; architectureChangedPixels++; }
        if (name == "castle-playground-player-shifted.png" && playerPixels != null)
            for (int i = 0; i < pixels.Length; i++)
                if (Math.Abs(pixels[i].r - playerPixels[i].r) + Math.Abs(pixels[i].g - playerPixels[i].g) + Math.Abs(pixels[i].b - playerPixels[i].b) > 15) changedPixels++;
        File.WriteAllBytes(Path.GetFullPath("../" + name), image.EncodeToPNG());
        camera.targetTexture = previousTarget; camera.aspect = previousAspect;
        RenderTexture.active = previous; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
        return true;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); return false; }
    }
}
