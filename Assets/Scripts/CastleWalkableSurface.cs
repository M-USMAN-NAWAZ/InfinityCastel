using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CastleWalkableSurface : MonoBehaviour
{
    private static readonly Dictionary<Vector3, Mesh> meshes = new();
    private BoxCollider floor;
    public float Width { get; private set; }
    public float Depth { get; private set; }
    public Bounds WorldBounds => floor.bounds;

    public void Initialize(float width, float depth, Material wood, bool railings)
    {
        Width = width; Depth = depth;
        Vector3 key = new(width, depth, railings ? 1f : 0f);
        if (!meshes.TryGetValue(key, out Mesh mesh))
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            AddBox(vertices, triangles, uv, new Vector3(0f, -0.12f, 0f), new Vector3(width, 0.24f, depth));
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
            mesh = new Mesh { name = railings ? "Castle Timber Balcony" : "Castle Timber Deck" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(key, mesh);
        }
        MeshFilter filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = GetComponent<MeshRenderer>(); if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = wood;
        floor = GetComponent<BoxCollider>(); if (floor == null) floor = gameObject.AddComponent<BoxCollider>();
        floor.center = new Vector3(0f, -0.12f, 0f); floor.size = new Vector3(width, 0.24f, depth);
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
