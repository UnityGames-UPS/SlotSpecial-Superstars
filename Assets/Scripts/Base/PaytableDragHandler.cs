using UnityEngine;
using UnityEngine.EventSystems;

internal class PaytableDragHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private UIManager uiManager;

    public void OnBeginDrag(PointerEventData eventData) => uiManager.OnDragBegin();
    public void OnDrag(PointerEventData eventData) => uiManager.OnDragDelta(eventData.delta.x);
    public void OnEndDrag(PointerEventData eventData) => uiManager.OnDragEnd();
}
