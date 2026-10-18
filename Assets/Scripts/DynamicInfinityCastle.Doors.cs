using UnityEngine;

public partial class DynamicInfinityCastle
{
    [Header("Blocked Doorways")]
    public Mesh closedShojiMesh;
    public Material closedShojiMaterial;
    private DistrictDraw nativeDoorDraw;
    private readonly System.Collections.Generic.List<DistrictDraw> nativeDoorDraws = new(1);
    private bool nativeDoorsDirty = true;
    public int ClosedNativeDoorCount { get; private set; }

    private static bool HasDoorways(BuildingPart part) => part.module.kind == CastleArchitectureModule.ModuleKind.Room ||
        part.module.kind == CastleArchitectureModule.ModuleKind.Corridor;

    private void PrepareDoorwayClosures(BuildingWing wing)
    {
        foreach (BuildingPart part in wing.parts)
        {
            if (!HasDoorways(part)) continue;
            part.closedDoors = new BoxCollider[part.module.ports.Length];
            for (int i = 0; i < part.closedDoors.Length; i++)
            {
                var port = part.module.ports[i];
                BoxCollider door = part.module.gameObject.AddComponent<BoxCollider>();
                door.center = port.position - port.outward * .06f + Vector3.up * 1.15f;
                door.size = Mathf.Abs(port.outward.x) > .5f ? new Vector3(.14f, 2.3f, port.width) : new Vector3(port.width, 2.3f, .14f);
                door.enabled = false; part.closedDoors[i] = door;
            }
        }
        // These are deliberately outside wing.geometry: a closed door must not reject a future safe connection.
        nativeDoorsDirty = true;
    }

    private static void ApplyDoorwayColliders(BuildingWing wing)
    {
        foreach (BuildingPart part in wing.parts)
        {
            if (part.closedDoors == null) continue;
            for (int i = 0; i < part.closedDoors.Length; i++)
                part.closedDoors[i].enabled = wing.physicsNear && (part.openDoorMask & (1 << i)) == 0;
        }
    }

    private void RefreshNativeDoorways()
    {
        ClosedNativeDoorCount = 0;
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active) continue;
            foreach (BuildingPart part in wing.parts)
            {
                if (part.closedDoors == null) continue;
                part.openDoorMask = 0;
                for (int i = 0; i < part.module.ports.Length; i++)
                {
                    Vector3 local = part.rootFromPart.MultiplyPoint3x4(part.module.ports[i].position);
                    bool connected = false;
                    foreach (Vector3 linked in wing.internalPorts)
                        if ((linked - local).sqrMagnitude < .12f) { connected = true; break; }
                    if (!connected)
                    {
                        Vector3 world = part.module.transform.TransformPoint(part.module.ports[i].position);
                        foreach (BuildingJoin join in buildingJoins)
                        {
                            if ((join.from == part.module && (join.from.transform.TransformPoint(join.exit) - world).sqrMagnitude < .12f) ||
                                (join.to == part.module && (join.to.transform.TransformPoint(join.entry) - world).sqrMagnitude < .12f))
                            { connected = true; break; }
                        }
                    }
                    if (connected) part.openDoorMask |= 1 << i;
                    else ClosedNativeDoorCount++;
                }
            }
            ApplyDoorwayColliders(wing);
        }
        nativeDoorsDirty = false;
    }

    private void DrawNativeDoorways(Camera camera)
    {
        if (closedShojiMesh == null || closedShojiMaterial == null) return;
        if (nativeDoorsDirty) RefreshNativeDoorways();
        if (nativeDoorDraw == null)
        {
            nativeDoorDraw = new DistrictDraw { mesh = closedShojiMesh, material = PooledMaterial(closedShojiMaterial) };
            nativeDoorDraws.Add(nativeDoorDraw);
        }
        nativeDoorDraw.instances.Clear(); nativeDoorDraw.construction.Clear();
        GeometryUtility.CalculateFrustumPlanes(camera, districtCameraPlanes);
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.bounds.SqrDistance(player.position) > CastleVisibleRadius * CastleVisibleRadius ||
                !GeometryUtility.TestPlanesAABB(districtCameraPlanes, wing.bounds)) continue;
            foreach (BuildingPart part in wing.parts)
            {
                if (part.closedDoors == null || !GeometryUtility.TestPlanesAABB(districtCameraPlanes, part.bounds)) continue;
                for (int i = 0; i < part.closedDoors.Length; i++)
                {
                    if ((part.openDoorMask & (1 << i)) != 0) continue;
                    var port = part.module.ports[i];
                    Matrix4x4 local = Matrix4x4.TRS(part.closedDoors[i].center, Quaternion.LookRotation(port.outward), new Vector3(port.width, 2.3f, .14f));
                    nativeDoorDraw.instances.Add(part.module.transform.localToWorldMatrix * local);
                    nativeDoorDraw.construction.Add(part.construction);
                }
            }
        }
        DrawDistrictInstances(nativeDoorDraws, camera);
    }

    public bool ValidateDoorwayClosures(out string result)
    {
        RefreshNativeDoorways();
        int open = 0, closed = 0;
        if (closedShojiMesh == null || closedShojiMaterial == null)
        { result = "Missing visible shoji closure assets."; return false; }
        foreach (BuildingWing wing in buildingWings) foreach (BuildingPart part in wing.parts)
        {
            if (!wing.active || part.closedDoors == null) continue;
            for (int i = 0; i < part.closedDoors.Length; i++)
            {
                bool connected = !BuildingPortFree(wing, part.module.transform.TransformPoint(part.module.ports[i].position));
                bool blocked = (part.openDoorMask & (1 << i)) == 0;
                if (connected == blocked || part.closedDoors[i].enabled != (blocked && wing.physicsNear))
                { result = "Incorrect door state on " + part.module.name + ", port " + i; return false; }
                if (blocked) closed++; else open++;
            }
        }
        result = $"Native doorways: {open} connected openings, {closed} visibly closed shoji panels; no unguarded exits.";
        return open > 0 && closed == ClosedNativeDoorCount;
    }
}
