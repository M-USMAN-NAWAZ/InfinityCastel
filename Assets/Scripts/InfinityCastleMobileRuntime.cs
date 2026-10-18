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
        CreateUltraJumpButton();
        ApplySafeArea();
    }
    private void CreateUltraJumpButton()
    {
        RectTransform jump = null;
        foreach (UIVirtualButton existing in buttons)
            if (existing.name == "UI_Virtual_Button_Jump") jump = (RectTransform)existing.transform;
        var root = new GameObject("Ultra Jump", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(UIVirtualButton));
        root.layer = Controls.gameObject.layer;
        RectTransform rect = root.GetComponent<RectTransform>(); rect.SetParent(safeRoot, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(128f, 128f);
        rect.anchoredPosition = new Vector2(-93f, 355f);
        if (jump != null)
        {
            rect.SetParent(jump.parent, false);
            rect.anchorMin = jump.anchorMin; rect.anchorMax = jump.anchorMax;
            rect.anchoredPosition = jump.anchoredPosition + Vector2.up * (jump.rect.height * (1f - jump.pivot.y) + rect.rect.height * .5f + 24f);
        }
        Image image = root.GetComponent<Image>();
        if (jump != null && jump.TryGetComponent(out Image jumpImage)) image.sprite = jumpImage.sprite;
        image.color = new Color(.86f, .57f, .18f, .9f);
        Button feedback = root.GetComponent<Button>(); feedback.targetGraphic = image;
        ColorBlock colours = feedback.colors;
        colours.pressedColor = new Color(.65f, .55f, .35f); colours.highlightedColor = Color.white;
        feedback.colors = colours;
        feedback.navigation = new Navigation { mode = Navigation.Mode.None };
        UIVirtualButton button = root.GetComponent<UIVirtualButton>();
        button.buttonStateOutputEvent = new UIVirtualButton.BoolEvent();
        button.buttonStateOutputEvent.AddListener(inputs.UltraJumpInput);
        var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform labelRect = label.GetComponent<RectTransform>(); labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        Text text = label.GetComponent<Text>(); text.text = "\u2191\u2191\nSUPER\nJUMP"; text.fontSize = 22;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        buttons = safeRoot.GetComponentsInChildren<UIVirtualButton>(true);
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
        inputs.MoveInput(Vector2.zero); inputs.LookInput(Vector2.zero); inputs.JumpInput(false); inputs.SprintInput(false); inputs.UltraJumpInput(false);
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
