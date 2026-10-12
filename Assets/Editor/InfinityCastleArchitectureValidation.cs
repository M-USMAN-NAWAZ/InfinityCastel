using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InfinityCastleArchitectureValidation
{
    private const string Runtime = "Assets/prefabs/InfinityCastleRuntime.prefab";
    private const string Art = "Assets/InfinityCastle/CastleModules/";
    private const string PreviewPending = "InfinityCastle.ArchitecturePreviewPending";
    private const string PreviewStarted = "InfinityCastle.ArchitecturePreviewStarted";
    private static StreamWriter report;
    private static DynamicInfinityCastle castle;
    private static ThirdPersonController player;
    private static StarterAssetsInputs input;
    private static readonly Collider[] zoneHits = new Collider[256];
    private static readonly HashSet<InfiniteCastleGrid> enteredZones = new();
    private static readonly HashSet<InfiniteCastleGrid> touchingZones = new();

    [MenuItem("Infinity Castle/Use Playable Building Architecture")]
    public static void Install()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(Runtime);
        try
        {
            var generator = root.GetComponent<DynamicInfinityCastle>();
            generator.roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CorrectedRoom/CorrectedRoom.prefab");
            generator.corridorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "HallWayWithTurn1/HallWayWithTurn1.prefab");
            generator.junctionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "HallWay3Way/HallWay3Way.prefab");
            generator.doubleStairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "DoubleStaresBase/DoubleStaresBase.prefab");
            generator.roofAccessStairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "SingleStareBase/SingleStareBase.prefab");
            generator.useBuildingTraversal = true;
            if (!generator.BuildingTraversalEnabled)
                throw new InvalidOperationException("Prepare supplied architecture before installation.");
            generator.randomNearbyCastle = true;
            generator.architectureWingPoolSize = 20;
            generator.perimeterWalkways = false;
            CastleCurrentModelImporter.ApplyTheme(generator);
            PrefabUtility.SaveAsPrefabAsset(root, Runtime);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Check(bool passed, string message)
    {
        report.WriteLine((passed ? "PASS " : "FAIL ") + message); report.Flush();
        if (!passed) throw new InvalidOperationException(message);
    }

    public static void Test()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated validation project.");
        report = new StreamWriter(Path.GetFullPath("../building-architecture-tests.txt"));
        try
        {
            Install();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.simulationMode = SimulationMode.Script;
            GameObject actor = new("Building traversal player");
            var settings = actor.AddComponent<CharacterController>();
            settings.height = 1.8f; settings.radius = .28f; settings.center = Vector3.up * .93f;
            settings.skinWidth = .02f; settings.stepOffset = .25f;
            input = actor.AddComponent<StarterAssetsInputs>();
            actor.AddComponent<InfinityGravityBody>();
            player = actor.AddComponent<ThirdPersonController>(); player.GroundLayers = ~0; player.JumpTimeout = .1f;
            actor.transform.position = Vector3.up * 15f;
            GameObject world = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Runtime));
            castle = world.GetComponent<DynamicInfinityCastle>(); castle.player = actor.transform;
            castle.castleLighting = false; castle.periodicRebuilding = false; castle.useLearnedDirector = false;
            castle.simultaneousShifts = 0;
            castle.InitializeCastle();
            Frame(12);
            Check(castle.BuildingTraversalEnabled && player.Grounded, "Player starts inside a real, supported building room");
            Check(castle.unifiedArchitectureTheme && castle.themeWoodTexture != null && castle.themePaperTexture != null &&
                world.GetComponentsInChildren<InfinityCastleBuilder>().SelectMany(piece => piece.GetComponentsInChildren<Renderer>())
                    .SelectMany(renderer => renderer.sharedMaterials).All(material => material != null &&
                        material.HasProperty("_ThemeStrength") && material.GetFloat("_ThemeStrength") > .99f &&
                        material.GetTexture("_WoodMap") != null && material.GetTexture("_PaperMap") != null),
                "Every active castle renderer uses the shared timber-and-shoji theme");
            Check(castle.ValidateBuildingArchitecture(out string topology), topology);
            Check(castle.ValidateNoBuildingOverlaps(out string overlap), "Initial building clearance: " + overlap);
            Check(castle.BuildingDirectJoinCount > 0 &&
                world.GetComponentsInChildren<CastleArchitectureModule>().Any(module => module.name.StartsWith("SingleStareBase")) &&
                world.GetComponentsInChildren<CastleArchitectureModule>().Any(module => module.name.StartsWith("DoubleStaresBase")),
                "Direct building seams and both supplied stair layouts; optional bridges require free space");
            Check(!world.GetComponentsInChildren<CastleWalkableSurface>().Any(), "No active broad decks or generated perimeter paths");
            CheckRailCollisions();
            int transforms = world.GetComponentsInChildren<Transform>(true).Length;

            int connectionCount = castle.BuildingDirectJoinCount + castle.BuildingStairJoinCount;
            Check(connectionCount >= 2 && castle.BuildingStairJoinCount > 0, "Nearby playable wings have direct doorway joins and a sparse railed bridge");
            for (int join = 0; join < connectionCount; join++)
            {
                Check(castle.TryGetBuildingJoin(join, out Vector3 exit, out Vector3 entry, out Vector3 up, out bool bridge),
                    "Measured inter-building connection exists: " + exit + " -> " + entry);
                Vector3 direction = (entry - exit).normalized;
                player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
                Teleport(exit - direction * .85f + up * .08f);
                WalkTo(entry + direction * 1.05f);
                Check(player.Grounded, bridge ? "Walk across a guarded bridge into another building interior" : "Walk directly between neighboring building interiors");
            }

            castle.TryGetBuildingFloor(0, 0, false, out Vector3 start, out _);
            CrossSeam(0, 0, 1, "Walk through an actual doorway into the adjoining building");

            CrossSeam(1, 0, 1, "Enter the new corner hall from a connected room");
            CastleArchitectureModule hall = Part(1, 1);
            castle.TryGetBuildingSeam(1, 1, 2, out Vector3 hallExit, out Vector3 hallEntry);
            WalkInside(hall, hallExit - (hallEntry - hallExit).normalized * .75f);
            WalkTo(hallEntry + (hallEntry - hallExit).normalized * 1.1f);
            Check(player.Grounded, "Turn inside the authored L hall and exit into another room");
            CrossSeam(2, 0, 1, "Enter the new three-way hall");
            CrossSeam(2, 1, 2, "Three-way hall forward branch connects to a room");
            CrossSeam(2, 1, 3, "Three-way hall side branch connects to a room");

            for (int wing = 1; wing <= 3; wing++)
            {
                CrossSeam(wing, wing == 3 ? 0 : 3, 4, "Enter the stair-to-roof route in wing " + wing);
                castle.TryGetBuildingSeam(wing, 4, 5, out Vector3 stairExit, out Vector3 raisedEntry);
                Vector3 direction = (raisedEntry - stairExit).normalized;
                CastleArchitectureModule flight = Part(wing, 4);
                var lowerEntry = flight.ports.OrderBy(port => port.position.y).First();
                WalkTo(flight.transform.TransformPoint(lowerEntry.position - lowerEntry.outward * 3.3f));
                WalkTo(stairExit - direction * .8f);
                WalkTo(raisedEntry + direction * 1.05f);
                Check(player.Grounded, "Climb the native stair and enter the raised room in wing " + wing);
                CrossSeam(wing, 5, 6, "Raised room opens directly onto the native roof in wing " + wing);
                castle.TryGetBuildingFloor(wing, 7, false, out Vector3 roofFloor, out _);
                Check(Mathf.Abs(player.transform.position.y - roofFloor.y) < .2f, "Reach the roof through the playable interior route in wing " + wing);
            }

            CrossSeam(0, 0, 2, "Enter the single stair model at its lower landing");
            CastleArchitectureModule stairs = Part(0, 2);
            var lower = stairs.ports.OrderBy(port => port.position.y).First();
            WalkTo(stairs.transform.TransformPoint(lower.position - lower.outward * 3.3f));
            Check(player.Grounded && player.transform.position.y > start.y + 1f,
                "Climb the authored stair flight to its upper platform: " + player.transform.position + " " + player.Motor.LastStepBlock);
            CrossSeam(0, 2, 3, "Upper stair platform connects to the raised room");
            castle.TryGetBuildingFloor(0, 3, false, out Vector3 raised, out _);
            CrossSeam(0, 3, 5, "Walk from a doorway directly onto the neighboring native roof");
            castle.TryGetBuildingFloor(0, 6, false, out Vector3 nativeRoof, out _);
            Check(Mathf.Abs(player.transform.position.y - nativeRoof.y) < .2f, "Native roof and adjoining doorway heights match");

            Teleport(start + Vector3.up * .08f); input.jump = true;
            float peak = player.transform.position.y;
            for (int i = 0; i < 80; i++) { Frame(1); peak = Mathf.Max(peak, player.transform.position.y); }
            Check(peak > start.y + .4f && player.Grounded, "Jump inside the room, respecting its ceiling: peak=" + peak + ", floor=" + start.y);
            Teleport(nativeRoof + Vector3.up * .08f); input.jump = true; peak = player.transform.position.y;
            for (int i = 0; i < 80; i++) { Frame(1); peak = Mathf.Max(peak, player.transform.position.y); }
            Check(peak > nativeRoof.y + 1.2f && player.Grounded, "Full outdoor jump and landing on the actual building rooftop");
            CastleArchitectureModule sideways = Part(0, 7);
            Vector3 sidewaysUp = sideways.transform.up;
            Vector3 sidewaysFloor = sideways.WorldFloor;
            float roofEdge = Vector3.Dot(sidewaysFloor - nativeRoof, sidewaysUp) - .95f;
            Teleport(nativeRoof + sidewaysUp * roofEdge + Vector3.up * .08f);
            Vector3 turnRight = Vector3.Cross(castle.PlayerUp, player.MovementForward);
            input.move = new Vector2(Vector3.Dot(sidewaysUp, turnRight), Vector3.Dot(sidewaysUp, player.MovementForward));
            input.jump = true;
            bool gravityTurn = false;
            for (int i = 0; i < 70; i++)
            {
                Frame(1);
                if (Vector3.Dot(castle.PlayerUp, sidewaysUp) > .99f) { gravityTurn = true; break; }
            }
            input.move = Vector2.zero; Frame(15);
            Check(gravityTurn && player.Grounded && Vector3.Dot(castle.PlayerUp, sidewaysUp) > .99f &&
                player.Motor.GroundHit.collider != null && player.Motor.GroundHit.collider.transform.IsChildOf(sideways.transform),
                "Land and stay in the sideways room with naturally changed gravity: " +
                player.transform.position + ", up=" + castle.PlayerUp + ", grounded=" + player.Grounded + ", target=" + sidewaysFloor + ", targetUp=" + sidewaysUp);
            Vector3 returnDirection = Vector3.ProjectOnPlane(nativeRoof - player.transform.position, castle.PlayerUp).normalized;
            bool returned = false;
            for (int i = 0; i < 100; i++)
            {
                WalkWorld(returnDirection, 1);
                if (Vector3.Dot(castle.PlayerUp, Vector3.up) > .99f) { returned = true; break; }
            }
            Frame(15);
            Check(returned && player.Grounded && Mathf.Abs(player.transform.position.y - nativeRoof.y) < .2f,
                "Return from the sideways room to its connected native roof with a natural gravity turn: " + player.transform.position);
            castle.TryGetBuildingSeam(0, 3, 5, out Vector3 roomExit, out Vector3 roofEntry);
            Vector3 doorDirection = (roofEntry - roomExit).normalized;
            WalkTo(roofEntry + doorDirection * .8f);
            WalkTo(roomExit - doorDirection * 1.05f);
            Check(player.Grounded, "Re-enter the adjoining building interior from its native roof");
            player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
            Teleport(start + Vector3.up * .08f);

            Vector3 rideStart = player.transform.position;
            castle.TryGetBuildingFloor(1, 1, false, out Vector3 movingCorridor, out _);
            castle.TryGetBuildingFloor(0, 0, false, out Vector3 connectedRoom, out _);
            Vector3 seam = movingCorridor - connectedRoom;
            Check(castle.DirectShift(Vector3.up), "The occupied connected buildings can physically shift");
            Frame(200);
            Check(player.Grounded && Vector3.Distance(rideStart, player.transform.position) > .8f,
                "Moving building carries the player without losing its connected rooms");
            castle.TryGetBuildingFloor(1, 1, false, out Vector3 shiftedCorridor, out _);
            castle.TryGetBuildingFloor(0, 0, false, out Vector3 shiftedRoom, out _);
            Check(Vector3.Distance(movingCorridor, shiftedCorridor) > .8f && Vector3.Distance(seam, shiftedCorridor - shiftedRoom) < .001f,
                "Long corridor physically moves while its connected doorway offsets remain exact");
            Check(castle.ValidateNoBuildingOverlaps(out overlap), "Shift swept volumes remain clear: " + overlap);

            Frame(20);
            Check(castle.DirectRebuild(Vector3.right), "Whole-castle rebuilding starts");
            for (int i = 0; i < 1600 && castle.IsRebuilding; i++) Frame(1);
            Check(!castle.IsRebuilding && castle.RebuildGeneration > 0 && castle.LastRebuildNearMovedCount >= 4,
                "Whole-castle wave moves nearby room/corridor groups: " + castle.LastRebuildNearMovedCount + " near, " + castle.LastRebuildMovedCount + " total");
            Check(castle.ValidateNoBuildingOverlaps(out overlap), "Rebuilt castle clearance: " + overlap);

            foreach (Vector3 up in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.down })
            {
                player.GetComponent<InfinityGravityBody>().SetGravity(-up, 22f, true, 8f);
                Teleport(new Vector3(400, -500, 300) + up * 100f);
                bool caught = false;
                for (int i = 0; i < 260; i++)
                {
                    Frame(1);
                    if (player.Grounded && castle.IsCastleSurface(player.Motor.GroundHit.collider)) { caught = true; break; }
                }
                Check(caught && Vector3.Dot(castle.PlayerUp, up) > .99f,
                    "Stream an actual building below a fall in gravity " + up + ": " + player.transform.position);
                Check(castle.ValidateNoBuildingOverlaps(out overlap), "Fall recovery clearance " + up + ": " + overlap);
            }
            Check(world.GetComponentsInChildren<Transform>(true).Length == transforms,
                "No new scene objects during traversal, shifting, rebuild or fall streaming");
            report.WriteLine("ALL BUILDING ARCHITECTURE CHECKS PASSED");
        }
        catch (Exception error) { report.WriteLine(error); Debug.LogException(error); report.Dispose(); EditorApplication.Exit(1); return; }
        report.Dispose();
    }

    private static void Teleport(Vector3 point)
    {
        input.move = Vector2.zero; player.transform.position = point; player.ResetFallVelocity(); Frame(10);
    }

    private static void CheckRailCollisions()
    {
        GameObject actor = new("Railing collision probe");
        var settings = actor.AddComponent<CharacterController>();
        settings.height = 1.8f; settings.radius = .28f; settings.center = Vector3.up * .93f;
        settings.skinWidth = .02f; settings.stepOffset = .25f;
        InfinityCharacterMotor motor = actor.AddComponent<InfinityCharacterMotor>(); motor.Initialize(settings);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object pool = typeof(DynamicInfinityCastle).GetField("linkPool", flags).GetValue(castle);
        object link = pool.GetType().GetMethod("Peek").Invoke(pool, null);
        GameObject root = (GameObject)link.GetType().GetField("root").GetValue(link);
        try
        {
            foreach (string kind in new[] { "flat", "flight" })
            {
                object stair = typeof(DynamicInfinityCastle).GetField(kind == "flat" ? "flatStair" : "flightStair", flags).GetValue(castle);
                Check(stair != null, "Measured " + kind + " path available for rail testing");
                float run = (float)stair.GetType().GetField("run").GetValue(stair);
                float rise = (float)stair.GetType().GetField("rise").GetValue(stair);
                float width = (float)stair.GetType().GetField("width").GetValue(stair);
                Vector3 start = new(1000, 1000, 1000);
                typeof(DynamicInfinityCastle).GetMethod("ConfigureLink", flags).Invoke(castle,
                    new object[] { link, start, start + Vector3.forward * run + Vector3.up * rise, Vector3.up,
                        Quaternion.identity, null, null, true });
                GameObject art = (GameObject)link.GetType().GetField(kind).GetValue(link);
                Check(art.activeInHierarchy && root.GetComponentsInChildren<BoxCollider>().Count(collider =>
                    collider.enabled && !collider.isTrigger && collider.name.Contains("Rail Collision")) == 2,
                    "Pooled " + kind + " path has two solid side rails without tread obstacles");
                Vector3 localStart = root.transform.InverseTransformPoint(start);
                foreach (Vector3 up in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.down })
                {
                    // Translate and rotate the same pooled connection, including its collision geometry.
                    root.transform.SetPositionAndRotation(start + up * 8f, Quaternion.FromToRotation(Vector3.up, up));
                    Physics.SyncTransforms();
                    Collider[] geometry = root.GetComponentsInChildren<Collider>().Where(collider => collider.enabled && !collider.isTrigger).ToArray();
                    foreach (float fraction in new[] { .25f, .5f, .75f })
                    {
                        Vector3 at = root.transform.TransformPoint(localStart + Vector3.forward * (run * fraction) + Vector3.up * (rise + 2f));
                        Check(FloorHit(geometry, at, -up, rise + 4f, out RaycastHit floor), "Supported " + kind + " path sample");
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector3 across = root.transform.right * side;
                            actor.transform.SetPositionAndRotation(floor.point + up * .04f, root.transform.rotation);
                            motor.ClearSupport(); motor.Move(across * (width + 1f), up, true, 1f / 30f);
                            float travelled = Vector3.Dot(actor.transform.position - floor.point, across);
                            Check(travelled < width * .5f && travelled > .05f,
                                kind + " rail blocks sideways walking in gravity " + up + ", side=" + side + ", travel=" + travelled);
                        }
                    }
                    Vector3 entry = root.transform.TransformPoint(localStart + Vector3.forward * (run * .12f) + Vector3.up * (rise + 2f));
                    Check(FloorHit(geometry, entry, -up, rise + 4f, out RaycastHit entryFloor), "Supported path entry");
                    actor.transform.SetPositionAndRotation(entryFloor.point + up * .04f, root.transform.rotation);
                    motor.ClearSupport();
                    float travelStep = Mathf.Min(.08f, run / 60f);
                    for (int step = 0; step < Mathf.CeilToInt(run / travelStep); step++)
                        motor.Move(root.transform.forward * travelStep - up * .04f, up, true, 1f / 30f);
                    bool walked = Vector3.Dot(actor.transform.position - entryFloor.point, root.transform.forward) > run * .65f;
                    Check(walked, kind + " path remains walkable between its rails in gravity " + up + (walked ? "" : ": " + motor.LastStepBlock));
                }
                root.SetActive(false);
            }
            foreach (GameObject prefab in new[] { castle.roofAccessStairPrefab, castle.doubleStairPrefab })
            {
                GameObject stairs = UnityEngine.Object.Instantiate(prefab, new Vector3(2000, 1000, 1000), Quaternion.identity);
                try
                {
                    Physics.SyncTransforms();
                    Collider[] geometry = stairs.GetComponentsInChildren<Collider>().Where(collider => collider.enabled && !collider.isTrigger).ToArray();
                    Bounds bounds = stairs.GetComponent<CastleArchitectureModule>().localBounds;
                    int protectedEdges = 0;
                    for (float z = bounds.min.z + .5f; z < bounds.max.z - .5f; z += .6f)
                    for (float x = bounds.min.x + .5f; x < bounds.max.x - .5f; x += .6f)
                    {
                        Vector3 at = stairs.transform.TransformPoint(new Vector3(x, bounds.max.y + 1f, z));
                        if (!FloorHit(geometry, at, Vector3.down, bounds.size.y + 2f, out RaycastHit floor)) continue;
                        actor.transform.SetPositionAndRotation(floor.point + Vector3.up * .04f, Quaternion.identity);
                        CapsuleCollider capsule = actor.GetComponent<CapsuleCollider>();
                        if (geometry.Any(collider => Physics.ComputePenetration(capsule, actor.transform.position, actor.transform.rotation,
                            collider, collider.transform.position, collider.transform.rotation, out _, out float depth) && depth > .015f)) continue;
                        foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                        {
                            Ray ray = default;
                            RaycastHit rail = default; float distance = 1.5f;
                            foreach (float height in new[] { .35f, .55f, .65f, .85f, 1.05f })
                            {
                                Ray sample = new(floor.point + Vector3.up * height, direction);
                                foreach (Collider collider in geometry)
                                    if (collider.Raycast(sample, out RaycastHit hit, distance) && Vector3.Dot(hit.normal, direction) < -.5f)
                                    { distance = hit.distance; rail = hit; ray = sample; }
                            }
                            if (rail.collider == null || distance < .4f) continue;
                            // A climbable riser or the end of an open entrance is not a guardrail.
                            if (FloorHit(geometry, rail.point + direction * .4f + Vector3.up * 2f, Vector3.down, bounds.size.y + 4f,
                                out RaycastHit beyond) && beyond.point.y > floor.point.y + .3f) continue;
                            Vector3 tangent = Vector3.Cross(Vector3.up, direction) * .35f;
                            bool continuous = true;
                            foreach (float side in new[] { -1f, 1f })
                            {
                                Ray beside = new(ray.origin + tangent * side, direction);
                                if (!geometry.Any(collider => collider.Raycast(beside, out RaycastHit hit, distance + .25f) &&
                                    Vector3.Dot(hit.normal, direction) < -.5f)) continuous = false;
                            }
                            if (!continuous) continue;
                            actor.transform.position = floor.point + Vector3.up * .04f;
                            motor.ClearSupport(); motor.Move(direction * (distance + .75f), Vector3.up, true, 1f / 30f);
                            Check(Vector3.Dot(actor.transform.position - floor.point, direction) < distance,
                                prefab.name + " authored railing blocks the player capsule: floor=" + stairs.transform.InverseTransformPoint(floor.point) +
                                ", direction=" + direction + ", rail=" + stairs.transform.InverseTransformPoint(rail.point) +
                                ", actor=" + stairs.transform.InverseTransformPoint(actor.transform.position) + ", step=" + motor.LastStepBlock);
                            protectedEdges++;
                        }
                    }
                    Check(protectedEdges >= 8, prefab.name + " protects its native stair/landing edges: " + protectedEdges + " capsule checks");
                }
                finally { UnityEngine.Object.DestroyImmediate(stairs); }
            }
        }
        finally { root.SetActive(false); UnityEngine.Object.DestroyImmediate(actor); Physics.SyncTransforms(); }
    }

    private static bool FloorHit(Collider[] geometry, Vector3 origin, Vector3 down, float distance, out RaycastHit nearest)
    {
        nearest = default;
        foreach (Collider collider in geometry)
            if (collider.Raycast(new Ray(origin, down), out RaycastHit hit, distance) && Vector3.Dot(hit.normal, -down) > .65f)
            { nearest = hit; distance = hit.distance; }
        return nearest.collider != null;
    }
    private static void Frame(int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            // Edit-mode physics queries work, but gameplay trigger callbacks are not
            // delivered. Reproduce their registration from actual capsule overlaps.
            Physics.SyncTransforms();
            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            Vector3 centre = player.transform.TransformPoint(capsule.center);
            float half = capsule.height * .5f - capsule.radius;
            int overlaps = Physics.OverlapCapsuleNonAlloc(centre + player.transform.up * half,
                centre - player.transform.up * half, capsule.radius, zoneHits, ~0, QueryTriggerInteraction.Collide);
            touchingZones.Clear();
            for (int i = 0; i < overlaps; i++)
            {
                InfiniteCastleGrid zone = zoneHits[i].GetComponent<InfiniteCastleGrid>();
                if (zone != null && zone.isActiveAndEnabled && zoneHits[i].isTrigger) touchingZones.Add(zone);
            }
            InfinityGravityBody body = player.GetComponent<InfinityGravityBody>();
            foreach (InfiniteCastleGrid zone in enteredZones) if (!touchingZones.Contains(zone)) body.LeaveZone(zone);
            foreach (InfiniteCastleGrid zone in touchingZones) if (!enteredZones.Contains(zone)) body.EnterZone(zone);
            enteredZones.Clear(); foreach (InfiniteCastleGrid zone in touchingZones) enteredZones.Add(zone);
            castle.SimulateCastle(1f / 30f); player.Simulate(1f / 30f); Physics.SyncTransforms(); Physics.Simulate(1f / 30f);
        }
    }
    private static void WalkWorld(Vector3 direction, int frames)
    {
        Vector3 forward = player.MovementForward;
        Vector3 right = Vector3.Cross(castle.PlayerUp, forward);
        input.move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward));
        Frame(frames); input.move = Vector2.zero;
    }

    private static CastleArchitectureModule Part(int wing, int index) => castle.GetComponentsInChildren<InfinityCastleBuilder>()
        .First(piece => piece.name == "Playable Building Wing " + wing.ToString("00"))
        .GetComponentsInChildren<CastleArchitectureModule>()[index];

    private static void CrossSeam(int wing, int from, int to, string message)
    {
        Check(castle.TryGetBuildingSeam(wing, from, to, out Vector3 exit, out Vector3 entry), "Measured connection exists: " + message);
        Vector3 direction = (entry - exit).normalized;
        Teleport(exit - direction * .85f + castle.PlayerUp * .08f);
        WalkTo(entry + direction * 1.05f);
        Check(player.Grounded, message + ": " + player.transform.position);
    }

    private static void WalkTo(Vector3 target)
    {
        for (int i = 0; i < 260; i++)
        {
            Vector3 delta = Vector3.ProjectOnPlane(target - player.transform.position, castle.PlayerUp);
            if (delta.magnitude < .13f) { input.move = Vector2.zero; Frame(4); return; }
            Vector3 right = Vector3.Cross(castle.PlayerUp, player.MovementForward);
            Vector3 direction = delta.normalized;
            input.move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, player.MovementForward)) * Mathf.Clamp01(delta.magnitude * 3f);
            Frame(1);
        }
        input.move = Vector2.zero;
        Collider blocker = (Collider)typeof(InfinityCharacterMotor).GetField("stepBlocker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player.Motor);
        CastleArchitectureModule blockedModule = blocker != null ? blocker.GetComponentInParent<CastleArchitectureModule>() : null;
        Check(false, "Traversal blocked at " + player.transform.position + " heading to " + target + ": " + player.Motor.LastStepBlock +
            ", grounded=" + player.Grounded + ", up=" + castle.PlayerUp + ", velocity=" + player.Motor.Velocity +
            ", zone=" + player.GetComponent<InfinityGravityBody>().CurrentZone + ", module=" + blockedModule +
            (blockedModule != null ? ", local=" + blockedModule.transform.InverseTransformPoint(player.transform.position) : ""));
    }

    private static void WalkInside(CastleArchitectureModule module, Vector3 target)
    {
        const float step = .32f;
        int width = Mathf.CeilToInt(module.localBounds.size.x / step), depth = Mathf.CeilToInt(module.localBounds.size.z / step);
        var points = new Vector3[width * depth]; var usable = new bool[points.Length];
        Collider[] geometry = module.GetComponentsInChildren<Collider>().Where(collider => !collider.isTrigger).ToArray();
        GameObject wing = module.GetComponentInParent<InfinityCastleBuilder>().gameObject;
        for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            bool stair = module.kind == CastleArchitectureModule.ModuleKind.Stair;
            Vector3 point = module.transform.TransformPoint(new Vector3(module.localBounds.min.x + (x + .5f) * step,
                stair ? module.localBounds.max.y + .5f : .5f,
                module.localBounds.min.z + (z + .5f) * step));
            foreach (Collider collider in geometry)
                if (collider.Raycast(new Ray(point, -castle.PlayerUp), out RaycastHit hit, stair ? module.localBounds.size.y + 1f : .9f) && Vector3.Dot(hit.normal, castle.PlayerUp) > .65f)
                { points[index] = hit.point; usable[index] = CastleCurrentModelImporter.Clear(wing, hit.point, Vector3.forward, 0); break; }
        }
        int Nearest(Vector3 position)
        {
            int best = -1; float distance = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++) if (usable[i] && (points[i] - position).sqrMagnitude < distance)
            { distance = (points[i] - position).sqrMagnitude; best = i; }
            return best;
        }
        int start = Nearest(player.transform.position), finish = Nearest(target);
        Check(start >= 0 && finish >= 0, "Authored hallway has supported interior samples");
        var map = new System.Text.StringBuilder();
        map.AppendLine("Hall map X/Z min=" + module.localBounds.min + "; start=" + module.transform.InverseTransformPoint(player.transform.position) + "; end=" + module.transform.InverseTransformPoint(target));
        for (int z = depth - 1; z >= 0; z--)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                map.Append(i == start ? 'S' : i == finish ? 'F' : usable[i] ? '.' : points[i] == Vector3.zero ? ' ' : '#');
            }
            map.AppendLine();
        }
        File.WriteAllText(Path.GetFullPath("../hall-clearance.txt"), map.ToString());
        var previous = Enumerable.Repeat(-1, points.Length).ToArray(); var queue = new Queue<int>();
        queue.Enqueue(start); previous[start] = start;
        while (queue.Count > 0 && previous[finish] < 0)
        {
            int at = queue.Dequeue();
            foreach (int next in new[] { at - 1, at + 1, at - width, at + width, at - 2, at + 2, at - width * 2, at + width * 2 })
                if (next >= 0 && next < points.Length && usable[next] && previous[next] < 0 &&
                    Vector3.Distance(points[at], points[next]) < step * 2.1f &&
                    CastleCurrentModelImporter.Clear(wing, (points[at] + points[next]) * .5f, Vector3.forward, 0))
                { previous[next] = at; queue.Enqueue(next); }
        }
        Check(previous[finish] >= 0, "Authored hall has a connected capsule-clear route between its doors");
        var route = new List<int>();
        for (int at = finish; at != start; at = previous[at]) route.Add(at);
        route.Reverse(); foreach (int index in route) WalkTo(points[index]);
        WalkTo(target);
    }

    public static void Preview()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use isolated preview.");
        Install();
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity", OpenSceneMode.Single);
        SessionState.SetBool(PreviewPending, true);
        SessionState.SetFloat(PreviewStarted, (float)EditorApplication.timeSinceStartup);
        RestorePreviewCallback();
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void RestorePreviewCallback()
    {
        // SessionState survives the domain reload when entering Play mode.
        if (!Application.isBatchMode || !SessionState.GetBool(PreviewPending, false)) return;
        EditorApplication.update -= CapturePreview;
        EditorApplication.update += CapturePreview;
    }
    private static void CapturePreview()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(PreviewStarted, 0) > 180)
        {
            SessionState.SetBool(PreviewPending, false);
            EditorApplication.update -= CapturePreview;
            Debug.LogError("Timed out waiting for the Playground architecture preview.");
            EditorApplication.Exit(1);
            return;
        }
        if (!EditorApplication.isPlaying || Time.time < 3f) return;
        SessionState.SetBool(PreviewPending, false);
        EditorApplication.update -= CapturePreview;
        try
        {
            castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
            Camera camera = Camera.main;
            Capture(camera, "building-architecture-player.png");
            GameObject overview = new("Architecture overview"); camera = overview.AddComponent<Camera>();
            camera.transform.position = castle.player.position + new Vector3(35, 32, -46);
            camera.transform.LookAt(castle.player.position + Vector3.forward * 8f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 600f;
            Capture(camera, "building-architecture-overview.png");
            File.WriteAllText(Path.GetFullPath("../building-architecture-preview.txt"), "Playground loaded; " + castle.ActiveBuildingCount + " buildings; " + castle.BuildingWingCount + " playable wings.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Capture(Camera camera, string filename)
    {
        RenderTexture texture = RenderTexture.GetTemporary(1366, 768, 24);
        RenderTexture old = camera.targetTexture, active = RenderTexture.active;
        camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
        Texture2D pixels = new(1366, 768, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 1366, 768), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.GetFullPath("../" + filename), pixels.EncodeToPNG());
        camera.targetTexture = old; RenderTexture.active = active;
        UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(texture);
    }
}
