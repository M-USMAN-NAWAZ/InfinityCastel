using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    [Header("Building Traversal")]
    public bool useBuildingTraversal = true;
    public GameObject roomPrefab, corridorPrefab, junctionPrefab, doubleStairPrefab, roofAccessStairPrefab;
    [Range(96, 160)] public int architectureWingPoolSize = 96;
    public List<GameObject> architectureDistanceModels = new();
    public List<Material> architectureDistanceMaterials = new();

    private sealed class BuildingPart
    {
        public CastleArchitectureModule module;
        public InfiniteCastleGrid floorZone, roofZone, cornerZone, returnZone;
        public Bounds bounds;
        public Bounds rootBounds;
        public Matrix4x4 rootFromPart;
        public Quaternion rootRotation;
        public bool reachable = true;
    }
    private sealed class BuildingWing
    {
        public InfinityCastleBuilder piece;
        public readonly List<BuildingPart> parts = new();
        public readonly List<BuildingSeam> seams = new();
        public readonly List<BuildingEntry> entries = new(48);
        public readonly List<Vector3> internalPorts = new(64);
        public Bounds localBounds;
        public Bounds bounds;
        public Collider[] geometry;
        public bool[] collisionEnabled;
        public int serial, group, revision;
        public Transform content;
        public Vector3 baseScale;
        public bool active, carried, physicsNear = true;
        public Vector3 Floor => parts[0].module.WorldFloor;
        public Vector3 Up => piece.transform.up;
    }
    private struct BuildingEntry
    {
        public BuildingPart part;
        public CastleArchitectureModule.Port port;
        public Vector3 rootPoint;
        public float score;
    }
    private sealed class BuildingSeam
    {
        public int a, b;
        public Vector3 exit, entry;
        public Link bridge;
    }
    private sealed class BuildingJoin
    {
        public BuildingWing a, b;
        public Link stair;
        public CastleArchitectureModule from, to;
        public Vector3 exit, entry;
    }
    private readonly List<BuildingWing> buildingWings = new();
    private readonly List<BuildingJoin> buildingJoins = new();
    private readonly List<BuildingWing> movingWingGroup = new();
    private readonly List<Bounds> wingSweeps = new();
    private readonly Queue<BuildingWing> buildingWave = new();
    private readonly HashSet<int> waveGroups = new();
    private readonly RaycastHit[] buildingFloorHits = new RaycastHit[64];
    private BuildingWing buildingLanding;
    private float architectureStreamClock;
    private int architectureStreamRevision;
    private float buildingLayoutClock;
    private int buildingMotionSerial, buildingGroupSerial;
    private bool buildingWaveActive;
    private Vector3 buildingWaveDirection;
    private float roomWidth, roomDepth, corridorDepth, roofFloorHeight;

    public bool BuildingTraversalEnabled => useBuildingTraversal && roomPrefab != null && corridorPrefab != null &&
        junctionPrefab != null && doubleStairPrefab != null && roofAccessStairPrefab != null;
    public int BuildingRoomCount
    {
        get { int count = 0; foreach (BuildingWing wing in buildingWings) if (wing.active) count += wing.parts.Count; return count; }
    }
    public int BuildingWingCount
    {
        get { int count = 0; foreach (BuildingWing wing in buildingWings) if (wing.active) count++; return count; }
    }
    public int BuildingDirectJoinCount
    {
        get { int count = 0; foreach (BuildingJoin join in buildingJoins) if (join.stair == null) count++; return count; }
    }
    public int BuildingStairJoinCount => buildingJoins.Count - BuildingDirectJoinCount;
    private int BuildingMovingCount
    {
        get { int count = 0; foreach (BuildingWing wing in buildingWings) if (wing.active && wing.piece.IsMoving) count++; return count; }
    }

    private void PrepareStartingBuilding()
    {
        if (buildingWings.Count > 0) return;
        // Also repair an already-open Playground scene still holding the old blue fog preset.
        if (RenderSettings.fog && Mathf.Approximately(RenderSettings.fogDensity, .025f) &&
            RenderSettings.fogColor == new Color(.4862745f, .69411767f, .8117647f, 1f))
        { RenderSettings.fogColor = new Color(.13f, .085f, .055f); RenderSettings.fogDensity = .004f; }
        CastleArchitectureModule room = roomPrefab.GetComponent<CastleArchitectureModule>();
        CastleArchitectureModule corridor = corridorPrefab.GetComponent<CastleArchitectureModule>();
        roomWidth = room.floorSize.x + .08f;
        roomDepth = room.floorSize.y + .08f;
        corridorDepth = corridor.floorSize.y;
        roofFloorHeight = room.localBounds.max.y + .22f;
        architectureWingPoolSize = Mathf.Clamp(architectureWingPoolSize, 96, mobileBudget ? 96 : 160);
        BuildingWing first = CreateBuildingWing(0); buildingWings.Add(first);
        PlaceBuildingWing(first, origin, Quaternion.Euler(0f, LayoutPolicy.Direction * 90f, 0f));
        if (controller != null) ReserveGravityLeapClearance(first);
        player.position = first.parts[6].module.WorldFloor + Vector3.up * .08f;
        gravity.RestoreDefaultGravity(); gravity.retainZoneGravityInAir = true;
        if (controller != null)
        {
            controller.MoveSpeed = 4f; controller.JumpHeight = 1.7f;
            controller.TopClamp = Mathf.Max(controller.TopClamp, 85f); controller.BottomClamp = Mathf.Min(controller.BottomClamp, -85f);
            controller.ResetFallVelocity();
        }
        startupSafePosition = player.position; startupSafeUp = Vector3.up; startupSafeRotation = player.rotation;
        Physics.SyncTransforms();
        ConfigureCastleMobileControls();
        IsPlayerReady = true; PlayerReadySeconds = Time.realtimeSinceStartupAsDouble - startupStarted;
        RestoreStartupControls();
    }

    private IEnumerator InitializeBuildingArchitecture()
    {
        PrepareStartingBuilding();
        for (int i = buildingWings.Count; i < architectureWingPoolSize; i++)
        {
            BuildingWing wing = CreateBuildingWing(i); buildingWings.Add(wing);
            yield return null;
            for (int link = 0; link < 3; link++) { linkPool.Enqueue(CreateLink(i * 3 + link)); yield return null; }
            if (i >= architectureWingPoolSize - 2) continue;
            Vector3 target = NetworkTarget(i, player.position, 20f + Mathf.Sqrt(i) * 3f);
            if (i > architectureWingPoolSize / 3) target = FindCastleGap(i, player.position + Vector3.up * 1.8f);
            // Publish each real, connected building while the rest of the pool warms.
            yield return GrowCastleNetwork(wing, target);
        }
        foreach (BuildingWing wing in buildingWings) PoolCapacity += wing.parts.Count;
        yield return BuildCastleNetwork();
        yield return InitializeDenseDistricts();
        Physics.SyncTransforms();
        RefreshReservations();
    }

    public bool ValidateSurroundingCastle(out string error) => ValidateCastleNetwork(out error);

    private BuildingWing CreateBuildingWing(int serial)
    {
        GameObject root = new("Playable Building Wing " + serial.ToString("00"));
        root.transform.SetParent(buildingsParent, false);
        GameObject contentObject = new("ML Assembled Building");
        contentObject.transform.SetParent(root.transform, false);
        Transform content = contentObject.transform;
        var wing = new BuildingWing { serial = serial, group = ++buildingGroupSerial };
        int plan = LayoutPolicy.Layout;
        int Part(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            GameObject art = Instantiate(prefab, content);
            art.transform.localPosition = position;
            art.transform.localRotation = rotation;
            art.SetActive(true);
            var module = art.GetComponent<CastleArchitectureModule>();
            if (module.kind == CastleArchitectureModule.ModuleKind.Room ||
                module.kind == CastleArchitectureModule.ModuleKind.Corridor)
                AddWalkableSurface(art.transform, module.floorPoint, module.floorSize);
            var part = new BuildingPart { module = module };
            part.floorZone = CreateBuildingZone(art.transform, "Room Gravity", module.floorPoint, module.floorSize,
                module.kind == CastleArchitectureModule.ModuleKind.Stair ? 8f : 3f,
                module.kind != CastleArchitectureModule.ModuleKind.Stair);
            if (module.kind == CastleArchitectureModule.ModuleKind.Room)
                part.roofZone = CreateBuildingZone(art.transform, "Roof Gravity", module.roofPoint, module.roofSize, 3f, true);
            wing.parts.Add(part);
            return wing.parts.Count - 1;
        }
        int Join(int source, GameObject prefab, CastleArchitectureModule.Port exit, bool stair = false, bool roof = false,
            bool matchFacing = false, CastleArchitectureModule.Port? entryOverride = null)
        {
            CastleArchitectureModule from = wing.parts[source].module;
            CastleArchitectureModule target = prefab.GetComponent<CastleArchitectureModule>();
            CastleArchitectureModule.Port entry = entryOverride ?? EntryPort(target, stair);
            Quaternion fromRotation = from.transform.localRotation;
            Vector3 normal = fromRotation * exit.outward;
            if (matchFacing) entry = DirectionPort(target, -normal);
            for (int span = 0; span < 3; span++) for (int side = -1; side < from.ports.Length; side++)
            {
                var sourcePort = side < 0 ? exit : from.ports[side];
                if (side >= 0 && (sourcePort.position - exit.position).sqrMagnitude < .001f) continue;
                if (wing.seams.Exists(seam => seam.a == source && (seam.exit - sourcePort.position).sqrMagnitude < .12f ||
                    seam.b == source && (seam.entry - sourcePort.position).sqrMagnitude < .12f)) continue;
                for (int door = -1; door < target.ports.Length; door++)
                {
                    var targetPort = door < 0 ? entry : target.ports[door];
                    if (door >= 0 && ((targetPort.position - entry.position).sqrMagnitude < .001f || entryOverride.HasValue ||
                        (stair && Mathf.Abs(targetPort.position.y - entry.position.y) > .15f))) continue;
                    normal = fromRotation * sourcePort.outward;
                    Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(targetPort.outward, -normal, Vector3.up), Vector3.up);
                    Vector3 targetPoint = targetPort.position + (roof ? target.roofPoint : Vector3.zero);
                    float gap = span == 0 ? .04f : span == 1 ? 3.5f : 7f;
                    Vector3 sourcePoint = from.transform.localPosition + fromRotation * sourcePort.position;
                    Vector3 position = sourcePoint + normal * gap - turn * targetPoint;
                    if (!PartSpaceFree(prefab, position, turn)) continue;
                    if (span > 0)
                    {
                        Bounds passage = CastleGeometry.TransformBounds(new Bounds(Vector3.up * 1.2f, new Vector3(1.3f, 2.2f, gap)),
                            Matrix4x4.TRS(sourcePoint + normal * (gap * .5f), Quaternion.LookRotation(normal), Vector3.one));
                        bool clear = true;
                        foreach (BuildingPart other in wing.parts)
                        {
                            if (other.module == from || other.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                            Bounds box = CastleGeometry.TransformBounds(other.module.localBounds,
                                Matrix4x4.TRS(other.module.transform.localPosition, other.module.transform.localRotation, Vector3.one));
                            box.Expand(-.45f); if (box.Intersects(passage)) { clear = false; break; }
                        }
                        if (!clear) continue;
                    }
                    int added = Part(prefab, position, turn);
                    Link bridge = null;
                    if (span > 0)
                    {
                        bridge = CreateLink(serial);
                        ConfigureNetworkBridge(bridge, from.transform.TransformPoint(sourcePort.position),
                            wing.parts[added].module.transform.TransformPoint(targetPoint), Vector3.up);
                        bridge.root.transform.SetParent(content, true);
                    }
                    wing.parts[added].reachable = !roof;
                    wing.seams.Add(new BuildingSeam { a = source, b = added, exit = sourcePort.position, entry = targetPoint, bridge = bridge });
                    return added;
                }
            }
            throw new InvalidOperationException($"Building {serial}: no collision-free entrance from part {source} to {prefab.name}.");
        }
        bool PartSpaceFree(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            Bounds candidate = CastleGeometry.TransformBounds(prefab.GetComponent<CastleArchitectureModule>().localBounds,
                Matrix4x4.TRS(position, rotation, Vector3.one));
            candidate.Expand(-.45f);
            foreach (BuildingPart existing in wing.parts)
            {
                if (existing.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                Bounds bounds = CastleGeometry.TransformBounds(existing.module.localBounds,
                    Matrix4x4.TRS(existing.module.transform.localPosition, existing.module.transform.localRotation, Vector3.one));
                bounds.Expand(-.45f);
                if (bounds.Intersects(candidate)) return false;
            }
            return true;
        }
        int JoinRoof(int source)
        {
            CastleArchitectureModule from = wing.parts[source].module;
            CastleArchitectureModule target = roomPrefab.GetComponent<CastleArchitectureModule>();
            foreach (Vector3 side in new[] { Vector3.right, Vector3.forward, Vector3.left })
            {
                var exit = DirectionPort(from, side);
                foreach (var entry in target.ports)
                {
                    Vector3 normal = from.transform.localRotation * exit.outward;
                    Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(entry.outward, -normal, Vector3.up), Vector3.up);
                    Vector3 position = from.transform.localPosition + from.transform.localRotation * exit.position + normal * .04f -
                        turn * (entry.position + target.roofPoint);
                    if (PartSpaceFree(roomPrefab, position, turn))
                        return Join(source, roomPrefab, exit, roof: true, entryOverride: entry);
                }
            }
            throw new InvalidOperationException("No clear roof exit for building wing " + serial);
        }
        void Rooftop(int source)
        {
            CastleArchitectureModule room = wing.parts[source].module;
            GameObject marker = new("Native Room Rooftop"); marker.transform.SetParent(content, false);
            marker.transform.localPosition = room.transform.localPosition + room.transform.localRotation * room.roofPoint;
            marker.transform.localRotation = room.transform.localRotation;
            CastleArchitectureModule module = marker.AddComponent<CastleArchitectureModule>();
            module.kind = CastleArchitectureModule.ModuleKind.Roof; module.floorSize = room.roofSize;
            module.localBounds = new Bounds(Vector3.zero, new Vector3(room.roofSize.x, .1f, room.roofSize.y));
            module.ports = room.ports;
            AddWalkableSurface(marker.transform, Vector3.zero, room.roofSize);
            wing.parts.Add(new BuildingPart { module = module,
                floorZone = CreateBuildingZone(marker.transform, "Native Roof Gravity", Vector3.zero, room.roofSize, 3f, true) });
        }
        void GravityRoom(int source)
        {
            CastleArchitectureModule roofRoom = wing.parts[source].module;
            foreach (Vector3 outward in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 up = roofRoom.transform.localRotation * outward;
                Vector3 forward = roofRoom.transform.localRotation * (Mathf.Abs(outward.z) > .5f ? Vector3.right : Vector3.forward);
                Quaternion rotation = Quaternion.LookRotation(forward, up);
                Vector3 edge = roofRoom.roofPoint;
                edge.x = outward.x > .5f ? roofRoom.localBounds.max.x + .35f : outward.x < -.5f ? roofRoom.localBounds.min.x - .35f : edge.x;
                edge.z = outward.z > .5f ? roofRoom.localBounds.max.z + .35f : outward.z < -.5f ? roofRoom.localBounds.min.z - .35f : edge.z;
                edge.y += .35f;
                Vector3 position = roofRoom.transform.localPosition + roofRoom.transform.localRotation * edge;
                if (!PartSpaceFree(roomPrefab, position, rotation)) continue;
                int added = Part(roomPrefab, position, rotation);
                BuildingPart gravityRoom = wing.parts[added];
                gravityRoom.floorZone.cornerTransition = false;
                gravityRoom.roofZone.cornerTransition = false;
                gravityRoom.cornerZone = CreateBuildingZone(gravityRoom.module.transform, "Roof To Room Gravity Corner",
                    Quaternion.Inverse(rotation) * (Vector3.down * .35f), new Vector2(2.2f, 2f), 2.2f, true);
                gravityRoom.cornerZone.priority = 4;
                gravityRoom.cornerZone.cornerEntryOnly = true;
                gravityRoom.cornerZone.Configure(Vector3.down,
                    Vector3.up * .75f + Quaternion.Inverse(rotation) * Vector3.up * .7f,
                    new Vector3(2.8f, 3.2f, 2f), 4, true, true);
                Vector3 returnPoint = roofRoom.roofPoint - outward * 1.05f;
                returnPoint.x += outward.x > .5f ? roofRoom.localBounds.max.x : outward.x < -.5f ? roofRoom.localBounds.min.x : 0f;
                returnPoint.z += outward.z > .5f ? roofRoom.localBounds.max.z : outward.z < -.5f ? roofRoom.localBounds.min.z : 0f;
                gravityRoom.returnZone = CreateBuildingZone(roofRoom.transform, "Room To Roof Gravity Corner", returnPoint,
                    Mathf.Abs(outward.x) > .5f ? new Vector2(4f, 2f) : new Vector2(2f, 4f), 2.2f, true);
                gravityRoom.returnZone.cornerSnapDistance = 2.6f;
                return;
            }
        }
        Part(roomPrefab, Vector3.zero, Quaternion.identity);
        if (serial >= architectureWingPoolSize - 2)
        {
            Rooftop(0);
        }
        else if (serial == 0)
        {
            Join(0, roomPrefab, DirectionPort(wing.parts[0].module, Vector3.left), matchFacing: true);
            Join(0, roofAccessStairPrefab, DirectionPort(wing.parts[0].module, Vector3.forward), stair: true);
            Join(2, roomPrefab, UpperPort(wing.parts[2].module));
            Join(3, roomPrefab, DirectionPort(wing.parts[3].module, Vector3.forward));
            Join(3, roomPrefab, DirectionPort(wing.parts[3].module, Vector3.right), roof: true);
            Rooftop(5);
            GravityRoom(5);
        }
        else if (Mathf.Abs(serial * 5 + plan * 3 + LayoutPolicy.Direction) % 6 < 4)
        {
            int variant = Mathf.Abs(serial * 5 + plan * 3 + LayoutPolicy.Direction) % 6;
            GameObject hall = variant % 2 == 0 ? corridorPrefab : junctionPrefab;
            Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.left };
            Join(0, hall, DirectionPort(wing.parts[0].module, directions[variant % directions.Length]));
            CastleArchitectureModule corridor = wing.parts[1].module;
            CastleArchitectureModule.Port entry = EntryPort(corridor, false);
            var exits = new List<CastleArchitectureModule.Port>();
            foreach (var port in corridor.ports) if ((port.position - entry.position).sqrMagnitude > .1f) exits.Add(port);
            int exit = variant % exits.Count;
            Join(1, roomPrefab, exits[exit]);
            if (variant >= 2 && exits.Count > 1) Join(1, roomPrefab, exits[(exit + 1) % exits.Count]);
            else Join(2, roomPrefab, DirectionPort(wing.parts[2].module, Vector3.forward));
            Vector3 stairSide = variant % 2 == 0 ? Vector3.right : Vector3.left;
            Join(3, roofAccessStairPrefab, DirectionPort(wing.parts[3].module, stairSide), stair: true);
            int raised = Join(4, roomPrefab, UpperPort(wing.parts[4].module));
            int roofRoom = JoinRoof(raised);
            Rooftop(roofRoom);
            GravityRoom(roofRoom);
        }
        else
        {
            bool mirror = (serial + plan + LayoutPolicy.Direction) % 2 == 0;
            Join(0, roomPrefab, DirectionPort(wing.parts[0].module, Vector3.forward));
            Join(1, roomPrefab, DirectionPort(wing.parts[1].module, Vector3.forward));
            Join(1, roomPrefab, DirectionPort(wing.parts[1].module, mirror ? Vector3.right : Vector3.left));
            Join(0, roofAccessStairPrefab, DirectionPort(wing.parts[0].module, mirror ? Vector3.left : Vector3.right), stair: true);
            int raised = Join(4, roomPrefab, UpperPort(wing.parts[4].module));
            int roofRoom = JoinRoof(raised);
            Rooftop(roofRoom);
            GravityRoom(roofRoom);
        }
        bool exteriorAdded = false;
        for (int source = wing.parts.Count - 1; serial < architectureWingPoolSize - 2 && source >= 0 && !exteriorAdded; source--)
        {
            BuildingPart part = wing.parts[source];
            if (!part.reachable || part.module.kind == CastleArchitectureModule.ModuleKind.Stair ||
                Vector3.Dot(part.module.transform.up, Vector3.up) < .99f || flatStair == null) continue;
            foreach (var exit in part.module.ports)
            {
                if (wing.seams.Exists(seam => seam.a == source && (seam.exit - exit.position).sqrMagnitude < .12f ||
                    seam.b == source && (seam.entry - exit.position).sqrMagnitude < .12f)) continue;
                for (int attempt = 0; attempt < mobileDecorationPrefabs.Count && !exteriorAdded; attempt++)
                {
                    GameObject prefab = mobileDecorationPrefabs[(serial + attempt) % mobileDecorationPrefabs.Count];
                    var tower = prefab.GetComponent<CastleArchitectureModule>();
                    if (tower == null || tower.ports.Length == 0) continue;
                    foreach (var entry in tower.ports)
                    {
                        Vector3 normal = part.module.transform.localRotation * exit.outward;
                        Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(entry.outward, -normal, Vector3.up), Vector3.up);
                        Vector3 position = part.module.transform.localPosition + part.module.transform.localRotation * exit.position +
                            normal * flatStair.run - turn * entry.position;
                        if (!PartSpaceFree(prefab, position, turn)) continue;
                        int added = Part(prefab, position, turn);
                        Link bridge = CreateLink(serial);
                        ConfigureLink(bridge, part.module.transform.TransformPoint(exit.position),
                            wing.parts[added].module.transform.TransformPoint(entry.position), Vector3.up, turn, endpoints: true);
                        bridge.root.transform.SetParent(content, true);
                        wing.seams.Add(new BuildingSeam { a = source, b = added, exit = exit.position, entry = entry.position, bridge = bridge });
                        exteriorAdded = true;
                        break;
                    }
                }
                if (exteriorAdded) break;
            }
        }
        // Keep the double staircase as an optional upper landing; its lower edge is not an entrance.
        CastleArchitectureModule landing = doubleStairPrefab.GetComponent<CastleArchitectureModule>();
        var landingEntry = UpperPort(landing);
        bool landingAdded = false;
        for (int source = 0; serial >= 6 && serial < architectureWingPoolSize - 2 && source < wing.parts.Count && !landingAdded; source++)
        {
            BuildingPart part = wing.parts[source];
            if (!part.reachable || part.module.kind != CastleArchitectureModule.ModuleKind.Room ||
                Vector3.Dot(part.module.transform.localRotation * Vector3.up, Vector3.up) < .99f) continue;
            foreach (var exit in part.module.ports)
            {
                if (wing.seams.Exists(seam => seam.a == source && (seam.exit - exit.position).sqrMagnitude < .12f ||
                    seam.b == source && (seam.entry - exit.position).sqrMagnitude < .12f)) continue;
                Vector3 normal = part.module.transform.localRotation * exit.outward;
                Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(landingEntry.outward, -normal, Vector3.up), Vector3.up);
                Vector3 position = part.module.transform.localPosition + part.module.transform.localRotation * exit.position +
                    normal * .04f - turn * landingEntry.position;
                if (!PartSpaceFree(doubleStairPrefab, position, turn)) continue;
                Join(source, doubleStairPrefab, exit, stair: true, entryOverride: landingEntry);
                landingAdded = true;
                break;
            }
        }
        Vector3Int shape = new(serial, plan, LayoutPolicy.Direction);
        if (serial > 0 && serial < architectureWingPoolSize - 2)
        {
            int extensions = 1 + Mathf.FloorToInt(Hash(shape, 404) * 4f);
            for (int extension = 0; extension < extensions; extension++)
            {
                bool added = false;
                int start = Mathf.FloorToInt(Hash(shape, 410 + extension) * wing.parts.Count);
                for (int attempt = 0; attempt < wing.parts.Count && !added; attempt++)
                {
                    int source = (start + attempt) % wing.parts.Count;
                    CastleArchitectureModule from = wing.parts[source].module;
                    if (!wing.parts[source].reachable || from.kind != CastleArchitectureModule.ModuleKind.Room) continue;
                    GameObject prefab = Hash(shape, 420 + extension) < .3f ? junctionPrefab : roomPrefab;
                    CastleArchitectureModule target = prefab.GetComponent<CastleArchitectureModule>();
                    var entry = EntryPort(target, false);
                    foreach (var exit in from.ports)
                    {
                        if (wing.seams.Exists(seam => seam.a == source && (seam.exit - exit.position).sqrMagnitude < .12f ||
                            seam.b == source && (seam.entry - exit.position).sqrMagnitude < .12f)) continue;
                        Vector3 normal = from.transform.localRotation * exit.outward;
                        // Extra rooms stay in their source room's gravity frame.
                        if (Vector3.Dot(from.transform.up, Vector3.up) < .99f) continue;
                        Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(entry.outward, -normal, Vector3.up), Vector3.up);
                        Vector3 position = from.transform.localPosition + from.transform.localRotation * exit.position + normal * .04f - turn * entry.position;
                        if (!PartSpaceFree(prefab, position, turn)) continue;
                        Join(source, prefab, exit); added = true; break;
                    }
                }
            }
        }
        if (serial == 0)
        {
            // Real, separated landing rooms: one overhead and four sideways, not a boundary shell.
            Vector3 launch = wing.parts[6].module.transform.localPosition;
            foreach (Vector3 outward in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Quaternion turn = Quaternion.LookRotation(Mathf.Abs(outward.z) > .5f ? Vector3.right : Vector3.forward, -outward);
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Vector3 roof = launch + outward * ((outward == Vector3.forward ? 46f : 22f) + attempt * 6f);
                    if (outward == Vector3.back) roof += Vector3.up * 10f;
                    Vector3 position = roof - turn * roomPrefab.GetComponent<CastleArchitectureModule>().roofPoint;
                    if (!PartSpaceFree(roomPrefab, position, turn)) continue;
                    int added = Part(roomPrefab, position, turn);
                    wing.parts[added].module.name = "Gravity Leap Room " + outward;
                    wing.parts[added].reachable = false;
                    Rooftop(added); wing.parts[wing.parts.Count - 1].reachable = false;
                    break;
                }
            }
        }
        content.localScale = new Vector3(Mathf.Lerp(.76f, 1.48f, Hash(shape, 401)),
            Mathf.Lerp(.95f, 1.30f, Hash(shape, 402)), Mathf.Lerp(.78f, 1.55f, Hash(shape, 403)));
        wing.content = content; wing.baseScale = content.localScale;
        root.name += $" {content.localScale.x:F2}x{content.localScale.y:F2}x{content.localScale.z:F2}";
        wing.piece = root.AddComponent<InfinityCastleBuilder>();
        wing.piece.InitializePooledPiece(this);
        wing.localBounds = CastleGeometry.RendererBoundsInRoot(root.transform);
        wing.geometry = root.GetComponentsInChildren<Collider>(true);
        CacheBuildingTemplate(wing);
        UpdateBuildingZones(wing);
        wing.collisionEnabled = new bool[wing.geometry.Length];
        for (int i = 0; i < wing.geometry.Length; i++) wing.collisionEnabled[i] = wing.geometry[i].enabled;
        root.SetActive(false);
        return wing;
    }

    private static void AddWalkableSurface(Transform parent, Vector3 floorPoint, Vector2 floorSize)
    {
        GameObject surface = new("Runtime Walkable Surface");
        surface.transform.SetParent(parent, false);
        surface.transform.localPosition = floorPoint + Vector3.down * .06f;
        BoxCollider collider = surface.AddComponent<BoxCollider>();
        collider.size = new Vector3(Mathf.Max(.75f, floorSize.x - .16f), .12f,
            Mathf.Max(.75f, floorSize.y - .16f));
    }

    private GameObject PrepareInnerCastleModule(GameObject source)
    {
        if (source.GetComponent<CastleArchitectureModule>() != null) return source;
        GameObject root = new(source.name + " Runtime Module");
        root.transform.SetParent(transform, false);
        GameObject art = Instantiate(source, root.transform);
        DisableAuthoredLights(art);
        EnsureSolidColliders(art);
        CastleArchitectureModule module = root.AddComponent<CastleArchitectureModule>();
        ConfigureInnerCastleModule(module, source.name);
        FitInnerCastleArt(root.transform, art.transform, module.localBounds);
        foreach (GameObject distant in architectureDistanceModels)
        {
            if (!distant.name.StartsWith(source.name + "_LOD")) continue;
            GameObject lowRoot = new("Distant Architecture LOD"); lowRoot.transform.SetParent(root.transform, false);
            GameObject lowArt = Instantiate(distant, lowRoot.transform);
            FitInnerCastleArt(lowRoot.transform, lowArt.transform, module.localBounds);
            int index = architectureDistanceModels.IndexOf(distant);
            Material material = index < architectureDistanceMaterials.Count ? architectureDistanceMaterials[index] : null;
            if (material == null) material = art.GetComponentInChildren<Renderer>().sharedMaterial;
            foreach (Renderer renderer in lowRoot.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
            FinishArt(lowRoot.transform);
            root.AddComponent<LODGroup>().SetLODs(new[] {
                new LOD(.22f, art.GetComponentsInChildren<Renderer>()),
                new LOD(.001f, lowRoot.GetComponentsInChildren<Renderer>()) });
            root.GetComponent<LODGroup>().RecalculateBounds();
            break;
        }
        root.SetActive(false);
        return root;
    }

    private static void FitInnerCastleArt(Transform wrapper, Transform art, Bounds target)
    {
        Bounds current = CastleGeometry.RendererBoundsInRoot(wrapper);
        float scale = target.size.magnitude / current.size.magnitude;
        art.localScale *= scale;
        Bounds fitted = CastleGeometry.RendererBoundsInRoot(wrapper);
        art.localPosition += target.center - fitted.center;
        fitted = CastleGeometry.RendererBoundsInRoot(wrapper);
        Debug.Assert((fitted.center - target.center).sqrMagnitude < .001f &&
            Mathf.Abs(fitted.size.magnitude - target.size.magnitude) < .02f,
            "InnerCastle art did not fit its runtime module bounds.");
    }

    private static void ConfigureInnerCastleModule(CastleArchitectureModule module, string name)
    {
        CastleArchitectureModule.Port Port(Vector3 position, Vector3 outward, float width) =>
            new() { position = position, outward = outward, width = width };
        switch (name)
        {
            case "CorrectedRoom":
                module.kind = CastleArchitectureModule.ModuleKind.Room;
                module.localBounds = new Bounds(new Vector3(-.000573f, 1.743732f, .000172f), new Vector3(7.274077f, 4.965935f, 5.77914f));
                module.floorSize = new Vector2(7.274077f, 5.77914f); module.roofPoint = Vector3.up * 3.5f;
                module.roofSize = new Vector2(6.924077f, 5.42914f);
                module.ports = new[] { Port(new Vector3(-.007612f, 0f, 2.709741f), Vector3.forward, 2.04f),
                    Port(new Vector3(3.456466f, 0f, .699742f), Vector3.right, 2.52f),
                    Port(new Vector3(-3.457611f, 0f, .740601f), Vector3.left, 2.52f) };
                break;
            case "HallWayWithTurnFixed":
                module.kind = CastleArchitectureModule.ModuleKind.Corridor;
                module.localBounds = new Bounds(new Vector3(.004292f, 1.667948f, .000117f), new Vector3(7.903196f, 4.962653f, 7.711104f));
                module.floorSize = new Vector2(7.903196f, 7.711104f); module.roofPoint = Vector3.up * 3.5f;
                module.roofSize = new Vector2(7.553196f, 7.361104f);
                module.ports = new[] { Port(new Vector3(-2.117308f, 0f, 3.675669f), Vector3.forward, 1.68f),
                    Port(new Vector3(1.285891f, 0f, -3.675436f), Vector3.back, 3f),
                    Port(new Vector3(3.77589f, 0f, -2.05433f), Vector3.right, 1.92f),
                    Port(new Vector3(-3.767306f, 0f, -2.025437f), Vector3.left, 2.04f) };
                break;
            case "HallWay3Way":
                module.kind = CastleArchitectureModule.ModuleKind.Corridor;
                module.localBounds = new Bounds(new Vector3(.003007f, 1.877047f, .002475f), new Vector3(12.093962f, 4.649627f, 8.661213f));
                module.floorSize = new Vector2(12.093962f, 8.661213f); module.roofPoint = Vector3.up * 3.5f;
                module.roofSize = new Vector2(11.743961f, 8.311213f);
                module.ports = new[] { Port(new Vector3(-2.533977f, .206679f, 4.153081f), Vector3.forward, 1.56f),
                    Port(new Vector3(-2.500008f, .212463f, -4.148132f), Vector3.back, 1.56f),
                    Port(new Vector3(-5.863974f, .183218f, -1.778134f), Vector3.left, 3f) };
                break;
            case "DoubleStaresBase":
                module.kind = CastleArchitectureModule.ModuleKind.Stair;
                module.localBounds = new Bounds(new Vector3(-.001898f, -.944007f, -.025095f), new Vector3(9.14111f, 4.108849f, 8.354752f));
                module.floorPoint = module.roofPoint = new Vector3(-4.392453f, .000684f, .747527f);
                module.floorSize = new Vector2(9.14111f, 8.354752f); module.roofSize = new Vector2(8.79111f, 8.004751f);
                module.ports = new[] { Port(module.floorPoint, Vector3.left, 1.2f) };
                break;
            case "SingleStareBase":
                module.kind = CastleArchitectureModule.ModuleKind.Stair;
                module.localBounds = new Bounds(new Vector3(.000119f, -.464269f, -.000108f), new Vector3(7.745963f, 3.352814f, 8.302068f));
                module.floorPoint = new Vector3(-.602864f, -1.424797f, 3.970926f);
                module.roofPoint = new Vector3(-2.036898f, .020779f, -3.971142f);
                module.floorSize = new Vector2(7.745963f, 8.302068f); module.roofSize = new Vector2(7.395963f, 7.952068f);
                module.ports = new[] { Port(module.floorPoint, Vector3.forward, 1.2f), Port(module.roofPoint, Vector3.back, 2.28f),
                    Port(new Vector3(3.693101f, -1.099183f, -1.039072f), Vector3.right, 1.8f),
                    Port(new Vector3(-3.692862f, .001916f, -1.001143f), Vector3.left, 3f) };
                break;
            default: throw new InvalidOperationException("Unsupported InnerCastle module: " + name);
        }
    }

    private static CastleArchitectureModule.Port DirectionPort(CastleArchitectureModule module, Vector3 direction)
    {
        CastleArchitectureModule.Port best = module.ports[0]; float score = -2;
        foreach (var port in module.ports)
        {
            float next = Vector3.Dot(port.outward, direction);
            if (next > score || (Mathf.Abs(next - score) < .001f && port.outward.x < best.outward.x)) { best = port; score = next; }
        }
        return best;
    }
    private static CastleArchitectureModule.Port EntryPort(CastleArchitectureModule module, bool stair)
    {
        if (!stair) return DirectionPort(module, Vector3.back);
        CastleArchitectureModule.Port entry = module.ports[0];
        foreach (var port in module.ports) if (port.position.y < entry.position.y) entry = port;
        return entry;
    }
    private static CastleArchitectureModule.Port UpperPort(CastleArchitectureModule module)
    {
        CastleArchitectureModule.Port upper = module.ports[0];
        foreach (var port in module.ports)
            if (port.position.y > upper.position.y + .001f ||
                (Mathf.Abs(port.position.y - upper.position.y) < .001f && port.outward.z < upper.outward.z)) upper = port;
        return upper;
    }

    private InfiniteCastleGrid CreateBuildingZone(Transform parent, string name, Vector3 surface, Vector2 size, float height, bool bound)
    {
        GameObject root = new(name); root.transform.SetParent(parent, false); root.transform.localPosition = surface;
        InfiniteCastleGrid zone = root.AddComponent<InfiniteCastleGrid>();
        // A corner field reaches in front of the floor's thickness plus the player
        // radius, so gravity turns before the capsule treats that floor as a wall.
        zone.Configure(Vector3.down, Vector3.up * (height * .5f - .35f),
            new Vector3(Mathf.Max(1.2f, size.x), height + 1f, Mathf.Max(1.2f, size.y)), 3, bound, bound);
        return zone;
    }

    private void PlaceBuildingWing(BuildingWing wing, Vector3 floor, Quaternion orientation)
    {
        SetBuildingPhysics(wing, true);
        wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
        wing.active = true; wing.carried = false; wing.group = ++buildingGroupSerial;
        wing.piece.gameObject.SetActive(true);
        UpdateBuildingZones(wing);
    }

    private void UpdateBuildingZones(BuildingWing wing)
    {
        CacheBuildingBounds(wing);
        foreach (BuildingSeam seam in wing.seams)
            if (seam.bridge != null)
            {
                InfiniteCastleGrid zone = seam.bridge.root.GetComponent<InfiniteCastleGrid>();
                zone.gravityDirection = -wing.Up;
                zone.enabled = enableGravityZones && wing.physicsNear;
                seam.bridge.bounds = ConnectionVisualBounds(seam.bridge);
            }
        foreach (BuildingPart part in wing.parts)
        {
            part.floorZone.gravityDirection = -part.module.transform.up; part.floorZone.enabled = enableGravityZones && wing.physicsNear;
            if (part.cornerZone != null)
            { part.cornerZone.gravityDirection = -part.module.transform.up; part.cornerZone.enabled = enableGravityZones && wing.physicsNear; }
            if (part.returnZone != null)
            { part.returnZone.gravityDirection = -part.returnZone.transform.up; part.returnZone.enabled = enableGravityZones && wing.physicsNear; }
            if (part.roofZone == null) continue;
            part.roofZone.gravityDirection = -part.module.transform.up; part.roofZone.enabled = enableGravityZones && wing.physicsNear;
        }
    }

    private static void CacheBuildingBounds(BuildingWing wing)
    {
        wing.bounds = wing.piece.WorldBounds;
        Matrix4x4 world = wing.piece.transform.localToWorldMatrix;
        foreach (BuildingPart part in wing.parts) part.bounds = CastleGeometry.TransformBounds(part.rootBounds, world);
    }

    private static void CacheBuildingTemplate(BuildingWing wing)
    {
        Transform root = wing.piece.transform;
        foreach (BuildingPart part in wing.parts)
        {
            part.rootFromPart = root.worldToLocalMatrix * part.module.transform.localToWorldMatrix;
            part.rootRotation = Quaternion.Inverse(root.rotation) * part.module.transform.rotation;
            part.rootBounds = CastleGeometry.TransformBounds(part.module.localBounds, part.rootFromPart);
        }
        wing.internalPorts.Clear(); wing.entries.Clear();
        foreach (BuildingSeam seam in wing.seams)
        {
            wing.internalPorts.Add(wing.parts[seam.a].rootFromPart.MultiplyPoint3x4(seam.exit));
            wing.internalPorts.Add(wing.parts[seam.b].rootFromPart.MultiplyPoint3x4(seam.entry));
        }
        foreach (BuildingPart part in wing.parts)
        {
            if (!part.reachable || part.module.kind == CastleArchitectureModule.ModuleKind.Roof ||
                part.module.kind == CastleArchitectureModule.ModuleKind.Stair || part.module.kind == CastleArchitectureModule.ModuleKind.Exterior) continue;
            foreach (var port in part.module.ports)
            {
                Vector3 point = part.rootFromPart.MultiplyPoint3x4(port.position);
                bool used = false;
                foreach (Vector3 linked in wing.internalPorts) if ((linked - point).sqrMagnitude < .12f) { used = true; break; }
                if (used) continue;
                Vector3 normal = part.rootRotation * port.outward;
                wing.entries.Add(new BuildingEntry { part = part, port = port, rootPoint = point,
                    score = Vector3.Dot(point - wing.localBounds.center, normal) /
                        Mathf.Max(1f, Vector3.Dot(wing.localBounds.extents, new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)))) });
            }
        }
        // Prefer exposed perimeter doors instead of trying every interior doorway first.
        wing.entries.Sort((a, b) => b.score.CompareTo(a.score));
    }

    private BuildingWing AvailableBuildingWing(bool emergency = false)
    {
        if (mainCamera != null) GeometryUtility.CalculateFrustumPlanes(mainCamera, cameraPlanes);
        int reserveStart = Mathf.Min(buildingWings.Count, architectureWingPoolSize - 2);
        if (emergency)
        {
            for (int i = reserveStart; i < buildingWings.Count; i++) if (!buildingWings[i].active) return buildingWings[i];
            for (int i = reserveStart; i < buildingWings.Count; i++)
            {
                BuildingWing wing = buildingWings[i];
                float separation = (wing.Floor - player.position).sqrMagnitude;
                if (wing.piece.IsMoving || IsBuildingSupport(wing) || separation < 70f * 70f) continue;
                if (mainCamera != null && separation < 160f * 160f && GeometryUtility.TestPlanesAABB(cameraPlanes, wing.piece.WorldBounds)) continue;
                RetireBuildingWing(wing);
                return wing;
            }
            return null;
        }
        for (int i = 0; i < reserveStart; i++) if (!buildingWings[i].active) return buildingWings[i];
        return null;
    }

    private void RetireBuildingWing(BuildingWing wing)
    {
        for (int i = buildingJoins.Count - 1; i >= 0; i--)
        {
            BuildingJoin join = buildingJoins[i];
            if (join.a != wing && join.b != wing) continue;
            if (join.stair != null)
            {
                join.stair.root.transform.SetParent(pathsParent, true);
                join.stair.root.SetActive(false); linkPool.Enqueue(join.stair);
            }
            buildingJoins.RemoveAt(i);
        }
        wing.piece.StopShift(); wing.piece.gameObject.SetActive(false); wing.active = false;
    }

    private int GroupSize(BuildingWing wing)
    {
        int count = 0;
        foreach (BuildingWing other in buildingWings) if (other.active && other.group == wing.group) count++;
        return count;
    }

    private bool BuildingSpaceFree(Bounds box, BuildingWing ignore, bool includePlayer = true, int joiningGroup = 0)
    {
        if (ignore.serial != 0)
            foreach (Bounds corridor in gravityLeapClearance)
                if (box.Intersects(corridor))
                    foreach (BuildingPart part in ignore.parts)
                        if (part.module.kind != CastleArchitectureModule.ModuleKind.Roof && part.bounds.Intersects(corridor)) return false;
        if (!DenseWingSpaceFree(ignore, Vector3.zero)) return false;
        foreach (BuildingWing other in buildingWings)
            if (other.active && other != ignore && other.piece.ReservedBounds.Intersects(box))
            {
                // Door frames share a narrow architectural seam. Reject any deeper
                // intersection, while allowing empty corners of an L/T wing's AABB.
                if (BuildingModulesOverlap(other, ignore)) return false;
            }
        foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(box)) return false;
        foreach (BuildingJoin join in buildingJoins)
            if (join.stair != null && join.a != ignore && join.b != ignore && join.stair.bounds.Intersects(box))
                foreach (BuildingPart part in ignore.parts)
                {
                    if (part.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                    Bounds bounds = part.bounds; bounds.Expand(-.45f);
                    if (join.stair.bounds.Intersects(bounds)) return false;
                }
        if (includePlayer && box.Intersects(PlayerSafetyBounds()))
            foreach (BuildingPart part in ignore.parts) if (part.bounds.Intersects(PlayerSafetyBounds())) return false;
        if (!initialized && IsPlayerReady)
        {
            Bounds checkpoint = new(startupSafePosition + startupSafeUp, Vector3.one * 4f);
            foreach (BuildingPart part in ignore.parts) if (part.bounds.Intersects(checkpoint)) return false;
        }
        return true;
    }

    private static bool BuildingModulesOverlap(BuildingWing first, BuildingWing second)
    {
        if (!first.bounds.Intersects(second.bounds)) return false;
        foreach (BuildingPart a in first.parts) foreach (BuildingPart b in second.parts)
        {
            if (a.module.kind == CastleArchitectureModule.ModuleKind.Roof || b.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
            Bounds one = a.bounds, two = b.bounds;
            one.Expand(-.45f); two.Expand(-.45f);
            if (one.Intersects(two)) return true;
        }
        return false;
    }

    private IEnumerable<bool> AttachBuildingWingSteps(BuildingWing source, BuildingPart part, Vector3 localDirection, bool stair, bool rooftop = false,
        BuildingWing preferred = null)
    {
        if (source.piece.IsMoving || flatStair == null || linkPool.Count == 0) { networkRejects[0]++; yield break; }
        BuildingWing available = preferred ?? AvailableBuildingWing();
        if (available == null) yield break;
        SetBuildingPhysics(available, true);
        CastleArchitectureModule module = part.module;
        if (module.kind == CastleArchitectureModule.ModuleKind.Stair) yield break;
        bool validPort = false;
        CastleArchitectureModule.Port selected = default;
        foreach (CastleArchitectureModule.Port port in module.ports)
            if (Vector3.Dot(port.outward, localDirection) > .99f) { validPort = true; selected = port; }
        if (!validPort) yield break;
        Vector3 direction = module.transform.TransformDirection(localDirection);
        Vector3 up = module.transform.up;
        Vector3 exit = module.transform.TransformPoint(selected.position + (rooftop ? module.roofPoint : Vector3.zero));
        if (!BuildingPortFree(source, exit)) { networkRejects[1]++; yield break; }
        int attempts = preferred != null ? 1 : buildingWings.Count - 2;
        for (int offset = 0; offset < attempts; offset++)
        {
            BuildingWing wing = preferred ?? buildingWings[(available.serial + offset) % (buildingWings.Count - 2)];
            if (wing.active) continue;
            SetBuildingPhysics(wing, true);
            for (int span = 0; span < 3; span++) for (int portIndex = 0; portIndex < wing.entries.Count; portIndex++)
            {
                yield return false;
                if (source.piece.IsMoving || wing.active || !BuildingPortFree(source, exit)) yield break;
                BuildingEntry entrance = wing.entries[portIndex];
                BuildingPart targetPart = entrance.part;
                CastleArchitectureModule target = targetPart.module;
                CastleArchitectureModule.Port targetPort = entrance.port;
                float gap = 3f + span * 4f + Hash(new Vector3Int(source.serial, wing.serial, portIndex), 516) * 3f;
                // Use the scaled assembly's doorway, not the unscaled source FBX coordinates.
                Transform root = wing.piece.transform;
                Vector3 rootEntry = entrance.rootPoint;
                Vector3 rootFacing = targetPart.rootRotation * targetPort.outward;
                Vector3 rootUp = targetPart.rootRotation * Vector3.up;
                Quaternion orientation = Quaternion.LookRotation(-direction, up) *
                    Quaternion.Inverse(Quaternion.LookRotation(rootFacing, rootUp));
                Vector3 entry = exit + direction * gap;
                Vector3 floor = entry - orientation * rootEntry;
                // Test cached geometry in memory; do not move PhysX objects for rejected placements.
                Matrix4x4 placement = Matrix4x4.TRS(floor, orientation, Vector3.one);
                wing.bounds = CastleGeometry.TransformBounds(wing.localBounds, placement);
                foreach (BuildingPart candidate in wing.parts) candidate.bounds = CastleGeometry.TransformBounds(candidate.rootBounds, placement);
                Bounds box = wing.bounds; box.Expand(.02f);
                if (!BuildingSpaceFree(box, wing, IsPlayerReady, source.group)) { networkRejects[3]++; continue; }
                wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
                wing.piece.gameObject.SetActive(true);
                Physics.SyncTransforms();
                bool passageClear = BuildingPassageClear(source, wing, exit, entry, direction, up);
                wing.piece.gameObject.SetActive(false);
                if (!passageClear) { networkRejects[4]++; continue; }
                if (!NetworkPassageFree(exit, entry, up, source, wing)) { networkRejects[5]++; continue; }
                Link bridge = linkPool.Dequeue();
                ConfigureNetworkBridge(bridge, exit, entry, up);
                PlaceBuildingWing(wing, floor, orientation);
                buildingJoins.Add(new BuildingJoin { a = source, b = wing, stair = bridge, from = module, to = target,
                    exit = selected.position + (rooftop ? module.roofPoint : Vector3.zero), entry = targetPort.position });
                RefreshReservations();
                yield return true; yield break;
            }
        }
    }

    private bool BuildingPortFree(BuildingWing wing, Vector3 point)
    {
        Vector3 local = wing.piece.transform.InverseTransformPoint(point);
        foreach (Vector3 linked in wing.internalPorts) if ((linked - local).sqrMagnitude < .12f) return false;
        foreach (BuildingJoin join in buildingJoins)
            if ((join.a == wing || join.b == wing) && ((join.from.transform.TransformPoint(join.exit) - point).sqrMagnitude < .12f ||
                (join.to.transform.TransformPoint(join.entry) - point).sqrMagnitude < .12f)) return false;
        return true;
    }

    private static bool BuildingPassageClear(BuildingWing source, BuildingWing target, Vector3 exit, Vector3 entry, Vector3 direction, Vector3 up)
    {
        Vector3 across = Vector3.Cross(up, direction);
        float distance = Vector3.Distance(exit, entry) + 1.8f;
        for (int height = 0; height < 3; height++) for (int side = -1; side <= 1; side++)
        {
            Ray ray = new(exit - direction * .9f + up * (.4f + height * .575f) + across * (side * .27f), direction);
            foreach (Collider collider in source.geometry)
                if (collider.enabled && !collider.isTrigger && collider.Raycast(ray, out _, distance)) return false;
            foreach (Collider collider in target.geometry)
                if (collider.enabled && !collider.isTrigger && collider.Raycast(ray, out _, distance)) return false;
        }
        return true;
    }

    private bool IsBuildingSupport(BuildingWing wing) => controller != null && controller.Grounded &&
        controller.Motor.GroundHit.collider != null && controller.Motor.GroundHit.collider.transform.IsChildOf(wing.piece.transform);

    private void UpdateBuildingArchitecture(float dt)
    {
        StreamBuildingVolume(dt);
        if (buildingLanding != null && controller != null && controller.Grounded) buildingLanding = null;
        bool changed = false;
        // Decide stops for the entire connected group before advancing any member.
        // This keeps door and stair seams exact even when a player approaches mid-shift.
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || !wing.piece.IsMoving) continue;
            bool groupRider = false, carriedGroup = false;
            bool approaching = false;
            foreach (BuildingWing member in buildingWings)
                if (member.active && member.group == wing.group)
                {
                    groupRider |= IsBuildingSupport(member); carriedGroup |= member.carried;
                    approaching |= member.piece.ReservedBounds.Intersects(PlayerSafetyBounds());
                }
            bool unsafeApproach = !groupRider && approaching;
            if (carriedGroup && !groupRider) unsafeApproach = true;
            if (unsafeApproach) StopBuildingGroup(wing.group);
        }
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || !wing.piece.IsMoving) continue;
            wing.piece.Tick(dt, false); UpdateBuildingZones(wing); changed = true;
        }
        if (changed) foreach (BuildingJoin join in buildingJoins)
            if (join.stair != null) ConfigureNetworkBridge(join.stair, join.from.transform.TransformPoint(join.exit),
                join.to.transform.TransformPoint(join.entry), join.from.transform.up);
        if (changed) RefreshReservations();
        ProcessBuildingWave();
        buildingLayoutClock -= dt;
        if (buildingLayoutClock > 0f || buildingWaveActive) return;
        buildingLayoutClock = .25f;
        BuildingWing support = null;
        foreach (BuildingWing wing in buildingWings) if (wing.active && IsBuildingSupport(wing)) { support = wing; break; }
        if (support != null && !support.piece.IsMoving)
        {
            foreach (BuildingPart part in support.parts)
            {
                if (!part.reachable || part.module.kind == CastleArchitectureModule.ModuleKind.Stair) continue;
                Vector3 local = part.module.transform.InverseTransformPoint(player.position);
                bool rooftop = part.module.kind == CastleArchitectureModule.ModuleKind.Room && Mathf.Abs(local.y - part.module.roofPoint.y) < .5f;
                if (!rooftop && Mathf.Abs(local.y - part.module.floorPoint.y) > .5f) continue;
                foreach (CastleArchitectureModule.Port port in part.module.ports)
                {
                    Vector3 portPoint = rooftop ? port.position + part.module.roofPoint : port.position;
                    if (Vector3.Distance(local, portPoint) > 3.2f) continue;
                    bool stair = !rooftop &&
                        Hash(Vector3Int.RoundToInt(portPoint + part.module.transform.position), buildingMotionSerial + 90) < Mathf.Min(.18f, LayoutPolicy.StairChance * .2f);
                    if (!CastleWorkPending && AvailableBuildingWing() != null)
                    { ScheduleCastleWork(ExtendBuildingRoute(support, part, port.outward, stair, rooftop)); return; }
                }
            }
        }
    }

    private IEnumerator ExtendBuildingRoute(BuildingWing source, BuildingPart part, Vector3 direction, bool stair, bool rooftop)
    {
        foreach (bool attached in AttachBuildingWingSteps(source, part, direction, stair, rooftop))
        {
            if (attached) { Physics.SyncTransforms(); RefreshReservations(); yield break; }
            yield return null;
        }
    }

    private void StopBuildingGroup(int group)
    {
        foreach (BuildingWing wing in buildingWings)
            if (wing.active && wing.group == group) { wing.piece.StopShift(); wing.carried = false; }
        RefreshReservations();
    }

    private bool TryShiftBuildingGroup(BuildingWing candidate, Vector3 preferredDirection, bool wave)
    {
        if (CastleWorkPending) return false;
        movingWingGroup.Clear();
        bool riding = false;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.group != candidate.group) continue;
            if (IsUltraJumpTarget(wing)) return false;
            if (wing.piece.IsMoving || wing == buildingLanding) return false;
            movingWingGroup.Add(wing); riding |= IsBuildingSupport(wing);
        }
        if (movingWingGroup.Count == 0 || (riding && !rideMovingBuildings)) return false;
        Vector3 pivot = candidate.piece.WorldBounds.center;
        Vector3 direction = preferredDirection.sqrMagnitude > .01f ? preferredDirection.normalized : Vector3.right;
        int sequence = ++buildingMotionSerial;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector3 axis = attempt == 0 ? direction : directions[(sequence + attempt) % 6];
            float distance = attempt < 7 ? Mathf.Clamp(nearbyShiftTravel, 2f, 12f) : attempt < 13 ? 3f : 1.2f;
            Vector3 offset = axis * distance;
            Quaternion turn = Quaternion.identity;
            bool connected = buildingJoins.Exists(join => join.a == candidate || join.b == candidate);
            if (!connected && !riding && wave && attempt < 7)
                turn = Quaternion.AngleAxis(sequence % 2 == 0 ? 90f : -90f,
                    sequence % 3 == 0 ? candidate.piece.transform.right : candidate.Up);
            wingSweeps.Clear(); bool free = true;
            for (int i = 0; i < movingWingGroup.Count && free; i++)
            {
                BuildingWing wing = movingWingGroup[i];
                Bounds sweep = wing.piece.SweepRearrange(pivot, offset, turn * wing.piece.transform.rotation);
                wingSweeps.Add(sweep);
                if (!DenseWingSpaceFree(wing, offset)) { free = false; break; }
                foreach (BuildingWing other in buildingWings)
                    if (other.active && other.group != candidate.group && other.piece.ReservedBounds.Intersects(sweep) &&
                        (turn != Quaternion.identity || !NetworkShiftClear(wing, other, offset))) { free = false; break; }
                foreach (InfinityCastleBuilder other in decor.Values)
                    if (other.ReservedBounds.Intersects(sweep)) { free = false; break; }
                foreach (BuildingJoin join in buildingJoins)
                    if (join.stair != null && join.a.group != candidate.group && join.b.group != candidate.group &&
                        join.stair.bounds.Intersects(sweep)) { free = false; break; }
                if (!riding && sweep.Intersects(PlayerSafetyBounds())) free = false;
            }
            if (free && riding)
            {
                Bounds playerSweep = CastleGeometry.Sweep(new Bounds(player.position + PlayerUp, Vector3.one * 2.2f), offset);
                foreach (BuildingWing other in buildingWings)
                    if (other.active && other.group != candidate.group && other.piece.ReservedBounds.Intersects(playerSweep)) free = false;
                foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(playerSweep)) free = false;
            }
            if (free)
                foreach (BuildingJoin join in buildingJoins)
                {
                    if (join.stair == null || (join.a.group != candidate.group && join.b.group != candidate.group)) continue;
                    Vector3 a = join.from.transform.TransformPoint(join.exit), b = join.to.transform.TransformPoint(join.entry);
                    if (join.a.group == candidate.group) a += offset;
                    if (join.b.group == candidate.group) b += offset;
                    Vector3 span = b - a;
                    Vector3 flat = Vector3.ProjectOnPlane(span, join.from.transform.up);
                    if (Vector3.Dot(flat.normalized, NetworkDoorOutward(join.from, join.exit)) < .98f ||
                        Vector3.Dot(-flat.normalized, NetworkDoorOutward(join.to, join.entry)) < .98f || flat.magnitude < 2f ||
                        Mathf.Abs(Vector3.Dot(span, join.from.transform.up)) > Vector3.ProjectOnPlane(span, join.from.transform.up).magnitude * .5f)
                    { free = false; break; }
                    Bounds bridgeSweep = CastleGeometry.Sweep(join.stair.bounds, offset, .2f);
                    if (turn != Quaternion.identity) { free = false; break; }
                    foreach (BuildingWing other in buildingWings)
                        if (other.active && other != join.a && other != join.b && other.group != candidate.group &&
                            other.piece.ReservedBounds.Intersects(bridgeSweep)) free = false;
                    foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(bridgeSweep)) free = false;
                }
            if (!free) continue;
            for (int i = 0; i < movingWingGroup.Count; i++)
            {
                BuildingWing wing = movingWingGroup[i]; wing.carried = IsBuildingSupport(wing);
                wing.piece.BeginRearrange(pivot, offset, turn * wing.piece.transform.rotation,
                    Mathf.Max(3f, rebuildMoveSeconds), wingSweeps[i]);
                wing.revision++;
            }
            if (wave)
            {
                LastRebuildMovedCount += movingWingGroup.Count;
                if (turn != Quaternion.identity) LastRebuildRotatedCount += movingWingGroup.Count;
                LastRebuildNearMovedCount += movingWingGroup.Count;
            }
            RefreshReservations(); return true;
        }
        return false;
    }

    private bool DirectBuildingShift(Vector3 direction)
    {
        foreach (BuildingWing wing in buildingWings)
            if (wing.active && IsBuildingSupport(wing) && TryShiftBuildingGroup(wing, direction, false)) return true;
        BuildingWing nearest = null; float distance = float.PositiveInfinity;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.piece.IsMoving) continue;
            float next = (wing.piece.WorldBounds.ClosestPoint(player.position) - player.position).sqrMagnitude;
            if (next < distance) { nearest = wing; distance = next; }
        }
        if (nearest != null && TryShiftBuildingGroup(nearest, direction, true)) return true;
        foreach (BuildingWing wing in buildingWings)
            if (wing.active && !wing.piece.IsMoving && TryShiftBuildingGroup(wing, direction, true)) return true;
        return false;
    }

    private void BeginBuildingWave(Vector3 direction)
    {
        buildingWave.Clear(); waveGroups.Clear(); buildingWaveDirection = direction;
        foreach (BuildingWing wing in buildingWings)
            if (wing.active && waveGroups.Add(wing.group)) buildingWave.Enqueue(wing);
        buildingWaveActive = buildingWave.Count > 0;
        LastRebuildSelectedCount += BuildingWingCount;
    }

    private void ProcessBuildingWave()
    {
        if (!buildingWaveActive) return;
        int steps = Mathf.Max(1, layoutStepsPerFrame);
        double started = Time.realtimeSinceStartupAsDouble;
        while (buildingWave.Count > 0 && steps-- > 0)
        {
            BuildingWing wing = buildingWave.Dequeue();
            if (wing.active && !TryShiftBuildingGroup(wing, buildingWaveDirection, true)) LastRebuildBlockedCount += GroupSize(wing);
            if (mobileBudget && (Time.realtimeSinceStartupAsDouble - started) * 1000d > layoutMillisecondsPerFrame) break;
        }
        if (buildingWave.Count == 0 && BuildingMovingCount == 0) buildingWaveActive = false;
    }

    private bool PrepareBuildingFallLanding()
    {
        if (!keepFloorUnderPlayer) return false;
        if (controller != null && controller.IsUltraJumping) return false;
        Vector3 up = PlayerUp;
        float depth = Mathf.Clamp(catchLayersBelow, 3, 5) * 20f;
        if (HasBuildingFloorBelow(depth)) return false;
        if (controller != null && controller.Grounded) return false;
        BuildingWing wing = AvailableBuildingWing(true);
        if (wing == null) return false;
        float fallingSpeed = controller != null ? Mathf.Max(0f, -controller.VerticalSpeed) : 0f;
        float seconds = Mathf.Clamp((Mathf.Sqrt(fallingSpeed * fallingSpeed + 44f * depth) - fallingSpeed) / 22f, .25f, 1.6f);
        Vector3 target = player.position - up * (depth - 3f) + Vector3.ProjectOnPlane(playerVelocity, up) * seconds;
        Quaternion orientation = CastleGeometry.Orientation(up);
        Vector3 roof = wing.piece.transform.InverseTransformPoint(
            wing.parts[0].module.transform.TransformPoint(wing.parts[0].module.roofPoint));
        Vector3 floor = target - orientation * roof;
        wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
        CacheBuildingBounds(wing);
        if (!BuildingSpaceFree(wing.piece.WorldBounds, wing, false)) return false;
        if (buildingLanding != null && !IsBuildingSupport(buildingLanding)) buildingLanding = null;
        PlaceBuildingWing(wing, floor, orientation);
        buildingLanding = wing; landingGeneration++;
        RefreshReservations(); Physics.SyncTransforms(); return true;
    }

    private bool HasBuildingFloorBelow(float depth)
    {
        Vector3 up = PlayerUp;
        int count = Physics.RaycastNonAlloc(player.position + up * .5f, -up, buildingFloorHits, depth, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (Vector3.Dot(buildingFloorHits[i].normal, up) > .65f && IsCastleSurface(buildingFloorHits[i].collider)) return true;
        return false;
    }

    private void ReserveBuildingArchitecture()
    {
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active) continue;
            reservations.Add(wing.piece.ReservedBounds);
            foreach (BuildingPart part in wing.parts)
            {
                Vector3 size = new(part.module.floorSize.x, 2.5f, part.module.floorSize.y);
                Bounds headroom = CastleGeometry.TransformBounds(new Bounds(part.module.floorPoint + Vector3.up * 1.25f, size), part.module.transform.localToWorldMatrix);
                reservations.Add(headroom);
                if (part.roofZone != null)
                    reservations.Add(CastleGeometry.TransformBounds(new Bounds(part.module.roofPoint + Vector3.up * 1.25f,
                        new Vector3(part.module.roofSize.x, 2.5f, part.module.roofSize.y)), part.module.transform.localToWorldMatrix));
            }
        }
        foreach (BuildingJoin join in buildingJoins) if (join.stair != null) reservations.Add(join.stair.bounds);
    }

    public bool TryGetBuildingFloor(int wingIndex, int partIndex, bool rooftop, out Vector3 point, out Vector3 up)
    {
        point = up = default;
        if (wingIndex < 0 || wingIndex >= buildingWings.Count) return false;
        BuildingWing wing = buildingWings[wingIndex];
        if (!wing.active || partIndex < 0 || partIndex >= wing.parts.Count) return false;
        CastleArchitectureModule module = wing.parts[partIndex].module;
        point = module.transform.TransformPoint(rooftop ? module.roofPoint : module.floorPoint);
        up = module.transform.up; return true;
    }

    public bool TryGetBuildingSeam(int wingIndex, int from, int to, out Vector3 exit, out Vector3 entry)
    {
        exit = entry = default;
        if (wingIndex < 0 || wingIndex >= buildingWings.Count) return false;
        BuildingWing wing = buildingWings[wingIndex];
        foreach (BuildingSeam seam in wing.seams)
            if (seam.a == from && seam.b == to)
            {
                exit = wing.parts[from].module.transform.TransformPoint(seam.exit);
                entry = wing.parts[to].module.transform.TransformPoint(seam.entry);
                return true;
            }
        return false;
    }

    public bool TryGetBuildingJoin(int index, out Vector3 exit, out Vector3 entry, out Vector3 up, out bool stair)
    {
        exit = entry = up = default; stair = false;
        if (index < 0 || index >= buildingJoins.Count) return false;
        BuildingJoin join = buildingJoins[index];
        exit = join.from.transform.TransformPoint(join.exit);
        entry = join.to.transform.TransformPoint(join.entry);
        up = join.from.transform.up; stair = join.stair != null;
        return true;
    }

    public bool ValidateBuildingArchitecture(out string error)
    {
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active) continue;
            if (wing.piece.GetComponentsInChildren<CastleWalkableSurface>(true).Length > 0)
            { error = "An added walkway remains on " + wing.piece.name; return false; }
            foreach (BuildingPart part in wing.parts)
                if (part.module.requiresPassageRepair) { error = "Blocked passage: " + part.module.name; return false; }
            for (int i = 0; i < wing.parts.Count; i++) for (int j = i + 1; j < wing.parts.Count; j++)
            {
                var a = wing.parts[i].module; var b = wing.parts[j].module;
                if (a.kind == CastleArchitectureModule.ModuleKind.Roof || b.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                // Test authored assembly seams before its non-uniform dimension variation.
                Bounds one = CastleGeometry.TransformBounds(a.localBounds, wing.content.worldToLocalMatrix * a.transform.localToWorldMatrix);
                Bounds two = CastleGeometry.TransformBounds(b.localBounds, wing.content.worldToLocalMatrix * b.transform.localToWorldMatrix);
                one.Expand(-.45f); two.Expand(-.45f);
                if (one.Intersects(two)) { error = wing.piece.name + " internal overlap: " + i + " " + a.name + " / " + j + " " + b.name; return false; }
            }
        }
        foreach (BuildingJoin join in buildingJoins)
            if (Vector3.Dot(join.from.transform.up, join.to.transform.up) < .999f)
            { error = "Connected buildings have different gravity."; return false; }
        error = "Playable modules=" + BuildingRoomCount + ", wings=" + BuildingWingCount +
            ", direct joints=" + BuildingDirectJoinCount + ", stair bridges=" + BuildingStairJoinCount;
        return true;
    }
}
