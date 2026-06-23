using System.Collections.Generic;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    public GameObject cardPrefab;
    public Transform handContainer;

    private List<CardUI> spawnedCards = new List<CardUI>();

    private void Start()
    {
        InvokeRepeating(nameof(TryInitializeHand), 0.5f, 0.5f); // Check liên tục đến khi Player Spawn xong
    }

    private void TryInitializeHand()
    {
        if (PlayerNetworkController.Local != null && ServerCardManager.Instance != null)
        {
            if (PlayerNetworkController.Local.HandCards[0].isInitialized)
            {
                CancelInvoke(nameof(TryInitializeHand));
                InitializeHand();
            }
        }
    }

    private void InitializeHand()
    {
        foreach (Transform child in handContainer) Destroy(child.gameObject);
        spawnedCards.Clear();

        for (int i = 0; i < PlayerNetworkController.Local.HandCards.Length; i++)
        {
            var netCard = PlayerNetworkController.Local.HandCards[i];
            if (netCard.isInitialized)
            {
                CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);
                GameObject go = Instantiate(cardPrefab, handContainer);
                CardUI cardUI = go.GetComponent<CardUI>();

                cardUI.SetupNetworked(netCard, data, i, OnCardClicked);
                spawnedCards.Add(cardUI);
            }
        }
    }

    public void RefreshAllCards()
    {
        if (PlayerNetworkController.Local == null || ServerCardManager.Instance == null || spawnedCards.Count == 0) return;

        for (int i = 0; i < spawnedCards.Count; i++)
        {
            var netCard = PlayerNetworkController.Local.HandCards[i];
            CardData data = ServerCardManager.Instance.GetCardData(netCard.cardDataIndex);
            spawnedCards[i].UpdateUI(netCard, data);
        }
    }

    private void OnCardClicked(int slotIndex)
    {
        // Lấy tọa độ mục tiêu từ quân cờ đang click sáng trên bàn
        Vector2Int targetGridPos = PlayerNetworkController.Local.GetSelectedPieceGridPos();

        // Gửi RPC báo Server chơi thẻ ở slot này, nhắm vào tọa độ này
        PlayerNetworkController.Local.Rpc_RequestPlayCard(slotIndex, targetGridPos);
    }
}