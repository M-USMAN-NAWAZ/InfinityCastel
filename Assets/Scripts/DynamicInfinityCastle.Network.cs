using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private readonly Stack<IEnumerator> castleWork = new();
    private bool loadingControllerWasEnabled;
    private bool startupControlsHeld;
    private Vector3 startupSafePosition, startupSafeUp;
    private Quaternion startupSafeRotation;
    public bool CastleWorkPending => castleWork.Count > 0;
    public double MaxCastleWorkSliceMilliseconds { get; private set; }
    public double MaxCastleWorkUnitMilliseconds { get; private set; }
    public double MaxStreamingWorkSliceMilliseconds { get; private set; }
    public string MaxCastleWorkUnitName { get; private set; }
    public int CastleWorkFrames { get; private set; }

    public const float CastleVisibleRadius = 192f;
    public const float CastleFadeStart = 160f;
    // The shader makes the outer preparation band invisible in every camera direction.
    private bool CanRecycleCastleBounds(Bounds bounds)
    {
        // Keep the whole visible sphere resident, including behind the camera during a fast turn.
        return bounds.SqrDistance(player.position) > (CastleVisibleRadius + 8f) * (CastleVisibleRadius + 8f);
    }

    private void ScheduleCastleWork(IEnumerator steps)
    {
        if (CastleWorkPending) return;
        castleWork.Push(steps);
        if (!Application.isPlaying) AdvanceCastleWork(true);
    }

    private void AdvanceCastleWork(bool drain)
    {
        if (!CastleWorkPending) return;
        double start = Time.realtimeSinceStartupAsDouble;
        bool streaming = initialized;
        // Loading holds the controls already; a 1 ms streaming budget can otherwise take minutes at 30 fps.
        double budgetMilliseconds = streaming ? layoutMillisecondsPerFrame : Mathf.Max(8f, layoutMillisecondsPerFrame);
        CastleWorkFrames++;
        try
        {
            do
            {
                IEnumerator next = castleWork.Peek();
                double unit = drain ? 0d : Time.realtimeSinceStartupAsDouble;
                if (!next.MoveNext()) { (castleWork.Pop() as System.IDisposable)?.Dispose(); }
                else if (next.Current is IEnumerator child) castleWork.Push(child);
                if (!drain)
                {
                    double elapsed = (Time.realtimeSinceStartupAsDouble - unit) * 1000d;
                    if (elapsed > MaxCastleWorkUnitMilliseconds)
                    { MaxCastleWorkUnitMilliseconds = elapsed; MaxCastleWorkUnitName = next.GetType().Name; }
                }
            }
            while (CastleWorkPending && (drain || (Time.realtimeSinceStartupAsDouble - start) * 1000d < budgetMilliseconds));
        }
        catch { ClearCastleWork(); RestoreStartupControls(); throw; }
        if (!drain)
        {
            double elapsed = (Time.realtimeSinceStartupAsDouble - start) * 1000d;
            MaxCastleWorkSliceMilliseconds = System.Math.Max(MaxCastleWorkSliceMilliseconds, elapsed);
            if (streaming) MaxStreamingWorkSliceMilliseconds = System.Math.Max(MaxStreamingWorkSliceMilliseconds, elapsed);
        }
    }

    private void ClearCastleWork()
    {
        while (castleWork.Count > 0) (castleWork.Pop() as System.IDisposable)?.Dispose();
    }

    private void RestoreStartupControls()
    {
        if (!startupControlsHeld) return;
        if (controller != null) controller.enabled = loadingControllerWasEnabled;
        startupControlsHeld = false;
    }

    private void OnDisable()
    {
        RestoreStartupControls();
        CancelTimberAssemblies();
    }

    private void UpdateStartupSafety()
    {
        if (!IsPlayerReady || controller == null) return;
        if (controller.IsUltraJumping) return;
        if (controller.Grounded && controller.Motor.GroundHit.collider != null && IsCastleSurface(controller.Motor.GroundHit.collider))
        {
            startupSafePosition = player.position + gravity.Up * .04f;
            startupSafeUp = gravity.Up; startupSafeRotation = player.rotation;
        }
        // Until the fall-recovery pool is warm, return an unsupported fall to the last real floor.
        else if (controller.VerticalSpeed < -4f && Vector3.Dot(player.position - startupSafePosition, startupSafeUp) < -18f &&
            !HasBuildingFloorBelow(180f))
        {
            player.SetPositionAndRotation(startupSafePosition, startupSafeRotation);
            gravity.SetGravity(-startupSafeUp, 22f, true, 8f);
            controller.ResetFallVelocity(); Physics.SyncTransforms();
        }
    }

    private void OnGUI()
    {
        if (initialized || !CastleWorkPending || !BuildingTraversalEnabled) { DrawUltraJumpHint(); return; }
        // Keep the live construction visible instead of dimming the whole scene behind a loading overlay.
        GUI.Box(new Rect(12f, 12f, Mathf.Min(470f, Screen.width - 24f), 76f), "");
        GUI.Label(new Rect(24f, 20f, Mathf.Min(450f, Screen.width - 48f), 68f),
            $"Preparing the surrounding castle…\nBuildings: {buildingWings.Count}/{architectureWingPoolSize}; surrounding rooms: {DenseCastleRoomCount}\nControls unlock when the initial layers are ready.");
    }

    private struct CastleFrontier
    {
        public BuildingWing wing;
        public BuildingPart part;
        public Vector3 outward, point;
        public float score;
    }

    private readonly List<CastleFrontier> castleFrontier = new();
    private readonly HashSet<BuildingWing> connectedBuildings = new();
    private readonly Queue<BuildingWing> networkSearch = new();
    private readonly int[] networkRejects = new int[6];
    private int networkFrontierAttempt;
    public int BuildingNetworkLoopCount => Mathf.Max(0, buildingJoins.Count - BuildingWingCount + 1);

    private IEnumerator BuildCastleNetwork()
    {
        double started = Time.realtimeSinceStartupAsDouble;
        // Grow complete, differently proportioned buildings through real open doorways.
        // The bounded pool is the currently loaded part of an expanding castle, not its edge.
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 1; i < buildingWings.Count - ArchitectureLayerReserveCount; i++)
            {
                BuildingWing wing = buildingWings[i];
                if (wing.active) continue;
                Vector3 target = NetworkTarget(i + pass * 17, player.position, 20f + Mathf.Sqrt(i) * 3f);
                if (i > buildingWings.Count / 3 && pass == 0) target = FindCastleGap(i, player.position + Vector3.up * 1.8f);
                yield return GrowCastleNetwork(wing, target);
            }
        }
        yield return AddNetworkLoops(mobileBudget ? 16 : 28);
        RefreshNativeDoorways();
        Physics.SyncTransforms();
        Debug.Log($"Castle network: {BuildingWingCount} full buildings, {BuildingRoomCount} room/hall/stair modules, " +
            $"{buildingJoins.Count} physical crossings, {BuildingNetworkLoopCount} alternative-route loops. " +
            $"Placement checks (template/port/entry/overlap/door/passage): {string.Join(",", networkRejects)}; " +
            $"generation={Time.realtimeSinceStartupAsDouble - started:F2}s", this);
    }

    private Vector3 FindCastleGap(int serial, Vector3 observer)
    {
        Physics.SyncTransforms();
        float best = -1f; Vector3 direction = Vector3.up;
        // Fill open sightlines with more complete buildings, never a backdrop or enclosing wall.
        for (int i = 0; i < 72; i++)
        {
            int sample = (i + serial * 19) % 72;
            float y = 1f - 2f * (sample + .5f) / 72f, angle = sample * 2.399963f;
            float radius = Mathf.Sqrt(1f - y * y);
            Vector3 ray = new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            float distance = Physics.Raycast(observer, ray, out RaycastHit hit, 130f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 130f;
            if (distance > best) { best = distance; direction = ray; }
        }
        return observer + direction * Mathf.Clamp(best * .5f, 22f, 52f);
    }

    private Vector3 NetworkTarget(int serial, Vector3 centre, float radius)
    {
        Vector3 axis = directions[(serial + LayoutPolicy.Direction) % directions.Length];
        Vector3Int key = new(serial, LayoutPolicy.Layout, architectureStreamRevision);
        Vector3 scatter = new(Hash(key, 601) - .5f, Hash(key, 602) - .5f, Hash(key, 603) - .5f);
        return centre + (axis + scatter * .9f) * radius;
    }

    private IEnumerator GatherCastleFrontier(Vector3 target)
    {
        castleFrontier.Clear();
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.piece.IsMoving || (initialized && !wing.physicsNear) || wing.serial >= architectureWingPoolSize - ArchitectureLayerReserveCount) continue;
            Matrix4x4 world = wing.piece.transform.localToWorldMatrix;
            Quaternion rotation = wing.piece.transform.rotation;
            foreach (BuildingEntry entry in wing.entries)
            {
                    Vector3 point = world.MultiplyPoint3x4(entry.rootPoint);
                    if (!BuildingPortFree(wing, point)) continue;
                    Vector3 normal = rotation * entry.part.rootRotation * entry.port.outward;
                    castleFrontier.Add(new CastleFrontier { wing = wing, part = entry.part, outward = entry.port.outward, point = point,
                        score = (point + normal * 11f - target).sqrMagnitude });
            }
            yield return null;
        }
        castleFrontier.Sort((a, b) => a.score.CompareTo(b.score));
    }

    private IEnumerator GrowCastleNetwork(BuildingWing wing, Vector3 target)
    {
        yield return GatherCastleFrontier(target);
        // ponytail: bounded frontier search; use a spatial index if the loaded pool exceeds 160 buildings.
        int tries = Mathf.Min(castleFrontier.Count, initialized ? 1 : 64);
        for (int i = 0; i < tries; i++)
        {
            CastleFrontier frontier = castleFrontier[initialized ? networkFrontierAttempt++ % Mathf.Min(64, castleFrontier.Count) : i];
            foreach (bool attached in AttachBuildingWingSteps(frontier.wing, frontier.part, frontier.outward, true, preferred: wing))
            {
                if (attached) yield break;
                yield return null;
            }
        }
    }

    private bool NetworkPassageFree(Vector3 start, Vector3 end, Vector3 up, BuildingWing source, BuildingWing target)
    {
        Bounds passage = CastleGeometry.TransformBounds(new Bounds(Vector3.zero, new Vector3(1.3f, 2.2f, Vector3.Distance(start, end))),
            Matrix4x4.TRS((start + end) * .5f + up * 1.15f, Quaternion.LookRotation(end - start, up), Vector3.one));
        foreach (Bounds corridor in gravityLeapClearance) if (passage.Intersects(corridor)) return false;
        foreach (BuildingWing other in buildingWings)
        {
            if (!other.active || other == source || other == target || !other.bounds.Intersects(passage)) continue;
            foreach (BuildingPart part in other.parts)
            {
                if (part.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                Bounds bounds = part.bounds; bounds.Expand(-.3f);
                if (bounds.Intersects(passage)) return false;
            }
        }
        foreach (BuildingJoin join in buildingJoins)
            if (join.stair != null && join.stair.bounds.Intersects(passage)) return false;
        return true;
    }

    private void ConfigureNetworkBridge(Link link, Vector3 start, Vector3 end, Vector3 up)
    {
        if (!link.root.activeSelf) ResetTimberLink(link);
        Vector3 delta = end - start;
        Vector3 along = delta.normalized;
        Quaternion facing = Quaternion.LookRotation(along, up);
        Vector3 normal = facing * Vector3.up;
        start -= along * .22f; end += along * .22f;
        float length = Vector3.Distance(start, end);
        link.root.transform.SetPositionAndRotation((start + end) * .5f, facing);
        link.root.transform.localScale = Vector3.one;
        link.ramp.SetActive(false);
        if (link.flight != null) link.flight.SetActive(false);
        if (link.landing != null) link.landing.gameObject.SetActive(false);
        Vector3 authored = Vector3.ProjectOnPlane(flatStair.high - flatStair.low, Vector3.up).normalized;
        float width = Mathf.Clamp(flatStair.width, 1.4f, 2.1f);
        Vector3 scale = Mathf.Abs(authored.z) > .5f ? new Vector3(width / flatStair.width, 1f, length / flatStair.run) :
            new Vector3(length / flatStair.run, 1f, width / flatStair.width);
        Quaternion rotation = facing * Quaternion.Inverse(Quaternion.LookRotation(authored, Vector3.up));
        link.flat.transform.localScale = scale;
        link.flat.transform.rotation = rotation;
        link.flat.transform.position = start - rotation * Vector3.Scale(flatStair.low, scale);
        link.flat.SetActive(true);
        link.flatCollision.transform.SetPositionAndRotation((start + end) * .5f, facing);
        BoxCollider floor = link.flatCollision.GetComponent<BoxCollider>();
        floor.center = Vector3.down * .1f; floor.size = new Vector3(width - .1f, .2f, length + .08f);
        link.flatCollision.SetActive(true);
        for (int side = 0; side < link.rails.Length; side++)
        {
            BoxCollider rail = link.rails[side];
            rail.transform.SetPositionAndRotation((start + end) * .5f + facing * Vector3.right * ((side == 0 ? -1f : 1f) * (width - .1f) * .5f) + normal * .5f, facing);
            rail.size = new Vector3(.1f, 1f, length);
            rail.gameObject.SetActive(true);
        }
        link.root.SetActive(true);
        link.bounds = ConnectionVisualBounds(link);
        InfiniteCastleGrid zone = link.root.GetComponent<InfiniteCastleGrid>();
        zone.Configure(-up, link.root.transform.InverseTransformPoint((start + end) * .5f + up * 1.5f),
            new Vector3(width + .4f, 4f, length + .8f), 1);
        zone.enabled = enableGravityZones;
    }

    private static Vector3 NetworkDoorOutward(CastleArchitectureModule module, Vector3 entry)
    {
        float nearest = float.PositiveInfinity; Vector3 outward = Vector3.forward;
        foreach (var port in module.ports)
        {
            float distance = Vector3.ProjectOnPlane(port.position - entry, Vector3.up).sqrMagnitude;
            if (distance < nearest) { nearest = distance; outward = port.outward; }
        }
        return module.transform.TransformDirection(outward);
    }

    private static bool NetworkShiftClear(BuildingWing moving, BuildingWing other, Vector3 offset)
    {
        foreach (BuildingPart a in moving.parts)
        {
            if (a.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
            Bounds sweep = a.bounds; sweep.Expand(-.45f); sweep = CastleGeometry.Sweep(sweep, offset, .05f);
            foreach (BuildingPart b in other.parts)
            {
                if (b.module.kind == CastleArchitectureModule.ModuleKind.Roof) continue;
                Bounds bounds = b.bounds; bounds.Expand(-.45f);
                if (sweep.Intersects(bounds)) return false;
            }
        }
        return true;
    }

    private IEnumerator AddNetworkLoops(int maximum)
    {
        int added = 0;
        yield return GatherCastleFrontier(player.position);
        var used = new HashSet<int>();
        for (int i = 0; i < castleFrontier.Count && added < maximum && linkPool.Count > 0; i++)
        {
            CastleFrontier a = castleFrontier[i];
            if (used.Contains(i)) continue;
            Vector3 up = a.part.module.transform.up;
            Vector3 outward = a.part.module.transform.TransformDirection(a.outward);
            for (int j = i + 1; j < castleFrontier.Count; j++)
            {
                if ((j & 63) == 0) yield return null;
                CastleFrontier b = castleFrontier[j];
                if (a.wing == b.wing || used.Contains(j)) continue;
                Vector3 delta = b.point - a.point;
                float length = delta.magnitude;
                if (length < 2f || length > 30f || Mathf.Abs(Vector3.Dot(delta, up)) > length * .3f ||
                    Vector3.Dot(up, b.part.module.transform.up) < .99f || Vector3.Dot(delta.normalized, outward) < .94f ||
                    Vector3.Dot(-delta.normalized, b.part.module.transform.TransformDirection(b.outward)) < .94f) continue;
                if (!BuildingPassageClear(a.wing, b.wing, a.point, b.point, delta.normalized, up) ||
                    !NetworkPassageFree(a.point, b.point, up, a.wing, b.wing)) continue;
                Link bridge = linkPool.Dequeue(); ConfigureNetworkBridge(bridge, a.point, b.point, up);
                buildingJoins.Add(new BuildingJoin { a = a.wing, b = b.wing, from = a.part.module, to = b.part.module,
                    exit = a.part.module.transform.InverseTransformPoint(a.point), entry = b.part.module.transform.InverseTransformPoint(b.point), stair = bridge });
                nativeDoorsDirty = true;
                added++;
                used.Add(i); used.Add(j);
                break;
            }
        }
    }

    private void StreamBuildingVolume(float dt)
    {
        if (CastleWorkPending) return;
        architectureStreamClock -= dt;
        if (architectureStreamClock > 0f && (player.position - districtPhysicsPosition).sqrMagnitude < 8f * 8f) return;
        bool falling = Vector3.Dot(playerVelocity, PlayerUp) < -8f;
        architectureStreamClock = falling || playerVelocity.sqrMagnitude > 100f ? .1f : .3f;
        ScheduleCastleWork(StreamCastleSteps());
    }

    private IEnumerator StreamCastleSteps()
    {
        bool falling = Vector3.Dot(playerVelocity, PlayerUp) < -8f;
        UpdateNetworkPhysics();
        yield return UpdateDenseDistricts();
        // Expensive door matching must not delay the next high-speed volume update.
        if (playerVelocity.sqrMagnitude > 100f) yield break;
        BuildingWing next = AvailableBuildingWing();
        if (next == null)
        {
            int nearby = 0;
            foreach (BuildingWing wing in buildingWings) if (wing.active && wing.bounds.SqrDistance(player.position) < 35f * 35f) nearby++;
            if (nearby >= 26) yield break;
            float farthest = 58f * 58f;
            foreach (BuildingWing wing in buildingWings)
            {
                if (!wing.active || wing.streamingLayer || wing.serial >= buildingWings.Count - ArchitectureLayerReserveCount || wing.piece.IsMoving || IsBuildingSupport(wing) || IsUltraJumpTarget(wing)) continue;
                int degree = 0;
                foreach (BuildingJoin join in buildingJoins) if (join.a == wing || join.b == wing) degree++;
                float distance = (wing.piece.WorldBounds.ClosestPoint(player.position) - player.position).sqrMagnitude;
                if (degree != 1 || distance < farthest || !CanRecycleBuildingWing(wing)) continue;
                next = wing; farthest = distance;
            }
            if (next != null)
            {
                RetireBuildingWing(next); next.revision++;
                Vector3Int key = new(next.serial, next.revision, LayoutPolicy.Layout);
                next.content.localScale = new Vector3(Mathf.Lerp(.8f, 1.48f, Hash(key, 711)),
                    Mathf.Lerp(.95f, 1.3f, Hash(key, 712)), Mathf.Lerp(.8f, 1.55f, Hash(key, 713)));
                next.localBounds = CastleGeometry.RendererBoundsInRoot(next.piece.transform);
                next.piece.RefreshGeometryBounds(); CacheBuildingTemplate(next); CacheBuildingBounds(next);
            }
        }
        if (next != null)
        {
            ++architectureStreamRevision;
            Vector3 target = falling ? player.position - PlayerUp * 38f : playerVelocity.sqrMagnitude > 4f ?
                player.position + playerVelocity.normalized * 38f : NetworkTarget(architectureStreamRevision, player.position, 34f);
            yield return GrowCastleNetwork(next, target);
            if (next.active) { Physics.SyncTransforms(); RefreshReservations(); }
        }
    }

    private void UpdateNetworkPhysics()
    {
        foreach (BuildingWing wing in buildingWings)
            if (wing.active)
            {
                float distance = wing.bounds.SqrDistance(player.position);
                wing.piece.SetRendering(distance < (CastleVisibleRadius + 8f) * (CastleVisibleRadius + 8f));
                if (mobileBudget) SetBuildingPhysics(wing, distance < 90f * 90f || wing.piece.IsMoving || IsUltraJumpTarget(wing));
            }
    }

    private void SetBuildingPhysics(BuildingWing wing, bool near)
    {
        if (wing.physicsNear == near || wing.collisionEnabled == null) return;
        wing.physicsNear = near;
        for (int i = 0; i < wing.geometry.Length; i++) wing.geometry[i].enabled = near && wing.collisionEnabled[i];
        UpdateBuildingZones(wing);
        ApplyDoorwayColliders(wing);
    }

    private bool ValidateCastleNetwork(out string error)
    {
        connectedBuildings.Clear(); networkSearch.Clear();
        if (buildingWings.Count == 0) { error = "No castle buildings."; return false; }
        BuildingWing start = buildingWings.Find(wing => wing.active && wing.serial < buildingWings.Count - ArchitectureLayerReserveCount);
        if (start == null) { error = "No active castle network."; return false; }
        connectedBuildings.Add(start); networkSearch.Enqueue(start);
        while (networkSearch.Count > 0)
        {
            BuildingWing wing = networkSearch.Dequeue();
            foreach (BuildingJoin join in buildingJoins)
            {
                BuildingWing other = join.a == wing ? join.b : join.b == wing ? join.a : null;
                if (other != null && connectedBuildings.Add(other)) networkSearch.Enqueue(other);
            }
        }
        int expected = 0; int[] sides = new int[6];
        Vector3 anchor = player.position;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.streamingLayer || wing.serial >= buildingWings.Count - ArchitectureLayerReserveCount) continue;
            expected++;
            if (!connectedBuildings.Contains(wing))
            {
                if (PreparedCastleLayerGenerations > 2) return ValidateDistributedDistricts(out error);
                error = "Unconnected building " + wing.serial; return false;
            }
            foreach (BuildingPart part in wing.parts)
            {
                Vector3 delta = part.module.WorldFloor - anchor;
                for (int i = 0; i < 6; i++) if (Vector3.Dot(delta, directions[i]) > 10f) sides[i]++;
            }
        }
        for (int i = 0; i < 6; i++) if (sides[i] < 4)
        { error = "Too little actual architecture toward " + directions[i] + ": " + sides[i]; return false; }
        error = $"Connected network: {expected} buildings / {BuildingRoomCount} modules / {buildingJoins.Count} crossings; " +
            "six-side module coverage=" + string.Join(",", sides);
        return expected >= architectureWingPoolSize * .85f || ValidateDistributedDistricts(out error);
    }
}
