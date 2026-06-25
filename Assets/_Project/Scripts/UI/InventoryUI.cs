using System.Collections.Generic;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public GameObject cardPrefab;
    public Transform handContainer;

    private List<CardUI> spawnedCards = new List<CardUI>();

    private void Start()
    {
        InvokeRepeating(nameof(TryInitializeHand), 0.5f, 0.5f);
    }

    private void TryInitializeHand()
    {
        if (PlayerNetworkController.Local != null && ServerCardManager.Instance != null)
        {
            bool hasCard = false;
            for (int i = 0; i < PlayerNetworkController.Local.HandCards.Length; i++)
            {
                if (PlayerNetworkController.Local.HandCards[i].isInitialized) hasCard = true;
            }

            if (hasCard)
            {
                Debug.Log("🟩 [InventoryUI] Đã nhận được dữ liệu bài từ Server. Bắt đầu sinh UI...");
                CancelInvoke(nameof(TryInitializeHand));
                InitializeHand();
            }
        }
    }

    private void InitializeHand()
    {
        foreach (Transform child in handContainer) Destroy(child.gameObject);
        spawnedCards.Clear();

        int count = 0;
        for (int i = 0; i < PlayerNetworkController.Local.HandCards.Length; i++)
        {
            var netCard = PlayerNetworkController.Local.HandCards[i];
            if (netCard.isInitialized)
            {
                CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);
                if (data != null)
                {
                    GameObject go = Instantiate(cardPrefab, handContainer);
                    CardUI cardUI = go.GetComponent<CardUI>();

                    cardUI.SetupNetworked(netCard, data, i, OnCardClicked);
                    spawnedCards.Add(cardUI);
                    count++;
                    Debug.Log($"🟩 [InventoryUI] Đã sinh lá bài: {data.cardName} (Số lần dùng: {netCard.remainingUses})");
                }
            }
        }
    }

    public void RefreshAllCards()
    {
        if (PlayerNetworkController.Local == null || ServerCardManager.Instance == null || spawnedCards.Count == 0) return;

        foreach (var cardUI in spawnedCards)
        {
            int slot = cardUI.GetSlotIndex(); // Lấy đúng thẻ của mình để cập nhật, tránh lệch pha
            var netCard = PlayerNetworkController.Local.HandCards[slot];
            CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);

            if (data != null) cardUI.UpdateUI(netCard, data);
        }
    }

    private void OnCardClicked(int slotIndex)
    {
        var netCard = PlayerNetworkController.Local.HandCards[slotIndex];
        CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);

        if (data != null)
        {
            PlayerNetworkController.Local.StartAimingCard(slotIndex, data);
        }
    }
}