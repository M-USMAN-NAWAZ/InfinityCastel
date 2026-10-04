using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private enum RebuildPhase { Idle, Planning, Moving }
    private struct RebuildCandidate
    {
        public InfinityCastleBuilder piece;
        public NearbyNode node;
        public Vector3Int cell;
        public Vector3 position;
        public Quaternion rotation;
        public int serial;
        public int district;
        public float distance;
        public bool started;
    }
    private RebuildPhase rebuildPhase;
    private readonly List<RebuildCandidate> rebuildCandidates = new();
    private readonly HashSet<NearbyNode> rebuildProtected = new();
    private readonly List<(NearbyLink link, Bounds sweep)> rebuildLinkSweeps = new();
    private Vector3 rebuildDirection;
    private int nearbyShapeRevision, rebuildCursor, rebuildPass, rebuildSequence;
    public bool IsRebuilding => rebuildPhase != RebuildPhase.Idle;
    public int MovingBuildingCount
    {
        get
        {
            int count = NearbyMovingCount;
            foreach (InfinityCastleBuilder piece in decor.Values) if (piece.IsMoving) count++;
            foreach (Node node in nodes.Values) if (node.piece.IsMoving) count++;
            return count;
        }
    }
    public int RebuildGeneration { get; private set; }
    public int RebuiltBuildingCount { get; private set; }
    public int RebuiltRouteCount { get; private set; }
    public int RebuiltDistrictCount { get; private set; }
    public int LastRebuildSelectedCount { get; private set; }
    public int LastRebuildProtectedCount { get; private set; }
    public int LastRebuildMovedCount { get; private set; }
    public int LastRebuildRotatedCount { get; private set; }
    public int LastRebuildBlockedCount { get; private set; }
    public int LastRebuildDistrictCount { get; private set; }
    public int LastRebuildDistrictBuildingCount { get; private set; }
    public int LastRebuildNearMovedCount { get; private set; }
    public int LastRebuildCornerMovedCount { get; private set; }
    public Vector3 LastRebuildUp { get; private set; } = Vector3.up;
    public int NearbyLayoutSignature
    {
        get
        {
            int signature = 0;
            foreach (NearbyNode node in nearbyNodes)
                signature ^= Vector3Int.RoundToInt(node.surface * 100f).GetHashCode() ^
                    Vector3Int.RoundToInt(node.up).GetHashCode();
            return signature;
        }
    }
    public bool TryGetRebuildingSurface(out Vector3 point, out Vector3 up, bool coordinatedOnly = false)
    {
        foreach (RebuildCandidate candidate in rebuildCandidates)
            if (candidate.node != null && (!coordinatedOnly || candidate.district > 0) &&
                IsCurrentRebuildCandidate(candidate) && candidate.started && candidate.piece.IsMoving)
            { point = OpenBalconyPoint(candidate.node, candidate.node.surface) + candidate.node.up * 0.08f; up = candidate.node.up; return true; }
        point = up = default; return false;
    }
    public bool DirectRebuild(Vector3 preferredDirection)
    {
        if (!initialized || !randomNearbyCastle || IsRebuilding || controller == null || !controller.Grounded || HasPendingLayout) return false;
        foreach (NearbyNode node in nearbyNodes) if (node.piece.IsMoving) return false;
        foreach (InfinityCastleBuilder piece in decor.Values) if (piece.IsMoving) return false;
        rebuildDirection = preferredDirection;
        ProtectRebuildNeighborhood();
        rebuildCandidates.Clear();
        foreach (NearbyNode node in nearbyNodes)
        {
            if (rebuildProtected.Contains(node)) continue;
            node.rebuilding = true;
            rebuildCandidates.Add(new RebuildCandidate { node = node, piece = node.piece,
                position = node.piece.transform.position, rotation = node.piece.transform.rotation, serial = node.serial,
                distance = NearbyMotionDistance(node) });
        }
        foreach (var pair in decor)
            if (!pair.Value.ReservedBounds.Intersects(PlayerSafetyBounds()))
                rebuildCandidates.Add(new RebuildCandidate { piece = pair.Value, cell = pair.Key,
                    position = pair.Value.transform.position, rotation = pair.Value.transform.rotation });
        rebuildCandidates.Sort((a, b) =>
        {
            bool nearA = a.node != null && a.distance <= NearFieldRadius, nearB = b.node != null && b.distance <= NearFieldRadius;
            if (nearA != nearB) return nearA ? -1 : 1;
            if (nearA) return a.distance.CompareTo(b.distance);
            return (b.piece.WorldBounds.center - player.position).sqrMagnitude.CompareTo((a.piece.WorldBounds.center - player.position).sqrMagnitude);
        });
        LastRebuildSelectedCount = rebuildCandidates.Count;
        LastRebuildProtectedCount = ActiveBuildingCount - LastRebuildSelectedCount;
        LastRebuildMovedCount = LastRebuildRotatedCount = LastRebuildBlockedCount = 0;
        LastRebuildDistrictCount = LastRebuildDistrictBuildingCount = 0;
        LastRebuildNearMovedCount = LastRebuildCornerMovedCount = 0;
        if (rebuildCandidates.Count == 0) return false;
        rebuildCursor = rebuildPass = 0; rebuildSequence++;
        LastRebuildUp = gravity.Up;
        rebuildPhase = RebuildPhase.Planning;
        return true;
    }
    private void ProtectRebuildNeighborhood()
    {
        rebuildProtected.Clear();
        Bounds approaching = PlayerSafetyBounds();
        approaching.Encapsulate(new Bounds(player.position + playerVelocity * 1.5f + gravity.Up, Vector3.one * 5f));
        NearbyNode nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (NearbyNode node in nearbyNodes)
        {
            float distance = Mathf.Min(SurfaceDistance(node, player.position), NearbyFaceDistance(node, true, player.position));
            if (distance < nearestDistance) { nearest = node; nearestDistance = distance; }
            if (IsRiding(node) || node.piece.ReservedBounds.Intersects(approaching) ||
                NearbyStandingSpace(node).Intersects(approaching) || NearbyStandingSpace(node, true).Intersects(approaching) ||
                OccupiesNearbyZone(node.zone) || OccupiesNearbyZone(node.undersideZone) ||
                (HasNearbyLanding && node == nearbyLanding)) rebuildProtected.Add(node);
        }
        if (nearest != null) rebuildProtected.Add(nearest);
        NearbyNode escape = null;
        float best = float.NegativeInfinity;
        foreach (NearbyLink link in nearbyLinks)
        {
            if (link.reservation.Intersects(approaching)) { rebuildProtected.Add(link.a); rebuildProtected.Add(link.b); }
            NearbyNode other = link.a == nearest ? link.b : link.b == nearest ? link.a : null;
            if (other == null) continue;
            float score = Vector3.Dot((other.surface - nearest.surface).normalized, rebuildDirection.normalized);
            if (score > best) { best = score; escape = other; }
        }
        if (escape != null) rebuildProtected.Add(escape);
        if (rideMovingBuildings)
            foreach (NearbyNode node in nearbyNodes)
                if ((!node.piece.IsMoving || node.carryingPlayer) && IsSafeRider(node)) rebuildProtected.Remove(node);
    }
    private bool UpdateNearbyRebuild()
    {
        if (!IsRebuilding) return false;
        if (IsUnsafeRebuildFall()) { CancelCastleRebuild(); return false; }
        ProtectRebuildNeighborhood();
        bool stopped = false;
        foreach (RebuildCandidate candidate in rebuildCandidates)
        {
            if (!IsCurrentRebuildCandidate(candidate) || !candidate.started || !candidate.piece.IsMoving) continue;
            bool approaching = candidate.node != null ? (candidate.node.carryingPlayer ? !IsSafeRider(candidate.node) : rebuildProtected.Contains(candidate.node)) :
                candidate.piece.ReservedBounds.Intersects(PlayerSafetyBounds());
            if (!approaching) continue;
            StopRebuildDistrict(candidate); stopped = true;
        }
        if (stopped) RefreshRearrangedConnections();
        if (rebuildPhase != RebuildPhase.Planning) return false;
        double start = Time.realtimeSinceStartupAsDouble;
        int budget = mobileBudget ? Mathf.Max(1, layoutStepsPerFrame) : int.MaxValue;
        while (rebuildCursor < rebuildCandidates.Count && budget > 0)
        {
            RebuildCandidate candidate = rebuildCandidates[rebuildCursor];
            if (!candidate.started && IsCurrentRebuildCandidate(candidate) && candidate.piece.gameObject.activeSelf)
            {
                budget--;
                bool success = candidate.node != null ? TryRearrangeDistrict(candidate.node) || TryRearrangeNearby(candidate.node) :
                    TryRearrangeDecor(candidate.piece, candidate.cell);
                if (success)
                {
                    candidate = rebuildCandidates[rebuildCursor];
                    candidate.started = true; rebuildCandidates[rebuildCursor] = candidate; RefreshReservations();
                }
            }
            rebuildCursor++;
            if (mobileBudget && (Time.realtimeSinceStartupAsDouble - start) * 1000d >= layoutMillisecondsPerFrame) break;
        }
        if (rebuildCursor == rebuildCandidates.Count) rebuildPhase = RebuildPhase.Moving;
        return false;
    }
    private bool TryRearrangeNearby(NearbyNode node) => TryBeginNearbyMotion(node, rebuildDirection, rebuildMoveSeconds, rebuildSequence, true);
    private Bounds NearbyStandingSweep(NearbyNode node, Vector3 offset, Quaternion rotation, bool underside = false)
    {
        Bounds initial = NearbyStandingSpace(node, underside), swept = initial;
        Quaternion delta = rotation * Quaternion.Inverse(node.piece.transform.rotation);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(Quaternion.identity, delta) / 5f));
        for (int i = 1; i <= steps; i++)
        {
            float fraction = (float)i / steps;
            Matrix4x4 pose = Matrix4x4.TRS(node.surface + offset * fraction,
                Quaternion.Slerp(Quaternion.identity, delta, fraction), Vector3.one) * Matrix4x4.Translate(-node.surface);
            swept.Encapsulate(CastleGeometry.TransformBounds(initial, pose));
        }
        float radius = initial.extents.magnitude + Vector3.Distance(initial.center, node.surface);
        swept.Expand(4f * radius * Mathf.Sin(Quaternion.Angle(Quaternion.identity, delta) / steps * Mathf.Deg2Rad * 0.25f) + 0.15f);
        return swept;
    }
    private bool TryRearrangeDecor(InfinityCastleBuilder piece, Vector3Int cell)
    {
        if (!decor.TryGetValue(cell, out InfinityCastleBuilder current) || current != piece || piece.IsMoving) return false;
        Vector3 centre = piece.WorldBounds.center;
        Vector3 anchor = origin + (Vector3)cell * decorSpacing;
        int first = Mathf.FloorToInt(Hash(cell, rebuildSequence * 113) * 6f);
        for (int attempt = 0; attempt < 18; attempt++)
        {
            Vector3 offset = directions[(first + attempt) % 6] * (attempt < 12 ? rebuildTravel : rebuildTravel * 0.35f);
            Vector3 relative = centre + offset - anchor;
            float envelope = decorSpacing * 0.3f;
            relative = new Vector3(Mathf.Clamp(relative.x, -envelope, envelope), Mathf.Clamp(relative.y, -envelope, envelope), Mathf.Clamp(relative.z, -envelope, envelope));
            offset = anchor + relative - centre;
            if (offset.sqrMagnitude < 0.25f) continue;
            Vector3 axis = attempt < 6 && Hash(cell, rebuildSequence * 113 + 1) < 0.35f ? piece.transform.right : piece.transform.up;
            float angle = attempt < 12 ? (Hash(cell, rebuildSequence * 113 + 2) < 0.5f ? 90f : -90f) : 0f;
            Quaternion rotation = Quaternion.AngleAxis(angle, axis) * piece.transform.rotation;
            Bounds sweep = piece.SweepRearrange(centre, offset, rotation);
            if (!IsSpaceFree(sweep, piece)) continue;
            piece.BeginRearrange(centre, offset, rotation, rebuildMoveSeconds, sweep);
            return true;
        }
        return false;
    }
    private bool ReserveRearrangedConnections(NearbyNode node, Vector3 offset, Quaternion rotation)
    {
        rebuildLinkSweeps.Clear();
        foreach (NearbyLink connection in nearbyLinks)
        {
            if (connection.a != node && connection.b != node) continue;
            Bounds swept = connection.reservation;
            bool clear = true;
            for (int sample = 1; sample <= 16; sample++)
            {
                float fraction = sample / 16f;
                Quaternion pose = Quaternion.Slerp(node.piece.transform.rotation, rotation, fraction);
                Vector3 a = connection.ASurface + (connection.a == node ? offset * fraction : Vector3.zero);
                Vector3 b = connection.BSurface + (connection.b == node ? offset * fraction : Vector3.zero);
                Quaternion aRotation = connection.a == node ? pose : connection.a.piece.transform.rotation;
                Quaternion bRotation = connection.b == node ? pose : connection.b.piece.transform.rotation;
                if (!connection.corner)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(b - a, connection.AUp).normalized;
                    float run = Vector3.ProjectOnPlane(b - a, connection.AUp).magnitude - DeckEdgeDistance(direction, aRotation) - DeckEdgeDistance(direction, bRotation) + 0.36f;
                    float authoredRun = Mathf.Abs(Vector3.Dot(b - a, connection.AUp)) > 0.2f ? flightStair.run : flatStair != null ? flatStair.run : 0f;
                    if (run < authoredRun + 0.15f) { clear = false; break; }
                }
                ConfigureNearbyConnectionPose(connection, a, b, aRotation, bRotation);
                swept.Encapsulate(NearbyConnectionReservation(connection));
            }
            ConfigureNearbyLink(connection);
            float rotationPadding = connection.corner ? deckSize * Mathf.Sqrt(2f) *
                Mathf.Sin(Quaternion.Angle(node.piece.transform.rotation, rotation) * Mathf.Deg2Rad / 32f) : 0f;
            swept.Expand(0.5f + rotationPadding * 2f);
            if (!clear || swept.Intersects(PlayerSafetyBounds()) || !NearbyConnectionIsClear(swept, connection.a, connection.b, connection)) return false;
            foreach (InfinityCastleBuilder other in decor.Values) if (other.ReservedBounds.Intersects(swept)) return false;
            rebuildLinkSweeps.Add((connection, swept));
        }
        foreach (var reservation in rebuildLinkSweeps) reservation.link.reservation = reservation.sweep;
        return true;
    }
    private void ConfigureNearbyLink(NearbyLink connection) => ConfigureNearbyConnectionPose(connection, connection.ASurface, connection.BSurface,
        connection.a.piece.transform.rotation, connection.b.piece.transform.rotation);
    private void RefreshRearrangedConnections()
    {
        foreach (NearbyNode node in nearbyNodes) node.surface = node.roof.transform.position;
        foreach (NearbyLink connection in nearbyLinks)
        {
            ConfigureNearbyLink(connection);
            if (!connection.a.piece.IsMoving && !connection.b.piece.IsMoving)
                connection.reservation = NearbyConnectionReservation(connection);
        }
        RefreshReservations(); Physics.SyncTransforms();
    }
    private void CancelCastleRebuild()
    {
        foreach (RebuildCandidate candidate in rebuildCandidates)
        {
            if (!IsCurrentRebuildCandidate(candidate)) continue;
            if (candidate.started) candidate.piece.StopShift();
            if (candidate.node != null) candidate.node.rebuilding = false;
        }
        rebuildPhase = RebuildPhase.Idle;
        RefreshRearrangedConnections(); rebuildCandidates.Clear();
    }
    private bool IsCurrentRebuildCandidate(RebuildCandidate candidate) => candidate.node != null ?
        candidate.node.serial == candidate.serial && nearbyNodes.Contains(candidate.node) :
        decor.TryGetValue(candidate.cell, out InfinityCastleBuilder piece) && piece == candidate.piece;
    private bool IsUnsafeRebuildFall()
    {
        if (controller.Grounded || controller.VerticalSpeed >= -4f) return false;
        return !Physics.Raycast(player.position + gravity.Up * 0.3f, -gravity.Up, out RaycastHit hit,
            controller.JumpHeight + 1.5f, controller.GroundLayers, QueryTriggerInteraction.Ignore) || !IsCastleSurface(hit.collider);
    }
    private void CompleteCastleRebuild()
    {
        if (rebuildPhase != RebuildPhase.Moving) return;
        foreach (RebuildCandidate candidate in rebuildCandidates) if (IsCurrentRebuildCandidate(candidate) && candidate.piece.IsMoving) return;
        if (rebuildPass++ < 2)
        { rebuildCursor = 0; rebuildPhase = RebuildPhase.Planning; RefreshRearrangedConnections(); return; }
        int moved = 0, rotated = 0, routes = 0, near = 0, corners = 0;
        foreach (RebuildCandidate candidate in rebuildCandidates)
        {
            if (!IsCurrentRebuildCandidate(candidate)) continue;
            bool changed = candidate.started && (Vector3.Distance(candidate.position, candidate.piece.transform.position) > 0.1f ||
                Quaternion.Angle(candidate.rotation, candidate.piece.transform.rotation) > 1f);
            if (changed) { moved++; if (candidate.node != null && candidate.node.route) routes++; }
            if (changed && candidate.node != null)
            {
                if (candidate.distance <= NearFieldRadius) near++;
                if (HasNearbyGravityCorner(candidate.node)) corners++;
            }
            if (candidate.started && Quaternion.Angle(candidate.rotation, candidate.piece.transform.rotation) > 1f) rotated++;
            if (candidate.node != null) { candidate.node.rebuilding = false; candidate.node.anchor = candidate.node.roof.transform.position; }
        }
        LastRebuildMovedCount = moved; LastRebuildRotatedCount = rotated; LastRebuildBlockedCount = LastRebuildSelectedCount - moved;
        LastRebuildNearMovedCount = near; LastRebuildCornerMovedCount = corners;
        RebuiltBuildingCount += moved; RebuiltRouteCount += routes; RebuildGeneration++;
        RebuiltDistrictCount += LastRebuildDistrictCount;
        nearbyShapeRevision = rebuildSequence;
        rebuildPhase = RebuildPhase.Idle;
        RefreshRearrangedConnections(); rebuildCandidates.Clear();
    }
}
