using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Exact candidate-set checks, not a lower quality/density performance preset.
public static class CastlePerformanceValidation
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);

    public static string Validate(DynamicInfinityCastle castle)
    {
        var blocks = ((IDictionary)typeof(DynamicInfinityCastle).GetField("denseBlocks", Hidden).GetValue(castle)).Values.Cast<object>()
            .Select(block => (bounds: Field<Bounds>(block, "bounds"),
                rooms: Field<Array>(block, "rooms").Cast<object>().Where(r => Field<bool>(r, "active"))
                    .Select(r => (id: r, bounds: Field<Bounds>(r, "bounds"))).ToArray())).ToArray();
        if (blocks.Length == 0) throw new InvalidOperationException("Initialize the castle before checking its spatial queries.");
        foreach (var block in blocks) foreach (var room in block.rooms)
            if (BoundsDistance(block.bounds, room.bounds) > .001f)
                throw new InvalidOperationException("A district room extends beyond its cluster bounds.");
        foreach (object block in ((IDictionary)typeof(DynamicInfinityCastle).GetField("denseBlocks", Hidden).GetValue(castle)).Values)
            if (!Field<bool>(block, "physicsNear") && Field<Array>(block, "crossings").Cast<object>().Any(crossing => Field<object>(crossing, "link") != null))
                throw new InvalidOperationException("A distant cluster has a crossing lease that would be skipped.");
        int queries = 0;
        foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
        foreach (float distance in new[] { 0f, 48f, 96f, 192f, 300f })
        foreach (Vector3 lead in new[] { Vector3.zero, direction * 60f, -direction * 60f })
        {
            Vector3 position = castle.player.position + direction * distance, ahead = position + lead;
            var exhaustive = new HashSet<object>(); var clustered = new HashSet<object>();
            foreach (var block in blocks)
            {
                bool near = block.bounds.SqrDistance(position) < 48f * 48f || block.bounds.SqrDistance(ahead) < 24f * 24f;
                foreach (var room in block.rooms)
                {
                    if (room.bounds.SqrDistance(position) >= 48f * 48f && room.bounds.SqrDistance(ahead) >= 24f * 24f) continue;
                    exhaustive.Add(room.id); if (near) clustered.Add(room.id);
                }
            }
            if (!exhaustive.SetEquals(clustered)) throw new InvalidOperationException("Cluster pruning changed nearby collision candidates.");
            queries++;
        }
        // Cached door transforms must be identical to the old per-frame expression.
        foreach (object template in (Array)typeof(DynamicInfinityCastle).GetField("districtTemplates", Hidden).GetValue(castle))
        {
            var module = Field<CastleArchitectureModule>(template, "module");
            var cached = Field<Matrix4x4[]>(template, "doorMatrices");
            for (int door = 0; door < module.ports.Length; door++)
            {
                var port = module.ports[door];
                var expected = Matrix4x4.TRS(port.position - port.outward * .06f + Vector3.up * 1.15f,
                    Quaternion.LookRotation(port.outward), new Vector3(port.width, 2.3f, .14f));
                for (int entry = 0; entry < 16; entry++)
                    if (Mathf.Abs(cached[door][entry] - expected[entry]) > .000001f)
                        throw new InvalidOperationException("Caching changed a visible door transform.");
            }
        }
        return $"PASS {queries} six-direction/fast-travel queries match exhaustive collision candidates; cached door transforms unchanged. " +
            $"Resident rooms={castle.DenseCastleRoomCount}, paths={castle.DenseCastleCrossingCount}; physics pool={castle.DenseCastlePhysicsCount}.";
    }

    private static float BoundsDistance(Bounds outer, Bounds inner) =>
        Mathf.Max(outer.SqrDistance(inner.min), outer.SqrDistance(inner.max));
}
