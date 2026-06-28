using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardUI : MonoBehaviour
{
    public TextMeshProUGUI cardNameText;

    // ---> THÊM BIẾN NÀY <---
    public Image cardImage;

    public TextMeshProUGUI usesText;
    public TextMeshProUGUI cooldownText;
    public Image cooldownOverlay;
    public Button cardButton;

    private int mySlotIndex;

    public void SetupNetworked(NetworkCardInstance netCard, CardData data, int slotIndex, System.Action<int> onClickCallback)
    {
        mySlotIndex = slotIndex;

        if (cardNameText != null) cardNameText.text = data.cardName;

        // ---> THÊM ĐOẠN NÀY ĐỂ ỐP HÌNH VÀO LÁ BÀI <---
        if (cardImage != null && data.cardArtwork != null)
        {
            cardImage.sprite = data.cardArtwork;
            cardImage.color = Color.white; // Reset màu chuẩn
        }

        CardDescriptionTooltipTarget tooltip = GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null) tooltip = gameObject.AddComponent<CardDescriptionTooltipTarget>();
        tooltip.SetCardData(data);

        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() =>
        {
            Debug.Log($"🟨 [CardUI] Đã Click vào thẻ: {data.cardName} (Khe số: {mySlotIndex})");
            onClickCallback?.Invoke(mySlotIndex);
        });

        UpdateUI(netCard, data);
    }

    public void UpdateUI(NetworkCardInstance netCard, CardData data)
    {
        usesText.text = $"Uses: {netCard.remainingUses}/{data.maxUses}";

        if (netCard.remainingUses <= 0)
        {
            cooldownOverlay.gameObject.SetActive(true);
            cooldownText.text = "X";
            cardButton.interactable = false;
        }
        else if (netCard.currentCooldown > 0)
        {
            cooldownOverlay.gameObject.SetActive(true);
            cooldownText.text = netCard.currentCooldown.ToString();
            cardButton.interactable = false;
        }
        else
        {
            cooldownOverlay.gameObject.SetActive(false);
            cooldownText.text = "";
            cardButton.interactable = true;
        }
    }

    public int GetSlotIndex() => mySlotIndex;
}