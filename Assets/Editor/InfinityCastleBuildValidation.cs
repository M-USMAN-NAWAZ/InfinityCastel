using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CastleHeadlessCheckShaders : IPreprocessShaders
{
    public int callbackOrder => int.MaxValue;
    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        // Only the opt-in headless physics test; never the Android game build.
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64 &&
            SessionState.GetBool("Castle.HeadlessPlayerCheck", false)) { data.Clear(); return; }
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
            shader.name != "Infinity Castle/Architecture" || snippet.passName != "Castle Forward") return;
        var instancing = new ShaderKeyword("INSTANCING_ON");
        foreach (var platform in new[] { ShaderCompilerPlatform.GLES3x, ShaderCompilerPlatform.Vulkan })
        {
            int total = data.Count(item => item.shaderCompilerPlatform == platform);
            int instanced = data.Count(item => item.shaderCompilerPlatform == platform && item.shaderKeywordSet.IsEnabled(instancing));
            UnityEngine.Debug.Log($"CASTLE_ANDROID_SHADER {platform}: variants={total}, instanced={instanced}");
            if (total == 0 || instanced == 0 || instanced == total)
                throw new BuildFailedException("Castle requires regular AND instanced variants on " + platform + ". Do not strip runtime instancing.");
        }
    }
}

public static class InfinityCastleBuildValidation
{
    private const string Scene = "Assets/StarterAssets/ThirdPersonController/Scenes/Playground.unity";
    private static void Build(BuildTarget target, string output, BuildOptions options)
    {
        InfinityCastleArchitectureValidation.Install();
        AssetDatabase.SaveAssets();
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, target = target,
            locationPathName = output, options = options });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Castle player build failed: " + report.summary.result);
    }
    public static void Player()
    {
        string folder = Path.GetFullPath("../PlayerCheck"); Directory.CreateDirectory(folder);
        string player = Path.Combine(folder, "InfinityCastle.exe"), report = Path.Combine(folder, "checks.txt");
        SessionState.SetBool("Castle.HeadlessPlayerCheck", true);
        try { Build(BuildTarget.StandaloneWindows64, player, BuildOptions.Development); }
        finally { SessionState.SetBool("Castle.HeadlessPlayerCheck", false); }
        var start = new ProcessStartInfo(player, "-batchmode -nographics -castle-check -castle-report \"" + report + "\" -logFile \"" + Path.Combine(folder, "Player.log") + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = folder };
        using Process process = Process.Start(start);
        if (!process.WaitForExit(300000)) { process.Kill(); throw new InvalidOperationException("Compiled player checks timed out: " + report); }
        UnityEngine.Debug.Log(File.Exists(report) ? File.ReadAllText(report) : "No compiled player report was created.");
        if (process.ExitCode != 0 || !File.Exists(report) || !File.ReadAllText(report).Contains("ALL COMPILED PLAYER CHECKS PASSED"))
            throw new InvalidOperationException("Compiled player regression failed: " + report);
    }
    public static void AndroidBuild()
    {
        string output = Path.GetFullPath("../../Builds/InfinityCastle-Android-dense-textured.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        EditorUserBuildSettings.buildAppBundle = false;
        SessionState.SetBool("Castle.HeadlessPlayerCheck", false);
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        // KeepAll=2. No scene renderer exists yet for runtime-only instanced castle districts.
        graphics.FindProperty("m_InstancingStripping").intValue = 2;
        graphics.ApplyModifiedPropertiesWithoutUndo();
        Build(BuildTarget.Android, output, BuildOptions.None);
        File.WriteAllText(Path.GetFullPath("../castle-android-build.txt"), "PASS Android ARM64 player build: " + output + "\nBytes=" + new FileInfo(output).Length);
    }
}
