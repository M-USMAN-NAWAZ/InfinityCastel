using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class UIVirtualButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [System.Serializable]
    public class BoolEvent : UnityEvent<bool> { }
    [System.Serializable]
    public class Event : UnityEvent { }

    [Header("Output")]
    public BoolEvent buttonStateOutputEvent;
    public Event buttonClickOutputEvent;
    private int? activePointer;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (activePointer.HasValue) return;
        activePointer = eventData.pointerId;
        OutputButtonStateValue(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (activePointer != eventData.pointerId) return;
        activePointer = null;
        OutputButtonStateValue(false);
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        OutputButtonClickEvent();
    }

    void OutputButtonStateValue(bool buttonState)
    {
        buttonStateOutputEvent?.Invoke(buttonState);
    }

    void OutputButtonClickEvent()
    {
        buttonClickOutputEvent?.Invoke();
    }
    public void ResetInput() { activePointer = null; OutputButtonStateValue(false); }
    private void OnDisable() => ResetInput();

}
