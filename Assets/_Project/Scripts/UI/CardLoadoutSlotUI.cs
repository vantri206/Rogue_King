using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CardLoadoutSlotUI : MonoBehaviour, IDropHandler, IPointerClickHandler
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image artworkImage;
    [SerializeField] private TextMeshProUGUI indexText;

    private PreMatchCardSelectionUI owner;
    private CardRoleType role;
    private int slotIndex;
    private CardData cardData;
    private int cardIndex = -1;

    public int CardIndex => cardIndex;
    public CardRoleType Role => role;
    public int SlotIndex => slotIndex;

    public void Setup(PreMatchCardSelectionUI newOwner, CardRoleType cardRole, int newSlotIndex)
    {
        owner = newOwner;
        role = cardRole;
        slotIndex = newSlotIndex;
        Clear();
    }

    public void SetCard(CardData data, int index)
    {
        cardData = data;
        cardIndex = index;

        if (nameText != null)
            nameText.text = data != null ? data.cardName : "Empty";

        if (artworkImage != null)
        {
            artworkImage.sprite = data != null ? data.cardArtwork : null;
            artworkImage.color = data != null && data.cardArtwork != null ? Color.white : new Color(1f, 1f, 1f, 0.18f);
        }

        if (indexText != null)
            indexText.text = (slotIndex + 1).ToString();

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(cardData);
    }

    public void Clear()
    {
        cardData = null;
        cardIndex = -1;

        if (nameText != null)
            nameText.text = "Empty";

        if (artworkImage != null)
        {
            artworkImage.sprite = null;
            artworkImage.color = new Color(1f, 1f, 1f, 0.18f);
        }

        if (indexText != null)
            indexText.text = (slotIndex + 1).ToString();

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(cardData);
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerDrag == null || owner == null)
            return;

        CardLoadoutOptionUI option = eventData.pointerDrag.GetComponent<CardLoadoutOptionUI>();
        if (option == null)
            option = eventData.pointerDrag.GetComponentInParent<CardLoadoutOptionUI>();

        if (option == null)
            return;

        owner.TrySetSlot(role, slotIndex, option.CardData, option.CardIndex);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.ClearSlot(role, slotIndex);
    }
}
