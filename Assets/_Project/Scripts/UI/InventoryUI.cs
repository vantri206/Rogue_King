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
        ClearSpawnedCards();

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
        if (PlayerNetworkController.Local == null || ServerCardManager.Instance == null) return;

        int initializedCardCount = CountInitializedHandCards();
        if (initializedCardCount <= 0)
        {
            ClearSpawnedCards();
            return;
        }

        // Khi đổi phase, server rebuild hand theo role mới. Nếu số lượng card thay đổi,
        // hoặc UI chưa từng sinh card, rebuild lại toàn bộ panel để không giữ card cũ.
        if (spawnedCards.Count != initializedCardCount)
        {
            InitializeHand();
            return;
        }

        foreach (var cardUI in spawnedCards)
        {
            if (cardUI == null) continue;

            int slot = cardUI.GetSlotIndex(); // Lấy đúng thẻ của mình để cập nhật, tránh lệch pha
            if (slot < 0 || slot >= PlayerNetworkController.Local.HandCards.Length) continue;

            var netCard = PlayerNetworkController.Local.HandCards[slot];
            if (!netCard.isInitialized) continue;

            CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);
            if (data != null) cardUI.UpdateUI(netCard, data);
        }
    }

    private int CountInitializedHandCards()
    {
        if (PlayerNetworkController.Local == null)
            return 0;

        int count = 0;
        for (int i = 0; i < PlayerNetworkController.Local.HandCards.Length; i++)
        {
            if (PlayerNetworkController.Local.HandCards[i].isInitialized)
                count++;
        }

        return count;
    }

    private void ClearSpawnedCards()
    {
        if (handContainer != null)
        {
            foreach (Transform child in handContainer)
                Destroy(child.gameObject);
        }

        spawnedCards.Clear();
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
    public void ToggleHandPanel()
    {
        if (handContainer != null)
        {
            bool currentState = handContainer.gameObject.activeSelf;

            handContainer.gameObject.SetActive(!currentState);
        }
    }
}