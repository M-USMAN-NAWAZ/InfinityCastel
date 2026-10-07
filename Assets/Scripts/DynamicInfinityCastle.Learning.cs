using UnityEngine;

public partial class DynamicInfinityCastle
{
    [Header("Learned Castle Director")]
    public bool useLearnedDirector = true;
    private InfinityCastleMLAgent learnedDirector;
    private readonly CastleDirectorPolicy fallbackPolicy = new();
    private CastleDirectorPolicy LayoutPolicy => learnedDirector != null ? learnedDirector.Policy : fallbackPolicy;
    public bool UsesTrainedDirector => learnedDirector != null && learnedDirector.HasTrainedModel;
    public int LearnedDecisions => learnedDirector != null ? learnedDirector.InferenceDecisions : 0;
    public int LayoutArchetype => LayoutPolicy.Layout;
    public int AttachedBuildingCount { get; private set; }
    public int StairBuildingCount { get; private set; }
    public int JumpBuildingCount { get; private set; }
    public int DistantBuildingCount { get; private set; }

    private void InitializeLearning()
    {
        fallbackPolicy.Reset(randomSeed);
        if (useLearnedDirector && Application.isPlaying) learnedDirector = InfinityCastleMLAgent.Create(transform, false, randomSeed);
    }
    public void ObserveDirector()
    {
        int attached = 0, stairs = 0, jumps = 0, distant = 0, corners = 0;
        foreach (NearbyNode node in nearbyNodes)
        {
            if (node.kind == CastleDirectorPolicy.RouteKind.Attached) attached++;
            else if (node.kind == CastleDirectorPolicy.RouteKind.Jump) jumps++;
            else if (node.kind == CastleDirectorPolicy.RouteKind.Distant) distant++;
            else stairs++;
        }
        foreach (NearbyLink link in nearbyLinks) if (link.corner) corners++;
        AttachedBuildingCount = attached; StairBuildingCount = stairs; JumpBuildingCount = jumps; DistantBuildingCount = distant + decor.Count;
        float total = Mathf.Max(1, attached + stairs + jumps + distant);
        LayoutPolicy.Observe(new Vector4(attached, stairs, jumps, distant) / total,
            Mathf.Clamp01(corners / 8f), controller != null && controller.Grounded, IsRebuilding,
            nearbyNodes.Count / (float)Mathf.Max(1, nearbyPoolSize), playerVelocity, PlayerUp,
            LastRebuildMovedCount / (float)Mathf.Max(1, LastRebuildSelectedCount),
            LastRebuildBlockedCount / (float)Mathf.Max(1, LastRebuildSelectedCount));
    }
    public bool RequestLearnedPlan()
    {
        if (learnedDirector == null) return true;
        if (learnedDirector.PlanReady) { learnedDirector.ConsumePlan(); return true; }
        if (!learnedDirector.Pending) { ObserveDirector(); learnedDirector.RequestPlan(); }
        return false;
    }
    public Vector3 LearnedDirection => CastleGeometry.Orientation(PlayerUp) * directions[LayoutPolicy.Direction];
    public int LearnedMotion => LayoutPolicy.Motion;
    public float LearnedIntervalMultiplier => LayoutPolicy.IntervalMultiplier;
    private bool HasConnectedWalkExit(NearbyNode node) => ConnectedWalkExitCount(node) > 0;
    private int ConnectedWalkExitCount(NearbyNode node)
    {
        int count = 0;
        foreach (NearbyLink link in nearbyLinks) if (!link.corner && (link.a == node || link.b == node)) count++;
        return count;
    }
    private void ConfigureAttachedConnection(NearbyLink connection, Vector3 a, Vector3 b, Quaternion aRotation, Quaternion bRotation)
    {
        Vector3 up = connection.AUp, direction = Vector3.ProjectOnPlane(b - a, up).normalized;
        Vector3 start = a + direction * (DeckEdgeDistance(direction, aRotation) - .01f);
        Vector3 end = b - direction * (DeckEdgeDistance(direction, bRotation) - .01f);
        ConfigureCornerLanding(connection.link, start, end, up);
        // A broad timber seam, not a stair flight, joins adjoining building courts.
        connection.link.landing.transform.localScale = new Vector3(2f, 1f, (end - start).magnitude + .02f);
        connection.link.bounds = ConnectionVisualBounds(connection.link);
        connection.link.root.GetComponent<InfiniteCastleGrid>().Configure(-up, Vector3.up * 2.3f,
            new Vector3(6.4f, 5.4f, (end - start).magnitude + 1f), 1, true);
    }
    private bool ConnectAttached(NearbyNode a, NearbyNode b)
    {
        if (linkPool.Count == 0 || Vector3.Dot(a.up, b.up) < .99f) return false;
        Link link = linkPool.Dequeue();
        NearbyLink connection = new() { a = a, b = b, link = link, attached = true };
        ConfigureAttachedConnection(connection, a.surface, b.surface, a.piece.transform.rotation, b.piece.transform.rotation);
        Bounds room = ConnectionClearance(link.bounds, a.up);
        if (!NearbyConnectionIsClear(room, a, b)) { link.root.SetActive(false); linkPool.Enqueue(link); return false; }
        ClearRouteDecor(room); connection.reservation = room; nearbyLinks.Add(connection); return true;
    }
    public bool ValidateRouteMix(out string error)
    {
        ObserveDirector();
        if (AttachedBuildingCount == 0 || StairBuildingCount == 0 || JumpBuildingCount == 0 || DistantBuildingCount == 0)
        { error = "Missing route category: attached=" + AttachedBuildingCount + ", stairs=" + StairBuildingCount + ", jumps=" + JumpBuildingCount + ", distant=" + DistantBuildingCount; return false; }
        foreach (NearbyNode node in nearbyNodes)
        {
            if (node.kind != CastleDirectorPolicy.RouteKind.Jump) continue;
            NearbyNode source = null;
            foreach (NearbyNode candidate in nearbyNodes) if (candidate.serial == node.parentSerial) { source = candidate; break; }
            if (source == null || node.piece.IsMoving || source.piece.IsMoving || node.travelled || source.travelled) continue;
            foreach (NearbyLink link in nearbyLinks)
                if ((link.a == node && link.b == source) || (link.b == node && link.a == source))
                { error = "An intentional jump gap was filled by a connection."; return false; }
            Vector3 delta = Quaternion.Inverse(CastleGeometry.Orientation(node.up)) * (node.surface - source.surface);
            float gap = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z)) - deckSize;
            if (Vector3.Dot(node.up, source.up) < .99f || Mathf.Abs(delta.y) > .05f || gap < 1f || gap > 2.21f)
            { error = "An optional jump starts outside its reachable same-gravity gap budget."; return false; }
        }
        error = string.Empty; return true;
    }
    public bool ValidateAuthoredNearbyStairs(out string error)
    {
        foreach (NearbyLink link in nearbyLinks)
            if (!link.corner && !link.attached && !FitsNearbyStairs(link.ASurface, link.BSurface, link.AUp,
                link.a.piece.transform.rotation, link.b.piece.transform.rotation))
            { error = "A walking connection cannot fit its authored stair run/rise."; return false; }
        error = string.Empty; return true;
    }
    public bool ValidateNearbyCornerApproaches(out string error)
    {
        foreach (NearbyLink link in nearbyLinks) if (link.corner)
            if (!FitsNearbyCorner(link.ASurface, link.BSurface, link.AUp, link.BUp, link.a.piece.transform.rotation, link.b.piece.transform.rotation))
            { error = "A gravity corner points the capsule back into its incoming balcony."; return false; }
        error = string.Empty; return true;
    }
    public string DescribeLocalConnections()
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine("Authored stairs: run=" + flightStair?.run + ", rise=" + flightStair?.rise + "; flat run=" + flatStair?.run);
        foreach (NearbyLink link in nearbyLinks)
            if (!link.corner && Vector3.Dot(link.AUp, PlayerUp) > .99f &&
                Mathf.Min(SurfaceDistance(link.a, player.position), SurfaceDistance(link.b, player.position)) < deckSize)
                output.AppendLine("a=" + link.ASurface + " b=" + link.BSurface + " attached=" + link.attached +
                    " flat=" + (link.link.flat != null && link.link.flat.activeSelf) + " stairs=" + (link.link.flight != null && link.link.flight.activeSelf) +
                    " moving=" + link.a.piece.IsMoving + "/" + link.b.piece.IsMoving + " zone=" + link.link.root.transform.position);
        return output.ToString();
    }
    public string DescribeRiderReadiness()
    {
        foreach (NearbyNode node in nearbyNodes) if (IsRiding(node))
        {
            int occupiedLinks = 0;
            foreach (NearbyLink link in nearbyLinks)
                if ((link.a == node || link.b == node) && (OccupiesNearbyZone(link.link.root.GetComponent<InfiniteCastleGrid>()) ||
                    (link.turnLanding != null && link.turnLanding.root.activeSelf && OccupiesNearbyZone(link.turnLanding.root.GetComponent<InfiniteCastleGrid>())))) occupiedLinks++;
            return "safe=" + IsSafeRider(node) + ", foot=" + node.roof.transform.InverseTransformPoint(player.position) +
                ", deck=" + deckSize + ", travelled=" + node.travelled + ", moving=" + node.piece.IsMoving + ", occupiedLinks=" + occupiedLinks;
        }
        return "No supporting nearby building";
    }
    public bool TryGetNearbySafeRiderPoint(out Vector3 point)
    {
        foreach (NearbyNode node in nearbyNodes) if (IsRiding(node))
        {
            if (IsSafeRider(node)) { point = player.position; return true; }
            float edge = deckSize * .5f - 2f;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 candidate = OpenBalconyPoint(node, node.roof.transform.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? -edge : edge, 0, (corner & 2) == 0 ? -edge : edge))) + node.up * .08f;
                bool clear = true;
                foreach (NearbyLink link in nearbyLinks)
                {
                    if (link.a != node && link.b != node) continue;
                    InfiniteCastleGrid zone = link.link.root.GetComponent<InfiniteCastleGrid>();
                    InfiniteCastleGrid turn = link.turnLanding != null && link.turnLanding.root.activeSelf ? link.turnLanding.root.GetComponent<InfiniteCastleGrid>() : null;
                    if (zone.Contains(candidate) || zone.Contains(candidate + node.up * .9f) ||
                        (turn != null && (turn.Contains(candidate) || turn.Contains(candidate + node.up * .9f)))) { clear = false; break; }
                }
                if (clear) { point = candidate; return true; }
            }
            break;
        }
        point = default; return false;
    }
    public bool TryGetNearbyJumpRoute(out Vector3 start, out Vector3 end, out InfiniteCastleGrid source, out Transform destination)
    {
        foreach (NearbyNode node in nearbyNodes)
        {
            if (node.kind != CastleDirectorPolicy.RouteKind.Jump || node.piece.IsMoving || Vector3.Dot(node.up, PlayerUp) < .99f) continue;
            foreach (NearbyNode parent in nearbyNodes)
            {
                if (parent.serial != node.parentSerial || parent.piece.IsMoving) continue;
                Vector3 direction = Vector3.ProjectOnPlane(node.surface - parent.surface, node.up).normalized;
                start = parent.surface + direction * (DeckEdgeDistance(direction, parent.piece.transform.rotation) - .35f) + parent.up * .08f;
                end = node.surface - direction * (DeckEdgeDistance(direction, node.piece.transform.rotation) - .35f) + node.up * .08f;
                source = parent.zone; destination = node.piece.transform; return true;
            }
        }
        start = end = default; source = null; destination = null; return false;
    }
}
