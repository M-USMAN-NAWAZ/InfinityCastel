using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// A single atlas and exported opening manifest accompany the clean Blender kit.
// Legacy/source models keep their existing materials and measured openings.
public static class CastleRetopologyBake
{
    public const string Folder = "Assets/InfinityCastle/CastleModules/Retopology/";
    public const string Atlas = Folder + "CastleTrimAlbedo.png";
    [Serializable] private sealed class Manifest { public Module[] modules; }
    [Serializable] private sealed class Module
    {
        public string name;
        public int kind;
        public Vector3 minimum, maximum, floorPoint, roofPoint;
        public Vector2 floorSize, roofSize;
        public CastleArchitectureModule.Port[] ports;
    }

    public static bool IsRetopology(GameObject model)
    {
        MeshFilter mesh = model != null ? model.GetComponentInChildren<MeshFilter>(true) : null;
        return mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.name.StartsWith("Retopo_", StringComparison.Ordinal);
    }

    public static string TextureFor(GameObject model, string legacy) => IsRetopology(model) ? Atlas : legacy;

    public static void PrepareTexture()
    {
        var importer = AssetImporter.GetAtPath(Atlas) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing shared retopology atlas: " + Atlas);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
        importer.maxTextureSize = 1024; importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 2; importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true,
            maxTextureSize = 1024, format = TextureImporterFormat.ASTC_6x6 });
        importer.SaveAndReimport();
    }

    public static void Material(Material material, GameObject model)
    {
        if (!IsRetopology(model)) return;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_ThemeStrength", 0f); material.SetFloat("_FinishMode", 0f);
        material.SetFloat("_AtlasEmission", .65f);
        material.enableInstancing = true; EditorUtility.SetDirty(material);
    }

    public static void Openings(GameObject root, string name)
    {
        if (!IsRetopology(root)) return;
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "CastleModules.json");
        if (json == null) throw new InvalidOperationException("Missing Blender retopology opening manifest.");
        Module data = Array.Find(JsonUtility.FromJson<Manifest>(json.text).modules, item => item.name == name);
        if (data == null) throw new InvalidOperationException("Missing retopology module: " + name);
        if (data.ports.Length == 0) return; // Native paths are measured along their actual treads.
        var module = root.AddComponent<CastleArchitectureModule>();
        module.kind = (CastleArchitectureModule.ModuleKind)data.kind;
        module.localBounds = new Bounds((data.minimum + data.maximum) * .5f, data.maximum - data.minimum);
        module.floorPoint = data.floorPoint; module.roofPoint = data.roofPoint;
        module.floorSize = data.floorSize; module.roofSize = data.roofSize;
        module.ports = data.ports; module.hasAuthoredFloors = true;
    }

    public static void Validate()
    {
        InfinityCastleArchitectureValidation.Install();
        using var report = new StreamWriter(Path.GetFullPath("../castle-retopology-tests.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        { report.WriteLine((passed ? "PASS " : "FAIL ") + message); if (!passed) throw new InvalidOperationException(message); }
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab");
        var castle = saved.GetComponent<DynamicInfinityCastle>();
        Texture atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas);
        Check(atlas != null && atlas.width <= 1024 && atlas.height <= 1024, "One shared mobile atlas, maximum 1024 pixels");
        foreach (GameObject prefab in castle.buildingPrefabs)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var module = instance.GetComponent<CastleArchitectureModule>();
                Check(module != null && module.hasAuthoredFloors, prefab.name + " has openings exported with its actual geometry");
                Renderer renderer = instance.GetComponentInChildren<Renderer>();
                Bounds actual = renderer.bounds;
                Check((actual.center - module.localBounds.center).magnitude < .02f && (actual.size - module.localBounds.size).magnitude < .02f,
                    prefab.name + " FBX metres and axes match Unity manifest: " + actual);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                    Check(r.sharedMaterial.GetTexture("_BaseMap") == atlas && r.sharedMaterial.GetFloat("_ThemeStrength") == 0f,
                        prefab.name + " uses its UV atlas without a procedural wood override");
                MeshCollider[] colliders = instance.GetComponentsInChildren<MeshCollider>();
                Check(colliders.Length > 0 && colliders.All(c => c.sharedMesh.isReadable), prefab.name + " retains Android-readable collision geometry");
                Physics.SyncTransforms();
                foreach (var door in module.ports)
                {
                    Vector3 inside = door.position - door.outward * .3f;
                    bool floor = colliders.Any(c => c.Raycast(new Ray(inside + Vector3.up * .4f, Vector3.down), out RaycastHit hit, .65f) && hit.normal.y > .85f);
                    Check(floor, prefab.name + " solid floor inside opening " + door.position);
                    bool blocked = colliders.Any(c => c.Raycast(new Ray(inside + Vector3.up * 1.2f, door.outward), out _, .7f));
                    Check(!blocked, prefab.name + " actual doorway is clear " + door.position);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        foreach (var item in castle.timberAssemblyModels)
        {
            int triangles = item.source.triangles.Length / 3;
            Check(triangles <= 6000, item.source.name + " mobile triangle budget=" + triangles);
            Check(item.source.uv.All(uv => uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1), item.source.name + " atlas UVs are in range");
            int[] indices = item.source.triangles; Vector3[] positions = item.source.vertices;
            Check(Enumerable.Range(0, indices.Length / 3).All(i => Vector3.Cross(positions[indices[i*3+1]]-positions[indices[i*3]],
                positions[indices[i*3+2]]-positions[indices[i*3]]).sqrMagnitude > 1e-14f), item.source.name + " no degenerate triangles");
        }
        CastleStairRoute route = castle.pathPrefabs.First(p => p.name == "PackSwitchbackStair").GetComponent<CastleStairRoute>();
        Check(route.hasAuthoredTreads && route.points.Length == 4 && Mathf.Abs(route.points[1].y-route.points[2].y) < .12f && route.points[3].y-route.points[0].y > 3f,
            "Retopologized stair_2 remains one intact U-shaped two-flight route");
        report.WriteLine("ALL RETOPOLOGY CHECKS PASSED");
    }
}
