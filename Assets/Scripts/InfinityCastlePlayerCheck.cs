#if !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using StarterAssets;
using UnityEngine;

// Opt-in compiled-player check: unlike the Editor, a player enforces mesh readability.
public sealed class InfinityCastlePlayerCheck : MonoBehaviour
{
    private string report;
    private double deadline;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartCheck()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains("-castle-check")) return;
        int index = Array.IndexOf(args, "-castle-report");
        var check = new GameObject("Compiled castle regression").AddComponent<InfinityCastlePlayerCheck>();
        check.report = args[index + 1];
        File.WriteAllText(check.report, "Compiled player=" + Application.platform + "; development=" + Debug.isDebugBuild + "\n");
        check.deadline = Time.realtimeSinceStartupAsDouble + 240d;
        Application.logMessageReceived += check.OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.AppendAllText(report, "FAIL " + message + "\n" + trace + "\n");
        Application.Quit(1);
    }
    private void Update()
    {
        if (Time.realtimeSinceStartupAsDouble > deadline) { Check(false, "Compiled castle generation timed out"); enabled = false; }
    }
    private void Check(bool passed, string message)
    {
        File.AppendAllText(report, (passed ? "PASS " : "FAIL ") + message + "\n");
        if (!passed) throw new InvalidOperationException(message);
    }
    private IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Time.captureDeltaTime = 1f / 30f;
        DynamicInfinityCastle castle = FindFirstObjectByType<DynamicInfinityCastle>();
        Check(castle != null, "Saved Playground includes the runtime generator");
        while (!castle.IsPlayerReady) yield return null;
        File.AppendAllText(report, "MEASURE Full startup seconds=" + castle.PlayerReadySeconds.ToString("F2") +
            "; work frames=" + castle.CastleWorkFrames + "; max work unit ms=" + castle.MaxCastleWorkUnitMilliseconds.ToString("F2") +
            "; unit=" + castle.MaxCastleWorkUnitName + "\n");
        Check(castle.IsInitialized && castle.DenseCastleRoomCount >= 500 && castle.PreparedCastleLayerCount >= 2,
            "Controls unlock after the surrounding volume and multiple full lower layers exist");
        var player = castle.player.GetComponent<ThirdPersonController>();
        var input = castle.player.GetComponent<StarterAssetsInputs>();
        for (int i = 0; i < 20; i++) yield return null;
        Check(player.enabled && player.Grounded, "Player starts on a collidable native roof in the compiled build");
        foreach (GameObject source in castle.buildingPrefabs.Concat(castle.pathPrefabs))
        {
            MeshCollider[] colliders = source.GetComponentsInChildren<MeshCollider>(true);
            Check(colliders.Length > 0 && colliders.All(c => c.sharedMesh != null && c.sharedMesh.isReadable),
                source.name + " retains CPU mesh data for transformed runtime colliders");
            var parent = new GameObject("Transformed collision probe");
            parent.transform.position = Vector3.up * 2000f; parent.transform.localScale = new Vector3(1.3f, .85f, 1.1f);
            GameObject sample = Instantiate(source, parent.transform); sample.transform.localRotation = Quaternion.Euler(0f, 27f, 0f); sample.SetActive(true);
            Physics.SyncTransforms(); bool hit = false;
            foreach (Collider collider in sample.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                for (int x = 1; x < 8 && !hit; x++) for (int z = 1; z < 8 && !hit; z++)
                {
                    Vector3 from = new(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 8f), bounds.max.y + 1f, Mathf.Lerp(bounds.min.z, bounds.max.z, z / 8f));
                    hit = collider.Raycast(new Ray(from, Vector3.down), out _, bounds.size.y + 2f);
                }
            }
            Check(hit, source.name + " has working cooked collision under rotated, nonuniform parents");
            Destroy(parent); yield return null;
        }
        Vector3 start = player.transform.position; input.JumpInput(true); float peak = start.y;
        for (int i = 0; i < 60; i++)
        {
            yield return null; peak = Mathf.Max(peak, player.transform.position.y);
            Check(Vector3.Distance(start, player.transform.position) < 6f, "Normal warm-up jump stays local");
        }
        Check(peak > start.y + .7f && player.Grounded, "Normal jump lands without a spawn reset during warm-up");
        player.transform.position = start + Vector3.right * 64f; player.ResetFallVelocity();
        for (int i = 0; i < 5; i++) yield return null;
        Check(Vector3.Distance(start, player.transform.position) > 60f, "Horizontal travel does not trigger the old spawn-distance reset");
        player.transform.SetPositionAndRotation(start, Quaternion.identity); player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity(); player.ResetFallVelocity();
        while (!castle.IsInitialized) yield return null;
        Check(castle.BuildingWingCount >= 70 && castle.DenseCastleRoomCount >= 500, "Compiled build completes castle generation: wings=" + castle.BuildingWingCount + "; distributed rooms=" + castle.DenseCastleRoomCount);
        Check(castle.ValidateNoBuildingOverlaps(out string clearance), "Compiled model clearance " + clearance);
        Check(castle.ValidateSurroundingCastle(out string coverage), "Compiled six-direction coverage " + coverage);
        Check(castle.UsesTrainedDirector, "Trained ML director is included in the compiled player");
        castle.periodicRebuilding = false; castle.simultaneousShifts = 0;
        input.MoveInput(Vector2.zero); input.SprintInput(false);
        player.transform.SetPositionAndRotation(start + Vector3.right * 112f + Vector3.up * 5f, Quaternion.identity);
        player.GetComponent<InfinityGravityBody>().RestoreDefaultGravity(); player.ResetFallVelocity();
        Vector3 departure = player.transform.position;
        bool landed = false;
        for (int frame = 0; frame < 420; frame++)
        {
            yield return null;
            if (frame > 30 && player.Grounded && Vector3.Distance(departure, player.transform.position) > 20f)
            { landed = true; break; }
        }
        Check(landed && Vector3.Distance(start, player.transform.position) > 30f,
            "Actual deep departure lands on real castle collision without returning to spawn; position=" + player.transform.position);
        int transforms = castle.GetComponentsInChildren<Transform>(true).Length;
        player.enabled = false;
        var blocks = (System.Collections.IDictionary)typeof(DynamicInfinityCastle).GetField("denseBlocks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(castle);
        foreach (Vector3 direction in new[] { Vector3.down, Vector3.up, Vector3.right })
        {
            for (int frame = 0; frame < 240; frame++)
            {
                player.transform.position += direction * 1.5f;
                yield return null;
                if (frame < 120 || frame % 30 != 0) continue;
                Vector3Int cell = Vector3Int.RoundToInt(player.transform.position / DynamicInfinityCastle.DistrictSpan);
                bool ready = true;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (x * x + y * y + z * z <= 1 && !blocks.Contains(cell + new Vector3Int(x, y, z))) ready = false;
                Check(ready, "Frame-budgeted inner residency at 45 m/s toward " + direction);
            }
        }
        Check(castle.GetComponentsInChildren<Transform>(true).Length == transforms && castle.DenseCastlePhysicsCount <= 96,
            "Compiled streaming retains the fixed GameObject and physical-body pools");
        File.AppendAllText(report, "ALL COMPILED PLAYER CHECKS PASSED\n"); Application.Quit(0);
    }
}
#endif
