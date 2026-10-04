using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class UIVirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable]
    public class Event : UnityEvent<Vector2> { }
    
    [Header("Rect References")]
    public RectTransform containerRect;
    public RectTransform handleRect;

    [Header("Settings")]
    public float joystickRange = 50f;
    public float magnitudeMultiplier = 1f;
    public bool invertXOutputValue;
    public bool invertYOutputValue;

    [Header("Output")]
    public Event joystickOutputEvent;
    private int? activePointer;

    void Start()
    {
        SetupHandle();
    }

    private void SetupHandle()
    {
        if(handleRect)
        {
            UpdateHandleRectPosition(Vector2.zero);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (activePointer.HasValue) return;
        activePointer = eventData.pointerId;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (activePointer != eventData.pointerId || containerRect == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, eventData.position, eventData.pressEventCamera, out Vector2 position);
        
        position = ApplySizeDelta(position);
        
        Vector2 clampedPosition = ClampValuesToMagnitude(position);

        Vector2 outputPosition = ApplyInversionFilter(clampedPosition);

        OutputPointerEventValue(outputPosition * magnitudeMultiplier);

        if(handleRect)
        {
            UpdateHandleRectPosition(clampedPosition * joystickRange);
        }
        
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (activePointer != eventData.pointerId) return;
        activePointer = null;
        OutputPointerEventValue(Vector2.zero);

        if(handleRect)
        {
             UpdateHandleRectPosition(Vector2.zero);
        }
    }

    private void OutputPointerEventValue(Vector2 pointerPosition)
    {
        joystickOutputEvent?.Invoke(pointerPosition);
    }
    public void ResetInput() { activePointer = null; OutputPointerEventValue(Vector2.zero); SetupHandle(); }
    private void OnDisable() => ResetInput();

    private void UpdateHandleRectPosition(Vector2 newPosition)
    {
        handleRect.anchoredPosition = newPosition;
    }

    Vector2 ApplySizeDelta(Vector2 position)
    {
        float x = (position.x/Mathf.Max(1f, containerRect.rect.width)) * 2.5f;
        float y = (position.y/Mathf.Max(1f, containerRect.rect.height)) * 2.5f;
        return new Vector2(x, y);
    }

    Vector2 ClampValuesToMagnitude(Vector2 position)
    {
        return Vector2.ClampMagnitude(position, 1);
    }

    Vector2 ApplyInversionFilter(Vector2 position)
    {
        if(invertXOutputValue)
        {
            position.x = InvertValue(position.x);
        }

        if(invertYOutputValue)
        {
            position.y = InvertValue(position.y);
        }

        return position;
    }

    float InvertValue(float value)
    {
        return -value;
    }
    
}
