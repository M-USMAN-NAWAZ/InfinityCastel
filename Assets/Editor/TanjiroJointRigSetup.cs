using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Scene-only skin variant. Original FBX files, prefabs, UVs and materials are kept.
public static class TanjiroJointRigSetup
{
    public const string Folder = "Assets/Characters/SunBreathers/ProceduralRig";
    public static void RestoreBind(SkinnedMeshRenderer skin)
    {
        var bind = skin.sharedMesh.bindposes;
        var world = bind.Select(p => skin.transform.localToWorldMatrix * p.inverse).ToArray();
        // Renderer bones are exported in parent-before-child order.
        for (int i = 0; i < skin.bones.Length; i++)
            skin.bones[i].SetPositionAndRotation(world[i].GetPosition(), world[i].rotation);
    }
    public static void Upgrade(TanjiroSword sword)
    {
        var skin = sword.body;
        skin.sharedMesh = sword.drawableBodyMesh;
        RestoreBind(skin);
        if (skin.bones.Any(b => b.name == "LeftForearmTwist")) { RepairWaistAndCoat(sword); return; }
        var original = skin.sharedMesh;
        var mesh = Object.Instantiate(original); mesh.name = "Tanjiro_AnatomicalSkin";
        var bones = skin.bones.ToList(); var bind = original.bindposes.ToList();
        var vertices = original.vertices;
        var influence = original.boneWeights.Select(w => {
            var result = new Dictionary<int, float>();
            void Add(int id, float weight) { if (weight > 0) result[id] = weight; }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            return result;
        }).ToArray();
        Transform Bone(string name) => bones.First(b => b.name == name);
        int Index(string name) => bones.FindIndex(b => b.name == name);
        Transform Child(string name, Transform parent, Vector3 world)
        {
            var child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            child.position = world; child.localRotation = Quaternion.identity; return child;
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            int middle = Index(side + "ThumbIntermediate"), distal = Index(side + "ThumbDistal");
            Transform first = bones[middle], last = bones[distal];
            Vector3 axis = (last.position - first.position).normalized;
            var samples = Enumerable.Range(0, vertices.Length).Where(i => influence[i].TryGetValue(middle, out float w) && w > .05f).ToArray();
            float extent = samples.Max(i => Vector3.Dot(skin.transform.TransformPoint(vertices[i]) - first.position, axis));
            float joint = Mathf.Max(.004f, extent * .48f);
            last.position = first.position + axis * joint;
            bind[distal] = last.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            foreach (int i in samples)
            {
                float along = Vector3.Dot(skin.transform.TransformPoint(vertices[i]) - first.position, axis);
                float share = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(joint - .003f, extent * .92f, along));
                float moved = influence[i][middle] * share;
                influence[i][middle] -= moved; influence[i][distal] = moved;
            }
            foreach (bool forearm in new[] { false, true })
            {
                Transform source = Bone(side + (forearm ? "LowerArm" : "UpperArm"));
                Transform end = Bone(side + (forearm ? "Hand" : "LowerArm"));
                string name = side + (forearm ? "ForearmTwist" : "UpperArmTwist");
                Transform twist = Child(name, source, Vector3.Lerp(source.position, end.position, .55f));
                int oldId = bones.IndexOf(source), newId = bones.Count;
                Vector3 delta = end.position - source.position;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!influence[i].TryGetValue(oldId, out float weight)) continue;
                    float u = Vector3.Dot(skin.transform.TransformPoint(vertices[i]) - source.position, delta) / delta.sqrMagnitude;
                    float share = forearm ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .95f, u)) : .8f * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.1f, .85f, u)));
                    influence[i][oldId] = weight * (1 - share); influence[i][newId] = weight * share;
                }
                bones.Add(twist); bind.Add(twist.worldToLocalMatrix * skin.transform.localToWorldMatrix);
            }
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                int id = Index(side + finger + "Distal"); Transform end = bones[id];
                var points = Enumerable.Range(0, vertices.Length).Where(i => influence[i].TryGetValue(id, out float w) && w > .03f)
                    .Select(i => skin.transform.TransformPoint(vertices[i])).ToArray();
                if (points.Length == 0) throw new InvalidOperationException(side + finger + " has no weighted fingertip vertices.");
                float far = points.Max(p => Vector3.Dot(p - end.position, end.up));
                var tip = points.Where(p => Vector3.Dot(p - end.position, end.up) >= far - .003f).ToArray();
                Child(side + finger + "Tip", end, tip.Aggregate(Vector3.zero, (a, b) => a + b) / tip.Length);
            }
        }
        mesh.boneWeights = influence.Select(weights => {
            var best = weights.Where(pair => pair.Value > .00001f).OrderByDescending(pair => pair.Value).Take(4).ToArray();
            float sum = best.Sum(pair => pair.Value); var w = new BoneWeight();
            if (best.Length > 0) { w.boneIndex0 = best[0].Key; w.weight0 = best[0].Value / sum; }
            if (best.Length > 1) { w.boneIndex1 = best[1].Key; w.weight1 = best[1].Value / sum; }
            if (best.Length > 2) { w.boneIndex2 = best[2].Key; w.weight2 = best[2].Value / sum; }
            if (best.Length > 3) { w.boneIndex3 = best[3].Key; w.weight3 = best[3].Value / sum; }
            return w;
        }).ToArray();
        mesh.bindposes = bind.ToArray();
        if (!original.uv.SequenceEqual(mesh.uv) || original.vertexCount != mesh.vertexCount)
            throw new InvalidOperationException("Rig upgrade changed the source geometry or UVs.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Characters/SunBreathers", "ProceduralRig");
        string path = Folder + "/Tanjiro_AnatomicalSkin.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
        else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
        skin.bones = bones.ToArray(); skin.sharedMesh = saved; sword.drawableBodyMesh = saved;
        EditorUtility.SetDirty(skin); EditorUtility.SetDirty(sword); AssetDatabase.SaveAssets();
        RepairWaistAndCoat(sword);
    }

    [Serializable] private sealed class CoatMask { public Vector3[] samples; }
    private static void RepairWaistAndCoat(TanjiroSword sword)
    {
        // Always derive this scene-only variant from the preserved anatomical
        // skin. Reinstalling must not progressively reweight an already fixed mesh.
        var skin = sword.body;
        var original = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Tanjiro_AnatomicalSkin.asset");
        var maskAsset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Editor/TanjiroCoatMask.json");
        if (original == null || maskAsset == null) throw new InvalidOperationException("Missing anatomical skin or baked coat mask.");
        var samples = JsonUtility.FromJson<CoatMask>(maskAsset.text).samples;
        var mask = samples.ToDictionary(p => new Vector2Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000)), p => p.z);
        var bones = skin.bones.Take(original.bindposes.Length).ToList();
        skin.bones = bones.ToArray(); skin.sharedMesh = original; RestoreBind(skin);
        var bind = original.bindposes.ToList();
        int Index(string name) => bones.FindIndex(b => b.name == name);
        Transform hips = bones[Index("Hips")];
        Transform actor = sword.GetComponentInParent<StarterAssets.ThirdPersonController>().transform;
        Vector3 up = actor.up, forward = Vector3.ProjectOnPlane(bones[Index("Head")].forward, up).normalized;
        Quaternion frame = Quaternion.LookRotation(forward, up), inverse = Quaternion.Inverse(frame);
        float hipHeight = Vector3.Dot(hips.position - actor.position, up), scale = hipHeight / .8603042f;
        var panelIds = new int[3, 2];
        foreach (int panel in new[] { 0, 1, 2 })
        {
            string name = panel == 0 ? "CoatBack" : panel == 1 ? "CoatLeft" : "CoatRight";
            float side = panel == 0 ? 0 : panel == 1 ? -.21f : .21f;
            float depth = panel == 0 ? -.115f : .025f;
            Transform parent = hips;
            for (int segment = 0; segment < 2; segment++)
            {
                string label = name + (segment == 0 ? "Root" : "Hem");
                Transform joint = parent.Find(label);
                if (joint == null) { joint = new GameObject(label).transform; joint.SetParent(parent, false); }
                joint.position = actor.position + frame * (new Vector3(side, segment == 0 ? .98f : .73f, depth) * scale);
                joint.rotation = hips.rotation;
                panelIds[panel, segment] = bones.Count; bones.Add(joint);
                bind.Add(joint.worldToLocalMatrix * skin.transform.localToWorldMatrix);
                parent = joint;
            }
        }
        var mesh = Object.Instantiate(original); mesh.name = "Tanjiro_WaistCoatSkin";
        Vector3[] vertices = original.vertices; Vector2[] uv = original.uv;
        Color[] colors = original.colors;
        if (colors.Length != vertices.Length) colors = new Color[vertices.Length];
        var weights = original.boneWeights;
        int hipsId = Index("Hips"), spineId = Index("Spine");
        var legIds = new HashSet<int>(Enumerable.Range(0, original.bindposes.Length).Where(i =>
            bones[i].name.Contains("UpperLeg") || bones[i].name.Contains("LowerLeg") || bones[i].name.EndsWith("Foot") || bones[i].name.EndsWith("Toes")));
        var legPairs = legIds.ToDictionary(id => id, id => Index(bones[id].name.StartsWith("Left") ?
            "Right" + bones[id].name.Substring(4) : "Left" + bones[id].name.Substring(5)));
        float leftSide = Mathf.Sign((inverse * (bones[Index("LeftUpperLeg")].position - actor.position)).x);
        int clothVertices = 0, fixedWaist = 0;
        float Smooth(float a, float b, float v) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, v));
        var localVertices = new Vector3[vertices.Length];
        var clothMasks = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            localVertices[i] = inverse * (skin.transform.TransformPoint(vertices[i]) - actor.position) / scale;
            var key = new Vector2Int(Mathf.RoundToInt(uv[i].x * 100000), Mathf.RoundToInt(uv[i].y * 100000));
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                if (mask.TryGetValue(key + new Vector2Int(x, y), out float value)) clothMasks[i] = Mathf.Max(clothMasks[i], value);
        }
        ResolveLowerCloth(original.triangles, localVertices, clothMasks);
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i]; var influence = new Dictionary<int, float>();
            void Add(int id, float value) { if (value > .000001f) influence[id] = influence.TryGetValue(id, out float old) ? old + value : value; }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            Vector3 p = localVertices[i];
            // Thigh heat weights leaked above the hip crease into the abdomen.
            // Transfer that influence to the pelvis/lumbar region, leaving the
            // actual joint positions and leg lengths unchanged.
            float remove = Smooth(.885f, .95f, p.y), moved = 0;
            foreach (int id in influence.Keys.ToArray()) if (legIds.Contains(id))
            { float take = influence[id] * remove; influence[id] -= take; moved += take; }
            float lumbar = Smooth(.92f, 1.08f, p.y);
            Add(hipsId, moved * (1 - lumbar)); Add(spineId, moved * lumbar);
            if (moved > .01f) fixedWaist++;
            float ownLeg = Smooth(.015f, .045f, Mathf.Abs(p.x)) * (1 - Smooth(.78f, .86f, p.y));
            bool left = p.x * leftSide > 0;
            foreach (int id in influence.Keys.ToArray())
            {
                if (!legPairs.TryGetValue(id, out int own) || bones[id].name.StartsWith("Left") == left) continue;
                float take = influence[id] * ownLeg;
                influence[id] -= take;
                Add(own, take);
            }
            float cloth = clothMasks[i] * (1 - Smooth(.98f, 1.10f, p.y));
            colors[i].b = cloth;
            if (cloth > .001f)
            {
                // Back and two open front panels blend at the side seams.
                // No thigh, shin or foot directly drives this fabric.
                foreach (int id in influence.Keys.ToArray()) influence[id] *= 1 - cloth;
                float back = Smooth(-.035f, .055f, -p.z), hem = 1 - Smooth(.60f, .80f, p.y);
                int front = p.x < 0 ? 1 : 2;
                Add(panelIds[0, 0], cloth * back * (1 - hem)); Add(panelIds[0, 1], cloth * back * hem);
                Add(panelIds[front, 0], cloth * (1 - back) * (1 - hem)); Add(panelIds[front, 1], cloth * (1 - back) * hem);
                if (cloth > .5f) clothVertices++;
            }
            var best = influence.Where(pair => pair.Value > .000001f).OrderByDescending(pair => pair.Value).Take(4).ToArray();
            float sum = best.Sum(pair => pair.Value); var result = new BoneWeight();
            if (best.Length > 0) { result.boneIndex0 = best[0].Key; result.weight0 = best[0].Value / sum; }
            if (best.Length > 1) { result.boneIndex1 = best[1].Key; result.weight1 = best[1].Value / sum; }
            if (best.Length > 2) { result.boneIndex2 = best[2].Key; result.weight2 = best[2].Value / sum; }
            if (best.Length > 3) { result.boneIndex3 = best[3].Key; result.weight3 = best[3].Value / sum; }
            weights[i] = result;
        }
        if (clothVertices < 1500 || fixedWaist < 100) throw new InvalidOperationException($"Waist/coat repair selected too few vertices: {fixedWaist}/{clothVertices}.");
        int seams = BindLowerSeams(original.triangles, localVertices, weights, colors);
        mesh.boneWeights = weights; mesh.bindposes = bind.ToArray(); mesh.colors = colors;
        string path = Folder + "/Tanjiro_WaistCoatSkin.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
        else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
        skin.bones = bones.ToArray(); skin.sharedMesh = saved; sword.drawableBodyMesh = saved;
        EditorUtility.SetDirty(skin); EditorUtility.SetDirty(sword); AssetDatabase.SaveAssets();
        Debug.Log($"Procedural waist/coat skin: repaired {fixedWaist} torso vertices and {seams} lower seam points; {clothVertices} coat vertices use six separate panel bones.");
    }

    internal static IEnumerable<(int first, int second)> LowerSeamPairs(int[] triangles, Vector3[] vertices)
    {
        var positions = new Dictionary<Vector3Int, int>();
        var point = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3Int key = Vector3Int.RoundToInt(vertices[i] * 100000);
            if (!positions.TryGetValue(key, out point[i])) positions.Add(key, point[i] = i);
        }
        var edges = new Dictionary<(int, int), (int a, int b, int c, int d, int uses)>();
        void Add(int a, int b)
        {
            if (vertices[a].y < .36f || vertices[a].y > .91f || vertices[b].y < .36f || vertices[b].y > .91f || point[a] == point[b]) return;
            if (point[a] > point[b]) (a, b) = (b, a);
            var key = (point[a], point[b]);
            if (edges.TryGetValue(key, out var edge)) edges[key] = (edge.a, edge.b, a, b, edge.uses + 1);
            else edges.Add(key, (a, b, a, b, 1));
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Add(triangles[i], triangles[i + 1]); Add(triangles[i + 1], triangles[i + 2]); Add(triangles[i + 2], triangles[i]);
        }
        foreach (var edge in edges.Values)
        {
            // A two-face edge belongs to one surface. Coincident layers can
            // have more faces and must keep their independent weights.
            if (edge.uses != 2) continue;
            if (edge.a != edge.c) yield return (edge.a, edge.c);
            if (edge.b != edge.d) yield return (edge.b, edge.d);
        }
    }

    private static int BindLowerSeams(int[] triangles, Vector3[] vertices, BoneWeight[] weights, Color[] colors)
    {
        int[] parents = Enumerable.Range(0, vertices.Length).ToArray();
        int Root(int i)
        {
            while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; }
            return i;
        }
        var members = new HashSet<int>();
        foreach (var (a, b) in LowerSeamPairs(triangles, vertices))
        { parents[Root(b)] = Root(a); members.Add(a); members.Add(b); }
        int repaired = 0;
        foreach (var group in members.GroupBy(Root))
        {
            int first = group.First();
            if (group.All(i => weights[i].Equals(weights[first]) && Mathf.Abs(colors[i].b - colors[first].b) < .001f)) continue;
            var total = new Dictionary<int, float>();
            void Add(int bone, float weight)
            { if (weight > 0) total[bone] = total.TryGetValue(bone, out float old) ? old + weight : weight; }
            foreach (int i in group)
            {
                BoneWeight w = weights[i];
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            }
            var best = total.OrderByDescending(w => w.Value).Take(4).ToArray();
            float sum = best.Sum(w => w.Value);
            var combined = new BoneWeight { boneIndex0 = best[0].Key, weight0 = best[0].Value / sum };
            if (best.Length > 1) { combined.boneIndex1 = best[1].Key; combined.weight1 = best[1].Value / sum; }
            if (best.Length > 2) { combined.boneIndex2 = best[2].Key; combined.weight2 = best[2].Value / sum; }
            if (best.Length > 3) { combined.boneIndex3 = best[3].Key; combined.weight3 = best[3].Value / sum; }
            float cloth = group.Average(i => colors[i].b);
            foreach (int i in group) { weights[i] = combined; colors[i].b = cloth; }
            repaired++;
        }
        return repaired;
    }

    private static void ResolveLowerCloth(int[] triangles, Vector3[] vertices, float[] masks)
    {
        // A texture vote can mislabel individual corners of a black checker
        // or a trouser island. Keep each connected lower surface on one rig.
        int[] parents = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int root = Find(triangles[i]);
            parents[Find(triangles[i + 1])] = root;
            parents[Find(triangles[i + 2])] = root;
        }
        var votes = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            if (vertices[i].y < .89f) votes[Find(i)] += masks[i] - .5f;
        for (int i = 0; i < vertices.Length; i++)
        {
            float lower = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.85f, .92f, vertices[i].y));
            masks[i] = Mathf.Lerp(masks[i], votes[Find(i)] > 0 ? 1 : 0, lower);
        }
    }
}
