using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Native meshes are instantiated, rescaled and used for physics in the player.
// Editor-only access can conceal missing CPU mesh data in an Android build.
public sealed class CastleRuntimeModelImporter : AssetPostprocessor, IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Models/CastleModules/", StringComparison.Ordinal)) return;
        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.addCollider = true;
    }
    public void OnPreprocessBuild(BuildReport report) => Prepare();
    public static void Prepare()
    {
        foreach (string name in new[] { "CorrectedRoom", "HallWayWithTurnFixed", "HallWay3Way", "DoubleStaresBase", "SingleStareBase", "Path", "Stares" })
        {
            string path = "Assets/Models/CastleModules/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new BuildFailedException("Missing runtime castle model: " + path);
            if (importer.isReadable && importer.addCollider) continue;
            importer.isReadable = true; importer.addCollider = true; importer.SaveAndReimport();
        }
    }
}

public static class CastleCurrentModelImporter
{
    public const string Folder = "Assets/InfinityCastle/CastleModules/";
    public const string Theme = "Assets/InfinityCastle/CastleTheme/";
    public static readonly string[] Names = { "CorrectedRoom", "HallWayWithTurn1", "HallWay3Way", "DoubleStaresBase", "SingleStareBase" };
    private static CapsuleCollider probe;
    [Serializable] private sealed class Inspection { public float roof_height; public string kind; }

    [MenuItem("Infinity Castle/Prepare Current Castle Models")]
    public static void Prepare()
    {
        Directory.CreateDirectory(Theme); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        MakeTexture("Timber", false); MakeTexture("Shoji", true);
        StringBuilder report = new();
        foreach (string name in Names)
        {
            string path = Folder + name + "/";
            Inspection inspection = JsonUtility.FromJson<Inspection>(File.ReadAllText(path + "Inspection.json"));
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path + "Textures" }))
            {
                TextureImporter texture = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                texture.maxTextureSize = 1024; texture.mipmapEnabled = true; texture.textureCompression = TextureImporterCompression.Compressed;
                if (texture.assetPath.Contains("normal")) texture.textureType = TextureImporterType.NormalMap;
                texture.SaveAndReimport();
            }
            Material material = MakeMaterial(path + name + ".mat", AssetDatabase.LoadAssetAtPath<Texture2D>(path + "Textures/texture_pbr_20250901.png"), 0);
            if (material.GetTexture("_BaseMap") == null) throw new InvalidOperationException("Missing repaired texture: " + name);
            GameObject root = new(name);
            try
            {
                LOD[] levels = new LOD[3];
                for (int level = 0; level < 3; level++)
                {
                    GameObject art = ImportMesh(path + name + "_LOD" + level + ".fbx", root.transform, "LOD" + level);
                    Renderer[] renderers = art.GetComponentsInChildren<Renderer>();
                    foreach (Renderer renderer in renderers)
                    {
                        renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = level == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    }
                    levels[level] = new LOD(level == 0 ? .22f : level == 1 ? .08f : .009f, renderers);
                }
                GameObject collision = ImportMesh(path + name + "_Collision.fbx", root.transform, "Authored Collision");
                foreach (MeshFilter filter in collision.GetComponentsInChildren<MeshFilter>())
                {
                    UnityEngine.Object.DestroyImmediate(filter.GetComponent<MeshRenderer>());
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                bool stair = inspection.kind == "Stair";
                if (File.Exists(path + name + "_Floor.fbx"))
                {
                    GameObject floor = ImportMesh(path + name + "_Floor.fbx", root.transform, "Authored Building Floor");
                    foreach (MeshFilter filter in floor.GetComponentsInChildren<MeshFilter>())
                    {
                        UnityEngine.Object.DestroyImmediate(filter.GetComponent<MeshRenderer>());
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    }
                }
                Physics.SyncTransforms();
                Bounds bounds = root.GetComponentInChildren<Renderer>().bounds;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                CastleArchitectureModule module = root.AddComponent<CastleArchitectureModule>();
                module.kind = stair ? CastleArchitectureModule.ModuleKind.Stair : inspection.kind == "Room" ? CastleArchitectureModule.ModuleKind.Room : CastleArchitectureModule.ModuleKind.Corridor;
                module.localBounds = bounds; module.floorSize = new Vector2(bounds.size.x, bounds.size.z);
                module.roofPoint = Vector3.up * inspection.roof_height;
                module.roofSize = module.floorSize - Vector2.one * .35f;
                module.ports = FindPorts(root, bounds, stair).ToArray();
                // A stair with only an upper entrance is a side landing, not a through route.
                module.requiresPassageRepair = module.ports.Length < (stair ? 1 : 2);
                if (stair && module.ports.Length > 0)
                {
                    CastleArchitectureModule.Port lower = module.ports[0], upper = lower;
                    foreach (var port in module.ports)
                    {
                        if (port.position.y < lower.position.y) lower = port;
                        if (port.position.y >= upper.position.y) upper = port;
                    }
                    module.floorPoint = lower.position;
                    module.roofPoint = upper.position;
                }
                root.AddComponent<LODGroup>().SetLODs(levels); root.GetComponent<LODGroup>().RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, path + name + ".prefab");
                report.AppendLine(name + ": size=" + bounds.size + ", floor=" + module.floorPoint + ", roof=" + module.roofPoint + ", ports=" + module.ports.Length);
                foreach (var port in module.ports) report.AppendLine("  " + port.position + " outward=" + port.outward + " width=" + port.width);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
        probe = null;
        AssetDatabase.SaveAssets(); File.WriteAllText(Folder + "Preparation.txt", report.ToString()); Debug.Log(report.ToString());
    }

    private static GameObject ImportMesh(string path, Transform parent, string name)
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.importAnimation = false; importer.isReadable = false; importer.addCollider = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.SaveAndReimport();
        GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), parent);
        root.name = name; return root;
    }

    public static List<CastleArchitectureModule.Port> FindPorts(GameObject root, Bounds bounds, bool stair, float floorHeight = 0f)
    {
        List<CastleArchitectureModule.Port> ports = new();
        Collider[] colliders = root.GetComponentsInChildren<Collider>();
        foreach (Vector3 outward in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            int supported = 0, clear = 0;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            float edge = outward.x == 0 ? bounds.extents.z : bounds.extents.x;
            float halfWidth = outward.x == 0 ? bounds.extents.x : bounds.extents.z;
            const float spacing = .12f;
            List<Vector3> run = new();
            void Finish()
            {
                if (run.Count * spacing >= 1.1f)
                {
                    Vector3 middle = run[run.Count / 2];
                    ports.Add(new CastleArchitectureModule.Port { position = middle, outward = outward, width = Mathf.Min(3f, run.Count * spacing) });
                }
                run.Clear();
            }
            for (float across = -halfWidth + .15f; across < halfWidth; across += spacing)
            {
                Vector3 at = bounds.center + outward * (edge - .18f) + tangent * across;
                bool usable = false; float y = 0;
                for (int inset = 0; inset < 5 && !usable; inset++)
                {
                    Vector3 inside = at - outward * (inset * .14f);
                    Ray ray = new(new Vector3(inside.x, stair ? bounds.max.y + .5f : floorHeight + .45f, inside.z), Vector3.down);
                    float nearest = float.PositiveInfinity; RaycastHit surface = default;
                    foreach (Collider collider in colliders)
                        if (collider.Raycast(ray, out RaycastHit hit, stair ? bounds.size.y + 1f : .85f) && hit.distance < nearest)
                        { nearest = hit.distance; surface = hit; }
                    if (!float.IsFinite(nearest) || surface.normal.y < .65f) continue;
                    supported++;
                    y = surface.point.y;
                    if (stair && y > .25f) continue;
                    usable = Clear(root, new Vector3(at.x, y, at.z), -outward, stair && y < -.25f ? .35f : .8f);
                    if (usable && stair && y < -.25f)
                        usable = StairEntrySupported(root, colliders, new Vector3(at.x, y, at.z), -outward);
                    if (usable && stair && y > -.25f)
                    {
                        Vector3 support = at - outward * .75f;
                        Ray supportRay = new(new Vector3(support.x, y + 1f, support.z), Vector3.down);
                        bool broadLanding = false;
                        foreach (Collider collider in colliders)
                            if (collider.Raycast(supportRay, out RaycastHit landing, 1.25f) && landing.normal.y > .65f && Mathf.Abs(landing.point.y - y) < .8f)
                            { broadLanding = true; break; }
                        usable = broadLanding;
                    }
                    if (usable) clear++;
                }
                if (usable && (run.Count == 0 || Mathf.Abs(run[run.Count - 1].y - y) < .24f))
                { at.y = y; run.Add(at); }
                else { Finish(); if (usable) { at.y = y; run.Add(at); } }
            }
            Finish();
            Debug.Log(root.name + " ports " + outward + ": supported=" + supported + ", clear=" + clear);
        }
        return ports;
    }

    private static bool StairEntrySupported(GameObject root, Collider[] geometry, Vector3 port, Vector3 inward)
    {
        float height = port.y;
        int supported = 0;
        for (float depth = .45f; depth <= 1.06f; depth += .3f)
        {
            Vector3 point = port + inward * depth;
            Ray ray = new(new Vector3(point.x, height + 1f, point.z), Vector3.down);
            float nearest = 1.4f; RaycastHit floor = default;
            foreach (Collider collider in geometry)
                if (collider.Raycast(ray, out RaycastHit hit, nearest) && hit.normal.y > .65f)
                { nearest = hit.distance; floor = hit; }
            if (floor.collider == null) continue;
            if (floor.point.y < height - .35f || floor.point.y > height + .65f || !Clear(root, floor.point + Vector3.up * .25f, inward, 0f)) return false;
            height = floor.point.y;
            supported++;
        }
        return supported >= 2;
    }

    public static bool Clear(GameObject root, Vector3 port, Vector3 direction, float depth = .8f)
    {
        if (probe == null)
        {
            GameObject actor = new("Doorway clearance probe"); actor.transform.position = Vector3.one * 10000;
            probe = actor.AddComponent<CapsuleCollider>(); probe.radius = .29f; probe.height = 1.8f; probe.center = Vector3.up * 1.05f;
        }
        foreach (Collider collider in root.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || !collider.enabled) continue;
            for (float distance = -.2f; distance <= depth; distance += .1f)
                if (Physics.ComputePenetration(probe, port + direction * distance, Quaternion.identity,
                    collider, collider.transform.position, collider.transform.rotation, out _, out float penetration) && penetration > .035f) return false;
        }
        return true;
    }

    private static void MakeTexture(string name, bool paper)
    {
        const int size = 512;
        Texture2D texture = new(size, size, TextureFormat.RGB24, true); Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float grain = Mathf.PerlinNoise(x * .025f, y * .27f) * .12f + Mathf.Sin((y + Mathf.Sin(x * .022f) * 3f) * .7f) * .025f;
            if (paper)
            {
                float fiber = Mathf.PerlinNoise(x * .8f, y * .8f) * .035f;
                pixels[y * size + x] = new Color(.93f - fiber, .86f - fiber, .72f - fiber);
            }
            else
            {
                float seam = y % 64 < 2 ? .38f : 1f;
                pixels[y * size + x] = new Color(.30f + grain, .13f + grain * .58f, .105f + grain * .42f) * seam;
            }
        }
        texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(Theme + name + ".png", texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(Theme + name + ".png", ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(Theme + name + ".png");
        importer.wrapMode = TextureWrapMode.Repeat; importer.maxTextureSize = 512; importer.mipmapEnabled = true; importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
    }

    public static Material MakeMaterial(string path, Texture albedo, int mode)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Infinity Castle/Architecture")); AssetDatabase.CreateAsset(material, path); }
        material.shader = Shader.Find("Infinity Castle/Architecture"); material.enableInstancing = true;
        material.SetTexture("_BaseMap", albedo); material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_WoodMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Theme + "Timber.png"));
        material.SetTexture("_PaperMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Theme + "Shoji.png"));
        material.SetFloat("_ThemeStrength", 1); material.SetFloat("_FinishMode", mode);
        EditorUtility.SetDirty(material); return material;
    }

    public static void ApplyTheme(UnityEngine.Object generator)
    {
        SerializedObject castle = new(generator);
        castle.FindProperty("unifiedArchitectureTheme").boolValue = true;
        castle.FindProperty("themeWoodTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Theme + "Timber.png");
        castle.FindProperty("themePaperTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(Theme + "Shoji.png");
        SerializedProperty finishes = castle.FindProperty("architectureFinishes");
        var existing = new Dictionary<Material, (Texture albedo, Color tint, float glow, int mode)>();
        for (int i = 0; i < finishes.arraySize; i++)
        {
            var item = finishes.GetArrayElementAtIndex(i);
            if (item.FindPropertyRelative("source").objectReferenceValue is Material source)
                existing[source] = (item.FindPropertyRelative("albedo").objectReferenceValue as Texture,
                    item.FindPropertyRelative("tint").colorValue, item.FindPropertyRelative("glow").floatValue,
                    item.FindPropertyRelative("finishMode").intValue);
        }
        finishes.ClearArray();
        HashSet<Material> originals = new();
        foreach (string field in new[] { "buildingPrefabs", "pathPrefabs" })
        {
            SerializedProperty prefabs = castle.FindProperty(field);
            for (int i = 0; i < prefabs.arraySize; i++)
                if (prefabs.GetArrayElementAtIndex(i).objectReferenceValue is GameObject art)
                    foreach (Renderer renderer in art.GetComponentsInChildren<Renderer>(true))
                        foreach (Material source in renderer.sharedMaterials) if (source != null) originals.Add(source);
        }
        foreach (Material source in originals)
        {
            string name = source.name.ToLowerInvariant();
            existing.TryGetValue(source, out var old);
            int mode = old.mode > 0 ? old.mode : old.glow > .1f || name.Contains("lamp") || name.Contains("light") ? 5 :
                old.tint.grayscale > .6f ? 2 :
                name.Contains("shoji") || name.Contains("window") || name.Contains("paper") || name.Contains("white") || name.Contains("wall") ? 2 :
                name.Contains("roof") || name.Contains("dark") ? 3 : name.Contains("gold") || name.Contains("metal") ? 4 : 1;
            finishes.InsertArrayElementAtIndex(finishes.arraySize);
            SerializedProperty finish = finishes.GetArrayElementAtIndex(finishes.arraySize - 1);
            finish.FindPropertyRelative("source").objectReferenceValue = source;
            finish.FindPropertyRelative("albedo").objectReferenceValue = old.albedo;
            finish.FindPropertyRelative("tint").colorValue = Color.white;
            finish.FindPropertyRelative("glow").floatValue = 0;
            finish.FindPropertyRelative("finishMode").intValue = mode;
        }
        castle.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Validate()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use isolated validation.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); Prepare();
        foreach (string name in Names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + "/" + name + ".prefab");
            CastleArchitectureModule module = prefab.GetComponent<CastleArchitectureModule>();
            if (module.requiresPassageRepair) throw new InvalidOperationException("Missing usable entrance ports: " + name);
            if (prefab.GetComponent<LODGroup>().GetLODs().Length != 3) throw new InvalidOperationException("Missing mobile LOD: " + name);
        }
        File.WriteAllText(Path.GetFullPath("../supplied-model-validation.txt"), "Current module checks passed.\n" + File.ReadAllText(Folder + "Preparation.txt"));
    }
}
