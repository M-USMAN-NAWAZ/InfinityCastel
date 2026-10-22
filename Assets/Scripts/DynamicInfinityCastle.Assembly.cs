using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class DynamicInfinityCastle
{
    [System.Serializable] public sealed class TimberAssemblyModel { public Mesh source, fragments; }
    [Header("Timber Construction")]
    public bool animateTimberConstruction = true;
    [Range(.6f, 3f)] public float timberConstructionSeconds = 1.8f;
    public List<TimberAssemblyModel> timberAssemblyModels = new();
    private static readonly int TimberBuildId = Shader.PropertyToID("_CastleBuild");
    private static readonly int TimberInstanceBuildId = Shader.PropertyToID("_CastleInstanceBuild");
    private MaterialPropertyBlock timberProperties;
    // Must match the shader's forcemaxcount; density is unchanged, only draw splitting changes.
    private const int CastleInstanceBatchSize = 64;
    private readonly Vector4[] timberBatch = new Vector4[CastleInstanceBatchSize];
    private readonly Matrix4x4[] timberMatrices = new Matrix4x4[CastleInstanceBatchSize];
    private readonly Plane[] timberPlanes = new Plane[6];
    private readonly List<Link> timberLinks = new();
    private bool timberHooked;
    private float nextTimberScan;
    private int timberSerial;
    public int ActiveTimberAssemblyCount { get; private set; }
    public int TimberConstructionStartCount { get; private set; }
    public int StartupTimberConstructionCount { get; private set; }
    private static readonly Vector4 PendingTimber = new(0f, 0f, 0f, 2f);

    public Mesh FindTimberAssemblyMesh(Mesh source)
    {
        foreach (var model in timberAssemblyModels) if (model.source == source) return model.fragments;
        return null;
    }
    private void InitializeTimberAssemblies()
    {
        if (timberHooked) return;
        timberHooked = true;
        RenderPipelineManager.beginCameraRendering += RenderDenseDistricts;
    }
    private void PrepareTimberRenderers(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null) continue;
            Mesh pieces = FindTimberAssemblyMesh(filter.sharedMesh);
            if (pieces != null) filter.sharedMesh = pieces; // Visuals only: never replace the collider mesh.
        }
    }
    private void SetTimberProperties(Renderer[] renderers, Vector4 state)
    {
        if (renderers.Length == 0) return;
        timberProperties ??= new MaterialPropertyBlock();
        timberProperties.Clear(); timberProperties.SetVector(TimberBuildId, state);
        timberProperties.SetVector(TimberInstanceBuildId, state);
        foreach (Renderer renderer in renderers) if (renderer != null) renderer.SetPropertyBlock(timberProperties);
    }
    private bool TimberIsMoving(Vector4 state) => state.w == 1f && state.y > 0f && Time.time < state.x + state.y;

    // Every visible room gets a clock; there is no slot-full branch that skips construction.
    private bool AdvanceTimberConstruction(ref bool seen, ref Vector4 state, Bounds bounds, bool visible)
    {
        if (!animateTimberConstruction)
        { bool changed = state != Vector4.zero; state = Vector4.zero; seen = true; return changed; }
        // Protect the contact area, not the entire 35-metre neighbourhood.
        if (bounds.SqrDistance(player.position) < 4f * 4f)
        {
            if (seen && state.y < 0f) return false;
            seen = true; state = new Vector4(0f, -1f, 0f, 1f); return true;
        }
        if (seen || !visible) return false;
        seen = true;
        int seed = ++timberSerial;
        state = new Vector4(Time.time + (seed % 7) * .025f, timberConstructionSeconds, seed % 10000, 1f);
        TimberConstructionStartCount++;
        if (!initialized) StartupTimberConstructionCount++;
        return true;
    }
    private void ResetTimberConstruction(BuildingWing wing)
    {
        foreach (BuildingPart part in wing.parts)
        {
            part.assemblySeen = false; part.construction = Application.isPlaying ? PendingTimber : Vector4.zero;
            SetTimberProperties(part.constructionRenderers, part.construction);
        }
        foreach (BuildingSeam seam in wing.seams) if (seam.bridge != null) ResetTimberLink(seam.bridge);
    }
    private void ResetTimberLink(Link link)
    {
        link.assemblySeen = false; link.construction = Application.isPlaying ? PendingTimber : Vector4.zero;
        SetTimberProperties(link.renderers, link.construction);
    }
    private void UpdateTimberAssemblies(Camera camera)
    {
        if (!Application.isPlaying || camera != mainCamera) return;
        ActiveTimberAssemblyCount = 0;
        if (Time.time < nextTimberScan) return;
        nextTimberScan = Time.time + .08f;
        GeometryUtility.CalculateFrustumPlanes(camera, timberPlanes);
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active) continue;
            foreach (BuildingPart part in wing.parts)
            {
                if (part.constructionRenderers.Length == 0) continue; // Collision/gravity-only roof markers have no visual mesh.
                bool visible = part.bounds.SqrDistance(player.position) <= CastleVisibleRadius * CastleVisibleRadius &&
                    GeometryUtility.TestPlanesAABB(timberPlanes, part.bounds);
                if (AdvanceTimberConstruction(ref part.assemblySeen, ref part.construction, part.bounds, visible))
                    SetTimberProperties(part.constructionRenderers, part.construction);
                if (visible && TimberIsMoving(part.construction)) ActiveTimberAssemblyCount++;
            }
        }
        foreach (Link link in timberLinks)
        {
            if (!link.root.activeInHierarchy || link.root.transform.parent == denseRoot) continue;
            bool visible = link.bounds.SqrDistance(player.position) <= CastleVisibleRadius * CastleVisibleRadius &&
                GeometryUtility.TestPlanesAABB(timberPlanes, link.bounds);
            if (AdvanceTimberConstruction(ref link.assemblySeen, ref link.construction, link.bounds, visible))
                SetTimberProperties(link.renderers, link.construction);
            if (visible && TimberIsMoving(link.construction)) ActiveTimberAssemblyCount++;
        }
    }
    private Vector4 DistrictTimber(ref bool seen, ref Vector4 state, Bounds bounds, Camera camera)
    {
        if (Application.isPlaying && camera == mainCamera)
            AdvanceTimberConstruction(ref seen, ref state, bounds, true);
        if (camera == mainCamera && TimberIsMoving(state)) ActiveTimberAssemblyCount++;
        return state;
    }
    private void CancelTimberAssemblies()
    {
        foreach (BuildingWing wing in buildingWings) foreach (BuildingPart part in wing.parts)
        {
            part.assemblySeen = true; part.construction = new Vector4(0f, -1f, 0f, 1f);
            SetTimberProperties(part.constructionRenderers, part.construction);
        }
        foreach (Link link in timberLinks) if (link.root != null)
        {
            link.assemblySeen = true; link.construction = new Vector4(0f, -1f, 0f, 1f);
            SetTimberProperties(link.renderers, link.construction);
        }
    }
}
