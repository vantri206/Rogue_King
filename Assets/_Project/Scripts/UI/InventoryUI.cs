using System.Collections.Generic;
using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public PlayerControl playerControl;
    public GameObject cardPrefab;   // Khuôn mẫu của 1 lá bài
    public Transform handContainer; // Nơi chứa các lá bài (Horizontal Layout Group)

    private List<CardUI> spawnedCards = new List<CardUI>();

    private void Start()
    {
        // Delay nhẹ 0.1s để chờ PlayerInventory khởi tạo xong dữ liệu
        Invoke(nameof(InitializeHand), 0.1f);

        // Đăng ký sự kiện: Cứ qua Turn là làm mới giao diện (Cập nhật số Cooldown)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnChanged += (faction) => RefreshAllCards();
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTurnChanged -= (faction) => RefreshAllCards();
        }
    }

    private void InitializeHand()
    {
        if (PlayerInventory.Instance == null) return;

        // Xóa các UI thừa nếu có
        foreach (Transform child in handContainer) Destroy(child.gameObject);
        spawnedCards.Clear();

        // Sinh ra các lá bài tương ứng với dữ liệu Hand
        foreach (var card in PlayerInventory.Instance.handCards)
        {
            GameObject go = Instantiate(cardPrefab, handContainer);
            CardUI cardUI = go.GetComponent<CardUI>();
            cardUI.Setup(card, OnCardClicked);
            spawnedCards.Add(cardUI);
        }
    }

    public void RefreshAllCards()
    {
        foreach (var cardUI in spawnedCards)
        {
            cardUI.UpdateUI();
        }
    }

    // Khi User click vào 1 lá bài trên màn hình
    private void OnCardClicked(CardInstance card)
    {
        ChessPieceRuntime targetPiece = GetSelectedPiece();

        // Nếu thẻ yêu cầu mục tiêu cụ thể mà chưa chọn quân cờ nào
        if (!string.IsNullOrEmpty(card.data.requiredTargetName) && targetPiece == null)
        {
            Debug.LogWarning($"[UI] Vui lòng CLICK CHỌN 1 QUÂN {card.data.requiredTargetName.ToUpper()} TRÊN BÀN trước khi dùng thẻ này!");
            return;
        }

        // Kích hoạt logic trong RAM
        CardManager.Instance.ActivateCard(card, targetPiece);

        // Làm mới lại UI (Trừ số lần dùng, bật bảng Cooldown)
        RefreshAllCards();
    }

    // Hàm lôi cổ quân cờ đang được người chơi Click chọn (Lấy từ PlayerControl)
    private ChessPieceRuntime GetSelectedPiece()
    {
        if (playerControl == null) return null;
        var field = playerControl.GetType().GetField("selectedPiece", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            ChessPiece piece = field.GetValue(playerControl) as ChessPiece;
            if (piece != null) return piece.pieceData;
        }
        return null;
    }
}