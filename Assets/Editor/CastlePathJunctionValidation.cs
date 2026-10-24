using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CastlePathJunctionValidation
{
    public static void Junctions()
    {
        InfinityCastleArchitectureValidation.Install();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        using var report = new StreamWriter(Path.GetFullPath("../castle-path-junction-tests.txt")) { AutoFlush = true };
        void Check(bool passed, string message)
        { report.WriteLine((passed ? "PASS " : "FAIL ") + message); if (!passed) throw new InvalidOperationException(message); }
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        castle.pathsParent = castle.transform;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string method, params object[] args) => typeof(DynamicInfinityCastle).GetMethod(method, flags).Invoke(castle, args);
        object Get(object target, string field) => target.GetType().GetField(field, flags | BindingFlags.Public).GetValue(target);
        void Set(object target, string field, object value) => target.GetType().GetField(field, flags | BindingFlags.Public).SetValue(target, value);
        Call("ReadStairs");
        object first = Call("CreateLink", 0), second = Call("CreateLink", 1);
        object[] links = { first, second };
        int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
        var collision = new Collider[32];
        void Passage(Vector3 from, Vector3 to, Vector3 up, string label)
        {
            for (int sample = 0; sample <= 32; sample++)
            {
                Vector3 foot = Vector3.Lerp(from, to, sample / 32f);
                int count = Physics.OverlapCapsuleNonAlloc(foot + up * .55f, foot + up * 1.45f, .28f,
                    collision, ~0, QueryTriggerInteraction.Ignore);
                if (count > 0) Check(false, label + " railing blocks the walking capsule at " + foot + ": " + collision[0].name);
            }
            Vector3 centre = (from + to) * .5f;
            Check(Physics.Raycast(centre + up * .2f, -up, out RaycastHit hit, .5f, ~0, QueryTriggerInteraction.Ignore) &&
                Vector3.Dot(hit.normal, up) > .9f, label + " retains a solid floor and a clear walking lane");
        }
        Type crossingType = typeof(DynamicInfinityCastle).GetNestedType("DistrictCrossing", flags);
        Type roomType = typeof(DynamicInfinityCastle).GetNestedType("DistrictRoom", flags);
        foreach (bool dense in new[] { false, true })
        foreach (Vector3 up in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        foreach (float length in new[] { 1.3f, 4f, 12f })
        foreach (int side in new[] { -1, 1 })
        {
            Quaternion basis = Quaternion.FromToRotation(Vector3.up, up);
            Vector3 from = basis * Vector3.left * length, corner = Vector3.zero, to = basis * Vector3.forward * (length * side);
            Vector3[] points = { from, corner, to };
            for (int i = 0; i < 2; i++)
            {
                if (!dense) Call("ConfigureNetworkBridge", links[i], points[i], points[i+1], up);
                else
                {
                    object room = Activator.CreateInstance(roomType, true), crossing = Activator.CreateInstance(crossingType, true);
                    Set(room, "rotation", basis); Set(crossing, "a", room);
                    Set(crossing, "start", points[i]); Set(crossing, "end", points[i+1]); Set(crossing, "link", links[i]);
                    Call("FitDistrictCrossing", crossing, up); Call("ConfigureDistrictStairCollider", crossing);
                }
            }
            Physics.SyncTransforms();
            string label = (dense ? "Distributed" : "Native") + $" corner up={up}, run={length}, turn={side}";
            Passage(from, corner, up, label); Passage(corner, to, up, label);
        }
        Check(transforms == castle.GetComponentsInChildren<Transform>(true).Length, "All junction cases reuse the same two pooled connectors");

        // Look down through the player's body-height band, excluding the floor.
        // This catches rendered fences even when their colliders have been removed.
        Call("ConfigureNetworkBridge", first, Vector3.left * 4f, Vector3.zero, Vector3.up);
        Call("ConfigureNetworkBridge", second, Vector3.zero, Vector3.forward * 4f, Vector3.up);
        var renderers = links.SelectMany(link => (Renderer[])Get(link, "renderers")).ToArray();
        foreach (Renderer renderer in renderers) renderer.gameObject.layer = 27;
        Material[] materials = renderers.Select(r => r.sharedMaterial).Distinct().ToArray();
        foreach (Renderer renderer in renderers)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var data = new List<Vector4>(); mesh.GetUVs(3, data);
            report.WriteLine($"RENDER {renderer.name}: mesh={mesh.name}, scale={renderer.transform.lossyScale}, rail vertices={data.Count}, fences={data.Count(v => v.z > .5f)}, " +
                (data.Count > 0 ? $"distances={data.Min(v => v.x)}..{data.Max(v => v.x)}, axes={string.Join(",", data.Select(v => v.w).Distinct())}" : ""));
        }
        Check(materials.All(m => m.HasProperty("_RailEndClearance") && m.GetFloat("_RailEndClearance") > 1f),
            "The pooled path materials retain the visible rail-opening setting");
        ShaderUtil.allowAsyncCompilation = false;
        RenderSettings.fog = false; Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero);
        var camera = new GameObject("Junction clearance camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(Vector3.up * 1.4f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        camera.orthographic = true; camera.orthographicSize = 1.3f; camera.nearClipPlane = .05f; camera.farClipPlane = 1.1f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 27;
        string folder = Path.GetFullPath("../JunctionPreview"); Directory.CreateDirectory(folder);
        int Capture(string filename)
        {
            // Multiple Camera.Render calls share an editor frame. Supply the
            // changed uniform explicitly rather than a cached SRP material buffer.
            foreach (Renderer renderer in renderers)
            {
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                properties.SetFloat("_RailEndClearance", renderer.sharedMaterial.GetFloat("_RailEndClearance"));
                renderer.SetPropertyBlock(properties);
            }
            RenderTexture target = RenderTexture.GetTemporary(512, 512, 24);
            RenderTexture previous = RenderTexture.active; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(512,512,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,512,512),0,0); pixels.Apply();
            int visible = pixels.GetPixels32().Count(p => p.r > 15 || p.g > 15 || p.b > 15);
            File.WriteAllBytes(Path.Combine(folder, filename), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels); camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            return visible;
        }
        foreach (int lod in new[] { 0, 1 })
        {
            foreach (object link in links) foreach (var group in ((GameObject)Get(link, "root")).GetComponentsInChildren<LODGroup>(true)) group.ForceLOD(lod);
            foreach (Material material in materials) material.SetFloat("_RailEndClearance", 0f);
            Capture("warmup.png"); int blocked = Capture($"lod{lod}-before.png");
            foreach (Material material in materials) material.SetFloat("_RailEndClearance", 1.6f);
            int clear = Capture($"lod{lod}-after.png");
            Check(blocked > 500 && clear == 0, $"LOD{lod} visibly clears the junction, not just its collider: obstructing pixels {blocked} -> {clear}");
        }
        report.WriteLine("ALL PATH JUNCTION CHECKS PASSED");
    }
}
