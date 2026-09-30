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
            TestCastle();
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
            Step(player, 60); Check(player.Grounded, "Grounding " + up);
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
        castle.player = player.transform; castle.castleLighting = false;
        castle.InitializeCastle();
        Check(castle.architectureFinishes.Count > 0 && castle.architectureFinishes.All(finish => finish.source != null && finish.albedo != null),
            "Original imported architecture textures resolve from the runtime prefab");
        Check(castle.IsInitialized && castle.ActiveConnectionCount == 144 * (castle.playableFloorsBelow + 1), "Connected route layers, including two lower floors");
        Check(castle.ValidateNoBuildingOverlaps(out string reason), "Initial building clearance: " + reason);
        Step(player, 60); Check(player.Grounded, "Player begins on an open, solid balcony");
        int count = root.GetComponentsInChildren<Transform>(true).Length;
        Vector3 initial = player.transform.position;
        for (int move = 0; move < 6; move++)
        {
            player.transform.position = initial + new Vector3(move % 2 == 0 ? castle.RouteSpacing * (move + 1) : -castle.RouteSpacing * (move + 1), 0f, move * castle.RouteSpacing);
            castle.SimulateCastle(0.02f);
            Check(root.GetComponentsInChildren<Transform>(true).Length == count, "Streaming reuses pool " + move);
            Check(castle.ValidateNoBuildingOverlaps(out reason), "Streamed clearance " + move + ": " + reason);
        }
        player.transform.position = initial; castle.SimulateCastle(0.02f);
        for (int i = 0; i < 600; i++)
        {
            castle.SimulateCastle(1f / 30f);
            if (i % 60 == 0) Check(castle.ValidateNoBuildingOverlaps(out reason), "Moving building clearance " + i + ": " + reason);
        }
        Check(root.GetComponentsInChildren<InfinityCastleBuilder>(true).Any(piece => piece.HasShifted), "Castle shifts even while player stands still");
        // Walk both connector orientations, including the real stair mesh, with the actual motor.
        foreach (Vector2Int direction in new[] { Vector2Int.right, Vector2Int.up })
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
        results.WriteLine($"Pool={castle.PoolCapacity}, active={castle.ActiveBuildingCount}, links={castle.ActiveConnectionCount}, spacing={castle.LayerSpacing:F2}");
        UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(player.gameObject);
    }
    private static void TestPlayground()
    {
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity", OpenSceneMode.Single);
        DynamicInfinityCastle castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        Check(castle != null && castle.player != null, "Saved Playground has an active castle and assigned player");
        castle.castleLighting = false; castle.InitializeCastle();
        ThirdPersonController player = castle.player.GetComponent<ThirdPersonController>();
        Step(player, 60); Check(player.Grounded, "Saved Playground player can stand on generated balcony");
        Check(castle.ValidateNoBuildingOverlaps(out string error), "Saved Playground building clearance: " + error);
        player.GetComponent<StarterAssetsInputs>().jump = true;
        float start = player.transform.position.y; Step(player, 12);
        Check(player.transform.position.y > start + 0.5f, "Saved Playground jump works with its actual input component");
        Check(!ShaderUtil.ShaderHasError(castle.architectureShader), "Architecture dissolve shader compiles");
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
