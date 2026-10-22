using UnityEngine;

public partial class DynamicInfinityCastle
{
    private CastleArchitectureModule DistrictModule(DistrictRoom room) => districtTemplates[room.template].module;
    private Vector3 DistrictDoor(DistrictRoom room, int port) => room.matrix.MultiplyPoint3x4(DistrictModule(room).ports[port].position);
    private Vector3 DistrictOutward(DistrictRoom room, int port) => room.rotation * DistrictModule(room).ports[port].outward;

    // Grow the actual doorway graph, not another rotated/scaled copy of a four-room grid.
    private void PopulateDistrictBlock(DistrictBlock block, Vector3Int key)
    {
        foreach (DistrictRoom room in block.rooms)
        { room.active = room.assemblySeen = false; room.openDoorMask = 0; room.construction = Vector4.zero; }
        foreach (DistrictCrossing crossing in block.crossings)
        { crossing.active = crossing.assemblySeen = crossing.doorPair = crossing.switchback = false; crossing.construction = Vector4.zero; }
        int sample = 850, count = 0, used = 0;
        float Next() => Hash(key, ++sample);
        int Pick(int choices) => Mathf.Min(choices - 1, Mathf.FloorToInt(Next() * choices));
        int stories = 3 + Pick(2);
        Vector3 up = directions[Pick(6)];
        Quaternion basis = CastleGeometry.Orientation(up) * Quaternion.Euler(0f, Pick(4) * 90f, 0f);
        Vector3 centre = (Vector3)key * DistrictSpan;
        float floorStep = Mathf.Lerp(7.7f, 8.6f, Next());
        block.bounds = new Bounds(centre, Vector3.zero);

        bool Add(int type, int floor, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
        {
            if (count == block.rooms.Length) return false;
            var spec = districtTemplates[type].module;
            Bounds local = CastleGeometry.TransformBounds(spec.localBounds, Matrix4x4.TRS(localPosition, localRotation, scale));
            if (local.min.x < -16.3f || local.max.x > 16.3f || local.min.z < -16.3f || local.max.z > 16.3f ||
                local.min.y < -22.5f || local.max.y > 22.5f) return false;
            Matrix4x4 matrix = Matrix4x4.TRS(centre + basis * localPosition, basis * localRotation, scale);
            Bounds bounds = CastleGeometry.TransformBounds(spec.localBounds, matrix), clearance = bounds;
            clearance.Expand(-.45f);
            for (int i = 0; i < count; i++)
            {
                Bounds other = block.rooms[i].bounds; other.Expand(-.45f);
                if (clearance.Intersects(other)) return false;
            }
            DistrictRoom room = block.rooms[count++];
            room.template = type; room.floor = floor; room.position = matrix.GetPosition();
            room.rotation = basis * localRotation; room.scale = scale; room.matrix = matrix;
            room.bounds = bounds; room.active = true; block.bounds.Encapsulate(bounds);
            return true;
        }
        bool Clear(Vector3 from, Vector3 to, DistrictRoom a, DistrictRoom b)
        {
            Vector3 delta = to - from;
            Bounds passage = CastleGeometry.TransformBounds(new Bounds(Vector3.up * 1.05f,
                new Vector3(1.2f, 2.1f, Mathf.Max(.05f, delta.magnitude - .3f))),
                Matrix4x4.TRS((from + to) * .5f, Quaternion.LookRotation(delta.normalized, up), Vector3.one));
            for (int i = 0; i < count; i++)
            {
                DistrictRoom room = block.rooms[i];
                if (room == a || room == b) continue;
                Bounds box = room.bounds; box.Expand(-.2f);
                if (box.Intersects(passage)) return false;
            }
            return true;
        }
        void Segment(DistrictRoom a, DistrictRoom b, Vector3 from, Vector3 to, bool doors = false)
        {
            DistrictCrossing crossing = block.crossings[used++];
            crossing.a = a; crossing.b = b; crossing.start = from; crossing.end = to;
            crossing.active = true; crossing.doorPair = doors;
            FitDistrictCrossing(crossing, up); block.bounds.Encapsulate(crossing.bounds);
        }

        for (int floor = 0; floor < stories; floor++)
        {
            int first = count, recipe = (Pick(4) + LayoutPolicy.Structure) % 4;
            int target = (stories == 3 ? 4 : 3) + Pick(2);
            Add(1 + Pick(2), floor, new Vector3((Next() - .5f) * 4f, -15f + floor * floorStep, (Next() - .5f) * 4f),
                Quaternion.Euler(0, Pick(4) * 90f, 0), Vector3.one);
            for (int attempt = 0; count - first < target && count < block.rooms.Length && attempt < 180; attempt++)
            {
                int existing = count - first;
                int source = first + (recipe == 0 && Next() < .65f ? 0 : recipe == 1 && Next() < .65f ? existing - 1 : Pick(existing));
                DistrictRoom parent = block.rooms[source];
                var parentSpec = DistrictModule(parent);
                int exit = Pick(parentSpec.ports.Length);
                if ((parent.openDoorMask & (1 << exit)) != 0) continue;
                // Every floor has a real hallway; other branch nodes mix rooms and junctions.
                int type = existing < 3 || Next() < .65f ? 0 : 1 + Pick(2);
                var spec = districtTemplates[type].module;
                int entry = Pick(spec.ports.Length);
                Vector3 outward = DistrictOutward(parent, exit);
                Quaternion rotation = Quaternion.AngleAxis(Vector3.SignedAngle(spec.ports[entry].outward,
                    -(Quaternion.Inverse(basis) * outward), Vector3.up), Vector3.up);
                Vector3 scale = new(Mathf.Lerp(.95f, 1.24f, Next()), Mathf.Lerp(.98f, 1.12f, Next()), Mathf.Lerp(.95f, 1.24f, Next()));
                Vector3 from = DistrictDoor(parent, exit), to = from + outward * Mathf.Lerp(.6f, 2.4f, Next());
                Vector3 local = Quaternion.Inverse(basis) * (to - centre) - rotation * Vector3.Scale(spec.ports[entry].position, scale);
                if (!Clear(from, to, parent, null) || !Add(type, floor, local, rotation, scale)) continue;
                DistrictRoom child = block.rooms[count - 1];
                parent.openDoorMask |= 1 << exit; child.openDoorMask |= 1 << entry;
                Segment(parent, child, from - outward * .2f, to + outward * .2f, true);
            }
        }

        // Add genuine alternative routes where two free, facing doors have a clear passage.
        for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++)
        {
            DistrictRoom a = block.rooms[i], b = block.rooms[j];
            if (a.floor != b.floor || Next() > .55f) continue;
            for (int pa = 0; pa < DistrictModule(a).ports.Length; pa++) for (int pb = 0; pb < DistrictModule(b).ports.Length; pb++)
            {
                if ((a.openDoorMask & (1 << pa)) != 0 || (b.openDoorMask & (1 << pb)) != 0) continue;
                Vector3 from = DistrictDoor(a, pa), to = DistrictDoor(b, pb), delta = to - from;
                Vector3 normal = DistrictOutward(a, pa);
                if (delta.magnitude < .4f || delta.magnitude > 8f || Vector3.Dot(delta.normalized, normal) < .98f ||
                    Vector3.Dot(normal, DistrictOutward(b, pb)) > -.99f || Mathf.Abs(Vector3.Dot(delta, up)) > .08f || !Clear(from, to, a, b)) continue;
                a.openDoorMask |= 1 << pa; b.openDoorMask |= 1 << pb;
                Segment(a, b, from - normal * .2f, to + normal * .2f, true);
            }
        }

        // One intact stair_2 in a different eligible position per storey, not stair cages around every room.
        for (int floor = 0; floor < stories - 1; floor++)
        {
            bool joined = false;
            int offset = Pick(count);
            for (int ai = 0; ai < count && !joined; ai++) for (int bi = 0; bi < count && !joined; bi++)
            {
                DistrictRoom a = block.rooms[(ai + offset) % count], b = block.rooms[(bi + offset) % count];
                if (a.floor != floor || b.floor != floor + 1) continue;
                for (int pa = 0; pa < DistrictModule(a).ports.Length && !joined; pa++)
                    for (int pb = 0; pb < DistrictModule(b).ports.Length && !joined; pb++)
                {
                    if ((a.openDoorMask & (1 << pa)) != 0 || (b.openDoorMask & (1 << pb)) != 0) continue;
                    Vector3 outward = DistrictOutward(a, pa);
                    if (Vector3.Dot(outward, DistrictOutward(b, pb)) < .99f) continue;
                    Vector3 from = DistrictDoor(a, pa) - outward * .2f, to = DistrictDoor(b, pb) - outward * .2f;
                    Vector3 p = from + outward * (17.4f - Vector3.Dot(from - centre, outward));
                    Vector3[] route = districtSwitchbackRoute.points;
                    Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(outward, up) * Mathf.Sign(route[3].x - route[0].x), up);
                    Vector3 approach = rotation * Vector3.ProjectOnPlane(route[1] - route[0], Vector3.up).normalized;
                    Vector3 bottom = p + approach * 1.3f;
                    float rise = Vector3.Dot(to - from, up);
                    Vector3 scale = new(1f, rise / (route[3].y - route[0].y), 1f);
                    Matrix4x4 matrix = Matrix4x4.TRS(bottom - rotation * Vector3.Scale(route[0], scale), rotation, scale);
                    Bounds stairBounds = CastleGeometry.TransformBounds(districtSwitchbackPrefab.GetComponent<MeshFilter>().sharedMesh.bounds, matrix);
                    Bounds cell = new(centre, Vector3.one * (DistrictSpan - .1f));
                    if (rise < 6f || !cell.Contains(stairBounds.min) || !cell.Contains(stairBounds.max)) continue;
                    bool clear = true;
                    for (int r = 0; r < count; r++) if (stairBounds.Intersects(block.rooms[r].bounds)) { clear = false; break; }
                    Vector3 top = matrix.MultiplyPoint3x4(route[3]), exit = top - approach * 1.3f;
                    Vector3 outside = exit + outward * (21.4f - Vector3.Dot(exit - centre, outward));
                    Vector3 turn = outside + approach * Vector3.Dot(to - outside, approach);
                    if (!clear || Mathf.Abs(Vector3.Dot(to - top, approach)) < 2.2f ||
                        !Clear(from, p, a, null) || !Clear(p, bottom, null, null) ||
                        !Clear(top, exit, null, null) || !Clear(exit, outside, null, null) ||
                        !Clear(outside, turn, null, null) || !Clear(turn, to, null, b)) continue;
                    Segment(a, b, from, p); Segment(a, b, p, bottom);
                    DistrictCrossing stair = block.crossings[used++];
                    stair.a = a; stair.b = b; stair.active = stair.flight = stair.switchback = true;
                    stair.matrix = matrix; stair.start = bottom; stair.end = top; stair.bounds = stairBounds;
                    block.bounds.Encapsulate(stairBounds);
                    Segment(a, b, top, exit); Segment(a, b, exit, outside);
                    Segment(a, b, outside, turn); Segment(a, b, turn, to);
                    a.openDoorMask |= 1 << pa; b.openDoorMask |= 1 << pb; joined = true;
                }
            }
        }
        if (!DistrictClearOfNetwork(block.bounds))
        {
            foreach (DistrictRoom room in block.rooms) room.active = false;
            foreach (DistrictCrossing crossing in block.crossings) crossing.active = false;
        }
    }

    private void ConfigureDistrictBody(DistrictBody body, DistrictRoom room)
    {
        DistrictTemplate spec = districtTemplates[room.template];
        body.root.SetActive(false);
        for (int i = 0; i < body.geometry.Length; i++)
        {
            MeshCollider collider = body.geometry[i]; collider.enabled = i < spec.geometry.Length;
            if (!collider.enabled) continue;
            Matrix4x4 local = spec.collisionMatrices[i];
            collider.transform.SetLocalPositionAndRotation(local.GetPosition(), local.rotation);
            collider.transform.localScale = local.lossyScale; collider.sharedMesh = spec.geometry[i].sharedMesh;
        }
        body.floor.center = spec.module.floorPoint + Vector3.down * .06f;
        body.floor.size = new Vector3(spec.module.floorSize.x - .16f, .12f, spec.module.floorSize.y - .16f);
        body.roof.center = spec.module.roofPoint + Vector3.down * .06f;
        body.roof.size = new Vector3(spec.module.roofSize.x, .12f, spec.module.roofSize.y);
        for (int i = 0; i < body.doors.Length; i++)
        {
            BoxCollider door = body.doors[i];
            door.enabled = i < spec.module.ports.Length && (room.openDoorMask & (1 << i)) == 0;
            if (!door.enabled) continue;
            var port = spec.module.ports[i];
            door.center = port.position - port.outward * .06f + Vector3.up * 1.15f;
            door.size = Mathf.Abs(port.outward.x) > .5f ? new Vector3(.14f, 2.3f, port.width) : new Vector3(port.width, 2.3f, .14f);
        }
        for (int i = 0; i < 2; i++)
        {
            Vector3 surface = i == 0 ? spec.module.floorPoint : spec.module.roofPoint;
            Vector2 size = i == 0 ? spec.module.floorSize : spec.module.roofSize;
            var zone = body.zones[i]; zone.transform.localPosition = surface;
            zone.Configure(-(room.rotation * Vector3.up), Vector3.up * 1.15f,
                new Vector3(size.x, 4f, size.y), 3, true, true);
            zone.enabled = enableGravityZones;
        }
        body.piece.SetPooledGeometryBounds(spec.module.localBounds);
        body.root.transform.SetPositionAndRotation(room.position, room.rotation); body.root.transform.localScale = room.scale;
        body.root.SetActive(true);
    }

    public bool ValidateDistrictVariety(out string result)
    {
        var graphs = new System.Collections.Generic.HashSet<ulong>();
        var sizes = new System.Collections.Generic.HashSet<int>();
        int[] types = new int[3], localTypes = new int[3]; int clusters = 0, vertical = 0;
        foreach (DistrictBlock block in denseBlocks.Values)
        {
            int nodes = 0; ulong graph = 14695981039346656037UL;
            for (int i = 0; i < block.rooms.Length; i++)
            {
                DistrictRoom room = block.rooms[i]; if (!room.active) continue;
                nodes++; types[room.template]++;
                if (room.bounds.SqrDistance(player.position) < 100f * 100f) localTypes[room.template]++;
                graph = unchecked((graph ^ (ulong)(room.template + room.floor * 3 + 1)) * 1099511628211UL);
                for (int j = 0; j < i; j++)
                {
                    bool joined = false;
                    foreach (DistrictCrossing edge in block.crossings)
                        if (edge.active && (edge.doorPair || edge.switchback) &&
                            (edge.a == room && edge.b == block.rooms[j] || edge.b == room && edge.a == block.rooms[j]))
                        { joined = true; break; }
                    graph = unchecked((graph ^ (joined ? 1UL : 0UL)) * 1099511628211UL);
                }
            }
            if (nodes == 0) continue;
            clusters++; graphs.Add(graph); sizes.Add(nodes);
            foreach (DistrictCrossing edge in block.crossings) if (edge.active && edge.switchback) vertical++;
        }
        result = $"Streamed graph variety: clusters={clusters}, distinct topology/type graphs={graphs.Count}, module counts={sizes.Count}, rooms/turn halls/junctions={string.Join(",", types)}, within 100m={string.Join(",", localTypes)}, intact stair_2 links={vertical}. Fingerprints exclude transforms and scale.";
        return clusters >= 64 && graphs.Count >= 24 && sizes.Count >= 3 && types[0] > 0 && types[1] > 0 && types[2] > 0 &&
            localTypes[0] >= 4 && localTypes[1] + localTypes[2] >= 4 && vertical > 0;
    }
}
