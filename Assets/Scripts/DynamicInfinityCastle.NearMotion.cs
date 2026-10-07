using UnityEngine;

public partial class DynamicInfinityCastle
{
    private int nearbyMotionSequence;
    public float NearFieldRadius => deckSize * 1.5f;
    public int NearFieldMotionCount { get; private set; }
    public int NearbyCornerMotionCount { get; private set; }
    public float LastNearFieldMotionDistance { get; private set; }
    public int RideMotionCount { get; private set; }
    public float LargestNearbyTravel { get; private set; }

    private bool IsSafeRider(NearbyNode node)
    {
        if (!IsRiding(node)) return false;
        if (Vector3.Dot(gravity.Up, node.up) < 0.99f) return false;
        Transform face = node.roof.transform;
        Vector3 foot = face.InverseTransformPoint(player.position);
        if (Mathf.Abs(foot.x) > deckSize * 0.5f - 1.5f || Mathf.Abs(foot.z) > deckSize * 0.5f - 1.5f || Mathf.Abs(foot.y) > 0.4f) return false;
        foreach (NearbyLink link in nearbyLinks)
            if ((link.a == node || link.b == node) && (OccupiesNearbyZone(link.link.root.GetComponent<InfiniteCastleGrid>()) ||
                (link.turnLanding != null && link.turnLanding.root.activeSelf && OccupiesNearbyZone(link.turnLanding.root.GetComponent<InfiniteCastleGrid>())))) return false;
        return true;
    }

    private float NearbyMotionDistance(NearbyNode node) =>
        Mathf.Min(SurfaceDistance(node, player.position), NearbyFaceDistance(node, true, player.position));

    private bool HasNearbyGravityCorner(NearbyNode node)
    {
        foreach (NearbyLink link in nearbyLinks)
            if (link.corner && (link.a == node || link.b == node)) return true;
        return false;
    }

    private Vector3 NearbyMotionDirection(NearbyNode node, Vector3 direction)
    {
        direction = Vector3.ProjectOnPlane(direction, node.up);
        return direction.normalized;
    }

    private bool TryBeginNearbyMotion(NearbyNode node, Vector3 preference, float seconds, int sequence, bool wholeCastle)
    {
        if (!nearbyNodes.Contains(node) || rebuildProtected.Contains(node) || node.piece.IsMoving) return false;
        bool riding = rideMovingBuildings && IsSafeRider(node);
        foreach (NearbyLink connection in nearbyLinks)
            if ((connection.a == node || connection.b == node) && (connection.a.piece.IsMoving || connection.b.piece.IsMoving ||
                connection.reservation.Intersects(PlayerSafetyBounds()))) return false;
        Vector3 outward = node.surface - player.position;
        float distanceToPlayer = NearbyMotionDistance(node);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 direction = NearbyMotionDirection(node, attempt % 6 == 0 ? preference : attempt % 6 == 1 ? outward :
                directions[(attempt + node.serial + sequence) % 6]);
            if (direction.sqrMagnitude < 0.5f) continue;
            float travel = nearbyShiftTravel * (attempt < 8 ? 1f : attempt < 16 ? 0.67f : attempt < 24 ? 0.33f : 0.125f);
            Vector3 offset = direction * travel;
            float yaw = !riding && attempt < 6 ? 90f : !riding && attempt < 12 && !node.route ? -90f : 0f;
            Quaternion rotation = Quaternion.AngleAxis(yaw, node.up) * node.piece.transform.rotation;
            Bounds sweep = node.piece.SweepRearrange(node.surface, offset, rotation);
            if (!NearbySpaceIsFree(sweep, node, ignorePlayer: riding) ||
                !NearbyHeadroomIsClear(node, NearbyStandingSweep(node, offset, rotation)) ||
                !NearbyHeadroomIsClear(node, NearbyStandingSweep(node, offset, rotation, true)) ||
                !ReserveRearrangedConnections(node, offset, rotation)) continue;
            node.travelled = true; node.finishedAt = nearbyClock + seconds + (wholeCastle ? 25f : 12f);
            node.carryingPlayer = riding;
            if (riding) { RideMotionCount++; node.finishedAt += 12f; }
            LargestNearbyTravel = Mathf.Max(LargestNearbyTravel, travel);
            node.piece.BeginRearrange(node.surface, offset, rotation, seconds, sweep);
            RecordNearbyMotion(node, distanceToPlayer);
            return true;
        }
        return false;
    }

    private void RecordNearbyMotion(NearbyNode node, float distance = -1f)
    {
        if (distance < 0f) distance = NearbyMotionDistance(node);
        if (distance <= NearFieldRadius) { NearFieldMotionCount++; LastNearFieldMotionDistance = distance; }
        if (HasNearbyGravityCorner(node)) NearbyCornerMotionCount++;
    }

    private void UpdateNearbyMotionSafety()
    {
        bool moving = false;
        foreach (NearbyNode node in nearbyNodes) if (node.piece.IsMoving) { moving = true; break; }
        if (!moving) return;
        ProtectRebuildNeighborhood();
        bool stopped = false;
        foreach (NearbyNode node in nearbyNodes)
            if (node.piece.IsMoving && (node.carryingPlayer ? !IsSafeRider(node) : rebuildProtected.Contains(node)))
            { node.piece.StopShift(); node.carryingPlayer = false; stopped = true; }
        if (stopped) RefreshRearrangedConnections();
    }

    public bool TryGetNearFieldMovingSurface(out Vector3 point, out Vector3 up, out float distance)
    {
        NearbyNode closest = null;
        distance = NearFieldRadius;
        foreach (NearbyNode node in nearbyNodes)
        {
            float next = NearbyMotionDistance(node);
            if (!node.piece.IsMoving || next > distance) continue;
            closest = node; distance = next;
        }
        if (closest == null) { point = up = default; return false; }
        bool back = NearbyFaceDistance(closest, true, player.position) < SurfaceDistance(closest, player.position);
        up = back ? -closest.up : closest.up;
        point = (back ? closest.underside : closest.roof).transform.position + up * 0.08f;
        return true;
    }

    private Vector3 RoofPointAtPose(Vector3 surface, Quaternion rotation, Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - surface);
        local.x = Mathf.Clamp(local.x, -deckSize * 0.5f + 0.01f, deckSize * 0.5f - 0.01f);
        local.z = Mathf.Clamp(local.z, -deckSize * 0.5f + 0.01f, deckSize * 0.5f - 0.01f);
        local.y = 0f;
        return surface + rotation * local;
    }

    private void ConfigureNearbyConnectionPose(NearbyLink connection, Vector3 a, Vector3 b, Quaternion aRotation, Quaternion bRotation)
    {
        if (!connection.corner)
        {
            if (connection.attached) { ConfigureAttachedConnection(connection, a, b, aRotation, bRotation); return; }
            ConfigureLink(connection.link, a, b, connection.AUp, CastleGeometry.Orientation(connection.AUp), aRotation, bRotation);
            return;
        }
        // Keep the two gravity faces joined along their common intersection while either face slides.
        Vector3 axis = Vector3.Cross(connection.AUp, connection.BUp);
        Vector3 corner = a + connection.BUp * Vector3.Dot(b - a, connection.BUp);
        corner += axis * (Vector3.Dot((a + b) * 0.5f - corner, axis) + connection.cornerAxisOffset);
        ConfigureCornerLanding(connection.link, RoofPointAtPose(a, aRotation, corner), corner, connection.AUp);
        Vector3 turnEnd = RoofPointAtPose(b, bRotation, corner);
        ConfigureCornerLanding(connection.turnLanding, CornerTurnStart(corner, turnEnd), turnEnd, connection.BUp, true);
    }

    private Bounds NearbyConnectionReservation(NearbyLink connection)
    {
        Bounds bounds = ConnectionClearance(connection.link.bounds, connection.AUp);
        if (connection.corner) bounds.Encapsulate(ConnectionClearance(connection.turnLanding.bounds, connection.BUp));
        return bounds;
    }
}
