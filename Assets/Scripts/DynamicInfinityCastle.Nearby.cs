using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private sealed class NearbyNode
    {
        public InfinityCastleBuilder piece;
        public CastleWalkableSurface roof, underside;
        public InfiniteCastleGrid zone, undersideZone;
        public Vector3 surface, up, anchor;
        public Bounds artBounds;
        public float depth, finishedAt, nextFaceGrowthAt;
        public int serial, faceGrowthAttempt;
        public bool still, travelled, route, rebuilding, movedThisFrame, carryingPlayer;
    }
    private sealed class NearbyLink
    {
        public NearbyNode a, b;
        public Link link, turnLanding;
        public Bounds reservation;
        public bool corner;
        public bool aUnderside, bUnderside;
        public float cornerAxisOffset;
        public Vector3 AUp => aUnderside ? -a.up : a.up;
        public Vector3 BUp => bUnderside ? -b.up : b.up;
        public Vector3 ASurface => aUnderside ? a.underside.transform.position : a.surface;
        public Vector3 BSurface => bUnderside ? b.underside.transform.position : b.surface;
    }
    private readonly List<NearbyNode> nearbyNodes = new();
    private readonly Queue<NearbyNode> nearbyPool = new();
    private readonly List<NearbyLink> nearbyLinks = new();
    private readonly List<NearbyNode> nearbyCandidates = new();
    private readonly List<NearbyNode> growthCandidates = new();
    private IEnumerator<object> nearbyLayout;
    private IComparer<NearbyNode> growthComparer;
    private bool nearbyRefillRequested;
    private int NearbyReserveCount => mobileBudget ? 16 : 32;
    private NearbyNode startingRoof, firstJumpRoof;
    private NearbyNode nearbyLanding;
    private int nearbyLandingSerial;
    private bool HasNearbyLanding => nearbyLanding != null && nearbyLanding.serial == nearbyLandingSerial && nearbyNodes.Contains(nearbyLanding);
    private Vector3 nearbyStreamPosition;
    private float nearbyClock, nearbyRadius;
    private float NearbyWalkRadius => (deckSize + (flightStair != null ? flightStair.run : 8f) + 2f) * 3.7f;
    private int nearbySerial, nearbyWave;
    public int NearbyMovingCount
    {
        get { int count = 0; foreach (NearbyNode node in nearbyNodes) if (node.piece.IsMoving) count++; return count; }
    }

    private void PrewarmNearbyCastle(List<InfinityCastleBuilder> samples)
    {
        nearbyPoolSize = Mathf.Clamp(nearbyPoolSize, 96, 320);
        nearbyRadius = maximumBuildingHeight * layersInEveryDirection + deckSize;
        for (int i = 0; i < nearbyPoolSize; i++)
        {
            InfinityCastleBuilder piece = i < samples.Count ? samples[i] : CreatePiece(buildingPrefabs[i % buildingPrefabs.Count]);
            Bounds art = CastleGeometry.RendererBoundsInRoot(piece.transform);
            CastleWalkableSurface roof = CreateSurface("Connected Castle Base", deckSize, deckSize, false, piece.transform);
            roof.transform.localPosition = new Vector3(art.center.x, art.min.y - 0.02f, art.center.z);
            InfiniteCastleGrid zone = roof.gameObject.AddComponent<InfiniteCastleGrid>();
            zone.Configure(Vector3.down, Vector3.up * 2.3f, new Vector3(deckSize + 0.6f, 5.4f, deckSize + 0.6f), 1, true);
            CastleWalkableSurface underside = CreateSurface("Opposite Gravity Castle Balcony", deckSize, deckSize, false, piece.transform);
            underside.transform.SetLocalPositionAndRotation(new Vector3(art.center.x, art.min.y - 0.3f, art.center.z), Quaternion.Euler(180f, 0f, 0f));
            InfiniteCastleGrid undersideZone = underside.gameObject.AddComponent<InfiniteCastleGrid>();
            undersideZone.Configure(Vector3.up, Vector3.up * 2.3f, new Vector3(deckSize + 0.6f, 5.4f, deckSize + 0.6f), 1, true);
            // Lower faces are not gameplay entrances: their art faces away from the player's gravity.
            underside.gameObject.SetActive(false);
            piece.InitializePooledPiece(this);
            piece.gameObject.SetActive(false);
            nearbyPool.Enqueue(new NearbyNode { piece = piece, roof = roof, zone = zone, underside = underside, undersideZone = undersideZone,
                artBounds = art, depth = CastleGeometry.RendererBoundsInRoot(piece.transform).size.y });
        }
    }
    private void InitializeNearbyCastle()
    {
        startingRoof = TryCreateNearbyNode(origin, Vector3.up, true, true);
        startingRoof.route = true;
        gravity.retainZoneGravityInAir = enableGravityZones;
        player.position = OpenBalconyPoint(startingRoof, origin + Vector3.forward * (deckSize * 0.5f - 2f)) + Vector3.up * 0.08f;
        float stairRun = flightStair != null ? flightStair.run : flatStair != null ? flatStair.run : 8f;
        float stairRise = flightStair != null ? flightStair.rise : 0f;
        firstJumpRoof = TryCreateNearbyNode(origin + Vector3.forward * (deckSize + stairRun + 2f) + Vector3.up * stairRise, Vector3.up, false);
        if (firstJumpRoof != null) { firstJumpRoof.route = true; ConnectNearby(startingRoof, firstJumpRoof); }
        NearbyNode south = TryCreateNearbyNode(origin - Vector3.forward * (deckSize + stairRun + 3f) - Vector3.up * stairRise, Vector3.up, false);
        if (south != null) { south.route = true; ConnectNearby(startingRoof, south); }
        float edge = deckSize * 0.5f + 4.6f;
        // The lower branches face outward, so their towers rise clear of the floor.
        float wallHeight = -deckSize * 0.5f - 8.5f;
        NearbyNode right = TryCreateNearbyNode(origin + new Vector3(edge, wallHeight, 0f), Vector3.right, true);
        NearbyNode left = TryCreateNearbyNode(origin + new Vector3(-edge, wallHeight, 3f), Vector3.left, true);
        NearbyNode ceiling = TryCreateNearbyNode(origin + new Vector3(0f, -deckSize - 20f, 0f), Vector3.down, true);
        float cornerOffset = deckSize * 0.35f;
        if (right != null) ConnectGravityCorner(startingRoof, right, cornerOffset);
        if (left != null) ConnectGravityCorner(startingRoof, left, cornerOffset);
        if (right != null && ceiling != null) ConnectGravityCorner(right, ceiling, cornerOffset);
        if (left != null && ceiling != null) ConnectGravityCorner(left, ceiling, cornerOffset);
        if (firstJumpRoof != null)
        {
            NearbyNode endWall = TryCreateNearbyNode(firstJumpRoof.surface + new Vector3(0f, wallHeight, edge), Vector3.forward, true);
            if (endWall != null) ConnectGravityCorner(firstJumpRoof, endWall, cornerOffset);
        }
        if (south != null)
        {
            NearbyNode endWall = TryCreateNearbyNode(south.surface + new Vector3(2f, wallHeight, -edge), Vector3.back, true);
            if (endWall != null) ConnectGravityCorner(south, endWall, cornerOffset);
        }
        FillNearbyCastle();
        nearbyStreamPosition = player.position;
        RefreshReservations();
    }
    private NearbyNode TryCreateNearbyNode(Vector3 surface, Vector3 up, bool still, bool ignorePlayer = false, bool clearDecor = true, bool anchorAtWalkway = false)
    {
        if (nearbyPool.Count == 0 || (surface - player.position).sqrMagnitude > nearbyRadius * nearbyRadius) return null;
        NearbyNode node = nearbyPool.Dequeue();
        int serial = ++nearbySerial;
        Quaternion rotation = CastleGeometry.Orientation(up, Mathf.Floor(NearbyRandom(serial, 8) * 4f) * 90f);
        if (anchorAtWalkway) surface -= rotation * Vector3.right * (deckSize * 0.5f - 2f);
        node.piece.Place(surface + up * (node.depth * 0.5f), rotation);
        // Anchor the base balcony, leaving the authored tower above its floor.
        node.piece.transform.position += surface - node.roof.transform.position;
        node.piece.StopShift();
        Bounds bounds = node.piece.WorldBounds; bounds.Expand(0.3f);
        if (!NearbySpaceIsFree(bounds, null, ignorePlayer, !clearDecor) || !NearbyHeadroomIsClear(node, NearbyStandingSpace(node), !clearDecor) ||
            !NearbyHeadroomIsClear(node, NearbyStandingSpace(node, true), !clearDecor))
        { node.piece.gameObject.SetActive(false); nearbyPool.Enqueue(node); return null; }
        if (clearDecor) { ClearRouteDecor(bounds); ClearRouteDecor(NearbyStandingSpace(node)); ClearRouteDecor(NearbyStandingSpace(node, true)); }
        node.surface = node.roof.transform.position; node.anchor = node.surface; node.up = up; node.serial = serial;
        node.still = still; node.travelled = node.route = node.rebuilding = false; node.finishedAt = 0f;
        node.faceGrowthAttempt = 0; node.nextFaceGrowthAt = 0f;
        node.carryingPlayer = false;
        node.zone.gravityDirection = -up;
        node.zone.priority = 1;
        node.undersideZone.gravityDirection = up; node.undersideZone.priority = 1;
        node.piece.gameObject.SetActive(true); node.zone.enabled = enableGravityZones; node.undersideZone.enabled = false;
        nearbyNodes.Add(node);
        return node;
    }
    private float NearbyRandom(int serial, int salt) => Hash(new Vector3Int(serial, nearbyWave, 0), salt);
    private Bounds NearbyStandingSpace(NearbyNode node, bool underside = false) => CastleGeometry.TransformBounds(new Bounds(Vector3.up * 2f,
        new Vector3(deckSize, 4f, deckSize)), (underside ? node.underside : node.roof).transform.localToWorldMatrix);
    private bool NearbyHeadroomIsClear(NearbyNode candidate)
    {
        return NearbyHeadroomIsClear(candidate, NearbyStandingSpace(candidate));
    }
    private bool NearbyHeadroomIsClear(NearbyNode candidate, Bounds room, bool includeDecor = true, ICollection<NearbyNode> movingTogether = null)
    {
        foreach (NearbyNode node in nearbyNodes)
            if (node != candidate && !(movingTogether?.Contains(node) ?? false) && node.piece.ReservedBounds.Intersects(room)) return false;
        if (includeDecor) foreach (InfinityCastleBuilder piece in decor.Values) if (piece.ReservedBounds.Intersects(room)) return false;
        foreach (NearbyLink link in nearbyLinks)
            if (link.a != candidate && link.b != candidate && !BelongsToDistrict(link, movingTogether) && link.reservation.Intersects(room)) return false;
        return !HitsLandingDistrict(room);
    }
    private float SurfaceDistance(NearbyNode node, Vector3 point)
    {
        Vector3 local = node.roof.transform.InverseTransformPoint(point);
        return new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - deckSize * 0.5f), local.y,
            Mathf.Max(0f, Mathf.Abs(local.z) - deckSize * 0.5f)).magnitude;
    }
    private bool NearbySpaceIsFree(Bounds box, NearbyNode ignore = null, bool ignorePlayer = false, bool includeDecor = true, ICollection<NearbyNode> movingTogether = null)
    {
        if (!ignorePlayer && box.Intersects(PlayerSafetyBounds())) return false;
        foreach (NearbyNode node in nearbyNodes)
        {
            if (node == ignore || (movingTogether?.Contains(node) ?? false)) continue;
            if (node.piece.ReservedBounds.Intersects(box)) return false;
            Bounds standingSpace = NearbyStandingSpace(node);
            if (standingSpace.Intersects(box)) return false;
            if (NearbyStandingSpace(node, true).Intersects(box)) return false;
        }
        foreach (NearbyLink link in nearbyLinks)
            if (link.a != ignore && link.b != ignore && !BelongsToDistrict(link, movingTogether) && link.reservation.Intersects(box)) return false;
        foreach (Node node in nodes.Values) if (node.reservation.Intersects(box)) return false;
        foreach (Link link in links.Values) if (link.bounds.Intersects(box)) return false;
        if (includeDecor) foreach (InfinityCastleBuilder piece in decor.Values) if (piece.ReservedBounds.Intersects(box)) return false;
        return !HitsLandingDistrict(box);
    }
    private void FillNearbyCastle()
    {
        if (IsRebuilding) return;
        if (nearbyLayout != null) { nearbyRefillRequested = true; return; }
        growthComparer ??= Comparer<NearbyNode>.Create((a, b) => NearbyGrowthScore(a).CompareTo(NearbyGrowthScore(b)));
        nearbyLayout = BuildNearbyLayout();
        if (!initialized || !mobileBudget)
        {
            while (nearbyLayout.MoveNext()) { }
            nearbyLayout.Dispose(); nearbyLayout = null;
        }
    }
    private bool ProcessNearbyLayout()
    {
        if (nearbyLayout == null || IsRebuilding) return false;
        double start = Time.realtimeSinceStartupAsDouble;
        for (int step = 0; step < Mathf.Max(1, layoutStepsPerFrame); step++)
        {
            if (!nearbyLayout.MoveNext())
            {
                nearbyLayout.Dispose(); nearbyLayout = null;
                if (nearbyRefillRequested) { nearbyRefillRequested = false; FillNearbyCastle(); }
                break;
            }
            if ((Time.realtimeSinceStartupAsDouble - start) * 1000d >= layoutMillisecondsPerFrame) break;
        }
        RefreshReservations();
        return true;
    }
    private IEnumerator<object> BuildNearbyLayout()
    {
        nearbyWave++;
        // Local gravity exits are urgent; do not defer them until every distant route has been planned.
        EnsureNearbyGravityRoutes();
        yield return null;
        growthCandidates.Clear();
        foreach (NearbyNode node in nearbyNodes) if (node.route && !node.rebuilding) growthCandidates.Add(node);
        growthCandidates.Sort(growthComparer);
        // Grow connected routes first. Disconnected scenery must not consume the space needed for walking.
        int routeLimit = nearbyPoolSize - NearbyReserveCount;
        for (int cursor = 0; cursor < growthCandidates.Count && nearbyNodes.Count < routeLimit && nearbyPool.Count > NearbyReserveCount; cursor++)
        {
            NearbyNode node = growthCandidates[cursor];
            if (!nearbyNodes.Contains(node) || node.piece.IsTranslating || node.rebuilding) continue;
            int frameCount = 0;
            foreach (NearbyNode placed in nearbyNodes) if (placed.route && Vector3.Dot(placed.up, node.up) > 0.99f) frameCount++;
            if (frameCount >= (Vector3.Dot(node.up, gravity.Up) > 0.99f ? 96 : 4)) continue;
            Quaternion basis = CastleGeometry.Orientation(node.up);
            int sourceSerial = node.serial;
            for (int direction = 0; direction < 4 && nearbyNodes.Count < routeLimit && nearbyPool.Count > NearbyReserveCount; direction++)
            {
                if (!nearbyNodes.Contains(node) || node.serial != sourceSerial || node.rebuilding || node.piece.IsTranslating) break;
                Vector3 tangent = basis * (direction == 0 ? Vector3.right : direction == 1 ? Vector3.left :
                    direction == 2 ? Vector3.forward : Vector3.back);
                if (HasNearbyExit(node, tangent)) continue;
                float stairChance = nearbyShapeRevision == 0 ? 0.65f : nearbyShapeRevision % 2 == 0 ? 0.85f : 0.3f;
                bool stairs = flightStair != null && NearbyRandom(node.serial, direction + 41) < stairChance;
                float gap = (stairs ? flightStair.run : flatStair != null ? flatStair.run : 8f) + 2f;
                float rise = stairs ? flightStair.rise * (NearbyRandom(node.serial, direction + 53) < 0.45f ? -1f : 1f) : 0f;
                // A stable heading per port prevents a refill from scattering fresh candidates into occupied routes.
                Vector3Int port = new(node.serial, direction, nearbyShapeRevision);
                float skew = (Hash(port, 54) - 0.5f) * (nearbyShapeRevision == 0 ? 5f : 9f);
                if (nearbyShapeRevision > 0) gap += Hash(port, 55) * 3f;
                Vector3 surface = node.surface + tangent * (deckSize + gap) + node.up * rise + Vector3.Cross(node.up, tangent) * skew;
                if ((surface - player.position).magnitude > NearbyWalkRadius) continue;
                NearbyNode existing = FindNearbyNeighbor(node, surface);
                if (existing != null) { ConnectNearby(node, existing); continue; }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    NearbyNode next = TryCreateNearbyNode(surface + tangent * attempt * 2f, node.up,
                        NearbyRandom(node.serial, direction + 71) < stillPieceChance);
                    if (next == null) continue;
                    if (ConnectNearby(node, next)) { next.route = true; growthCandidates.Add(next); break; }
                    RetireNearbyNode(nearbyNodes.IndexOf(next));
                }
                yield return null;
            }
            growthCandidates.Sort(cursor + 1, growthCandidates.Count - cursor - 1, growthComparer);
        }
        // Close loops between already placed neighbors, giving branches more than one way out.
        for (int i = 0; i < nearbyNodes.Count; i++)
        {
            for (int j = i + 1; j < nearbyNodes.Count; j++)
                if (CanConnectNearby(nearbyNodes[i], nearbyNodes[j])) ConnectNearby(nearbyNodes[i], nearbyNodes[j]);
            yield return null;
        }
        EnsureNearbyGravityRoutes();
        yield return null;
        int count = growthCandidates.Count;
        int sceneryCount = 0;
        foreach (NearbyNode node in nearbyNodes) if (!node.route) sceneryCount++;
        for (int i = 0; i < count && nearbyPool.Count > NearbyReserveCount && sceneryCount < 16; i++)
        {
            NearbyNode node = growthCandidates[i];
            if (!nearbyNodes.Contains(node)) continue;
            Vector3 surface = node.surface + node.up * (maximumBuildingHeight + 8f) +
                CastleGeometry.Orientation(node.up) * new Vector3(deckSize * 0.35f, 0f, deckSize * 0.2f);
            if (TryCreateNearbyNode(surface, node.up, NearbyRandom(node.serial, 72) < stillPieceChance, false, false) != null) sceneryCount++;
            yield return null;
        }
        RefreshReservations();
    }
    private float NearbyGrowthScore(NearbyNode node) => (node.surface - player.position - Vector3.ProjectOnPlane(playerVelocity, gravity.Up) * 3f).magnitude +
        (Vector3.Dot(node.up, gravity.Up) > 0.99f ? 0f : nearbyRadius * 4f);
    private bool HasNearbyGravityFrame(Vector3 up)
    {
        foreach (NearbyLink connection in nearbyLinks)
            if (connection.corner && Vector3.Dot(connection.a.up, gravity.Up) > 0.99f &&
                Vector3.Dot(connection.BUp, up) > 0.99f && SurfaceDistance(connection.a, player.position) < deckSize) return true;
        return false;
    }
    private void EnsureNearbyGravityRoutes()
    {
        if (!enableGravityZones || nearbyPool.Count <= 10) return;
        NearbyNode anchor = null;
        foreach (NearbyNode node in nearbyNodes)
            if (node.route && Vector3.Dot(node.up, gravity.Up) > 0.99f &&
                (anchor == null || SurfaceDistance(node, player.position) < SurfaceDistance(anchor, player.position))) anchor = node;
        if (anchor == null || SurfaceDistance(anchor, player.position) > deckSize) return;
        Quaternion basis = CastleGeometry.Orientation(anchor.up);
        NearbyNode ceiling = null;
        foreach (NearbyNode node in nearbyNodes)
            if (node.route && Vector3.Dot(node.up, -anchor.up) > 0.99f && (node.surface - anchor.surface).magnitude < NearbyWalkRadius &&
                Vector3.Dot(node.surface - anchor.surface, anchor.up) < -deckSize * 0.5f &&
                (ceiling == null || (node.surface - anchor.surface).sqrMagnitude < (ceiling.surface - anchor.surface).sqrMagnitude)) ceiling = node;
        for (int attempt = 0; ceiling == null && attempt < 8 && nearbyPool.Count > 10; attempt++)
        {
            ceiling = TryCreateNearbyNode(anchor.surface - anchor.up * (deckSize + 20f + attempt * 12f), -anchor.up, true);
        }
        for (int i = 0; i < 4 && nearbyPool.Count > 8; i++)
        {
            Vector3 tangent = basis * (i == 0 ? Vector3.right : i == 1 ? Vector3.left : i == 2 ? Vector3.forward : Vector3.back);
            if (HasNearbyGravityFrame(tangent)) continue;
            foreach (NearbyNode existing in nearbyNodes)
                if (existing != anchor && existing != ceiling && Vector3.Dot(existing.up, tangent) > 0.99f &&
                    (existing.surface - anchor.surface).magnitude < deckSize * 2f &&
                    ConnectGravityCorner(anchor, existing, deckSize * 0.35f))
                { if (ceiling != null) ConnectGravityCorner(existing, ceiling, deckSize * 0.35f); break; }
            if (HasNearbyGravityFrame(tangent)) continue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int slot = attempt % 5;
                float stagger = slot == 0 ? 0f : (slot % 2 == 0 ? -1f : 1f) * ((slot + 1) / 2) * (deckSize + 6f);
                Vector3 surface = anchor.surface + tangent * (deckSize * 0.5f + 7f) -
                    anchor.up * (deckSize * 0.5f + 8.5f + attempt / 5 * (deckSize + 6f)) + Vector3.Cross(anchor.up, tangent) * stagger;
                NearbyNode wall = TryCreateNearbyNode(surface, tangent, true);
                if (wall == null) continue;
                if (!ConnectGravityCorner(anchor, wall, deckSize * 0.35f)) { RetireNearbyNode(nearbyNodes.IndexOf(wall)); continue; }
                if (ceiling != null) ConnectGravityCorner(wall, ceiling, deckSize * 0.35f);
                break;
            }
        }
        if (ceiling != null && !ceiling.route) RetireNearbyNode(nearbyNodes.IndexOf(ceiling));
    }
    private bool HasNearbyExit(NearbyNode node, Vector3 direction)
    {
        foreach (NearbyLink connection in nearbyLinks)
        {
            NearbyNode other = connection.a == node ? connection.b : connection.b == node ? connection.a : null;
            if (other != null && Vector3.Dot(Vector3.ProjectOnPlane(other.surface - node.surface, node.up).normalized, direction) > 0.85f) return true;
        }
        return false;
    }
    private NearbyNode FindNearbyNeighbor(NearbyNode node, Vector3 surface)
    {
        foreach (NearbyNode other in nearbyNodes)
            if (other != node && other.route && (other.surface - surface).sqrMagnitude < 100f && CanConnectNearby(node, other)) return other;
        return null;
    }
    private bool CanConnectNearby(NearbyNode a, NearbyNode b)
    {
        if (a == b || a.rebuilding || b.rebuilding || Vector3.Dot(a.up, b.up) < 0.99f || a.piece.IsTranslating || b.piece.IsTranslating) return false;
        float rise = Mathf.Abs(Vector3.Dot(b.surface - a.surface, a.up));
        if (rise > 0.1f && (flightStair == null || Mathf.Abs(rise - flightStair.rise) > 0.1f)) return false;
        Vector3 delta = Quaternion.Inverse(CastleGeometry.Orientation(a.up)) * (b.surface - a.surface);
        float along = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z)), across = Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
        float gap = along - deckSize;
        return across < deckSize * 0.3f && gap >= 1f && gap <= (rise > 0.1f ? flightStair.run : flatStair != null ? flatStair.run : 8f) + 8f;
    }
    private bool ConnectNearby(NearbyNode a, NearbyNode b, bool aUnderside = false)
    {
        if (!nearbyNodes.Contains(a) || !nearbyNodes.Contains(b)) return false;
        foreach (NearbyLink connection in nearbyLinks)
            if ((connection.a == a && connection.b == b) || (connection.a == b && connection.b == a)) return true;
        Vector3 up = aUnderside ? -a.up : a.up;
        Vector3 surface = aUnderside ? a.underside.transform.position : a.surface;
        if (a.rebuilding || b.rebuilding || linkPool.Count == 0 || Vector3.Dot(up, b.up) < 0.99f) return false;
        Link link = linkPool.Dequeue();
        ConfigureLink(link, surface, b.surface, up, CastleGeometry.Orientation(up), a.piece.transform.rotation, b.piece.transform.rotation);
        Bounds clearance = ConnectionClearance(link.bounds, up);
        if (!NearbyConnectionIsClear(clearance, a, b)) { link.root.SetActive(false); linkPool.Enqueue(link); return false; }
        ClearRouteDecor(clearance);
        nearbyLinks.Add(new NearbyLink { a = a, b = b, link = link, reservation = clearance, aUnderside = aUnderside });
        return true;
    }
    private Bounds ConnectionClearance(Bounds bounds, Vector3 up)
    {
        bounds.Encapsulate(new Bounds(bounds.center + up * 4f, bounds.size));
        bounds.Expand(0.1f); return bounds;
    }
    private bool NearbyConnectionIsClear(Bounds bounds, NearbyNode a, NearbyNode b, NearbyLink ignore = null, ICollection<NearbyNode> movingTogether = null)
    {
        if (HitsLandingDistrict(bounds)) return false;
        foreach (NearbyNode node in nearbyNodes)
            if (node != a && node != b && !(movingTogether?.Contains(node) ?? false) && node.piece.ReservedBounds.Intersects(bounds)) return false;
        foreach (NearbyLink other in nearbyLinks)
            if (other != ignore && other.a != a && other.a != b && other.b != a && other.b != b &&
                !BelongsToDistrict(other, movingTogether) && other.reservation.Intersects(bounds)) return false;
        return true;
    }
    private void ClearRouteDecor(Bounds region)
    {
        removeDecor.Clear();
        foreach (var pair in decor) if (pair.Value.ReservedBounds.Intersects(region)) removeDecor.Add(pair.Key);
        foreach (Vector3Int key in removeDecor)
        { InfinityCastleBuilder piece = decor[key]; piece.StopShift(); piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(key); }
    }
    private void ConfigureCornerLanding(Link link, Vector3 start, Vector3 end, Vector3 up, bool turnFace = false)
    {
        if (link.flat != null) link.flat.SetActive(false);
        if (link.flight != null) link.flight.SetActive(false);
        link.ramp.SetActive(false); link.flatCollision.SetActive(false);
        Vector3 delta = end - start;
        if (delta.magnitude < 0.03f) { link.root.SetActive(false); link.bounds = new Bounds(start, Vector3.zero); return; }
        link.root.transform.SetPositionAndRotation((start + end) * 0.5f, Quaternion.LookRotation(delta.normalized, up));
        link.landing.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        link.landing.transform.localScale = new Vector3(1f, 1f, delta.magnitude + 0.02f);
        link.root.SetActive(true);
        link.bounds = ConnectionVisualBounds(link);
        InfiniteCastleGrid zone = link.root.GetComponent<InfiniteCastleGrid>();
        zone.Configure(-up, Vector3.up * 2.3f, new Vector3(3.4f, turnFace ? 6.6f : 5.4f, delta.magnitude + 1f), 1, true, turnFace);
        zone.enabled = enableGravityZones;
    }
    private Vector3 RoofPoint(NearbyNode node, Vector3 point, bool underside = false)
    {
        Transform surface = (underside ? node.underside : node.roof).transform;
        Vector3 local = surface.InverseTransformPoint(point);
        local.x = Mathf.Clamp(local.x, -deckSize * 0.5f + 0.01f, deckSize * 0.5f - 0.01f);
        local.z = Mathf.Clamp(local.z, -deckSize * 0.5f + 0.01f, deckSize * 0.5f - 0.01f);
        local.y = 0f; return surface.TransformPoint(local);
    }
    private static Vector3 CornerTurnStart(Vector3 corner, Vector3 end)
    {
        Vector3 direction = end - corner;
        // The next face must support the capsule centre through the quarter turn.
        return direction.sqrMagnitude < 0.0009f ? corner : corner - direction.normalized * 2f;
    }
    private Vector3 OpenBalconyPoint(NearbyNode node, Vector3 point)
    {
        Transform face = node.roof.transform;
        Vector3 local = face.InverseTransformPoint(point);
        float edge = deckSize * 0.5f - 1.4f;
        local.x = Mathf.Clamp(local.x, -edge, edge);
        local.z = Mathf.Clamp(local.z, -edge, edge);
        local.y = 0f;
        Bounds art = node.artBounds;
        Vector3 min = art.min - face.localPosition, max = art.max - face.localPosition;
        const float clearance = 0.8f;
        if (local.x > min.x - clearance && local.x < max.x + clearance &&
            local.z > min.z - clearance && local.z < max.z + clearance)
        {
            float left = local.x - min.x + clearance, right = max.x + clearance - local.x;
            float back = local.z - min.z + clearance, front = max.z + clearance - local.z;
            float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(back, front));
            if (nearest == left) local.x = min.x - clearance;
            else if (nearest == right) local.x = max.x + clearance;
            else if (nearest == back) local.z = min.z - clearance;
            else local.z = max.z + clearance;
        }
        return face.TransformPoint(local);
    }
    private bool ConnectGravityCorner(NearbyNode a, NearbyNode b, float axisOffset = 0f, bool bUnderside = false)
    {
        foreach (NearbyLink existing in nearbyLinks)
            if (existing.corner && existing.a == a && existing.b == b && existing.bUnderside == bUnderside) return true;
        Vector3 up = bUnderside ? -b.up : b.up;
        Vector3 surface = bUnderside ? b.underside.transform.position : b.surface;
        if (a.rebuilding || b.rebuilding || !enableGravityZones || linkPool.Count < 2 || Mathf.Abs(Vector3.Dot(a.up, up)) > 0.01f) return false;
        Vector3 axis = Vector3.Cross(a.up, up);
        Vector3 corner = a.surface + up * Vector3.Dot(surface - a.surface, up);
        corner += axis * Vector3.Dot((a.surface + surface) * 0.5f - corner, axis);
        corner += axis * axisOffset;
        Link approach = linkPool.Dequeue(), turn = linkPool.Dequeue();
        ConfigureCornerLanding(approach, RoofPoint(a, corner), corner, a.up);
        Vector3 turnEnd = RoofPoint(b, corner, bUnderside);
        ConfigureCornerLanding(turn, CornerTurnStart(corner, turnEnd), turnEnd, up, true);
        Bounds clearance = ConnectionClearance(approach.bounds, a.up);
        clearance.Encapsulate(ConnectionClearance(turn.bounds, up));
        if (!NearbyConnectionIsClear(clearance, a, b))
        { approach.root.SetActive(false); turn.root.SetActive(false); linkPool.Enqueue(approach); linkPool.Enqueue(turn); return false; }
        ClearRouteDecor(clearance);
        a.route = b.route = true;
        nearbyLinks.Add(new NearbyLink { a = a, b = b, link = approach, turnLanding = turn, corner = true, reservation = clearance,
            bUnderside = bUnderside, cornerAxisOffset = axisOffset });
        return true;
    }
    private void DisconnectNearby(NearbyNode node)
    {
        for (int i = nearbyLinks.Count - 1; i >= 0; i--)
        {
            NearbyLink connection = nearbyLinks[i];
            if (connection.a != node && connection.b != node) continue;
            connection.link.root.SetActive(false); linkPool.Enqueue(connection.link); nearbyLinks.RemoveAt(i);
            if (connection.turnLanding != null) { connection.turnLanding.root.SetActive(false); linkPool.Enqueue(connection.turnLanding); }
            NearbyNode other = connection.a == node ? connection.b : connection.a;
            bool linked = false;
            foreach (NearbyLink remaining in nearbyLinks)
                if (remaining.a == other || remaining.b == other) { linked = true; break; }
            if (!linked && !IsRiding(other) && !(HasNearbyLanding && nearbyLanding == other)) other.route = false;
        }
    }
    private bool IsOccupiedRouteDependency(NearbyNode candidate)
    {
        foreach (NearbyLink connection in nearbyLinks)
        {
            NearbyNode other = connection.a == candidate ? connection.b : connection.b == candidate ? connection.a : null;
            if (other == null || !IsRiding(other)) continue;
            int exits = 0;
            foreach (NearbyLink remaining in nearbyLinks) if (remaining.a == other || remaining.b == other) exits++;
            if (exits == 1) return true;
        }
        return false;
    }
    private bool IsRiding(NearbyNode node) => controller != null && controller.Grounded && controller.Motor.GroundHit.collider != null &&
        controller.Motor.GroundHit.collider.transform.IsChildOf(node.piece.transform) && controller.Motor.SupportMotion().sqrMagnitude < 4f;
    private void RetireNearbyNode(int index)
    {
        NearbyNode node = nearbyNodes[index];
        if (node == startingRoof) startingRoof = null;
        if (node == firstJumpRoof) firstJumpRoof = null;
        gravity.LeaveZone(node.zone); gravity.LeaveZone(node.undersideZone); DisconnectNearby(node);
        node.rebuilding = false;
        node.piece.StopShift(); node.piece.gameObject.SetActive(false);
        nearbyPool.Enqueue(node); nearbyNodes.RemoveAt(index);
    }
    private bool UpdateNearbyCastle(float dt, bool forceRebuild)
    {
        nearbyClock += dt;
        bool changed = forceRebuild;
        if (!IsRebuilding) UpdateNearbyMotionSafety();
        for (int i = nearbyNodes.Count - 1; i >= 0; i--)
        {
            NearbyNode node = nearbyNodes[i];
            bool riding = IsRiding(node);
            if (riding && !node.route) { node.route = true; changed = true; }
            Vector3 playerFromSurface = player.position - node.surface;
            bool inside = Vector3.Dot(playerFromSurface, node.up) < -0.05f &&
                node.piece.WorldBounds.Intersects(new Bounds(player.position + gravity.Up, Vector3.one * 1.5f));
            node.movedThisFrame = node.piece.IsAnimating;
            if (node.movedThisFrame) node.piece.Tick(dt, inside && !riding);
            node.surface = node.roof.transform.position;
            if (!node.piece.IsMoving) node.carryingPlayer = false;
            if (node.zone.enabled && OccupiesNearbyZone(node.zone)) gravity.EnterZone(node.zone);
            else gravity.LeaveZone(node.zone);
            if (node.undersideZone.enabled && OccupiesNearbyZone(node.undersideZone)) gravity.EnterZone(node.undersideZone);
            else gravity.LeaveZone(node.undersideZone);
            if (riding && !IsRebuilding && Vector3.Dot(gravity.Up, node.up) > 0.99f) GrowFromNearbyFace(node, false);
            float radius = node.route ? NearbyWalkRadius * 1.1f : nearbyRadius;
            bool tooFar = (node.surface - player.position).sqrMagnitude > radius * radius;
            bool promisedLanding = fallPrepared && HasNearbyLanding && node == nearbyLanding;
            if (node.travelled && !node.piece.IsMoving && nearbyClock > node.finishedAt) node.travelled = false;
            if (!node.rebuilding && !promisedLanding && !riding && !OccupiesNearbyZone(node.zone) && !OccupiesNearbyZone(node.undersideZone) &&
                tooFar && !IsOccupiedRouteDependency(node))
            { RetireNearbyNode(i); changed = true; }
        }
        foreach (NearbyLink connection in nearbyLinks)
        {
            Vector3 midpoint = (connection.ASurface + connection.BSurface) * 0.5f;
            if (connection.a.movedThisFrame || connection.b.movedThisFrame ||
                (!connection.corner && (connection.link.root.transform.position - midpoint).sqrMagnitude > 0.00000001f))
                ConfigureNearbyLink(connection);
            if (!connection.a.piece.IsMoving && !connection.b.piece.IsMoving)
                connection.reservation = NearbyConnectionReservation(connection);
        }
        if (changed || (player.position - nearbyStreamPosition).sqrMagnitude > deckSize * deckSize * 0.12f)
        {
            nearbyStreamPosition = player.position;
            if (nearbyNodes.Count == 0)
            {
                NearbyNode root = TryCreateNearbyNode(player.position - gravity.Up * (maximumBuildingHeight + 8f), gravity.Up, false);
                if (root != null) root.route = true;
            }
            FillNearbyCastle(); changed = true;
        }
        foreach (NearbyLink connection in nearbyLinks)
        {
            RegisterNearbyZone(connection.link);
            if (connection.turnLanding != null) RegisterNearbyZone(connection.turnLanding);
        }
        if (ProcessNearbyLayout()) changed = true;
        if (changed || NearbyMovingCount > 0) RefreshReservations();
        return changed;
    }
    private bool OccupiesNearbyZone(InfiniteCastleGrid zone) => zone.Contains(player.position) || zone.Contains(player.position + player.up * 0.9f);
    private void GrowFromNearbyFace(NearbyNode node, bool underside)
    {
        foreach (NearbyLink connection in nearbyLinks)
            if (!connection.corner && ((connection.a == node && connection.aUnderside == underside) ||
                (connection.b == node && connection.bUnderside == underside))) return;
        if (nearbyClock < node.nextFaceGrowthAt) return;
        Vector3 up = underside ? -node.up : node.up;
        Vector3 face = underside ? node.underside.transform.position : node.surface;
        Quaternion basis = CastleGeometry.Orientation(up);
        // Search different stair rises, flat exits and lateral offsets without exhausting a frame.
        for (int step = 0; step < 4 && nearbyPool.Count > 8; step++)
        {
            int attempt = node.faceGrowthAttempt++;
            int i = attempt % 4, profile = attempt / 4;
            Vector3 tangent = basis * (i == 0 ? Vector3.forward : i == 1 ? Vector3.back : i == 2 ? Vector3.right : Vector3.left);
            bool stairs = profile % 3 != 1 && flightStair != null;
            float run = stairs ? flightStair.run : flatStair != null ? flatStair.run : 8f;
            float rise = stairs ? flightStair.rise * (profile % 3 == 2 ? -1f : 1f) : 0f;
            float gap = 2f + (profile >= 6 ? 4f : 0f);
            float skew = profile >= 3 ? (profile % 2 == 0 ? -4f : 4f) : 0f;
            if (profile >= 9)
            {
                int ring = (profile - 9) / 3 + 1;
                gap += ring * 8f;
                skew = deckSize * ring * 0.4f * (profile % 2 == 0 ? -1f : 1f);
            }
            Vector3 surface = face + tangent * (deckSize + run + gap) + up * rise + Vector3.Cross(up, tangent) * skew;
            NearbyNode next = TryCreateNearbyNode(surface, up, false);
            if (next != null)
            {
                if (ConnectNearby(node, next, underside)) { next.route = true; FillNearbyCastle(); return; }
                RetireNearbyNode(nearbyNodes.IndexOf(next));
            }
            if (node.faceGrowthAttempt < 72) continue;
            node.faceGrowthAttempt = 0; node.nextFaceGrowthAt = nearbyClock + 0.75f;
            break;
        }
    }
    private void RegisterNearbyZone(Link link)
    {
        InfiniteCastleGrid zone = link.root.GetComponent<InfiniteCastleGrid>();
        if (zone.isActiveAndEnabled && OccupiesNearbyZone(zone)) gravity.EnterZone(zone);
        else gravity.LeaveZone(zone);
    }
    private bool TryShiftNearbyBuilding(Vector3 preferredDirection)
    {
        rebuildDirection = preferredDirection;
        ProtectRebuildNeighborhood();
        nearbyCandidates.Clear();
        foreach (NearbyNode node in nearbyNodes)
            if ((!node.still || HasNearbyGravityCorner(node)) && !node.travelled && !node.rebuilding &&
                !node.piece.IsMoving && !rebuildProtected.Contains(node) && NearbyMotionDistance(node) <= NearFieldRadius)
                nearbyCandidates.Add(node);
        nearbyCandidates.Sort((a, b) => NearbyMotionDistance(a).CompareTo(NearbyMotionDistance(b)));
        int sequence = ++nearbyMotionSequence;
        if (rideMovingBuildings)
            foreach (NearbyNode node in nearbyNodes)
                if (!node.travelled && !node.rebuilding && IsSafeRider(node) &&
                    TryBeginNearbyMotion(node, preferredDirection, 5f, sequence, false))
                { RefreshReservations(); return true; }
        foreach (NearbyNode node in nearbyCandidates)
            if (TryBeginNearbyMotion(node, preferredDirection, 5f, sequence, false)) { RefreshReservations(); return true; }
        return false;
    }
    private void ClearNearbyForLanding(Bounds region)
    {
        for (int i = nearbyNodes.Count - 1; i >= 0; i--)
            if (nearbyNodes[i].piece.ReservedBounds.Intersects(region) || NearbyStandingSpace(nearbyNodes[i]).Intersects(region) ||
                NearbyStandingSpace(nearbyNodes[i], true).Intersects(region)) RetireNearbyNode(i);
    }
    private bool PrepareNearbyFallLanding()
    {
        if (controller == null) return false;
        Vector3 up = gravity.Up;
        if (controller.Grounded || Vector3.Dot(up, lastFallUp) < 0.99f) fallPrepared = false;
        lastFallUp = up;
        if (controller.Grounded || controller.VerticalSpeed > -4f) return false;
        if (Physics.Raycast(player.position + up * 0.1f, -up, out RaycastHit closeFloor, 4f, ~0, QueryTriggerInteraction.Ignore) &&
            Vector3.Dot(closeFloor.normal, up) > 0.65f && IsCastleSurface(closeFloor.collider)) return false;
        if (fallPrepared && HasNearbyLanding && Vector3.Dot(nearbyLanding.up, up) > 0.99f)
        {
            Vector3 local = nearbyLanding.roof.transform.InverseTransformPoint(player.position);
            // The player can steer while falling. Keep the promised surface until the trajectory leaves its footprint.
            Vector3 horizontal = Vector3.ProjectOnPlane(playerVelocity, up);
            float time = NearbyFallTime(Mathf.Max(0f, local.y), Mathf.Max(0f, -controller.VerticalSpeed));
            Vector3 arrival = nearbyLanding.roof.transform.InverseTransformPoint(player.position + horizontal * time);
            if (local.y >= -0.2f && local.y < decorSpacing * catchLayersBelow &&
                Mathf.Abs(arrival.x) < deckSize * 0.5f - 1f && Mathf.Abs(arrival.z) < deckSize * 0.5f - 1f) return false;
        }
        float drop = decorSpacing * 1.8f;
        float speed = Mathf.Max(0f, -controller.VerticalSpeed);
        float duration = NearbyFallTime(drop, speed);
        Vector3 target = player.position - up * drop + Vector3.ProjectOnPlane(playerVelocity, up) * duration;
        Bounds region = CastleGeometry.TransformBounds(new Bounds(Vector3.up * (maximumBuildingHeight * 0.5f),
            new Vector3(deckSize * 2f + 10f, maximumBuildingHeight + 12f, deckSize * 2f + 10f)),
            Matrix4x4.TRS(target, CastleGeometry.Orientation(up), Vector3.one));
        foreach (NearbyNode node in nearbyNodes)
            if ((node.piece.ReservedBounds.Intersects(region) || NearbyStandingSpace(node).Intersects(region) ||
                NearbyStandingSpace(node, true).Intersects(region)) &&
                (IsRiding(node) || OccupiesNearbyZone(node.zone) || OccupiesNearbyZone(node.undersideZone))) return false;
        ClearNearbyForLanding(region);
        for (int i = nearbyLinks.Count - 1; i >= 0; i--)
            if (nearbyLinks[i].reservation.Intersects(region))
            {
                if (nearbyLinks[i].turnLanding != null) { nearbyLinks[i].turnLanding.root.SetActive(false); linkPool.Enqueue(nearbyLinks[i].turnLanding); }
                nearbyLinks[i].link.root.SetActive(false); linkPool.Enqueue(nearbyLinks[i].link); nearbyLinks.RemoveAt(i);
            }
        removeDecor.Clear();
        foreach (var pair in decor) if (pair.Value.ReservedBounds.Intersects(region)) removeDecor.Add(pair.Key);
        foreach (Vector3Int key in removeDecor)
        { InfinityCastleBuilder piece = decor[key]; piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(key); }
        nearbyLanding = TryCreateNearbyNode(target, up, true, anchorAtWalkway: true);
        if (nearbyLanding == null) return false;
        nearbyLandingSerial = nearbyLanding.serial;
        nearbyLanding.route = true;
        landingGeneration++; fallPrepared = true;
        RefreshReservations(); Physics.SyncTransforms();
        return true;
    }
    private float NearbyFallTime(float height, float speed)
    {
        float acceleration = Mathf.Max(0.1f, gravity.Strength);
        float toTerminal = Mathf.Max(0f, 45f - speed) / acceleration;
        float acceleratingDrop = speed * toTerminal + 0.5f * acceleration * toTerminal * toTerminal;
        return height > acceleratingDrop ? toTerminal + (height - acceleratingDrop) / 45f :
            (Mathf.Sqrt(speed * speed + 2f * acceleration * height) - speed) / acceleration;
    }
    public bool TryGetNearbyJump(out Vector3 start, out Vector3 end, out InfinityCastleBuilder destination)
    {
        start = end = default; destination = null;
        if (startingRoof == null || firstJumpRoof == null || !nearbyNodes.Contains(startingRoof) || !nearbyNodes.Contains(firstJumpRoof)) return false;
        Vector3 direction = Vector3.ProjectOnPlane(firstJumpRoof.surface - startingRoof.surface, startingRoof.up).normalized;
        start = startingRoof.surface + direction * (deckSize * 0.5f - 0.4f) + startingRoof.up * 0.06f;
        end = firstJumpRoof.surface - direction * (deckSize * 0.5f - 0.6f) + firstJumpRoof.up * 0.06f;
        destination = firstJumpRoof.piece; return true;
    }
    public bool TryGetNearbyGravitySurface(Vector3 up, out Vector3 point, out InfiniteCastleGrid zone)
    {
        NearbyNode nearest = null;
        bool back = false;
        float distance = float.PositiveInfinity;
        foreach (NearbyNode node in nearbyNodes)
        {
            bool underside = false;
            if (Vector3.Dot(node.up, up) < 0.99f) continue;
            Transform face = (underside ? node.underside : node.roof).transform;
            Vector3 local = face.InverseTransformPoint(player.position);
            float candidate = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - deckSize * 0.5f), local.y,
                Mathf.Max(0f, Mathf.Abs(local.z) - deckSize * 0.5f)).magnitude;
            if (candidate < distance) { distance = candidate; nearest = node; back = underside; }
        }
        if (nearest != null) { point = OpenBalconyPoint(nearest, nearest.surface) + up * 0.08f;
            zone = back ? nearest.undersideZone : nearest.zone; return true; }
        point = default; zone = null; return false;
    }
    public bool TryGetNearbyWallApproach(out Vector3 point, out Vector3 direction) => TryGetNearbyWallApproach(out point, out direction, out _);
    public bool TryGetNearbyWallApproach(out Vector3 point, out Vector3 direction, out Vector3 up) =>
        TryGetNearbyWallApproach(out point, out direction, out up, out _);
    public bool TryGetNearbyWallApproach(out Vector3 point, out Vector3 direction, out Vector3 up, out InfiniteCastleGrid wallBase)
    {
        NearbyLink nearest = null;
        foreach (NearbyLink connection in nearbyLinks)
            if (connection.corner && Vector3.Dot(connection.a.up, Vector3.up) > 0.99f &&
                Mathf.Abs(Vector3.Dot(connection.b.up, Vector3.up)) < 0.01f && connection.link.root.activeSelf &&
                (nearest == null || (connection.link.root.transform.position - player.position).sqrMagnitude <
                    (nearest.link.root.transform.position - player.position).sqrMagnitude)) nearest = connection;
        if (nearest == null) { point = direction = up = default; wallBase = null; return false; }
        up = nearest.b.up;
        wallBase = nearest.b.zone;
        direction = nearest.link.root.transform.forward;
        point = nearest.link.root.transform.position - direction * (nearest.link.landing.transform.localScale.z * 0.5f + 1.2f) + Vector3.up * 0.06f;
        return true;
    }
    public bool ValidateNearbyConnectionGravity(out string error)
    {
        foreach (NearbyLink connection in nearbyLinks)
        {
            if (!nearbyNodes.Contains(connection.a) || !nearbyNodes.Contains(connection.b))
            { error = "A connection points to a retired pooled building."; return false; }
            if (connection.corner)
            {
                for (int i = 0; i < 2; i++)
                {
                    Link part = i == 0 ? connection.link : connection.turnLanding;
                    if (!part.root.activeSelf) continue;
                    InfiniteCastleGrid cornerZone = part.root.GetComponent<InfiniteCastleGrid>();
                    Vector3 partUp = part == connection.link ? connection.AUp : connection.BUp;
                    if (Vector3.Dot(cornerZone.gravityDirection, -partUp) > 0.99f &&
                        cornerZone.Contains(part.root.transform.position + partUp * 0.2f)) continue;
                    error = "A moving gravity-corner landing loses its surface gravity."; return false;
                }
                continue;
            }
            InfiniteCastleGrid zone = connection.link.root.GetComponent<InfiniteCastleGrid>();
            Vector3 direction = Vector3.ProjectOnPlane(connection.BSurface - connection.ASurface, connection.AUp).normalized;
            Vector3 a = connection.ASurface + direction * (DeckEdgeDistance(direction, connection.a.piece.transform.rotation) + 0.35f) + connection.AUp * 0.2f;
            Vector3 b = connection.BSurface - direction * (DeckEdgeDistance(direction, connection.b.piece.transform.rotation) + 0.35f) + connection.BUp * 0.2f;
            if (Vector3.Dot(connection.AUp, connection.BUp) < 0.99f ||
                !zone.Contains(a) || !zone.Contains(b) || Vector3.Dot(zone.gravityDirection, -connection.AUp) < 0.99f)
            { error = "A stair connection loses its gravity frame at an entrance."; return false; }
        }
        error = null; return true;
    }
    public bool ValidateArchitectureOrientation(out string error)
    {
        foreach (NearbyLink connection in nearbyLinks)
            if (connection.aUnderside || connection.bUnderside)
            { error = "A path uses a lower-side entrance."; return false; }
        foreach (NearbyNode node in nearbyNodes)
        {
            Bounds art = CastleGeometry.RendererBoundsInRoot(node.piece.transform.GetChild(0));
            Transform model = node.piece.transform.GetChild(0);
            Bounds authored = CastleGeometry.TransformBounds(art, Matrix4x4.TRS(model.localPosition, model.localRotation, model.localScale));
            if (node.underside.gameObject.activeSelf || node.undersideZone.enabled || Vector3.Dot(node.piece.transform.up, node.up) < 0.99f ||
                Vector3.Dot(node.roof.transform.up, node.up) < 0.99f ||
                Mathf.Abs(node.roof.transform.localPosition.y - authored.min.y + 0.02f) > 0.02f ||
                authored.center.y <= node.roof.transform.localPosition.y)
            { error = "Building must rise above its base in its own gravity frame: " + node.piece.name; return false; }
        }
        error = null; return true;
    }
    public bool ValidateNearbyConnectionClearance(out string error)
    {
        foreach (NearbyLink connection in nearbyLinks)
        {
            for (int part = 0; part < (connection.corner ? 2 : 1); part++)
            {
                Link piece = part == 0 ? connection.link : connection.turnLanding;
                if (!piece.root.activeSelf) continue;
                foreach (NearbyNode node in nearbyNodes)
                    if (node != connection.a && node != connection.b && node.piece.WorldBounds.Intersects(piece.bounds))
                    { error = "A connection intersects an unrelated building: " + node.piece.name; return false; }
                foreach (InfinityCastleBuilder building in decor.Values)
                    if (building.WorldBounds.Intersects(piece.bounds))
                    { error = "A connection intersects distant architecture: " + building.name; return false; }
                foreach (NearbyLink other in nearbyLinks)
                {
                    if (other == connection || other.a == connection.a || other.a == connection.b ||
                        other.b == connection.a || other.b == connection.b) continue;
                    if ((other.link.root.activeSelf && other.link.bounds.Intersects(piece.bounds)) ||
                        (other.turnLanding != null && other.turnLanding.root.activeSelf && other.turnLanding.bounds.Intersects(piece.bounds)))
                    { error = "Unrelated connections intersect."; return false; }
                }
            }
        }
        error = null; return true;
    }
    public bool TryGetNearbyCeilingApproach(out Vector3 point)
    {
        NearbyLink nearest = null;
        foreach (NearbyLink connection in nearbyLinks)
            if (connection.corner && Vector3.Dot(connection.a.up, gravity.Up) > 0.99f &&
                Vector3.Dot(connection.b.up, Vector3.down) > 0.99f &&
                Mathf.Abs(Vector3.Dot(player.position - connection.a.surface, connection.a.up)) < 0.5f &&
                (nearest == null || (connection.a.surface - player.position).sqrMagnitude < (nearest.a.surface - player.position).sqrMagnitude)) nearest = connection;
        if (nearest == null) { point = default; return false; }
        Vector3 approach = nearest.turnLanding.root.transform.position + Vector3.up * 2f;
        Transform face = nearest.a.roof.transform;
        Vector3 local = face.InverseTransformPoint(approach);
        float edge = deckSize * 0.5f - 0.8f;
        local.x = Mathf.Clamp(local.x, -edge, edge); local.z = Mathf.Clamp(local.z, -edge, edge); local.y = 0f;
        point = face.TransformPoint(local) + nearest.a.up * 0.06f;
        return true;
    }
    public bool TryGetNearbyWalkConnection(Vector3 preference, out Vector3 start, out Vector3 end)
    {
        NearbyNode current = null;
        bool currentBack = false;
        float nearest = float.PositiveInfinity;
        foreach (NearbyLink connection in nearbyLinks)
        {
            if (connection.corner || Vector3.Dot(connection.AUp, gravity.Up) < 0.99f) continue;
            float a = NearbyFaceDistance(connection.a, connection.aUnderside, player.position);
            float b = NearbyFaceDistance(connection.b, connection.bUnderside, player.position);
            if (a < nearest) { nearest = a; current = connection.a; currentBack = connection.aUnderside; }
            if (b < nearest) { nearest = b; current = connection.b; currentBack = connection.bUnderside; }
        }
        NearbyLink next = null;
        bool fromA = false;
        float best = float.NegativeInfinity;
        if (current != null) foreach (NearbyLink connection in nearbyLinks)
        {
            if (connection.corner) continue;
            bool a = connection.a == current && connection.aUnderside == currentBack;
            bool b = connection.b == current && connection.bUnderside == currentBack;
            if (!a && !b) continue;
            float score = Vector3.Dot((a ? connection.BSurface - connection.ASurface : connection.ASurface - connection.BSurface).normalized, preference.normalized);
            if (score > best) { best = score; next = connection; fromA = a; }
        }
        if (next != null)
        {
            NearbyNode source = fromA ? next.a : next.b, destination = fromA ? next.b : next.a;
            Vector3 direction = Vector3.ProjectOnPlane(destination.surface - source.surface, gravity.Up).normalized;
            start = OpenBalconyPoint(source, source.surface + direction * (DeckEdgeDistance(direction, source.piece.transform.rotation) - 1.4f)) + gravity.Up * 0.06f;
            end = OpenBalconyPoint(destination, destination.surface - direction * (DeckEdgeDistance(direction, destination.piece.transform.rotation) - 1.4f)) + gravity.Up * 0.06f;
            return true;
        }
        start = end = default; return false;
    }
    private float NearbyFaceDistance(NearbyNode node, bool underside, Vector3 point)
    {
        Vector3 local = (underside ? node.underside : node.roof).transform.InverseTransformPoint(point);
        return new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - deckSize * 0.5f), local.y,
            Mathf.Max(0f, Mathf.Abs(local.z) - deckSize * 0.5f)).magnitude;
    }
    public bool ValidateNearbyRouteCoverage(out string error)
    {
        foreach (NearbyNode node in nearbyNodes)
        {
            if (!node.route || SurfaceDistance(node, player.position) > deckSize * 2f) continue;
            int exits = 0;
            foreach (NearbyLink connection in nearbyLinks) if (connection.a == node || connection.b == node) exits++;
            if (exits == 0) { error = "A nearby playable building has no connected exit: " + node.surface; return false; }
        }
        error = null; return true;
    }
}
