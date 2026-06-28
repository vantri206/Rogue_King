using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CardLoadoutOptionUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image artworkImage;
    [SerializeField] private CanvasGroup canvasGroup;

    private PreMatchCardSelectionUI owner;
    private CardData cardData;
    private int cardIndex = -1;
    private CardRoleType role;
    private Vector3 startPosition;
    private Transform startParent;

    public CardData CardData => cardData;
    public int CardIndex => cardIndex;
    public CardRoleType Role => role;

    public void Setup(PreMatchCardSelectionUI newOwner, CardData data, int index, CardRoleType cardRole)
    {
        owner = newOwner;
        cardData = data;
        cardIndex = index;
        role = cardRole;

        if (nameText != null)
            nameText.text = data != null ? data.cardName : "Empty";

        if (artworkImage != null)
        {
            artworkImage.sprite = data != null ? data.cardArtwork : null;
            artworkImage.color = data != null && data.cardArtwork != null ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(cardData);
    }

    public void SetAvailable(bool available)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(cardData);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = available ? 1f : 0.35f;
            canvasGroup.blocksRaycasts = available;
            canvasGroup.interactable = available;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.TryAddCardToFirstEmptySlot(role, cardData, cardIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (owner == null || !owner.CanPickCard(cardIndex))
            return;

        startPosition = transform.position;
        startParent = transform.parent;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(cardData);

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = false;

        transform.SetParent(owner.DragRoot != null ? owner.DragRoot : owner.transform, true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        transform.SetParent(startParent, true);
        transform.position = startPosition;
    }
}
