using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private readonly List<NearbyNode> rebuildDistrict = new(4);
    private readonly List<Bounds> rebuildDistrictSweeps = new(4);
    private readonly List<InfinityCastleBuilder> decorDistrict = new(4);
    private readonly List<int> decorDistrictCandidates = new(4);
    private readonly List<Bounds> decorDistrictSweeps = new(4);

    private bool TryRearrangeDecorDistrict(InfinityCastleBuilder seed, Vector3Int cell)
    {
        if (!denseDistantArchitecture || seed.IsMoving || Hash(cell, rebuildSequence * 137 + 1) > .65f) return false;
        decorDistrict.Clear(); decorDistrictCandidates.Clear();
        AddDecorDistrictMember(seed);
        for (int neighbor = 0; neighbor < directions.Length; neighbor++)
        {
            if (decorDistrict.Count == 4) break;
            int axis = neighbor < 2 ? neighbor + 2 : neighbor < 4 ? neighbor - 2 : neighbor;
            Vector3Int direction = Vector3Int.RoundToInt(directions[axis]);
            if (decor.TryGetValue(cell + direction, out InfinityCastleBuilder next) && !next.IsMoving) AddDecorDistrictMember(next);
        }
        if (decorDistrict.Count < 2) return false;
        int first = Mathf.FloorToInt(Hash(cell, rebuildSequence * 137) * 6f);
        for (int attempt = 0; attempt < 18; attempt++)
        {
            float distance = attempt < 6 ? Mathf.Min(12f, rebuildTravel) : attempt < 12 ? 6f : 2.5f;
            Vector3 offset = directions[(first + attempt) % 6] * distance;
            decorDistrictSweeps.Clear();
            bool clear = true;
            for (int member = 0; member < decorDistrict.Count; member++)
            {
                InfinityCastleBuilder piece = decorDistrict[member];
                RebuildCandidate entry = rebuildCandidates[decorDistrictCandidates[member]];
                Vector3 relative = piece.WorldBounds.center + offset - DecorAnchor(entry.cell);
                Vector3 envelope = decorGridSpacing * .45f;
                Bounds sweep = piece.SweepTo(offset);
                if (Mathf.Abs(relative.x) > envelope.x || Mathf.Abs(relative.y) > envelope.y || Mathf.Abs(relative.z) > envelope.z ||
                    !IsSpaceFree(sweep, piece, decorDistrict)) { clear = false; break; }
                decorDistrictSweeps.Add(sweep);
            }
            if (!clear) continue;
            int district = ++LastRebuildDistrictCount;
            LastRebuildDistrictBuildingCount += decorDistrict.Count;
            for (int member = 0; member < decorDistrict.Count; member++)
            {
                InfinityCastleBuilder piece = decorDistrict[member];
                piece.BeginRearrange(piece.WorldBounds.center, offset, piece.transform.rotation, rebuildMoveSeconds, decorDistrictSweeps[member]);
                int index = decorDistrictCandidates[member];
                RebuildCandidate entry = rebuildCandidates[index]; entry.started = true; entry.district = district; rebuildCandidates[index] = entry;
            }
            return true;
        }
        return false;
    }
    private void AddDecorDistrictMember(InfinityCastleBuilder piece)
    {
        for (int index = 0; index < rebuildCandidates.Count; index++)
        {
            RebuildCandidate entry = rebuildCandidates[index];
            if (entry.piece != piece || entry.node != null || entry.started || !IsCurrentRebuildCandidate(entry) ||
                Hash(entry.cell, rebuildSequence * 137 + 1) > .65f) continue;
            decorDistrict.Add(piece); decorDistrictCandidates.Add(index); return;
        }
    }

    private static bool BelongsToDistrict(NearbyLink link, ICollection<NearbyNode> district) =>
        district != null && (district.Contains(link.a) || district.Contains(link.b));

    private bool CanJoinRebuildDistrict(NearbyNode node)
    {
        if (rebuildProtected.Contains(node) || node.piece.IsMoving || IsSafeRider(node)) return false;
        bool pending = false;
        foreach (RebuildCandidate candidate in rebuildCandidates)
            if (candidate.node == node && IsCurrentRebuildCandidate(candidate) && !candidate.started)
            { pending = true; break; }
        if (!pending) return false;
        foreach (NearbyLink link in nearbyLinks)
            if ((link.a == node || link.b == node) && (link.corner || link.a.piece.IsMoving || link.b.piece.IsMoving ||
                link.reservation.Intersects(PlayerSafetyBounds()))) return false;
        return true;
    }

    private bool TryRearrangeDistrict(NearbyNode seed)
    {
        if (!CanJoinRebuildDistrict(seed)) return false;
        rebuildDistrict.Clear(); rebuildDistrict.Add(seed);
        for (int cursor = 0; cursor < rebuildDistrict.Count && rebuildDistrict.Count < 4; cursor++)
        {
            NearbyNode node = rebuildDistrict[cursor];
            foreach (NearbyLink link in nearbyLinks)
            {
                if (link.corner || link.aUnderside || link.bUnderside) continue;
                NearbyNode next = link.a == node ? link.b : link.b == node ? link.a : null;
                if (next == null || rebuildDistrict.Contains(next) || Vector3.Dot(next.up, seed.up) < 0.99f ||
                    !CanJoinRebuildDistrict(next)) continue;
                rebuildDistrict.Add(next);
                if (rebuildDistrict.Count == 4) break;
            }
        }
        if (rebuildDistrict.Count < 2) return false;
        Vector3 outward = Vector3.ProjectOnPlane(seed.surface - player.position, seed.up).normalized;
        for (int attempt = 0; attempt < 18; attempt++)
        {
            Vector3 direction = attempt % 6 == 0 ? Vector3.ProjectOnPlane(rebuildDirection, seed.up).normalized :
                attempt % 6 == 1 ? outward : Vector3.ProjectOnPlane(directions[(attempt + seed.serial + rebuildSequence) % 6], seed.up).normalized;
            if (direction.sqrMagnitude < 0.5f) continue;
            float distance = attempt < 6 ? Mathf.Min(6f, rebuildTravel * 0.35f) : attempt < 12 ? 3f : 1.2f;
            Vector3 offset = direction * distance;
            rebuildDistrictSweeps.Clear();
            bool clear = true;
            foreach (NearbyNode member in rebuildDistrict)
            {
                Bounds sweep = member.piece.SweepTo(offset);
                if (!NearbySpaceIsFree(sweep, member, movingTogether: rebuildDistrict) ||
                    !NearbyHeadroomIsClear(member, CastleGeometry.Sweep(NearbyStandingSpace(member), offset), movingTogether: rebuildDistrict) ||
                    !NearbyHeadroomIsClear(member, CastleGeometry.Sweep(NearbyStandingSpace(member, true), offset), movingTogether: rebuildDistrict))
                { clear = false; break; }
                rebuildDistrictSweeps.Add(sweep);
            }
            if (!clear || !ReserveDistrictConnections(offset)) continue;
            int district = ++LastRebuildDistrictCount;
            LastRebuildDistrictBuildingCount += rebuildDistrict.Count;
            // Identical offset, duration and easing preserve each internal floor and stair relationship.
            for (int member = 0; member < rebuildDistrict.Count; member++)
            {
                NearbyNode node = rebuildDistrict[member];
                node.travelled = true; node.finishedAt = nearbyClock + rebuildMoveSeconds + 25f;
                node.piece.BeginRearrange(node.surface, offset, node.piece.transform.rotation, rebuildMoveSeconds, rebuildDistrictSweeps[member]);
                RecordNearbyMotion(node);
                for (int candidate = 0; candidate < rebuildCandidates.Count; candidate++)
                {
                    RebuildCandidate entry = rebuildCandidates[candidate];
                    if (entry.node != node) continue;
                    entry.started = true; entry.district = district; rebuildCandidates[candidate] = entry; break;
                }
            }
            return true;
        }
        return false;
    }

    private bool ReserveDistrictConnections(Vector3 offset)
    {
        rebuildLinkSweeps.Clear();
        foreach (NearbyLink connection in nearbyLinks)
        {
            bool moveA = rebuildDistrict.Contains(connection.a), moveB = rebuildDistrict.Contains(connection.b);
            if (!moveA && !moveB) continue;
            if (connection.corner) return false;
            Bounds sweep = connection.reservation;
            if (moveA && moveB) sweep = CastleGeometry.Sweep(sweep, offset);
            else
            {
                bool valid = true;
                for (int sample = 1; sample <= 16; sample++)
                {
                    float fraction = sample / 16f;
                    Vector3 a = connection.ASurface + (moveA ? offset * fraction : Vector3.zero);
                    Vector3 b = connection.BSurface + (moveB ? offset * fraction : Vector3.zero);
                    Vector3 direction = Vector3.ProjectOnPlane(b - a, connection.AUp).normalized;
                    float run = Vector3.ProjectOnPlane(b - a, connection.AUp).magnitude -
                        DeckEdgeDistance(direction, connection.a.piece.transform.rotation) - DeckEdgeDistance(direction, connection.b.piece.transform.rotation) + 0.36f;
                    float authoredRun = connection.attached ? 0f : Mathf.Abs(Vector3.Dot(b - a, connection.AUp)) > 0.2f ? flightStair.run : flatStair != null ? flatStair.run : 0f;
                    if (run < authoredRun + 0.15f) { valid = false; break; }
                    ConfigureNearbyConnectionPose(connection, a, b, connection.a.piece.transform.rotation, connection.b.piece.transform.rotation);
                    sweep.Encapsulate(ConnectionClearance(connection.link.bounds, connection.AUp));
                }
                ConfigureNearbyLink(connection);
                if (!valid) return false;
                for (int member = 0; member < rebuildDistrict.Count; member++)
                    if (rebuildDistrict[member] != connection.a && rebuildDistrict[member] != connection.b &&
                        rebuildDistrictSweeps[member].Intersects(sweep)) return false;
            }
            sweep.Expand(0.5f);
            if (sweep.Intersects(PlayerSafetyBounds()) ||
                !NearbyConnectionIsClear(sweep, connection.a, connection.b, connection, rebuildDistrict)) return false;
            foreach (InfinityCastleBuilder piece in decor.Values) if (piece.ReservedBounds.Intersects(sweep)) return false;
            foreach (Node node in nodes.Values) if (node.reservation.Intersects(sweep)) return false;
            foreach (Link link in links.Values) if (link.bounds.Intersects(sweep)) return false;
            foreach (var other in rebuildLinkSweeps)
            {
                if (connection.a == other.link.a || connection.a == other.link.b || connection.b == other.link.a || connection.b == other.link.b) continue;
                bool otherInternal = rebuildDistrict.Contains(other.link.a) && rebuildDistrict.Contains(other.link.b);
                if (!(moveA && moveB && otherInternal) && other.sweep.Intersects(sweep)) return false;
            }
            rebuildLinkSweeps.Add((connection, sweep));
        }
        foreach (var reservation in rebuildLinkSweeps) reservation.link.reservation = reservation.sweep;
        return true;
    }

    private void StopRebuildDistrict(RebuildCandidate approached)
    {
        if (approached.district == 0) { approached.piece.StopShift(); return; }
        foreach (RebuildCandidate candidate in rebuildCandidates)
            if (candidate.district == approached.district && IsCurrentRebuildCandidate(candidate)) candidate.piece.StopShift();
    }

    public bool ValidateRebuildDistrictMotion(out string error)
    {
        for (int i = 0; i < rebuildCandidates.Count; i++)
        {
            RebuildCandidate candidate = rebuildCandidates[i];
            if (candidate.district == 0 || !IsCurrentRebuildCandidate(candidate)) continue;
            for (int j = i + 1; j < rebuildCandidates.Count; j++)
            {
                RebuildCandidate other = rebuildCandidates[j];
                if (other.district != candidate.district || !IsCurrentRebuildCandidate(other)) continue;
                Vector3 delta = candidate.piece.transform.position - candidate.position;
                Vector3 otherDelta = other.piece.transform.position - other.position;
                if (Vector3.Distance(delta, otherDelta) < 0.003f &&
                    Quaternion.Angle(candidate.rotation, candidate.piece.transform.rotation) < 0.02f &&
                    Quaternion.Angle(other.rotation, other.piece.transform.rotation) < 0.02f) continue;
                error = "Connected district lost its rigid floor relationship: " + candidate.piece.name + " / " + other.piece.name;
                return false;
            }
        }
        error = string.Empty; return true;
    }
}
