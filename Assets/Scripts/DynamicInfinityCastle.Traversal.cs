using System.Collections.Generic;
using UnityEngine;

public partial class DynamicInfinityCastle
{
    private struct JumpLanding
    {
        public Vector3 point, up;
        public float score;
        public BuildingWing wing;
        public DistrictRoom room;
    }

    private readonly List<JumpLanding> jumpLandings = new(2048);
    private readonly RaycastHit[] jumpHits = new RaycastHit[48];
    private readonly List<Bounds> gravityLeapClearance = new(80);
    private BuildingWing jumpLandingWing;
    private DistrictRoom jumpLandingRoom;
    private string jumpMessage;
    private float jumpMessageUntil;
    public Vector3 UltraJumpDestination { get; private set; }
    public Vector3 UltraJumpLandingUp { get; private set; }
    public string UltraJumpBlockReason { get; private set; }
    private bool IsUltraJumpTarget(BuildingWing wing) => controller != null && controller.IsUltraJumping && wing == jumpLandingWing;
    private bool IsUltraJumpTarget(DistrictRoom room) => controller != null && controller.IsUltraJumping && room == jumpLandingRoom;

    public bool TryUltraJump(Vector3 aim)
    {
        if (!IsPlayerReady || controller == null || controller.IsUltraJumping || !controller.Grounded) return false;
        jumpLandings.Clear();
        UltraJumpBlockReason = "No roof in aim";
        jumpLandingWing = null; jumpLandingRoom = null;
        aim.Normalize();
        Vector3 centre = player.position + PlayerUp * .93f;
        void Consider(Vector3 point, Vector3 up, BuildingWing wing, DistrictRoom room)
        {
            Vector3 delta = point + up * .93f - centre;
            float distance = delta.magnitude;
            // A sideways departure can be level with the destination roof; its arc supplies the headroom.
            if (distance < 7f || distance > controller.EffectiveUltraJumpRange || Vector3.Dot(centre - point, up) < -.25f) return;
            float facing = Vector3.Dot(delta / distance, aim);
            if (facing < .97f) return;
            jumpLandings.Add(new JumpLanding { point = point, up = up, wing = wing, room = room,
                score = facing * 10000f - distance * .01f });
        }
        foreach (BuildingWing wing in buildingWings)
        {
            if (!wing.active || wing.piece.IsMoving) continue;
            foreach (BuildingPart part in wing.parts)
            {
                if (part.module.kind == CastleArchitectureModule.ModuleKind.Roof)
                    Consider(part.module.WorldFloor, part.module.transform.up, wing, null);
                else if (part.roofZone != null)
                    Consider(part.module.transform.TransformPoint(part.module.roofPoint), part.module.transform.up, wing, null);
            }
        }
        foreach (DistrictBlock block in denseBlocks.Values) foreach (DistrictRoom room in block.rooms)
            if (room.active)
                Consider(room.position + room.rotation * Vector3.Scale(roomPrefab.GetComponent<CastleArchitectureModule>().roofPoint, room.scale),
                    room.rotation * Vector3.up, null, room);
        jumpLandings.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (JumpLanding candidate in jumpLandings)
        {
            if (candidate.wing != null) SetBuildingPhysics(candidate.wing, true);
            if (candidate.room != null && !AcquireDistrictBody(candidate.room)) continue;
            Physics.SyncTransforms();
            int count = Physics.RaycastNonAlloc(candidate.point + candidate.up * 2f, -candidate.up, jumpHits, 3f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit landing = default; float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (jumpHits[i].distance < nearest && Vector3.Dot(jumpHits[i].normal, candidate.up) > .9f && IsCastleSurface(jumpHits[i].collider))
                { landing = jumpHits[i]; nearest = jumpHits[i].distance; }
            if (landing.collider == null) continue;
            Vector3 destination = landing.point + candidate.up * .95f;
            float duration = Mathf.Ceil(Mathf.Clamp(Vector3.Distance(centre, destination) / controller.EffectiveUltraJumpSpeed, 1.6f, 3.2f) * 2.5f) * .4f;
            Vector3 firstAcceleration = -PlayerUp * gravity.Strength;
            Vector3 lastAcceleration = -candidate.up * gravity.Strength;
            Vector3 velocity = default; bool clear = false;
            for (; duration <= 3.201f; duration += .4f)
            {
                velocity = JumpLaunchVelocity(centre, destination, firstAcceleration, lastAcceleration, duration);
                if (JumpArcClear(centre, velocity, firstAcceleration, lastAcceleration, duration)) { clear = true; break; }
            }
            if (!clear) continue;
            jumpLandingWing = candidate.wing; jumpLandingRoom = candidate.room;
            UltraJumpDestination = landing.point; UltraJumpLandingUp = candidate.up;
            UltraJumpBlockReason = "";
            controller.LaunchUltraJump(velocity, candidate.up, duration);
            jumpMessage = "Ultra jump - gravity transfer"; jumpMessageUntil = Time.unscaledTime + duration;
            return true;
        }
        jumpMessage = "No clear landing in aim - forward ultra jump"; jumpMessageUntil = Time.unscaledTime + 2f;
        return false;
    }

    private bool JumpArcClear(Vector3 start, Vector3 velocity, Vector3 first, Vector3 last, float duration)
    {
        Vector3 previous = start;
        // Bounded, on-demand checks only; the motor still sweeps the actual capsule during flight.
        for (int step = 1; step <= 32; step++)
        {
            float t = duration * step / 32f;
            Vector3 at = JumpArcPosition(start, velocity, first, last, duration, t);
            Vector3 delta = at - previous;
            int count = Physics.SphereCastNonAlloc(previous, .82f, delta.normalized, jumpHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!jumpHits[i].collider.transform.IsChildOf(player))
                {
                    UltraJumpBlockReason = "Blocked by " + jumpHits[i].collider.name + " at " + jumpHits[i].point + " / arc step " + step;
                    return false;
                }
            previous = at;
        }
        return true;
    }

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
        string hint = Time.unscaledTime < jumpMessageUntil ? jumpMessage : "Aim at a roof - Q / Shift+Space: Ultra jump";
        GUI.Label(new Rect(14f, 12f, 520f, 26f), hint);
        GUI.Label(new Rect(Screen.width * .5f - 5f, Screen.height * .5f - 10f, 20f, 20f), "+");
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
