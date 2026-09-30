using System;
using System.IO;
using StarterAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

public static class InfinityCastlePreviewRunner
{
    private static Camera inspection;
    private static DynamicInfinityCastle castle;
    private static ThirdPersonController controller;
    private static double started;
    private static float capturedAt;
    private static Color32[] overviewPixels;
    private static int changedPixels;
    private static bool keyboardVerified;
#if ENABLE_INPUT_SYSTEM
    private static Keyboard keyboard;
    private static int inputPhase;
    private static float inputAt, jumpBase, jumpPeak;
    private static Vector3 moveStart;
#endif
    public static void Begin()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Preview is only for an isolated batch-mode project.");
        Application.targetFrameRate = 60;
        Application.runInBackground = true;
#if ENABLE_INPUT_SYSTEM
        // A hidden batch editor has no focused Game view; route synthetic device events to the game.
        InputSystem.settings = UnityEngine.Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - started > 120)
        { Debug.LogError("Play-mode preview timed out."); Finish(1); return; }
        if (!EditorApplication.isPlaying || Time.time < 1f) return;
        if (inspection != null)
        {
            if (Time.time - capturedAt < 8f) return;
            if (!Capture(inspection, "castle-playground-shifted.png")) return;
            bool clear = castle.ValidateNoBuildingOverlaps(out string error);
            File.WriteAllText(Path.GetFullPath("../castle-playmode.txt"),
                $"Grounded={controller.Grounded}\nInitialized={castle.IsInitialized}\nKeyboard movement and jump={keyboardVerified}\nNo overlaps={clear}\nChanged pixels={changedPixels}\n{error}\n");
            if (!clear || !controller.Grounded || changedPixels < 1000) { Debug.LogError("Play-mode validation failed: " + error); Finish(1); return; }
            Debug.Log("Play-mode castle preview and clearance checks passed.");
            Finish(0); return;
        }
        castle = UnityEngine.Object.FindFirstObjectByType<DynamicInfinityCastle>();
        controller = castle != null ? castle.player.GetComponent<ThirdPersonController>() : null;
        if (castle == null || controller == null || !castle.IsInitialized)
        { Debug.LogError("Play-mode castle did not initialize on solid ground."); Finish(1); return; }
        if (!VerifyKeyboard()) return;
        Camera camera = Camera.main;
        if (!Capture(camera, "castle-playground-player.png")) return;
        var go = new GameObject("Castle Inspection Camera");
        inspection = go.AddComponent<Camera>(); inspection.CopyFrom(camera);
        inspection.enabled = false;
        inspection.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        inspection.transform.position = castle.player.position + new Vector3(30f, 20f, -38f);
        inspection.transform.LookAt(castle.player.position + new Vector3(0f, 13f, 30f));
        if (!Capture(inspection, "castle-playground-overview.png")) return;
        capturedAt = Time.time;
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
    private static bool VerifyKeyboard()
    {
        if (keyboardVerified) return true;
#if ENABLE_INPUT_SYSTEM
        if (inputPhase == 0)
        {
            if (!controller.Grounded)
            { Debug.LogError("Playground player did not start grounded."); Finish(1); return false; }
            PlayerInput playerInput = controller.GetComponent<PlayerInput>();
            if (playerInput == null || playerInput.currentActionMap == null || !playerInput.currentActionMap.enabled)
            { Debug.LogError("Playground player has no active input action map."); Finish(1); return false; }
            keyboard = InputSystem.AddDevice<Keyboard>("Castle Validation Keyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("Castle Validation Mouse");
            playerInput.SwitchCurrentControlScheme("KeyboardMouse", keyboard, mouse);
            moveStart = controller.transform.position; inputAt = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); inputPhase = 1; return false;
        }
        if (inputPhase == 1)
        {
            if (Time.time - inputAt < 0.6f) return false;
            if (Vector3.ProjectOnPlane(controller.transform.position - moveStart, Vector3.up).magnitude < 0.6f)
            {
                Debug.LogError("Playground WASD binding did not move the player: input=" + controller.GetComponent<StarterAssetsInputs>().move +
                    ", key=" + keyboard.wKey.isPressed + ", position=" + controller.transform.position + ", start=" + moveStart);
                Finish(1); return false;
            }
            jumpBase = jumpPeak = controller.transform.position.y; inputAt = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); inputPhase = 2; return false;
        }
        jumpPeak = Mathf.Max(jumpPeak, controller.transform.position.y);
        if (Time.time - inputAt < 1.2f) return false;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        if (jumpPeak - jumpBase < 0.8f || !controller.Grounded)
        { Debug.LogError("Playground Space binding did not jump and land."); Finish(1); return false; }
#endif
        keyboardVerified = true;
        Debug.Log("Playground actual WASD and Space bindings passed."); return true;
    }
    private static bool Capture(Camera camera, string name)
    {
        try
        {
        RenderTexture target = new(1280, 720, 24, RenderTextureFormat.ARGB32);
        target.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        Texture2D image = new(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        Color32[] pixels = image.GetPixels32();
        long brightness = 0;
        foreach (Color32 pixel in pixels) brightness += pixel.r + pixel.g + pixel.b;
        if (brightness < pixels.Length * 3) throw new InvalidOperationException("Castle preview rendered blank.");
        if (name == "castle-playground-overview.png") overviewPixels = pixels;
        if (name == "castle-playground-shifted.png" && overviewPixels != null)
            for (int i = 0; i < pixels.Length; i++)
                if (Math.Abs(pixels[i].r - overviewPixels[i].r) + Math.Abs(pixels[i].g - overviewPixels[i].g) + Math.Abs(pixels[i].b - overviewPixels[i].b) > 15) changedPixels++;
        File.WriteAllBytes(Path.GetFullPath("../" + name), image.EncodeToPNG());
        RenderTexture.active = previous; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
        return true;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); return false; }
    }
}
