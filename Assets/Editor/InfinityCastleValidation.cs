using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarterAssets;

[InitializeOnLoad]
public static class InfinityCastleValidation
{
    static InfinityCastleValidation() => EditorApplication.playModeStateChanged += StartPreview;
    private static StreamWriter results;
    private static void Check(bool condition, string name)
    {
        results.WriteLine((condition ? "PASS " : "FAIL ") + name); results.Flush();
        if (!condition) throw new Exception(name);
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run validation in an isolated batch-mode project, not your open scene.");
        results = new StreamWriter(Path.GetFullPath("../castle-validation.txt"));
        results.WriteLine("UTC " + DateTime.UtcNow.ToString("O"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestGravityMotor();
            TestZones();
            TestRigidbodyGravity();
            TestMobileInput();
            TestScreenLook();
            TestCastle();
            TestRebuilding();
            TestRidingCastle();
            TestPlayground();
            results.WriteLine("ALL CHECKS PASSED");
        }
        catch (Exception error)
        {
            results.WriteLine(error); Debug.LogException(error); results.Close(); EditorApplication.Exit(1); return;
        }
        results.Close(); Debug.Log("Castle validation passed.");
    }
    private static ThirdPersonController Player(Vector3 position)
    {
        var go = new GameObject("Validation Player");
        var settings = go.AddComponent<CharacterController>();
        settings.height = 1.8f; settings.radius = 0.28f; settings.center = new Vector3(0f, 0.93f, 0f);
        settings.skinWidth = 0.02f; settings.stepOffset = 0.25f;
        go.AddComponent<StarterAssetsInputs>(); go.AddComponent<InfinityGravityBody>();
        var controller = go.AddComponent<ThirdPersonController>();
        controller.JumpTimeout = 0.1f; controller.GroundLayers = ~0;
        go.transform.position = position; controller.Initialize(); return controller;
    }
    private static void Step(ThirdPersonController player, int frames)
    {
        for (int i = 0; i < frames; i++) player.Simulate(1f / 60f);
    }
    private static void TestGravityMotor()
    {
        Vector3[] ups = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        foreach (Vector3 up in ups)
        {
            GameObject floor = new("Validation Floor");
            floor.transform.rotation = CastleGeometry.Orientation(up);
            BoxCollider collider = floor.AddComponent<BoxCollider>(); collider.center = Vector3.down * 0.2f; collider.size = new Vector3(40f, 0.4f, 40f);
            ThirdPersonController player = Player(up * 0.07f);
            player.GetComponent<InfinityGravityBody>().SetGravity(-up, 22f, true, 8f);
            Step(player, 60); Check(player.Grounded, "Grounding " + up + "; position=" + player.transform.position);
            Vector3 before = player.transform.position;
            player.GetComponent<StarterAssetsInputs>().move = Vector2.up; Step(player, 60);
            Check(Vector3.ProjectOnPlane(player.transform.position - before, up).magnitude > 2f, "Movement " + up);
            player.GetComponent<StarterAssetsInputs>().move = Vector2.zero;
            player.GetComponent<StarterAssetsInputs>().jump = true;
            float baseHeight = Vector3.Dot(player.transform.position, up), peak = baseHeight;
            for (int i = 0; i < 90; i++) { Step(player, 1); peak = Mathf.Max(peak, Vector3.Dot(player.transform.position, up)); }
            Check(peak - baseHeight > 0.9f, "Jump " + up);
            Check(player.Grounded && Mathf.Abs(Vector3.Dot(player.transform.position, up) - baseHeight) < 0.15f, "Landing " + up);
            UnityEngine.Object.DestroyImmediate(player.gameObject); UnityEngine.Object.DestroyImmediate(floor);
        }
    }
    private static void TestMobileInput()
    {
        GameObject canvas = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/StarterAssets/Mobile/Prefabs/CanvasInputs/UI_Canvas_StarterAssetsInputs_Joysticks.prefab"));
        var bridge = canvas.GetComponent<UICanvasControllerInput>();
        canvas.transform.localScale = Vector3.one;
        foreach (UIVirtualJoystick joystick in canvas.GetComponentsInChildren<UIVirtualJoystick>())
            for (int i = 0; i < joystick.joystickOutputEvent.GetPersistentEventCount(); i++)
                joystick.joystickOutputEvent.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        foreach (UIVirtualButton button in canvas.GetComponentsInChildren<UIVirtualButton>())
            for (int i = 0; i < button.buttonStateOutputEvent.GetPersistentEventCount(); i++)
                button.buttonStateOutputEvent.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        GameObject events = new("Touch Validation Events", typeof(UnityEngine.EventSystems.EventSystem));
        foreach (Vector3 up in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            GameObject floor = new("Touch Validation Surface"); floor.transform.rotation = CastleGeometry.Orientation(up);
            var box = floor.AddComponent<BoxCollider>(); box.center = Vector3.down * 0.2f; box.size = new Vector3(50f, 0.4f, 50f);
            ThirdPersonController player = Player(up * 0.07f);
            StarterAssetsInputs input = player.GetComponent<StarterAssetsInputs>(); input.analogMovement = true;
            bridge.starterAssetsInputs = input;
            player.GetComponent<InfinityGravityBody>().SetGravity(-up, 22f, true, 8f); Step(player, 60);
            Canvas.ForceUpdateCanvases();
            UIVirtualJoystick move = canvas.GetComponentsInChildren<UIVirtualJoystick>().First(j => j.name.Contains("Move"));
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, move.containerRect.position);
            var finger = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>())
                { pointerId = 12, position = center + Vector2.up * 10000f };
            move.OnPointerDown(finger);
            Check(input.move.magnitude > 0.9f && input.move.magnitude <= 1.001f, "Touch joystick output clamped " + up + "; input=" + input.move);
            var secondFinger = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>())
                { pointerId = 13, position = center + Vector2.down * 10000f };
            move.OnPointerDown(secondFinger); move.OnPointerUp(secondFinger);
            Check(input.move.y > 0.9f, "A second touch cannot steal movement " + up);
            Vector3 before = player.transform.position; Step(player, 60);
            Check(Vector3.ProjectOnPlane(player.transform.position - before, up).magnitude > 2f, "Touch movement in gravity frame " + up);
            move.OnPointerUp(finger); Check(input.move == Vector2.zero, "Touch release stops movement " + up);
            UIVirtualButton jump = canvas.GetComponentsInChildren<UIVirtualButton>().First(b => b.name.Contains("Jump"));
            float baseHeight = Vector3.Dot(player.transform.position, up), peak = baseHeight;
            jump.OnPointerDown(finger);
            for (int i = 0; i < 90; i++) { Step(player, 1); peak = Mathf.Max(peak, Vector3.Dot(player.transform.position, up)); }
            jump.OnPointerUp(finger);
            Check(peak - baseHeight > 0.9f && player.Grounded, "Touch jump and landing " + up);
            move.OnPointerDown(finger); move.enabled = false;
            // These fixtures run outside Play mode, where Unity does not dispatch this runtime lifecycle message.
            move.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
            Check(input.move == Vector2.zero, "Disabled touch control clears held input " + up); move.enabled = true;
            move.OnPointerDown(finger); move.ResetInput(); move.OnPointerDown(secondFinger);
            Check(input.move.y < -0.9f, "Touch reset releases ownership for a new finger " + up);
            move.OnPointerUp(secondFinger);
            UnityEngine.Object.DestroyImmediate(player.gameObject); UnityEngine.Object.DestroyImmediate(floor);
        }
        bridge.starterAssetsInputs = null;
        UnityEngine.Object.DestroyImmediate(canvas); UnityEngine.Object.DestroyImmediate(events);
    }
    private static void TestScreenLook()
    {
        ThirdPersonController player = Player(Vector3.zero);
        GameObject root = new("Screen look fixture");
        DynamicInfinityCastle castle = root.AddComponent<DynamicInfinityCastle>();
        castle.player = player.transform; castle.previewTouchControls = true;
        castle.mobileControlsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/StarterAssets/Mobile/Prefabs/CanvasInputs/UI_Canvas_StarterAssetsInputs_Joysticks.prefab");
        var mobile = root.AddComponent<InfinityCastleMobileRuntime>(); mobile.Configure(castle);
        Canvas canvas = mobile.Controls.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.ForceUpdateCanvases();
        GameObject events = new("Screen look events", typeof(UnityEngine.EventSystems.EventSystem));
        var finger = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()) { pointerId = 41 };
        InfinityScreenLook look = mobile.ScreenLook;
        RectTransform lookRect = look.GetComponent<RectTransform>();
        Check(lookRect.anchorMin == Vector2.zero && lookRect.anchorMax == Vector2.one && lookRect.GetSiblingIndex() == 0,
            "Camera input stretches across the entire viewport behind UI, outside the safe-area wrapper");
        StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
        foreach (Vector2 position in new[] { new Vector2(0.08f, 0.65f), new Vector2(0.92f, 0.65f), new Vector2(0.15f, 0.9f), new Vector2(0.85f, 0.9f) })
        {
            finger.position = new Vector2(position.x * Screen.width, position.y * Screen.height);
            finger.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = look.gameObject }; look.OnPointerDown(finger);
            finger.position += new Vector2(30f, 20f); look.OnDrag(finger); look.FlushLook(1f / 30f, 720f);
            Check(Mathf.Abs(inputs.look.x / 30f - 7.5f) < 0.01f && Mathf.Abs(inputs.look.y / 30f + 5f) < 0.01f,
                "Screen drag produces normalized yaw and upward pitch " + position);
            look.FlushLook(1f / 30f, 720f); Check(inputs.look == Vector2.zero, "Holding a stationary finger does not keep rotating");
            look.OnPointerUp(finger); Check(!look.IsDragging && inputs.look == Vector2.zero, "Screen look release clears ownership");
        }
        foreach (Component control in mobile.Controls.GetComponentsInChildren<MonoBehaviour>().Where(c => c is UIVirtualButton || c is UIVirtualJoystick))
        {
            RectTransform rect = control is UIVirtualJoystick joystick ? joystick.containerRect : control.GetComponent<RectTransform>();
            finger.position = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            finger.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = control.gameObject }; look.OnPointerDown(finger);
            Check(!look.IsDragging, "UI touch cannot start camera rotation: " + control.name);
        }
        finger.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = look.gameObject };
        finger.position = Vector2.one * 300f; look.OnPointerDown(finger);
        var other = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()) { pointerId = 42, position = Vector2.zero };
        look.OnPointerDown(other); look.OnDrag(other); look.OnPointerUp(other); look.FlushLook(1f / 60f, 720f);
        Check(look.IsDragging && inputs.look == Vector2.zero, "A second finger cannot steal the camera drag");
        finger.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = mobile.Controls.GetComponentsInChildren<UIVirtualButton>()[0].gameObject };
        finger.position += Vector2.right * 200f; look.OnDrag(finger); look.FlushLook(1f / 60f, 720f);
        Check(inputs.look == Vector2.zero, "Dragging over UI pauses camera rotation");
        finger.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = look.gameObject };
        finger.position += Vector2.right * 20f; look.OnDrag(finger); look.FlushLook(1f / 60f, 720f);
        Check(Mathf.Abs(inputs.look.x / 60f - 5f) < 0.01f, "Returning from UI does not accumulate a camera jump");
        look.ResetInput(); mobile.Configure(castle);
        Check(mobile.Controls.GetComponentsInChildren<InfinityScreenLook>(true).Length == 1, "Reconfiguring touch controls does not duplicate the full-screen input surface");
        UnityEngine.Object.DestroyImmediate(mobile.Controls.gameObject); UnityEngine.Object.DestroyImmediate(events);
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
    }
    private static void TestZones()
    {
        ThirdPersonController player = Player(Vector3.zero);
        InfinityGravityBody body = player.GetComponent<InfinityGravityBody>();
        GameObject a = new("Zone A"), b = new("Zone B");
        InfiniteCastleGrid za = a.AddComponent<InfiniteCastleGrid>(), zb = b.AddComponent<InfiniteCastleGrid>();
        za.Configure(Vector3.left, Vector3.zero, Vector3.one * 10f, 1);
        zb.Configure(Vector3.right, Vector3.zero, Vector3.one * 10f, 2);
        body.EnterZone(za); body.EnterZone(zb); body.LeaveZone(za);
        Check(body.Direction == Vector3.right, "Leaving one overlap retains the other gravity zone");
        zb.enabled = false; body.ResolveGravity(); Check(body.Direction == Vector3.down, "Disabled pooled zone releases gravity");
        za.Configure(Vector3.down, Vector3.up * 1.8f, Vector3.one * 10f, 1);
        b.transform.SetPositionAndRotation(new Vector3(0.7f, 1.5f, 0f), CastleGeometry.Orientation(Vector3.left));
        zb.Configure(Vector3.right, Vector3.up * 1.8f, Vector3.one * 10f, 1); zb.enabled = true;
        body.EnterZone(za); body.EnterZone(zb);
        for (int i = 0; i < 10; i++) body.EnterZone(za);
        Check(body.Up == Vector3.left, "Gravity selection stays stable before the capsule has physically turned");
        body.retainZoneGravityInAir = true; body.LeaveZone(za); body.LeaveZone(zb);
        Check(body.Up == Vector3.left, "Airborne gaps retain the last castle gravity instead of applying world-down");
        body.retainZoneGravityInAir = false;
        za.Configure(Vector3.down, Vector3.up * 2.3f, new Vector3(10f, 5.4f, 10f), 1, true);
        player.transform.position = Vector3.down * 1.1f;
        body.EnterZone(za);
        Check(body.CurrentZone == null, "Surface gravity cannot capture a capsule from behind its plane");
        UnityEngine.Object.DestroyImmediate(player.gameObject); UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
    }
    private static void TestRigidbodyGravity()
    {
        GameObject go = new("Validation Rigidbody");
        Rigidbody rigidbody = go.AddComponent<Rigidbody>(); rigidbody.useGravity = true;
        InfinityGravityBody body = go.AddComponent<InfinityGravityBody>();
        // This suite runs in edit mode, so explicitly invoke the runtime lifecycle callbacks.
        body.SendMessage("Awake"); body.SendMessage("OnDisable");
        Check(rigidbody.useGravity, "Disabling custom gravity restores native Rigidbody gravity");
        body.SendMessage("OnEnable");
        Check(!rigidbody.useGravity, "Reenabled custom gravity does not double-apply world-down gravity");
        UnityEngine.Object.DestroyImmediate(go);
    }
    private static void TestCastle()
    {
        ThirdPersonController player = Player(new Vector3(0f, 15f, 0f));
        GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab"));
        DynamicInfinityCastle castle = root.GetComponent<DynamicInfinityCastle>();
        castle.player = player.transform; castle.castleLighting = false; castle.periodicRebuilding = false;
        castle.rideMovingBuildings = false;
        castle.InitializeCastle();
        Check(castle.architectureFinishes.Count > 0 && castle.architectureFinishes.All(finish => finish.source != null && finish.albedo != null),
            "Original imported architecture textures resolve from the runtime prefab");
        Check(castle.IsInitialized && castle.ActiveBuildingCount > 80,
            "Random pooled castle surrounds the player");
        Check(castle.ValidateNoBuildingOverlaps(out string reason), "Initial building clearance: " + reason);
        Check(castle.ValidateNearbyConnectionGravity(out string connectionError), "Same-gravity stairs cover both entrances: " + connectionError);
        Check(castle.ActiveConnectionCount > 32, "Nearby buildings have connected walking routes; links=" + castle.ActiveConnectionCount);
        Check(castle.ValidateNearbyRouteCoverage(out string routeError), "Initial playable routes have connected exits: " + routeError);
        Step(player, 60); Check(player.Grounded, "Player begins on an open, solid balcony");
        Check(player.GetComponent<InfinityGravityBody>().Up == Vector3.up, "The starting route has normal gravity, not the lower face's gravity");
        Check(castle.ValidateArchitectureOrientation(out string orientation), "Buildings rise above their base balconies in every gravity frame: " + orientation);
        int count = root.GetComponentsInChildren<Transform>(true).Length;
        Vector3 initial = player.transform.position;
        castle.TryGetNearbyGravitySurface(Vector3.down, out Vector3 initialCeiling, out _);
        results.WriteLine("Initial ceiling=" + initialCeiling + ", player=" + initial);
        if (castle.randomNearbyCastle) TestNearbyTraversal(castle, player);
        for (int move = 0; move < 6; move++)
        {
            Vector3[] streamingDirections = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };
            player.transform.position = initial + streamingDirections[move] * castle.LayerSpacing * 6f;
            castle.SimulateCastle(0.02f);
            Check(root.GetComponentsInChildren<Transform>(true).Length == count, "Streaming reuses pool " + move);
            Check(castle.ValidateNoBuildingOverlaps(out reason), "Streamed clearance " + move + ": " + reason);
        }
        player.transform.position = initial; castle.SimulateCastle(0.02f);
        player.ResetFallVelocity(); player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
        for (int i = 0; i < 1200; i++)
        {
            castle.SimulateCastle(1f / 60f); Step(player, 1);
            if (i > 10 && player.Grounded) break;
        }
        castle.TryGetLandingTarget(out Vector3 settleTarget, out Vector3 settleUp);
        Check(player.Grounded, "Player settles on actual streamed architecture before stationary reshaping checks; position=" + player.transform.position +
            ", up=" + player.GetComponent<InfinityGravityBody>().Up + ", speed=" + player.VerticalSpeed + ", landing=" + settleTarget + ", landingUp=" + settleUp +
            ", generation=" + castle.LandingGeneration + ", block=" + player.Motor.LastStepBlock);
        int commandsBeforeRest = castle.ShiftCommandsIssued;
        bool observedMotion = false, observedRest = false;
        for (int i = 0; i < 1800; i++)
        {
            castle.SimulateCastle(1f / 30f);
            Step(player, 2);
            bool moving = root.GetComponentsInChildren<InfinityCastleBuilder>().Any(piece => piece.IsMoving);
            observedMotion |= moving;
            if (observedMotion && !moving) observedRest = true;
            if (i % 60 == 0) Check(castle.ValidateNoBuildingOverlaps(out reason), "Moving building clearance " + i + ": " + reason);
        }
        Check(observedMotion && observedRest && castle.ShiftCommandsIssued > commandsBeforeRest,
            "Random reshaping settles into a quiet interval while the player stands still");
        // Walk both connector orientations, including the real stair mesh, with the actual motor.
        foreach (Vector2Int direction in castle.randomNearbyCastle ? Array.Empty<Vector2Int>() : new[] { Vector2Int.right, Vector2Int.up })
        {
            Check(castle.TryGetTraversal(Vector2Int.zero, direction, out Vector3 start, out Vector3 end), "Connector exists " + direction);
            player.transform.position = start; player.ResetFallVelocity();
            player.GetComponent<StarterAssetsInputs>().move = Vector2.zero; Step(player, 15);
            Vector3 forward = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
            player.GetComponent<StarterAssetsInputs>().move = new Vector2(forward.x, forward.z);
            int frames = Mathf.CeilToInt(Vector3.ProjectOnPlane(end - start, Vector3.up).magnitude / player.MoveSpeed * 60f) + 15;
            Step(player, frames);
            Check(Vector3.ProjectOnPlane(player.transform.position - end, Vector3.up).magnitude < 2f &&
                Mathf.Abs(player.transform.position.y - end.y) < 0.3f, "Walkable end-to-end connection " + direction + " reached=" + player.transform.position + " expected=" + end);
            player.transform.position = end; player.ResetFallVelocity();
            player.GetComponent<StarterAssetsInputs>().move = Vector2.zero; Step(player, 15);
            player.GetComponent<StarterAssetsInputs>().move = new Vector2(-forward.x, -forward.z); Step(player, frames);
            Check(Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).magnitude < 2f &&
                Mathf.Abs(player.transform.position.y - start.y) < 0.3f, "Walkable reverse/uphill connection " + direction +
                " reached=" + player.transform.position + " expected=" + start + " grounded=" + player.Grounded + " normal=" + player.Motor.GroundHit.normal + " step=" + player.Motor.LastStepBlock);
        }
        player.GetComponent<StarterAssetsInputs>().move = Vector2.zero;
        // A gap away from the bridges must still have a fixed physical landing within three layers.
        player.transform.position = initial + new Vector3(castle.RouteSpacing * 0.5f, 0f, castle.RouteSpacing * 0.5f);
        player.ResetFallVelocity(); float fallStart = player.transform.position.y;
        bool landed = false;
        for (int i = 0; i < 1200; i++)
        {
            castle.SimulateCastle(1f / 60f); Step(player, 1);
            if (i > 10 && player.Grounded) { landed = true; break; }
        }
        Check(landed && fallStart - player.transform.position.y <= castle.LayerSpacing * 3.1f, "Falling lands within three architectural layers");
        Check(root.GetComponentsInChildren<Transform>(true).Length == count, "No runtime instantiation after long fall");
        Check(castle.IsCastleSurface(player.Motor.GroundHit.collider), "Falling lands on castle architecture, not a terminal safety sheet");
        TestInfiniteDescent(castle, player, count);
        TestDirectionalLanding(castle, player, count);
        results.WriteLine($"Pool={castle.PoolCapacity}, active={castle.ActiveBuildingCount}, links={castle.ActiveConnectionCount}, spacing={castle.LayerSpacing:F2}");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
    }
    public static void RunBaseCorners()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        results = new StreamWriter(Path.GetFullPath("../castle-base-corners.txt"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ThirdPersonController player = Player(new Vector3(0f, 15f, 0f));
            GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab"));
            DynamicInfinityCastle castle = root.GetComponent<DynamicInfinityCastle>();
            castle.player = player.transform; castle.castleLighting = false; castle.periodicRebuilding = false;
            castle.simultaneousShifts = 0; castle.rideMovingBuildings = false;
            castle.InitializeCastle(); Step(player, 60);
            TestNearbyTraversal(castle, player, 0);
            results.WriteLine("ALL CHECKS PASSED");
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
        catch (Exception error) { results.WriteLine(error); results.Close(); EditorApplication.Exit(1); return; }
        results.Close();
    }
    private static void TestNearbyTraversal(DynamicInfinityCastle castle, ThirdPersonController player, int travelLegs = 8)
    {
        InfinityGravityBody gravity = player.GetComponent<InfinityGravityBody>();
        StarterAssetsInputs input = player.GetComponent<StarterAssetsInputs>();
        Vector3 initial = player.transform.position;
        float furthestTravel = 0f;
        int objects = castle.GetComponentsInChildren<Transform>(true).Length;
        for (int leg = 0; leg < travelLegs; leg++)
        {
            Vector3 preference = (Vector3.forward + Vector3.right).normalized;
            Check(castle.TryGetNearbyWalkConnection(preference, out Vector3 start, out Vector3 end), "Connected walking exit after travel " + leg);
            WalkTo(castle, player, start);
            WalkTo(castle, player, end);
            furthestTravel = Mathf.Max(furthestTravel, (player.transform.position - initial).magnitude);
            Check(player.Grounded && Vector3.ProjectOnPlane(player.transform.position - end, Vector3.up).magnitude < 2f &&
                Mathf.Abs(player.transform.position.y - end.y) < 0.35f, "Walk stairs without jumping after travel " + leg +
                "; position=" + player.transform.position + ", expected=" + end + ", up=" + gravity.Up + ", block=" + player.Motor.LastStepBlock);
            Check(castle.ValidateNearbyRouteCoverage(out string routeError), "Route coverage after walking " + leg + ": " + routeError);
            Check(castle.ValidateNoBuildingOverlaps(out string overlap), "Walking and reshaping clearance " + leg + ": " + overlap);
        }
        if (travelLegs > 0) Check(furthestTravel > castle.LayerSpacing * 2f, "Walking reaches beyond the starting neighborhood; maximum distance=" + furthestTravel);
        Check(objects == castle.GetComponentsInChildren<Transform>(true).Length, "Walking route refill reuses the original pools");
        // Test a real gravity corner in the streamed neighborhood, not just the starting structure.
        bool accessibleWall = castle.TryGetNearbyWallApproach(out Vector3 approach, out Vector3 wallDirection, out Vector3 wallUp, out InfiniteCastleGrid wallBase);
        if (!accessibleWall) DescribeFaceGrowth(castle, player);
        Check(accessibleWall, "Sideways wall has an accessible approach");
        player.transform.position = approach; player.ResetFallVelocity(); gravity.RestoreDefaultGravity();
        castle.SimulateCastle(0.02f); Step(player, 40);
        input.move = new Vector2(wallDirection.x, wallDirection.z);
        for (int i = 0; i < 140; i++)
        {
            if (Vector3.Dot(gravity.Up, wallUp) > 0.99f && player.Grounded) input.move = Vector2.zero;
            castle.SimulateCastle(1f / 60f); Step(player, 1);
        }
        Check(Vector3.Dot(gravity.Up, wallUp) > 0.99f && player.Grounded,
            "Walking onto a corner landing enters sideways gravity without jumping; position=" + player.transform.position + ", up=" + gravity.Up +
            ", expected=" + wallUp + ", grounded=" + player.Grounded + ", approach=" + approach + ", direction=" + wallDirection +
            ", speed=" + player.VerticalSpeed + ", block=" + player.Motor.LastStepBlock);
        Check(wallBase != null && wallBase.isActiveAndEnabled, "Gravity corner leads to a real building base");
        CastleWalkableSurface wallBalcony = wallBase.GetComponent<CastleWalkableSurface>();
        Vector3 entry = wallBase.transform.InverseTransformPoint(player.transform.position);
        entry.x = Mathf.Clamp(entry.x, -wallBalcony.Width * 0.5f + 0.8f, wallBalcony.Width * 0.5f - 0.8f);
        entry.z = Mathf.Clamp(entry.z, -wallBalcony.Depth * 0.5f + 0.8f, wallBalcony.Depth * 0.5f - 0.8f);
        entry.y = 0f;
        WalkTo(castle, player, wallBase.transform.TransformPoint(entry) + wallUp * 0.06f, false);
        Check(player.Grounded && Vector3.Dot(gravity.Up, wallUp) > 0.99f, "Walk from the gravity corner onto the building's base balcony; position=" +
            player.transform.position + ", target=" + wallBase.transform.TransformPoint(entry) + ", up=" + gravity.Up + ", expected=" + wallUp +
            ", grounded=" + player.Grounded + ", block=" + player.Motor.LastStepBlock);
        Check(castle.TryGetNearbyCeilingApproach(out Vector3 ceilingApproach), "Streamed wall has a connected ceiling corner");
        if (TryBalconyDetour(player, ceilingApproach, out Vector3 firstCorner, out Vector3 secondCorner))
        {
            WalkTo(castle, player, firstCorner, false);
            if ((secondCorner - firstCorner).sqrMagnitude > 0.01f) WalkTo(castle, player, secondCorner, false);
        }
        for (int i = 0; i < 1500; i++)
        {
            castle.TryGetNearbyCeilingApproach(out ceilingApproach);
            if (Vector3.Dot(gravity.Up, Vector3.down) > 0.99f && player.Grounded) break;
            Vector3 delta = Vector3.ProjectOnPlane(ceilingApproach - player.transform.position, wallUp);
            if (delta.magnitude < 0.3f) break;
            delta.Normalize(); Vector3 forward = player.MovementForward;
            input.move = new Vector2(Vector3.Dot(delta, Vector3.Cross(wallUp, forward).normalized), Vector3.Dot(delta, forward));
            castle.SimulateCastle(1f / 60f); Step(player, 1);
        }
        Check(player.Grounded && ((Vector3.Dot(gravity.Up, wallUp) > 0.99f &&
            Vector3.Distance(player.transform.position, ceilingApproach) < 1f) || Vector3.Dot(gravity.Up, Vector3.down) > 0.99f),
            "Walk along the wall to its moving ceiling landing; position=" + player.transform.position + ", expected=" + ceilingApproach +
            ", up=" + gravity.Up + ", block=" + player.Motor.LastStepBlock);
        for (int i = 0; i < 1800; i++)
        {
            if (Vector3.Dot(gravity.Up, Vector3.down) > 0.99f && player.Grounded) break;
            Vector3 forward = player.MovementForward;
            input.move = new Vector2(Vector3.Dot(Vector3.down, Vector3.Cross(gravity.Up, forward).normalized), Vector3.Dot(Vector3.down, forward));
            if (Vector3.Dot(gravity.Up, Vector3.down) > 0.99f && player.Grounded) input.move = Vector2.zero;
            castle.SimulateCastle(1f / 60f); Step(player, 1);
        }
        input.move = Vector2.zero;
        Check(Vector3.Dot(gravity.Up, Vector3.down) > 0.99f && player.Grounded, "Walking around the next corner enters inverted gravity naturally; position=" +
            player.transform.position + ", up=" + gravity.Up + ", grounded=" + player.Grounded + ", block=" + player.Motor.LastStepBlock);
        int verifiedFrames = 0;
        foreach (Vector3 up in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.down })
        {
            if (!castle.TryGetNearbyGravitySurface(up, out Vector3 point, out InfiniteCastleGrid zone))
            { results.WriteLine("INFO No base-balcony branch currently uses " + up + "; an unreachable lower face is not substituted."); continue; }
            verifiedFrames++;
            Check(Vector3.Dot(zone.transform.up, up) > 0.99f && zone.name == "Connected Castle Base",
                "Streamed alternate gravity uses the building's correctly oriented base balcony " + up);
            Check((point - player.transform.position).magnitude < castle.LayerSpacing * 2f, "Alternate gravity is in the nearby neighborhood " + up +
                "; surface=" + point + ", initial=" + initial + ", current=" + player.transform.position);
            player.transform.position = point; player.ResetFallVelocity(); gravity.RestoreDefaultGravity();
            gravity.EnterZone(zone);
            for (int i = 0; i < 90; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
            Check(Vector3.Dot(gravity.Up, up) > 0.99f && player.Grounded, "Actual building gravity zone supports player " + up);
            input.jump = true; float height = Vector3.Dot(player.transform.position, up), peak = height;
            for (int i = 0; i < 90; i++)
            {
                castle.SimulateCastle(1f / 60f); Step(player, 1); peak = Mathf.Max(peak, Vector3.Dot(player.transform.position, up));
            }
            Check(peak - height > 0.7f && player.Grounded && Vector3.Dot(gravity.Up, up) > 0.99f,
                "Jump and landing on gravity-oriented building " + up + "; peak=" + (peak - height) +
                ", grounded=" + player.Grounded + ", up=" + gravity.Up + ", position=" + player.transform.position);
            if (up == Vector3.right)
            {
                bool hasExit = castle.TryGetNearbyWalkConnection(Vector3.forward, out Vector3 sourcePort, out Vector3 destination);
                if (!hasExit) DescribeFaceGrowth(castle, player);
                Check(hasExit, "Occupied alternate-gravity face has a continuous walking exit");
                WalkTo(castle, player, sourcePort);
                WalkTo(castle, player, destination);
                Check(player.Grounded && Vector3.Dot(gravity.Up, up) > 0.99f &&
                    Vector3.Distance(player.transform.position, destination) < 1f, "Walk the opposite-gravity stair connection without jumping; position=" +
                    player.transform.position + ", expected=" + destination + ", up=" + gravity.Up + ", grounded=" + player.Grounded + ", block=" + player.Motor.LastStepBlock);
            }
            gravity.LeaveZone(zone);
        }
        Check(verifiedFrames >= 3, "Streamed neighborhood retains several actual base-gravity orientations; frames=" + verifiedFrames);
        player.transform.position = initial; gravity.RestoreDefaultGravity(); player.ResetFallVelocity();
        castle.SimulateCastle(0.02f); Step(player, 30);
    }
    private static void DescribeFaceGrowth(DynamicInfinityCastle castle, ThirdPersonController player)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        results.WriteLine("FACE SUPPORT=" + player.Motor.GroundHit.collider.name + "; pool=" +
            castle.GetType().GetField("nearbyPool", flags).GetValue(castle) + "; clock=" + castle.GetType().GetField("nearbyClock", flags).GetValue(castle));
        foreach (object node in (System.Collections.IEnumerable)castle.GetType().GetField("nearbyNodes", flags).GetValue(castle))
        {
            Type type = node.GetType();
            var piece = (InfinityCastleBuilder)type.GetField("piece").GetValue(node);
            if (Vector3.Distance(piece.WorldBounds.ClosestPoint(player.transform.position), player.transform.position) > 90f) continue;
            results.WriteLine("FACE " + piece.name + "; bounds=" + piece.WorldBounds + "; up=" + type.GetField("up").GetValue(node) +
                "; attempts=" + type.GetField("faceGrowthAttempt").GetValue(node) + "; next=" + type.GetField("nextFaceGrowthAt").GetValue(node) +
                "; moving=" + piece.IsMoving + "; surface=" + type.GetField("surface").GetValue(node));
        }
        results.Flush();
    }
    private static void WalkTo(DynamicInfinityCastle castle, ThirdPersonController player, Vector3 target, bool aroundTower = true)
    {
        if (aroundTower && TryBalconyDetour(player, target, out Vector3 first, out Vector3 second))
        {
            WalkTo(castle, player, first, false);
            if ((second - first).sqrMagnitude > 0.01f) WalkTo(castle, player, second, false);
        }
        StarterAssetsInputs input = player.GetComponent<StarterAssetsInputs>();
        input.jump = false; input.sprint = false;
        for (int i = 0; i < 1600; i++)
        {
            Vector3 up = player.GetComponent<InfinityGravityBody>().Up;
            Vector3 delta = Vector3.ProjectOnPlane(target - player.transform.position, up);
            if (delta.magnitude < 0.3f) break;
            delta.Normalize(); Vector3 forward = player.MovementForward;
            input.move = new Vector2(Vector3.Dot(delta, Vector3.Cross(up, forward).normalized), Vector3.Dot(delta, forward));
            castle.SimulateCastle(1f / 60f); Step(player, 1);
        }
        input.move = Vector2.zero;
        for (int i = 0; i < 20; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
    }
    private static bool TryBalconyDetour(ThirdPersonController player, Vector3 target, out Vector3 first, out Vector3 second)
    {
        first = second = default;
        Collider support = player.Motor.GroundHit.collider;
        InfinityCastleBuilder building = support != null ? support.GetComponentInParent<InfinityCastleBuilder>() : null;
        if (building == null) return false;
        CastleWalkableSurface balcony = building.GetComponentInChildren<CastleWalkableSurface>();
        if (balcony == null) return false;
        Transform face = balcony.transform, model = building.transform.GetChild(0);
        Bounds obstacle = CastleGeometry.TransformBounds(CastleGeometry.RendererBoundsInRoot(model), face.worldToLocalMatrix * model.localToWorldMatrix);
        obstacle.Expand(new Vector3(1.4f, 0f, 1.4f));
        obstacle.center = new Vector3(obstacle.center.x, 0f, obstacle.center.z);
        obstacle.size = new Vector3(obstacle.size.x, 1000f, obstacle.size.z);
        Vector3 start = face.InverseTransformPoint(player.transform.position), end = face.InverseTransformPoint(target);
        start.y = end.y = 0f;
        bool Clear(Vector3 a, Vector3 b) => !obstacle.IntersectRay(new Ray(a, (b - a).normalized), out float hit) || hit > (b - a).magnitude;
        if (Clear(start, end)) return false;
        Vector3 min = obstacle.min - Vector3.one * 0.2f, max = obstacle.max + Vector3.one * 0.2f;
        Vector3[] corners = { new(min.x, 0f, min.z), new(min.x, 0f, max.z), new(max.x, 0f, min.z), new(max.x, 0f, max.z) };
        float best = float.PositiveInfinity;
        foreach (Vector3 a in corners) foreach (Vector3 b in corners)
        {
            if (!Clear(start, a) || (a != b && !Clear(a, b)) || !Clear(b, end)) continue;
            float length = Vector3.Distance(start, a) + Vector3.Distance(a, b) + Vector3.Distance(b, end);
            if (length >= best) continue;
            best = length; first = face.TransformPoint(a); second = face.TransformPoint(b);
        }
        return best < float.PositiveInfinity;
    }
    private static void TestInfiniteDescent(DynamicInfinityCastle castle, ThirdPersonController player, int objectCount)
    {
        for (int drop = 0; drop < 6; drop++)
        {
            player.transform.position = new Vector3(castle.RouteSpacing * (drop + 0.45f),
                15f - castle.LayerSpacing * (drop * 4 + 5), castle.RouteSpacing * (drop + 0.53f));
            player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
            player.GetComponent<InfinityGravityBody>().SetGravity(Vector3.down, 22f, true, 8f);
            player.ResetFallVelocity();
            float start = player.transform.position.y; bool landed = false;
            for (int i = 0; i < 1200; i++)
            {
                castle.SimulateCastle(1f / 60f); Step(player, 1);
                if (i > 10 && player.Grounded) { landed = true; break; }
            }
            castle.TryGetLandingTarget(out Vector3 target, out Vector3 landingUp);
            Check(landed && start - player.transform.position.y < castle.LayerSpacing * 3.1f, "Continued descent lands on rebuilt castle " + drop +
                "; grounded=" + landed + ", start=" + start + ", end=" + player.transform.position + ", up=" +
                player.GetComponent<InfinityGravityBody>().Up + ", target=" + target + ", landing up=" + landingUp);
            Check(castle.CurrentRouteLevel >= drop * 4 + 4, "Connected floors stream vertically " + drop + "; level=" + castle.CurrentRouteLevel);
            Check(castle.ValidateNoBuildingOverlaps(out string reason), "Vertical streaming clearance " + drop + ": " + reason);
            Check(castle.IsCastleSurface(player.Motor.GroundHit.collider), "Descent remains inside usable architecture " + drop);
            Check(castle.GetComponentsInChildren<Transform>(true).Length == objectCount, "Descent reuses initial pools " + drop);
        }
        Check(castle.LandingGeneration > 0 && castle.ActiveLandingDistrictCount > 0, "Director assembles populated landing districts during falls");
    }
    private static void TestDirectionalLanding(DynamicInfinityCastle castle, ThirdPersonController player, int objectCount)
    {
        Vector3[] ups = { Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.down };
        InfinityGravityBody gravity = player.GetComponent<InfinityGravityBody>();
        for (int direction = 0; direction < ups.Length; direction++)
        {
            Vector3 up = ups[direction];
            player.transform.position = new Vector3(1500f + direction * 500f, -2800f, 1700f);
            gravity.SetGravity(-up, 22f, true, 8f); player.ResetFallVelocity();
            Vector3 start = player.transform.position; bool landed = false;
            for (int i = 0; i < 1400; i++)
            {
                castle.SimulateCastle(1f / 60f); Step(player, 1);
                if (i > 10 && player.Grounded) { landed = true; break; }
            }
            castle.TryGetLandingTarget(out Vector3 target, out Vector3 landingUp);
            Check(landed && Vector3.Dot(start - player.transform.position, up) < castle.LayerSpacing * 3.1f,
                "Pooled landing catches alternate gravity " + up + "; landed=" + landed + ", start=" + start +
                ", end=" + player.transform.position + ", up=" + gravity.Up + ", speed=" + player.VerticalSpeed +
                ", ground=" + player.Motor.GroundHit.collider + ", ceiling=" + player.Motor.CeilingCollider +
                ", target=" + target + ", landingUp=" + landingUp + ", zone=" + gravity.CurrentZone +
                ", zone point=" + (gravity.CurrentZone != null ? gravity.CurrentZone.SurfacePoint : Vector3.zero));
            Check(castle.ValidateNoBuildingOverlaps(out string error), "Alternate-gravity district clearance: " + error);
            Check(castle.GetComponentsInChildren<Transform>(true).Length == objectCount, "Alternate gravity reuses pools " + up);
        }
        gravity.RestoreDefaultGravity();
    }
    public static void RunDirectionalFalls()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch-mode project.");
        results = new StreamWriter(Path.GetFullPath("../castle-directional.txt"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ThirdPersonController player = Player(new Vector3(0f, 15f, 0f));
            GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab"));
            DynamicInfinityCastle castle = root.GetComponent<DynamicInfinityCastle>();
            castle.player = player.transform; castle.castleLighting = false; castle.InitializeCastle();
            Step(player, 60);
            TestDirectionalLanding(castle, player, root.GetComponentsInChildren<Transform>(true).Length);
            results.WriteLine("ALL DIRECTIONAL CHECKS PASSED");
        }
        catch (Exception error)
        { results.WriteLine(error); results.Close(); Debug.LogException(error); EditorApplication.Exit(1); return; }
        results.Close();
    }
    private static void TestRearrangementSweep(GameObject root)
    {
        InfinityCastleBuilder piece = root.GetComponentsInChildren<InfinityCastleBuilder>(true).First(p => !p.gameObject.activeSelf);
        Vector3 originalCentre = piece.WorldBounds.center;
        Quaternion originalRotation = piece.transform.rotation;
        Vector3 pivot = originalCentre + new Vector3(9f, 17f, -12f);
        Vector3 offset = new(17f, -13f, 9f);
        Quaternion target = Quaternion.AngleAxis(137f, new Vector3(1f, 2f, 3f).normalized) * originalRotation;
        Bounds sweep = piece.SweepRearrange(pivot, offset, target);
        piece.BeginRearrange(pivot, offset, target, 2f, sweep);
        piece.Tick(1f, true);
        Check(Vector3.Distance(piece.WorldBounds.center, originalCentre) < 0.001f, "Paused rearrangement does not advance its physical pose");
        for (int i = 0; i < 1000; i++)
        {
            piece.Tick(0.002f, false);
            Bounds current = piece.WorldBounds;
            if (!sweep.Contains(current.min) || !sweep.Contains(current.max)) Check(false, "Continuous rotation escaped its reserved sweep at " + i);
        }
        Check(Quaternion.Angle(piece.transform.rotation, target) < 0.02f && piece.Visibility == 1f,
            "Rotating motion stays inside its sweep and remains fully visible");
        piece.Place(originalCentre, originalRotation);
    }
    private static void TestNearFieldMotion(DynamicInfinityCastle castle, GameObject root, ThirdPersonController player)
    {
        Collider support = player.Motor.GroundHit.collider;
        Vector3 occupiedPosition = player.transform.position;
        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        int nearBefore = castle.NearFieldMotionCount, cornersBefore = castle.NearbyCornerMotionCount;
        foreach (Vector3 direction in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
        {
            Check(castle.DirectShift(direction), "A quiet shift selects close playable architecture " + direction);
            Check(castle.TryGetNearFieldMovingSurface(out _, out _, out float distance) && distance <= castle.NearFieldRadius,
                "Physical motion begins beside the player, not only in distant decoration; distance=" + distance);
            InfinityCastleBuilder[] moving = root.GetComponentsInChildren<InfinityCastleBuilder>().Where(p => p.IsMoving).ToArray();
            Vector3[] centres = moving.Select(p => p.WorldBounds.center).ToArray();
            Quaternion[] rotations = moving.Select(p => p.transform.rotation).ToArray();
            for (int i = 0; i < 360; i++)
            {
                castle.SimulateCastle(1f / 60f); Step(player, 1);
                if (!player.Grounded || player.Motor.GroundHit.collider != support || moving.Any(p => !p.gameObject.activeSelf || p.Visibility != 1f))
                    Check(false, "Near-field motion removed occupied support or visible architecture at frame " + i);
                if (i % 30 != 0) continue;
                Check(castle.ValidateNoBuildingOverlaps(out string error), "Close moving building clearance: " + error);
                Check(castle.ValidateNearbyConnectionGravity(out string zone), "Moving stairs and gravity corners retain their gravity: " + zone);
                Check(castle.ValidateNearbyConnectionClearance(out string clearance), "Close moving connection clearance: " + clearance);
            }
            Check(moving.Where((piece, i) => Vector3.Distance(centres[i], piece.WorldBounds.center) > 0.25f ||
                Quaternion.Angle(rotations[i], piece.transform.rotation) > 1f).Any(), "The selected close structure physically changes pose");
            Check(Vector3.Distance(occupiedPosition, player.transform.position) < 0.15f, "Close rearrangement protects the occupied floor");
        }
        Check(castle.NearFieldMotionCount >= nearBefore + 4 && castle.NearbyCornerMotionCount > cornersBefore,
            "Near-field shifts include gravity-corner architecture: " + (castle.NearFieldMotionCount - nearBefore) + " close pieces, " +
            (castle.NearbyCornerMotionCount - cornersBefore) + " gravity corners");
        Check(root.GetComponentsInChildren<Transform>(true).Length == objects, "Close shifts reuse the prewarmed objects");
    }
    private static void TestRebuilding()
    {
        ThirdPersonController player = Player(new Vector3(0f, 15f, 0f));
        GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab"));
        DynamicInfinityCastle castle = root.GetComponent<DynamicInfinityCastle>();
        castle.player = player.transform; castle.castleLighting = false; castle.periodicRebuilding = false;
        castle.simultaneousShifts = 0; // Isolate whole-castle choreography from independent ambient shifts.
        castle.rideMovingBuildings = false; // Also retain coverage of the optional stationary-floor mode.
        castle.InitializeCastle(); Step(player, 60);
        TestRearrangementSweep(root);
        Check(castle.PoolCapacity <= 272, "Android building pool has a fixed 272-module ceiling");
        Check(castle.mobileDecorationPrefabs.Count == castle.buildingPrefabs.Count &&
            castle.mobileDecorationPrefabs.All(p => p != null && p.GetComponentsInChildren<MeshCollider>().Length == 0),
            "All original building variants have lightweight distant prefabs");
        TestNearFieldMotion(castle, root, player);
        for (int i = 0; i < 30; i++) { castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) { castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(player.Grounded, "Player remains supported at the Android 30 FPS timestep");
        Check(allocated <= 1024, "Idle castle and motor allocation over 60 frames: " + allocated + " bytes");
        InfinityGravityBody gravity = player.GetComponent<InfinityGravityBody>();
        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        int coordinatedDistricts = 0, coordinatedBuildings = 0;
        foreach (Vector3 up in new[] { Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.down })
        {
            Check(castle.TryGetNearbyGravitySurface(up, out Vector3 point, out InfiniteCastleGrid zone), "Rebuild has a supporting face " + up);
            player.transform.position = point; player.ResetFallVelocity();
            gravity.SetGravity(-up, 22f, true, 8f); gravity.EnterZone(zone);
            for (int i = 0; i < 180; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
            for (int i = 0; i < 1800 && (castle.MovingBuildingCount > 0 || castle.HasPendingLayout || castle.IsRebuilding); i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
            Check(player.Grounded && Vector3.Dot(gravity.Up, up) > 0.99f, "Player settles before rebuild " + up);
            Collider support = player.Motor.GroundHit.collider;
            Vector3 before = player.transform.position;
            int signature = castle.NearbyLayoutSignature, generation = castle.RebuildGeneration;
            Check(castle.DirectRebuild(CastleGeometry.Orientation(up) * Vector3.forward), "Rebuild wave starts " + up +
                "; moving=" + castle.NearbyMovingCount + ", buildings=" + castle.ActiveBuildingCount + ", links=" + castle.ActiveConnectionCount);
            Check(castle.LastRebuildSelectedCount > 24 && castle.LastRebuildSelectedCount >= castle.ActiveBuildingCount * 0.8f,
                "Whole-castle selection " + castle.LastRebuildSelectedCount + "/" + castle.ActiveBuildingCount + "; protected=" + castle.LastRebuildProtectedCount);
            InfinityCastleBuilder[] visible = root.GetComponentsInChildren<InfinityCastleBuilder>(true).Where(p => p.gameObject.activeSelf).ToArray();
            Vector3[] lastCentres = visible.Select(p => p.WorldBounds.center).ToArray();
            Quaternion[] lastRotations = visible.Select(p => p.transform.rotation).ToArray();
            for (int i = 0; i < 1800 && castle.IsRebuilding; i++)
            {
                castle.SimulateCastle(1f / 60f); Step(player, 1);
                if (!castle.ValidateRebuildDistrictMotion(out string districtError)) Check(false, districtError);
                for (int piece = 0; piece < visible.Length; piece++)
                {
                    InfinityCastleBuilder building = visible[piece];
                    if (!building.gameObject.activeSelf || building.Visibility < 1f)
                        Check(false, "A rebuilding module disappeared: " + building.name);
                    if (Vector3.Distance(lastCentres[piece], building.WorldBounds.center) > 2f ||
                        Quaternion.Angle(lastRotations[piece], building.transform.rotation) > 3f)
                        Check(false, "A rebuilding module teleported: " + building.name);
                    lastCentres[piece] = building.WorldBounds.center; lastRotations[piece] = building.transform.rotation;
                }
                if (!player.Grounded || player.Motor.GroundHit.collider != support || Vector3.Dot(gravity.Up, up) < 0.99f)
                    Check(false, "Rebuild preserves occupied support " + up + " frame " + i + "; position=" + player.transform.position + ", up=" + gravity.Up);
                if (i % 60 == 0)
                {
                    Check(castle.ValidateNoBuildingOverlaps(out string overlap), "Rebuild clearance " + up + ": " + overlap);
                    Check(castle.ValidateNearbyRouteCoverage(out string route), "Rebuild keeps connected exits " + up + ": " + route);
                    Check(castle.ValidateNearbyConnectionGravity(out string entrance), "Moving stairs retain entrance gravity " + up + ": " + entrance);
                    Check(castle.ValidateNearbyConnectionClearance(out string stairOverlap), "Moving stair clearance " + up + ": " + stairOverlap);
                }
            }
            Check(!castle.IsRebuilding && castle.RebuildGeneration == generation + 1 && castle.NearbyLayoutSignature != signature,
                "Rebuild completes with a different layout " + up);
            Check(castle.LastRebuildMovedCount >= castle.LastRebuildSelectedCount * 0.8f,
                "Whole-castle physical motion committed " + castle.LastRebuildMovedCount + "/" + castle.LastRebuildSelectedCount +
                " modules; rotated=" + castle.LastRebuildRotatedCount + "; blocked=" + castle.LastRebuildBlockedCount + " " + up);
            Check(castle.LastRebuildRotatedCount > 24, "Castle rotates visible original buildings " + up);
            Check(castle.LastRebuildNearMovedCount > 0,
                "Whole-castle choreography changes adjacent architecture: " + castle.LastRebuildNearMovedCount +
                " close buildings, " + castle.LastRebuildCornerMovedCount + " gravity corners " + up);
            coordinatedDistricts += castle.LastRebuildDistrictCount;
            coordinatedBuildings += castle.LastRebuildDistrictBuildingCount;
            Check(castle.LastRebuildDistrictBuildingCount >= castle.LastRebuildDistrictCount * 2,
                "Connected wings move as districts: " + castle.LastRebuildDistrictCount + " groups, " + castle.LastRebuildDistrictBuildingCount + " buildings " + up);
            Check(visible.All(p => p.gameObject.activeSelf && p.Visibility == 1f), "Rebuild keeps the same visible objects, without fading or replacement " + up);
            Check(visible.All(p => p.MotionGravityZone == null || Vector3.Dot(p.MotionGravityZone.gravityDirection, -p.transform.up) > 0.99f),
                "Rotated buildings carry gravity matching their physical orientation " + up);
            Check(player.Motor.GroundHit.collider == support, "Rebuild preserves occupied support throughout the wave " + up);
            Check(Vector3.Distance(before, player.transform.position) < 0.15f && Vector3.Dot(castle.LastRebuildUp, up) > 0.99f,
                "Rebuild uses occupied gravity without displacing the player " + up);
            Check(castle.ValidateNearbyConnectionGravity(out string connections), "Rebuilt stairs match their endpoint gravity: " + connections);
            Check(root.GetComponentsInChildren<Transform>(true).Length == objects, "Rebuild reuses prewarmed pools " + up);
        }
        Check(castle.RebuiltRouteCount > 0, "Rebuild changes connected architecture, not only decoration");
        Check(coordinatedDistricts > 0 && coordinatedBuildings >= coordinatedDistricts * 2,
            "Choreography physically moves connected wings: " + coordinatedDistricts + " groups, " + coordinatedBuildings + " buildings across six gravity frames");
        for (int i = 0; i < 1800 && (castle.MovingBuildingCount > 0 || castle.HasPendingLayout || castle.IsRebuilding); i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        int jumpGeneration = castle.RebuildGeneration;
        Check(castle.DirectRebuild(player.MovementForward), "Jump safety starts with a whole-castle rebuild");
        player.GetComponent<StarterAssetsInputs>().jump = true;
        for (int i = 0; i < 90; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(player.Grounded && castle.IsRebuilding, "Ordinary jumping does not cancel the castle-wide rebuild");
        for (int i = 0; i < 1800 && castle.IsRebuilding; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(castle.RebuildGeneration == jumpGeneration + 1, "Castle finishes rearranging after an ordinary jump");
        Check(castle.DirectRebuild(player.MovementForward), "Approach safety starts with physical rearrangement");
        for (int i = 0; i < 600 && !castle.TryGetRebuildingSurface(out _, out _, true); i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(castle.TryGetRebuildingSurface(out _, out _, true), "Approach fixture targets a coordinated moving wing");
        castle.TryGetRebuildingSurface(out Vector3 approached, out Vector3 approachUp, true);
        player.transform.position = approached; player.ResetFallVelocity(); gravity.SetGravity(-approachUp, 22f, true, 8f);
        for (int i = 0; i < 300; i++)
        {
            castle.SimulateCastle(1f / 60f); Step(player, 1);
            if (!castle.ValidateRebuildDistrictMotion(out string districtError)) Check(false, "District approach stop: " + districtError);
        }
        Check(player.Grounded && Vector3.Distance(player.transform.position, approached) < 0.15f,
            "Approaching a moving building stops it safely at its current pose");
        for (int i = 0; i < 1800 && (castle.MovingBuildingCount > 0 || castle.HasPendingLayout || castle.IsRebuilding); i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(castle.DirectRebuild(player.MovementForward), "Fall safety starts with a rebuild in progress");
        int beforeFall = castle.RebuildGeneration;
        player.transform.position += gravity.Up * castle.LayerSpacing * 7f; player.ResetFallVelocity();
        for (int i = 0; i < 60; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(!castle.IsRebuilding && castle.RebuildGeneration == beforeFall, "Significant falling safely stops physical rearrangement");
        for (int i = 0; i < 1800; i++)
        { castle.SimulateCastle(1f / 60f); Step(player, 1); if (player.Grounded && castle.NearbyMovingCount == 0) break; }
        Check(player.Grounded, "Fall during rearrangement still ends on solid castle architecture");
        Check(root.GetComponentsInChildren<Transform>(true).Length == objects, "Approach and fall cancellation keep the original pools");
        gravity.RestoreDefaultGravity();
        castle.periodicRebuilding = true; castle.rebuildInterval = new Vector2(10f, 10f);
        int prior = castle.RebuildGeneration;
        for (int i = 0; i < 3600 && castle.RebuildGeneration == prior; i++) { castle.SimulateCastle(1f / 60f); Step(player, 1); }
        Check(castle.RebuildGeneration > prior, "Director starts a timed rebuild while the player remains stationary");
        castle.periodicRebuilding = false;
        Collider mobileSupport = player.Motor.GroundHit.collider;
        Vector3 mobilePosition = player.transform.position;
        InfinityCastleBuilder[] mobileVisible = root.GetComponentsInChildren<InfinityCastleBuilder>(true).Where(p => p.gameObject.activeSelf).ToArray();
        int mobileGeneration = castle.RebuildGeneration;
        Check(castle.DirectRebuild(player.MovementForward), "Android timestep starts a visible rearrangement");
        for (int i = 0; i < 1800 && castle.IsRebuilding; i++)
        {
            castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f);
            if (!castle.ValidateRebuildDistrictMotion(out string districtError)) Check(false, "Android district motion: " + districtError);
            if (!player.Grounded || player.Motor.GroundHit.collider != mobileSupport || mobileVisible.Any(p => !p.gameObject.activeSelf || p.Visibility != 1f))
                Check(false, "Android motion lost visible architecture or occupied support at frame " + i);
            if (i % 30 == 0)
            {
                Check(castle.ValidateNoBuildingOverlaps(out string error), "Android physical motion clearance: " + error);
                Check(castle.ValidateNearbyConnectionGravity(out string entrance), "Android moving stair gravity: " + entrance);
                Check(castle.ValidateNearbyConnectionClearance(out string stairs), "Android moving stair clearance: " + stairs);
            }
        }
        Check(castle.RebuildGeneration == mobileGeneration + 1 && Vector3.Distance(mobilePosition, player.transform.position) < 0.15f,
            "Visible pooled rearrangement completes safely at the Android 30 FPS timestep");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
    }
    public static void InspectArchitecture()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Inspect in an isolated project.");
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Danish/inifinity_castle_pack.fbx");
        using var output = new StreamWriter(Path.GetFullPath("../castle-authored-frame.txt"));
        foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>(true))
            output.WriteLine(mesh.name + ": world=" + mesh.transform.rotation.eulerAngles + ", local=" + mesh.transform.localRotation.eulerAngles +
                ", up=" + mesh.transform.up + ", scale=" + mesh.transform.lossyScale + ", mesh=" + mesh.sharedMesh.name + ", bounds=" + mesh.sharedMesh.bounds);
    }
    public static void RunRebuilding()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch-mode project.");
        results = new StreamWriter(Path.GetFullPath("../castle-rebuilding.txt"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestRebuilding(); results.WriteLine("ALL REBUILD CHECKS PASSED");
        }
        catch (Exception error)
        { results.WriteLine(error); results.Close(); Debug.LogException(error); EditorApplication.Exit(1); return; }
        results.Close();
    }
    public static void RunRiding()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch-mode project.");
        results = new StreamWriter(Path.GetFullPath("../castle-riding.txt"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestScreenLook(); TestRidingCastle(); TestPlayground(); results.WriteLine("ALL RIDE CHECKS PASSED");
        }
        catch (Exception error) { results.WriteLine(error); Debug.LogException(error); results.Close(); EditorApplication.Exit(1); return; }
        results.Close();
    }
    private static void TestRidingCastle()
    {
        ThirdPersonController player = Player(new Vector3(0f, 15f, 0f));
        GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab"));
        DynamicInfinityCastle castle = root.GetComponent<DynamicInfinityCastle>();
        castle.player = player.transform; castle.castleLighting = false; castle.periodicRebuilding = false; castle.simultaneousShifts = 0;
        castle.InitializeCastle();
        Check(castle.rideMovingBuildings, "Default castle enables occupied-building shifts");
        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        foreach (Vector3 up in new[] { Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.down })
        {
            Check(castle.TryGetNearbyGravitySurface(up, out Vector3 point, out InfiniteCastleGrid zone), "Ride has a supporting gravity face " + up);
            player.transform.position = point; player.ResetFallVelocity();
            var gravity = player.GetComponent<InfinityGravityBody>(); gravity.SetGravity(-up, 22f, true, 8f); gravity.EnterZone(zone);
            for (int i = 0; i < 600; i++) { castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); }
            Check(player.Grounded, "Rider settles " + up);
            int rides = castle.RideMotionCount;
            float travelled = 0f;
            for (int attempt = 0; attempt < 12 && castle.RideMotionCount == rides; attempt++)
            {
                Collider support = player.Motor.GroundHit.collider;
                Transform platform = support.GetComponentInParent<InfinityCastleBuilder>().transform;
                Vector3 local = platform.InverseTransformPoint(player.transform.position), before = player.transform.position;
                castle.DirectShift(CastleGeometry.Orientation(up) * (attempt % 2 == 0 ? Vector3.forward : Vector3.right));
                for (int i = 0; i < 240; i++)
                {
                    castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f);
                    if (!player.Grounded || player.Motor.GroundHit.collider != support || Vector3.Dot(gravity.Up, up) < 0.99f ||
                        Vector3.Distance(local, platform.InverseTransformPoint(player.transform.position)) > 0.12f)
                        Check(false, "Occupied platform must carry the rider without changing support/gravity " + up + " frame=" + i);
                    if (i % 30 != 0) continue;
                    Check(castle.ValidateNoBuildingOverlaps(out string overlap), "Ride building clearance: " + overlap);
                    Check(castle.ValidateNearbyConnectionClearance(out string clearance), "Ride moving-path clearance: " + clearance);
                    Check(castle.ValidateNearbyConnectionGravity(out string entrance), "Ride path gravity: " + entrance);
                    if (up == Vector3.down && castle.RideMotionCount > rides && i == 60)
                    {
                        Check(platform.GetComponent<InfinityCastleBuilder>().IsMoving, "Jump fixture begins on a physically moving occupied building");
                        player.GetComponent<StarterAssetsInputs>().jump = true;
                        float height = Vector3.Dot(player.transform.position, up), peak = height;
                        for (int frame = 0; frame < 90; frame++)
                        {
                            castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f);
                            peak = Mathf.Max(peak, Vector3.Dot(player.transform.position, up));
                        }
                        Check(!platform.GetComponent<InfinityCastleBuilder>().IsMoving && player.Grounded &&
                            player.Motor.GroundHit.collider == support && peak - height > 0.8f,
                            "Jumping stops occupied motion at its current pose and lands back on the same support");
                        break;
                    }
                }
                travelled = Vector3.Distance(before, player.transform.position);
            }
            Check(castle.RideMotionCount > rides && travelled > 0.5f, "The occupied building physically carries the player " + up + "; travel=" + travelled);
            for (int i = 0; i < 1800 && (castle.HasPendingLayout || castle.MovingBuildingCount > 0); i++)
            { castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); }
            Check(castle.DirectRebuild(player.MovementForward), "Whole-castle rebuild includes rider choreography " + up);
            for (int i = 0; i < 1800 && castle.IsRebuilding; i++) { castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); }
            Check(!castle.IsRebuilding && player.Grounded, "Rider remains supported through the whole wave " + up);
        }
        Check(castle.LargestNearbyTravel >= 8f, "Adjacent architecture can make large spatial shifts; largest=" + castle.LargestNearbyTravel);
        Check(root.GetComponentsInChildren<Transform>(true).Length == objects, "Ride and large shifts reuse their original pools");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
    }
    private static void TestPlayground()
    {
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity", OpenSceneMode.Single);
        DynamicInfinityCastle castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        Check(castle != null && castle.player != null, "Saved Playground has an active castle and assigned player");
        Material skybox = RenderSettings.skybox;
        castle.InitializeCastle();
        Check(skybox != null && RenderSettings.skybox == skybox, "Playground keeps its assigned skybox with castle lighting enabled");
        Check(Camera.main != null && Camera.main.clearFlags == CameraClearFlags.Skybox, "Main camera renders the assigned skybox");
        ThirdPersonController player = castle.player.GetComponent<ThirdPersonController>();
        Step(player, 60); Check(player.Grounded, "Saved Playground player can stand on generated balcony");
        Check(castle.ValidateArchitectureOrientation(out string orientation) && player.GetComponent<InfinityGravityBody>().Up == Vector3.up,
            "Saved Playground starts upright with buildings above their bases: " + orientation);
        Check(castle.ValidateNoBuildingOverlaps(out string error), "Saved Playground building clearance: " + error);
        player.GetComponent<StarterAssetsInputs>().jump = true;
        float start = player.transform.position.y; Step(player, 12);
        Check(player.transform.position.y > start + 0.5f, "Saved Playground jump works with its actual input component");
        Check(!ShaderUtil.ShaderHasError(castle.architectureShader), "Architecture shader compiles");
        results.WriteLine($"Playground: {castle.ActiveBuildingCount} buildings, {castle.ActiveConnectionCount} links, {castle.LayerSpacing:F2} layer spacing.");
    }
    public static void RenderPlayground()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Preview runs only in an isolated batch-mode project.");
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity", OpenSceneMode.Single);
        SessionState.SetBool("InfinityCastle.Preview", true);
        EditorApplication.isPlaying = true;
    }
    private static void StartPreview(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("InfinityCastle.Preview", false)) return;
        SessionState.SetBool("InfinityCastle.Preview", false);
        InfinityCastlePreviewRunner.Begin();
    }
    public static void InspectArt()
    {
        string output = Path.GetFullPath("../castle-art-report.txt");
        using (var writer = new StreamWriter(output))
        {
            foreach (string file in Directory.GetFiles("Assets/prefabs/Danish", "*.prefab"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                writer.WriteLine($"{prefab.name}: scale={prefab.transform.localScale:F3} centre={bounds.center:F3} size={bounds.size:F3}");
                if (prefab.name.StartsWith("stair", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (MeshCollider collider in instance.GetComponentsInChildren<MeshCollider>()) collider.convex = false;
                    Physics.SyncTransforms();
                    for (int i = 0; i <= 10; i++)
                    {
                        Vector3 point = bounds.center + Vector3.forward * Mathf.Lerp(-bounds.extents.z, bounds.extents.z, i / 10f);
                        point.y = bounds.max.y + 1;
                        if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, bounds.size.y + 2))
                            writer.WriteLine($"  z={hit.point.z:F3} surfaceY={hit.point.y:F3} normal={hit.normal:F3}");
                    }
                }
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
        Debug.Log("Castle art inspected: " + output);
    }
}
