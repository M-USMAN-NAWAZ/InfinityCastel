using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CastleWalkableSurface : MonoBehaviour
{
    private static readonly Dictionary<(float, float, bool, float, float), Mesh> meshes = new();
    private BoxCollider floor;
    private readonly BoxCollider[] perimeterFloors = new BoxCollider[4];
    public float Width { get; private set; }
    public float Depth { get; private set; }
    public bool HasCentralOpening { get; private set; }
    public Bounds WorldBounds => CastleGeometry.TransformBounds(new Bounds(Vector3.down * .12f,
        new Vector3(Width, .24f, Depth)), transform.localToWorldMatrix);

    public float DistanceToSurface(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        float nearest = float.PositiveInfinity;
        foreach (BoxCollider board in perimeterFloors)
        {
            if (board == null || !board.enabled) continue;
            Vector3 half = board.size * .5f;
            Vector3 face = new(Mathf.Clamp(local.x, board.center.x - half.x, board.center.x + half.x), 0f,
                Mathf.Clamp(local.z, board.center.z - half.z, board.center.z + half.z));
            nearest = Mathf.Min(nearest, (point - transform.TransformPoint(face)).sqrMagnitude);
        }
        return Mathf.Sqrt(nearest);
    }

    public void Initialize(float width, float depth, Material wood, bool railings) =>
        InitializeGeometry(width, depth, wood, railings, Vector2.zero);
    public void InitializePerimeter(float width, float depth, Vector2 opening, Material wood, bool railings) =>
        InitializeGeometry(width, depth, wood, railings,
            new Vector2(Mathf.Clamp(opening.x, 1f, width - 2f), Mathf.Clamp(opening.y, 1f, depth - 2f)));
    private void InitializeGeometry(float width, float depth, Material wood, bool railings, Vector2 opening)
    {
        Width = width; Depth = depth;
        HasCentralOpening = opening.x > 0f && opening.y > 0f;
        var key = (width, depth, railings, opening.x, opening.y);
        var centres = new Vector3[4]; var sizes = new Vector3[4];
        int boards = HasCentralOpening ? 4 : 1;
        if (HasCentralOpening)
        {
            float side = (width - opening.x) * .5f, end = (depth - opening.y) * .5f;
            centres[0] = new Vector3(-(opening.x + side) * .5f, -.12f, 0f);
            centres[1] = new Vector3((opening.x + side) * .5f, -.12f, 0f);
            centres[2] = new Vector3(0f, -.12f, -(opening.y + end) * .5f);
            centres[3] = new Vector3(0f, -.12f, (opening.y + end) * .5f);
            sizes[0] = sizes[1] = new Vector3(side, .24f, depth);
            sizes[2] = sizes[3] = new Vector3(opening.x, .24f, end);
        }
        else { centres[0] = Vector3.down * .12f; sizes[0] = new Vector3(width, .24f, depth); }
        if (!meshes.TryGetValue(key, out Mesh mesh))
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            for (int board = 0; board < boards; board++) AddBox(vertices, triangles, uv, centres[board], sizes[board]);
            if (railings)
            {
                for (int side = 0; side < 4; side++)
                {
                    bool xEdge = side < 2;
                    float length = xEdge ? depth : width;
                    float edge = (xEdge ? width : depth) * (side % 2 == 0 ? 0.5f : -0.5f);
                    for (int half = -1; half <= 1; half += 2)
                    {
                        float segment = Mathf.Max(0f, length * 0.5f - 2.5f);
                        Vector3 centre = xEdge ? new Vector3(edge, 1f, half * (2.5f + segment * 0.5f)) : new Vector3(half * (2.5f + segment * 0.5f), 1f, edge);
                        Vector3 size = xEdge ? new Vector3(0.1f, 0.12f, segment) : new Vector3(segment, 0.12f, 0.1f);
                        AddBox(vertices, triangles, uv, centre, size);
                        AddBox(vertices, triangles, uv, centre - Vector3.up * 0.5f, size);
                        for (float at = 2.6f; at < length * 0.5f; at += 2f)
                        {
                            Vector3 post = xEdge ? new Vector3(edge, 0.52f, half * at) : new Vector3(half * at, 0.52f, edge);
                            AddBox(vertices, triangles, uv, post, new Vector3(0.12f, 1.1f, 0.12f));
                        }
                    }
                }
            }
            mesh = new Mesh { name = HasCentralOpening ? "Castle Perimeter Walkway" : railings ? "Castle Timber Balcony" : "Castle Timber Deck" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(key, mesh);
        }
        MeshFilter filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = GetComponent<MeshRenderer>(); if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = wood;
        floor = GetComponent<BoxCollider>(); if (floor == null) floor = gameObject.AddComponent<BoxCollider>();
        perimeterFloors[0] = floor;
        for (int board = 0; board < perimeterFloors.Length; board++)
        {
            if (board < boards)
            {
                if (perimeterFloors[board] == null) perimeterFloors[board] = gameObject.AddComponent<BoxCollider>();
                perimeterFloors[board].enabled = true;
                perimeterFloors[board].center = centres[board]; perimeterFloors[board].size = sizes[board];
            }
            else if (perimeterFloors[board] != null) perimeterFloors[board].enabled = false;
        }
        // Open ports are five metres wide; rail colliders do not block attached stairs.
        if (railings)
            for (int side = 0; side < 4; side++)
                for (int half = -1; half <= 1; half += 2)
                {
                    bool xEdge = side < 2;
                    float length = xEdge ? depth : width;
                    float segment = Mathf.Max(0.01f, length * 0.5f - 2.5f);
                    float edge = (xEdge ? width : depth) * (side % 2 == 0 ? 0.5f : -0.5f);
                    BoxCollider rail = gameObject.AddComponent<BoxCollider>();
                    rail.center = xEdge ? new Vector3(edge, 0.52f, half * (2.5f + segment * 0.5f)) : new Vector3(half * (2.5f + segment * 0.5f), 0.52f, edge);
                    rail.size = xEdge ? new Vector3(0.12f, 1.1f, segment) : new Vector3(segment, 1.1f, 0.12f);
                }
    }

    private static void AddBox(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, Vector3 centre, Vector3 size)
    {
        Vector3 e = size * 0.5f;
        Vector3[] corners = {
            new(-e.x,-e.y,-e.z),new(e.x,-e.y,-e.z),new(e.x,-e.y,e.z),new(-e.x,-e.y,e.z),
            new(-e.x,e.y,-e.z),new(e.x,e.y,-e.z),new(e.x,e.y,e.z),new(-e.x,e.y,e.z)};
        int[] faces = {4,7,6,5, 0,1,2,3, 0,4,5,1, 1,5,6,2, 2,6,7,3, 3,7,4,0};
        for (int face = 0; face < 6; face++)
        {
            int start = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                Vector3 v = corners[faces[face * 4 + i]]; vertices.Add(centre + v);
                uv.Add(face < 2 ? new Vector2(v.x, v.z) * 0.5f : new Vector2(v.x + v.z, v.y) * 0.5f);
            }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
    }
}
