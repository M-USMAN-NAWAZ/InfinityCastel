using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class DynamicInfinityCastle
{
    [Header("Distributed Castle Rooms")]
    public GameObject architectureDistrictModel;
    public Material architectureDistrictMaterial;
    public const float DistrictSpan = 48f;
    // Extra residency is lightweight layout data, not extra GameObjects or collider bodies.
    private const int DistrictBlockCapacity = 1536, DistrictBodyCapacity = 96;
    private const int DistrictFloors = 4, DistrictRoomsPerFloor = 4;
    private Stair districtFlatStair, districtFlightStair;
    private sealed class DistrictRoom
    {
        public Vector3 position, scale;
        public Quaternion rotation;
        public Bounds bounds;
        public bool active;
        public DistrictBody body;
        public bool assemblySeen;
        public Vector4 construction;
    }
    private sealed class DistrictCrossing
    {
        public DistrictRoom a, b;
        public Vector3 start, end;
        public Bounds bounds;
        public Matrix4x4 matrix;
        public Link link;
        public bool assemblySeen;
        public Vector4 construction;
        public bool active, flight, doorPair;
        public float physicsDistance;
    }
    private sealed class DistrictBlock
    {
        public Bounds bounds;
        public readonly DistrictRoom[] rooms = new DistrictRoom[DistrictFloors * DistrictRoomsPerFloor];
        public readonly DistrictCrossing[] crossings = new DistrictCrossing[56];
        public DistrictBlock()
        {
            for (int i = 0; i < rooms.Length; i++) rooms[i] = new DistrictRoom();
            for (int i = 0; i < crossings.Length; i++) crossings[i] = new DistrictCrossing { a = rooms[0], b = rooms[0] };
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
        public readonly List<Vector4> construction = new(2048);
        public readonly MaterialPropertyBlock properties = new();
    }
    private readonly Dictionary<Vector3Int, DistrictBlock> denseBlocks = new(DistrictBlockCapacity);
    private readonly Queue<DistrictBlock> freeDistrictBlocks = new();
    private readonly HashSet<Vector3Int> wantedDenseBlocks = new(DistrictBlockCapacity);
    private readonly List<Vector3Int> pendingDenseBlocks = new(DistrictBlockCapacity);
    private readonly List<Vector3Int> retiredDenseBlocks = new(DistrictBlockCapacity);
    private readonly Queue<DistrictBody> freeDistrictBodies = new();
    private readonly List<DistrictRoom> physicalDistrictRooms = new(DistrictBodyCapacity);
    private readonly List<DistrictRoom> nearDistrictRooms = new(256);
    private readonly List<DistrictDraw>[] districtDraws = { new(), new(), new() };
    private readonly List<DistrictDraw> districtPathDraws = new();
    private readonly List<DistrictDraw> districtFlightDraws = new();
    private readonly List<DistrictCrossing> nearDistrictCrossings = new(512);
    private Transform denseRoot;
    private Vector3Int denseCell;
    private Vector3Int densePredictionCell;
    private float districtPhysicsClock;
    private Bounds districtModuleBounds;
    private CastleArchitectureModule districtRoomTemplate;
    private Bounds districtPathBounds;
    private Bounds districtFlightBounds;
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
        districtRoomTemplate = roomPrefab.GetComponent<CastleArchitectureModule>();
        districtModuleBounds = districtRoomTemplate.localBounds;
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
        if (districtFlatStair == null || districtFlightStair == null)
            throw new System.InvalidOperationException("Assign the extracted flat/flight stairs from inifinity_castle_pack.fbx.");
        districtPathBounds = CastleGeometry.RendererBoundsInRoot(districtFlatStair.prefab.transform);
        districtFlightBounds = CastleGeometry.RendererBoundsInRoot(districtFlightStair.prefab.transform);
        AddDistrictDrawSources(districtPathDraws, districtFlatStair.prefab.GetComponentsInChildren<Renderer>(true), districtFlatStair.prefab.transform);
        AddDistrictDrawSources(districtFlightDraws, districtFlightStair.prefab.GetComponentsInChildren<Renderer>(true), districtFlightStair.prefab.transform);
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
        InitializeTimberAssemblies();
        Debug.Log($"Distributed castle: {DenseCastleRoomCount} full rooms and {DenseCastleCrossingCount} native paths throughout a 3D volume; no enclosing faces.", this);
    }

    private void AddDistrictDrawSources(List<DistrictDraw> draws, Renderer[] renderers, Transform source)
    {
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Material material = PooledMaterial(renderer.sharedMaterial); material.enableInstancing = true;
            draws.Add(new DistrictDraw { mesh = FindTimberAssemblyMesh(filter.sharedMesh) ?? filter.sharedMesh, material = material,
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
        // District geometry is contained in its own 48m cell, so adjacent cells cannot intersect.
        return !bounds.Intersects(PlayerSafetyBounds());
    }

    private void PopulateDistrictBlock(DistrictBlock block, Vector3Int key)
    {
        foreach (DistrictRoom room in block.rooms) { room.active = room.assemblySeen = false; room.construction = Vector4.zero; }
        foreach (DistrictCrossing crossing in block.crossings)
        { crossing.active = crossing.assemblySeen = crossing.doorPair = false; crossing.construction = Vector4.zero; }
        int stories = 3 + (Hash(key, 831) > .35f ? 1 : 0);
        Vector3 up = directions[Mathf.Min(5, Mathf.FloorToInt(Hash(key, 832) * 6f))];
        Quaternion basis = CastleGeometry.Orientation(up) * Quaternion.Euler(0f, Mathf.Floor(Hash(key, 835) * 4f) * 90f, 0f);
        Vector3 centre = (Vector3)key * DistrictSpan + basis * new Vector3((Hash(key, 833) - .5f) * 1.4f, 0f, (Hash(key, 834) - .5f) * 1.4f);
        float floorStep = Mathf.Lerp(7.6f, 8.6f, Hash(key, 836));
        block.bounds = new Bounds(centre, Vector3.zero);
        for (int floor = 0; floor < stories; floor++) for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++)
        {
            int index = floor * 4 + row * 2 + col;
            Vector3Int shape = key + new Vector3Int(index * 47, LayoutPolicy.Layout * 19, 0);
            DistrictRoom room = block.rooms[index];
            room.rotation = basis * Quaternion.Euler(0f, row * 180f, 0f);
            room.position = centre + basis * new Vector3(col == 0 ? -7.2f : 7.2f, -15f + floor * floorStep, row == 0 ? -8.2f : 8.2f);
            room.scale = new Vector3(Mathf.Lerp(.95f, 1.28f, Hash(shape, 838)), Mathf.Lerp(1f, 1.14f, Hash(shape, 839)), Mathf.Lerp(.9f, 1.28f, Hash(shape, 840)));
            room.bounds = CastleGeometry.TransformBounds(districtModuleBounds, Matrix4x4.TRS(room.position, room.rotation, room.scale));
            room.active = true; block.bounds.Encapsulate(room.bounds);
        }
        int used = 0;
        Vector3 right = basis * Vector3.right, forward = basis * Vector3.forward;
        Vector3 Door(DistrictRoom room, Vector3 outward)
        {
            var port = DirectionPort(districtRoomTemplate, Quaternion.Inverse(room.rotation) * outward);
            return room.position + room.rotation * Vector3.Scale(port.position, room.scale) - outward * .2f + up * .04f;
        }
        void Segment(DistrictRoom a, DistrictRoom b, Vector3 from, Vector3 to, bool doorPair = false)
        {
            DistrictCrossing crossing = block.crossings[used++]; crossing.a = a; crossing.b = b;
            crossing.start = from; crossing.end = to; crossing.active = true; crossing.doorPair = doorPair;
            FitDistrictCrossing(crossing, up); block.bounds.Encapsulate(crossing.bounds);
        }
        void Flight(DistrictRoom a, DistrictRoom b, Vector3 from, Vector3 to)
        {
            // Meet the leading EDGE of the perpendicular landing, not its centre. Otherwise
            // the landing's underside becomes a half-metre wall across the top of the stairs.
            Vector3 along = Vector3.ProjectOnPlane(to - from, up).normalized;
            Segment(a, b, from + along * .85f, to - along * .85f);
        }
        for (int floor = 0; floor < stories; floor++)
        {
            int at = floor * 4, next = ((floor + 1) % stories) * 4;
            for (int col = 0; col < 2; col++)
                Segment(block.rooms[at + col], block.rooms[at + 2 + col], Door(block.rooms[at + col], forward), Door(block.rooms[at + 2 + col], -forward), true);
            for (int row = 0; row < 2; row++)
                Segment(block.rooms[at + row * 2], block.rooms[at + row * 2 + 1], Door(block.rooms[at + row * 2], right), Door(block.rooms[at + row * 2 + 1], -right), true);
            for (int side = 0; side < 2; side++)
            {
                // Exterior switchbacks join floors and close the remaining side doors into walkable loops.
                int col = side, row = side;
                DistrictRoom a = block.rooms[at + row * 2 + col], b = block.rooms[next + (1 - row) * 2 + col];
                Vector3 outward = right * (side == 0 ? -1f : 1f);
                Vector3 from = Door(a, outward), to = Door(b, outward);
                float outer = floor == stories - 1 ? 17.5f : 13.5f + floor * .65f;
                Vector3 p = from + outward * (outer - Mathf.Abs(Vector3.Dot(from - centre, right)));
                Segment(a, b, from, p);
                Vector3 q = to + outward * (outer - Mathf.Abs(Vector3.Dot(to - centre, right)));
                if (floor == stories - 1)
                {
                    Vector3 first = Vector3.Lerp(p, q, 1f / 3f) + Vector3.ProjectOnPlane(q - p, up) * (2f / 3f);
                    Vector3 second = Vector3.Lerp(p, q, 2f / 3f) - Vector3.ProjectOnPlane(q - p, up) * (2f / 3f) + outward * 2f;
                    q += outward * 4f;
                    Flight(a, b, p, first); Segment(a, b, first, first + outward * 2f);
                    Flight(a, b, first + outward * 2f, second); Segment(a, b, second, second + outward * 2f);
                    Flight(a, b, second + outward * 2f, q);
                }
                else Flight(a, b, p, q);
                Segment(a, b, q, to);
            }
        }
        if (!DistrictClearOfNetwork(block.bounds))
        {
            foreach (DistrictRoom room in block.rooms) room.active = false;
            foreach (DistrictCrossing crossing in block.crossings) crossing.active = false;
        }
    }

    private void FitDistrictCrossing(DistrictCrossing crossing, Vector3 up)
    {
        Vector3 from = crossing.start, to = crossing.end;
        if (Vector3.Dot(to - from, up) < 0f) (from, to) = (to, from);
        Vector3 delta = to - from, flat = Vector3.ProjectOnPlane(delta, up);
        float rise = Vector3.Dot(delta, up);
        crossing.flight = rise > .15f;
        Stair spec = crossing.flight ? districtFlightStair : districtFlatStair;
        Vector3 authored = Vector3.ProjectOnPlane(spec.high - spec.low, Vector3.up).normalized;
        Quaternion rotation = Quaternion.LookRotation(flat.normalized, up) * Quaternion.Inverse(Quaternion.LookRotation(authored, Vector3.up));
        Vector3 scale = new(1.75f / spec.width, crossing.flight ? rise / Mathf.Max(.01f, spec.rise) : 1f, flat.magnitude / spec.run);
        if (Mathf.Abs(authored.x) > .5f) scale = new Vector3(flat.magnitude / spec.run, scale.y, 1.75f / spec.width);
        crossing.matrix = Matrix4x4.TRS(from - rotation * Vector3.Scale(spec.low, scale), rotation, scale);
        crossing.bounds = CastleGeometry.TransformBounds(crossing.flight ? districtFlightBounds : districtPathBounds, crossing.matrix);
    }

    private void ReleaseDistrictBody(DistrictRoom room)
    {
        if (room.body == null) return;
        room.body.root.SetActive(false); freeDistrictBodies.Enqueue(room.body); room.body = null;
        physicalDistrictRooms.Remove(room);
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
            for (int x = -6; x <= 6; x++) for (int y = -6; y <= 6; y++) for (int z = -6; z <= 6; z++)
            {
                int radius = x * x + y * y + z * z;
                if (radius <= 36) wantedDenseBlocks.Add(cell + new Vector3Int(x, y, z));
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
        DenseCastleCrossingCount = 0;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictCrossing crossing in block.crossings)
            if (crossing.active) DenseCastleCrossingCount++;
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
            if (room.body != null || !AcquireDistrictBody(room)) continue;
            if (++promoted >= 4 && initialized) break;
        }
        nearDistrictCrossings.Clear();
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            bool nearby = block.bounds.SqrDistance(player.position) < 24f * 24f || block.bounds.SqrDistance(ahead) < 12f * 12f;
            foreach (DistrictCrossing crossing in block.crossings)
            {
                if (!nearby || !crossing.active) { ReleaseDistrictCrossing(crossing); continue; }
                // A sloping stair's AABB includes empty air. Rank the real walking line, so
                // unrelated ramps cannot take the collider lease from the player's path.
                crossing.physicsDistance = DistrictPathDistance(crossing, player.position);
                bool wanted = crossing.physicsDistance < 24f * 24f || DistrictPathDistance(crossing, ahead) < 12f * 12f;
                if (!wanted) { ReleaseDistrictCrossing(crossing); continue; }
                if (crossing.link == null) nearDistrictCrossings.Add(crossing);
            }
        }
        nearDistrictCrossings.Sort((a, b) => a.physicsDistance.CompareTo(b.physicsDistance));
        int paths = 0;
        foreach (DistrictCrossing crossing in nearDistrictCrossings)
        {
            if (linkPool.Count < 12 || initialized && paths >= 6) break;
            crossing.link = linkPool.Dequeue(); crossing.link.root.transform.SetParent(denseRoot, true);
            ConfigureDistrictStairCollider(crossing);
            // Leasing collision must not switch the already-visible instanced path's representation.
            foreach (Renderer renderer in crossing.link.renderers) renderer.enabled = false;
            paths++;
        }
        Physics.SyncTransforms();
    }

    private static float DistrictPathDistance(DistrictCrossing crossing, Vector3 point)
    {
        Vector3 delta = crossing.end - crossing.start;
        float along = Mathf.Clamp01(Vector3.Dot(point - crossing.start, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
        return (point - (crossing.start + delta * along)).sqrMagnitude;
    }

    private void ConfigureDistrictStairCollider(DistrictCrossing crossing)
    {
        Link link = crossing.link;
        Vector3 up = crossing.a.rotation * Vector3.up, delta = crossing.end - crossing.start;
        ConfigureNetworkBridge(link, crossing.start, crossing.end, up);
        link.flat.SetActive(false);
        Quaternion slope = Quaternion.LookRotation(delta.normalized, up);
        link.flatCollision.transform.SetPositionAndRotation((crossing.start + crossing.end) * .5f, slope);
        BoxCollider floor = link.flatCollision.GetComponent<BoxCollider>();
        floor.center = Vector3.down * .1f; floor.size = new Vector3(1.65f, .2f, delta.magnitude + .22f);
        Vector3 across = Vector3.Cross(up, delta).normalized;
        for (int side = 0; side < link.rails.Length; side++)
        {
            var rail = link.rails[side];
            rail.transform.SetPositionAndRotation((crossing.start + crossing.end) * .5f + across * (side == 0 ? -.84f : .84f) + up * .5f, slope);
            rail.size = new Vector3(.08f, 1f, Mathf.Max(.05f, delta.magnitude - 1.8f));
        }
        link.bounds = crossing.bounds;
        link.root.GetComponent<InfiniteCastleGrid>().Configure(-up, link.root.transform.InverseTransformPoint((crossing.start + crossing.end) * .5f + up),
            new Vector3(2.3f, Mathf.Abs(Vector3.Dot(delta, up)) + 4f, Vector3.ProjectOnPlane(delta, up).magnitude + 2f), 1);
    }

    private bool AcquireDistrictBody(DistrictRoom room, bool priority = false)
    {
        if (room.body != null) return true;
        if (freeDistrictBodies.Count == 0)
        {
            // Dense neighbourhoods can fill the hysteresis band. A distant old body must not
            // deny collision to a closer landing; never take the contact area or jump target.
            DistrictRoom victim = null;
            float farthest = Mathf.Max(18f * 18f, priority ? 0f : room.bounds.SqrDistance(player.position) + 4f * 4f);
            foreach (DistrictRoom candidate in physicalDistrictRooms)
            {
                float distance = candidate.bounds.SqrDistance(player.position);
                if (distance <= farthest || IsUltraJumpTarget(candidate)) continue;
                victim = candidate; farthest = distance;
            }
            if (victim != null) ReleaseDistrictBody(victim);
        }
        if (freeDistrictBodies.Count == 0) return false;
        DistrictBody body = freeDistrictBodies.Dequeue(); room.body = body;
        physicalDistrictRooms.Add(room);
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
        foreach (var level in districtDraws) foreach (DistrictDraw draw in level)
        { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtPathDraws) { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtFlightDraws) { draw.instances.Clear(); draw.construction.Clear(); }
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
                Vector4 construction = DistrictTimber(ref room.assemblySeen, ref room.construction, room.bounds, camera);
                foreach (DistrictDraw draw in districtDraws[level])
                { draw.instances.Add(world * draw.local); draw.construction.Add(construction); }
            }
            foreach (DistrictCrossing crossing in block.crossings)
                if (crossing.active && crossing.bounds.SqrDistance(player.position) <= CastleVisibleRadius * CastleVisibleRadius &&
                    GeometryUtility.TestPlanesAABB(districtCameraPlanes, crossing.bounds))
                {
                    Vector4 construction = DistrictTimber(ref crossing.assemblySeen, ref crossing.construction, crossing.bounds, camera);
                    foreach (DistrictDraw draw in crossing.flight ? districtFlightDraws : districtPathDraws)
                    { draw.instances.Add(crossing.matrix * draw.local); draw.construction.Add(construction); }
                }
        }
    }

    private void DrawDistrictInstances(List<DistrictDraw> draws, Camera camera)
    {
        foreach (DistrictDraw draw in draws)
        {
            var parameters = new RenderParams(draw.material) { camera = camera, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer,
                worldBounds = new Bounds(player.position, Vector3.one * 750f), matProps = draw.properties };
            for (int start = 0; start < draw.instances.Count; start += 128)
            {
                int count = Mathf.Min(128, draw.instances.Count - start);
                for (int i = 0; i < count; i++)
                { timberBatch[i] = draw.construction[start + i]; timberMatrices[i] = draw.instances[start + i]; }
                draw.properties.SetVectorArray(TimberInstanceBuildId, timberBatch);
                // Rebase both arrays together; instance-property indices must restart at zero in every batch.
                Graphics.RenderMeshInstanced(parameters, draw.mesh, 0, timberMatrices, count);
            }
        }
    }

    private void RenderDenseDistricts(ScriptableRenderContext context, Camera camera)
    {
        if (player == null || !isActiveAndEnabled || camera.cameraType == CameraType.Preview) return;
        Shader.SetGlobalFloat("_CastleConstructionTime", Time.time);
        Shader.SetGlobalVector("_CastleStreamCenter", new Vector4(player.position.x, player.position.y, player.position.z, 1f));
        Shader.SetGlobalVector("_CastleStreamDistances", new Vector4(CastleFadeStart, CastleVisibleRadius, 0f, 0f));
        UpdateTimberAssemblies(camera);
        DrawNativeDoorways(camera);
        if (denseRoot == null) return;
        BuildDistrictDraws(camera);
        foreach (var level in districtDraws) DrawDistrictInstances(level, camera);
        DrawDistrictInstances(districtPathDraws, camera);
        DrawDistrictInstances(districtFlightDraws, camera);
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
                foreach (DistrictCrossing crossing in block.crossings) if (crossing.active && sweep.Intersects(crossing.bounds)) return false;
            }
        }
        return true;
    }

    private void ShutdownDenseDistricts()
    {
        RenderPipelineManager.beginCameraRendering -= RenderDenseDistricts;
        timberHooked = false;
        CancelTimberAssemblies();
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

    public bool ValidateDistrictDoorways(out string result)
    {
        int doors = 0, flights = 0;
        CastleArchitectureModule template = roomPrefab.GetComponent<CastleArchitectureModule>();
        foreach (var pair in denseBlocks)
        {
            DistrictBlock block = pair.Value;
            foreach (DistrictRoom room in block.rooms)
            {
                if (!room.active) continue;
                foreach (var port in template.ports)
                {
                    Vector3 point = room.position + room.rotation * Vector3.Scale(port.position, room.scale);
                    bool connected = false;
                    foreach (DistrictCrossing crossing in block.crossings)
                        if (crossing.active && ((crossing.start - point).sqrMagnitude < .12f || (crossing.end - point).sqrMagnitude < .12f))
                        { connected = true; break; }
                    if (!connected) { result = "District door has no staircase route at " + point; return false; }
                    doors++;
                }
            }
            foreach (DistrictCrossing crossing in block.crossings)
            {
                if (!crossing.active) continue;
                Vector3 up = crossing.a.rotation * Vector3.up, delta = crossing.end - crossing.start;
                float run = Vector3.ProjectOnPlane(delta, up).magnitude;
                if (run < .1f || Mathf.Abs(Vector3.Dot(delta, up)) / run > .7f)
                { result = "Unsafe district stair slope at " + crossing.start; return false; }
                Stair spec = crossing.flight ? districtFlightStair : districtFlatStair;
                Vector3 low = crossing.matrix.MultiplyPoint3x4(spec.low), high = crossing.matrix.MultiplyPoint3x4(spec.high);
                float endpointError = Mathf.Min((low - crossing.start).magnitude + (high - crossing.end).magnitude,
                    (high - crossing.start).magnitude + (low - crossing.end).magnitude);
                if (endpointError > .04f)
                { result = "Stair mesh misses its landing by " + endpointError + "m"; return false; }
                Bounds cell = new Bounds((Vector3)pair.Key * DistrictSpan, Vector3.one * (DistrictSpan - .02f));
                if (!cell.Contains(crossing.bounds.min) || !cell.Contains(crossing.bounds.max))
                { result = "Stair geometry leaves its collision-safe cell: " + pair.Key + " / " + crossing.bounds; return false; }
                if (crossing.flight) flights++;
            }
        }
        result = $"Distributed doorways: {doors} connected exits, {flights} fitted stair flights; slopes/landings/cell clearance verified.";
        return doors > 0 && flights > 0;
    }

    public bool TryGetDistrictDoorPair(out Vector3 exit, out Vector3 entry, out Vector3 up, out Vector3 outward)
    {
        exit = entry = up = outward = default; float nearest = float.PositiveInfinity;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictCrossing crossing in block.crossings)
        {
            if (!crossing.active || !crossing.doorPair) continue;
            float distance = crossing.a.bounds.SqrDistance(player.position);
            if (distance >= nearest) continue;
            nearest = distance; exit = crossing.start; entry = crossing.end;
            up = crossing.a.rotation * Vector3.up; outward = (entry - exit).normalized;
        }
        return float.IsFinite(nearest);
    }

    public bool TryGetDistrictStairRoute(List<Vector3> points, out Vector3 up)
    {
        points.Clear(); up = Vector3.up;
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            for (int i = 1; i < block.crossings.Length - 1; i++)
            {
                DistrictCrossing stair = block.crossings[i], before = block.crossings[i - 1], after = block.crossings[i + 1];
                if (!stair.active || !stair.flight || !before.active || !after.active || before.flight || after.flight ||
                    before.doorPair || after.doorPair || (before.end - stair.start).sqrMagnitude > 1f || (stair.end - after.start).sqrMagnitude > 1f) continue;
                up = stair.a.rotation * Vector3.up;
                points.Add(before.start - (before.end - before.start).normalized * 1.4f);
                points.Add(before.end); points.Add(stair.end); points.Add(after.start);
                points.Add(after.end + (after.end - after.start).normalized * 1.4f);
                return true;
            }
        }
        return false;
    }
}
