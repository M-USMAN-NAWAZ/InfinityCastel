using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-200)]
public class DynamicInfinityCastle : MonoBehaviour
{
    [Header("Player and Pool Parents")]
    public Transform player;
    public Transform buildingsParent, pathsParent;
    [Header("Your Artwork (Original Scale)")]
    public List<GameObject> buildingPrefabs = new();
    public List<GameObject> pathPrefabs = new();
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
    [Range(1, 3)] public int playableFloorsBelow = 2;
    [Range(0.1f, 1f)] public float decorationDensity = 0.48f;
    [Header("One-Way Shifting")]
    [Range(0f, 1f)] public float stillPieceChance = 0.45f;
    public float maxMotionRadius = 6f;
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

    private sealed class Node
    {
        public InfinityCastleBuilder piece;
        public Vector2Int cell;
        public Vector3 centre;
        public float height;
        public Bounds reservation;
        public bool rebuilding;
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
    }
    private readonly Queue<Node> nodePool = new();
    private readonly Queue<InfinityCastleBuilder> decorPool = new();
    private readonly Queue<Link> linkPool = new();
    private readonly Queue<CastleWalkableSurface> floorPool = new();
    private readonly Dictionary<Vector2Int, Node> nodes = new();
    private readonly Dictionary<Vector3Int, Node> lowerNodes = new();
    private readonly Dictionary<Vector3Int, InfinityCastleBuilder> decor = new();
    private readonly Dictionary<Edge, Link> links = new();
    private readonly Dictionary<Vector2Int, CastleWalkableSurface> floors = new();
    private readonly HashSet<Vector2Int> wantedNodes = new();
    private readonly HashSet<Vector3Int> wantedDecor = new();
    private readonly List<Bounds> reservations = new();
    private readonly List<Vector2Int> removeNodes = new();
    private readonly List<Vector3Int> removeDecor = new();
    private readonly List<Edge> removeLinks = new();
    private readonly Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    private ThirdPersonController controller;
    private InfinityGravityBody gravity;
    private Stair flatStair, flightStair;
    private Vector3 origin;
    private float deckSize, routeSpacing, decorSpacing, shiftDistance, floorTileSize;
    private float maximumBuildingHeight;
    private readonly Plane[] cameraPlanes = new Plane[6];
    private Vector2Int routeCell, floorCell;
    private Vector3Int decorCell;
    private Quaternion floorRotation;
    private Vector3 floorOrigin, floorUp = Vector3.up;
    private CastleWalkableSurface[] enclosure;
    private Camera mainCamera;
    private float shiftClock;
    private int shiftSerial;
    private int pieceSerial;
    private bool initialized;
    private static Mesh stairRampMesh;
    private bool previousFog;
    private float previousFogDensity;
    private Color previousFogColor, previousAmbient;
    private AmbientMode previousAmbientMode;
    private Material previousSkybox;
    private FogMode previousFogMode;
    private readonly Dictionary<Material, Material> runtimeMaterials = new();
    private CameraClearFlags previousClearFlags;
    private Color previousBackground;

    public float LayerSpacing => decorSpacing;
    public float RouteSpacing => routeSpacing;
    public int ActiveBuildingCount => nodes.Count + lowerNodes.Count + decor.Count;
    public int ActiveConnectionCount => links.Count;
    public int PoolCapacity { get; private set; }
    public bool IsInitialized => initialized;

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
        playableFloorsBelow = Mathf.Clamp(playableFloorsBelow, 1, Mathf.Min(3, catchLayersBelow - 1));
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
        floorTileSize = decorSpacing * 2f;
        int width = layersInEveryDirection * 2 + 1;
        int nodeCount = width * width * (playableFloorsBelow + 1);
        int decorCount = width * width * width;
        int reserveCount = Mathf.Clamp(extraPoolPieces, 2, 24);
        PoolCapacity = nodeCount + decorCount + reserveCount;
        for (int i = 0; i < nodeCount + reserveCount; i++)
        {
            InfinityCastleBuilder piece = i < samples.Count ? samples[i] : CreatePiece(buildingPrefabs[i % buildingPrefabs.Count]);
            AddBalcony(piece, deckSize, false);
            piece.gameObject.SetActive(false);
            nodePool.Enqueue(new Node { piece = piece });
        }
        for (int i = 0; i < decorCount; i++)
        {
            InfinityCastleBuilder piece = CreatePiece(buildingPrefabs[i % buildingPrefabs.Count]);
            Bounds local = CastleGeometry.RendererBoundsInRoot(piece.transform);
            AddBalcony(piece, Mathf.Max(local.size.x, local.size.z) + 3f, true);
            piece.gameObject.SetActive(false); decorPool.Enqueue(piece);
        }
        for (int i = 0; i < width * (width - 1) * 2 * (playableFloorsBelow + 1); i++) linkPool.Enqueue(CreateLink(i));
        for (int i = 0; i < 25; i++)
        {
            CastleWalkableSurface surface = CreateSurface("Lower Landing Hall " + i, floorTileSize, floorTileSize, false, buildingsParent);
            surface.gameObject.SetActive(false); floorPool.Enqueue(surface);
        }
        enclosure = new CastleWalkableSurface[6];
        for (int i = 0; i < enclosure.Length; i++)
            enclosure[i] = CreateSurface("Outer Castle Wall " + i, decorSpacing * (width + 2), decorSpacing * (width + 2), false, buildingsParent);
        routeCell = Vector2Int.zero; decorCell = Vector3Int.zero;
        floorUp = gravity.Up; floorRotation = CastleGeometry.Orientation(floorUp);
        floorOrigin = origin - floorUp * (decorSpacing * catchLayersBelow);
        RebuildRoutes(); UpdateFloor(true); UpdateEnclosure(); RebuildDecor();
        // Start at an open balcony port, not inside a tower's convex hull.
        Node start = nodes[Vector2Int.zero];
        player.position = start.centre + new Vector3(deckSize * 0.5f - 1.4f, 0.08f, 0f);
        if (controller != null) controller.ResetFallVelocity();
        if (castleLighting) ApplyLighting();
        Physics.SyncTransforms();
        initialized = true;
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
        CastleWalkableSurface surface = CreateSurface("Connected Timber Balcony", size, size, true, piece.transform);
        surface.transform.localPosition = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        if (localGravity)
        {
            InfiniteCastleGrid zone = surface.gameObject.AddComponent<InfiniteCastleGrid>();
            zone.Configure(Vector3.down, new Vector3(0f, 2.5f, 0f), new Vector3(size, 5f, size)); zone.enabled = false;
        }
        piece.InitializePooledPiece(this);
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
        root.SetActive(false); return link;
    }

    private float HeightAt(Vector2Int cell, int level = 0) => flightStair == null ? 0f : (Hash(new Vector3Int(cell.x, level, cell.y), 2) > 0.5f ? flightStair.rise : 0f);
    private Vector3 RoutePosition(Vector2Int cell, int level = 0)
    {
        float jitter = Mathf.Min(9f, irregularity * 16f);
        Vector3 offset = new((Hash(new Vector3Int(cell.x, level, cell.y), 11) - 0.5f) * jitter,
            HeightAt(cell, level) - HeightAt(Vector2Int.zero) - level * decorSpacing,
            (Hash(new Vector3Int(cell.x, level, cell.y), 12) - 0.5f) * jitter);
        return origin + new Vector3(cell.x * routeSpacing, 0f, cell.y * routeSpacing) + offset;
    }
    private void RebuildRoutes()
    {
        wantedNodes.Clear(); int r = layersInEveryDirection;
        for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++) wantedNodes.Add(routeCell + new Vector2Int(x, z));
        removeLinks.Clear();
        foreach (var pair in links)
            if (!wantedNodes.Contains(pair.Key.a) || !wantedNodes.Contains(pair.Key.b)) removeLinks.Add(pair.Key);
        foreach (Edge edge in removeLinks) { Link link = links[edge]; link.root.SetActive(false); linkPool.Enqueue(link); links.Remove(edge); }
        removeNodes.Clear();
        foreach (var pair in nodes) if (!wantedNodes.Contains(pair.Key)) removeNodes.Add(pair.Key);
        foreach (Vector2Int cell in removeNodes) { Node node = nodes[cell]; node.piece.gameObject.SetActive(false); nodePool.Enqueue(node); nodes.Remove(cell); }
        removeDecor.Clear();
        foreach (Vector3Int cell in lowerNodes.Keys)
            if (!wantedNodes.Contains(new Vector2Int(cell.x, cell.z))) removeDecor.Add(cell);
        foreach (Vector3Int cell in removeDecor)
        { Node node = lowerNodes[cell]; node.piece.gameObject.SetActive(false); nodePool.Enqueue(node); lowerNodes.Remove(cell); }
        // Only the missing far-edge row is filled. All retained nodes and links keep their transforms.
        foreach (Vector2Int cell in wantedNodes)
        {
            if (nodes.ContainsKey(cell)) continue;
            if (nodePool.Count == 0) throw new InvalidOperationException("Castle balcony pool exhausted.");
            Node node = PlaceNode(cell, 0); nodes.Add(cell, node);
        }
        foreach (Vector2Int cell in wantedNodes)
        {
            for (int level = 1; level <= playableFloorsBelow; level++)
            {
                Vector3Int key = new(cell.x, level, cell.y); if (lowerNodes.ContainsKey(key)) continue;
                lowerNodes.Add(key, PlaceNode(cell, level));
            }
        }
        foreach (var pair in nodes)
        {
            Connect(pair.Key, pair.Key + Vector2Int.right); Connect(pair.Key, pair.Key + Vector2Int.up);
            for (int level = 1; level <= playableFloorsBelow; level++)
            { Connect(pair.Key, pair.Key + Vector2Int.right, level); Connect(pair.Key, pair.Key + Vector2Int.up, level); }
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
        Node node = nodePool.Dequeue(); node.cell = cell; node.centre = RoutePosition(cell, level); node.height = node.centre.y;
        node.rebuilding = false;
        PlaceNodePiece(node, level);
        return node;
    }
    private void PlaceNodePiece(Node node, int level)
    {
        Bounds bounds = CastleGeometry.RendererBoundsInRoot(node.piece.transform);
        Vector3 centre = node.centre + Vector3.up * (bounds.size.y * 0.5f - 0.24f);
        Quaternion yaw = Quaternion.Euler(0f, Mathf.Floor(Hash(new Vector3Int(node.cell.x, level, node.cell.y), 19) * 4f) * 90f, 0f);
        node.reservation = new Bounds(node.centre + Vector3.up * (maximumBuildingHeight * 0.5f),
            new Vector3(deckSize + 1f, maximumBuildingHeight + 1f, deckSize + 1f));
        node.piece.Place(centre, yaw); node.piece.gameObject.SetActive(true);
    }
    private Node GetNode(Vector2Int cell, int level)
    {
        if (level == 0) { nodes.TryGetValue(cell, out Node node); return node; }
        lowerNodes.TryGetValue(new Vector3Int(cell.x, level, cell.y), out Node lower); return lower;
    }
    private void Connect(Vector2Int a, Vector2Int b, int level = 0)
    {
        Node low = GetNode(a, level), high = GetNode(b, level); if (low == null || high == null) return;
        if (low.rebuilding || high.rebuilding) return;
        Edge edge = new(a, b, level); if (links.ContainsKey(edge)) return;
        if (low.height > high.height) (low, high) = (high, low);
        Vector3 direction = Vector3.ProjectOnPlane(high.centre - low.centre, Vector3.up).normalized;
        float edgeDistance = deckSize * 0.5f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z));
        Vector3 start = low.centre + direction * (edgeDistance - 0.18f);
        Vector3 end = high.centre - direction * (edgeDistance - 0.18f);
        Link link = linkPool.Dequeue();
        bool step = high.height - low.height > 0.2f;
        Stair spec = step ? flightStair : flatStair; GameObject art = step ? link.flight : link.flat;
        if (link.flat != null) link.flat.SetActive(false); if (link.flight != null) link.flight.SetActive(false);
        link.ramp.SetActive(false); link.flatCollision.SetActive(false);
        Vector3 landingStart = start;
        if (spec != null && art != null)
        {
            Vector3 authoredForward = Vector3.ProjectOnPlane(spec.high - spec.low, Vector3.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(authoredForward, Vector3.up));
            art.transform.rotation = rotation * spec.prefab.transform.rotation;
            art.transform.position = start - rotation * spec.low;
            art.SetActive(true); landingStart = start + direction * spec.run + Vector3.up * spec.rise;
            if (step)
            {
                link.ramp.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction, Vector3.up));
                link.ramp.transform.localScale = new Vector3(Mathf.Max(1.2f, spec.width - 0.15f), spec.rise, spec.run);
                link.ramp.SetActive(true);
            }
            else
            {
                link.flatCollision.transform.SetPositionAndRotation((start + landingStart) * 0.5f, Quaternion.LookRotation(direction, Vector3.up));
                BoxCollider board = link.flatCollision.GetComponent<BoxCollider>();
                board.center = Vector3.down * 0.12f; board.size = new Vector3(Mathf.Max(1.2f, spec.width - 0.15f), 0.24f, spec.run + 0.36f);
                link.flatCollision.SetActive(true);
            }
        }
        float remaining = Vector3.ProjectOnPlane(end - landingStart, Vector3.up).magnitude;
        link.landing.transform.SetPositionAndRotation((landingStart + end) * 0.5f, Quaternion.LookRotation(direction, Vector3.up));
        link.landing.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.1f, remaining + (step ? 0.02f : 0.36f)));
        link.root.SetActive(true);
        link.bounds = CastleGeometry.TransformBounds(CastleGeometry.RendererBoundsInRoot(link.root.transform), link.root.transform.localToWorldMatrix);
        links.Add(edge, link);
    }
    private void RefreshReservations()
    {
        reservations.Clear();
        foreach (Node node in nodes.Values) reservations.Add(node.reservation);
        foreach (Node node in lowerNodes.Values) reservations.Add(node.reservation);
        foreach (Link link in links.Values) { Bounds bounds = link.bounds; bounds.Expand(0.5f); reservations.Add(bounds); }
    }
    private bool HitsRoute(Bounds box) { foreach (Bounds reservation in reservations) if (reservation.Intersects(box)) return true; return false; }
    private bool IsSpaceFree(Bounds swept, InfinityCastleBuilder ignore = null)
    {
        if (HitsRoute(swept) || swept.Intersects(PlayerSafetyBounds())) return false;
        foreach (InfinityCastleBuilder piece in decor.Values) if (piece != ignore && piece.ReservedBounds.Intersects(swept)) return false;
        foreach (CastleWalkableSurface floor in floors.Values) if (floor.WorldBounds.Intersects(swept)) return false;
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
        wantedDecor.Clear(); int r = layersInEveryDirection;
        for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
        {
            Vector3Int cell = decorCell + new Vector3Int(x, y, z);
            if (Hash(cell, 3) <= decorationDensity) wantedDecor.Add(cell);
        }
        removeDecor.Clear();
        foreach (Vector3Int cell in decor.Keys) if (!wantedDecor.Contains(cell)) removeDecor.Add(cell);
        foreach (Vector3Int cell in removeDecor)
        { InfinityCastleBuilder piece = decor[cell]; piece.StopShift(); piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
        foreach (Vector3Int cell in wantedDecor)
        {
            if (decor.ContainsKey(cell)) continue;
            if (decorPool.Count == 0) throw new InvalidOperationException("Castle decoration pool exhausted.");
            InfinityCastleBuilder piece = decorPool.Dequeue();
            float jitter = 14f * Mathf.Clamp01(irregularity);
            Vector3 position = origin + (Vector3)cell * decorSpacing + new Vector3(Hash(cell, 21) - 0.5f, Hash(cell, 22) - 0.5f, Hash(cell, 23) - 0.5f) * jitter;
            piece.Place(position, DecorOrientation(cell));
            Bounds margin = piece.WorldBounds; margin.Expand(1f);
            if (!IsSpaceFree(margin)) { decorPool.Enqueue(piece); continue; }
            InfiniteCastleGrid zone = piece.GetComponentInChildren<InfiniteCastleGrid>(true);
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
                    piece.Place(position + arrival, DecorOrientation(cell)); piece.BeginReveal(-arrival, 3f);
                }
                else piece.BeginReveal(Vector3.zero, 1f);
            }
            piece.gameObject.SetActive(true); decor.Add(cell, piece);
        }
    }

    private void Update() => SimulateCastle(Time.deltaTime);
    public void SimulateCastle(float dt)
    {
        if (!initialized || player == null) return;
        Vector3 delta = player.position - origin;
        Vector2Int nextRoute = new(Mathf.RoundToInt(delta.x / routeSpacing), Mathf.RoundToInt(delta.z / routeSpacing));
        Vector3Int nextDecor = new(Mathf.RoundToInt(delta.x / decorSpacing), Mathf.RoundToInt(delta.y / decorSpacing), Mathf.RoundToInt(delta.z / decorSpacing));
        bool changed = false;
        if (nextRoute != routeCell) { routeCell = nextRoute; RebuildRoutes(); changed = true; }
        if (nextDecor != decorCell) { decorCell = nextDecor; UpdateEnclosure(); changed = true; }
        bool floorChanged = UpdateFloor(false);
        if (changed || floorChanged) { PruneBlockedDecor(); RebuildDecor(); }
        Bounds playerBounds = PlayerSafetyBounds();
        int moving = 0;
        bool wingsChanged = false;
        foreach (Node node in nodes.Values)
        {
            node.piece.Tick(dt, node.piece.HasShifted && node.piece.ReservedBounds.Intersects(playerBounds));
            if (node.piece.IsMoving) moving++;
            if (node.piece.ReadyToPool)
            {
                Node spare = nodePool.Dequeue();
                node.piece.gameObject.SetActive(false);
                InfinityCastleBuilder retired = node.piece; node.piece = spare.piece; spare.piece = retired; nodePool.Enqueue(spare);
                PlaceNodePiece(node, 0); node.piece.BeginReveal(Vector3.zero, 1.2f); wingsChanged = true;
            }
            else if (node.rebuilding && !node.piece.IsMoving && !node.piece.HasShifted)
            {
                node.rebuilding = false;
                Connect(node.cell, node.cell + Vector2Int.right); Connect(node.cell, node.cell + Vector2Int.up);
                Connect(node.cell - Vector2Int.right, node.cell); Connect(node.cell - Vector2Int.up, node.cell);
                wingsChanged = true;
            }
        }
        if (wingsChanged) { RefreshReservations(); PruneBlockedDecor(); }
        removeDecor.Clear();
        foreach (var pair in decor)
        {
            InfinityCastleBuilder piece = pair.Value;
            piece.Tick(dt, piece.ReservedBounds.Intersects(playerBounds)); moving++;
            if (!piece.IsMoving) moving--;
            if (piece.ReadyToPool) removeDecor.Add(pair.Key);
        }
        foreach (Vector3Int cell in removeDecor)
        { InfinityCastleBuilder piece = decor[cell]; piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
        if (removeDecor.Count > 0) RebuildDecor();
        shiftClock -= dt;
        if (shiftClock <= 0f)
        {
            shiftClock = Mathf.Max(0.5f, shiftInterval);
            if (moving < simultaneousShifts) ScheduleShift();
        }
    }
    private void ScheduleShift()
    {
        shiftSerial++;
        if (TryShiftOuterWing()) return;
        foreach (var pair in decor)
        {
            InfinityCastleBuilder piece = pair.Value;
            if (piece.IsMoving || piece.HasShifted || (piece.WorldBounds.center - player.position).sqrMagnitude < decorSpacing * decorSpacing * 1.8f) continue;
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
                piece.BeginShift(offset, Mathf.Max(oneWayExitDuration, shiftDistance / speed)); return;
            }
        }
    }
    private void PruneBlockedDecor()
    {
        removeDecor.Clear();
        foreach (var pair in decor)
        {
            bool blocked = HitsRoute(pair.Value.ReservedBounds);
            if (!blocked) foreach (CastleWalkableSurface floor in floors.Values)
                if (floor.WorldBounds.Intersects(pair.Value.ReservedBounds)) { blocked = true; break; }
            if (blocked) removeDecor.Add(pair.Key);
        }
        foreach (Vector3Int cell in removeDecor)
        { InfinityCastleBuilder piece = decor[cell]; piece.gameObject.SetActive(false); decorPool.Enqueue(piece); decor.Remove(cell); }
    }
    private bool TryShiftOuterWing()
    {
        if (Vector3.Dot(gravity.Up, Vector3.up) < 0.99f || Mathf.Abs(player.position.y - origin.y) > decorSpacing * 0.7f) return false;
        if (mainCamera != null) GeometryUtility.CalculateFrustumPlanes(mainCamera, cameraPlanes);
        Node candidate = null; float score = float.PositiveInfinity;
        foreach (Node node in nodes.Values)
        {
            Vector2Int relative = node.cell - routeCell;
            if (node.rebuilding || Mathf.Max(Mathf.Abs(relative.x), Mathf.Abs(relative.y)) <= 2) continue;
            if (Hash(new Vector3Int(node.cell.x, 0, node.cell.y), shiftSerial + 81) < stillPieceChance) continue;
            float distance = (node.centre - player.position).sqrMagnitude;
            if (mainCamera != null && GeometryUtility.TestPlanesAABB(cameraPlanes, node.piece.WorldBounds)) distance *= 0.25f;
            if (distance < score) { score = distance; candidate = node; }
        }
        if (candidate == null) return false;
        int first = Mathf.FloorToInt(Hash(new Vector3Int(candidate.cell.x, 0, candidate.cell.y), shiftSerial + 92) * 6f);
        for (int direction = 0; direction < directions.Length; direction++)
        {
            Vector3 offset = directions[(first + direction) % directions.Length] * shiftDistance;
            Bounds sweep = CastleGeometry.Sweep(candidate.reservation, offset);
            if (!WingSweepIsClear(candidate, sweep)) continue;
            removeLinks.Clear();
            foreach (var pair in links)
                if (pair.Key.level == 0 && (pair.Key.a == candidate.cell || pair.Key.b == candidate.cell)) removeLinks.Add(pair.Key);
            foreach (Edge edge in removeLinks)
            { Link link = links[edge]; link.root.SetActive(false); linkPool.Enqueue(link); links.Remove(edge); }
            candidate.rebuilding = true; candidate.reservation = sweep;
            candidate.piece.BeginShift(offset, oneWayExitDuration); RefreshReservations(); return true;
        }
        return false;
    }
    private bool WingSweepIsClear(Node owner, Bounds sweep)
    {
        if (sweep.Intersects(PlayerSafetyBounds())) return false;
        foreach (Node node in nodes.Values) if (node != owner && node.reservation.Intersects(sweep)) return false;
        foreach (Node node in lowerNodes.Values) if (node.reservation.Intersects(sweep)) return false;
        foreach (var pair in links)
        {
            if (pair.Key.level == 0 && (pair.Key.a == owner.cell || pair.Key.b == owner.cell)) continue;
            if (pair.Value.bounds.Intersects(sweep)) return false;
        }
        foreach (InfinityCastleBuilder piece in decor.Values) if (piece.ReservedBounds.Intersects(sweep)) return false;
        foreach (CastleWalkableSurface floor in floors.Values) if (floor.WorldBounds.Intersects(sweep)) return false;
        return true;
    }

    private bool UpdateFloor(bool force)
    {
        Vector3 up = gravity.Up;
        if (Vector3.Dot(up, floorUp) < 0.99f)
        {
            floorUp = up; floorRotation = CastleGeometry.Orientation(up);
            floorOrigin = player.position - up * (decorSpacing * catchLayersBelow); force = true;
        }
        Vector3 local = Quaternion.Inverse(floorRotation) * (player.position - floorOrigin);
        Vector2Int next = new(Mathf.RoundToInt(local.x / floorTileSize), Mathf.RoundToInt(local.z / floorTileSize));
        // Pin the landing hall during a fall. It must never chase the falling player downward.
        if (controller != null && controller.Grounded && controller.Motor.GroundHit.collider != null &&
            controller.Motor.GroundHit.collider.GetComponent<CastleWalkableSurface>() == null &&
            Mathf.Abs(local.y - decorSpacing * catchLayersBelow) > decorSpacing)
        { floorOrigin += up * (local.y - decorSpacing * catchLayersBelow); force = true; }
        if (!force && next == floorCell) return false;
        floorCell = next;
        removeNodes.Clear();
        foreach (Vector2Int cell in floors.Keys)
            if (force || Mathf.Abs(cell.x - floorCell.x) > 2 || Mathf.Abs(cell.y - floorCell.y) > 2) removeNodes.Add(cell);
        foreach (Vector2Int cell in removeNodes)
        { CastleWalkableSurface floor = floors[cell]; floor.gameObject.SetActive(false); floorPool.Enqueue(floor); floors.Remove(cell); }
        for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
        {
            Vector2Int cell = floorCell + new Vector2Int(x, z); if (floors.ContainsKey(cell)) continue;
            CastleWalkableSurface floor = floorPool.Dequeue();
            floor.transform.SetPositionAndRotation(floorOrigin + floorRotation * new Vector3(cell.x * floorTileSize, 0f, cell.y * floorTileSize), floorRotation);
            floor.gameObject.SetActive(true); floors.Add(cell, floor);
        }
        Physics.SyncTransforms();
        return true;
    }
    private void UpdateEnclosure()
    {
        Vector3 centre = origin + (Vector3)decorCell * decorSpacing;
        float distance = decorSpacing * (layersInEveryDirection + 1f);
        for (int i = 0; i < enclosure.Length; i++)
            enclosure[i].transform.SetPositionAndRotation(centre + directions[i] * distance, CastleGeometry.Orientation(-directions[i]));
    }
    private void ApplyLighting()
    {
        previousFog = RenderSettings.fog; previousFogColor = RenderSettings.fogColor; previousFogDensity = RenderSettings.fogDensity;
        previousAmbient = RenderSettings.ambientLight; previousAmbientMode = RenderSettings.ambientMode; previousSkybox = RenderSettings.skybox;
        previousFogMode = RenderSettings.fogMode;
        RenderSettings.skybox = null; RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.045f, 0.035f, 0.04f); RenderSettings.fogDensity = 1f / (decorSpacing * 2.2f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.32f, 0.25f, 0.22f);
        if (mainCamera != null)
        {
            previousClearFlags = mainCamera.clearFlags; previousBackground = mainCamera.backgroundColor;
            mainCamera.clearFlags = CameraClearFlags.SolidColor; mainCamera.backgroundColor = RenderSettings.fogColor;
        }
        GameObject lamp = new("Castle Lantern Light"); lamp.transform.SetParent(player, false); lamp.transform.localPosition = new Vector3(0f, 3f, 0f);
        Light light = lamp.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1f, 0.76f, 0.48f);
        light.intensity = 5f; light.range = 24f; light.shadows = LightShadows.None;
    }
    public bool ValidateNoBuildingOverlaps(out string error)
    {
        var boxes = new List<Bounds>();
        foreach (Node node in nodes.Values) boxes.Add(node.piece.WorldBounds);
        foreach (Node node in lowerNodes.Values) boxes.Add(node.piece.WorldBounds);
        foreach (InfinityCastleBuilder piece in decor.Values) boxes.Add(piece.WorldBounds);
        for (int i = 0; i < boxes.Count; i++) for (int j = i + 1; j < boxes.Count; j++)
            if (boxes[i].Intersects(boxes[j])) { error = $"Buildings {i} and {j} overlap."; return false; }
        error = null; return true;
    }
    public bool TryGetBalcony(Vector2Int cell, out Vector3 position)
    { position = RoutePosition(cell) + new Vector3(deckSize * 0.5f - 1.4f, 0.08f, 0f); return nodes.ContainsKey(cell); }
    public bool TryGetTraversal(Vector2Int a, Vector2Int b, out Vector3 start, out Vector3 end)
    {
        start = end = default;
        if (!nodes.ContainsKey(a) || !nodes.ContainsKey(b)) return false;
        Vector3 direction = Vector3.ProjectOnPlane(nodes[b].centre - nodes[a].centre, Vector3.up).normalized;
        float distance = deckSize * 0.5f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z)) - 1f;
        start = nodes[a].centre + direction * distance + Vector3.up * 0.07f;
        end = nodes[b].centre - direction * distance + Vector3.up * 0.07f;
        return true;
    }
    private void OnDestroy()
    {
        var uniqueMaterials = new HashSet<Material>(runtimeMaterials.Values);
        foreach (Material material in uniqueMaterials)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        if (!initialized || !castleLighting) return;
        RenderSettings.fogMode = previousFogMode;
        RenderSettings.fog = previousFog; RenderSettings.fogColor = previousFogColor; RenderSettings.fogDensity = previousFogDensity;
        RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbient; RenderSettings.skybox = previousSkybox;
        if (mainCamera != null) { mainCamera.clearFlags = previousClearFlags; mainCamera.backgroundColor = previousBackground; }
    }
}
