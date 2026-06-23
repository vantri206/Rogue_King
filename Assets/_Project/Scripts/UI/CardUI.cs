using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardUI : MonoBehaviour
{
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI usesText;
    public TextMeshProUGUI cooldownText;
    public Image cooldownOverlay;
    public Button cardButton;

    private int mySlotIndex;

    public void SetupNetworked(NetworkCardInstance netCard, CardData data, int slotIndex, System.Action<int> onClickCallback)
    {
        mySlotIndex = slotIndex;
        cardNameText.text = data.cardName;

        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() => onClickCallback?.Invoke(mySlotIndex));

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
}