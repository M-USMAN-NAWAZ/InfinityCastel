using UnityEngine;

// Shared by the training curriculum and the live castle; no scene or physics dependencies.
public sealed class CastleDirectorPolicy
{
    public const int ObservationCount = 40;
    public static readonly int[] Branches = { 6, 4, 6, 3 };
    public enum RouteKind { Attached, Stairs, Jump, Distant }
    public readonly float[] observations = new float[ObservationCount];
    private readonly int[] history = new int[8];
    private int cursor, count;
    public int Layout { get; private set; }
    public int Motion { get; private set; }
    public int Direction { get; private set; }
    public int Tempo { get; private set; } = 1;
    public int Decisions { get; private set; }
    public int PreviousLayout => count > 0 ? history[(cursor + 7) % 8] : -1;
    public int OlderLayout => count > 1 ? history[(cursor + 6) % 8] : -1;
    private static readonly Vector4[] mixtures =
    {
        new(.38f, .38f, .16f, .08f), new(.18f, .58f, .14f, .10f),
        new(.26f, .42f, .22f, .10f), new(.20f, .42f, .18f, .20f),
        new(.18f, .34f, .30f, .18f), new(.32f, .40f, .16f, .12f)
    };
    public Vector4 Mixture => mixtures[Layout];
    public float Skew => Layout == 2 ? 13f : Layout == 3 ? 18f : Layout == 4 ? 16f : 8f;
    public float StairChance => Layout == 1 ? .85f : Layout == 5 ? .4f : .65f;
    public float IntervalMultiplier => Tempo == 0 ? .85f : Tempo == 2 ? 1.3f : 1f;

    public void Reset(int seed)
    {
        cursor = count = Decisions = 0; Layout = Mathf.Abs(seed % 6); Motion = Direction = 0; Tempo = 1;
        System.Array.Clear(observations, 0, observations.Length);
    }
    public void Observe(Vector4 routes, float gravityRoutes, bool grounded, bool busy, float poolUse,
        Vector3 velocity, Vector3 up, float coverage, float blocked)
    {
        System.Array.Clear(observations, 0, observations.Length);
        for (int i = 0; i < count; i++) observations[history[i]] += 1f / count;
        if (PreviousLayout >= 0) observations[6 + PreviousLayout] = 1f;
        observations[12 + Motion] = 1f; observations[16 + Direction] = 1f;
        for (int i = 0; i < 4; i++) observations[22 + i] = routes[i];
        observations[26] = gravityRoutes;
        observations[27] = grounded ? 1f : 0f; observations[28] = busy ? 1f : 0f;
        observations[29] = poolUse; observations[30] = Mathf.Clamp01(velocity.magnitude / 6f);
        Vector3 local = Quaternion.Inverse(CastleGeometry.Orientation(up)) * velocity;
        observations[31] = Mathf.Clamp(local.x / 6f, -1f, 1f); observations[32] = Mathf.Clamp(local.z / 6f, -1f, 1f);
        observations[33] = up.x; observations[34] = up.y; observations[35] = up.z;
        observations[36] = coverage; observations[37] = blocked;
        int distinct = 0; for (int i = 0; i < 6; i++) if (observations[i] > 0f) distinct++;
        observations[38] = distinct / 6f; observations[39] = Mathf.Min(Decisions, 20) / 20f;
    }
    public RouteKind Route(float value)
    {
        Vector4 weights = Mixture;
        if (value < weights.x) return RouteKind.Attached;
        if (value < weights.x + weights.y) return RouteKind.Stairs;
        if (value < 1f - weights.w) return RouteKind.Jump;
        return RouteKind.Distant;
    }
    public bool AllowedLayout(int layout) => layout != PreviousLayout && layout != OlderLayout;
    public int FallbackLayout()
    {
        int best = -1; float score = float.NegativeInfinity;
        for (int i = 0; i < 6; i++)
        {
            if (!AllowedLayout(i)) continue;
            float candidate = LayoutScore(i);
            if (candidate > score) { score = candidate; best = i; }
        }
        return best < 0 ? (Layout + 1) % 6 : best;
    }
    public float LayoutScore(int layout)
    {
        Vector4 target = new(.28f, .44f, .18f, .10f);
        float score = 0f;
        for (int i = 0; i < 4; i++) score += (target[i] - observations[22 + i]) * mixtures[layout][i] * 4f;
        return score - observations[layout] * .8f;
    }
    public float Reward(int layout, int motion, int direction, int tempo)
    {
        float value = LayoutScore(layout) + (AllowedLayout(layout) ? .4f : -1f);
        value += motion != Motion ? .12f : -.12f;
        value += direction != Direction ? .08f : -.1f;
        bool falling = observations[27] < .5f;
        if (falling && motion != 0) value -= .8f;
        if (observations[37] > .35f && motion == 3) value -= .3f;
        int desiredTempo = falling || observations[37] > .35f ? 2 : observations[30] > .6f ? 0 : 1;
        value += tempo == desiredTempo ? .25f : -.15f;
        return value;
    }
    public void Accept(int layout, int motion, int direction, int tempo)
    {
        Layout = Mathf.Clamp(layout, 0, 5); Motion = Mathf.Clamp(motion, 0, 3);
        Direction = Mathf.Clamp(direction, 0, 5); Tempo = Mathf.Clamp(tempo, 0, 2);
        history[cursor] = Layout; cursor = (cursor + 1) % history.Length; count = Mathf.Min(count + 1, history.Length);
        Decisions++;
    }
}
