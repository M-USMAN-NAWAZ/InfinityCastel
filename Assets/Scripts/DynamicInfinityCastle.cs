using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-200)]
public partial class DynamicInfinityCastle : MonoBehaviour
{
    [Header("Player and Pool Parents")]
    public Transform player;
    public Transform buildingsParent, pathsParent;
    [Header("Your Artwork (Original Scale)")]
    public List<GameObject> buildingPrefabs = new();
    public List<GameObject> pathPrefabs = new();
    public List<GameObject> mobileDecorationPrefabs = new();
    public GameObject mobileControlsPrefab;
    public RenderPipelineAsset mobileRenderPipeline;
    public bool previewTouchControls;
    public GameObject landingPrefab;
    public Material deckMaterial;
    public Shader architectureShader;
    [Serializable]
    public sealed class ArchitectureFinish
    {
        public Material source;
        public Texture2D albedo;
        public Color tint = Color.white;
        [Range(0f, 2f)] public float glow;
    }
    public List<ArchitectureFinish> architectureFinishes = new();
    [Header("Surrounding Layers")]
    [Range(3, 4)] public int layersInEveryDirection = 4;
    public float cellSize = 18f;
    public float verticalLayerSpacing = 7f;
    public int extraPoolPieces = 12;
    [Range(0f, 0.65f)] public float irregularity = 0.35f;
    public int randomSeed = 9217;
    [Header("Connected Balconies")]
    [Range(2f, 6f)] public float balconyWidth = 4f;
    [Range(2, 4)] public int playableFloorsBelow = 3;
    [Range(1, 2)] public int playableFloorsAbove = 1;
    [Range(0.1f, 1f)] public float decorationDensity = 0.48f;
    [Header("One-Way Shifting")]
    [Range(0f, 1f)] public float stillPieceChance = 0.45f;
    public float maxMotionRadius = 6f;
    [Range(4f, 24f)] public float nearbyShiftTravel = 12f;
    public bool rideMovingBuildings = true;
    public Vector2 motionSpeedRange = new(1.2f, 2.5f);
    public float oneWayExitDuration = 4f;
    [Range(1, 12)] public int simultaneousShifts = 6;
    public float shiftInterval = 1.5f;
    [Header("Landing Safety")]
    public bool keepFloorUnderPlayer = true;
    public float floorHeightBelowPlayer = 0.05f;
    [Range(3, 5)] public int catchLayersBelow = 3;
    public int catchRadius = 2;
    [Header("Local Gravity")]
    public bool enableGravityZones = true;
    [Range(0f, 0.5f)] public float gravityZoneChance = 0.25f;
    public bool castleLighting = true;
    [Header("Nearby Random Castle")]
    public bool randomNearbyCastle = true;
    [Range(96, 320)] public int nearbyPoolSize = 128;
    [Range(1f, 2.2f)] public float jumpGap = 1.4f;
    [Header("Periodic Rebuilding")]
    public bool periodicRebuilding = true;
    public Vector2 rebuildInterval = new(25f, 40f);
    [UnityEngine.Serialization.FormerlySerializedAs("rebuildDissolveSeconds")]
    [Range(3f, 12f)] public float rebuildMoveSeconds = 6f;
    [Range(4f, 24f)] public float rebuildTravel = 18f;
    [Header("Android Budget")]
    public bool mobileBudget = true;
    [Range(48, 192)] public int decorationPoolSize = 144;
    [Range(1, 8)] public int layoutStepsPerFrame = 2;
    [Range(0.5f, 8f)] public float layoutMillisecondsPerFrame = 2f;

    private sealed class Node
    {
        public InfinityCastleBuilder piece;
        public Vector2Int cell;
        public Vector3 centre;
        public float height;
        public Bounds reservation;
        public bool rebuilding;
        public int level, revision;
        public float score;
    }
    private sealed class Stair
    {
        public GameObject prefab;
        public Vector3 low, high;
        public float run, rise, width;
    }
    private readonly struct Edge : IEquatable<Edge>
    {
        public readonly Vector2Int a, b;
        public readonly int level;
        public Edge(Vector2Int from, Vector2Int to, int floorLevel = 0) { a = from; b = to; level = floorLevel; }
        public bool Equals(Edge other) => a == other.a && b == other.b && level == other.level;
        public override bool Equals(object obj) => obj is Edge edge && Equals(edge);
        public override int GetHashCode() => unchecked((a.GetHashCode() * 397 ^ b.GetHashCode()) * 397 ^ level);
    }
    private sealed class Link
    {
        public GameObject root, flat, flight, ramp, flatCollision;
        public CastleWalkableSurface landing;
        public Bounds bounds;
        public Renderer[] renderers;
    }
    private sealed class LandingDistrict
    {
        public Transform root;
        public readonly InfinityCastleBuilder[] pieces = new InfinityCastleBuilder[9];
        public readonly CastleWalkableSurface[] courts = new CastleWalkableSurface[9];
        public readonly Link[] connections = new Link[12];
        public readonly Vector3[] centres = new Vector3[9];
        public Vector3 target, up;
        public Bounds reservation;
        public bool active;
        public int generation;
    }
    private readonly Queue<Node> nodePool = new();
    private readonly Queue<InfinityCastleBuilder> decorPool = new();
    private readonly Queue<Link> linkPool = new();
    private readonly Dictionary<Vector3Int, Node> nodes = new();
    private readonly Dictionary<Vector3Int, InfinityCastleBuilder> decor = new();
    private readonly Dictionary<Edge, Link> links = new();
    private readonly HashSet<Vector3Int> wantedNodes = new();
    private readonly HashSet<Vector3Int> wantedDecor = new();
    private readonly Queue<Vector3Int> decorLayout = new();
    private readonly List<Vector3Int> decorCandidates = new();
    private readonly int[] decorSectors = new int[30];
    private readonly List<Bounds> reservations = new();
    private readonly List<Vector3Int> removeNodes = new();
    private readonly List<Node> shiftCandidates = new();
    private readonly List<Vector3Int> removeDecor = new();
    private readonly List<Edge> removeLinks = new();
    private readonly Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    private ThirdPersonController controller;
    private InfinityGravityBody gravity;
    private Stair flatStair, flightStair;
    private Vector3 origin;
    private float deckSize, routeSpacing, decorSpacing, shiftDistance;
    private float maximumBuildingHeight;
    private readonly Plane[] cameraPlanes = new Plane[6];
    private Vector2Int routeCell;
    private int routeLevel, routeSignature;
    private Vector3Int decorCell;
    private LandingDistrict[] landingDistricts = new LandingDistrict[3];
    private InfinityCastleDirector director;
    private Vector3 previousPlayerPosition, playerVelocity, lastFallUp;
    private bool fallPrepared;
    private int landingGeneration;
    private Camera mainCamera;
    private int shiftSerial;
    private int pieceSerial;
    private bool initialized;
    private static Mesh stairRampMesh;
    private bool previousFog;
    private float previousFogDensity;
    private Color previousFogColor, previousAmbient;
    private AmbientMode previousAmbientMode;
    private FogMode previousFogMode;
    private readonly Dictionary<Material, Material> runtimeMaterials = new();
    private CameraClearFlags previousClearFlags;
    private Color previousBackground;

    public float LayerSpacing => decorSpacing;
    public float RouteSpacing => routeSpacing;
    public int ActiveBuildingCount => nodes.Count + nearbyNodes.Count + decor.Count +
        (ActiveLandingDistrictCount - (HasNearbyLanding ? 1 : 0)) * 9;
    public int ActiveConnectionCount => links.Count + nearbyLinks.Count;
    public int ShiftCommandsIssued => director != null ? director.CommandsIssued : 0;
    public Vector3 PlayerUp => gravity != null ? gravity.Up : Vector3.up;
    public int PoolCapacity { get; private set; }
    public bool IsInitialized => initialized;
    public bool HasPendingLayout => nearbyLayout != null || decorLayout.Count > 0;
    public int CurrentRouteLevel => routeLevel;
    public int LandingGeneration => landingGeneration;
    public int ActiveLandingDistrictCount
    {
        get { int count = HasNearbyLanding ? 1 : 0; foreach (LandingDistrict district in landingDistricts) if (district != null && district.active) count++; return count; }
    }

    private void Start() => InitializeCastle();
    public void InitializeCastle()
    {
        if (initialized) return;
        buildingPrefabs.RemoveAll(item => item == null);
        pathPrefabs.RemoveAll(item => item == null);
        if (player == null)
        {
            ThirdPersonController found = FindFirstObjectByType<ThirdPersonController>();
            if (found != null) player = found.transform;
        }
        if (player == null || buildingPrefabs.Count == 0)
        { Debug.LogError("Infinity Castle requires a player and your building prefabs.", this); enabled = false; return; }
        layersInEveryDirection = Mathf.Clamp(layersInEveryDirection, 3, 4);
        catchLayersBelow = Mathf.Clamp(catchLayersBelow, 3, 5);
        balconyWidth = Mathf.Clamp(balconyWidth, 2f, 6f);
        playableFloorsBelow = Mathf.Clamp(playableFloorsBelow, 2, 4);
        playableFloorsAbove = Mathf.Clamp(playableFloorsAbove, 1, 2);
        nearbyPoolSize = Mathf.Clamp(nearbyPoolSize, 96, 320);
        if (mobileBudget && randomNearbyCastle)
        {
            nearbyPoolSize = Mathf.Min(nearbyPoolSize, 128);
            landingDistricts = Array.Empty<LandingDistrict>();
        }
        // Old scene values cannot override the physical clearance budget.
        shiftDistance = Mathf.Clamp(maxMotionRadius, 2f, 8f);
        maxMotionRadius = shiftDistance;
        oneWayExitDuration = Mathf.Clamp(oneWayExitDuration, 2f, 8f);
        if (buildingsParent == null) buildingsParent = transform;
        if (pathsParent == null) pathsParent = transform;
        controller = player.GetComponent<ThirdPersonController>();
        gravity = player.GetComponent<InfinityGravityBody>();
        if (gravity == null) gravity = player.gameObject.AddComponent<InfinityGravityBody>();
        gravity.defaultGravityStrength = 22f;
        if (controller != null) { controller.Initialize(); controller.JumpTimeout = Mathf.Min(controller.JumpTimeout, 0.2f); }
        mainCamera = Camera.main;
        origin = player.position;
        ResolveDeckMaterial();
        if (architectureShader == null) architectureShader = Shader.Find("Infinity Castle/Architecture");
        deckMaterial = PooledMaterial(deckMaterial);
        ReadStairs();

        var samples = new List<InfinityCastleBuilder>();
        float footprint = 0f, largest = 0f;
        foreach (GameObject prefab in buildingPrefabs)
        {
            InfinityCastleBuilder sample = CreatePiece(prefab);
            Bounds bounds = sample.WorldBounds;
            footprint = Mathf.Max(footprint, bounds.size.x, bounds.size.z);
            largest = Mathf.Max(largest, bounds.size.x, bounds.size.y, bounds.size.z);
            samples.Add(sample);
        }
        deckSize = footprint + balconyWidth * 2f;
        maximumBuildingHeight = largest;
        float longestPath = Mathf.Max(flatStair != null ? flatStair.run : 0f, flightStair != null ? flightStair.run : 0f);
        routeSpacing = deckSize + longestPath + 12f;
        decorSpacing = Mathf.Max(Mathf.Clamp(cellSize, 18f, 120f), largest + balconyWidth * 2f + shiftDistance * 2f + 8f);
        int width = layersInEveryDirection * 2 + 1;
        int floorCount = playableFloorsBelow + playableFloorsAbove + 1;
        int nodeCount = width * width * floorCount;
        int decorCount = mobileBudget ? Mathf.Clamp(decorationPoolSize, 48, 192) : width * width * width;
        int reserveCount = Mathf.Clamp(extraPoolPieces, 2, 24);
        if (randomNearbyCastle) { nodeCount = 0; reserveCount = 0; }
        PoolCapacity = nodeCount + decorCount + reserveCount + landingDistricts.Length * 9 + (randomNearbyCastle ? nearbyPoolSize : 0);
        for (int i = 0; i < nodeCount + reserveCount; i++)
        {
            InfinityCastleBuilder piece = i < samples.Count ? samples[i] : CreatePiece(buildingPrefabs[i % buildingPrefabs.Count]);
            AddBalcony(piece, deckSize, false);
            piece.gameObject.SetActive(false);
            nodePool.Enqueue(new Node { piece = piece });
        }
        for (int i = 0; i < decorCount; i++)
        {
            GameObject art = mobileBudget && mobileDecorationPrefabs.Count == buildingPrefabs.Count ?
                mobileDecorationPrefabs[i % mobileDecorationPrefabs.Count] : buildingPrefabs[i % buildingPrefabs.Count];
            InfinityCastleBuilder piece = CreatePiece(art);
            Bounds local = CastleGeometry.RendererBoundsInRoot(piece.transform);
            AddBalcony(piece, Mathf.Max(local.size.x, local.size.z) + 3f, true);
            piece.gameObject.SetActive(false); decorPool.Enqueue(piece);
        }
        int connectionCount = randomNearbyCastle ? nearbyPoolSize * 2 : width * (width - 1) * 2 * floorCount;
        for (int i = 0; i < connectionCount; i++) linkPool.Enqueue(CreateLink(i));
        PrewarmLandingDistricts();
        routeCell = Vector2Int.zero; decorCell = Vector3Int.zero;
        routeLevel = 0; routeSignature = RouteWindowSignature();
        if (randomNearbyCastle)
        {
            PrewarmNearbyCastle(samples); InitializeNearbyCastle();
            if (controller != null) { controller.MoveSpeed = Mathf.Max(controller.MoveSpeed, 4f); controller.JumpHeight = Mathf.Max(controller.JumpHeight, 1.7f); }
        }
        else
        {
            RebuildRoutes();
            // Start at an open balcony port, not inside a tower's convex hull.
            Node start = GetNode(Vector2Int.zero, 0);
            player.position = start.centre + new Vector3(deckSize * 0.5f - 1.4f, 0.08f, 0f);
        }
        RebuildDecor();
        if (controller != null) controller.ResetFallVelocity();
        if (castleLighting) ApplyLighting();
        if (mainCamera != null)
        {
            previousClearFlags = mainCamera.clearFlags; previousBackground = mainCamera.backgroundColor;
            if (RenderSettings.skybox != null) mainCamera.clearFlags = CameraClearFlags.Skybox;
        }
        previousPlayerPosition = player.position;
        director = GetComponent<InfinityCastleDirector>();
        if (director == null) director = gameObject.AddComponent<InfinityCastleDirector>();
        director.Initialize(randomSeed);
        InitializeLearning();
        Physics.SyncTransforms();
        initialized = true;
        if (Application.isPlaying)
        {
            InfinityCastleMobileRuntime mobile = gameObject.AddComponent<InfinityCastleMobileRuntime>();
            mobile.Configure(this);
        }
    }

    private void ResolveDeckMaterial()
    {
        if (deckMaterial != null) return;
        foreach (GameObject prefab in buildingPrefabs)
        {
            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            if (renderer != null && renderer.sharedMaterial != null) { deckMaterial = renderer.sharedMaterial; break; }
        }
        if (deckMaterial == null)
        {
            deckMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            deckMaterial.color = new Color(0.22f, 0.13f, 0.08f);
        }
    }
    private InfinityCastleBuilder CreatePiece(GameObject prefab)
    {
        GameObject root = new($"{prefab.name} Castle Module {pieceSerial++:0000}"); root.transform.SetParent(buildingsParent, false);
        GameObject art = Instantiate(prefab, root.transform);
        art.transform.localPosition = Vector3.zero;
        // The art's authored rotation and scale are never changed.
        InfinityCastleBuilder piece = root.AddComponent<InfinityCastleBuilder>(); piece.InitializePooledPiece(this);
        return piece;
    }
    internal Material PooledMaterial(Material source)
    {
        if (source == null || architectureShader == null) return source;
        if (runtimeMaterials.TryGetValue(source, out Material cached)) return cached;
        Material material = new(architectureShader) { name = source.name + " Pooled Architecture", enableInstancing = true };
        string map = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        if (source.HasProperty(map))
        {
            material.SetTexture("_BaseMap", source.GetTexture(map));
            material.SetTextureScale("_BaseMap", source.GetTextureScale(map));
            material.SetTextureOffset("_BaseMap", source.GetTextureOffset(map));
        }
        string colour = source.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        if (source.HasProperty(colour)) material.SetColor("_BaseColor", source.GetColor(colour));
        foreach (ArchitectureFinish finish in architectureFinishes)
        {
            if (finish.source != source) continue;
            if (finish.albedo != null) material.SetTexture("_BaseMap", finish.albedo);
            material.SetColor("_BaseColor", finish.tint); material.SetFloat("_Glow", finish.glow); break;
        }
        runtimeMaterials[source] = material; runtimeMaterials[material] = material;
        return material;
    }
    private void FinishArt(Transform root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = PooledMaterial(materials[i]);
            renderer.sharedMaterials = materials;
        }
    }
    private void AddBalcony(InfinityCastleBuilder piece, float size, bool localGravity)
    {
        Bounds bounds = CastleGeometry.RendererBoundsInRoot(piece.transform);
        CastleWalkableSurface surface = CreateSurface("Connected Timber Balcony", size, size, !mobileBudget || !localGravity, piece.transform);
        surface.transform.localPosition = new Vector3(bounds.center.x, bounds.min.y - 0.02f, bounds.center.z);
        InfiniteCastleGrid motionZone = null;
        if (localGravity)
        {
            InfiniteCastleGrid zone = surface.gameObject.AddComponent<InfiniteCastleGrid>();
            zone.Configure(Vector3.down, new Vector3(0f, 2.5f, 0f), new Vector3(size, 5f, size)); zone.enabled = false;
            motionZone = zone;
        }
        piece.InitializePooledPiece(this);
        piece.ConfigureMotionGravity(motionZone, enableGravityZones);
    }
    private CastleWalkableSurface CreateSurface(string label, float width, float depth, bool rails, Transform parent)
    {
        GameObject go = landingPrefab != null ? Instantiate(landingPrefab, parent) : new GameObject(label);
        go.name = label; go.transform.SetParent(parent, false);
        CastleWalkableSurface surface = go.GetComponent<CastleWalkableSurface>();
        if (surface == null) surface = go.AddComponent<CastleWalkableSurface>();
        surface.Initialize(width, depth, deckMaterial, rails); return surface;
    }

    private void ReadStairs()
    {
        foreach (GameObject prefab in pathPrefabs)
        {
            GameObject sample = new("Path measurement"); sample.transform.SetParent(pathsParent, false);
            GameObject art = Instantiate(prefab, sample.transform); art.transform.localPosition = Vector3.zero;
            foreach (FloatingStructure legacy in sample.GetComponentsInChildren<FloatingStructure>()) legacy.enabled = false;
            foreach (MeshCollider collider in sample.GetComponentsInChildren<MeshCollider>()) collider.convex = false;
            Bounds bounds = CastleGeometry.RendererBoundsInRoot(sample.transform);
            Vector3 axis = bounds.size.z >= bounds.size.x ? Vector3.forward : Vector3.right;
            float length = bounds.size.z >= bounds.size.x ? bounds.size.z : bounds.size.x;
            Vector3 first = default, last = default; bool found = false;
            Collider[] colliders = sample.GetComponentsInChildren<Collider>();
            Physics.SyncTransforms();
            for (int i = 0; i <= 80; i++)
            {
                Vector3 point = bounds.center + axis * Mathf.Lerp(-length * 0.5f + 0.12f, length * 0.5f - 0.12f, i / 80f);
                point.y = bounds.max.y + 1f; float distance = float.PositiveInfinity; RaycastHit best = default;
                foreach (Collider collider in colliders)
                    if (!collider.isTrigger && collider.Raycast(new Ray(sample.transform.TransformPoint(point), Vector3.down), out RaycastHit hit, bounds.size.y + 2f) && hit.distance < distance)
                    { best = hit; distance = hit.distance; }
                if (distance == float.PositiveInfinity || best.normal.y < 0.9f) continue;
                Vector3 surfacePoint = sample.transform.InverseTransformPoint(best.point);
                if (!found) { first = surfacePoint; found = true; }
                last = surfacePoint;
            }
            if (found)
            {
                if (first.y > last.y) (first, last) = (last, first);
                Stair spec = new() { prefab = prefab, low = first, high = last,
                    run = Vector3.ProjectOnPlane(last - first, Vector3.up).magnitude, rise = last.y - first.y,
                    width = bounds.size.z >= bounds.size.x ? bounds.size.x : bounds.size.z };
                if (spec.run > 1f && spec.width > 0.8f)
                {
                    if (spec.rise < 0.15f && flatStair == null) flatStair = spec;
                    if (spec.rise > 0.5f && spec.rise / spec.run < 0.78f &&
                        (flightStair == null || prefab.name == "stair_3")) flightStair = spec;
                }
            }
            // Measurements become dormant templates: no objects are destroyed during play.
            sample.SetActive(false);
        }
        if (flightStair == null) Debug.LogWarning("No usable stair flight found. Routes will stay level; assign Danish stair_3 for tiered balconies.", this);
    }

    private Link CreateLink(int index)
    {
        GameObject root = new("Pooled Castle Connection " + index); root.transform.SetParent(pathsParent, false);
        Link link = new() { root = root };
        if (flatStair != null) link.flat = Instantiate(flatStair.prefab, root.transform);
        if (flightStair != null) link.flight = Instantiate(flightStair.prefab, root.transform);
        FinishArt(root.transform);
        foreach (Collider collider in root.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (FloatingStructure legacy in root.GetComponentsInChildren<FloatingStructure>()) legacy.enabled = false;
        link.ramp = new GameObject("Fitted Stair Collision"); link.ramp.transform.SetParent(root.transform, false);
        if (stairRampMesh == null)
        {
            stairRampMesh = new Mesh { name = "Castle Stair Collision Wedge" };
            stairRampMesh.vertices = new[] { new Vector3(-0.5f,0f,0f), new Vector3(0.5f,0f,0f), new Vector3(-0.5f,1f,1f), new Vector3(0.5f,1f,1f),
                new Vector3(-0.5f,-0.05f,0f),new Vector3(0.5f,-0.05f,0f),new Vector3(-0.5f,0.95f,1f),new Vector3(0.5f,0.95f,1f) };
            stairRampMesh.triangles = new[] { 0,2,3,0,3,1, 4,5,7,4,7,6, 4,0,1,4,1,5, 6,7,3,6,3,2, 4,6,2,4,2,0, 5,1,3,5,3,7 };
            stairRampMesh.RecalculateBounds(); stairRampMesh.RecalculateNormals();
        }
        link.ramp.AddComponent<MeshCollider>().sharedMesh = stairRampMesh;
        link.flatCollision = new GameObject("Fitted Walkway Collision"); link.flatCollision.transform.SetParent(root.transform, false);
        link.flatCollision.AddComponent<BoxCollider>();
        link.landing = CreateSurface("Connector Landing", 3f, 1f, false, root.transform);
        InfiniteCastleGrid zone = root.AddComponent<InfiniteCastleGrid>();
        zone.enabled = false;
        link.renderers = root.GetComponentsInChildren<Renderer>(true);
        root.SetActive(false); return link;
    }

    private float HeightAt(Vector2Int cell, int level = 0, int revision = 0) => flightStair == null ? 0f :
        (Hash(new Vector3Int(cell.x, level, cell.y), 2 + revision * 41) > 0.5f ? flightStair.rise : 0f);
    private Quaternion LayerRotation(int level) => Quaternion.Euler(0f, level == 0 ? 0f :
        (Hash(new Vector3Int(0, level, 0), 53) - 0.5f) * 18f, 0f);
    private Vector3 LayerOffset(int level)
    {
        int pattern = (level % 4 + 4) % 4;
        return new Vector3((pattern == 1 || pattern == 2 ? 0.48f : 0f) * routeSpacing, 0f,
            (pattern == 1 || pattern == 3 ? 0.48f : 0f) * routeSpacing);
    }
    private Vector2Int CellAt(Vector3 position, int level)
    {
        Vector3 local = Quaternion.Inverse(LayerRotation(level)) * (position - origin) - LayerOffset(level);
        return new Vector2Int(Mathf.RoundToInt(local.x / routeSpacing), Mathf.RoundToInt(local.z / routeSpacing));
    }
    private int RouteWindowSignature()
    {
        unchecked
        {
            int signature = routeLevel;
            for (int level = routeLevel - playableFloorsAbove; level <= routeLevel + playableFloorsBelow; level++)
                signature = signature * 397 ^ CellAt(player.position, level).GetHashCode();
            return signature;
        }
    }
    private Vector3 RoutePosition(Vector2Int cell, int level = 0, int revision = 0)
    {
        float jitter = Mathf.Min(6.5f, irregularity * 16f);
        Vector3 offset = new((Hash(new Vector3Int(cell.x, level, cell.y), 11 + revision * 41) - 0.5f) * jitter,
            HeightAt(cell, level, revision) - HeightAt(Vector2Int.zero) - level * decorSpacing,
            (Hash(new Vector3Int(cell.x, level, cell.y), 12 + revision * 41) - 0.5f) * jitter);
        return origin + LayerRotation(level) * (new Vector3(cell.x * routeSpacing, 0f, cell.y * routeSpacing) +
            LayerOffset(level) + offset);
    }
    private void RebuildRoutes()
    {
        wantedNodes.Clear(); int r = layersInEveryDirection;
        for (int level = routeLevel - playableFloorsAbove; level <= routeLevel + playableFloorsBelow; level++)
        {
            Vector2Int centre = CellAt(player.position, level);
            for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++)
                wantedNodes.Add(new Vector3Int(centre.x + x, level, centre.y + z));
        }
        removeLinks.Clear();
        foreach (var pair in links)
            if (!wantedNodes.Contains(new Vector3Int(pair.Key.a.x, pair.Key.level, pair.Key.a.y)) ||
                !wantedNodes.Contains(new Vector3Int(pair.Key.b.x, pair.Key.level, pair.Key.b.y))) removeLinks.Add(pair.Key);
        foreach (Edge edge in removeLinks) { Link link = links[edge]; link.root.SetActive(false); linkPool.Enqueue(link); links.Remove(edge); }
        removeNodes.Clear();
        foreach (var pair in nodes) if (!wantedNodes.Contains(pair.Key)) removeNodes.Add(pair.Key);
        foreach (Vector3Int cell in removeNodes) { Node node = nodes[cell]; node.piece.gameObject.SetActive(false); nodePool.Enqueue(node); nodes.Remove(cell); }
        // Only the missing far-edge row is filled. All retained nodes and links keep their transforms.
        foreach (Vector3Int cell in wantedNodes)
        {
            if (nodes.ContainsKey(cell)) continue;
            if (nodePool.Count == 0) throw new InvalidOperationException("Castle balcony pool exhausted.");
            Node node = PlaceNode(new Vector2Int(cell.x, cell.z), cell.y);
            if (HitsLandingDistrict(node.reservation)) { node.piece.gameObject.SetActive(false); nodePool.Enqueue(node); continue; }
            nodes.Add(cell, node);
        }
        foreach (var pair in nodes)
        {
            Node node = pair.Value;
            Connect(node.cell, node.cell + Vector2Int.right, node.level);
            Connect(node.cell, node.cell + Vector2Int.up, node.level);
        }
        RefreshReservations();
        // Any distant decoration occupying a newly extended route is recycled before the route is used.
        removeDecor.Clear();
        foreach (var pair in decor) if (HitsRoute(pair.Value.ReservedBounds)) removeDecor.Add(pair.Key);
        foreach (Vector3Int cell in removeDecor) { InfinityCastleBuilder piece = decor[cell]; piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
    }
    private Node PlaceNode(Vector2Int cell, int level)
    {
        if (nodePool.Count == 0) throw new InvalidOperationException("Castle balcony pool exhausted.");
        Node node = nodePool.Dequeue(); node.cell = cell; node.level = level; node.revision = 0;
        node.centre = RoutePosition(cell, level); node.height = node.centre.y;
        node.rebuilding = false;
        PlaceNodePiece(node, level);
        return node;
    }
    private void PlaceNodePiece(Node node, int level)
    {
        Bounds bounds = CastleGeometry.RendererBoundsInRoot(node.piece.transform);
        Vector3 centre = node.centre + Vector3.up * (bounds.size.y * 0.5f - 0.24f);
        Quaternion yaw = LayerRotation(level) * Quaternion.Euler(0f,
            Mathf.Floor(Hash(new Vector3Int(node.cell.x, level, node.cell.y), 19 + node.revision * 41) * 4f) * 90f, 0f);
        node.reservation = CastleGeometry.TransformBounds(new Bounds(Vector3.up * (maximumBuildingHeight * 0.5f),
            new Vector3(deckSize + 1f, maximumBuildingHeight + 1f, deckSize + 1f)), Matrix4x4.TRS(node.centre, yaw, Vector3.one));
        node.piece.Place(centre, yaw); node.piece.gameObject.SetActive(true);
    }
    private Node GetNode(Vector2Int cell, int level)
    {
        nodes.TryGetValue(new Vector3Int(cell.x, level, cell.y), out Node node); return node;
    }
    private void Connect(Vector2Int a, Vector2Int b, int level = 0)
    {
        Node low = GetNode(a, level), high = GetNode(b, level); if (low == null || high == null) return;
        if (low.rebuilding || high.rebuilding) return;
        Edge edge = new(a, b, level); if (links.ContainsKey(edge)) return;
        if (linkPool.Count == 0) throw new InvalidOperationException("Castle connection pool exhausted.");
        Link link = linkPool.Dequeue();
        ConfigureLink(link, low.centre, high.centre, Vector3.up, LayerRotation(level));
        if (HitsLandingDistrict(link.bounds)) { link.root.SetActive(false); linkPool.Enqueue(link); return; }
        links.Add(edge, link);
    }
    private float DeckEdgeDistance(Vector3 direction, Quaternion rotation)
    {
        Vector3 local = Quaternion.Inverse(rotation) * direction;
        return deckSize * 0.5f / Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z)));
    }
    private void ConfigureLink(Link link, Vector3 low, Vector3 high, Vector3 up, Quaternion basis,
        Quaternion? lowRotation = null, Quaternion? highRotation = null)
    {
        if (Vector3.Dot(low, up) > Vector3.Dot(high, up))
        { (low, high) = (high, low); (lowRotation, highRotation) = (highRotation, lowRotation); }
        Vector3 direction = Vector3.ProjectOnPlane(high - low, up).normalized;
        link.root.transform.SetPositionAndRotation((low + high) * 0.5f, Quaternion.LookRotation(direction, up));
        Vector3 start = low + direction * (DeckEdgeDistance(direction, lowRotation ?? basis) - 0.18f);
        Vector3 end = high - direction * (DeckEdgeDistance(direction, highRotation ?? basis) - 0.18f);
        bool step = Vector3.Dot(high - low, up) > 0.2f;
        float availableRun = Vector3.ProjectOnPlane(end - start, up).magnitude;
        Stair spec = step ? flightStair : flatStair; GameObject art = step ? link.flight : link.flat;
        if (spec != null && (spec.run > availableRun + 0.1f || Mathf.Abs(Vector3.Dot(end - start, up) - spec.rise) > 0.15f))
        { spec = null; art = null; }
        if (link.flat != null) link.flat.SetActive(false); if (link.flight != null) link.flight.SetActive(false);
        link.ramp.SetActive(false); link.flatCollision.SetActive(false);
        Vector3 landingStart = start;
        if (spec != null && art != null)
        {
            Vector3 authoredForward = Vector3.ProjectOnPlane(spec.high - spec.low, Vector3.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction, up) * Quaternion.Inverse(Quaternion.LookRotation(authoredForward, Vector3.up));
            art.transform.rotation = rotation * spec.prefab.transform.rotation;
            art.transform.position = start - rotation * spec.low;
            art.SetActive(true); landingStart = start + direction * spec.run + up * spec.rise;
            if (step)
            {
                link.ramp.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction, up));
                link.ramp.transform.localScale = new Vector3(Mathf.Max(1.2f, spec.width - 0.15f), spec.rise, spec.run);
                link.ramp.SetActive(true);
            }
            else
            {
                link.flatCollision.transform.SetPositionAndRotation((start + landingStart) * 0.5f, Quaternion.LookRotation(direction, up));
                BoxCollider board = link.flatCollision.GetComponent<BoxCollider>();
                board.center = Vector3.down * 0.12f; board.size = new Vector3(Mathf.Max(1.2f, spec.width - 0.15f), 0.24f, spec.run + 0.36f);
                link.flatCollision.SetActive(true);
            }
        }
        float remaining = Vector3.Distance(end, landingStart);
        Vector3 landingDirection = remaining > 0.001f ? (end - landingStart).normalized : direction;
        link.landing.transform.SetPositionAndRotation((landingStart + end) * 0.5f, Quaternion.LookRotation(landingDirection, up));
        link.landing.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.1f, remaining + (step ? 0.02f : 0.36f)));
        link.root.SetActive(true);
        link.bounds = ConnectionVisualBounds(link);
        InfiniteCastleGrid zone = link.root.GetComponent<InfiniteCastleGrid>();
        float rise = Mathf.Abs(Vector3.Dot(high - low, up));
        zone.Configure(-up, link.root.transform.InverseTransformPoint((start + end) * 0.5f + up * 1.8f),
            new Vector3(3.4f, 5.4f + rise, Vector3.ProjectOnPlane(end - start, up).magnitude + 1f), 1);
        zone.enabled = enableGravityZones;
    }
    private Bounds ConnectionVisualBounds(Link link)
    {
        Bounds local = default;
        bool found = false;
        foreach (Renderer renderer in link.renderers)
        {
            if (!renderer.enabled) continue;
            bool active = true;
            for (Transform parent = renderer.transform; parent != link.root.transform; parent = parent.parent)
                if (!parent.gameObject.activeSelf) { active = false; break; }
            if (!active) continue;
            Bounds part = CastleGeometry.TransformBounds(renderer.localBounds, link.root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
            if (found) local.Encapsulate(part); else { local = part; found = true; }
        }
        return CastleGeometry.TransformBounds(local, link.root.transform.localToWorldMatrix);
    }
    private void RefreshReservations()
    {
        reservations.Clear();
        foreach (Node node in nodes.Values) reservations.Add(node.reservation);
        foreach (Link link in links.Values) { Bounds bounds = link.bounds; bounds.Expand(0.5f); reservations.Add(bounds); }
        foreach (LandingDistrict district in landingDistricts) if (district.active) reservations.Add(district.reservation);
        foreach (NearbyNode node in nearbyNodes)
        { reservations.Add(node.piece.ReservedBounds); reservations.Add(NearbyStandingSpace(node)); reservations.Add(NearbyStandingSpace(node, true)); }
        foreach (NearbyLink connection in nearbyLinks) reservations.Add(connection.reservation);
    }
    private bool HitsRoute(Bounds box) { foreach (Bounds reservation in reservations) if (reservation.Intersects(box)) return true; return false; }
    private bool IsSpaceFree(Bounds swept, InfinityCastleBuilder ignore = null)
    {
        if (HitsRoute(swept) || swept.Intersects(PlayerSafetyBounds())) return false;
        foreach (InfinityCastleBuilder piece in decor.Values) if (piece != ignore && piece.ReservedBounds.Intersects(swept)) return false;
        return true;
    }
    private Bounds PlayerSafetyBounds() => new(player.position + gravity.Up, Vector3.one * 5f);
    private float Hash(Vector3Int cell, int salt = 0)
    {
        unchecked
        {
            uint h = (uint)(randomSeed ^ salt * 374761393);
            h ^= (uint)cell.x * 73856093u; h ^= (uint)cell.y * 19349663u; h ^= (uint)cell.z * 83492791u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xffffffu) / 16777216f;
        }
    }
    private Quaternion DecorOrientation(Vector3Int cell)
    {
        Vector3 up = Vector3.up;
        if (enableGravityZones && Hash(cell, 5) < Mathf.Clamp01(gravityZoneChance))
            up = directions[Mathf.FloorToInt(Hash(cell, 6) * directions.Length)];
        return CastleGeometry.Orientation(up, Mathf.Floor(Hash(cell, 7) * 4f) * 90f);
    }
    private void RebuildDecor()
    {
        if (IsRebuilding) return;
        wantedDecor.Clear(); decorCandidates.Clear(); decorLayout.Clear(); int r = layersInEveryDirection;
        Array.Clear(decorSectors, 0, decorSectors.Length);
        for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
        {
            Vector3Int cell = decorCell + new Vector3Int(x, y, z);
            if (mobileBudget || Hash(cell, 3) <= decorationDensity) decorCandidates.Add(cell);
        }
        decorCandidates.Sort(CompareDecorCells);
        int perSector = Mathf.Max(1, decorationPoolSize / (6 * r));
        foreach (Vector3Int cell in decorCandidates)
        {
            Vector3Int relative = cell - decorCell;
            int layer = Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y), Mathf.Abs(relative.z));
            if (layer == 0) continue;
            int face = Mathf.Abs(relative.x) == layer ? (relative.x > 0 ? 0 : 1) :
                Mathf.Abs(relative.y) == layer ? (relative.y > 0 ? 2 : 3) : (relative.z > 0 ? 4 : 5);
            int sector = layer * 6 + face;
            if (mobileBudget && decorSectors[sector] >= perSector) continue;
            decorSectors[sector]++; wantedDecor.Add(cell);
        }
        removeDecor.Clear();
        foreach (Vector3Int cell in decor.Keys) if (!wantedDecor.Contains(cell)) removeDecor.Add(cell);
        foreach (Vector3Int cell in removeDecor)
        { InfinityCastleBuilder piece = decor[cell]; piece.StopShift(); piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
        foreach (Vector3Int cell in wantedDecor) if (!decor.ContainsKey(cell)) decorLayout.Enqueue(cell);
        if (!initialized || !mobileBudget) ProcessDecorLayout(true);
    }
    private int CompareDecorCells(Vector3Int a, Vector3Int b) =>
        Hash(a, 3).CompareTo(Hash(b, 3));
    private void ProcessDecorLayout(bool drain = false)
    {
        if (IsRebuilding) return;
        int attempts = drain ? int.MaxValue : Mathf.Max(1, layoutStepsPerFrame * 2);
        while (decorLayout.Count > 0 && attempts-- > 0)
        {
            Vector3Int cell = decorLayout.Dequeue();
            if (decor.ContainsKey(cell)) continue;
            if (decorPool.Count == 0) { decorLayout.Clear(); break; }
            InfinityCastleBuilder piece = decorPool.Dequeue();
            float jitter = 14f * Mathf.Clamp01(irregularity);
            Vector3 position = origin + (Vector3)cell * decorSpacing + new Vector3(Hash(cell, 21) - 0.5f, Hash(cell, 22) - 0.5f, Hash(cell, 23) - 0.5f) * jitter;
            piece.Place(position, DecorOrientation(cell));
            Bounds margin = piece.WorldBounds; margin.Expand(1f);
            if (!IsSpaceFree(margin)) { decorPool.Enqueue(piece); continue; }
            InfiniteCastleGrid zone = piece.MotionGravityZone;
            if (zone != null)
            {
                zone.gravityDirection = -piece.transform.up;
                zone.enabled = enableGravityZones && Vector3.Dot(piece.transform.up, Vector3.up) < 0.99f;
            }
            if (initialized)
            {
                Vector3 arrival = directions[Mathf.FloorToInt(Hash(cell, 44) * 6f)] * shiftDistance;
                if (IsSpaceFree(piece.SweepTo(arrival)))
                {
                    piece.Place(position + arrival, DecorOrientation(cell)); piece.BeginShift(-arrival, 3f);
                }
            }
            piece.gameObject.SetActive(true); decor.Add(cell, piece);
        }
    }

    private void Update() => SimulateCastle(Time.deltaTime);
    public void SimulateCastle(float dt)
    {
        if (!initialized || player == null) return;
        gravity.ResolveGravity();
        if (dt > 0f)
        {
            Vector3 velocity = (player.position - previousPlayerPosition) / dt;
            playerVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, gravity.Up), 12f) +
                gravity.Up * Mathf.Clamp(Vector3.Dot(velocity, gravity.Up), -45f, 45f);
        }
        previousPlayerPosition = player.position;
        Vector3 delta = player.position - origin;
        routeLevel = Mathf.RoundToInt(-delta.y / decorSpacing);
        routeCell = CellAt(player.position, routeLevel);
        Vector3Int nextDecor = new(Mathf.RoundToInt(delta.x / decorSpacing), Mathf.RoundToInt(delta.y / decorSpacing), Mathf.RoundToInt(delta.z / decorSpacing));
        bool changed = PrepareFallLanding();
        int nextSignature = RouteWindowSignature();
        if (randomNearbyCastle)
        {
            if (UpdateNearbyRebuild()) changed = true;
            if (UpdateNearbyCastle(dt, changed)) changed = true;
        }
        else if (changed || nextSignature != routeSignature) { routeSignature = nextSignature; RebuildRoutes(); changed = true; }
        if (nextDecor != decorCell) { decorCell = nextDecor; changed = true; }
        if (changed) { PruneBlockedDecor(); RebuildDecor(); }
        Bounds playerBounds = PlayerSafetyBounds();
        int moving = 0;
        foreach (NearbyNode node in nearbyNodes) if (node.piece.IsMoving) moving++;
        bool wingsChanged = false;
        foreach (Node node in nodes.Values)
        {
            Vector3 previous = node.piece.transform.position;
            node.piece.Tick(dt, node.piece.ReservedBounds.Intersects(playerBounds));
            node.centre += node.piece.transform.position - previous; node.height = node.centre.y;
            if (node.piece.IsMoving) moving++;
            if (node.rebuilding && !node.piece.IsMoving)
            {
                node.rebuilding = false;
                Connect(node.cell, node.cell + Vector2Int.right, node.level); Connect(node.cell, node.cell + Vector2Int.up, node.level);
                Connect(node.cell - Vector2Int.right, node.cell, node.level); Connect(node.cell - Vector2Int.up, node.cell, node.level);
                wingsChanged = true;
            }
        }
        foreach (LandingDistrict district in landingDistricts)
            if (district.active) foreach (InfinityCastleBuilder piece in district.pieces) piece.Tick(dt, false);
        if (wingsChanged) { RefreshReservations(); PruneBlockedDecor(); }
        foreach (var pair in decor)
        {
            InfinityCastleBuilder piece = pair.Value;
            piece.Tick(dt, piece.ReservedBounds.Intersects(playerBounds)); moving++;
            if (!piece.IsMoving) moving--;
        }
        ProcessDecorLayout();
        CompleteCastleRebuild();
        director.Tick(dt, this, Mathf.Max(0, simultaneousShifts - moving), playerVelocity);
    }
    public bool DirectShift(Vector3 preferredDirection)
    {
        if (!initialized || IsRebuilding) return false;
        shiftSerial++;
        if (randomNearbyCastle) return TryShiftNearbyBuilding(preferredDirection);
        if (TryShiftOuterWing(preferredDirection)) return true;
        foreach (var pair in decor)
        {
            InfinityCastleBuilder piece = pair.Value;
            if (piece.IsMoving || (piece.WorldBounds.center - player.position).sqrMagnitude < decorSpacing * decorSpacing * 1.8f) continue;
            if (Hash(pair.Key, shiftSerial + 100) < Mathf.Clamp01(stillPieceChance)) continue;
            int start = Mathf.FloorToInt(Hash(pair.Key, shiftSerial + 200) * 6f);
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 offset = directions[(start + i) % directions.Length] * shiftDistance;
                Bounds sweep = piece.SweepTo(offset);
                if (!IsSpaceFree(sweep, piece)) continue;
                // Keep cumulative shifts within this cell's private envelope.
                Vector3 anchor = origin + (Vector3)pair.Key * decorSpacing;
                Vector3 endCentre = piece.WorldBounds.center + offset;
                if (Mathf.Abs(endCentre.x - anchor.x) > shiftDistance || Mathf.Abs(endCentre.y - anchor.y) > shiftDistance || Mathf.Abs(endCentre.z - anchor.z) > shiftDistance) continue;
                float speed = Mathf.Clamp(Mathf.Lerp(motionSpeedRange.x, motionSpeedRange.y, Hash(pair.Key, 31)), 0.6f, 3f);
                piece.BeginShift(offset, Mathf.Max(2f, shiftDistance / speed)); return true;
            }
        }
        return false;
    }
    private void PruneBlockedDecor()
    {
        if (IsRebuilding) return;
        removeDecor.Clear();
        foreach (var pair in decor)
        {
            bool blocked = HitsRoute(pair.Value.ReservedBounds);
            if (blocked) removeDecor.Add(pair.Key);
        }
        foreach (Vector3Int cell in removeDecor)
        { InfinityCastleBuilder piece = decor[cell]; piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
    }
    private bool TryShiftOuterWing(Vector3 preferredDirection)
    {
        if (mainCamera != null) GeometryUtility.CalculateFrustumPlanes(mainCamera, cameraPlanes);
        shiftCandidates.Clear();
        foreach (Node node in nodes.Values)
        {
            Vector2Int relative = node.cell - CellAt(player.position, node.level);
            if (node.rebuilding || (Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y)) <= 1 &&
                Mathf.Abs(node.level - routeLevel) <= 1)) continue;
            if (Hash(new Vector3Int(node.cell.x, 0, node.cell.y), shiftSerial + 81) < stillPieceChance) continue;
            float distance = (node.centre - player.position).sqrMagnitude;
            if (mainCamera != null && GeometryUtility.TestPlanesAABB(cameraPlanes, node.piece.WorldBounds)) distance *= 0.25f;
            node.score = distance; shiftCandidates.Add(node);
        }
        shiftCandidates.Sort((left, right) => left.score.CompareTo(right.score));
        foreach (Node candidate in shiftCandidates)
        {
            int first = 0; float bestDirection = float.NegativeInfinity;
            for (int i = 0; i < directions.Length; i++)
                if (Vector3.Dot(directions[i], preferredDirection) > bestDirection)
                { bestDirection = Vector3.Dot(directions[i], preferredDirection); first = i; }
            for (int direction = 0; direction < directions.Length; direction++)
            {
                Vector3 offset = directions[(first + direction) % directions.Length] * shiftDistance;
                Bounds sweep = CastleGeometry.Sweep(candidate.reservation, offset);
                if (!WingSweepIsClear(candidate, sweep)) continue;
                removeLinks.Clear();
                foreach (var pair in links)
                    if (pair.Key.level == candidate.level && (pair.Key.a == candidate.cell || pair.Key.b == candidate.cell)) removeLinks.Add(pair.Key);
                foreach (Edge edge in removeLinks)
                { Link link = links[edge]; link.root.SetActive(false); linkPool.Enqueue(link); links.Remove(edge); }
                candidate.rebuilding = true; candidate.reservation = sweep;
                candidate.piece.BeginShift(offset, Mathf.Min(4f, oneWayExitDuration)); RefreshReservations(); return true;
            }
        }
        return false;
    }
    private bool WingSweepIsClear(Node owner, Bounds sweep)
    {
        if (sweep.Intersects(PlayerSafetyBounds())) return false;
        foreach (Node node in nodes.Values) if (node != owner && node.reservation.Intersects(sweep)) return false;
        foreach (var pair in links)
        {
            if (pair.Key.level == owner.level && (pair.Key.a == owner.cell || pair.Key.b == owner.cell)) continue;
            if (pair.Value.bounds.Intersects(sweep)) return false;
        }
        foreach (InfinityCastleBuilder piece in decor.Values) if (piece.ReservedBounds.Intersects(sweep)) return false;
        if (HitsLandingDistrict(sweep)) return false;
        return true;
    }

    private void PrewarmLandingDistricts()
    {
        for (int districtIndex = 0; districtIndex < landingDistricts.Length; districtIndex++)
        {
            LandingDistrict district = new() { root = new GameObject("Pooled Landing District " + districtIndex).transform };
            district.root.SetParent(buildingsParent, false);
            for (int i = 0; i < district.pieces.Length; i++)
            {
                InfinityCastleBuilder piece = CreatePiece(buildingPrefabs[(i + districtIndex * 3) % buildingPrefabs.Count]);
                AddBalcony(piece, deckSize, false); piece.transform.SetParent(district.root, false);
                district.pieces[i] = piece;
                CastleWalkableSurface court = CreateSurface("Castle Courtyard " + i, routeSpacing - 0.02f,
                    routeSpacing - 0.02f, false, district.root);
                InfiniteCastleGrid zone = court.gameObject.AddComponent<InfiniteCastleGrid>();
                zone.Configure(Vector3.down, Vector3.up * 4.5f, new Vector3(routeSpacing, 9f, routeSpacing), 2);
                zone.enabled = false; district.courts[i] = court;
            }
            for (int i = 0; i < district.connections.Length; i++)
            { district.connections[i] = CreateLink(i); district.connections[i].root.transform.SetParent(district.root, false); }
            district.root.gameObject.SetActive(false); landingDistricts[districtIndex] = district;
        }
    }
    private bool HitsLandingDistrict(Bounds bounds, LandingDistrict ignore = null)
    {
        foreach (LandingDistrict district in landingDistricts)
            if (district != ignore && district.active && district.reservation.Intersects(bounds)) return true;
        return false;
    }
    private bool PrepareFallLanding()
    {
        if (randomNearbyCastle) return PrepareNearbyFallLanding();
        if (controller == null) return false;
        Vector3 up = gravity.Up;
        if (controller.Grounded || Vector3.Dot(up, lastFallUp) < 0.99f) fallPrepared = false;
        lastFallUp = up;
        if (controller.Grounded || controller.VerticalSpeed > -4f) return false;
        if (fallPrepared)
        {
            foreach (LandingDistrict district in landingDistricts)
            {
                if (!district.active || Vector3.Dot(district.up, up) < 0.99f) continue;
                Vector3 local = Quaternion.Inverse(CastleGeometry.Orientation(up)) * (player.position - district.target);
                if (local.y >= -0.2f && local.y < decorSpacing * catchLayersBelow &&
                    Mathf.Abs(local.x) < routeSpacing && Mathf.Abs(local.z) < routeSpacing) return false;
            }
            fallPrepared = false;
        }
        float drop = decorSpacing * 2f;
        float speed = Mathf.Max(0f, -controller.VerticalSpeed);
        float duration = (Mathf.Sqrt(speed * speed + 2f * gravity.Strength * drop) - speed) / gravity.Strength;
        Vector3 target = player.position - up * drop + Vector3.ProjectOnPlane(playerVelocity, up) * duration;
        if (Vector3.Dot(up, Vector3.up) > 0.99f)
            target.y = origin.y - (routeLevel + 2) * decorSpacing - HeightAt(Vector2Int.zero) - 0.35f;
        Quaternion basis = CastleGeometry.Orientation(up);
        Vector3 centre = target - basis * new Vector3(deckSize * 0.5f - 1.4f, 0f, 0f);
        float districtHeight = maximumBuildingHeight + (flightStair != null ? flightStair.rise : 0f);
        Bounds region = CastleGeometry.TransformBounds(new Bounds(Vector3.up * (districtHeight * 0.5f),
            new Vector3(routeSpacing * 3f + 1f, districtHeight + 2f, routeSpacing * 3f + 1f)),
            Matrix4x4.TRS(centre, basis, Vector3.one));
        if (region.Intersects(PlayerSafetyBounds())) return false;
        foreach (Node node in nodes.Values)
            if (node.reservation.Intersects(region) && node.piece.WorldBounds.Intersects(PlayerSafetyBounds())) return false;
        foreach (NearbyNode node in nearbyNodes)
            if (node.piece.ReservedBounds.Intersects(region) && (IsRiding(node) || node.zone.Contains(player.position))) return false;
        LandingDistrict selected = null;
        foreach (LandingDistrict district in landingDistricts)
        {
            if (district.active && district.reservation.Intersects(PlayerSafetyBounds()))
            { if (district.reservation.Intersects(region)) return false; continue; }
            if (selected == null || !district.active || district.generation < selected.generation) selected = district;
        }
        if (selected == null) return false;
        // Rescue districts have priority over distant decoration and unoccupied route cells.
        foreach (LandingDistrict district in landingDistricts)
            if (district != selected && district.active && district.reservation.Intersects(region))
            { district.active = false; district.root.gameObject.SetActive(false); }
        selected.active = false; selected.root.gameObject.SetActive(false);
        ClearNearbyForLanding(region);
        removeNodes.Clear();
        foreach (var pair in nodes) if (pair.Value.reservation.Intersects(region)) removeNodes.Add(pair.Key);
        removeLinks.Clear();
        foreach (var pair in links)
            if (pair.Value.bounds.Intersects(region) ||
                removeNodes.Contains(new Vector3Int(pair.Key.a.x, pair.Key.level, pair.Key.a.y)) ||
                removeNodes.Contains(new Vector3Int(pair.Key.b.x, pair.Key.level, pair.Key.b.y))) removeLinks.Add(pair.Key);
        foreach (Edge edge in removeLinks)
        { Link link = links[edge]; link.root.SetActive(false); linkPool.Enqueue(link); links.Remove(edge); }
        foreach (Vector3Int key in removeNodes)
        { Node node = nodes[key]; node.piece.gameObject.SetActive(false); nodePool.Enqueue(node); nodes.Remove(key); }
        selected.target = target; selected.up = up; selected.reservation = region;
        selected.generation = ++landingGeneration; selected.active = true;
        selected.root.gameObject.SetActive(true);
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            int index = (x + 1) * 3 + z + 1;
            Vector3 courtCentre = centre + basis * new Vector3(x * routeSpacing, 0f, z * routeSpacing);
            Vector3Int key = new(x, landingGeneration, z);
            Vector3 nodeCentre = courtCentre + basis * new Vector3((Hash(key, 61) - 0.5f) * 6f,
                HeightAt(new Vector2Int(x, z), landingGeneration), (Hash(key, 62) - 0.5f) * 6f);
            selected.centres[index] = nodeCentre;
            InfinityCastleBuilder piece = selected.pieces[index];
            Bounds local = CastleGeometry.RendererBoundsInRoot(piece.transform);
            Quaternion orientation = basis * Quaternion.Euler(0f, ((index + landingGeneration) % 4) * 90f, 0f);
            piece.Place(nodeCentre + up * (local.size.y * 0.5f - 0.24f), orientation);
            selected.courts[index].transform.SetPositionAndRotation(courtCentre - up * 0.02f, basis);
            InfiniteCastleGrid zone = selected.courts[index].GetComponent<InfiniteCastleGrid>();
            zone.gravityDirection = -up; zone.enabled = Vector3.Dot(up, Vector3.up) < 0.99f;
        }
        int connection = 0;
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            int index = (x + 1) * 3 + z + 1;
            Vector3 a = selected.centres[index];
            if (x < 1) ConfigureLink(selected.connections[connection++], a, selected.centres[index + 3], up, basis);
            if (z < 1) ConfigureLink(selected.connections[connection++], a, selected.centres[index + 1], up, basis);
        }
        fallPrepared = true;
        RefreshReservations(); PruneBlockedDecor(); Physics.SyncTransforms();
        return true;
    }
    private void ApplyLighting()
    {
        previousFog = RenderSettings.fog; previousFogColor = RenderSettings.fogColor; previousFogDensity = RenderSettings.fogDensity;
        previousAmbient = RenderSettings.ambientLight; previousAmbientMode = RenderSettings.ambientMode;
        previousFogMode = RenderSettings.fogMode;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.045f, 0.035f, 0.04f); RenderSettings.fogDensity = 1f / (decorSpacing * 2.2f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.32f, 0.25f, 0.22f);
        GameObject lamp = new("Castle Lantern Light"); lamp.transform.SetParent(player, false); lamp.transform.localPosition = new Vector3(0f, 3f, 0f);
        Light light = lamp.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1f, 0.76f, 0.48f);
        light.intensity = 5f; light.range = 24f; light.shadows = LightShadows.None;
    }
    public bool ValidateNoBuildingOverlaps(out string error)
    {
        var boxes = new List<Bounds>();
        foreach (Node node in nodes.Values) boxes.Add(node.piece.WorldBounds);
        foreach (NearbyNode node in nearbyNodes) boxes.Add(node.piece.WorldBounds);
        foreach (LandingDistrict district in landingDistricts)
            if (district.active) foreach (InfinityCastleBuilder piece in district.pieces) boxes.Add(piece.WorldBounds);
        foreach (InfinityCastleBuilder piece in decor.Values) boxes.Add(piece.WorldBounds);
        for (int i = 0; i < boxes.Count; i++) for (int j = i + 1; j < boxes.Count; j++)
            if (boxes[i].Intersects(boxes[j])) { error = $"Buildings {i} and {j} overlap."; return false; }
        error = null; return true;
    }
    public bool TryGetBalcony(Vector2Int cell, out Vector3 position)
    {
        Node node = GetNode(cell, routeLevel);
        position = node != null ? node.centre + LayerRotation(routeLevel) * new Vector3(deckSize * 0.5f - 1.4f, 0.08f, 0f) : default;
        return node != null;
    }
    public bool TryGetTraversal(Vector2Int a, Vector2Int b, out Vector3 start, out Vector3 end)
    {
        start = end = default;
        Node first = GetNode(a, routeLevel), second = GetNode(b, routeLevel);
        if (first == null || second == null) return false;
        Vector3 direction = Vector3.ProjectOnPlane(second.centre - first.centre, Vector3.up).normalized;
        Vector3 localDirection = Quaternion.Inverse(LayerRotation(routeLevel)) * direction;
        float distance = deckSize * 0.5f / Mathf.Max(Mathf.Abs(localDirection.x), Mathf.Abs(localDirection.z)) - 1f;
        start = first.centre + direction * distance + Vector3.up * 0.07f;
        end = second.centre - direction * distance + Vector3.up * 0.07f;
        return true;
    }
    public bool IsLandingDistrictSurface(Collider collider)
    {
        if (collider == null) return false;
        foreach (LandingDistrict district in landingDistricts)
            if (district.active && collider.transform.IsChildOf(district.root)) return true;
        return false;
    }
    public bool IsCastleSurface(Collider collider)
    {
        if (collider == null) return false;
        if (collider.GetComponentInParent<InfinityCastleBuilder>() != null || IsLandingDistrictSurface(collider)) return true;
        foreach (Link link in links.Values) if (collider.transform.IsChildOf(link.root.transform)) return true;
        foreach (NearbyLink connection in nearbyLinks)
            if (collider.transform.IsChildOf(connection.link.root.transform) ||
                (connection.turnLanding != null && collider.transform.IsChildOf(connection.turnLanding.root.transform))) return true;
        return false;
    }
    public bool TryGetLandingTarget(out Vector3 target, out Vector3 up)
    {
        if (HasNearbyLanding) { target = nearbyLanding.surface; up = nearbyLanding.up; return true; }
        foreach (LandingDistrict district in landingDistricts)
            if (district.active && district.generation == landingGeneration) { target = district.target; up = district.up; return true; }
        target = up = default; return false;
    }
    private void OnDestroy()
    {
        var uniqueMaterials = new HashSet<Material>(runtimeMaterials.Values);
        foreach (Material material in uniqueMaterials)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        if (mainCamera != null && initialized) { mainCamera.clearFlags = previousClearFlags; mainCamera.backgroundColor = previousBackground; }
        if (!initialized || !castleLighting) return;
        RenderSettings.fogMode = previousFogMode;
        RenderSettings.fog = previousFog; RenderSettings.fogColor = previousFogColor; RenderSettings.fogDensity = previousFogDensity;
        RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbient;
    }
}
