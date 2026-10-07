using UnityEngine;

[DisallowMultipleComponent]
public sealed class InfinityCastleDirector : MonoBehaviour
{
    public enum CastlePhase { Shear, Lift, Reweave, Open }
    public CastlePhase Phase { get; private set; }
    public int CommandsIssued { get; private set; }
    private float pauseTime;
    private float rebuildTime = -1f;
    private System.Random random;
    private readonly Vector3[] axes = { Vector3.right, Vector3.back, Vector3.left, Vector3.forward };

    public void Initialize(int seed)
    {
        random = new System.Random(seed);
        rebuildTime = -1f; CommandsIssued = 0;
        Phase = CastlePhase.Shear;
        pauseTime = 4f + (float)random.NextDouble() * 3f;
    }
    public void Tick(float dt, DynamicInfinityCastle castle, int availableShifts, Vector3 playerVelocity)
    {
        // Count the quiet interval only after the previous group has completely settled.
        if (castle.IsRebuilding || availableShifts < castle.simultaneousShifts) return;
        Vector3 up = castle.PlayerUp;
        Quaternion basis = CastleGeometry.Orientation(up);
        if (castle.periodicRebuilding && castle.randomNearbyCastle)
        {
            if (rebuildTime < 0f) rebuildTime = RebuildDelay(castle);
            // Zero stays due while asynchronous inference completes; negative means uninitialized.
            rebuildTime = Mathf.Max(0f, rebuildTime - dt);
            if (rebuildTime <= 0f)
            {
                if (!castle.RequestLearnedPlan()) return;
                if (castle.DirectRebuild(castle.useLearnedDirector ? castle.LearnedDirection : basis * axes[random.Next(axes.Length)]))
                {
                    Phase = CastlePhase.Reweave;
                    rebuildTime = RebuildDelay(castle) * castle.LearnedIntervalMultiplier; pauseTime = Mathf.Max(pauseTime, 10f);
                    return;
                }
                rebuildTime = 3f;
            }
        }
        pauseTime -= dt;
        if (pauseTime > 0f) return;
        if (!castle.RequestLearnedPlan()) return;
        Phase = castle.useLearnedDirector ? (CastlePhase)castle.LearnedMotion : (CastlePhase)random.Next(4);
        Vector3 direction = Phase switch
        {
            CastlePhase.Lift => up,
            CastlePhase.Reweave => -up,
            CastlePhase.Open => Vector3.ProjectOnPlane(playerVelocity, up).sqrMagnitude > 1f ?
                Vector3.Cross(up, playerVelocity).normalized : basis * axes[random.Next(axes.Length)],
            _ => castle.useLearnedDirector ? castle.LearnedDirection : basis * axes[random.Next(axes.Length)]
        };
        int count = Mathf.Min(availableShifts, random.Next(1, 3));
        for (int i = 0; i < count; i++) if (castle.DirectShift(direction)) CommandsIssued++;
        pauseTime = (Mathf.Max(7f, castle.shiftInterval) + (float)random.NextDouble() * 6f) * castle.LearnedIntervalMultiplier;
    }
    private float RebuildDelay(DynamicInfinityCastle castle)
    {
        float minimum = Mathf.Max(10f, Mathf.Min(castle.rebuildInterval.x, castle.rebuildInterval.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(castle.rebuildInterval.x, castle.rebuildInterval.y));
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }
}
