using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// This project's only deployed network explicitly uses the Burst CPU backend.
public sealed class CastleCpuInferenceBuild : IPreprocessShaders, IPreprocessComputeShaders
{
    public int callbackOrder => 0;
    private static bool IsInferenceAsset(Object asset) =>
        AssetDatabase.GetAssetPath(asset).StartsWith("Packages/com.unity.ai.inference/", System.StringComparison.Ordinal);
    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (IsInferenceAsset(shader)) data.Clear();
    }
    public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
    {
        if (IsInferenceAsset(shader)) data.Clear();
    }
}
