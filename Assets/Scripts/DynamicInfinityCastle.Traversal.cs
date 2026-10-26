using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private readonly List<Bounds> gravityLeapClearance = new(80);

    private static Vector3 JumpLaunchVelocity(Vector3 start, Vector3 end, Vector3 first, Vector3 last, float duration)
    {
        float half = duration * .5f;
        return (end - start - first * (half * duration - half * half * .5f) - last * (.5f * half * half)) / duration;
    }

    private static Vector3 JumpArcPosition(Vector3 start, Vector3 velocity, Vector3 first, Vector3 last, float duration, float t)
    {
        float half = duration * .5f;
        return t <= half ? start + velocity * t + first * (.5f * t * t) :
            start + velocity * half + first * (.5f * half * half) + (velocity + first * half) * (t - half) + last * (.5f * (t - half) * (t - half));
    }

    private void DrawUltraJumpHint()
    {
        if (!IsPlayerReady || controller == null) return;
        string hint = Application.isMobilePlatform ? "Super Jump + move stick: steer freely in the air" :
            "Q / Shift+Space: Super Jump - WASD: steer in the air";
        GUI.Label(new Rect(14f, 12f, 520f, 26f), hint);
    }

    private void ReserveGravityLeapClearance(BuildingWing wing)
    {
        gravityLeapClearance.Clear();
        Vector3 start = wing.parts[6].module.WorldFloor + wing.Up * .95f;
        foreach (BuildingPart part in wing.parts)
        {
            if (!part.module.name.StartsWith("Gravity Leap Room")) continue;
            Vector3 up = part.module.transform.up;
            Vector3 end = part.module.transform.TransformPoint(part.module.roofPoint) + up * .95f;
            Vector3 first = -wing.Up * 22f, last = -up * 22f;
            for (int i = 1; i <= 16; i++)
            {
                Bounds corridor = default; bool empty = true;
                foreach (float duration in new[] { 1.6f, 3.2f })
                {
                    Vector3 velocity = JumpLaunchVelocity(start, end, first, last, duration);
                    for (int edge = i - 1; edge <= i; edge++)
                    {
                        Vector3 at = JumpArcPosition(start, velocity, first, last, duration, duration * edge / 16f);
                        Bounds sample = new(at, Vector3.one * 2.4f);
                        if (empty) { corridor = sample; empty = false; } else corridor.Encapsulate(sample);
                    }
                }
                gravityLeapClearance.Add(corridor);
            }
        }
    }
}
