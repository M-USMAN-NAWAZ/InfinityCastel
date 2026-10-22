using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MLAgents;

public static class InfinityCastleLearningTools
{
    public static void BuildTraining()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated training project.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new("Castle Director Curriculum"); root.AddComponent<InfinityCastleTrainingBootstrap>();
        Directory.CreateDirectory("Assets/InfinityCastle/Training");
        PrefabUtility.SaveAsPrefabAsset(root, "Assets/InfinityCastle/Training/CastleDirectorTraining.prefab");
        string scenePath = "Assets/InfinityCastle/Training/CastleDirectorTraining.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        string executable = Path.GetFullPath("../CastleML/Build/CastleDirectorTraining.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        BuildReport report;
        SessionState.SetBool("Castle.HeadlessPlayerCheck", true);
        try { report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = executable, options = BuildOptions.Development }); }
        finally { SessionState.SetBool("Castle.HeadlessPlayerCheck", false); }
        File.WriteAllText(Path.GetFullPath("../castle-ml-training-build.txt"), report.summary.result + "; errors=" + report.summary.totalErrors);
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
    public static void ValidateModel()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        InfinityCastleMLAgent agent = InfinityCastleMLAgent.Create(null, false, 397);
        agent.LazyInitialize();
        if (!agent.HasTrainedModel) throw new InvalidOperationException("Trained ONNX asset is missing.");
        Academy.Instance.AutomaticSteppingEnabled = false;
        var random = new System.Random(2901);
        int[] layouts = new int[6], structures = new int[4], combinations = new int[24]; float learned = 0f, baseline = 0f;
        double inferenceMs = 0;
        for (int sample = 0; sample < 512; sample++)
        {
            Vector4 routes = new((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            routes /= routes.x + routes.y + routes.z + routes.w;
            Vector3 up = (sample % 6) switch { 0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.right,
                3 => Vector3.left, 4 => Vector3.forward, _ => Vector3.back };
            agent.Policy.Observe(routes, .5f, sample % 8 != 0, false, .75f,
                CastleGeometry.Orientation(up) * new Vector3(0, 0, sample % 3 == 0 ? 5f : 0f), up, .8f, sample % 5 == 0 ? .5f : .1f);
            int previous = agent.Policy.PreviousLayout, older = agent.Policy.OlderLayout, previousStructure = agent.Policy.Structure;
            var scores = new System.Collections.Generic.Dictionary<int, float>();
            for (int l = 0; l < 6; l++) if (agent.Policy.AllowedLayout(l))
                for (int m = 0; m < 4; m++) for (int d = 0; d < 6; d++) for (int t = 0; t < 3; t++)
                    for (int s = 0; s < 4; s++) if (agent.Policy.AllowedStructure(s))
                        scores[Key(l, m, d, t, s)] = agent.Policy.Reward(l, m, d, t, s);
            var candidates = new System.Collections.Generic.List<float>(scores.Values);
            baseline += candidates[random.Next(candidates.Count)];
            double began = EditorApplication.timeSinceStartup;
            agent.RequestPlan(); Academy.Instance.EnvironmentStep();
            inferenceMs += (EditorApplication.timeSinceStartup - began) * 1000;
            if (!agent.PlanReady || agent.Policy.Layout == previous || agent.Policy.Layout == older ||
                sample > 0 && agent.Policy.Structure == previousStructure)
                throw new InvalidOperationException("Model failed to produce a non-repeating decision.");
            int lChosen = agent.Policy.Layout, mChosen = agent.Policy.Motion, dChosen = agent.Policy.Direction, tChosen = agent.Policy.Tempo;
            int sChosen = agent.Policy.Structure;
            learned += scores[Key(lChosen, mChosen, dChosen, tChosen, sChosen)];
            structures[sChosen]++; combinations[lChosen * 4 + sChosen]++;
            layouts[lChosen]++; agent.ConsumePlan();
        }
        int distinct = 0; foreach (int usage in layouts) if (usage > 0) distinct++;
        StringBuilder output = new();
        output.AppendLine("Model decisions=" + agent.InferenceDecisions + "; distinct layouts=" + distinct + "; counts=" + string.Join(",", layouts));
        output.AppendLine("Structure counts=" + string.Join(",", structures) + "; layout/structure pairs=" + string.Join(",", combinations));
        output.AppendLine("Held-out learned reward=" + learned / 512 + "; masked-random reward=" + baseline / 512);
        output.AppendLine("Mean editor CPU inference ms=" + inferenceMs / 512 + " (not a phone benchmark)");
        if (distinct != 6 || learned <= baseline) throw new InvalidOperationException("Model failed held-out quality evaluation: " + output);
        if (System.Array.Exists(combinations, usage => usage == 0)) throw new InvalidOperationException("Model did not explore all 24 structure/layout pairs: " + output);
        output.AppendLine("ALL MODEL CHECKS PASSED");
        File.WriteAllText(Path.GetFullPath("../castle-ml-model-validation.txt"), output.ToString());
        UnityEngine.Object.DestroyImmediate(agent.gameObject); Academy.Instance.Dispose();
        static int Key(int l, int m, int d, int t, int s) => (((l * 4 + m) * 6 + d) * 3 + t) * 4 + s;
    }
}
