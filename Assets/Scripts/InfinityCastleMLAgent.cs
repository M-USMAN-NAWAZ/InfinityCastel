using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

[DisallowMultipleComponent]
public sealed class InfinityCastleMLAgent : Agent
{
    public CastleDirectorPolicy Policy { get; } = new();
    public bool training;
    public int seed = 9217;
    public bool HasTrainedModel { get; private set; }
    public bool PlanReady { get; private set; }
    public bool Pending { get; private set; }
    public int InferenceDecisions { get; private set; }
    private System.Random random;
    private int episodeDecisions;

    public static InfinityCastleMLAgent Create(Transform parent, bool forTraining, int seed)
    {
        GameObject go = new(forTraining ? "Castle Director Training Agent" : "Learned Castle Director");
        go.SetActive(false); go.transform.SetParent(parent, false);
        BehaviorParameters behavior = go.AddComponent<BehaviorParameters>();
        behavior.BehaviorName = "InfinityCastleDirector";
        behavior.BrainParameters.VectorObservationSize = CastleDirectorPolicy.ObservationCount;
        behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(CastleDirectorPolicy.Branches);
        behavior.UseChildSensors = behavior.UseChildActuators = false;
        behavior.InferenceDevice = InferenceDevice.Burst;
        ModelAsset model = forTraining ? null : Resources.Load<ModelAsset>("CastleML/CastleDirector");
        behavior.Model = model;
        behavior.BehaviorType = forTraining ? BehaviorType.Default : model != null ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
        InfinityCastleMLAgent agent = go.AddComponent<InfinityCastleMLAgent>();
        agent.training = forTraining; agent.seed = seed; agent.HasTrainedModel = model != null;
        go.SetActive(true); return agent;
    }
    public override void Initialize() { random = new System.Random(seed); Policy.Reset(seed); }
    public override void OnEpisodeBegin()
    {
        if (!training) return;
        Policy.Reset(random.Next()); episodeDecisions = 0; SampleCurriculum();
    }
    private void FixedUpdate()
    {
        if (training) RequestDecision();
    }
    public void RequestPlan()
    {
        if (Pending) return;
        Pending = true; PlanReady = false; RequestDecision();
    }
    public void ConsumePlan() => PlanReady = false;
    public override void CollectObservations(VectorSensor sensor) => sensor.AddObservation(Policy.observations);
    public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
    {
        for (int i = 0; i < 6; i++) mask.SetActionEnabled(0, i, Policy.AllowedLayout(i));
    }
    public override void OnActionReceived(ActionBuffers actions)
    {
        var a = actions.DiscreteActions;
        if (training) AddReward(Policy.Reward(a[0], a[1], a[2], a[3]));
        Policy.Accept(a[0], a[1], a[2], a[3]);
        if (!training)
        {
            Pending = false; PlanReady = true;
            if (HasTrainedModel) InferenceDecisions++;
            return;
        }
        Academy.Instance.StatsRecorder.Add("Castle/Layout Diversity", Policy.observations[38]);
        if (++episodeDecisions >= 64) EndEpisode(); else SampleCurriculum();
    }
    private void SampleCurriculum()
    {
        // Fast high-level curriculum. Runtime geometry remains independently validated by the castle.
        Vector4 mix = Policy.Mixture;
        for (int i = 0; i < 4; i++) mix[i] = Mathf.Clamp01(mix[i] * .7f + (float)random.NextDouble() * .3f);
        mix /= mix.x + mix.y + mix.z + mix.w;
        Vector3 up = random.Next(6) switch { 0 => Vector3.up, 1 => Vector3.down, 2 => Vector3.right,
            3 => Vector3.left, 4 => Vector3.forward, _ => Vector3.back };
        Vector3 velocity = CastleGeometry.Orientation(up) * new Vector3((float)random.NextDouble() * 8f - 4f, 0f, (float)random.NextDouble() * 8f - 4f);
        Policy.Observe(mix, (float)random.NextDouble(), random.NextDouble() > .12, false,
            .55f + (float)random.NextDouble() * .4f, velocity, up, (float)random.NextDouble(), (float)random.NextDouble() * .65f);
    }
    public override void Heuristic(in ActionBuffers actions)
    {
        var a = actions.DiscreteActions;
        a[0] = Policy.FallbackLayout(); a[1] = Policy.observations[27] < .5f ? 0 : (Policy.Motion + 1) % 4;
        a[2] = (Policy.Direction + 1) % 6; a[3] = Policy.observations[27] < .5f || Policy.observations[37] > .35f ? 2 : Policy.observations[30] > .6f ? 0 : 1;
    }
}
