using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class DynamicInfinityCastle
{
    [Header("Distributed Castle Rooms")]
    public GameObject architectureDistrictModel;
    public Material architectureDistrictMaterial;
    private const float DistrictSpan = 64f;
    // Extra residency is lightweight layout data, not extra GameObjects or collider bodies.
    private const int DistrictBlockCapacity = 768, DistrictBodyCapacity = 96;
    private sealed class DistrictRoom
    {
        public Vector3 position, scale;
        public Quaternion rotation;
        public Bounds bounds;
        public bool active;
        public DistrictBody body;
    }
    private sealed class DistrictCrossing
    {
        public DistrictRoom a, b;
        public Vector3 start, end;
        public Bounds bounds;
        public Matrix4x4 matrix;
        public Link link;
    }
    private sealed class DistrictBlock
    {
        public Bounds bounds;
        public readonly DistrictRoom[] rooms = new DistrictRoom[8];
        public readonly DistrictCrossing[] crossings = new DistrictCrossing[4];
        public DistrictBlock()
        {
            for (int i = 0; i < rooms.Length; i++) rooms[i] = new DistrictRoom();
            for (int i = 0; i < crossings.Length; i++) crossings[i] = new DistrictCrossing { a = rooms[i * 2], b = rooms[i * 2 + 1] };
        }
    }
    private sealed class DistrictBody
    {
        public GameObject root;
        public InfinityCastleBuilder piece;
        public InfiniteCastleGrid[] zones;
    }
    private sealed class DistrictDraw
    {
        public Mesh mesh;
        public Material material;
        public Matrix4x4 local;
        public readonly List<Matrix4x4> instances = new(2048);
    }
    private readonly Dictionary<Vector3Int, DistrictBlock> denseBlocks = new(DistrictBlockCapacity);
    private readonly Queue<DistrictBlock> freeDistrictBlocks = new();
    private readonly HashSet<Vector3Int> wantedDenseBlocks = new(DistrictBlockCapacity);
    private readonly List<Vector3Int> pendingDenseBlocks = new(DistrictBlockCapacity);
    private readonly List<Vector3Int> retiredDenseBlocks = new(DistrictBlockCapacity);
    private readonly Queue<DistrictBody> freeDistrictBodies = new();
    private readonly List<DistrictRoom> nearDistrictRooms = new(256);
    private readonly List<DistrictDraw>[] districtDraws = { new(), new(), new() };
    private readonly List<DistrictDraw> districtPathDraws = new();
    private Transform denseRoot;
    private Vector3Int denseCell;
    private Vector3Int densePredictionCell;
    private float districtPhysicsClock;
    private Bounds districtModuleBounds;
    private Bounds districtPathBounds;
    private Vector3 districtPhysicsPosition;
    private readonly Plane[] districtCameraPlanes = new Plane[6];
    public int DenseCastleRoomCount { get; private set; }
    public int DenseCastlePhysicsCount => DistrictBodyCapacity - freeDistrictBodies.Count;
    public int DenseCastleCrossingCount { get; private set; }
    public int DistrictDataPoolCapacity => DistrictBlockCapacity;

    private IEnumerator InitializeDenseDistricts()
    {
        if (denseRoot != null) yield break;
        denseRoot = new GameObject("Distributed Castle - Pooled Room and Path Clusters").transform;
        denseRoot.SetParent(transform, false);
        districtModuleBounds = roomPrefab.GetComponent<CastleArchitectureModule>().localBounds;
        LODGroup group = roomPrefab.GetComponent<LODGroup>();
        Renderer[] high = group != null ? group.GetLODs()[0].renderers : roomPrefab.GetComponentsInChildren<Renderer>(true);
        Renderer[] medium = group != null ? group.GetLODs()[group.lodCount - 1].renderers : high;
        AddDistrictDrawSources(districtDraws[0], high, roomPrefab.transform);
        AddDistrictDrawSources(districtDraws[1], medium, roomPrefab.transform);
        if (architectureDistrictModel != null)
        {
            GameObject sample = new("Complete Room Distance Template"); sample.transform.SetParent(denseRoot, false);
            GameObject art = Instantiate(architectureDistrictModel, sample.transform);
            FitInnerCastleArt(sample.transform, art.transform, districtModuleBounds);
            foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = architectureDistrictMaterial != null ? architectureDistrictMaterial : high[0].sharedMaterial;
            AddDistrictDrawSources(districtDraws[2], sample.GetComponentsInChildren<Renderer>(), sample.transform);
            sample.SetActive(false);
        }
        else AddDistrictDrawSources(districtDraws[2], medium, roomPrefab.transform);
        LODGroup paths = flatStair.prefab.GetComponent<LODGroup>();
        districtPathBounds = CastleGeometry.RendererBoundsInRoot(flatStair.prefab.transform);
        AddDistrictDrawSources(districtPathDraws, paths != null ? paths.GetLODs()[paths.lodCount - 1].renderers :
            flatStair.prefab.GetComponentsInChildren<Renderer>(true), flatStair.prefab.transform);
        for (int i = 0; i < DistrictBlockCapacity; i++) { freeDistrictBlocks.Enqueue(new DistrictBlock()); yield return null; }
        for (int i = 0; i < DistrictBodyCapacity; i++)
        {
            GameObject root = Instantiate(roomPrefab, denseRoot); root.name = "Physical Castle District Room " + i;
            foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            CastleArchitectureModule module = root.GetComponent<CastleArchitectureModule>();
            AddWalkableSurface(root.transform, module.floorPoint, module.floorSize);
            CreateBuildingZone(root.transform, "District Room Gravity", module.floorPoint, module.floorSize, 3f, true);
            CreateBuildingZone(root.transform, "District Roof Gravity", module.roofPoint, module.roofSize, 3f, true);
            InfinityCastleBuilder piece = root.AddComponent<InfinityCastleBuilder>(); piece.InitializePooledPiece(this);
            var body = new DistrictBody { root = root, piece = piece, zones = root.GetComponentsInChildren<InfiniteCastleGrid>(true) };
            freeDistrictBodies.Enqueue(body); root.SetActive(false);
            yield return null;
        }
        denseCell = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        yield return UpdateDenseDistricts();
        RenderPipelineManager.beginCameraRendering += RenderDenseDistricts;
        Debug.Log($"Distributed castle: {DenseCastleRoomCount} full rooms and {DenseCastleCrossingCount} native paths throughout a 3D volume; no enclosing faces.", this);
    }

    private void AddDistrictDrawSources(List<DistrictDraw> draws, Renderer[] renderers, Transform source)
    {
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Material material = PooledMaterial(renderer.sharedMaterial); material.enableInstancing = true;
            draws.Add(new DistrictDraw { mesh = filter.sharedMesh, material = material,
                local = source.worldToLocalMatrix * filter.transform.localToWorldMatrix });
        }
    }

    private bool DistrictClearOfNetwork(Bounds bounds)
    {
        foreach (Bounds corridor in gravityLeapClearance) if (bounds.Intersects(corridor)) return false;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || !bounds.Intersects(wing.piece.ReservedBounds)) continue;
            if (wing.piece.IsMoving) return false;
            foreach (BuildingPart part in wing.parts) if (bounds.Intersects(part.bounds)) return false;
        }
        foreach (BuildingJoin join in buildingJoins) if (join.stair != null && bounds.Intersects(join.stair.bounds)) return false;
        foreach (DistrictBlock block in denseBlocks.Values)
            if (bounds.Intersects(block.bounds))
                foreach (DistrictRoom room in block.rooms) if (room.active && bounds.Intersects(room.bounds)) return false;
        return !bounds.Intersects(PlayerSafetyBounds());
    }

    private void PopulateDistrictBlock(DistrictBlock block, Vector3Int key)
    {
        bool first = true;
        int stories = 2 + Mathf.Min(2, Mathf.FloorToInt(Hash(key, 831) * 3f));
        Vector3 up = directions[Mathf.Min(5, Mathf.FloorToInt(Hash(key, 832) * 6f))];
        Quaternion basis = CastleGeometry.Orientation(up);
        Vector3 centre = (Vector3)key * DistrictSpan + basis * new Vector3((Hash(key, 833) - .5f) * 14f, 0f, (Hash(key, 834) - .5f) * 14f);
        for (int story = 0; story < block.crossings.Length; story++)
        {
            DistrictCrossing crossing = block.crossings[story];
            DistrictRoom a = crossing.a, b = crossing.b;
            a.active = b.active = false;
            if (story >= stories) continue;
            Vector3Int shape = key + new Vector3Int(story * 47, LayoutPolicy.Layout * 19, 0);
            Quaternion rotation = basis * Quaternion.Euler(0f, Mathf.Floor(Hash(shape, 835) * 4f) * 90f, 0f);
            Vector3 normal = rotation * Vector3.forward;
            Vector3 floor = centre + up * (-22f + story * Mathf.Lerp(10f, 13f, Hash(key, 836))) + rotation * Vector3.right * ((Hash(shape, 837) - .5f) * 20f);
            a.scale = new Vector3(Mathf.Lerp(1.1f, 2.4f, Hash(shape, 838)), Mathf.Lerp(1f, 1.3f, Hash(shape, 839)), Mathf.Lerp(1f, 1.7f, Hash(shape, 840)));
            b.scale = new Vector3(Mathf.Lerp(1.2f, 2.2f, Hash(shape, 841)), a.scale.y, Mathf.Lerp(1f, 1.7f, Hash(shape, 842)));
            float gap = Mathf.Lerp(6f, 13f, Hash(shape, 843));
            a.position = floor - normal * (2.709741f * a.scale.z + gap * .5f); a.rotation = rotation;
            b.position = floor + normal * (2.709741f * b.scale.z + gap * .5f); b.rotation = rotation * Quaternion.Euler(0f, 180f, 0f);
            a.bounds = CastleGeometry.TransformBounds(districtModuleBounds, Matrix4x4.TRS(a.position, a.rotation, a.scale));
            b.bounds = CastleGeometry.TransformBounds(districtModuleBounds, Matrix4x4.TRS(b.position, b.rotation, b.scale));
            Bounds pair = a.bounds; pair.Encapsulate(b.bounds);
            if (!DistrictClearOfNetwork(pair)) continue;
            a.active = b.active = true;
            Vector3 port = DirectionPort(roomPrefab.GetComponent<CastleArchitectureModule>(), Vector3.forward).position;
            crossing.start = DistrictEntrance(a, port); crossing.end = DistrictEntrance(b, port);
            Vector3 along = (crossing.end - crossing.start).normalized;
            Quaternion facing = Quaternion.LookRotation(along, up);
            Vector3 authored = Vector3.ProjectOnPlane(flatStair.high - flatStair.low, Vector3.up).normalized;
            float width = Mathf.Clamp(flatStair.width, 1.4f, 2.1f), length = Vector3.Distance(crossing.start, crossing.end) + .44f;
            Vector3 scale = Mathf.Abs(authored.z) > .5f ? new Vector3(width / flatStair.width, 1f, length / flatStair.run) :
                new Vector3(length / flatStair.run, 1f, width / flatStair.width);
            Quaternion pathRotation = facing * Quaternion.Inverse(Quaternion.LookRotation(authored, Vector3.up));
            crossing.matrix = Matrix4x4.TRS(crossing.start - along * .22f - pathRotation * Vector3.Scale(flatStair.low, scale), pathRotation, scale);
            crossing.bounds = CastleGeometry.TransformBounds(districtPathBounds, crossing.matrix);
            if (first) { block.bounds = pair; first = false; } else block.bounds.Encapsulate(pair);
        }
        if (first) block.bounds = new Bounds(centre, Vector3.zero);
    }

    private void ReleaseDistrictBody(DistrictRoom room)
    {
        if (room.body == null) return;
        room.body.root.SetActive(false); freeDistrictBodies.Enqueue(room.body); room.body = null;
    }

    private IEnumerator UpdateDenseDistricts()
    {
        if (denseRoot == null) yield break;
        districtPhysicsPosition = player.position;
        Vector3 forecast = player.position + Vector3.ClampMagnitude(playerVelocity * 2.5f +
            (controller != null && !controller.Grounded ? -PlayerUp * 45f : Vector3.zero), 140f);
        Vector3Int cell = Vector3Int.RoundToInt(player.position / DistrictSpan);
        Vector3Int prediction = Vector3Int.RoundToInt(forecast / DistrictSpan);
        if (cell != denseCell || prediction != densePredictionCell)
        {
            denseCell = cell; densePredictionCell = prediction; wantedDenseBlocks.Clear();
            // A complete halo in all six directions plus a lead volume along actual movement.
            for (int x = -5; x <= 5; x++) for (int y = -5; y <= 5; y++) for (int z = -5; z <= 5; z++)
            {
                int radius = x * x + y * y + z * z;
                if (radius <= 25) wantedDenseBlocks.Add(cell + new Vector3Int(x, y, z));
                if (radius <= 9) wantedDenseBlocks.Add(prediction + new Vector3Int(x, y, z));
            }
        }
        // Retry deferred offscreen work even if the player stays in the same cell and turns around.
        retiredDenseBlocks.Clear();
        foreach (var pair in denseBlocks)
            if (!wantedDenseBlocks.Contains(pair.Key) && pair.Value.bounds.SqrDistance(player.position) > 70f * 70f &&
                CanRecycleCastleBounds(pair.Value.bounds)) retiredDenseBlocks.Add(pair.Key);
        foreach (Vector3Int key in retiredDenseBlocks)
        {
            DistrictBlock block = denseBlocks[key];
            // Earlier releases yield between blocks; the camera may have turned since selection.
            if (!CanRecycleCastleBounds(block.bounds)) continue;
            foreach (DistrictRoom room in block.rooms) ReleaseDistrictBody(room);
            foreach (DistrictCrossing crossing in block.crossings) ReleaseDistrictCrossing(crossing);
            denseBlocks.Remove(key); freeDistrictBlocks.Enqueue(block);
            yield return null;
        }
        pendingDenseBlocks.Clear();
        foreach (Vector3Int key in wantedDenseBlocks) if (!denseBlocks.ContainsKey(key)) pendingDenseBlocks.Add(key);
        pendingDenseBlocks.Sort((a, b) => ((Vector3)a * DistrictSpan - forecast).sqrMagnitude.CompareTo(((Vector3)b * DistrictSpan - forecast).sqrMagnitude));
        foreach (Vector3Int key in pendingDenseBlocks)
        {
            if (denseBlocks.ContainsKey(key) || freeDistrictBlocks.Count == 0) continue;
            DistrictBlock block = freeDistrictBlocks.Dequeue();
            PopulateDistrictBlock(block, key);
            if (initialized && !CanRecycleCastleBounds(block.bounds))
            { freeDistrictBlocks.Enqueue(block); yield return null; continue; }
            denseBlocks.Add(key, block);
            yield return null;
        }
        DenseCastleRoomCount = 0;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictRoom room in block.rooms)
            if (room.active) DenseCastleRoomCount++;
        DenseCastleCrossingCount = DenseCastleRoomCount / 2;
        UpdateDistrictPhysics(1f);
    }

    private void UpdateDistrictPhysics(float dt)
    {
        if (denseRoot == null) return;
        districtPhysicsClock -= dt;
        if (districtPhysicsClock > 0f) return;
        districtPhysicsClock = .08f;
        Vector3 ahead = player.position + Vector3.ClampMagnitude(playerVelocity * .8f, 60f);
        nearDistrictRooms.Clear();
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictRoom room in block.rooms)
        {
            if (!room.active) continue;
            float distance = room.bounds.SqrDistance(player.position);
            bool approaching = room.bounds.SqrDistance(ahead) < 24f * 24f;
            if (distance < 48f * 48f || approaching) nearDistrictRooms.Add(room);
            else if (distance > 68f * 68f && !IsUltraJumpTarget(room)) ReleaseDistrictBody(room);
        }
        nearDistrictRooms.Sort((a, b) => a.bounds.SqrDistance(player.position).CompareTo(b.bounds.SqrDistance(player.position)));
        int promoted = 0;
        foreach (DistrictRoom room in nearDistrictRooms)
        {
            if (room.body != null || freeDistrictBodies.Count == 0) continue;
            AcquireDistrictBody(room);
            if (++promoted >= 4 && initialized) break;
        }
        int paths = 0;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictCrossing crossing in block.crossings)
        {
            if (crossing.a.body == null || crossing.b.body == null) { ReleaseDistrictCrossing(crossing); continue; }
            if (crossing.link != null || linkPool.Count < 12 || initialized && paths >= 2) continue;
            crossing.link = linkPool.Dequeue(); crossing.link.root.transform.SetParent(denseRoot, true);
            ConfigureNetworkBridge(crossing.link, crossing.start, crossing.end, crossing.a.rotation * Vector3.up);
            // Leasing collision must not switch the already-visible instanced path's representation.
            foreach (Renderer renderer in crossing.link.renderers) renderer.enabled = false;
            paths++;
        }
        Physics.SyncTransforms();
    }

    private bool AcquireDistrictBody(DistrictRoom room)
    {
        if (room.body != null) return true;
        if (freeDistrictBodies.Count == 0) return false;
        DistrictBody body = freeDistrictBodies.Dequeue(); room.body = body;
        body.root.transform.SetPositionAndRotation(room.position, room.rotation); body.root.transform.localScale = room.scale;
        foreach (InfiniteCastleGrid zone in body.zones) { zone.gravityDirection = -(room.rotation * Vector3.up); zone.enabled = enableGravityZones; }
        body.root.SetActive(true);
        return true;
    }

    private void ReleaseDistrictCrossing(DistrictCrossing crossing)
    {
        if (crossing.link == null) return;
        foreach (Renderer renderer in crossing.link.renderers) renderer.enabled = true;
        crossing.link.root.SetActive(false); crossing.link.root.transform.SetParent(pathsParent, true);
        linkPool.Enqueue(crossing.link); crossing.link = null;
    }

    private static Vector3 DistrictEntrance(DistrictRoom room, Vector3 port) =>
        room.position + room.rotation * (Vector3.Scale(port, room.scale) - Vector3.forward * .8f + Vector3.up * .18f);

    private void BuildDistrictDraws(Camera camera)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, districtCameraPlanes);
        foreach (var level in districtDraws) foreach (DistrictDraw draw in level) draw.instances.Clear();
        foreach (DistrictDraw draw in districtPathDraws) draw.instances.Clear();
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            if (!GeometryUtility.TestPlanesAABB(districtCameraPlanes, block.bounds)) continue;
            foreach (DistrictRoom room in block.rooms)
            {
                if (!room.active || !GeometryUtility.TestPlanesAABB(districtCameraPlanes, room.bounds)) continue;
                float distance = room.bounds.SqrDistance(player.position);
                if (distance > CastleVisibleRadius * CastleVisibleRadius) continue;
                int level = distance < 18f * 18f ? 0 : distance < 45f * 45f ? 1 : 2;
                Matrix4x4 world = Matrix4x4.TRS(room.position, room.rotation, room.scale);
                foreach (DistrictDraw draw in districtDraws[level]) draw.instances.Add(world * draw.local);
            }
            foreach (DistrictCrossing crossing in block.crossings)
                if (crossing.a.active && crossing.bounds.SqrDistance(player.position) <= CastleVisibleRadius * CastleVisibleRadius)
                    foreach (DistrictDraw draw in districtPathDraws) draw.instances.Add(crossing.matrix * draw.local);
        }
    }

    private void DrawDistrictInstances(List<DistrictDraw> draws, Camera camera)
    {
        foreach (DistrictDraw draw in draws)
        {
            var parameters = new RenderParams(draw.material) { camera = camera, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer,
                worldBounds = new Bounds(player.position, Vector3.one * 750f) };
            for (int start = 0; start < draw.instances.Count; start += 128)
                Graphics.RenderMeshInstanced(parameters, draw.mesh, 0, draw.instances, Mathf.Min(128, draw.instances.Count - start), start);
        }
    }

    private void RenderDenseDistricts(ScriptableRenderContext context, Camera camera)
    {
        if (denseRoot == null || !isActiveAndEnabled || camera.cameraType == CameraType.Preview) return;
        Shader.SetGlobalVector("_CastleStreamCenter", new Vector4(player.position.x, player.position.y, player.position.z, 1f));
        Shader.SetGlobalVector("_CastleStreamDistances", new Vector4(CastleFadeStart, CastleVisibleRadius, 0f, 0f));
        BuildDistrictDraws(camera);
        foreach (var level in districtDraws) DrawDistrictInstances(level, camera);
        DrawDistrictInstances(districtPathDraws, camera);
    }

    private bool DenseWingSpaceFree(BuildingWing wing, Vector3 offset)
    {
        Bounds whole = CastleGeometry.Sweep(wing.bounds, offset);
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            if (!whole.Intersects(block.bounds)) continue;
            foreach (BuildingPart part in wing.parts)
            {
                if (part.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                Bounds sweep = part.bounds; sweep.Expand(-.45f); sweep = CastleGeometry.Sweep(sweep, offset);
                foreach (DistrictRoom room in block.rooms) if (room.active && sweep.Intersects(room.bounds)) return false;
                foreach (DistrictCrossing crossing in block.crossings) if (crossing.a.active && sweep.Intersects(crossing.bounds)) return false;
            }
        }
        return true;
    }

    private void ShutdownDenseDistricts()
    {
        RenderPipelineManager.beginCameraRendering -= RenderDenseDistricts;
        Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero);
    }

    public bool ValidateDistributedDistricts(out string result)
    {
        var radii = new HashSet<int>(); var orientations = new HashSet<Vector3Int>(); int inner = 0;
        int[] sides = new int[6];
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictRoom room in block.rooms)
        {
            if (!room.active) continue;
            Vector3 delta = room.bounds.center - player.position;
            radii.Add(Mathf.FloorToInt(delta.magnitude / 16f));
            orientations.Add(Vector3Int.RoundToInt(room.rotation * Vector3.up));
            if (delta.magnitude < 100f) inner++;
            for (int i = 0; i < sides.Length; i++) if (Vector3.Dot(delta, directions[i]) > 16f) sides[i]++;
        }
        result = $"Distributed rooms={DenseCastleRoomCount}, paths={DenseCastleCrossingCount}, interior rooms={inner}, depth bands={radii.Count}, gravity frames={orientations.Count}, six-direction rooms={string.Join(",", sides)}";
        return inner >= 8 && radii.Count >= 8 && orientations.Count == 6 && System.Array.TrueForAll(sides, count => count >= 30) &&
            denseBlocks.Count + freeDistrictBlocks.Count == DistrictBlockCapacity && DenseCastlePhysicsCount <= DistrictBodyCapacity;
    }

    public bool TryGetDistrictDoorPair(out Vector3 exit, out Vector3 entry, out Vector3 up, out Vector3 outward)
    {
        exit = entry = up = outward = default; float nearest = float.PositiveInfinity;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictCrossing crossing in block.crossings)
        {
            if (!crossing.a.active) continue;
            float distance = crossing.a.bounds.SqrDistance(player.position);
            if (distance >= nearest) continue;
            nearest = distance; exit = crossing.start; entry = crossing.end;
            up = crossing.a.rotation * Vector3.up; outward = crossing.a.rotation * Vector3.forward;
        }
        return float.IsFinite(nearest);
    }
}
