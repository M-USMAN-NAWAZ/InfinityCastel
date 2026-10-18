using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CastleTimberAssemblyBake
{
    private const string Folder = "Assets/InfinityCastle/CastleModules/RuntimeAssembly/";
    public static void Configure(DynamicInfinityCastle castle)
    {
        castle.timberAssemblyModels.Clear();
        castle.timberConstructionSeconds = 1.8f;
        foreach (string name in new[] { "CorrectedRoom", "HallWay3Way", "HallWayWithTurnFixed", "DoubleStaresBase", "SingleStareBase", "Path", "Stares" })
        foreach (string suffix in new[] { "", "_LOD2" })
        {
            Mesh source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/InfinityCastle/CastleModules/RuntimeMobile/" + name + suffix + ".fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            Add(source, name + suffix);
        }
        Add(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/InfinityCastle/CastleModules/RuntimeLod/CorrectedRoom_LOD3.fbx").GetComponentInChildren<MeshFilter>().sharedMesh, "CorrectedRoom_LOD3");
        foreach (string name in new[] { "PackFlatStair", "PackStairFlight" })
            Add(AssetDatabase.LoadAssetAtPath<Mesh>("Assets/InfinityCastle/CastleModules/RuntimeMobile/" + name + ".asset"), name);

        void Add(Mesh source, string name)
        {
            // Group complete triangles into rigid sections, retaining the exact mobile triangle/texture budget.
            Vector3[] vertices = source.vertices, normals = source.normals;
            Vector2[] uv = source.uv; int[] triangles = source.triangles;
            var groups = new Dictionary<int, Bounds>(); int[] ids = new int[triangles.Length / 3];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 centre = (vertices[triangles[i]] + vertices[triangles[i+1]] + vertices[triangles[i+2]]) / 3f;
                Vector3 unit = centre - source.bounds.min;
                unit = new Vector3(unit.x / source.bounds.size.x, unit.y / source.bounds.size.y, unit.z / source.bounds.size.z);
                // The supplied flight is long and narrow: group along its treads rather
                // than spending all ten bins across two unsegmented side rails.
                bool alongFlight = name == "PackStairFlight";
                int x = Mathf.Clamp((int)((alongFlight ? unit.z : unit.x) * 10f), 0, 9),
                    z = (alongFlight ? unit.x : unit.z) < .5f ? 0 : 1, y = unit.y < .16f ? 0 : unit.y < .78f ? 1 : 2;
                int id = x + z * 10 + y * 20; ids[i / 3] = id;
                if (!groups.TryGetValue(id, out Bounds box)) box = new Bounds(centre, Vector3.zero);
                for (int c = 0; c < 3; c++) box.Encapsulate(vertices[triangles[i+c]]);
                groups[id] = box;
            }
            var positions = new Vector3[triangles.Length]; var directions = new Vector3[triangles.Length];
            var albedo = new Vector2[triangles.Length]; var data = new List<Vector4>(triangles.Length);
            var sizes = new List<Vector2>(triangles.Length);
            for (int i = 0; i < triangles.Length; i++)
            {
                int sourceIndex = triangles[i], id = ids[i / 3];
                positions[i] = vertices[sourceIndex]; directions[i] = normals[sourceIndex]; albedo[i] = uv[sourceIndex];
                Vector3 pivot = groups[id].center;
                data.Add(new Vector4(pivot.x, pivot.y, pivot.z, 1f + (id / 20) * .19f + Mathf.Repeat(id * .618034f, 1f) * .18f));
                sizes.Add(new Vector2(source.bounds.size.magnitude, 0)); triangles[i] = i;
            }
            Mesh mesh = new() { name = name + "_Instanced", vertices = positions, normals = directions, uv = albedo, triangles = triangles, bounds = source.bounds };
            mesh.SetUVs(1, data); mesh.SetUVs(2, sizes);
            string asset = Folder + name + "_Instanced.asset";
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(asset);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, asset); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            castle.timberAssemblyModels.Add(new DynamicInfinityCastle.TimberAssemblyModel { source = source, fragments = saved });
        }
    }

    public static void Assembly()
    {
        InfinityCastleArchitectureValidation.Install();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        using var report = new StreamWriter(Path.GetFullPath("../castle-timber-assembly-tests.txt")) { AutoFlush = true };
        void Check(bool value, string message)
        { report.WriteLine((value ? "PASS " : "FAIL ") + message); if (!value) throw new InvalidOperationException(message); }
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string name, params object[] arguments) => typeof(DynamicInfinityCastle).GetMethod(name, flags).Invoke(castle, arguments);
        void Set(object instance, string name, object value) => instance.GetType().GetField(name, flags | BindingFlags.Public).SetValue(instance, value);
        foreach (var model in castle.timberAssemblyModels)
        {
            var data = new List<Vector4>(); model.fragments.GetUVs(1, data);
            int[] triangles = model.fragments.triangles;
            Check(data.Count == model.fragments.vertexCount && data.Select(v => v).Distinct().Count() >= 20,
                model.fragments.name + " has distinct rigid timber pieces");
            Check(triangles.Length == model.source.triangles.Length && model.source.triangles.Length / 3 <= 6008,
                "Animated LOD keeps the exact original mobile triangle budget and UVs");
            for (int i = 0; i < triangles.Length; i += 3)
                if ((data[triangles[i]] - data[triangles[i + 1]]).sqrMagnitude > .000001f ||
                    (data[triangles[i]] - data[triangles[i + 2]]).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Triangle stretches between rigid pieces: " + model.fragments.name);
            Check(true, model.fragments.name + " triangles move rigidly without rubber-sheet stretching");
        }
        foreach (GameObject path in castle.pathPrefabs.Where(path => path.name.StartsWith("Pack")))
        {
            Check(path.GetComponent<MeshCollider>().sharedMesh.isReadable, path.name + " keeps CPU geometry for player collider cooking");
            Material material = (Material)Call("PooledMaterial", path.GetComponent<Renderer>().sharedMaterial);
            Check(material.GetTexture("_WoodMap") != null && material.GetFloat("_ThemeStrength") > .5f,
                path.name + " preserves its timber texture in the pooled material");
        }
        castle.player = new GameObject("Assembly safety probe").transform;
        int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
        Set(castle, "initialized", false);
        object[] nearRoom = { false, Vector4.zero, new Bounds(Vector3.right * 12f, Vector3.one), true };
        Check((bool)Call("AdvanceTimberConstruction", nearRoom) && ((Vector4)nearRoom[1]).y > 0f && castle.StartupTimberConstructionCount == 1,
            "Construction starts during initial generation, including rooms inside the old 35m exclusion");
        Vector4 firstClock = (Vector4)nearRoom[1];
        Check(!(bool)Call("AdvanceTimberConstruction", nearRoom) && (Vector4)nearRoom[1] == firstClock,
            "Looking at a room again does not restart its animation");
        for (int repeat = 0; repeat < 200; repeat++)
        {
            object[] room = { false, Vector4.zero, new Bounds(Vector3.right * (20f + repeat % 170), Vector3.one), true };
            if (!(bool)Call("AdvanceTimberConstruction", room) || ((Vector4)room[1]).y <= 0f)
                throw new InvalidOperationException("Visible room skipped construction: " + repeat);
        }
        Check(castle.StartupTimberConstructionCount == 201 && castle.GetComponentsInChildren<Transform>(true).Length == transforms,
            "All 201 visible startup rooms animate without two-slot skips or new scene objects");
        nearRoom[2] = new Bounds(Vector3.zero, Vector3.one);
        Check((bool)Call("AdvanceTimberConstruction", nearRoom) && ((Vector4)nearRoom[1]).y < 0f,
            "Only the immediate 4m contact area completes early, preserving the player's floor");
        object[] hidden = { false, Vector4.zero, new Bounds(Vector3.right * 100f, Vector3.one), false };
        Check(!(bool)Call("AdvanceTimberConstruction", hidden) && !(bool)hidden[0], "Unseen rooms keep their animation for first visibility");
        castle.animateTimberConstruction = false;
        Check((bool)Call("AdvanceTimberConstruction", nearRoom) && (Vector4)nearRoom[1] == Vector4.zero,
            "Construction can be disabled without changing architecture or physics");
        castle.animateTimberConstruction = true;
        Set(castle, "initialized", false);
        RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .65f;
        Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero);
        var sun = new GameObject("Timber preview light").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        GameObject art = UnityEngine.Object.Instantiate(castle.roomPrefab);
        Renderer sourceRenderer = art.GetComponentInChildren<Renderer>();
        Mesh sourceMesh = sourceRenderer.GetComponent<MeshFilter>().sharedMesh;
        Mesh animated = castle.FindTimberAssemblyMesh(sourceMesh);
        Bounds world = sourceRenderer.bounds;
        var camera = new GameObject("Timber assembly preview").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.055f);
        camera.orthographic = true; camera.orthographicSize = world.size.magnitude;
        camera.nearClipPlane = .001f; camera.farClipPlane = 100f;
        camera.transform.position = world.center + new Vector3(1f,.65f,-1.2f).normalized * world.size.magnitude * 3f;
        camera.transform.LookAt(world.center);
        var block = new MaterialPropertyBlock();
        float progress = 0;
        void Draw(ScriptableRenderContext context, Camera current)
        {
            if (current != camera) return;
            block.SetVector("_CastleAssembly", new Vector4(progress,animated.bounds.size.magnitude * .6f,42f,1f));
            var parameters = new RenderParams(sourceRenderer.sharedMaterial) { camera = camera, matProps = block,
                shadowCastingMode = ShadowCastingMode.Off, worldBounds = new Bounds(world.center, Vector3.one * 20f) };
            Graphics.RenderMesh(parameters, animated, 0, sourceRenderer.localToWorldMatrix);
        }
        string folder = Path.GetFullPath("../TimberAssemblyPreview"); Directory.CreateDirectory(folder);
        Color32[] Capture(string filename)
        {
            RenderTexture target = RenderTexture.GetTemporary(960, 640, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            Texture2D pixels = new(960, 640, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0,0,960,640),0,0); pixels.Apply();
            Color32[] values = pixels.GetPixels32(); File.WriteAllBytes(Path.Combine(folder, filename), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels); camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); return values;
        }
        Color32[] reference = Capture("reference.png");
        report.WriteLine("Reference renderer: enabled=" + sourceRenderer.enabled + ", off=" + sourceRenderer.forceRenderingOff +
            ", active=" + sourceRenderer.gameObject.activeInHierarchy + ", clock=" + sourceRenderer.sharedMaterial.GetVector("_CastleBuild"));
        sourceRenderer.forceRenderingOff = true;
        RenderPipelineManager.beginCameraRendering += Draw;
        try
        {
            progress = 1f; Color32[] finished = Capture("finished.png");
            int finalVisible = 0, scatteredPixels = 0; double difference = 0;
            bool Visible(Color32 pixel) => Math.Abs(pixel.r - reference[0].r) + Math.Abs(pixel.g - reference[0].g) + Math.Abs(pixel.b - reference[0].b) > 20;
            for (int i = 0; i < finished.Length; i++)
                if (Visible(reference[i]) && Visible(finished[i])) { finalVisible++; difference += Math.Abs(reference[i].r-finished[i].r)+Math.Abs(reference[i].g-finished[i].g)+Math.Abs(reference[i].b-finished[i].b); }
            Check(finalVisible > 10000 && difference / finalVisible / 765 < .15, "Finished assembly matches the actual textured room, not a proxy facade");
            for (int frame = 0; frame < 84; frame++)
            {
                progress = Mathf.Clamp01(frame / 54f);
                Color32[] pixels = Capture("frame-" + frame.ToString("000") + ".png");
                if (frame == 10)
                    for (int i = 0; i < pixels.Length; i++) if (Visible(pixels[i]) && !Visible(finished[i])) scatteredPixels++;
            }
            Check(scatteredPixels > 100, "Timber moves outside the final silhouette during flight; this is not a dissolve: pixels=" + scatteredPixels);
            Collider[] colliders = art.GetComponentsInChildren<Collider>();
            Check(colliders.Length > 0 && colliders.All(c => c.enabled), "Visual assembly never disables the room collider");
            // Exercise the production 128-instance batching, including a second batch and different clocks.
            Type drawType = typeof(DynamicInfinityCastle).GetNestedType("DistrictDraw", flags);
            object draw = Activator.CreateInstance(drawType, true);
            Set(draw, "mesh", animated); Set(draw, "material", sourceRenderer.sharedMaterial);
            var matrices = (List<Matrix4x4>)drawType.GetField("instances", flags | BindingFlags.Public).GetValue(draw);
            var clocks = (List<Vector4>)drawType.GetField("construction", flags | BindingFlags.Public).GetValue(draw);
            for (int i = 0; i < 129; i++)
            { matrices.Add(sourceRenderer.localToWorldMatrix); clocks.Add(new Vector4(0, 0, 0, 2)); }
            clocks[128] = new Vector4(0, -1, 42, 1);
            var draws = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(drawType)); draws.Add(draw);
            void DrawBatch(ScriptableRenderContext context, Camera current)
            { if (current == camera) Call("DrawDistrictInstances", draws, camera); }
            RenderPipelineManager.beginCameraRendering -= Draw;
            RenderPipelineManager.beginCameraRendering += DrawBatch;
            try
            {
                Color32[] batch = Capture("instanced-second-batch.png");
                Check(batch.Count(Visible) > 10000, "The 129th room renders its own clock after the 128-instance batch boundary");
                clocks[128] = new Vector4(0, 0, 0, 2);
                Check(Capture("instanced-pending.png").Count(Visible) == 0, "Pending rooms do not pop in before their construction clock starts");
                clocks[0] = new Vector4(0, 1.8f, 42, 1);
                Shader.SetGlobalFloat("_CastleConstructionTime", .9f);
                Color32[] moving = Capture("instanced-moving.png");
                Check(moving.Where((pixel, i) => Visible(pixel) && !Visible(finished[i])).Count() > 100,
                    "The production instanced draw visibly moves rigid timber, not just a visibility fade");
                Shader.SetGlobalVector("_CastleStreamCenter", new Vector4(world.center.x, world.center.y, world.center.z - 176f, 1));
                Shader.SetGlobalVector("_CastleStreamDistances", new Vector4(160f, 192f, 0, 0));
                Check(Capture("instanced-moving-distant.png").SequenceEqual(moving), "Distance fading cannot mask visible timber construction at 176m");
                Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero);
                Set(draw, "mesh", castle.closedShojiMesh); Set(draw, "material", Call("PooledMaterial", castle.closedShojiMaterial));
                matrices.Clear(); clocks.Clear();
                matrices.Add(Matrix4x4.TRS(world.center, Quaternion.LookRotation(-camera.transform.forward), new Vector3(2.04f, 2.3f, .14f)));
                clocks.Add(new Vector4(0, -1, 42, 1));
                Check(Capture("closed-shoji.png").Count(Visible) > 1000,
                    "Blocked doorways have a visible textured shoji panel in the actual instanced draw");
            }
            finally { RenderPipelineManager.beginCameraRendering -= DrawBatch; Shader.SetGlobalVector("_CastleStreamCenter", Vector4.zero); }
            report.WriteLine("ALL TIMBER ASSEMBLY CHECKS PASSED");
        }
        finally { RenderPipelineManager.beginCameraRendering -= Draw; sourceRenderer.forceRenderingOff = false; }
    }
}
