using System;
using System.IO;
using System.Linq;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CastleBackgroundValidation
{
    public static void Run()
    {
        if (!File.Exists(CastleSkyboxBake.Image)) CastleSkyboxBake.Bake();
        else CastleSkyboxBake.Apply();
        EditorSceneManager.OpenScene("Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity");
        using var report = new StreamWriter(Path.GetFullPath("../castle-background-tests.txt")) { AutoFlush = true };
        void Check(bool ok, string message)
        { report.WriteLine((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new InvalidOperationException(message); }
        ShaderUtil.allowAsyncCompilation = false;
        Material sky = RenderSettings.skybox;
        report.WriteLine("Assigned sky=" + (sky == null ? "null" : sky.name + ", shader=" + sky.shader.name + ", path=" + AssetDatabase.GetAssetPath(sky)));
        if (sky != null) foreach (var message in ShaderUtil.GetShaderMessages(sky.shader)) report.WriteLine(message.severity + ": " + message.message);
        Check(sky != null && sky.shader.name == "Skybox/Panoramic" && AssetDatabase.GetAssetPath(sky.GetTexture("_MainTex")) == CastleSkyboxBake.Image,
            "Playground uses the skybox rendered from the actual castle models");
        var texture = (TextureImporter)AssetImporter.GetAtPath(CastleSkyboxBake.Image);
        Check(texture.GetPlatformTextureSettings("Android").format == TextureImporterFormat.ASTC_6x6 && texture.mipmapEnabled,
            "The background uses Android ASTC compression and mipmaps, with no runtime capture");
        bool fog = RenderSettings.fog; Color fogColor = RenderSettings.fogColor;
        float fogDensity = RenderSettings.fogDensity;
        var ambientMode = RenderSettings.ambientMode;
        Color ambientSky = RenderSettings.ambientSkyColor, ambientGround = RenderSettings.ambientGroundColor;
        int objects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        int lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length;
        RenderSettings.skybox = sky;
        Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length == objects &&
            UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length == lights,
            "The background creates no scene objects, walls, colliders, or lights");
        Check(fog == RenderSettings.fog && fogColor == RenderSettings.fogColor && fogDensity == RenderSettings.fogDensity &&
            ambientMode == RenderSettings.ambientMode && ambientSky == RenderSettings.ambientSkyColor && ambientGround == RenderSettings.ambientGroundColor,
            "Existing fog and ambient lighting settings remain unchanged");
        var camera = new GameObject("Background validation camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox; camera.cullingMask = 0;
        camera.fieldOfView = 90; camera.nearClipPlane = .1f; camera.farClipPlane = 500;
        string folder = Path.GetFullPath("../CastleBackgroundPreview"); Directory.CreateDirectory(folder);
        Color32[] Capture(string name)
        {
            RenderTexture target = RenderTexture.GetTemporary(960, 540, 24);
            RenderTexture previous = RenderTexture.active; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(960,540,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,960,540),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Color32[] pixels = image.GetPixels32();
            UnityEngine.Object.DestroyImmediate(image); camera.targetTexture = null;
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            return pixels;
        }
        foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down })
        {
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(direction,
                Mathf.Abs(direction.y) > .9f ? Vector3.forward : Vector3.up));
            Capture("warmup"); Color32[] before = Capture("background-" + direction);
            foreach (var message in ShaderUtil.GetShaderMessages(sky.shader)) report.WriteLine(message.severity + ": " + message.message);
            Check(!ShaderUtil.ShaderHasError(sky.shader), "The background shader compiles for the active graphics API");
            Check(before.Count(p => p.r > 5 && p.r >= p.b) > before.Length * .9f && !before.Any(p => p.r > 220 && p.b > 220 && p.g < 50),
                "Warm non-black background renders without shader errors looking " + direction);
            Check(before.Count(p => p.r > 70) > before.Length * .15f,
                "Recognisable lit castle detail fills the background looking " + direction);
            camera.transform.position = new Vector3(10000,-10000,10000);
            Color32[] after = Capture("far-" + direction);
            int[] changes = before.Zip(after, (a,b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b))).ToArray();
            // Native skybox view matrices can shift textured edges subpixel at
            // large world coordinates. Reject parallax, not tiny raster rounding.
            Check(changes.Average() < .3 && changes.Count(delta => delta > 8) < changes.Length * .01,
                "Moving 17 km never approaches an edge or wall looking " + direction +
                $"; mean pixel delta={changes.Average():F4}, max={changes.Max()}");
        }
        var actor = new GameObject("Castle background preview player");
        actor.AddComponent<CharacterController>(); actor.AddComponent<StarterAssetsInputs>();
        actor.AddComponent<InfinityGravityBody>(); actor.AddComponent<ThirdPersonController>();
        var castle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/InfinityCastleRuntime.prefab")).GetComponent<DynamicInfinityCastle>();
        castle.player = actor.transform; castle.castleLighting = false; castle.useLearnedDirector = false;
        castle.periodicRebuilding = false; castle.simultaneousShifts = 0; castle.InitializeCastle();
        Check(castle.IsPlayerReady && castle.IsInitialized, "The real castle still prepares and unlocks the player");
        camera.cullingMask = ~0;
        camera.transform.SetPositionAndRotation(actor.transform.position + Vector3.up * 2f, Quaternion.Euler(-28, 35, 0));
        Capture("warmup-castle"); Capture("castle-new-background");
        Material old = new(Shader.Find("Skybox/Panoramic"));
        old.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Images/Infinite Lantern Pagoda City.png"));
        old.SetFloat("_Mapping",1); old.SetFloat("_Exposure",1);
        RenderSettings.skybox = old; Capture("warmup-before"); Capture("castle-old-background");
        RenderSettings.skybox = sky; UnityEngine.Object.DestroyImmediate(old);
        report.WriteLine("ALL BACKGROUND CHECKS PASSED");
    }
}
