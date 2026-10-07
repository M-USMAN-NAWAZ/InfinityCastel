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
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
            target = BuildTarget.StandaloneWindows64, locationPathName = executable, options = BuildOptions.Development });
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
        int[] layouts = new int[6]; float learned = 0f, baseline = 0f;
        double inferenceMs = 0;
        for (int sample = 0; sample < 512; sample++)
        {
            Vector4 routes = new((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            routes /= routes.x + routes.y + routes.z + routes.w;
            agent.Policy.Observe(routes, .5f, sample % 8 != 0, false, .75f,
                new Vector3(0, 0, sample % 3 == 0 ? 5f : 0f), Vector3.up, .8f, sample % 5 == 0 ? .5f : .1f);
            int previous = agent.Policy.PreviousLayout, older = agent.Policy.OlderLayout;
            float[] scores = new float[6 * 4 * 6 * 3]; int candidates = 0;
            for (int l = 0; l < 6; l++) if (agent.Policy.AllowedLayout(l))
                for (int m = 0; m < 4; m++) for (int d = 0; d < 6; d++) for (int t = 0; t < 3; t++)
                    scores[candidates++] = agent.Policy.Reward(l, m, d, t);
            baseline += scores[random.Next(candidates)];
            double began = EditorApplication.timeSinceStartup;
            agent.RequestPlan(); Academy.Instance.EnvironmentStep();
            inferenceMs += (EditorApplication.timeSinceStartup - began) * 1000;
            if (!agent.PlanReady || agent.Policy.Layout == previous || agent.Policy.Layout == older)
                throw new InvalidOperationException("Model failed to produce a non-repeating decision.");
            // Recreate the pre-action state for evaluation, since Accept changes the history.
            int lChosen = agent.Policy.Layout, mChosen = agent.Policy.Motion, dChosen = agent.Policy.Direction, tChosen = agent.Policy.Tempo;
            float score = agent.Policy.LayoutScore(lChosen) + .4f;
            score += agent.Policy.observations[12 + mChosen] < .5f ? .12f : -.12f;
            score += agent.Policy.observations[16 + dChosen] < .5f ? .08f : -.1f;
            bool falling = agent.Policy.observations[27] < .5f;
            if (falling && mChosen != 0) score -= .8f;
            if (agent.Policy.observations[37] > .35f && mChosen == 3) score -= .3f;
            int tempo = falling || agent.Policy.observations[37] > .35f ? 2 : agent.Policy.observations[30] > .6f ? 0 : 1;
            learned += score + (tChosen == tempo ? .25f : -.15f);
            layouts[lChosen]++; agent.ConsumePlan();
        }
        int distinct = 0; foreach (int usage in layouts) if (usage > 0) distinct++;
        StringBuilder output = new();
        output.AppendLine("Model decisions=" + agent.InferenceDecisions + "; distinct layouts=" + distinct + "; counts=" + string.Join(",", layouts));
        output.AppendLine("Held-out learned reward=" + learned / 512 + "; masked-random reward=" + baseline / 512);
        output.AppendLine("Mean editor CPU inference ms=" + inferenceMs / 512 + " (not a phone benchmark)");
        if (distinct != 6 || learned <= baseline) throw new InvalidOperationException("Model failed held-out quality evaluation: " + output);
        output.AppendLine("ALL MODEL CHECKS PASSED");
        File.WriteAllText(Path.GetFullPath("../castle-ml-model-validation.txt"), output.ToString());
        UnityEngine.Object.DestroyImmediate(agent.gameObject); Academy.Instance.Dispose();
    }
}
