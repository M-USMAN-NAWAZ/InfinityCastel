using System;
using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    [Header("Building Traversal")]
    public bool useBuildingTraversal = true;
    public GameObject roomPrefab, corridorPrefab, junctionPrefab, doubleStairPrefab, roofAccessStairPrefab;
    [Range(12, 28)] public int architectureWingPoolSize = 20;

    private sealed class BuildingPart
    {
        public CastleArchitectureModule module;
        public InfiniteCastleGrid floorZone, roofZone, cornerZone, returnZone;
        public bool reachable = true;
    }
    private sealed class BuildingWing
    {
        public InfinityCastleBuilder piece;
        public readonly List<BuildingPart> parts = new();
        public readonly List<BuildingSeam> seams = new();
        public Bounds localBounds;
        public Collider[] geometry;
        public int serial, group, revision;
        public bool active, carried;
        public Vector3 Floor => parts[0].module.WorldFloor;
        public Vector3 Up => piece.transform.up;
    }
    private sealed class BuildingSeam
    {
        public int a, b;
        public Vector3 exit, entry;
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
    private float buildingLayoutClock;
    private int buildingMotionSerial, buildingGroupSerial;
    private int buildingLayoutRemaining;
    private bool buildingWaveActive;
    private Vector3 buildingWaveDirection;
    private Vector3 buildingStreamAnchor;
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

    private void InitializeBuildingArchitecture()
    {
        CastleArchitectureModule room = roomPrefab.GetComponent<CastleArchitectureModule>();
        CastleArchitectureModule corridor = corridorPrefab.GetComponent<CastleArchitectureModule>();
        roomWidth = room.floorSize.x + .08f;
        roomDepth = room.floorSize.y + .08f;
        corridorDepth = corridor.floorSize.y;
        roofFloorHeight = room.localBounds.max.y + .22f;
        architectureWingPoolSize = Mathf.Clamp(architectureWingPoolSize, 12, mobileBudget ? 20 : 28);
        for (int i = 0; i < architectureWingPoolSize; i++) buildingWings.Add(CreateBuildingWing(i));
        foreach (BuildingWing wing in buildingWings) PoolCapacity += wing.parts.Count;
        BuildingWing first = buildingWings[0];
        PlaceBuildingWing(first, origin, Quaternion.identity);
        player.position = first.Floor + Vector3.up * .08f;
        gravity.RestoreDefaultGravity(); gravity.retainZoneGravityInAir = true;
        Physics.SyncTransforms();

        // Most seams are direct door-to-door joins. Only selected gaps get an authored stair bridge.
        AttachInitialBuildingWing(first, false, false);
        AttachInitialBuildingWing(first, true, false);
        AttachInitialBuildingWing(first, false, true);
        FillBuildingNeighborhood(true);
        buildingStreamAnchor = player.position;
        RefreshReservations();
    }

    private bool AttachInitialBuildingWing(BuildingWing first, bool stair, bool jump)
    {
        for (int i = first.parts.Count - 1; i >= 0; i--)
        {
            BuildingPart part = first.parts[i];
            if (!part.reachable || part.module.kind == CastleArchitectureModule.ModuleKind.Stair ||
                Vector3.Dot(part.module.transform.up, first.Up) < .99f) continue;
            foreach (var port in part.module.ports)
                if (AttachBuildingWing(first, part, port.outward, stair, jump)) return true;
        }
        return false;
    }

    private BuildingWing CreateBuildingWing(int serial)
    {
        GameObject root = new("Playable Building Wing " + serial.ToString("00"));
        root.transform.SetParent(buildingsParent, false);
        var wing = new BuildingWing { serial = serial, group = ++buildingGroupSerial };
        int Part(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            GameObject art = Instantiate(prefab, root.transform);
            art.transform.localPosition = position;
            art.transform.localRotation = rotation;
            var module = art.GetComponent<CastleArchitectureModule>();
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
            Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(entry.outward, -normal, Vector3.up), Vector3.up);
            Vector3 position = from.transform.localPosition + fromRotation * exit.position + normal * .04f - turn * entry.position;
            if (roof) position -= Vector3.up * target.roofPoint.y;
            int added = Part(prefab, position, turn);
            wing.parts[added].reachable = !roof;
            wing.seams.Add(new BuildingSeam { a = source, b = added, exit = exit.position,
                entry = entry.position + (roof ? Vector3.up * target.roofPoint.y : Vector3.zero) });
            return added;
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
            GameObject marker = new("Native Room Rooftop"); marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = room.transform.localPosition + room.transform.localRotation * room.roofPoint;
            marker.transform.localRotation = room.transform.localRotation;
            CastleArchitectureModule module = marker.AddComponent<CastleArchitectureModule>();
            module.kind = CastleArchitectureModule.ModuleKind.Roof; module.floorSize = room.roofSize;
            module.localBounds = new Bounds(Vector3.zero, new Vector3(room.roofSize.x, .1f, room.roofSize.y));
            module.ports = room.ports;
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
        if (serial == 0)
        {
            Join(0, roomPrefab, DirectionPort(wing.parts[0].module, Vector3.left), matchFacing: true);
            Join(0, roofAccessStairPrefab, DirectionPort(wing.parts[0].module, Vector3.forward), stair: true);
            Join(2, roomPrefab, UpperPort(wing.parts[2].module));
            Join(3, roomPrefab, DirectionPort(wing.parts[3].module, Vector3.forward));
            Join(3, roomPrefab, DirectionPort(wing.parts[3].module, Vector3.right), roof: true);
            Rooftop(5);
            GravityRoom(5);
        }
        else if (serial % 3 != 0)
        {
            GameObject hall = serial % 3 == 1 ? corridorPrefab : junctionPrefab;
            Join(0, hall, DirectionPort(wing.parts[0].module, Vector3.forward));
            CastleArchitectureModule corridor = wing.parts[1].module;
            CastleArchitectureModule.Port entry = EntryPort(corridor, false);
            var exits = new List<CastleArchitectureModule.Port>();
            foreach (var port in corridor.ports) if ((port.position - entry.position).sqrMagnitude > .1f) exits.Add(port);
            Join(1, roomPrefab, exits[0]);
            if (serial % 3 == 2 && exits.Count > 1) Join(1, roomPrefab, exits[1]);
            else Join(2, roomPrefab, DirectionPort(wing.parts[2].module, Vector3.forward));
            Join(3, roofAccessStairPrefab, DirectionPort(wing.parts[3].module, Vector3.right), stair: true);
            int raised = Join(4, roomPrefab, UpperPort(wing.parts[4].module));
            int roofRoom = JoinRoof(raised);
            Rooftop(roofRoom);
            GravityRoom(roofRoom);
        }
        else
        {
            Join(0, roomPrefab, DirectionPort(wing.parts[0].module, Vector3.forward));
            Join(1, roomPrefab, DirectionPort(wing.parts[1].module, Vector3.forward));
            Join(1, roomPrefab, DirectionPort(wing.parts[1].module, Vector3.right));
            Join(0, roofAccessStairPrefab, DirectionPort(wing.parts[0].module, Vector3.left), stair: true);
            int raised = Join(4, roomPrefab, UpperPort(wing.parts[4].module));
            int roofRoom = JoinRoof(raised);
            Rooftop(roofRoom);
            GravityRoom(roofRoom);
        }
        // Keep the double staircase as an optional upper landing; its lower edge is not an entrance.
        CastleArchitectureModule landing = doubleStairPrefab.GetComponent<CastleArchitectureModule>();
        var landingEntry = UpperPort(landing);
        bool landingAdded = false;
        for (int source = 0; serial >= 6 && source < wing.parts.Count && !landingAdded; source++)
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
        wing.piece = root.AddComponent<InfinityCastleBuilder>();
        wing.piece.InitializePooledPiece(this);
        wing.localBounds = CastleGeometry.RendererBoundsInRoot(root.transform);
        wing.geometry = root.GetComponentsInChildren<Collider>(true);
        UpdateBuildingZones(wing);
        root.SetActive(false);
        return wing;
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
        wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
        wing.active = true; wing.carried = false; wing.group = ++buildingGroupSerial;
        wing.piece.gameObject.SetActive(true);
        UpdateBuildingZones(wing);
    }

    private void UpdateBuildingZones(BuildingWing wing)
    {
        foreach (BuildingPart part in wing.parts)
        {
            part.floorZone.gravityDirection = -part.module.transform.up; part.floorZone.enabled = enableGravityZones;
            if (part.cornerZone != null)
            { part.cornerZone.gravityDirection = -part.module.transform.up; part.cornerZone.enabled = enableGravityZones; }
            if (part.returnZone != null)
            { part.returnZone.gravityDirection = -part.returnZone.transform.up; part.returnZone.enabled = enableGravityZones; }
            if (part.roofZone == null) continue;
            part.roofZone.gravityDirection = -part.module.transform.up; part.roofZone.enabled = enableGravityZones;
        }
    }

    private BuildingWing AvailableBuildingWing(bool emergency = false)
    {
        // Keep two growth slots as well as two emergency landing slots out of distant fill.
        int reserve = emergency ? 0 : 4;
        for (int i = 0; i < buildingWings.Count - reserve; i++) if (!buildingWings[i].active) return buildingWings[i];
        if (emergency)
            for (int i = buildingWings.Count - 2; i < buildingWings.Count; i++) if (!buildingWings[i].active) return buildingWings[i];
        BuildingWing furthest = null;
        float distance = emergency ? 45f * 45f : 70f * 70f;
        if (mainCamera != null) GeometryUtility.CalculateFrustumPlanes(mainCamera, cameraPlanes);
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.piece.IsMoving || wing == buildingLanding || IsBuildingSupport(wing)) continue;
            bool groupSafe = true;
            foreach (BuildingWing member in buildingWings)
            {
                if (!member.active || member.group != wing.group) continue;
                if (member.piece.IsMoving || member == buildingLanding || IsBuildingSupport(member) ||
                    (member.Floor - player.position).sqrMagnitude < distance) { groupSafe = false; break; }
                if (mainCamera != null && GeometryUtility.TestPlanesAABB(cameraPlanes, member.piece.WorldBounds) &&
                    (member.Floor - player.position).sqrMagnitude < 160f * 160f) { groupSafe = false; break; }
            }
            if (!groupSafe) continue;
            float candidate = (wing.Floor - player.position).sqrMagnitude;
            if (candidate <= distance) continue;
            if (mainCamera != null && candidate < 160f * 160f && GeometryUtility.TestPlanesAABB(cameraPlanes, wing.piece.WorldBounds)) continue;
            distance = candidate; furthest = wing;
        }
        if (furthest != null)
        {
            int group = furthest.group;
            foreach (BuildingWing member in buildingWings)
                if (member.active && member.group == group) RetireBuildingWing(member);
        }
        return furthest;
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
        foreach (BuildingWing other in buildingWings)
            if (other.active && other != ignore && other.piece.ReservedBounds.Intersects(box))
            {
                if (other.group != joiningGroup) return false;
                // Door frames share a narrow architectural seam. Reject any deeper
                // intersection, while allowing empty corners of an L/T wing's AABB.
                if (BuildingModulesOverlap(other, ignore)) return false;
            }
        foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(box)) return false;
        foreach (BuildingJoin join in buildingJoins)
            if (join.stair != null && join.stair.bounds.Intersects(box)) return false;
        return !includePlayer || !box.Intersects(PlayerSafetyBounds());
    }

    private static bool BuildingModulesOverlap(BuildingWing first, BuildingWing second)
    {
        foreach (BuildingPart a in first.parts) foreach (BuildingPart b in second.parts)
        {
            if (a.module.kind == CastleArchitectureModule.ModuleKind.Roof || b.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
            Bounds one = a.module.WorldBounds, two = b.module.WorldBounds;
            one.Expand(-.45f); two.Expand(-.45f);
            if (one.Intersects(two)) return true;
        }
        return false;
    }

    private bool AttachBuildingWing(BuildingWing source, BuildingPart part, Vector3 localDirection, bool stair, bool jump, bool rooftop = false)
    {
        if (source.piece.IsMoving || (!jump && GroupSize(source) >= 3)) return false;
        if (stair && (flatStair == null || linkPool.Count == 0)) return false;
        BuildingWing available = AvailableBuildingWing();
        if (available == null)
            for (int i = buildingWings.Count - 4; i < buildingWings.Count - 2; i++)
                if (!buildingWings[i].active) { available = buildingWings[i]; break; }
        if (available == null) return false;
        CastleArchitectureModule module = part.module;
        if (module.kind == CastleArchitectureModule.ModuleKind.Stair) return false;
        bool validPort = false;
        CastleArchitectureModule.Port selected = default;
        foreach (CastleArchitectureModule.Port port in module.ports)
            if (Vector3.Dot(port.outward, localDirection) > .99f) { validPort = true; selected = port; }
        if (!validPort) return false;
        Vector3 direction = module.transform.TransformDirection(localDirection);
        Vector3 up = module.transform.up;
        Vector3 exit = module.transform.TransformPoint(selected.position + (rooftop ? module.roofPoint : Vector3.zero));
        if (!BuildingPortFree(source, exit)) return false;
        float gap = stair ? flatStair.run : jump ? jumpGap : .08f;
        int candidates = 0;
        for (int offset = 0; offset < buildingWings.Count - 2 && candidates < 6; offset++)
        {
            BuildingWing wing = buildingWings[(available.serial + offset) % (buildingWings.Count - 2)];
            if (wing.active) continue;
            candidates++;
            CastleArchitectureModule target = wing.parts[0].module;
            for (int portIndex = -1; portIndex < target.ports.Length; portIndex++)
            {
                CastleArchitectureModule.Port targetPort = portIndex < 0 ? EntryPort(target, false) : target.ports[portIndex];
                if (portIndex >= 0 && (targetPort.position - EntryPort(target, false).position).sqrMagnitude < .01f) continue;
                Quaternion orientation = Quaternion.LookRotation(direction, up) *
                    Quaternion.AngleAxis(Vector3.SignedAngle(targetPort.outward, Vector3.back, Vector3.up), Vector3.up);
                Vector3 entry = exit + direction * gap;
                Vector3 floor = entry - orientation * targetPort.position;
                wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
                if (!BuildingPortFree(wing, entry)) continue;
                Bounds box = wing.piece.WorldBounds; box.Expand(.02f);
                if (!BuildingSpaceFree(box, wing, false, jump ? 0 : source.group)) continue;
                wing.piece.gameObject.SetActive(true);
                Physics.SyncTransforms();
                bool passageClear = BuildingPassageClear(source, wing, exit, entry, direction, up);
                wing.piece.gameObject.SetActive(false);
                if (!passageClear) continue;
                Link bridge = null;
                if (stair)
                {
                    Bounds passage = new((exit + entry) * .5f + up, Vector3.zero);
                    passage.Encapsulate(exit + up * 2f); passage.Encapsulate(entry + up * 2f);
                    passage.Expand(.5f);
                    bool clear = true;
                    foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(passage)) { clear = false; break; }
                    foreach (BuildingWing other in buildingWings)
                        if (other.active && other != source && other.piece.ReservedBounds.Intersects(passage)) { clear = false; break; }
                    if (!clear) continue;
                    bridge = linkPool.Dequeue();
                    ConfigureLink(bridge, exit, entry, up, orientation, endpoints: true);
                    bridge.root.transform.SetParent(source.piece.transform, true);
                }
                PlaceBuildingWing(wing, floor, orientation);
                if (!jump)
                {
                    wing.group = source.group;
                    buildingJoins.Add(new BuildingJoin { a = source, b = wing, stair = bridge, from = module, to = target,
                        exit = selected.position + (rooftop ? module.roofPoint : Vector3.zero), entry = targetPort.position });
                }
                RefreshReservations();
                return true;
            }
        }
        return false;
    }

    private bool BuildingPortFree(BuildingWing wing, Vector3 point)
    {
        foreach (BuildingSeam seam in wing.seams)
            if ((wing.parts[seam.a].module.transform.TransformPoint(seam.exit) - point).sqrMagnitude < .12f ||
                (wing.parts[seam.b].module.transform.TransformPoint(seam.entry) - point).sqrMagnitude < .12f) return false;
        foreach (BuildingJoin join in buildingJoins)
            if ((join.from.transform.TransformPoint(join.exit) - point).sqrMagnitude < .12f ||
                (join.to.transform.TransformPoint(join.entry) - point).sqrMagnitude < .12f) return false;
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

    private void FillBuildingNeighborhood(bool initial)
    {
        int attempts = initial ? 90 : Mathf.Max(1, layoutStepsPerFrame);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            BuildingWing wing = AvailableBuildingWing();
            if (wing == null) return;
            int serial = ++buildingMotionSerial;
            Vector3Int key = new(serial, serial / 6, serial % 5);
            int side = serial % 6;
            Vector3 up = serial % 5 == 0 ? directions[(serial / 5) % 6] : PlayerUp;
            Quaternion orientation = CastleGeometry.Orientation(up, Mathf.Floor(Hash(key, 82) * 4f) * 90f);
            int ring = 1 + (serial / 6) % 3;
            Vector3 offset = directions[side] * (side < 4 && (side == 2 || side == 3) ? ring * 13f : ring * 34f);
            offset += Vector3.Scale(new Vector3(Hash(key, 83) - .5f, Hash(key, 84) - .5f, Hash(key, 85) - .5f), new Vector3(14f, 3f, 14f));
            Vector3 centre = player.position + offset;
            wing.piece.Place(centre, orientation);
            Bounds box = wing.piece.WorldBounds; box.Expand(.3f);
            if (!BuildingSpaceFree(box, wing)) continue;
            Vector3 floor = wing.piece.transform.position;
            PlaceBuildingWing(wing, floor, orientation);
        }
    }

    private bool IsBuildingSupport(BuildingWing wing) => controller != null && controller.Grounded &&
        controller.Motor.GroundHit.collider != null && controller.Motor.GroundHit.collider.transform.IsChildOf(wing.piece.transform);

    private void UpdateBuildingArchitecture(float dt)
    {
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
        foreach (BuildingJoin join in buildingJoins)
            if (join.stair != null) join.stair.bounds = ConnectionVisualBounds(join.stair);
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
                if (part.module.kind == CastleArchitectureModule.ModuleKind.Stair) continue;
                Vector3 local = part.module.transform.InverseTransformPoint(player.position);
                bool rooftop = part.module.kind == CastleArchitectureModule.ModuleKind.Room && Mathf.Abs(local.y - part.module.roofPoint.y) < .5f;
                if (!rooftop && Mathf.Abs(local.y - part.module.floorPoint.y) > .5f) continue;
                foreach (CastleArchitectureModule.Port port in part.module.ports)
                {
                    Vector3 portPoint = rooftop ? port.position + part.module.roofPoint : port.position;
                    if (Vector3.Distance(local, portPoint) > 3.2f) continue;
                    bool stair = GroupSize(support) < 3 && !rooftop &&
                        Hash(Vector3Int.RoundToInt(portPoint + part.module.transform.position), buildingMotionSerial + 90) < Mathf.Min(.18f, LayoutPolicy.StairChance * .2f);
                    if (AttachBuildingWing(support, part, port.outward, stair, GroupSize(support) >= 3, rooftop))
                    { RefreshReservations(); Physics.SyncTransforms(); return; }
                }
            }
        }
        if ((buildingStreamAnchor - player.position).sqrMagnitude > 12f * 12f)
        { buildingStreamAnchor = player.position; buildingLayoutRemaining = 24; }
        if (buildingLayoutRemaining > 0)
        { FillBuildingNeighborhood(false); buildingLayoutRemaining -= Mathf.Max(1, layoutStepsPerFrame); RefreshReservations(); }
    }

    private void StopBuildingGroup(int group)
    {
        foreach (BuildingWing wing in buildingWings)
            if (wing.active && wing.group == group) { wing.piece.StopShift(); wing.carried = false; }
        RefreshReservations();
    }

    private bool TryShiftBuildingGroup(BuildingWing candidate, Vector3 preferredDirection, bool wave)
    {
        movingWingGroup.Clear();
        bool riding = false;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.group != candidate.group) continue;
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
            if (!riding && wave && attempt < 7)
                turn = Quaternion.AngleAxis(sequence % 2 == 0 ? 90f : -90f,
                    sequence % 3 == 0 ? candidate.piece.transform.right : candidate.Up);
            wingSweeps.Clear(); bool free = true;
            for (int i = 0; i < movingWingGroup.Count && free; i++)
            {
                BuildingWing wing = movingWingGroup[i];
                Bounds sweep = wing.piece.SweepRearrange(pivot, offset, turn * wing.piece.transform.rotation);
                wingSweeps.Add(sweep);
                foreach (BuildingWing other in buildingWings)
                    if (other.active && other.group != candidate.group && other.piece.ReservedBounds.Intersects(sweep)) { free = false; break; }
                foreach (InfinityCastleBuilder other in decor.Values)
                    if (other.ReservedBounds.Intersects(sweep)) { free = false; break; }
                foreach (BuildingJoin join in buildingJoins)
                    if (join.stair != null && join.a.group != candidate.group && join.stair.bounds.Intersects(sweep)) { free = false; break; }
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
                    if (join.stair == null || join.a.group != candidate.group) continue;
                    Bounds bridgeSweep = CastleGeometry.Sweep(join.stair.bounds, offset, .2f);
                    if (turn != Quaternion.identity) { free = false; break; }
                    foreach (BuildingWing other in buildingWings)
                        if (other.active && other.group != candidate.group && other.piece.ReservedBounds.Intersects(bridgeSweep)) free = false;
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
        Vector3 up = PlayerUp;
        float depth = Mathf.Clamp(catchLayersBelow, 3, 5) * 10f;
        int count = Physics.RaycastNonAlloc(player.position + up * .5f, -up, buildingFloorHits, depth, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (Vector3.Dot(buildingFloorHits[i].normal, up) > .65f && IsCastleSurface(buildingFloorHits[i].collider)) return false;
        if (controller != null && controller.Grounded) return false;
        BuildingWing wing = AvailableBuildingWing(true);
        if (wing == null) return false;
        float fallingSpeed = controller != null ? Mathf.Max(0f, -controller.VerticalSpeed) : 0f;
        float seconds = Mathf.Clamp((Mathf.Sqrt(fallingSpeed * fallingSpeed + 44f * depth) - fallingSpeed) / 22f, .25f, 1.6f);
        Vector3 target = player.position - up * (depth - 3f) + Vector3.ProjectOnPlane(playerVelocity, up) * seconds;
        Quaternion orientation = CastleGeometry.Orientation(up);
        Vector3 roof = wing.parts[0].module.roofPoint;
        Vector3 floor = target - orientation * roof;
        wing.piece.Place(floor + orientation * wing.localBounds.center, orientation);
        if (!BuildingSpaceFree(wing.piece.WorldBounds, wing, false)) return false;
        if (buildingLanding != null && !IsBuildingSupport(buildingLanding)) buildingLanding = null;
        PlaceBuildingWing(wing, floor, orientation);
        buildingLanding = wing; landingGeneration++;
        RefreshReservations(); Physics.SyncTransforms(); return true;
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
                Bounds one = a.WorldBounds, two = b.WorldBounds; one.Expand(-.45f); two.Expand(-.45f);
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
