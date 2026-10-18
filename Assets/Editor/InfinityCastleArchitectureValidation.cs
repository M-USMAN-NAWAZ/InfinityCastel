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
    private const string MobileArt = "Assets/InfinityCastle/CastleModules/RuntimeMobile/";
    private const string PreviewPending = "InfinityCastle.ArchitecturePreviewPending";
    private const string PreviewStarted = "InfinityCastle.ArchitecturePreviewStarted";
    private static StreamWriter report;
    private static DynamicInfinityCastle castle;
    private static ThirdPersonController player;
    private static StarterAssetsInputs input;
    private static readonly Collider[] zoneHits = new Collider[256];
    private static readonly HashSet<InfiniteCastleGrid> enteredZones = new();
    private static readonly HashSet<InfiniteCastleGrid> touchingZones = new();
    private static int startupProbe;
    private static float startupProbeTime;
    private static Vector3 startupProbePosition;
    private static Quaternion startupProbeRotation;
    private static bool startupProbeAnalog;

    public static void Exterior()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        InfinityCastleMobileBake.Bake();
    }

    [MenuItem("Infinity Castle/Use Playable Building Architecture")]
    public static void Install()
    {
        CastleRuntimeModelImporter.Prepare();
        GameObject root = PrefabUtility.LoadPrefabContents(Runtime);
        try
        {
            var generator = root.GetComponent<DynamicInfinityCastle>();
            generator.roomPrefab = PrepareMobileModel("CorrectedRoom", generator.architectureShader);
            generator.corridorPrefab = PrepareMobileModel("HallWayWithTurnFixed", generator.architectureShader);
            generator.junctionPrefab = PrepareMobileModel("HallWay3Way", generator.architectureShader);
            generator.doubleStairPrefab = PrepareMobileModel("DoubleStaresBase", generator.architectureShader);
            generator.roofAccessStairPrefab = PrepareMobileModel("SingleStareBase", generator.architectureShader);
            generator.buildingPrefabs = new List<GameObject> { generator.roomPrefab, generator.corridorPrefab,
                generator.junctionPrefab, generator.doubleStairPrefab, generator.roofAccessStairPrefab };
            generator.pathPrefabs = new List<GameObject> { PrepareMobileModel("Path", generator.architectureShader),
                PrepareMobileModel("Stares", generator.architectureShader),
                CastlePackStairsBake.Prepare("stair_1", "PackFlatStair", generator.architectureShader),
                CastlePackStairsBake.Prepare("stair_3", "PackStairFlight", generator.architectureShader) };
            generator.mobileDecorationPrefabs.Clear();
            generator.architectureDistanceModels = new List<GameObject>();
            generator.architectureDistanceMaterials = new List<Material>();
            foreach (string name in new[] { "CorrectedRoom", "HallWay3Way", "SingleStareBase", "DoubleStaresBase", "Path", "Stares", "HallWayWithTurnFixed" })
            {
                string path = MobileArt + name + "_LOD2.fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null) throw new InvalidOperationException("Run Tools/prepare_castle_distance_meshes.py first: " + path);
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false; importer.addCollider = false; importer.isReadable = false;
                importer.SaveAndReimport();
                generator.architectureDistanceModels.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                string atlas = MobileArt + name + "_LOD2Albedo.png";
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(atlas);
                textureImporter.maxTextureSize = 512; textureImporter.mipmapEnabled = true; textureImporter.wrapMode = TextureWrapMode.Clamp;
                textureImporter.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true,
                    maxTextureSize = 512, format = TextureImporterFormat.ASTC_6x6 });
                textureImporter.textureCompression = TextureImporterCompression.Compressed; textureImporter.SaveAndReimport();
                string materialPath = MobileArt + name + "_LOD2.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(generator.architectureShader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlas));
                material.SetVector("_CastleBuild", Vector4.zero);
                material.enableInstancing = true; EditorUtility.SetDirty(material);
                generator.architectureDistanceMaterials.Add(material);
            }
            generator.useLearnedDirector = true;
            const string district = "Assets/InfinityCastle/CastleModules/RuntimeLod/";
            var districtImporter = (ModelImporter)AssetImporter.GetAtPath(district + "CorrectedRoom_LOD3.fbx");
            districtImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            districtImporter.importAnimation = false; districtImporter.addCollider = false; districtImporter.isReadable = false;
            districtImporter.SaveAndReimport();
            generator.architectureDistrictModel = AssetDatabase.LoadAssetAtPath<GameObject>(district + "CorrectedRoom_LOD3.fbx");
            var atlasImporter = (TextureImporter)AssetImporter.GetAtPath(district + "CorrectedRoom_DistrictAlbedo.png");
            atlasImporter.maxTextureSize = 512; atlasImporter.mipmapEnabled = true;
            atlasImporter.textureCompression = TextureImporterCompression.Compressed; atlasImporter.SaveAndReimport();
            Material districtMaterial = AssetDatabase.LoadAssetAtPath<Material>(district + "DistrictRoom.mat");
            if (districtMaterial == null)
            {
                districtMaterial = new Material(generator.architectureShader);
                AssetDatabase.CreateAsset(districtMaterial, district + "DistrictRoom.mat");
            }
            districtMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(district + "CorrectedRoom_DistrictAlbedo.png"));
            districtMaterial.SetVector("_CastleBuild", Vector4.zero);
            districtMaterial.enableInstancing = true; EditorUtility.SetDirty(districtMaterial);
            generator.architectureDistrictMaterial = districtMaterial;
            generator.useBuildingTraversal = true;
            if (!generator.BuildingTraversalEnabled)
                throw new InvalidOperationException("Prepare supplied architecture before installation.");
            generator.randomNearbyCastle = true;
            generator.architectureWingPoolSize = 96;
            generator.decorationPoolSize = 0;
            generator.simultaneousShifts = 3;
            generator.perimeterWalkways = false;
            CastleCurrentModelImporter.ApplyTheme(generator);
            // Preserve the supplied meshes' authored timber, paper panels and lantern textures.
            generator.unifiedArchitectureTheme = false;
            CastleTimberAssemblyBake.Configure(generator);
            CastleDoorwayBake.Configure(generator);
            PrefabUtility.SaveAsPrefabAsset(root, Runtime);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static GameObject PrepareMobileModel(string name, Shader shader)
    {
        string path = MobileArt + name + ".fbx";
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Run Blender with Tools/prepare_castle_distance_meshes.py -- --mobile first: " + path);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
        importer.isReadable = true; importer.addCollider = true;
        importer.SaveAndReimport();
        string atlas = MobileArt + name + "Albedo.png";
        var texture = AssetImporter.GetAtPath(atlas) as TextureImporter;
        if (texture == null) throw new InvalidOperationException("Missing rebaked mobile atlas: " + atlas);
        texture.maxTextureSize = 1024; texture.mipmapEnabled = true; texture.wrapMode = TextureWrapMode.Clamp;
        texture.textureCompression = TextureImporterCompression.Compressed;
        texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true,
            maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6 });
        texture.SaveAndReimport();
        string materialPath = MobileArt + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        material.shader = shader; material.enableInstancing = true;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlas));
        material.SetFloat("_ThemeStrength", 0f); EditorUtility.SetDirty(material);
        material.SetVector("_CastleBuild", Vector4.zero);
        GameObject root = new(name);
        try
        {
            GameObject art = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
            foreach (Renderer renderer in art.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(root, MobileArt + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Check(bool passed, string message)
    {
        report.WriteLine((passed ? "PASS " : "FAIL ") + message); report.Flush();
        if (!passed) throw new InvalidOperationException(message);
    }

    public static void Volume()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use isolated validation.");
        report = new StreamWriter(Path.GetFullPath("../building-volume-tests.txt"));
        try
        {
            Install();
            foreach (int poolSize in new[] { 96 })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                enteredZones.Clear(); touchingZones.Clear();
                Physics.simulationMode = SimulationMode.Script;
                GameObject actor = new("Volume traversal player");
                var settings = actor.AddComponent<CharacterController>();
                settings.height = 1.8f; settings.radius = .28f; settings.center = Vector3.up * .93f;
                settings.skinWidth = .02f; settings.stepOffset = .25f;
                input = actor.AddComponent<StarterAssetsInputs>();
                actor.AddComponent<InfinityGravityBody>();
                player = actor.AddComponent<ThirdPersonController>(); player.GroundLayers = ~0;
                actor.transform.position = Vector3.up * 15f;
                GameObject world = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Runtime));
                castle = world.GetComponent<DynamicInfinityCastle>(); castle.player = actor.transform;
                castle.architectureWingPoolSize = poolSize; castle.mobileBudget = true;
                castle.castleLighting = false; castle.useLearnedDirector = false;
                castle.periodicRebuilding = false; castle.simultaneousShifts = 0;
                castle.InitializeCastle(); Frame(30);
                Check(castle.IsInitialized && castle.BuildingWingCount >= poolSize * .85f,
                    "Pool " + poolSize + " initializes fully without an index exception");
                Check(player.Grounded, "Pool " + poolSize + " starts on a solid native roof: " + player.transform.position);
                Check(castle.ValidateSurroundingCastle(out string coverage), coverage);
                Check(castle.ValidateDistributedDistricts(out string distribution), distribution);
                Check(castle.ValidateNoBuildingOverlaps(out string clearance), "Initial playable clearance " + clearance);
                Check(castle.ValidateBuildingArchitecture(out string topology), topology);
                int transforms = world.GetComponentsInChildren<Transform>(true).Length;
                input.jump = true; Frame(150);
                Check(player.Grounded, "Jump lands on the original roof instead of falling through it");
                Vector3 before = player.transform.position;
                actor.GetComponent<InfinityGravityBody>().RestoreDefaultGravity();
                Vector3 drop = default; bool shaft = false;
                for (int x = -48; x <= 48 && !shaft; x += 8) for (int z = -48; z <= 48 && !shaft; z += 8)
                {
                    Vector3 sample = before + new Vector3(x, 12f, z);
                    // Query the same streamed collision set the player will have at this location.
                    player.transform.position = sample; player.ResetFallVelocity();
                    castle.SimulateCastle(.7f); Physics.SyncTransforms();
                    int obstructions = Physics.OverlapCapsuleNonAlloc(sample + Vector3.up * .4f, sample + Vector3.up * 1.5f,
                        .32f, zoneHits, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int hit = 0; hit < obstructions; hit++)
                        if (!zoneHits[hit].transform.IsChildOf(actor.transform)) { blocked = true; break; }
                    if (blocked) continue;
                    if (Physics.SphereCast(sample + Vector3.up * .4f, .32f, Vector3.down, out RaycastHit floor, 90f, ~0,
                        QueryTriggerInteraction.Ignore) && floor.distance > 24f && floor.normal.y > .9f)
                    {
                        // A sphere grazing a roof corner is not a stable capsule landing.
                        bool supported = true;
                        foreach (Vector3 offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                            if (!floor.collider.Raycast(new Ray(sample + offset * .22f + Vector3.up * .4f, Vector3.down), out RaycastHit pad, 90f) ||
                                pad.normal.y < .9f || Mathf.Abs(pad.point.y - floor.point.y) > .2f) { supported = false; break; }
                        if (!supported) continue;
                        bool vertical = true;
                        for (float depth = 0; depth < floor.distance - 2f && vertical; depth += 1.5f)
                        {
                            Vector3 at = sample + Vector3.up * (1f - depth);
                            int overlaps = Physics.OverlapSphereNonAlloc(at, .7f, zoneHits, ~0, QueryTriggerInteraction.Collide);
                            for (int hit = 0; hit < overlaps; hit++)
                            {
                                var zone = zoneHits[hit].GetComponent<InfiniteCastleGrid>();
                                if (zone != null && zone.isActiveAndEnabled && zone.Contains(at) &&
                                    Vector3.Dot(-zone.gravityDirection, Vector3.up) < .99f) { vertical = false; break; }
                            }
                        }
                        if (vertical)
                        {
                            report.WriteLine("DROP probe=" + sample + "; expected floor=" + floor.point + "; normal=" + floor.normal +
                                "; collider=" + floor.collider.name); report.Flush();
                            drop = sample; shaft = true;
                        }
                    }
                }
                Check(shaft, "A genuine deep shaft exists above already-generated castle geometry");
                Teleport(drop);
                float lowest = player.transform.position.y;
                for (int frame = 0; frame < 300; frame++) { Frame(1); lowest = Mathf.Min(lowest, player.transform.position.y); }
                Check(player.Grounded && lowest < drop.y - 18f && lowest > drop.y - 120f,
                    "Deep jump lands on castle beneath: descent=" + (drop.y - lowest) + ", grounded=" + player.Grounded + ", up=" + castle.PlayerUp);
                Check(castle.BuildingStairJoinCount >= castle.BuildingWingCount - 3,
                    "Every connected building has a rendered, collidable native-model crossing");
                Check(world.GetComponentsInChildren<Transform>(true).Length == transforms,
                    "Runtime generation and deep falls reuse the existing object pool");
                var testedUp = new HashSet<Vector3Int>();
                for (int join = 0; join < castle.BuildingStairJoinCount; join++)
                {
                    if (!castle.TryGetBuildingJoin(join, out Vector3 exit, out Vector3 entry, out Vector3 up, out _)) continue;
                    if (!testedUp.Add(Vector3Int.RoundToInt(up))) continue;
                    InfinityGravityBody body = actor.GetComponent<InfinityGravityBody>();
                    foreach (InfiniteCastleGrid zone in enteredZones) body.LeaveZone(zone);
                    enteredZones.Clear(); body.SetGravity(-up, 22f, true, 100f);
                    actor.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up).sqrMagnitude > .5f ? Vector3.forward : Vector3.right, up);
                    Teleport(exit - (entry - exit).normalized * .7f + up * .08f);
                    WalkTo(entry + (entry - exit).normalized * .7f);
                    Check(player.Grounded, "Native bridge connects two interiors in gravity " + up);
                }
                Check(testedUp.Count >= 4, "Real connected routes span multiple gravity orientations: " + testedUp.Count);
                Check(castle.DirectShift(Vector3.up), "The director can physically move a building without breaking its routes");
                Frame(240);
                Check(player.Grounded, "Player stays supported while the director moves their building");
                Check(castle.ValidateNoBuildingOverlaps(out clearance), "No model intersections after shifting and streaming " + clearance);
                Check(castle.ValidateDoorwayClosures(out string doorReport), doorReport);
                Check(castle.ValidateDistrictDoorways(out string districtDoors), districtDoors);
                Check(castle.TryGetDistrictDoorPair(out Vector3 districtExit, out Vector3 districtEntry, out Vector3 districtUp, out Vector3 districtDirection),
                    "Outer districts contain complete adjoining rooms, not facade meshes");
                InfinityGravityBody districtGravity = actor.GetComponent<InfinityGravityBody>();
                foreach (InfiniteCastleGrid zone in enteredZones) districtGravity.LeaveZone(zone);
                enteredZones.Clear(); districtGravity.SetGravity(-districtUp, 22f, true, 100f);
                actor.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, districtUp).sqrMagnitude > .5f ? Vector3.forward : Vector3.right, districtUp);
                Teleport(districtExit - districtDirection * 2.5f + districtUp * .08f); Frame(30);
                WalkTo(districtEntry + districtDirection * 2.5f);
                Check(player.Grounded && player.Motor.GroundHit.collider != null &&
                    player.Motor.GroundHit.collider.GetComponentInParent<InfinityCastleBuilder>().name.StartsWith("Physical Castle District Room"),
                    "Player walks through actual doors and stays supported inside the outer castle rooms");
                var stairRoute = new List<Vector3>();
                Check(castle.TryGetDistrictStairRoute(stairRoute, out Vector3 stairUp), "Outer floors have an actual multi-segment staircase route");
                report.WriteLine("STAIR route=" + string.Join(" -> ", stairRoute) + "; up=" + stairUp); report.Flush();
                foreach (InfiniteCastleGrid zone in enteredZones) districtGravity.LeaveZone(zone);
                enteredZones.Clear(); districtGravity.SetGravity(-stairUp, 22f, true, 100f);
                actor.transform.rotation = Quaternion.LookRotation(Mathf.Abs(stairUp.y) < .9f ? Vector3.up : Vector3.forward, stairUp);
                Teleport(stairRoute[0] + stairUp * .08f); Frame(30);
                for (int waypoint = 1; waypoint < stairRoute.Count; waypoint++) WalkTo(stairRoute[waypoint]);
                Check(player.Grounded, "Player climbs the supplied pack staircase, turns both corners, and enters the next floor");
                const BindingFlags startupFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DynamicInfinityCastle).GetField("loadingControllerWasEnabled", startupFlags).SetValue(castle, player.enabled);
                typeof(DynamicInfinityCastle).GetField("startupControlsHeld", startupFlags).SetValue(castle, true);
                player.enabled = false;
                bool startupErrorCaught = false;
                try { typeof(DynamicInfinityCastle).GetMethod("ScheduleCastleWork", startupFlags).Invoke(castle, new object[] { BrokenStartupWork() }); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { startupErrorCaught = true; }
                Check(startupErrorCaught && player.enabled && !castle.CastleWorkPending,
                    "A generation exception clears pending work and releases held player controls");
            }
            report.WriteLine("ALL SIX-SIDED VOLUME CHECKS PASSED");
        }
        catch (Exception error)
        {
            report.WriteLine(error); Debug.LogException(error); report.Dispose(); EditorApplication.Exit(1); return;
        }
        report.Dispose();
    }

    public static void Test() => Volume();

    private static System.Collections.IEnumerator BrokenStartupWork()
    {
        yield return null;
        throw new InvalidOperationException("Intentional startup-lock regression check");
    }

    private static void LegacyModuleTraversalTest()
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
            Check(castle.BuildingTraversalEnabled && player.Grounded, "Player starts on a native rooftop connected to interiors and exterior towers");
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
            foreach (InfinityCastleBuilder tower in world.GetComponentsInChildren<InfinityCastleBuilder>())
                Check(tower.GetComponentsInChildren<Collider>().Any(collider => collider.enabled && !collider.isTrigger),
                    "Visible building has solid collision: " + tower.name);
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
            var startParts = castle.GetComponentsInChildren<InfinityCastleBuilder>().First(piece => piece.name == "Playable Building Wing 00")
                .GetComponentsInChildren<CastleArchitectureModule>();
            int exteriorIndex = Array.FindIndex(startParts, module => module.kind == CastleArchitectureModule.ModuleKind.Exterior);
            Check(exteriorIndex >= 0, "Starting cluster has a connected original exterior tower");
            int exteriorSource = -1;
            for (int partIndex = 0; partIndex < exteriorIndex; partIndex++)
                if (castle.TryGetBuildingSeam(0, partIndex, exteriorIndex, out _, out _)) { exteriorSource = partIndex; break; }
            CrossSeam(0, exteriorSource, exteriorIndex, "Walk from the room rooftop over the railed bridge onto an original tower");
            Check(player.Motor.GroundHit.collider != null && player.Motor.GroundHit.collider.transform.IsChildOf(startParts[exteriorIndex].transform),
                "Player stands on the tower's authored mesh, not an added base or walkway");
            castle.TryGetBuildingSeam(0, exteriorSource, exteriorIndex, out Vector3 exteriorExit, out Vector3 exteriorEntry);
            Vector3 exteriorDirection = (exteriorEntry - exteriorExit).normalized;
            input.jump = true; peak = player.transform.position.y;
            for (int frame = 0; frame < 80; frame++) { Frame(1); peak = Mathf.Max(peak, player.transform.position.y); }
            Check(peak > exteriorEntry.y + .8f && player.Grounded, "Jump and land on the original exterior tower");
            WalkTo(exteriorExit - exteriorDirection * .85f);
            Check(player.Grounded, "Walk back from the exterior tower to the interior cluster roof");
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

            int fallCase = 0;
            foreach (Vector3 up in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.down })
            {
                player.GetComponent<InfinityGravityBody>().SetGravity(-up, 22f, true, 8f);
                // Each case starts in empty space, not inside a previous case's differently oriented gravity field.
                Teleport(new Vector3(400, -500, 300 + fallCase++ * 220f) + up * 100f);
                bool caught = false;
                for (int i = 0; i < 260; i++)
                {
                    Frame(1);
                    if (player.Grounded && castle.IsCastleSurface(player.Motor.GroundHit.collider)) { caught = true; break; }
                }
                if (!caught)
                {
                    foreach (InfinityCastleBuilder piece in world.GetComponentsInChildren<InfinityCastleBuilder>(true))
                        if (piece.name.StartsWith("Playable Building Wing"))
                        {
                            report.WriteLine("Fall diagnostic " + piece.name + ": active=" + piece.gameObject.activeSelf + ", bounds=" + piece.WorldBounds);
                            if (piece.name.EndsWith("19"))
                                foreach (InfinityCastleBuilder other in world.GetComponentsInChildren<InfinityCastleBuilder>())
                                    if (other != piece && other.ReservedBounds.Intersects(piece.WorldBounds))
                                        report.WriteLine("  Landing obstruction " + other.name + ": " + other.ReservedBounds);
                        }
                    report.Flush();
                }
                Check(caught && Vector3.Dot(castle.PlayerUp, up) > .99f,
                    "Stream an actual building below a fall in gravity " + up + ": " + player.transform.position + ", current up=" + castle.PlayerUp + ", grounded=" + player.Grounded);
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
        .First(piece => piece.name.StartsWith("Playable Building Wing " + wing.ToString("00") + " "))
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
        SessionState.SetInt(PreviewPending + ".ClearMode", (int)Camera.main.clearFlags);
        SessionState.SetBool(PreviewPending + ".Fog", RenderSettings.fog);
        SessionState.SetBool(PreviewPending, true);
        SessionState.SetInt(PreviewPending + ".TimberSlots", 0);
        SessionState.SetBool(PreviewPending + ".TimberStartupCaptured", false);
        SessionState.SetFloat(PreviewPending + ".NextTimberRender", 0f);
        startupProbe = 0;
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
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(PreviewStarted, 0) > 240)
        {
            SessionState.SetBool(PreviewPending, false);
            EditorApplication.update -= CapturePreview;
            Debug.LogError("Timed out waiting for the Playground architecture preview.");
            EditorApplication.Exit(1);
            return;
        }
        if (!EditorApplication.isPlaying || Time.time <= 0f) return;
        castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        if (castle == null) return;
        // Batch-mode Game views do not render continuously. Pump the real camera during loading,
        // otherwise a visibility-driven effect cannot be tested until the final screenshot.
        if (!castle.IsInitialized && Camera.main != null &&
            Time.time >= SessionState.GetFloat(PreviewPending + ".NextTimberRender", 0f))
        {
            SessionState.SetFloat(PreviewPending + ".NextTimberRender", Time.time + .15f);
            Camera view = Camera.main; RenderTexture previous = view.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(192, 108, 24);
            try { view.targetTexture = target; view.Render(); }
            finally { view.targetTexture = previous; RenderTexture.ReleaseTemporary(target); }
        }
        SessionState.SetInt(PreviewPending + ".TimberSlots", Mathf.Max(
            SessionState.GetInt(PreviewPending + ".TimberSlots", 0), castle.ActiveTimberAssemblyCount));
        try
        {
            if (!castle.IsInitialized && !castle.CastleWorkPending && castle.BuildingWingCount > 0)
                throw new InvalidOperationException("Startup work stopped before castle initialization; see the preceding runtime exception.");
            if (!castle.IsInitialized && castle.ActiveTimberAssemblyCount > 2 &&
                !SessionState.GetBool(PreviewPending + ".TimberStartupCaptured", false))
            {
                Capture(Camera.main, "castle-timber-startup.png");
                SessionState.SetBool(PreviewPending + ".TimberStartupCaptured", true);
            }
            if (!CheckStartupInput()) return;
        }
        catch (Exception error)
        {
            SessionState.SetBool(PreviewPending, false); EditorApplication.update -= CapturePreview;
            Debug.LogException(error); EditorApplication.Exit(1); return;
        }
        if (!castle.IsInitialized || castle.CastleWorkPending) return;
        SessionState.SetBool(PreviewPending, false);
        EditorApplication.update -= CapturePreview;
        try
        {
            castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
            if (castle == null || !castle.IsInitialized) throw new InvalidOperationException("Castle initialization failed.");
            if (Vector3.ProjectOnPlane(castle.player.position - startupProbePosition, Vector3.up).magnitude < .08f)
                throw new InvalidOperationException("Finishing pool warm-up reset the player's movement back to spawn.");
            bool connected = castle.ValidateSurroundingCastle(out string coverage);
            bool clear = castle.ValidateNoBuildingOverlaps(out string overlap);
            if (castle.BuildingWingCount < 80 || castle.DenseCastleRoomCount < 600 || !castle.ValidateDistributedDistricts(out _))
                throw new InvalidOperationException("Saved scene overrides underfill the castle: " + castle.BuildingWingCount + "/" + castle.DenseCastleRoomCount);
            Camera camera = Camera.main;
            if ((int)camera.clearFlags != SessionState.GetInt(PreviewPending + ".ClearMode", -1) ||
                RenderSettings.fog != SessionState.GetBool(PreviewPending + ".Fog", false))
                throw new InvalidOperationException("Castle generation changed the user's skybox clear mode or fog.");
            var pooledTransforms = new HashSet<int>(castle.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()));
            Capture(camera, "building-architecture-player.png");
            int timberSlots = Mathf.Max(SessionState.GetInt(PreviewPending + ".TimberSlots", 0), castle.ActiveTimberAssemblyCount);
            if (castle.animateTimberConstruction && (timberSlots <= 2 || castle.StartupTimberConstructionCount <= 2))
                throw new InvalidOperationException("Playground skipped startup/multi-room construction: " + timberSlots + "/" + castle.StartupTimberConstructionCount);
            GameObject overview = new("Architecture overview"); camera = overview.AddComponent<Camera>();
            camera.transform.position = castle.player.position + new Vector3(35, 32, -46);
            camera.transform.LookAt(castle.player.position + Vector3.forward * 8f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 600f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
            Capture(camera, "building-architecture-overview.png");
            camera.transform.position = castle.player.position + new Vector3(230f, 180f, -270f);
            camera.transform.LookAt(castle.player.position); camera.farClipPlane = 800f;
            bool overviewFog = RenderSettings.fog; RenderSettings.fog = false;
            Capture(camera, "building-architecture-open-volume.png");
            RenderSettings.fog = overviewFog;
            for (int face = 0; face < 6; face++)
            {
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                Vector3 direction = axes[face];
                camera.transform.position = castle.player.position + Vector3.up * 2f;
                camera.transform.rotation = Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > .5f ? Vector3.forward : Vector3.up);
                Capture(camera, "castle-face-" + face + ".png");
            }
            File.WriteAllText(Path.GetFullPath("../building-architecture-preview.txt"), "Playground loaded; " + castle.ActiveBuildingCount +
                " modules; " + castle.BuildingWingCount + " playable buildings; model clearance=" + clear + ". " + coverage +
                "; trained director=" + castle.UsesTrainedDirector + "; learned decisions=" + castle.LearnedDecisions +
                "; complete district rooms=" + castle.DenseCastleRoomCount + "; district paths=" + castle.DenseCastleCrossingCount +
                "; concurrent animated modules observed=" + timberSlots +
                "; construction during startup=" + castle.StartupTimberConstructionCount +
                "; generation frames=" + castle.CastleWorkFrames + "; max generation slice(ms)=" + castle.MaxCastleWorkSliceMilliseconds.ToString("F2") +
                "; max atomic work unit(ms)=" + castle.MaxCastleWorkUnitMilliseconds.ToString("F2") + "; unit=" + castle.MaxCastleWorkUnitName +
                "; playable after(s)=" + castle.PlayerReadySeconds.ToString("F2") +
                "; PASS camera always rendering, movement/look work during warm-up, startup fall recovery, no completion teleport");
            string background = MeasureExposedBackground(camera, castle.player.position + Vector3.up * 2f);
            File.AppendAllText(Path.GetFullPath("../building-architecture-preview.txt"), "\n" + background);
            Vector3 initial = castle.player.position;
            foreach (Vector3 travel in new[] { new Vector3(64, 0, 0), new Vector3(0, 64, 0), new Vector3(0, 0, 64) })
            {
                castle.player.position = initial + travel; castle.SimulateCastle(.7f); Physics.SyncTransforms();
                int steps = 0;
                while (castle.CastleWorkPending && steps++ < 20000) castle.SimulateCastle(0f);
                if (castle.CastleWorkPending) throw new InvalidOperationException("Stream work did not finish.");
                var after = new HashSet<int>(castle.GetComponentsInChildren<Transform>(true).Select(item => item.GetInstanceID()));
                if (!pooledTransforms.SetEquals(after)) throw new InvalidOperationException("Streaming created or destroyed pooled castle objects.");
                if (!castle.ValidateDistributedDistricts(out string spread)) throw new InvalidOperationException(spread);
                background = MeasureExposedBackground(camera, castle.player.position + Vector3.up * 2f);
                File.AppendAllText(Path.GetFullPath("../building-architecture-preview.txt"), "\nAfter travel " + travel + ": " + background);
            }
            File.AppendAllText(Path.GetFullPath("../building-architecture-preview.txt"),
                "\nPASS Skybox/fog preserved; streamed room/path/physics objects reuse the original pool. Max work slice(ms)=" +
                castle.MaxCastleWorkSliceMilliseconds.ToString("F2") + "; max streaming work slice(ms)=" + castle.MaxStreamingWorkSliceMilliseconds.ToString("F2"));
            if (castle.MaxStreamingWorkSliceMilliseconds > 50d) throw new InvalidOperationException("A streamed work slice exceeded 50ms.");
            if (!connected || !clear) throw new InvalidOperationException(coverage + "; " + overlap);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static bool CheckStartupInput()
    {
        if (Camera.main == null || !Camera.main.enabled) throw new InvalidOperationException("Startup disabled the rendering camera.");
        if (!castle.IsPlayerReady)
        {
            if (Time.time > 220f) throw new InvalidOperationException("The surrounding castle did not finish preparing.");
            return false;
        }
        var actor = castle.player.GetComponent<ThirdPersonController>();
        var controls = castle.player.GetComponent<StarterAssetsInputs>();
        if (!actor.enabled) throw new InvalidOperationException("The starter building is ready but player control is disabled.");
        if (startupProbe >= 6) return true;
        if (!castle.IsInitialized || castle.PreparedCastleLayerCount < 2)
            throw new InvalidOperationException("Controls unlocked without the surrounding volume and lower layers.");
        if (startupProbe == 0)
        {
            if (!actor.Grounded) return false;
            startupProbePosition = actor.transform.position; startupProbeTime = Time.time;
            startupProbeAnalog = controls.analogMovement; controls.analogMovement = true;
            controls.MoveInput(new Vector2(.3f, 0f)); startupProbe = 1;
        }
        else if (startupProbe == 1 && Time.time - startupProbeTime > .35f)
        {
            controls.MoveInput(Vector2.zero); controls.analogMovement = startupProbeAnalog;
            if (Vector3.ProjectOnPlane(actor.transform.position - startupProbePosition, Vector3.up).magnitude < .08f)
                throw new InvalidOperationException("Movement input did not move the player during background warm-up.");
            startupProbeRotation = actor.CinemachineCameraTarget.transform.rotation;
            controls.LookInput(new Vector2(1f, .1f)); startupProbeTime = Time.time; startupProbe = 2;
        }
        else if (startupProbe == 2)
        {
            if (Quaternion.Angle(startupProbeRotation, actor.CinemachineCameraTarget.transform.rotation) > .05f)
            {
                controls.LookInput(Vector2.zero);
                controls.JumpInput(true); startupProbe = 6;
                Debug.Log("PASS Movement/look and jump after the surrounding castle is prepared.");
            }
            else if (Time.time - startupProbeTime > 1f) throw new InvalidOperationException("Look input did not rotate the camera during warm-up.");
        }
        return startupProbe >= 6;
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

    private static string MeasureExposedBackground(Camera camera, Vector3 observer)
    {
        const int size = 192;
        RenderTexture texture = RenderTexture.GetTemporary(size, size, 24);
        RenderTexture previous = RenderTexture.active;
        var counts = new List<int>(); bool fog = RenderSettings.fog;
        try
        {
            RenderSettings.fog = false; camera.backgroundColor = Color.magenta;
            camera.fieldOfView = 90f; camera.aspect = 1f; camera.targetTexture = texture;
            camera.transform.position = observer;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                camera.transform.rotation = Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .5f ? Vector3.forward : Vector3.up);
                camera.Render(); RenderTexture.active = texture;
                Texture2D pixels = new(size, size, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0); pixels.Apply();
                int uncovered = pixels.GetPixels32().Count(pixel => pixel.r > 245 && pixel.g < 10 && pixel.b > 245);
                File.WriteAllBytes(Path.GetFullPath("../castle-coverage-" + counts.Count + ".png"), pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels); counts.Add(uncovered);
            }
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderSettings.fog = fog;
            RenderTexture.ReleaseTemporary(texture);
        }
        string result = "Actual rendered background pixels (six 192x192 views): " + string.Join(",", counts);
        Debug.Log(result);
        // Open skybox gaps are intentional. Density is tested by real 3D room distribution, not an enclosure.
        return result;
    }
}
