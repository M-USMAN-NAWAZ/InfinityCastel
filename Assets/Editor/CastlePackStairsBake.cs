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
        float scale = 2.4f / Mathf.Min(bounds.size.x, bounds.size.z);
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
            root.AddComponent<MeshCollider>().sharedMesh = saved;
            return PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
