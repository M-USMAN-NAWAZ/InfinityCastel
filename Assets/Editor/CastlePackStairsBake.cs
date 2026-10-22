using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Extract only the requested stairs; no Danish building meshes enter the generated castle.
public static class CastlePackStairsBake
{
    private const string Folder = "Assets/InfinityCastle/CastleModules/RuntimeMobile/";
    public static GameObject Prepare(string sourceName, string name, Shader shader)
    {
        GameObject pack = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Danish/inifinity_castle_pack.fbx");
        MeshFilter source = Array.Find(pack.GetComponentsInChildren<MeshFilter>(true), filter => filter.name == sourceName);
        if (source == null) throw new InvalidOperationException("Missing staircase in supplied FBX: " + sourceName);
        // Imported FBX meshes can be editor-readable but stripped in a player. A new mesh
        // explicitly retains CPU vertices for the non-uniform runtime collider cooking.
        Mesh original = source.sharedMesh;
        Mesh mesh = new() { name = name, vertices = original.vertices, normals = original.normals,
            uv = original.uv, tangents = original.tangents };
        var triangles = new List<int>();
        for (int i = 0; i < original.subMeshCount; i++) triangles.AddRange(original.GetTriangles(i));
        mesh.subMeshCount = 1; mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds;
        float scale = (sourceName == "stair_2" ? 4f : 2.4f) / Mathf.Min(bounds.size.x, bounds.size.z);
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - bounds.center) * scale;
        mesh.vertices = vertices; mesh.RecalculateBounds();
        string path = Folder + name + ".asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
        else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
        string materialPath = Folder + "PackStairTimber.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        material.SetTexture("_WoodMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/InfinityCastle/CastleTheme/Timber.png"));
        material.SetFloat("_ThemeStrength", 1f); material.SetFloat("_FinishMode", 1f); material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        GameObject root = new(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = saved;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            MeshCollider collider = root.AddComponent<MeshCollider>(); collider.sharedMesh = saved;
            if (sourceName == "stair_2")
            {
                // Both entrances are on the same end, on different lanes and at different heights.
                // A centre-axis probe (suitable for stair_3) cannot measure this U-shaped route.
                Physics.SyncTransforms();
                Bounds box = saved.bounds;
                bool Sample(float x, float z, out Vector3 point)
                {
                    bool found = collider.Raycast(new Ray(new Vector3(x, box.max.y + 1f, z), Vector3.down), out RaycastHit hit, box.size.y + 2f) && hit.normal.y > .9f;
                    point = hit.point; return found;
                }
                float left = Mathf.Lerp(box.min.x, box.max.x, .25f), right = Mathf.Lerp(box.min.x, box.max.x, .75f);
                var pairs = new List<(Vector3 left, Vector3 right)>();
                for (int i = 0; i <= 100; i++)
                {
                    float z = Mathf.Lerp(box.min.z + .12f, box.max.z - .12f, i / 100f);
                    if (Sample(left, z, out Vector3 a) && Sample(right, z, out Vector3 b)) pairs.Add((a, b));
                }
                if (pairs.Count == 0) throw new InvalidOperationException("Cannot measure stair_2 walking lanes: " + box);
                var entrance = pairs[0]; float gap = -1f;
                foreach (var pair in pairs)
                    if (Mathf.Abs(pair.left.y - pair.right.y) > gap)
                    { entrance = pair; gap = Mathf.Abs(pair.left.y - pair.right.y); }
                float landingHeight = float.PositiveInfinity;
                foreach (var pair in pairs)
                    if (Mathf.Abs(pair.left.y - pair.right.y) < .1f && pair.left.y > Mathf.Min(entrance.left.y, entrance.right.y) + 1f)
                        landingHeight = Mathf.Min(landingHeight, pair.left.y);
                Vector3 turnLeft = Vector3.zero, turnRight = Vector3.zero; int landingSamples = 0;
                foreach (var pair in pairs)
                    if (Mathf.Abs(pair.left.y - landingHeight) < .06f && Mathf.Abs(pair.right.y - landingHeight) < .06f)
                    { turnLeft += pair.left; turnRight += pair.right; landingSamples++; }
                if (landingSamples == 0) throw new InvalidOperationException("Cannot measure stair_2 turn landing.");
                Vector3[] route = { entrance.left, turnLeft / landingSamples, turnRight / landingSamples, entrance.right };
                if (route[0].y > route[3].y) Array.Reverse(route);
                if (Mathf.Abs(route[1].y - route[2].y) > .12f || route[3].y - route[0].y < 3f)
                    throw new InvalidOperationException("stair_2 landing/entrance measurement failed: " + string.Join(" -> ", route));
                CastleStairRoute metadata = root.AddComponent<CastleStairRoute>(); metadata.points = route; metadata.laneWidth = 1.75f;
                Debug.Log("Authored stair_2 route: " + string.Join(" -> ", route));
            }
            return PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
