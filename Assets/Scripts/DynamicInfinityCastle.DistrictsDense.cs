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
    private GameObject districtSwitchbackPrefab;
    private CastleStairRoute districtSwitchbackRoute;
    private sealed class DistrictRoom
    {
        public int template, floor, openDoorMask;
        public Vector3 position, scale;
        public Quaternion rotation;
        public Matrix4x4 matrix;
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
        public bool active, flight, doorPair, switchback;
        public float physicsDistance;
    }
    private sealed class DistrictBlock
    {
        public Bounds bounds;
        public readonly DistrictRoom[] rooms = new DistrictRoom[DistrictFloors * DistrictRoomsPerFloor];
        public readonly DistrictCrossing[] crossings = new DistrictCrossing[72];
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
        public MeshCollider[] geometry;
        public BoxCollider floor, roof;
        public readonly BoxCollider[] doors = new BoxCollider[4];
    }
    private sealed class DistrictTemplate
    {
        public CastleArchitectureModule module;
        public MeshCollider[] geometry;
        public Matrix4x4[] collisionMatrices;
        public readonly List<DistrictDraw>[] draws = { new(), new(), new() };
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
    private readonly DistrictTemplate[] districtTemplates = new DistrictTemplate[3];
    private readonly List<DistrictDraw> districtDoorDraws = new();
    private readonly List<DistrictDraw> districtPathDraws = new();
    private readonly List<DistrictDraw> districtFlightDraws = new();
    private readonly List<DistrictDraw> districtSwitchbackDraws = new();
    private readonly List<DistrictCrossing> nearDistrictCrossings = new(512);
    private Transform denseRoot;
    private Vector3Int denseCell;
    private Vector3Int densePredictionCell;
    private float districtPhysicsClock;
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
        int maxColliders = 0;
        GameObject[] prefabs = { roomPrefab, corridorPrefab, junctionPrefab };
        for (int type = 0; type < prefabs.Length; type++)
        {
            GameObject prefab = prefabs[type];
            var spec = new DistrictTemplate { module = prefab.GetComponent<CastleArchitectureModule>(),
                geometry = prefab.GetComponentsInChildren<MeshCollider>(true) };
            districtTemplates[type] = spec;
            spec.collisionMatrices = new Matrix4x4[spec.geometry.Length];
            for (int c = 0; c < spec.geometry.Length; c++)
                spec.collisionMatrices[c] = prefab.transform.worldToLocalMatrix * spec.geometry[c].transform.localToWorldMatrix;
            maxColliders = Mathf.Max(maxColliders, spec.geometry.Length);
            LODGroup group = prefab.GetComponent<LODGroup>();
            Renderer[] high = group != null ? group.GetLODs()[0].renderers : prefab.GetComponentsInChildren<Renderer>(true);
            Renderer[] medium = group != null ? group.GetLODs()[group.lodCount - 1].renderers : high;
            AddDistrictDrawSources(spec.draws[0], high, prefab.transform);
            AddDistrictDrawSources(spec.draws[1], medium, prefab.transform);
            if (type == 0 && architectureDistrictModel != null)
            {
                GameObject sample = new("Complete Room Distance Template"); sample.transform.SetParent(denseRoot, false);
                GameObject art = Instantiate(architectureDistrictModel, sample.transform);
                if (!spec.module.hasAuthoredFloors) FitInnerCastleArt(sample.transform, art.transform, spec.module.localBounds);
                foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = architectureDistrictMaterial != null ? architectureDistrictMaterial : high[0].sharedMaterial;
                AddDistrictDrawSources(spec.draws[2], sample.GetComponentsInChildren<Renderer>(), sample.transform);
                sample.SetActive(false);
            }
            else AddDistrictDrawSources(spec.draws[2], medium, prefab.transform);
        }
        districtDoorDraws.Add(new DistrictDraw { mesh = closedShojiMesh, material = PooledMaterial(closedShojiMaterial) });
        if (districtFlatStair == null || districtFlightStair == null)
            throw new System.InvalidOperationException("Assign the extracted flat/flight stairs from inifinity_castle_pack.fbx.");
        districtPathBounds = CastleGeometry.RendererBoundsInRoot(districtFlatStair.prefab.transform);
        districtFlightBounds = CastleGeometry.RendererBoundsInRoot(districtFlightStair.prefab.transform);
        AddDistrictDrawSources(districtPathDraws, districtFlatStair.prefab.GetComponentsInChildren<Renderer>(true), districtFlatStair.prefab.transform);
        AddDistrictDrawSources(districtFlightDraws, districtFlightStair.prefab.GetComponentsInChildren<Renderer>(true), districtFlightStair.prefab.transform);
        if (districtSwitchbackPrefab == null) throw new System.InvalidOperationException("Assign the intact authored stair_2 switchback.");
        AddDistrictDrawSources(districtSwitchbackDraws, districtSwitchbackPrefab.GetComponentsInChildren<Renderer>(true), districtSwitchbackPrefab.transform);
        for (int i = 0; i < DistrictBlockCapacity; i++) { freeDistrictBlocks.Enqueue(new DistrictBlock()); yield return null; }
        for (int i = 0; i < DistrictBodyCapacity; i++)
        {
            GameObject root = new("Physical Castle District Room " + i); root.transform.SetParent(denseRoot, false);
            var body = new DistrictBody { root = root, geometry = new MeshCollider[maxColliders] };
            for (int c = 0; c < maxColliders; c++)
            {
                var collision = new GameObject("Authored module collision"); collision.transform.SetParent(root.transform, false);
                body.geometry[c] = collision.AddComponent<MeshCollider>();
            }
            body.floor = root.AddComponent<BoxCollider>(); body.roof = root.AddComponent<BoxCollider>();
            for (int door = 0; door < body.doors.Length; door++) body.doors[door] = root.AddComponent<BoxCollider>();
            body.zones = new[] { CreateBuildingZone(root.transform, "District Room Gravity", Vector3.zero, Vector2.one, 3f, true),
                CreateBuildingZone(root.transform, "District Roof Gravity", Vector3.zero, Vector2.one, 3f, true) };
            InfinityCastleBuilder piece = root.AddComponent<InfinityCastleBuilder>(); piece.InitializePooledPiece(this);
            body.piece = piece;
            freeDistrictBodies.Enqueue(body); root.SetActive(false);
            yield return null;
        }
        denseCell = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        yield return UpdateDenseDistricts();
        InitializeTimberAssemblies();
        Debug.Log($"Distributed castle: {DenseCastleRoomCount} rooms/hallways/junctions and {DenseCastleCrossingCount} native paths throughout a 3D volume; no enclosing faces.", this);
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


    private void FitDistrictCrossing(DistrictCrossing crossing, Vector3 up)
    {
        Vector3 from = crossing.start, to = crossing.end;
        if (Vector3.Dot(to - from, up) < 0f) (from, to) = (to, from);
        Vector3 delta = to - from, flat = Vector3.ProjectOnPlane(delta, up);
        float rise = Vector3.Dot(delta, up);
        crossing.flight = rise > .15f;
        Stair spec = crossing.flight ? districtFlightStair : districtFlatStair;
        Vector3 authored = Vector3.ProjectOnPlane(spec.high - spec.low, Vector3.up).normalized;
        Quaternion rotation = Quaternion.LookRotation(crossing.flight ? flat.normalized : delta.normalized, up) * Quaternion.Inverse(Quaternion.LookRotation(authored, Vector3.up));
        float run = crossing.flight ? flat.magnitude : delta.magnitude;
        Vector3 scale = new(1.75f / spec.width, crossing.flight ? rise / Mathf.Max(.01f, spec.rise) : 1f, run / spec.run);
        if (Mathf.Abs(authored.x) > .5f) scale = new Vector3(run / spec.run, scale.y, 1.75f / spec.width);
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
        if (crossing.switchback) return crossing.bounds.SqrDistance(point);
        Vector3 delta = crossing.end - crossing.start;
        float along = Mathf.Clamp01(Vector3.Dot(point - crossing.start, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
        return (point - (crossing.start + delta * along)).sqrMagnitude;
    }

    private void ConfigureDistrictStairCollider(DistrictCrossing crossing)
    {
        Link link = crossing.link;
        Vector3 up = crossing.a.rotation * Vector3.up, delta = crossing.end - crossing.start;
        if (crossing.switchback)
        {
            link.root.transform.SetPositionAndRotation(crossing.start, crossing.matrix.rotation); link.root.transform.localScale = Vector3.one;
            link.flat.SetActive(false); link.flight.SetActive(false); link.ramp.SetActive(false); link.flatCollision.SetActive(false);
            foreach (var rail in link.rails) rail.gameObject.SetActive(false);
            Transform collision = link.switchbackCollision.transform;
            collision.SetPositionAndRotation(crossing.matrix.GetPosition(), crossing.matrix.rotation);
            collision.localScale = crossing.matrix.lossyScale; collision.gameObject.SetActive(true);
            link.bounds = crossing.bounds;
            Bounds local = CastleGeometry.TransformBounds(crossing.bounds, link.root.transform.worldToLocalMatrix);
            link.root.GetComponent<InfiniteCastleGrid>().Configure(-up, local.center,
                local.size + Vector3.one * 2f, 1);
            link.root.GetComponent<InfiniteCastleGrid>().enabled = enableGravityZones;
            link.root.SetActive(true);
            return;
        }
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
            float railLength = delta.magnitude * Mathf.Max(0f, 1f - 2f * PathRailEndClearance / Vector3.ProjectOnPlane(delta, up).magnitude);
            rail.size = new Vector3(.08f, 1f, Mathf.Max(.05f, railLength));
            rail.gameObject.SetActive(railLength > .1f);
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
        ConfigureDistrictBody(body, room);
        return true;
    }

    private void ReleaseDistrictCrossing(DistrictCrossing crossing)
    {
        if (crossing.link == null) return;
        if (crossing.link.switchbackCollision != null) crossing.link.switchbackCollision.SetActive(false);
        foreach (Renderer renderer in crossing.link.renderers) renderer.enabled = true;
        crossing.link.root.SetActive(false); crossing.link.root.transform.SetParent(pathsParent, true);
        linkPool.Enqueue(crossing.link); crossing.link = null;
    }

    private static Vector3 DistrictEntrance(DistrictRoom room, Vector3 port) =>
        room.position + room.rotation * (Vector3.Scale(port, room.scale) - Vector3.forward * .8f + Vector3.up * .18f);

    private void BuildDistrictDraws(Camera camera)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, districtCameraPlanes);
        foreach (DistrictTemplate spec in districtTemplates) foreach (var level in spec.draws) foreach (DistrictDraw draw in level)
        { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtDoorDraws) { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtPathDraws) { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtFlightDraws) { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictDraw draw in districtSwitchbackDraws) { draw.instances.Clear(); draw.construction.Clear(); }
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            if (!GeometryUtility.TestPlanesAABB(districtCameraPlanes, block.bounds)) continue;
            foreach (DistrictRoom room in block.rooms)
            {
                if (!room.active || !GeometryUtility.TestPlanesAABB(districtCameraPlanes, room.bounds)) continue;
                float distance = room.bounds.SqrDistance(player.position);
                if (distance > CastleVisibleRadius * CastleVisibleRadius) continue;
                int level = distance < 18f * 18f ? 0 : distance < 45f * 45f ? 1 : 2;
                Matrix4x4 world = room.matrix;
                Vector4 construction = DistrictTimber(ref room.assemblySeen, ref room.construction, room.bounds, camera);
                foreach (DistrictDraw draw in districtTemplates[room.template].draws[level])
                { draw.instances.Add(world * draw.local); draw.construction.Add(construction); }
                var module = DistrictModule(room);
                for (int door = 0; door < module.ports.Length; door++)
                {
                    if ((room.openDoorMask & (1 << door)) != 0) continue;
                    var port = module.ports[door];
                    districtDoorDraws[0].instances.Add(world * Matrix4x4.TRS(port.position - port.outward * .06f + Vector3.up * 1.15f,
                        Quaternion.LookRotation(port.outward), new Vector3(port.width, 2.3f, .14f)));
                    districtDoorDraws[0].construction.Add(construction);
                }
            }
            foreach (DistrictCrossing crossing in block.crossings)
                if (crossing.active && crossing.bounds.SqrDistance(player.position) <= CastleVisibleRadius * CastleVisibleRadius &&
                    GeometryUtility.TestPlanesAABB(districtCameraPlanes, crossing.bounds))
                {
                    Vector4 construction = DistrictTimber(ref crossing.assemblySeen, ref crossing.construction, crossing.bounds, camera);
                    foreach (DistrictDraw draw in crossing.switchback ? districtSwitchbackDraws : crossing.flight ? districtFlightDraws : districtPathDraws)
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
            for (int start = 0; start < draw.instances.Count; start += CastleInstanceBatchSize)
            {
                int count = Mathf.Min(CastleInstanceBatchSize, draw.instances.Count - start);
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
        foreach (DistrictTemplate spec in districtTemplates) foreach (var level in spec.draws) DrawDistrictInstances(level, camera);
        DrawDistrictInstances(districtDoorDraws, camera);
        DrawDistrictInstances(districtPathDraws, camera);
        DrawDistrictInstances(districtFlightDraws, camera);
        DrawDistrictInstances(districtSwitchbackDraws, camera);
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
        int doors = 0, flights = 0, closed = 0;
        foreach (var pair in denseBlocks)
        {
            DistrictBlock block = pair.Value;
            foreach (DistrictRoom room in block.rooms)
            {
                if (!room.active) continue;
                var template = DistrictModule(room);
                for (int portIndex = 0; portIndex < template.ports.Length; portIndex++)
                {
                    var port = template.ports[portIndex];
                    Vector3 point = room.position + room.rotation * Vector3.Scale(port.position, room.scale);
                    bool connected = false;
                    foreach (DistrictCrossing crossing in block.crossings)
                        if (crossing.active && ((crossing.start - point).sqrMagnitude < .12f || (crossing.end - point).sqrMagnitude < .12f))
                        { connected = true; break; }
                    bool open = (room.openDoorMask & (1 << portIndex)) != 0;
                    if (open != connected || room.body != null && room.body.doors[portIndex].enabled == open)
                    { result = "District door is neither safely connected nor visibly closed at " + point; return false; }
                    if (connected) doors++; else closed++;
                }
            }
            foreach (DistrictCrossing crossing in block.crossings)
            {
                if (!crossing.active) continue;
                Vector3 up = crossing.a.rotation * Vector3.up, delta = crossing.end - crossing.start;
                if (crossing.switchback)
                {
                    Vector3[] route = districtSwitchbackRoute.points;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 step = crossing.matrix.MultiplyVector(route[i + 1] - route[i]);
                        if (Mathf.Abs(Vector3.Dot(step, up)) / Vector3.ProjectOnPlane(step, up).magnitude > .75f)
                        { result = "Authored stair_2 flight is too steep"; return false; }
                    }
                    Bounds switchCell = new Bounds((Vector3)pair.Key * DistrictSpan, Vector3.one * (DistrictSpan - .02f));
                    if (!switchCell.Contains(crossing.bounds.min) || !switchCell.Contains(crossing.bounds.max))
                    { result = "Authored stair_2 leaves its reserved cell"; return false; }
                    flights++;
                    continue;
                }
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
        result = $"Distributed doorways: {doors} connected exits, {closed} closed shoji doors, {flights} fitted stair flights; slopes/landings/cell clearance verified.";
        return doors > 0 && flights > 0;
    }

    public bool TryGetDistrictDoorPair(out Vector3 exit, out Vector3 entry, out Vector3 up, out Vector3 outward)
    {
        exit = entry = up = outward = default; float nearest = float.PositiveInfinity;
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictCrossing crossing in block.crossings)
        {
            if (!crossing.active || !crossing.doorPair || crossing.a.template != 0 || crossing.b.template != 0) continue;
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
                int first = i - 1;
                if (i >= 2 && block.crossings[i - 2].active && !block.crossings[i - 2].flight &&
                    (block.crossings[i - 2].end - before.start).sqrMagnitude < .01f) first = i - 2;
                DistrictCrossing entryPath = block.crossings[first];
                points.Add(entryPath.start - (entryPath.end - entryPath.start).normalized * 1.4f);
                for (int path = first; path < i; path++) points.Add(block.crossings[path].end);
                if (stair.switchback)
                    foreach (Vector3 point in districtSwitchbackRoute.points) points.Add(stair.matrix.MultiplyPoint3x4(point));
                else points.Add(stair.end);
                Vector3 end = stair.end, lastDirection = Vector3.zero;
                for (int next = i + 1; next < block.crossings.Length; next++)
                {
                    DistrictCrossing path = block.crossings[next];
                    if (!path.active || path.flight || (path.start - end).sqrMagnitude > .01f) break;
                    points.Add(path.end); lastDirection = (path.end - path.start).normalized; end = path.end;
                }
                points.Add(end + lastDirection * 1.4f);
                return true;
            }
        }
        return false;
    }
}
