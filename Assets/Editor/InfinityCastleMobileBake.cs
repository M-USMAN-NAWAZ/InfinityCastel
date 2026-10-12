using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class InfinityCastleMobileBake
{
    private const string CastlePath = "Assets/prefabs/InfinityCastleRuntime.prefab";
    private const string Folder = "Assets/InfinityCastle/MobileArchitecture";

    [MenuItem("Infinity Castle/Bake Mobile Architecture")]
    public static void Bake()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake outside Play mode.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        GameObject castleRoot = PrefabUtility.LoadPrefabContents(CastlePath);
        var report = new System.Text.StringBuilder();
        try
        {
            DynamicInfinityCastle castle = castleRoot.GetComponent<DynamicInfinityCastle>();
            castle.mobileDecorationPrefabs.Clear();
            foreach (GameObject source in castle.buildingPrefabs)
            {
                GameObject art = UnityEngine.Object.Instantiate(source);
                art.transform.position = Vector3.zero;
                GameObject proxy = new(source.name + " Mobile Architecture");
                try
                {
                    proxy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var renderers = new List<Renderer>();
                    int triangles = 0;
                    foreach (MeshFilter filter in art.GetComponentsInChildren<MeshFilter>())
                    {
                        var original = filter.GetComponent<MeshRenderer>();
                        Mesh mesh = filter.sharedMesh;
                        if (mesh == null || original == null || !original.enabled) continue;
                        var part = new GameObject(filter.name); part.transform.SetParent(proxy.transform, false);
                        part.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                        part.transform.localScale = new Vector3(filter.transform.lossyScale.x / proxy.transform.lossyScale.x,
                            filter.transform.lossyScale.y / proxy.transform.lossyScale.y, filter.transform.lossyScale.z / proxy.transform.lossyScale.z);
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        part.AddComponent<MeshCollider>().sharedMesh = mesh;
                        var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterials = original.sharedMaterials;
                        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderers.Add(renderer);
                        for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                    }
                    Bounds originalBounds = CastleGeometry.TransformBounds(CastleGeometry.RendererBoundsInRoot(art.transform), art.transform.localToWorldMatrix);
                    Bounds proxyBounds = CastleGeometry.RendererBoundsInRoot(proxy.transform);
                    if ((originalBounds.center - proxyBounds.center).magnitude > 0.01f || (originalBounds.size - proxyBounds.size).magnitude > 0.01f)
                        throw new InvalidOperationException("Mobile prefab changed bounds of " + source.name);
                    var lod = proxy.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.008f, renderers.ToArray()) }); lod.RecalculateBounds();
                    UnityEngine.Object.DestroyImmediate(art);
                    Physics.SyncTransforms();
                    var module = proxy.AddComponent<CastleArchitectureModule>();
                    module.kind = CastleArchitectureModule.ModuleKind.Exterior;
                    module.localBounds = proxyBounds;
                    module.floorSize = new Vector2(proxyBounds.size.x, proxyBounds.size.z);
                    var heights = new SortedSet<float>();
                    Collider[] geometry = proxy.GetComponentsInChildren<Collider>();
                    for (float x = proxyBounds.min.x + .25f; x < proxyBounds.max.x; x += .5f)
                        for (float z = proxyBounds.min.z + .25f; z < proxyBounds.max.z; z += .5f)
                        {
                            Ray ray = new(new Vector3(x, proxyBounds.max.y + .5f, z), Vector3.down);
                            foreach (Collider collider in geometry)
                                if (collider.Raycast(ray, out RaycastHit hit, proxyBounds.size.y + 1f) && hit.normal.y > .9f)
                                    heights.Add(Mathf.Round(hit.point.y * 20f) / 20f);
                        }
                    foreach (float height in System.Linq.Enumerable.Reverse(heights))
                    {
                        var ports = CastleCurrentModelImporter.FindPorts(proxy, proxyBounds, false, height);
                        if (ports.Count == 0) continue;
                        module.ports = ports.ToArray();
                        module.floorPoint = new Vector3(proxyBounds.center.x, height, proxyBounds.center.z);
                        module.roofPoint = module.floorPoint; module.roofSize = module.floorSize;
                        break;
                    }
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(proxy, Folder + "/" + source.name + ".prefab");
                    castle.mobileDecorationPrefabs.Add(prefab);
                    report.AppendLine(source.name + ": shared original mesh; renderers=" + renderers.Count +
                        "; triangles=" + triangles + "; mesh colliders=" + geometry.Length + "; native exterior ports=" + module.ports.Length +
                        "; surface=" + module.floorPoint + "; bounds=" + proxyBounds);
                }
                finally { if (art != null) UnityEngine.Object.DestroyImmediate(art); UnityEngine.Object.DestroyImmediate(proxy); }
            }
            PrefabUtility.SaveAsPrefabAsset(castleRoot, CastlePath); AssetDatabase.SaveAssets();
            File.WriteAllText(Path.GetFullPath("../castle-mobile-bake.txt"), report.ToString());
        }
        finally { PrefabUtility.UnloadPrefabContents(castleRoot); }
    }

    public static void BuildAndroid()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Build from the isolated validation project.");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.optimizedFramePacing = true;
        EditorUserBuildSettings.buildAppBundle = false;
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity" },
            target = BuildTarget.Android, locationPathName = Path.GetFullPath("../InfinityCastle-Android.apk"),
            options = BuildOptions.Development
        });
        File.WriteAllText(Path.GetFullPath("../castle-android-build.txt"), report.summary.result + "; errors=" +
            report.summary.totalErrors + "; size=" + report.summary.totalSize + "; duration=" + report.summary.totalTime);
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
