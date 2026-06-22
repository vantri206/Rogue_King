using System.Collections.Generic;
using UnityEngine;

public enum CardType
{
    SieuBuff,
    ButToc,
    HanhQuan,
    TotAnThang,
    DanhUp
}

public class CardManager : SingletonMB<CardManager>
{
    [ContextMenu("TEST: Kích hoạt Tốt Ăn Thẳng")]
    public void TestTotAnThang() => ActivateCard(CardType.TotAnThang);

    [ContextMenu("TEST: Kích hoạt Bứt Tốc")]
    public void TestButToc() => ActivateCard(CardType.ButToc);
    [Header("References")]
    [SerializeField] private ChessBoard chessBoard;
    [SerializeField] private PlayerControl playerControl;

    // Bộ đếm số lần xài chiêu Đánh Úp (Giới hạn 2 lượt/trận)
    private int recallUsesLeft = 2;

    // HÀM KÍCH HOẠT HIỆU ỨNG THẺ BÀI
    public void ActivateCard(CardType card, ChessPieceRuntime targetPiece = null)
    {
        ChessFaction myFaction = ChessFaction.ChessRogue; // Mặc định áp dụng cho phe người chơi điều khiển

        switch (card)
        {
            // 1. SIÊU BUFF: Không tốn lượt, tăng mạnh chỉ số thực thể được chọn
            case CardType.SieuBuff:
                if (targetPiece != null)
                {
                    targetPiece.isSuperBuffed = true;
                    targetPiece.currentAttack += 50;                     // Tăng Dame
                    targetPiece.currentHealth += 150;                    // Tăng HP hiện tại
                    targetPiece.baseData.baseHealth += 150;              // Tăng HP tối đa
                    Debug.Log($"[Card] Siêu Buff lên {targetPiece.baseData.pieceName}! ATK +50, HP +150.");
                }
                break;

            // 2. BỨT TỐC: Không tốn lượt, hồi lại quyền di chuyển ngay lập tức
            case CardType.ButToc:
                // Truy cập vào PlayerControl thông qua Reflection để gán lại quyền di chuyển không tốn lượt
                var fieldMoved = playerControl.GetType().GetField("hasMovedThisTurn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fieldMoved != null)
                {
                    fieldMoved.SetValue(playerControl, false); // Trả quyền đi về false để đi tiếp
                    Debug.Log("[Card] Kích hoạt Bứt Tốc! Bạn được tặng thêm 1 lượt hành động tự do.");
                }
                break;

            // 3. HÀNH QUÂN: Buff máu tối đa và tăng tầm chạy (+2 ô) cho toàn bộ lính Tốt phe ta
            case CardType.HanhQuan:
                ForEachFriendlyPiece(myFaction, (piece) => {
                    if (piece.baseData.pieceName.Contains("Pawn"))
                    {
                        piece.currentMoveRange += 2; // Tăng tầm di chuyển hành quân
                        piece.currentHealth += 100;   // Tăng máu cường hóa đoàn quân
                        piece.baseData.baseHealth += 100;
                    }
                });
                Debug.Log("[Card] Kích hoạt Hành Quân! Toàn bộ quân Tốt tăng tốc và tăng HP.");
                break;

            // 4. TỐT ĂN THẲNG: Mở khóa khả năng tấn công trực diện cho toàn bộ quân Tốt phe ta
            case CardType.TotAnThang:
                ForEachFriendlyPiece(myFaction, (piece) => {
                    if (piece.baseData.pieceName.Contains("Pawn"))
                    {
                        piece.canAttackStraight = true;
                    }
                });
                Debug.Log("[Card] Kích hoạt Tốt Ăn Thẳng! Đoàn quân Tốt có thể ăn quân phía trước mặt.");
                break;

            // 5. ĐÁNH ÚP (RECALL): Giật ngược thời gian 1 quân cờ về vị trí ở lượt trước (Tối đa 2 lần/trận)
            case CardType.DanhUp:
                if (recallUsesLeft <= 0)
                {
                    Debug.LogWarning("[Card] Chiêu Đánh Úp đã hết số lần sử dụng trong trận này!");
                    return;
                }
                if (targetPiece != null)
                {
                    Vector2Int oldPos = targetPiece.currentGridPosition;
                    Vector2Int targetPos = targetPiece.previousGridPosition;

                    // Kiểm tra xem ô lịch sử đó hiện tại có đang bị ai chiếm chỗ không
                    if (chessBoard.boardData.IsTileEmptyForMovement(targetPos.x, targetPos.y))
                    {
                        chessBoard.MovePieceOnBoard(oldPos, targetPos); // Bứng quân cờ về quá khứ
                        recallUsesLeft--;
                        Debug.Log($"[Card] Đánh Úp thành công! Giật ngược {targetPiece.baseData.pieceName} về {targetPos}. Còn lại: {recallUsesLeft} lần.");
                    }
                    else
                    {
                        Debug.LogWarning("[Card] Không thể Đánh Úp vì ô cờ quá khứ đang bị vật cản chiếm chỗ!");
                    }
                }
                break;
        }

        // Làm mới lại toàn bộ các ô hiển thị màu trên bàn cờ để cập nhật theo luật mới
        chessBoard.ResetAllTileHighlights();
    }

    // Hàm bổ trợ duyệt nhanh toàn bộ quân cờ cùng phe trên bàn cờ RAM
    private void ForEachFriendlyPiece(ChessFaction faction, System.Action<ChessPieceRuntime> action)
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                var piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.faction == faction)
                {
                    action?.Invoke(piece);
                }
            }
        }
    }
}