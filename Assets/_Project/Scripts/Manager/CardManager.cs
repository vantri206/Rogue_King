using UnityEngine;

public class CardManager : SingletonMB<CardManager>
{
    [SerializeField] private ChessBoard chessBoard;

    // Offline/local test flow cho card cũ:
    // Chỉ validate điều kiện cơ bản rồi consume card, không apply gameplay effect.
    // Bản online dùng ServerCardManager là chính.
    public void ActivateCard(CardInstance cardInstance, ChessPieceRuntime targetPiece = null)
    {
        if (cardInstance == null || cardInstance.data == null)
            return;

        if (!PlayerInventory.Instance.CanUseCard(cardInstance))
        {
            Debug.LogWarning($"[Card] Thẻ {cardInstance.data.cardName} chưa sẵn sàng (CD: {cardInstance.currentCooldown}, Uses: {cardInstance.remainingUses})");
            return;
        }

        CardData data = cardInstance.data;
        if (!ValidateTargetIfNeeded(data, targetPiece))
            return;

        PlayerInventory.Instance.ConsumeCard(cardInstance);

        if (chessBoard != null)
            chessBoard.ResetAllTileHighlights();

        Debug.Log($"[Card] Used test card '{data.cardName}'. No gameplay effect applied in this test version.");
    }

    private bool ValidateTargetIfNeeded(CardData data, ChessPieceRuntime targetPiece)
    {
        if (string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        if (targetPiece == null || targetPiece.baseData == null)
        {
            Debug.LogWarning($"[Card] Thẻ {data.cardName} cần target có tên chứa '{data.requiredTargetName}', nhưng chưa chọn target.");
            return false;
        }

        if (!targetPiece.baseData.pieceName.Contains(data.requiredTargetName))
        {
            Debug.LogWarning($"[Card] Thẻ {data.cardName} chỉ dùng được lên quân có tên chứa '{data.requiredTargetName}'. Target hiện tại: '{targetPiece.baseData.pieceName}'.");
            return false;
        }

        return true;
    }
}
