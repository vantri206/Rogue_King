using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI usesText;
    public TextMeshProUGUI cooldownText;
    public Image cooldownOverlay; // Lớp phủ màu đen mờ khi đang hồi chiêu
    public Button cardButton;

    private CardInstance currentCard;
    private System.Action<CardInstance> onClickCallback;

    // Nạp dữ liệu vào UI
    public void Setup(CardInstance card, System.Action<CardInstance> callback)
    {
        currentCard = card;
        onClickCallback = callback;

        cardNameText.text = card.data.cardName;

        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() => onClickCallback?.Invoke(currentCard));

        UpdateUI();
    }

    // Cập nhật lại giao diện (Được gọi mỗi khi xài bài hoặc qua Turn)
    public void UpdateUI()
    {
        if (currentCard == null) return;

        usesText.text = $"Uses: {currentCard.remainingUses}/{currentCard.data.maxUses}";

        if (currentCard.remainingUses <= 0)
        {
            // Hết lượt xài -> Mờ luôn
            cooldownOverlay.gameObject.SetActive(true);
            cooldownText.text = "X";
            cardButton.interactable = false;
        }
        else if (currentCard.currentCooldown > 0)
        {
            // Đang hồi chiêu -> Hiện số lượt chờ
            cooldownOverlay.gameObject.SetActive(true);
            cooldownText.text = currentCard.currentCooldown.ToString();
            cardButton.interactable = false;
        }
        else
        {
            // Sẵn sàng xài
            cooldownOverlay.gameObject.SetActive(false);
            cooldownText.text = "";
            cardButton.interactable = true;
        }
    }
}