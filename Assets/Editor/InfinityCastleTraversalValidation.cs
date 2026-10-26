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
    public static void Streaming()
    {
        InfinityCastleArchitectureValidation.Install();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        using var report = new StreamWriter(Path.GetFullPath("../castle-streaming-tests.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        { report.WriteLine((passed ? "PASS " : "FAIL ") + message); if (!passed) throw new InvalidOperationException(message); }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object Field(object value, string name) => value.GetType().GetField(name, flags).GetValue(value);
        try
        {
            var actor = new GameObject("Streaming player"); actor.AddComponent<CharacterController>(); actor.AddComponent<StarterAssetsInputs>();
            actor.AddComponent<InfinityGravityBody>(); actor.AddComponent<ThirdPersonController>(); actor.transform.position = Vector3.up * 15f;
            Camera camera = new GameObject("Streaming camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.farClipPlane = 1024f;
            var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
            castle.player = actor.transform; castle.useLearnedDirector = false; castle.castleLighting = false;
            castle.periodicRebuilding = false; castle.simultaneousShifts = 0; castle.InitializeCastle();
            Check(castle.IsPlayerReady && castle.IsInitialized && castle.PreparedCastleLayerCount >= 2, "Initial surroundings and multiple lower buildings exist before controls unlock");
            var wings = (System.Collections.IList)Field(castle, "buildingWings");
            foreach (object wing in wings)
                if ((bool)Field(wing, "streamingLayer"))
                    Check(((System.Collections.IList)Field(wing, "parts")).Count >= 5, "A prepared lower layer is a full room/corridor/stair assembly, not a single emergency room");
            int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
            var blocks = (System.Collections.IDictionary)Field(castle, "denseBlocks");
            var known = new System.Collections.Generic.HashSet<Vector3Int>();
            foreach (Vector3Int key in blocks.Keys) known.Add(key);
            int births = 0, visibleDirectionBirths = 0;
            foreach (Vector3 direction in new[] { Vector3.down, Vector3.up, Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
            {
                for (int frame = 0; frame < 320; frame++)
                {
                    actor.transform.position += direction * 1.5f;
                    camera.transform.position = actor.transform.position + Vector3.up * 2f;
                    camera.transform.rotation = Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > .9f ? Vector3.forward : Vector3.up);
                    castle.SimulateCastle(1f / 30f);
                    foreach (System.Collections.DictionaryEntry pair in blocks)
                    {
                        var key = (Vector3Int)pair.Key;
                        if (known.Contains(key)) continue;
                        Bounds bounds = (Bounds)Field(pair.Value, "bounds");
                        bool hidden = (bool)typeof(DynamicInfinityCastle).GetMethod("CanRecycleCastleBounds", flags).Invoke(castle, new object[] { bounds });
                        Check(hidden, "New cluster is prepared outside visible/near geometry: " + key);
                        if (bounds.SqrDistance(actor.transform.position) > 200f * 200f &&
                            GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds)) visibleDirectionBirths++;
                        births++;
                    }
                    known.Clear(); foreach (Vector3Int key in blocks.Keys) known.Add(key);
                    if (frame % 30 != 0) continue;
                    float span = (float)typeof(DynamicInfinityCastle).GetField("DistrictSpan", flags | BindingFlags.Static).GetRawConstantValue();
                    Vector3Int cell = Vector3Int.RoundToInt(actor.transform.position / span);
                    for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
                        if (x * x + y * y + z * z <= 4 && !blocks.Contains(cell + new Vector3Int(x, y, z)))
                            throw new InvalidOperationException("Missing inner residency while moving " + direction + " at " + actor.transform.position);
                }
                Check(castle.ValidateDistributedDistricts(out string coverage), "Full surrounding volume after 480 m travel " + direction + ": " + coverage);
                Check(castle.ValidateDistrictDoorways(out string connectedDoors), connectedDoors);
                Check(castle.ValidateDistrictVariety(out string graphVariety), graphVariety);
                int localRooms = blocks.Values.Cast<object>().SelectMany(block => ((Array)Field(block, "rooms")).Cast<object>())
                    .Count(room => (bool)Field(room, "active") && ((Bounds)Field(room, "bounds")).SqrDistance(actor.transform.position) < 100f * 100f);
                if (actor.transform.position.sqrMagnitude > 200f * 200f)
                    Check(localRooms >= 400, "Dense far-travel neighbourhood: " + localRooms + " complete rooms within 100 m");
                Check(castle.ValidateDoorwayClosures(out string nativeDoors), nativeDoors);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward + direction * .3f, Vector3.up).normalized + Vector3.down * .4f);
                Capture(camera, "streaming-" + direction + ".png");
            }
            Check(births > 30 && visibleDirectionBirths > 10, "Continuous preparation works even in the viewed travel direction: " + births + " births; " + visibleDirectionBirths + " ahead beyond the render band");
            Check(castle.PreparedCastleLayerGenerations >= 5, "Repeated deep travel uses varied full building layers: " + castle.PreparedCastleLayerGenerations);
            Check(castle.GetComponentsInChildren<Transform>(true).Length == transforms && castle.DenseCastlePhysicsCount <= 96,
                "Long travel reuses all GameObjects and retains the 96-body physics limit");
            object crossingProbe = blocks.Values.Cast<object>().SelectMany(block => ((Array)Field(block, "crossings")).Cast<object>())
                .First(crossing => (bool)Field(crossing, "active") && Field(crossing, "link") == null);
            Vector3 savedPosition = actor.transform.position;
            Vector3 pathStart = (Vector3)Field(crossingProbe, "start"), pathEnd = (Vector3)Field(crossingProbe, "end");
            actor.transform.position = (pathStart + pathEnd) * .5f;
            camera.transform.position = actor.transform.position + Vector3.up * 12f - Vector3.forward * 10f;
            camera.transform.LookAt(actor.transform.position);
            Color32[] beforePhysics = Capture(camera, "stream-path-before-physics.png");
            typeof(DynamicInfinityCastle).GetMethod("UpdateDistrictPhysics", flags).Invoke(castle, new object[] { 1f });
            Color32[] afterPhysics = Capture(camera, "stream-path-after-physics.png");
            Check(Field(crossingProbe, "link") != null, "The closest path gets its collider lease on the first physics update");
            // Allow a single 8-bit quantization step, not a silhouette/texture/LOD change.
            // Some GPU captures differ by one channel value in one pixel even with identical draws.
            int maxPixelChange = 0;
            for (int pixel = 0; pixel < beforePhysics.Length; pixel++)
            {
                Color32 a = beforePhysics[pixel], b = afterPhysics[pixel];
                maxPixelChange = Mathf.Max(maxPixelChange, Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
            }
            Check(maxPixelChange <= 1,
                "Collider promotion preserves the rendered path to 8-bit precision: maximum channel delta=" + maxPixelChange);
            actor.transform.position = savedPosition;
            foreach (object block in blocks.Values)
                foreach (object crossing in (Array)Field(block, "crossings"))
                {
                    object link = Field(crossing, "link");
                    if (link != null) Check(((Renderer[])Field(link, "renderers")).All(renderer => !renderer.enabled),
                        "Physics promotion leaves the existing instanced path appearance unchanged");
                }
            RenderSettings.fog = false;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.layer = 27; cube.transform.localScale = Vector3.one * 8f;
            cube.GetComponent<Renderer>().sharedMaterial = new Material(castle.architectureShader);
            camera.cullingMask = 1 << 27;
            int Pixels(float distance)
            {
                cube.transform.position = actor.transform.position + Vector3.forward * distance;
                camera.transform.SetPositionAndRotation(cube.transform.position - Vector3.forward * 14f, Quaternion.identity);
                return Capture(camera, "stream-fade-" + distance + ".png").Count(pixel => pixel.r > 20 || pixel.g > 20 || pixel.b > 20);
            }
            int near = Pixels(150f), transition = Pixels(176f), hiddenPixels = Pixels(210f);
            Check(near > transition && transition > 1000 && hiddenPixels == 0,
                "Opaque distance fade hides prepared geometry before reuse: pixels=" + near + "/" + transition + "/" + hiddenPixels);
            report.WriteLine("ALL PREDICTIVE STREAMING CHECKS PASSED");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

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
            if (source.name.StartsWith("Pack"))
            {
                Mesh mesh = source.GetComponentInChildren<MeshFilter>().sharedMesh;
                if (mesh.triangles.Length / 3 > 2000 || source.GetComponent<MeshCollider>().sharedMesh != mesh)
                    throw new InvalidOperationException("Extracted pack staircase exceeds its mesh/collider budget: " + source.name);
                report.WriteLine("PASS " + source.name + ": independent supplied-pack staircase, " + mesh.triangles.Length / 3 + " triangles; no room LOD substitution.");
                continue;
            }
            bool path = castle.pathPrefabs.Contains(source);
            var module = path ? castle.GetComponentsInChildren<LODGroup>(true).Single(group => group.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == source.name + "(Clone)")).gameObject :
                (GameObject)typeof(DynamicInfinityCastle).GetMethod("PrepareInnerCastleModule", flags).Invoke(castle, new object[] { source });
            typeof(DynamicInfinityCastle).GetMethod("FinishArt", flags).Invoke(castle, new object[] { module.transform });
            module.SetActive(true);
            var lod = module.GetComponent<LODGroup>();
            Bounds bounds = path ? CastleGeometry.RendererBoundsInRoot(module.transform) : module.GetComponent<CastleArchitectureModule>().localBounds;
            GameObject reference = new(source.name + " original comparison");
            GameObject original = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/CastleModules/" + source.name + ".fbx"), reference.transform);
            typeof(DynamicInfinityCastle).GetMethod("FitInnerCastleArt", flags | BindingFlags.Static).Invoke(null,
                new object[] { reference.transform, original.transform, bounds });
            typeof(DynamicInfinityCastle).GetMethod("FinishArt", flags).Invoke(castle, new object[] { reference.transform });
            camera.orthographicSize = bounds.size.magnitude * .48f;
            Color32[][] nearViews = new Color32[3][];
            for (int level = -1; level < lod.lodCount; level++)
            {
                reference.SetActive(level == -1); module.SetActive(level != -1);
                if (level >= 0) lod.ForceLOD(level);
                foreach (Renderer renderer in level == -1 ? reference.GetComponentsInChildren<Renderer>() : lod.GetLODs()[level].renderers)
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    report.WriteLine(source.name + " level=" + level + " triangles=" + mesh.triangles.Length / 3 + " submeshes=" + mesh.subMeshCount +
                        " materials=" + renderer.sharedMaterials.Length + " texture=" + AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BaseMap")));
                    if (level >= 0 && (mesh.triangles.Length / 3 > (level == 0 ? 6008 : 2508) || mesh.subMeshCount != 1))
                        throw new InvalidOperationException(source.name + " exceeds the mobile triangle/material budget.");
                    if (renderer.sharedMaterial.GetTexture("_BaseMap") == null)
                        throw new InvalidOperationException(source.name + " has no LOD albedo atlas.");
                }
                int face = 0;
                foreach (Vector3 view in new[] { new Vector3(1f, .7f, -1.2f), new Vector3(-1f, 1.8f, .8f), new Vector3(.01f, -2f, .01f) })
                {
                    camera.transform.position = bounds.center + view.normalized * bounds.size.magnitude * 2f;
                    camera.transform.LookAt(bounds.center);
                    Color32[] pixels = Capture(camera, "lod-" + source.name + "-" + level + "-" + face + ".png");
                    if (level == -1) nearViews[face] = pixels;
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
            module.SetActive(false); reference.SetActive(false);
        }
        report.WriteLine("PASS Mobile near/medium meshes meet triangle/material budgets and preserve original appearance from above, below and the side");
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

    public static void FreeJump()
    {
        using var report = new StreamWriter(Path.GetFullPath("../castle-free-jump-tests.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        { report.WriteLine((passed ? "PASS " : "FAIL ") + message); if (!passed) throw new InvalidOperationException(message); }
        SimulationMode previousMode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            foreach (var timing in new[] { (dt: 1f/30f, jitter: false), (dt: 1f/60f, jitter: false), (dt: 1f/30f, jitter: true) })
            foreach (Vector3 up in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                string label = $"{1f/timing.dt:F0}Hz jitter={timing.jitter} up={up}: ";
                Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.up;
                Vector3 right = Vector3.Cross(up, forward);
                GameObject Surface(Vector3 point, Vector3 normal)
                {
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.transform.SetPositionAndRotation(point-normal*.5f, Quaternion.FromToRotation(Vector3.up, normal));
                    floor.transform.localScale = new Vector3(400,1,400); return floor;
                }
                Surface(Vector3.zero, up);
                var camera = new GameObject("Free jump camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.transform.rotation = Quaternion.LookRotation(forward, up);
                var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/SunBreathers/TanjiroThirdPerson.prefab"));
                var player = actor.GetComponent<ThirdPersonController>();
                var input = actor.GetComponent<StarterAssetsInputs>(); input.analogMovement = true;
                var gravity = actor.GetComponent<InfinityGravityBody>();
                player.MoveSpeed = 5f; player.Initialize();
                int frames = 0; float peak = 0; bool hitCeiling = false;
                void Tick(float seconds)
                {
                    for (float elapsed = 0; elapsed < seconds-.00001f;)
                    {
                        float dt = Mathf.Min(seconds-elapsed, timing.dt*(timing.jitter ? (++frames%2==0 ? .7f : 1.3f) : 1));
                        Vector3 before = actor.transform.TransformPoint(actor.GetComponent<CharacterController>().center);
                        player.Simulate(dt); player.SimulateCamera(dt); Physics.SyncTransforms(); Physics.Simulate(dt);
                        Vector3 after = actor.transform.TransformPoint(actor.GetComponent<CharacterController>().center);
                        if (Vector3.Distance(before, after)>3f) throw new InvalidOperationException(label+"discontinuous flight");
                        peak = Mathf.Max(peak, Vector3.Dot(actor.transform.position, up)); hitCeiling |= player.Motor.HitCeiling;
                        elapsed += dt;
                    }
                }
                void Reset()
                {
                    player.ResetFallVelocity(); gravity.RestoreDefaultGravity(); gravity.SetGravity(-up,22,true,8);
                    actor.transform.SetPositionAndRotation(up*.05f, Quaternion.LookRotation(forward,up));
                    camera.transform.rotation = Quaternion.LookRotation(forward,up);
                    input.MoveInput(Vector2.zero); input.LookInput(Vector2.zero); input.JumpInput(false); input.SprintInput(false); input.UltraJumpInput(false);
                    Tick(.3f); peak = 0; hitCeiling = false;
                    Check(player.Grounded, label+"solid launch floor");
                }
                void Launch(Vector2 movement, bool sprintJump=false)
                {
                    input.MoveInput(movement);
                    if (sprintJump) { input.SprintInput(true); input.JumpInput(true); }
                    else input.UltraJumpInput(true);
                    Tick(timing.dt); input.SprintInput(false);
                    Check(player.IsUltraJumping && !player.Grounded && player.VerticalSpeed>0, label+"free launch without a castle or landing target");
                }
                Reset();
                Check(Mathf.Approximately(player.EffectiveUltraJumpSpeed,19.5f) && Mathf.Approximately(player.EffectiveUltraJumpHeight,13.5f),label+"75 percent power preserved");
                Launch(Vector2.zero); Tick(1.3f);
                Check(Vector3.ProjectOnPlane(actor.transform.position,up).magnitude<.1f && Vector3.Dot(gravity.Up,up)>.99f,
                    label+"stationary jump stays vertical with no timed gravity turn");
                Tick(2f);
                Check(player.Grounded && !player.IsUltraJumping && Mathf.Abs(peak-13.5f)<.6f, label+"natural apex and landing; peak="+peak);
                Reset(); Launch(Vector2.down); Tick(.3f);
                Check(Vector3.Dot(actor.transform.position,forward)<-4f,label+"backward launch follows input rather than camera aim");
                Tick(3f);
                Check(player.Grounded && Vector3.ProjectOnPlane(player.Motor.Velocity,up).magnitude<=player.MoveSpeed+.5f,
                    label+"landing restores normal movement speed");
                Reset(); Launch(new Vector2(1,1)); Tick(.25f);
                Check(Vector3.Dot(actor.transform.position,forward)>2f && Vector3.Dot(actor.transform.position,right)>2f,label+"diagonal launch");
                Reset(); Launch(Vector2.right*.25f); Tick(.3f);
                Check(Vector3.Dot(player.Motor.Velocity,right)>4f && Vector3.Dot(player.Motor.Velocity,right)<6f,label+"analog stick strength controls lateral speed");
                Reset(); Launch(Vector2.up); Tick(.3f); input.MoveInput(Vector2.right); Tick(.65f);
                Check(Vector3.Dot(player.Motor.Velocity,right)>10f && player.IsUltraJumping,label+"steer sideways during flight");
                input.MoveInput(Vector2.down); Tick(.65f);
                Check(Vector3.Dot(player.Motor.Velocity,forward)<-8f,label+"reverse direction during flight");
                input.MoveInput(Vector2.zero); Tick(.45f);
                Check(Vector3.ProjectOnPlane(player.Motor.Velocity,up).magnitude<.5f,label+"release movement to brake");
                Tick(1f); Check(player.Grounded && !player.IsUltraJumping,label+"land where free steering leads");
                Reset(); Launch(Vector2.up,true); Tick(.2f); camera.transform.rotation = Quaternion.LookRotation(right,up); Tick(.7f);
                Check(Vector3.Dot(player.Motor.Velocity,right)>10f,label+"Shift+Space uses the same camera-relative free steering");
                Reset(); input.JumpInput(true); Tick(3f);
                Check(player.Grounded && !player.IsUltraJumping && peak>1f && peak<1.8f,label+"normal jump remains unchanged");
                if (up != Vector3.up) continue;
                Reset(); var ceiling = Surface(Vector3.up*3.2f,Vector3.down); Launch(Vector2.zero); Tick(2f);
                Check(hitCeiling && peak<2f && player.Grounded && !player.IsUltraJumping,label+"ceiling collision stops ascent and permits landing");
                UnityEngine.Object.DestroyImmediate(ceiling);
                Reset(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = new Vector3(0,5,3); wall.transform.localScale = new Vector3(40,20,.2f);
                Launch(Vector2.up); Tick(.3f);
                Check(actor.transform.position.z<3f && player.IsUltraJumping,label+"wall collision does not cancel the jump or tunnel through it");
                input.MoveInput(Vector2.right); Tick(.6f);
                Check(actor.transform.position.x>2f && player.IsUltraJumping,label+"can steer along a wall after contact");
                UnityEngine.Object.DestroyImmediate(wall);
                foreach (Vector3 newUp in new[] { Vector3.right,Vector3.down })
                {
                    Reset(); Launch(Vector2.zero); Tick(.3f);
                    Vector3 landing = newUp==Vector3.right ? Vector3.left*10f : Vector3.up*12f;
                    var floor = Surface(landing,newUp);
                    var zone = new GameObject("Real destination gravity zone").AddComponent<InfiniteCastleGrid>();
                    zone.transform.position = actor.transform.position;
                    zone.Configure(-newUp,Vector3.zero,Vector3.one*100f,5); gravity.EnterZone(zone);
                    Tick(2f);
                    Check(player.Grounded && !player.IsUltraJumping && gravity.CurrentZone==zone && Vector3.Dot(gravity.Up,newUp)>.99f,
                        label+"real zone, not a timer, transfers gravity and lands on "+newUp);
                    zone.enabled = false; gravity.ResolveGravity();
                    Check(Vector3.Dot(gravity.Up,up)>.99f,label+"no airborne gravity override persists after leaving the zone");
                    UnityEngine.Object.DestroyImmediate(zone.gameObject); UnityEngine.Object.DestroyImmediate(floor);
                }
            }
            report.WriteLine("ALL FREE SUPER JUMP CHECKS PASSED");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); EditorApplication.Exit(1); }
        finally { Physics.simulationMode = previousMode; }
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
                    castle.SimulateCastle(dt); player.Simulate(dt); player.SimulateCamera(dt);
                    Physics.SyncTransforms(); Physics.Simulate(dt);
                }
            }
            Frame(30);
            Check(Mathf.Approximately(player.EffectiveUltraJumpSpeed, 19.5f) &&
                Mathf.Approximately(player.EffectiveUltraJumpHeight, 13.5f),
                "Free super-jump speed and height remain 75 percent of the original settings");
            var rooms = castle.GetComponentsInChildren<CastleArchitectureModule>().Where(module => module.name.StartsWith("Gravity Leap Room")).ToArray();
            Check(rooms.Length == 5, "Five real native landing rooms above and on four sides of the starting roof");
            int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
            // Free steering/flight is exercised by FreeJump(), independently
            // of the generated castle. No test should require an auto landing.
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
            RectTransform normalJump = (RectTransform)mobile.Controls.GetComponentsInChildren<UIVirtualButton>().Single(item => item.name == "UI_Virtual_Button_Jump").transform;
            RectTransform superJump = (RectTransform)button.transform;
            Vector3[] normalCorners = new Vector3[4], superCorners = new Vector3[4];
            Canvas.ForceUpdateCanvases(); normalJump.GetWorldCorners(normalCorners); superJump.GetWorldCorners(superCorners);
            Check(Mathf.Abs(normalJump.position.x - superJump.position.x) < 1f && superCorners[0].y > normalCorners[1].y,
                "Super Jump is directly above normal Jump, with a gap and no overlapping hit targets");
            Check(button.GetComponent<UnityEngine.UI.Image>().sprite != null && button.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains("SUPER"),
                "Super Jump has the existing rounded control artwork and a readable label");
            var events = new GameObject("Touch test events").AddComponent<EventSystem>();
            var lookPointer = new PointerEventData(events) { pointerId = 18, position = new Vector2(700f, 300f),
                pointerCurrentRaycast = new RaycastResult { gameObject = mobile.ScreenLook.gameObject } };
            mobile.ScreenLook.OnPointerDown(lookPointer);
            lookPointer.position += Vector2.right * 60f;
            lookPointer.pointerCurrentRaycast = new RaycastResult { gameObject = button.gameObject };
            mobile.ScreenLook.OnDrag(lookPointer); mobile.ScreenLook.FlushLook(1f / 30f, 1080f);
            Check(input.look.x > 0f, "An existing camera swipe continues across a jump button without sticking");
            mobile.ScreenLook.OnPointerUp(lookPointer);
            var pointer = new PointerEventData(events) { pointerId = 17 };
            button.OnPointerDown(pointer); Check(input.ultraJump, "Android Ultra Jump button sends the real input");
            Frame(1); Check(player.IsUltraJumping, "Android Ultra Jump button launches the player");
            button.OnPointerUp(pointer); mobile.enabled = false;
            Check(!input.ultraJump && input.move == Vector2.zero && input.look == Vector2.zero, "Touch release/disable clears ultra jump and movement input");
            player.ResetFallVelocity();
            CheckMotion(castle, actor.transform, camera, Check);
            report.WriteLine("ALL ULTRA JUMP AND GRAVITY TRANSFER CHECKS PASSED");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckMotion(DynamicInfinityCastle castle, Transform actor, Camera camera, Action<bool, string> check)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Field(object target, string name) => target.GetType().GetField(name, fields).GetValue(target);
        object Call(string name, params object[] args) => typeof(DynamicInfinityCastle).GetMethod(name, fields).Invoke(castle, args);
        var joins = (System.Collections.IList)Field(castle, "buildingJoins");
        object join = joins.Cast<object>().First(item => Field(item, "stair") != null);
        var from = (CastleArchitectureModule)Field(join, "from"); var to = (CastleArchitectureModule)Field(join, "to");
        Vector3 a = from.transform.TransformPoint((Vector3)Field(join, "exit"));
        Vector3 b = to.transform.TransformPoint((Vector3)Field(join, "entry"));
        Vector3 saved = actor.position;
        actor.position = (a + b) * .5f + from.transform.up * .05f;
        foreach (string end in new[] { "a", "b" })
        {
            object wing = Field(join, end);
            check((bool)Call("PlayerOccupiesBuildingBridge", (int)Field(wing, "group")) &&
                !(bool)Call("TryShiftBuildingGroup", wing, Vector3.right, true),
                "Standing on a path prevents its " + end + " endpoint from stretching or tilting it");
        }
        actor.position = saved;
        camera.transform.SetPositionAndRotation(saved + Vector3.up * 3f, Quaternion.identity);
        check(!(bool)Call("CanRecycleCastleBounds", new Bounds(camera.transform.position + Vector3.forward * 65f, Vector3.one * 6f)),
            "Visible pooled rooms cannot be removed or appear in front of the player");
        check(!(bool)Call("CanRecycleCastleBounds", new Bounds(actor.position - Vector3.forward * 10f, Vector3.one)),
            "Nearby pooled geometry remains protected even behind the camera");
        check(!(bool)Call("CanRecycleCastleBounds", new Bounds(camera.transform.position - Vector3.forward * 100f, Vector3.one * 6f)),
            "The visible-distance sphere remains resident behind the camera during a fast turn");
        check((bool)Call("CanRecycleCastleBounds", new Bounds(camera.transform.position + Vector3.forward * 280f, Vector3.one * 6f)),
            "Fully hidden geometry can be prepared/reused even directly in the viewing direction");
        typeof(DynamicInfinityCastle).GetField("architectureStreamClock", fields).SetValue(castle, 10000f);
        var wings = (System.Collections.IList)Field(castle, "buildingWings");
        var selected = new System.Collections.Generic.HashSet<int>();
        var travels = new System.Collections.Generic.HashSet<int>();
        var durations = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 18; i++)
        {
            if (castle.DirectShift(Vector3.right))
                foreach (object wing in wings)
                {
                    var piece = (InfinityCastleBuilder)Field(wing, "piece");
                    if (!piece.IsMoving) continue;
                    selected.Add((int)Field(wing, "group"));
                    travels.Add(Mathf.RoundToInt(((Vector3)Field(piece, "motionEnd") - (Vector3)Field(piece, "motionStart")).magnitude * 100f));
                    durations.Add(Mathf.RoundToInt((float)Field(piece, "duration") * 100f));
                }
            Call("UpdateBuildingArchitecture", 30f);
        }
        check(selected.Count >= 3 && travels.Count >= 3 && durations.Count >= 3,
            "Random shifts vary groups, travel and duration: " + selected.Count + "/" + travels.Count + "/" + durations.Count);
        check(castle.ValidateNoBuildingOverlaps(out string error), "Randomized shifts retain collision clearance: " + error);
    }

    public static void Controls()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var actor = new GameObject("Touch layout player"); actor.AddComponent<StarterAssetsInputs>();
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        castle.player = actor.transform; castle.previewTouchControls = true;
        var mobile = castle.gameObject.AddComponent<InfinityCastleMobileRuntime>(); mobile.Configure(castle);
        Camera camera = new GameObject("Touch UI preview camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .10f, .12f);
        Canvas canvas = mobile.Controls.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
        Capture(camera, "super-jump-controls-16x9.png");
        Capture(camera, "super-jump-controls-20x9.png", 1200, 540);
        Debug.Log("Rendered Super Jump above normal Jump at 16:9 and 20:9.");
    }

    private static Color32[] Capture(Camera camera, string name, int width = 960, int height = 540)
    {
        RenderTexture texture = RenderTexture.GetTemporary(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = texture;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = texture;
        Texture2D pixels = new(width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.GetFullPath("../" + name), pixels.EncodeToPNG());
        Color32[] result = pixels.GetPixels32();
        camera.targetTexture = null; RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(texture);
        return result;
    }
}
