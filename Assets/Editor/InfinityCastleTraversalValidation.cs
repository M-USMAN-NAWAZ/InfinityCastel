using System;
using System.IO;
using System.Reflection;
using System.Linq;
using StarterAssets;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InfinityCastleTraversalValidation
{
    public static void Recovery()
    {
        InfinityCastleArchitectureValidation.Install();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        using var report = new StreamWriter(Path.GetFullPath("../castle-startup-recovery.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        {
            report.WriteLine((passed ? "PASS " : "FAIL ") + message);
            if (!passed) throw new InvalidOperationException(message);
        }
        var actor = new GameObject("Warm-up recovery player"); actor.AddComponent<CharacterController>();
        actor.AddComponent<StarterAssetsInputs>(); var gravity = actor.AddComponent<InfinityGravityBody>();
        var controller = actor.AddComponent<ThirdPersonController>(); controller.Initialize();
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        castle.player = actor.transform;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(DynamicInfinityCastle).GetField("controller", flags).SetValue(castle, controller);
        typeof(DynamicInfinityCastle).GetField("gravity", flags).SetValue(castle, gravity);
        typeof(DynamicInfinityCastle).GetProperty("IsPlayerReady").SetValue(castle, true);
        Vector3 checkpoint = new(0f, 30f, 0f);
        typeof(DynamicInfinityCastle).GetField("startupSafePosition", flags).SetValue(castle, checkpoint);
        typeof(DynamicInfinityCastle).GetField("startupSafeUp", flags).SetValue(castle, Vector3.up);
        typeof(DynamicInfinityCastle).GetField("startupSafeRotation", flags).SetValue(castle, Quaternion.identity);
        var floor = (GameObject)typeof(DynamicInfinityCastle).GetMethod("PrepareInnerCastleModule", flags).Invoke(castle, new object[] { castle.roomPrefab });
        floor.transform.position = new Vector3(64f, -8f, 0f); floor.AddComponent<InfinityCastleBuilder>().InitializePooledPiece(castle); floor.SetActive(true);
        void Safety(Vector3 position, float verticalSpeed)
        {
            actor.transform.position = position; controller.ResetFallVelocity();
            typeof(ThirdPersonController).GetField("verticalSpeed", flags).SetValue(controller, verticalSpeed);
            Physics.SyncTransforms(); typeof(DynamicInfinityCastle).GetMethod("UpdateStartupSafety", flags).Invoke(castle, null);
        }
        Vector3 falling = new(64f, 0f, 0f);
        Safety(falling, -8f);
        Check(actor.transform.position == falling, "A deep fall with a real native roof below does not return to spawn");
        Vector3 horizontal = new(1000f, 30f, 0f); Safety(horizontal, -8f);
        Check(actor.transform.position == horizontal, "Horizontal travel beyond 60 metres does not return to spawn");
        Vector3 rising = new(1000f, 0f, 0f); Safety(rising, 5f);
        Check(actor.transform.position == rising, "Ascending jumps below the previous checkpoint are not cancelled");
        Safety(rising, -8f);
        Check(actor.transform.position == checkpoint, "Genuine unsupported falling still recovers to the last safe floor");
        Check(typeof(DynamicInfinityCastle).GetMethod("AvailableBuildingWing", flags).Invoke(castle, new object[] { true }) == null,
            "An unfinished emergency pool does not use negative indices");
    }

    public static void Quality()
    {
        InfinityCastleArchitectureValidation.Install();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.fog = false; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white * .65f;
        var sun = new GameObject("LOD comparison light").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        Camera camera = new GameObject("LOD comparison camera").AddComponent<Camera>();
        camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .06f, .08f);
        using var report = new StreamWriter(Path.GetFullPath("../castle-lod-quality.txt"));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        castle.pathsParent = castle.transform;
        typeof(DynamicInfinityCastle).GetMethod("ReadStairs", flags).Invoke(castle, null);
        foreach (var source in new[] { castle.roomPrefab, castle.corridorPrefab, castle.junctionPrefab, castle.doubleStairPrefab, castle.roofAccessStairPrefab }.Concat(castle.pathPrefabs))
        {
            bool path = castle.pathPrefabs.Contains(source);
            var module = path ? castle.GetComponentsInChildren<LODGroup>(true).Single(group => group.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == source.name + "(Clone)")).gameObject :
                (GameObject)typeof(DynamicInfinityCastle).GetMethod("PrepareInnerCastleModule", flags).Invoke(castle, new object[] { source });
            typeof(DynamicInfinityCastle).GetMethod("FinishArt", flags).Invoke(castle, new object[] { module.transform });
            module.SetActive(true);
            var lod = module.GetComponent<LODGroup>();
            Bounds bounds = path ? CastleGeometry.RendererBoundsInRoot(module.transform) : module.GetComponent<CastleArchitectureModule>().localBounds;
            camera.orthographicSize = bounds.size.magnitude * .48f;
            Color32[][] nearViews = new Color32[3][];
            for (int level = 0; level < lod.lodCount; level++)
            {
                lod.ForceLOD(level);
                foreach (Renderer renderer in lod.GetLODs()[level].renderers)
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    report.WriteLine(source.name + " level=" + level + " triangles=" + mesh.triangles.Length / 3 + " submeshes=" + mesh.subMeshCount +
                        " materials=" + renderer.sharedMaterials.Length + " texture=" + AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BaseMap")));
                    if (renderer.sharedMaterial.GetTexture("_BaseMap") == null)
                        throw new InvalidOperationException(source.name + " has no LOD albedo atlas.");
                }
                int face = 0;
                foreach (Vector3 view in new[] { new Vector3(1f, .7f, -1.2f), new Vector3(-1f, 1.8f, .8f), new Vector3(.01f, -2f, .01f) })
                {
                    camera.transform.position = bounds.center + view.normalized * bounds.size.magnitude * 2f;
                    camera.transform.LookAt(bounds.center);
                    Color32[] pixels = Capture(camera, "lod-" + source.name + "-" + level + "-" + face + ".png");
                    if (level == 0) nearViews[face] = pixels;
                    else
                    {
                        Color32[] high = nearViews[face]; Color32 background = high[0];
                        int intersection = 0, union = 0; double difference = 0d;
                        bool Visible(Color32 pixel) => Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 18;
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            bool a = Visible(high[i]), b = Visible(pixels[i]);
                            if (a || b) union++;
                            if (!a || !b) continue;
                            intersection++;
                            difference += Math.Abs(high[i].r - pixels[i].r) + Math.Abs(high[i].g - pixels[i].g) + Math.Abs(high[i].b - pixels[i].b);
                        }
                        double coverage = (double)intersection / Math.Max(1, union);
                        double colourError = difference / Math.Max(1, intersection) / 765d;
                        report.WriteLine("View=" + face + "; silhouette overlap=" + coverage.ToString("F3") + "; colour error=" + colourError.ToString("F3")); report.Flush();
                        if (coverage < .85 || colourError > .12)
                            throw new InvalidOperationException(source.name + " distant mesh changes its appearance excessively in view " + face);
                    }
                    face++;
                }
            }
            module.SetActive(false);
        }
        report.WriteLine("PASS Near/far supplied meshes preserve authored appearance from above, below and the side");
    }

    public static void Corners()
    {
        using var report = new StreamWriter(Path.GetFullPath("../castle-corner-stability.txt")) { AutoFlush = true };
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.simulationMode = SimulationMode.Script;
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Corner floor";
            floor.transform.position = new Vector3(-2.1f, -.1f, 0f); floor.transform.localScale = new Vector3(5.8f, .2f, 10f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Corner side floor";
            wall.transform.position = new Vector3(.7f, 0f, 0f); wall.transform.localScale = new Vector3(.2f, 8f, 10f);
            var actor = new GameObject("Corner player");
            var settings = actor.AddComponent<CharacterController>(); settings.height = 1.8f; settings.center = Vector3.up * .93f;
            settings.radius = .28f; settings.skinWidth = .02f;
            actor.AddComponent<StarterAssetsInputs>(); var gravity = actor.AddComponent<InfinityGravityBody>();
            var player = actor.AddComponent<ThirdPersonController>(); player.GroundLayers = ~0; player.Initialize();
            var a = new GameObject("Roof gravity").AddComponent<InfiniteCastleGrid>();
            a.Configure(Vector3.down, Vector3.up * 1.5f, new Vector3(10f, 4f, 10f), 3, true, true);
            a.cornerSnapDistance = 2.6f;
            var b = new GameObject("Corner entry gravity").AddComponent<InfiniteCastleGrid>();
            b.transform.SetPositionAndRotation(new Vector3(.8f, 0f, 0f), Quaternion.LookRotation(Vector3.forward, Vector3.right));
            b.Configure(Vector3.left, Vector3.up * .75f, new Vector3(2.8f, 3.2f, 2f), 4, true, true); b.cornerEntryOnly = true;
            var side = new GameObject("Side room gravity").AddComponent<InfiniteCastleGrid>();
            side.transform.SetPositionAndRotation(b.transform.position, b.transform.rotation);
            side.Configure(Vector3.left, Vector3.up * 1.5f, new Vector3(10f, 4f, 10f), 3, true);
            actor.transform.position = new Vector3(.35f, .04f, 0f);
            gravity.EnterZone(a); gravity.EnterZone(side); gravity.EnterZone(b);
            int turns = 0; Vector3 previous = Vector3.up;
            for (int frame = 0; frame < 240; frame++)
            {
                gravity.ResolveGravity(); player.Simulate(1f / 60f); Physics.SyncTransforms(); Physics.Simulate(1f / 60f);
                if (Vector3.Dot(gravity.Up, previous) < .99f) turns++;
                previous = gravity.Up;
            }
            report.WriteLine("Stationary corner turns=" + turns + "; up=" + gravity.Up + "; position=" + actor.transform.position);
            if (turns > 2) throw new InvalidOperationException("Stationary player oscillates between overlapping corner gravity fields.");
            report.WriteLine("PASS Stationary overlap does not rotate back and forth");
            if (!player.Grounded || gravity.Up.x < .99f) throw new InvalidOperationException("Player is not supported on the side floor.");
            var input = actor.GetComponent<StarterAssetsInputs>();
            for (int frame = 0; frame < 120 && gravity.Up.y < .99f; frame++)
            {
                Vector3 forward = player.MovementForward, right = Vector3.Cross(gravity.Up, forward);
                input.MoveInput(new Vector2(Vector3.Dot(Vector3.down, right), Vector3.Dot(Vector3.down, forward)));
                player.Simulate(1f / 60f); Physics.SyncTransforms(); Physics.Simulate(1f / 60f);
            }
            input.MoveInput(Vector2.zero);
            for (int frame = 0; frame < 240; frame++) { player.Simulate(1f / 60f); Physics.SyncTransforms(); Physics.Simulate(1f / 60f); }
            if (!player.Grounded || gravity.Up.y < .99f) throw new InvalidOperationException("Corner latch prevents intentional walking back onto the roof.");
            report.WriteLine("PASS Walking back releases the corner latch and remains stable on the original floor");
            a.enabled = false; b.enabled = false; side.enabled = false; gravity.ResolveGravity();
            if (gravity.CurrentZone != null || gravity.Up.y < .99f) throw new InvalidOperationException("Disabled pooled corner retains its old gravity.");
            report.WriteLine("PASS Disabling pooled corner zones releases their gravity");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void Diagnostics()
    {
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity");
        Material sky = RenderSettings.skybox;
        string description = "Skybox: " + (sky != null ? sky.name + " / " + sky.shader.name : "none");
        if (sky != null)
            foreach (string texture in sky.GetTexturePropertyNames())
                description += "\n" + texture + "=" + sky.GetTexture(texture);
        File.WriteAllText(Path.GetFullPath("../traversal-render-diagnostics.txt"), description);
        Debug.Log(description);
        var castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        castle.InitializeCastle();
        Camera camera = Camera.main;
        camera.transform.position = castle.player.position + Vector3.up * 8f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        Capture(camera, "traversal-full.png");
        typeof(DynamicInfinityCastle).GetMethod("ShutdownDenseDistricts", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(castle, null);
        Capture(camera, "traversal-no-instances.png");
        camera.cullingMask = 0;
        Capture(camera, "traversal-sky-only.png");
    }

    public static void Traversal()
    {
        using var report = new StreamWriter(Path.GetFullPath("../castle-ultra-jump-tests.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        {
            report.WriteLine((passed ? "PASS " : "FAIL ") + message);
            if (!passed) throw new InvalidOperationException(message);
        }
        try
        {
            InfinityCastleArchitectureValidation.Install();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.simulationMode = SimulationMode.Script;
            var actor = new GameObject("Ultra jump player");
            var settings = actor.AddComponent<CharacterController>();
            settings.height = 1.8f; settings.radius = .28f; settings.center = Vector3.up * .93f; settings.skinWidth = .02f;
            var input = actor.AddComponent<StarterAssetsInputs>();
            var gravity = actor.AddComponent<InfinityGravityBody>();
            var player = actor.AddComponent<ThirdPersonController>(); player.GroundLayers = ~0;
            Camera camera = new GameObject("Traversal camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            player.CinemachineCameraTarget = new GameObject("Gravity camera pivot");
            player.CinemachineCameraTarget.transform.SetParent(actor.transform, false);
            player.CinemachineCameraTarget.transform.localPosition = Vector3.up * 1.375f;
            actor.transform.position = Vector3.up * 15f;
            var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
            castle.player = actor.transform; castle.castleLighting = false; castle.useLearnedDirector = false;
            castle.periodicRebuilding = false; castle.simultaneousShifts = 0;
            castle.InitializeCastle();
            bool variableTimeStep = false; int simulatedFrames = 0;
            void Frame(int frames, float dt = 1f / 30f)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    Physics.SyncTransforms();
                    foreach (Collider hit in Physics.OverlapSphere(actor.transform.position + actor.transform.up * .9f, 1f, ~0, QueryTriggerInteraction.Collide))
                    {
                        var zone = hit.GetComponent<InfiniteCastleGrid>();
                        if (hit.isTrigger && zone != null) gravity.EnterZone(zone);
                    }
                    float stepTime = variableTimeStep ? dt * (simulatedFrames++ % 2 == 0 ? .7f : 1.3f) : dt;
                    castle.SimulateCastle(stepTime); player.Simulate(stepTime); player.SimulateCamera(stepTime);
                    Physics.SyncTransforms(); Physics.Simulate(stepTime);
                }
            }
            Frame(30);
            Vector3 start = actor.transform.position;
            var rooms = castle.GetComponentsInChildren<CastleArchitectureModule>().Where(module => module.name.StartsWith("Gravity Leap Room")).ToArray();
            Check(rooms.Length == 5, "Five real native landing rooms above and on four sides of the starting roof");
            int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
            float longestJump = 0f, largestCameraTurn = 0f;
            foreach (var timing in new[] { (dt: 1f / 30f, jitter: false), (dt: 1f / 60f, jitter: false), (dt: 1f / 30f, jitter: true) }) foreach (var room in rooms)
            {
                float dt = timing.dt; variableTimeStep = timing.jitter;
                actor.transform.SetPositionAndRotation(start, Quaternion.identity); player.ResetFallVelocity(); gravity.RestoreDefaultGravity(); Frame(20);
                Vector3 up = room.transform.up, roof = room.transform.TransformPoint(room.roofPoint);
                Check(player.Grounded, "Solid launch roof for " + up);
                Vector3 aim = (roof + up * .93f - (actor.transform.position + Vector3.up * .93f)).normalized;
                camera.transform.rotation = Quaternion.LookRotation(aim, Mathf.Abs(aim.y) > .99f ? Vector3.forward : Vector3.up);
                input.UltraJumpInput(true); Frame(1, dt);
                Check(player.IsUltraJumping && Vector3.Dot(castle.UltraJumpLandingUp, up) > .99f,
                    "Ultra input launches toward native room " + up + " at " + Mathf.RoundToInt(1f / dt) + " Hz; variable=" + variableTimeStep + "; " + castle.UltraJumpBlockReason);
                Check(gravity.CurrentZone == null, "Airborne transfer does not reuse a stale floor-corner pivot");
                longestJump = Mathf.Max(longestJump, Vector3.Distance(start, castle.UltraJumpDestination));
                bool changed = false, continuous = true;
                Vector3 previous = actor.transform.position;
                Quaternion previousCamera = player.CinemachineCameraTarget.transform.rotation;
                for (int frame = 0; frame < Mathf.CeilToInt(6f / dt); frame++)
                {
                    Frame(1, dt); changed |= Vector3.Dot(gravity.Up, up) > .99f;
                    continuous &= Vector3.Distance(previous, actor.transform.position) < 4f;
                    largestCameraTurn = Mathf.Max(largestCameraTurn, Quaternion.Angle(previousCamera, player.CinemachineCameraTarget.transform.rotation));
                    previous = actor.transform.position; previousCamera = player.CinemachineCameraTarget.transform.rotation;
                }
                Check(continuous, "Continuous physical flight without teleporting");
                Check(changed && player.Grounded && Vector3.Dot(gravity.Up, up) > .99f && Vector3.Distance(actor.transform.position, roof) < 4f,
                    "Land and remain supported in target gravity " + up + "; position=" + actor.transform.position + "; target=" + roof + "; grounded=" + player.Grounded);
                Vector3 returnAim = (start + Vector3.up * .93f - (actor.transform.position + up * .93f)).normalized;
                camera.transform.rotation = Quaternion.LookRotation(returnAim, Mathf.Abs(returnAim.y) > .99f ? Vector3.forward : Vector3.up);
                input.SprintInput(true); input.JumpInput(true); Frame(1, dt); input.SprintInput(false);
                Check(player.IsUltraJumping && castle.UltraJumpLandingUp.y > .99f, "Sprint+jump returns from " + up);
                Frame(Mathf.CeilToInt(6f / dt), dt);
                Check(player.Grounded && Vector3.Dot(gravity.Up, Vector3.up) > .99f && Vector3.Distance(actor.transform.position, start) < 5f,
                    "Return to original gravity without teleporting " + up + "; pos=" + actor.transform.position + "; up=" + gravity.Up + "; start=" + start);
            }
            Check(longestJump > 40f, "Real long-distance building jump: " + longestJump.ToString("F1") + " metres");
            Check(largestCameraTurn < 20f, "Gravity camera eases through transfers; largest frame turn=" + largestCameraTurn.ToString("F1") + " degrees");
            castle.TryGetBuildingFloor(0, 6, false, out Vector3 nativeRoof, out _);
            castle.TryGetBuildingFloor(0, 7, false, out Vector3 sideFloor, out Vector3 sideUp);
            gravity.RestoreDefaultGravity(); player.ResetFallVelocity(); actor.transform.rotation = Quaternion.identity;
            actor.transform.position = nativeRoof + sideUp * (Vector3.Dot(sideFloor - nativeRoof, sideUp) - .95f) + Vector3.up * .08f;
            void WalkDirection(Vector3 direction)
            {
                Vector3 forward = player.MovementForward, right = Vector3.Cross(gravity.Up, forward);
                input.MoveInput(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)));
            }
            Frame(1); WalkDirection(sideUp); input.JumpInput(true);
            for (int frame = 0; frame < 100 && Vector3.Dot(gravity.Up, sideUp) < .99f; frame++) Frame(1);
            input.MoveInput(Vector2.zero);
            int cornerTurns = 0; Vector3 lastCornerUp = gravity.Up;
            for (int frame = 0; frame < 240; frame++)
            {
                Frame(1);
                if (Vector3.Dot(gravity.Up, lastCornerUp) < .99f) cornerTurns++;
                lastCornerUp = gravity.Up;
            }
            Check(player.Grounded && Vector3.Dot(gravity.Up, sideUp) > .99f && cornerTurns == 0,
                "Native roof-to-room corner stays supported without idle gravity oscillation; turns=" + cornerTurns);
            for (int frame = 0; frame < 160 && gravity.Up.y < .99f; frame++)
            {
                WalkDirection(Vector3.ProjectOnPlane(nativeRoof - actor.transform.position, gravity.Up).normalized); Frame(1);
            }
            input.MoveInput(Vector2.zero); Frame(120);
            Check(player.Grounded && gravity.Up.y > .99f, "Player can walk back through the native gravity corner without getting stuck");
            Check(castle.GetComponentsInChildren<Transform>(true).Length == transforms, "All transfers reuse existing castle objects");
            Check(castle.ValidateNoBuildingOverlaps(out string overlap), "No castle intersections after transfers: " + overlap);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/StarterAssets/InputSystem/StarterAssets.inputactions");
            Check(actions.FindAction("Player/UltraJump").bindings.Any(binding => binding.path == "<Keyboard>/q") &&
                actions.FindAction("Player/UltraJump").bindings.Any(binding => binding.path == "<Gamepad>/buttonNorth"), "Keyboard and gamepad ultra-jump bindings exist");
            castle.previewTouchControls = true;
            var mobile = castle.gameObject.AddComponent<InfinityCastleMobileRuntime>(); mobile.Configure(castle); mobile.Configure(castle);
            UIVirtualButton button = mobile.Controls.GetComponentsInChildren<UIVirtualButton>().Single(item => item.name == "Ultra Jump");
            var events = new GameObject("Touch test events").AddComponent<EventSystem>();
            var pointer = new PointerEventData(events) { pointerId = 17 };
            button.OnPointerDown(pointer); Check(input.ultraJump, "Android Ultra Jump button sends the real input");
            Frame(1); Check(player.IsUltraJumping, "Android Ultra Jump button launches the player");
            button.OnPointerUp(pointer); mobile.enabled = false;
            Check(!input.ultraJump && input.move == Vector2.zero && input.look == Vector2.zero, "Touch release/disable clears ultra jump and movement input");
            player.ResetFallVelocity();
            report.WriteLine("ALL ULTRA JUMP AND GRAVITY TRANSFER CHECKS PASSED");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static Color32[] Capture(Camera camera, string name)
    {
        RenderTexture texture = RenderTexture.GetTemporary(960, 540, 24);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        Texture2D pixels = new(960, 540, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.GetFullPath("../" + name), pixels.EncodeToPNG());
        Color32[] result = pixels.GetPixels32();
        camera.targetTexture = null; RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(texture);
        return result;
    }
}
