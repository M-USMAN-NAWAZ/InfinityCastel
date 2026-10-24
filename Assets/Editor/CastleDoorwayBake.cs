using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CastleDoorwayBake
{
    private const string Folder = "Assets/InfinityCastle/CastleModules/RuntimeMobile/";
    public static void Configure(DynamicInfinityCastle castle)
    {
        // A single opaque, instanced panel, with a timber lattice and paper centre. No extra lights or renderers.
        const int width = 128, height = 256;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            bool frame = x < 7 || x >= width - 7 || y < 8 || y >= height - 8 || Mathf.Abs(x - width / 2) < 3 ||
                x % 32 < 2 || y % 40 < 3;
            float grain = Mathf.PerlinNoise(x * .19f, y * .11f);
            Color colour = frame ? Color.Lerp(new Color(.12f,.055f,.025f), new Color(.28f,.14f,.055f), grain) :
                Color.Lerp(new Color(.58f,.47f,.31f), new Color(.79f,.68f,.48f), grain * .35f + .4f);
            pixels[y * width + x] = colour;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "ClosedShoji.asset");
        if (texture == null)
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Closed Shoji", wrapMode = TextureWrapMode.Clamp };
            AssetDatabase.CreateAsset(texture, Folder + "ClosedShoji.asset");
        }
        texture.SetPixels32(pixels); texture.Apply(); EditorUtility.SetDirty(texture);
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "ClosedShoji.mat");
        if (material == null) { material = new Material(castle.architectureShader); AssetDatabase.CreateAsset(material, Folder + "ClosedShoji.mat"); }
        material.SetTexture("_BaseMap", texture); material.SetFloat("_Glow", .035f); material.enableInstancing = true; EditorUtility.SetDirty(material);
        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(CastleRetopologyBake.Atlas);
        if (atlas != null)
        {
            material.SetTexture("_BaseMap", atlas); material.SetFloat("_ThemeStrength", 0f);
            material.SetFloat("_AtlasEmission", 0f);
        }
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Mesh mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh); mesh.name = "Closed Shoji Panel";
            if (atlas != null)
            {
                Vector2[] uv = mesh.uv;
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(.008f + uv[i].x * .234f, .008f + uv[i].y * .234f);
                mesh.uv = uv;
            }
            // Share the room's construction clock so gates don't float before their room is assembled.
            var pivots = new List<Vector4>(); var sizes = new List<Vector2>();
            for (int i = 0; i < mesh.vertexCount; i++) { pivots.Add(new Vector4(0f, 0f, 0f, 1.12f)); sizes.Add(new Vector2(1.4f,0f)); }
            mesh.SetUVs(1, pivots); mesh.SetUVs(2, sizes);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "ClosedShojiMesh.asset");
            if (saved == null) { AssetDatabase.CreateAsset(mesh, Folder + "ClosedShojiMesh.asset"); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            castle.closedShojiMesh = saved; castle.closedShojiMaterial = material;
        }
        finally { Object.DestroyImmediate(cube); }
    }
}
