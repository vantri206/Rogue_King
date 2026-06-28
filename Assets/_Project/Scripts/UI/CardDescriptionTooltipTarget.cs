using UnityEngine;
using UnityEngine.EventSystems;

public class CardDescriptionTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [SerializeField] private CardData cardData;

    private bool hovering;

    public void SetCardData(CardData data)
    {
        cardData = data;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        CardDescriptionPopup.Show(cardData, eventData != null ? eventData.position : Vector2.zero);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!hovering)
            return;

        CardDescriptionPopup.Move(eventData != null ? eventData.position : Vector2.zero);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        CardDescriptionPopup.HideGlobal();
    }

    private void OnDisable()
    {
        if (hovering)
        {
            hovering = false;
            CardDescriptionPopup.HideGlobal();
        }
    }
}
