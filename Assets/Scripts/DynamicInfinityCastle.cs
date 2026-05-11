using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DynamicInfinityCastle : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Parents")]
    public Transform buildingsParent;
    public Transform pathsParent;

    [Header("Prefabs")]
    public List<GameObject> buildingPrefabs = new();
    public List<GameObject> pathPrefabs = new();

    [Header("Layered Grid Pool")]
    public int layersInEveryDirection = 4;
    public float cellSize = 18f;
    public float verticalLayerSpacing = 7f;
    public int extraPoolPieces = 12;
    [Range(0f, 0.65f)] public float irregularity = 0.35f;
    public int randomSeed = 9217;

    [Header("Player Floor")]
    public bool keepFloorUnderPlayer = true;
    public float floorHeightBelowPlayer = 0.75f;
    public int catchLayersBelow = 5;
    public int catchRadius = 1;

    [Header("Abnormal Movement")]
    public float stillPieceChance = 0.45f;
    public float maxMotionRadius = 1.1f;
    public Vector2 motionSpeedRange = new(0.03f, 0.1f);
    public float oneWayExitDuration = 1.35f;

    [Header("Gravity Zones")]
    public bool enableGravityZones = true;
    public float gravityZoneChance = 0.15f;

    private readonly Queue<InfinityCastleBuilder> inactivePieces = new();
    private readonly Queue<GameObject> inactivePaths = new();
    private readonly Dictionary<Vector3Int, InfinityCastleBuilder> activeByCell = new();
    private readonly List<GameObject> activePaths = new();
    private readonly HashSet<Vector3Int> wantedCells = new();
    private readonly HashSet<Vector3Int> retiringCells = new();

    private Vector3Int currentPlayerCell;
    private float baseY;
    private float effectiveCellSize;
    private float effectiveVerticalLayerSpacing;
    private readonly List<GameObject> catchPlatforms = new();
    private readonly List<Vector3> catchPlatformOriginalScales = new();
    private bool hasBuiltInitialCastle;

    private void Start()
    {
        if (player == null)
            player = FindPlayer();

        if (buildingsParent == null)
            buildingsParent = transform;

        if (pathsParent == null)
            pathsParent = transform;

        if (enableGravityZones)
            EnsurePlayerGravityBody();

        baseY = player != null ? player.position.y : transform.position.y;
        effectiveCellSize = cellSize;
        effectiveVerticalLayerSpacing = verticalLayerSpacing;
        currentPlayerCell = GetPlayerCell();

        PrewarmPool();
        PrewarmPathPool();
        currentPlayerCell = GetPlayerCell();
        CreateCatchPlatforms();
        RebuildWantedCells();
        FillMissingCells();
        RebuildPathConnections();
        UpdateCatchPlatforms();
        hasBuiltInitialCastle = true;
    }

    private void Update()
    {
        if (player == null)
            return;

        Vector3Int nextCell = GetPlayerCell();
        if (nextCell != currentPlayerCell)
        {
            currentPlayerCell = nextCell;
            RebuildWantedCells();
            RecycleOutsideCells();
            FillMissingCells();
            RebuildPathConnections();
            UpdateCatchPlatforms();
        }
    }

    private void EnsurePlayerGravityBody()
    {
        if (player != null && player.GetComponent<InfinityGravityBody>() == null)
            player.gameObject.AddComponent<InfinityGravityBody>();
    }

    private Transform FindPlayer()
    {
        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
            return taggedPlayer.transform;

        CharacterController controller = FindFirstObjectByType<CharacterController>();
        return controller != null ? controller.transform : null;
    }

    private void PrewarmPool()
    {
        if (buildingPrefabs.Count == 0)
        {
            Debug.LogWarning("DynamicInfinityCastle needs building prefabs. Add your castle/stair prefabs to Building Prefabs.");
            return;
        }

        int wantedCount = CalculateWantedCellCount();
        int poolSize = wantedCount + Mathf.Max(0, extraPoolPieces);
        float largestFootprint = cellSize;
        float tallestPiece = verticalLayerSpacing;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject prefab = buildingPrefabs[i % buildingPrefabs.Count];
            GameObject instance = Instantiate(prefab, buildingsParent);
            instance.name = $"{prefab.name}_Pooled_{i:00}";
            instance.SetActive(false);

            InfinityCastleBuilder piece = instance.GetComponent<InfinityCastleBuilder>();
            if (piece == null)
                piece = instance.AddComponent<InfinityCastleBuilder>();

            piece.InitializePooledPiece(this);
            inactivePieces.Enqueue(piece);

            Bounds bounds = CalculateObjectBounds(instance);
            largestFootprint = Mathf.Max(largestFootprint, bounds.size.x, bounds.size.z);
            tallestPiece = Mathf.Max(tallestPiece, bounds.size.y);
        }

        effectiveCellSize = largestFootprint * 1.15f;
        effectiveVerticalLayerSpacing = tallestPiece * 1.15f;
    }

    private void PrewarmPathPool()
    {
        if (pathPrefabs.Count == 0)
            return;

        int pathPoolSize = CalculateWantedCellCount() * 3;
        for (int i = 0; i < pathPoolSize; i++)
        {
            GameObject prefab = pathPrefabs[i % pathPrefabs.Count];
            GameObject path = Instantiate(prefab, pathsParent);
            path.name = $"{prefab.name}_PathPooled_{i:00}";
            path.SetActive(false);
            inactivePaths.Enqueue(path);
        }
    }

    private Bounds CalculateObjectBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(instance.transform.position, Vector3.one * cellSize);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    private int CalculateWantedCellCount()
    {
        int radius = Mathf.Max(1, layersInEveryDirection);
        int width = radius * 2 + 1;
        int perimeter = width * width - Mathf.Max(0, width - 2) * Mathf.Max(0, width - 2);
        int verticalCoverage = width * width * 2;
        int interiorBridges = Mathf.Max(4, radius * 4);
        return perimeter + verticalCoverage + interiorBridges;
    }

    private void RebuildWantedCells()
    {
        wantedCells.Clear();

        int radius = Mathf.Max(1, layersInEveryDirection);
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                bool edge = Mathf.Abs(x) == radius || Mathf.Abs(z) == radius;
                Vector3Int flat = new(currentPlayerCell.x + x, currentPlayerCell.y, currentPlayerCell.z + z);

                for (int y = 1; y <= Mathf.Max(1, catchLayersBelow); y++)
                    wantedCells.Add(new Vector3Int(flat.x, currentPlayerCell.y - y, flat.z));

                if (ShouldKeepIrregularCell(new Vector3Int(flat.x, currentPlayerCell.y + 1, flat.z), 0.25f))
                    wantedCells.Add(new Vector3Int(flat.x, currentPlayerCell.y + 1, flat.z));

                if (edge)
                {
                    if (ShouldKeepIrregularCell(flat, 0f))
                        wantedCells.Add(flat);
                }
            }
        }

        for (int i = -radius; i <= radius; i++)
        {
            wantedCells.Add(new Vector3Int(currentPlayerCell.x + i, currentPlayerCell.y, currentPlayerCell.z));
            wantedCells.Add(new Vector3Int(currentPlayerCell.x, currentPlayerCell.y, currentPlayerCell.z + i));
        }

        wantedCells.Remove(currentPlayerCell);
    }

    private bool ShouldKeepIrregularCell(Vector3Int cell, float extraKeepChance)
    {
        Vector3Int delta = cell - currentPlayerCell;
        int distance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));

        if (distance <= 1)
            return true;

        float keepChance = Mathf.Clamp01(1f - irregularity + extraKeepChance);
        return Hash01(cell) <= keepChance;
    }

    private float Hash01(Vector3Int cell)
    {
        unchecked
        {
            int hash = randomSeed;
            hash = hash * 73856093 ^ cell.x;
            hash = hash * 19349663 ^ cell.y;
            hash = hash * 83492791 ^ cell.z;
            hash ^= hash >> 13;
            hash *= 1274126177;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private void RecycleOutsideCells()
    {
        List<Vector3Int> remove = new();
        foreach (Vector3Int cell in activeByCell.Keys)
        {
            if (!wantedCells.Contains(cell))
                remove.Add(cell);
        }

        foreach (Vector3Int cell in remove)
        {
            InfinityCastleBuilder piece = activeByCell[cell];
            activeByCell.Remove(cell);
            retiringCells.Add(cell);
            StartCoroutine(ExitAndPoolPiece(piece, cell));
        }
    }

    private void RebuildPathConnections()
    {
        while (activePaths.Count > 0)
        {
            GameObject path = activePaths[^1];
            activePaths.RemoveAt(activePaths.Count - 1);
            path.SetActive(false);
            inactivePaths.Enqueue(path);
        }

        Vector3Int[] directions =
        {
            Vector3Int.right,
            new(0, 0, 1),
            Vector3Int.up
        };

        foreach (Vector3Int cell in activeByCell.Keys)
        {
            foreach (Vector3Int direction in directions)
            {
                Vector3Int neighbor = cell + direction;
                if (!activeByCell.ContainsKey(neighbor))
                    continue;

                if (!SameGravity(cell, neighbor))
                    continue;

                ActivatePath(cell, neighbor);
            }
        }
    }

    private bool SameGravity(Vector3Int a, Vector3Int b)
    {
        return Vector3.Dot(PickGravityDirection(a).normalized, PickGravityDirection(b).normalized) > 0.98f;
    }

    private void ActivatePath(Vector3Int fromCell, Vector3Int toCell)
    {
        if (inactivePaths.Count == 0)
            return;

        GameObject path = inactivePaths.Dequeue();
        Vector3 from = CellToWorld(fromCell);
        Vector3 to = CellToWorld(toCell);
        Vector3 delta = to - from;
        Vector3 gravity = PickGravityDirection(fromCell).normalized;
        Vector3 up = -gravity;
        Vector3 forward = Vector3.ProjectOnPlane(delta.normalized, up);

        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);

        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);

        path.transform.position = (from + to) * 0.5f;
        path.transform.rotation = Quaternion.LookRotation(forward.normalized, up);
        path.SetActive(true);
        activePaths.Add(path);
    }

    private IEnumerator ExitAndPoolPiece(InfinityCastleBuilder piece, Vector3Int cell)
    {
        Vector3 exitDirection = FindOpenDirection(cell);
        float distance = cell.y == currentPlayerCell.y ? effectiveCellSize : effectiveVerticalLayerSpacing;

        piece.BeginExit(exitDirection * distance * 0.9f, oneWayExitDuration);
        yield return new WaitForSeconds(oneWayExitDuration);

        piece.gameObject.SetActive(false);
        inactivePieces.Enqueue(piece);
        retiringCells.Remove(cell);
    }

    private Vector3 FindOpenDirection(Vector3Int cell)
    {
        Vector3Int[] directions =
        {
            Vector3Int.right,
            Vector3Int.left,
            new(0, 0, 1),
            new(0, 0, -1),
            Vector3Int.up,
            Vector3Int.down
        };

        Vector3Int fromPlayer = cell - currentPlayerCell;
        Vector3 preferred = new(fromPlayer.x, fromPlayer.y, fromPlayer.z);

        foreach (Vector3Int direction in directions)
        {
            Vector3Int neighbor = cell + direction;
            if (!activeByCell.ContainsKey(neighbor) && !wantedCells.Contains(neighbor) && !retiringCells.Contains(neighbor))
                return CellDirectionToWorld(direction);
        }

        if (preferred.sqrMagnitude > 0.01f)
            return preferred.normalized;

        return Vector3.up;
    }

    private Vector3 CellDirectionToWorld(Vector3Int direction)
    {
        return new Vector3(direction.x, direction.y, direction.z).normalized;
    }

    private void FillMissingCells()
    {
        foreach (Vector3Int cell in wantedCells)
        {
            if (activeByCell.ContainsKey(cell))
                continue;

            if (inactivePieces.Count == 0)
                return;

            InfinityCastleBuilder piece = inactivePieces.Dequeue();
            PlacePieceAtCell(piece, cell);
            activeByCell[cell] = piece;
        }
    }

    private void PlacePieceAtCell(InfinityCastleBuilder piece, Vector3Int cell)
    {
        piece.gameObject.SetActive(true);

        Vector3 position = CellToWorld(cell);
        Quaternion rotation = PickRotationForCell(cell);

        piece.SetFixedRotation(rotation);
        piece.AssignSlot(
            position,
            cellSize * 0.45f,
            PickMotionMode(cell),
            Random.Range(motionSpeedRange.x, motionSpeedRange.y),
            maxMotionRadius,
            hasBuiltInitialCastle ? GetArrivalOffset(cell) : Vector3.zero);

        ApplyRandomGravityZone(piece.gameObject, cell);
    }

    private Vector3 CellToWorld(Vector3Int cell)
    {
        float y = baseY + cell.y * effectiveVerticalLayerSpacing;

        return new Vector3(cell.x * effectiveCellSize, y, cell.z * effectiveCellSize);
    }

    private Quaternion PickRotationForCell(Vector3Int cell)
    {
        Vector3 gravityDirection = PickGravityDirection(cell).normalized;
        Vector3 up = -gravityDirection;

        Vector3 fromCenter = new(cell.x - currentPlayerCell.x, 0f, cell.z - currentPlayerCell.z);
        Vector3 forward = fromCenter.sqrMagnitude > 0.01f ? -fromCenter.normalized : Vector3.forward;
        forward = Vector3.ProjectOnPlane(forward, up);

        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);

        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);

        return Quaternion.LookRotation(forward.normalized, up);
    }

    private CastleMotionMode PickMotionMode(Vector3Int cell)
    {
        return CastleMotionMode.Still;
    }

    private Vector3 GetArrivalOffset(Vector3Int cell)
    {
        Vector3Int delta = cell - currentPlayerCell;

        if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x) && Mathf.Abs(delta.y) >= Mathf.Abs(delta.z) && delta.y != 0)
            return Vector3.up * Mathf.Sign(delta.y) * effectiveVerticalLayerSpacing * 0.75f;

        Vector3 planar = new(delta.x, 0f, delta.z);
        if (planar.sqrMagnitude < 0.01f)
            return Vector3.zero;

        return planar.normalized * effectiveCellSize * 0.75f;
    }

    private void ApplyRandomGravityZone(GameObject piece, Vector3Int cell)
    {
        InfiniteCastleGrid zone = piece.GetComponent<InfiniteCastleGrid>();
        if (zone == null)
            zone = piece.AddComponent<InfiniteCastleGrid>();

        if (!enableGravityZones)
        {
            zone.enabled = false;
            return;
        }

        zone.gravityDirection = PickGravityDirection(cell);
        zone.gravityStrength = 28f;
        zone.alignPlayerToGravity = true;
        zone.enabled = Random.value < gravityZoneChance;
    }

    private Vector3 PickGravityDirection(Vector3Int cell)
    {
        Vector3[] directions =
        {
            Vector3.down,
            Vector3.down,
            Vector3.up,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };

        int index = Mathf.FloorToInt(Hash01(cell) * directions.Length);
        return directions[Mathf.Clamp(index, 0, directions.Length - 1)];
    }

    private void CreateCatchPlatforms()
    {
        if (!keepFloorUnderPlayer || catchPlatforms.Count > 0)
            return;

        GameObject prefab = pathPrefabs.Count > 0 ? pathPrefabs[0] : null;
        if (prefab == null && buildingPrefabs.Count > 0)
            prefab = buildingPrefabs[0];

        if (prefab == null)
            return;

        int radius = Mathf.Max(0, catchRadius);
        int count = Mathf.Max(1, catchLayersBelow) * (radius * 2 + 1) * (radius * 2 + 1);

        for (int i = 0; i < count; i++)
        {
            GameObject platform = Instantiate(prefab, transform);
            platform.name = $"InfinityCastle_CatchPlatform_{i:00}";
            platform.layer = 0;
            platform.SetActive(true);
            catchPlatforms.Add(platform);
            catchPlatformOriginalScales.Add(platform.transform.localScale);
        }
    }

    private void UpdateCatchPlatforms()
    {
        if (catchPlatforms.Count == 0 || player == null)
            return;

        int index = 0;
        int radius = Mathf.Max(0, catchRadius);

        for (int layer = 1; layer <= Mathf.Max(1, catchLayersBelow); layer++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    if (index >= catchPlatforms.Count)
                        return;

                    GameObject platform = catchPlatforms[index];
                    platform.transform.position = new Vector3(
                        (currentPlayerCell.x + x) * effectiveCellSize,
                        baseY + (currentPlayerCell.y - layer) * effectiveVerticalLayerSpacing - floorHeightBelowPlayer,
                        (currentPlayerCell.z + z) * effectiveCellSize);
                    platform.transform.rotation = Quaternion.identity;
                    platform.transform.localScale = catchPlatformOriginalScales[index];
                    index++;
                }
            }
        }
    }

    private Vector3Int GetPlayerCell()
    {
        Vector3 position = player != null ? player.position : transform.position;
        float xzSpacing = effectiveCellSize > 0f ? effectiveCellSize : cellSize;
        float ySpacing = effectiveVerticalLayerSpacing > 0f ? effectiveVerticalLayerSpacing : verticalLayerSpacing;

        return new Vector3Int(
            Mathf.RoundToInt(position.x / xzSpacing),
            Mathf.RoundToInt((position.y - baseY) / ySpacing),
            Mathf.RoundToInt(position.z / xzSpacing));
    }
}
