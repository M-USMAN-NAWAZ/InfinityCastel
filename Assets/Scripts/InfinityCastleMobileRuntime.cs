using StarterAssets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class InfinityCastleMobileRuntime : MonoBehaviour
{
    private StarterAssetsInputs inputs;
    private RectTransform safeRoot;
    private Rect lastSafeArea;
    private Vector2Int lastSize;
    private int previousQuality, previousFrameRate;
    private RenderPipelineAsset previousPipeline;
    private bool profileApplied;
    private UIVirtualJoystick[] joysticks;
    private UIVirtualButton[] buttons;
    public UICanvasControllerInput Controls { get; private set; }
    public InfinityScreenLook ScreenLook { get; private set; }

    public void Configure(DynamicInfinityCastle castle)
    {
        inputs = castle.player.GetComponent<StarterAssetsInputs>();
        if (Application.isMobilePlatform && !profileApplied)
        {
            previousQuality = QualitySettings.GetQualityLevel();
            previousFrameRate = Application.targetFrameRate;
            previousPipeline = QualitySettings.renderPipeline;
            QualitySettings.SetQualityLevel(0, true);
            if (castle.mobileRenderPipeline != null) QualitySettings.renderPipeline = castle.mobileRenderPipeline;
            Application.targetFrameRate = 30;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            profileApplied = true;
        }
        if (!Application.isMobilePlatform && !castle.previewTouchControls) return;
        if (ScreenLook != null)
        {
            ScreenLook.Configure(inputs); Controls.gameObject.SetActive(true);
            ApplySafeArea(); return;
        }
        Controls = FindFirstObjectByType<UICanvasControllerInput>(FindObjectsInactive.Include);
        if (Controls == null && castle.mobileControlsPrefab != null)
            Controls = Instantiate(castle.mobileControlsPrefab).GetComponent<UICanvasControllerInput>();
        if (Controls == null) { Debug.LogError("Assign the existing Starter Assets mobile controls canvas.", castle); return; }
        Controls.starterAssetsInputs = inputs;
        inputs.analogMovement = true;
        inputs.touchLook = true;
        inputs.cursorLocked = inputs.cursorInputForLook = false;
        Cursor.lockState = CursorLockMode.None;
#if ENABLE_INPUT_SYSTEM
        var playerInput = castle.player.GetComponent<PlayerInput>();
        if (playerInput != null) playerInput.neverAutoSwitchControlSchemes = true;
#if UNITY_ANDROID || UNITY_IOS
        var autoSwitch = Controls.GetComponent<MobileDisableAutoSwitchControls>();
        if (autoSwitch != null) autoSwitch.playerInput = playerInput;
#endif
#endif
        safeRoot = new GameObject("Touch Safe Area", typeof(RectTransform)).GetComponent<RectTransform>();
        Transform canvas = Controls.transform;
        safeRoot.SetParent(canvas, false);
        for (int i = canvas.childCount - 1; i >= 0; i--)
            if (canvas.GetChild(i) != safeRoot) canvas.GetChild(i).SetParent(safeRoot, false);
        canvas.localScale = Vector3.one;
        joysticks = safeRoot.GetComponentsInChildren<UIVirtualJoystick>(true);
        buttons = safeRoot.GetComponentsInChildren<UIVirtualButton>(true);
        foreach (UIVirtualJoystick joystick in joysticks)
            if (joystick.name.Contains("Look")) joystick.gameObject.SetActive(false);
        var lookRoot = new GameObject("Castle Screen Look", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform lookRect = lookRoot.GetComponent<RectTransform>();
        lookRect.SetParent(canvas, false); lookRect.SetAsFirstSibling();
        lookRect.anchorMin = Vector2.zero; lookRect.anchorMax = Vector2.one;
        lookRect.offsetMin = lookRect.offsetMax = Vector2.zero;
        lookRoot.GetComponent<Image>().color = Color.clear;
        lookRoot.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        ScreenLook = lookRoot.AddComponent<InfinityScreenLook>(); ScreenLook.Configure(inputs);
        Controls.gameObject.SetActive(true);
        ApplySafeArea();
    }
    private void Update()
    {
        if (safeRoot != null && (lastSafeArea != Screen.safeArea || lastSize != new Vector2Int(Screen.width, Screen.height))) ApplySafeArea();
    }
    private void ApplySafeArea()
    {
        lastSafeArea = Screen.safeArea; lastSize = new Vector2Int(Screen.width, Screen.height);
        if (lastSize.x <= 0 || lastSize.y <= 0) return;
        safeRoot.anchorMin = new Vector2(lastSafeArea.xMin / lastSize.x, lastSafeArea.yMin / lastSize.y);
        safeRoot.anchorMax = new Vector2(lastSafeArea.xMax / lastSize.x, lastSafeArea.yMax / lastSize.y);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
    }
    private void ReleaseInputs()
    {
        if (inputs == null) return;
        if (ScreenLook != null) ScreenLook.ResetInput();
        if (joysticks != null) foreach (UIVirtualJoystick joystick in joysticks) if (joystick != null) joystick.ResetInput();
        if (buttons != null) foreach (UIVirtualButton button in buttons) if (button != null) button.ResetInput();
        inputs.MoveInput(Vector2.zero); inputs.LookInput(Vector2.zero); inputs.JumpInput(false); inputs.SprintInput(false);
    }
    private void OnApplicationPause(bool paused) { if (paused) ReleaseInputs(); }
    private void OnApplicationFocus(bool focused) { if (!focused) ReleaseInputs(); }
    private void OnDisable() => ReleaseInputs();
    private void OnDestroy()
    {
        if (!profileApplied) return;
        QualitySettings.SetQualityLevel(previousQuality, true);
        QualitySettings.renderPipeline = previousPipeline;
        Application.targetFrameRate = previousFrameRate;
    }
}
