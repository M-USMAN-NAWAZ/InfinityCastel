using StarterAssets;
using UnityEngine;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(-100)]
public sealed class InfinityScreenLook : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    public float degreesPerScreenHeight = 180f;
    private StarterAssetsInputs inputs;
    private int pointer = int.MinValue;
    private Vector2 lastPosition, pendingPixels;
    public bool IsDragging => pointer != int.MinValue;

    public void Configure(StarterAssetsInputs target) { ResetInput(); inputs = target; }
    public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;
    private bool OverOtherUI(PointerEventData data)
    {
        GameObject target = data.pointerCurrentRaycast.gameObject;
        return target != null && target != gameObject && target.GetComponentInParent<Canvas>() != null;
    }
    public void OnPointerDown(PointerEventData data)
    {
        if (IsDragging || OverOtherUI(data)) return;
        pointer = data.pointerId; lastPosition = data.position;
    }
    public void OnDrag(PointerEventData data)
    {
        if (data.pointerId != pointer) return;
        Vector2 delta = data.position - lastPosition;
        lastPosition = data.position;
        if (!OverOtherUI(data)) pendingPixels += delta;
    }
    public void OnPointerUp(PointerEventData data) { if (data.pointerId == pointer) ResetInput(); }
    public void FlushLook(float dt, float screenHeight)
    {
        if (inputs == null) return;
        inputs.LookInput(new Vector2(pendingPixels.x, -pendingPixels.y) *
            (degreesPerScreenHeight / Mathf.Max(1f, screenHeight) / Mathf.Max(0.0001f, dt)));
        pendingPixels = Vector2.zero;
    }
    private void LateUpdate() => FlushLook(Time.deltaTime, Screen.height);
    public void ResetInput()
    {
        pointer = int.MinValue; pendingPixels = Vector2.zero;
        if (inputs != null) inputs.LookInput(Vector2.zero);
    }
    private void OnDisable() => ResetInput();
}
