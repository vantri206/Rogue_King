using System.Collections.Generic;
using UnityEngine;

// Class này lưu trữ trạng thái của 1 thẻ bài trong trận (Số lần dùng còn lại, thời gian hồi chiêu)
[System.Serializable]
public class CardInstance
{
    public CardData data;
    public int currentCooldown;
    public int remainingUses;

    public CardInstance(CardData cardData)
    {
        data = cardData;
        currentCooldown = 0;
        remainingUses = cardData.maxUses;
    }
}

// Class này là Hành trang, quản lý bộ bài của người chơi
public class PlayerInventory : SingletonMB<PlayerInventory>
{
    [Header("Starting Deck (Thẻ mang theo vào trận)")]
    public List<CardData> startingDeck;

    [Header("Hand Cards (Dữ liệu chạy realtime)")]
    public List<CardInstance> handCards = new List<CardInstance>();

    private void Start()
    {
        // Khi trận đấu bắt đầu, nạp các thẻ từ Deck vào Hand
        if (startingDeck != null)
        {
            foreach (var card in startingDeck)
            {
                handCards.Add(new CardInstance(card));
            }
        }

        // Đăng ký sự kiện chuyển lượt để giảm Cooldown
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnChanged += DecreaseCooldowns;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnChanged -= DecreaseCooldowns;
        }
    }

    // Tự động giảm Cooldown thẻ bài mỗi khi tới Turn của phe bạn
    private void DecreaseCooldowns(ChessFaction nextTurnFaction)
    {
        if (nextTurnFaction == ChessFaction.ChessRogue)
        {
            foreach (var card in handCards)
            {
                if (card.currentCooldown > 0) card.currentCooldown--;
            }
        }
    }

    // Kiểm tra xem thẻ có đủ điều kiện xài không
    public bool CanUseCard(CardInstance card)
    {
        if (card.remainingUses <= 0) return false;
        if (card.currentCooldown > 0) return false;
        return true;
    }

    // Ghi nhận thẻ đã được xài (Trừ lượt, bắt đầu đếm hồi chiêu)
    public void ConsumeCard(CardInstance card)
    {
        card.remainingUses--;
        card.currentCooldown = card.data.baseCooldown;
    }
}