using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CombatManager : SingletonMB<CombatManager>
{
    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;

    private int activeProjectiles = 0;

    public void ExecuteAttack(ChessPieceRuntime attacker, WeaponData usedWeapon, Vector2Int lockedTarget)
    {
        if (attacker == null || usedWeapon == null) return;

        Dictionary<Vector2Int, List<CombatEffect>> effectMap = ActionResolver.CalculateWeaponGrid(
            usedWeapon,
            attacker.currentGridPosition,
            lockedTarget,
            chessBoard.boardData
        );

        List<Vector2Int> validTargets = effectMap.Keys.ToList();
        activeProjectiles = validTargets.Count;

        if (activeProjectiles == 0)
        {
            GameManager.Instance.ForceResolveTurn();
            return;
        }

        Vector3 startWorldPos = chessBoard.GetTileAt(attacker.currentGridPosition).transform.position + chessBoard.PiecePlacementOffset;
        CombatVFXManager.Instance.PlayWeaponVFX(usedWeapon, startWorldPos, attacker.currentGridPosition, lockedTarget, validTargets, chessBoard,
            (hitGridPos) => OnProjectileHit(hitGridPos, effectMap)
        );
    }
    private void OnProjectileHit(Vector2Int gridPos, Dictionary<Vector2Int, List<CombatEffect>> effectMap)
    {
        if (effectMap.TryGetValue(gridPos, out List<CombatEffect> effects))
        {
            ChessPieceRuntime targetPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(gridPos.x, gridPos.y);
            if (targetPiece != null)
            {
                foreach (CombatEffect effect in effects)
                {
                    ApplyEffect(targetPiece, effect);
                }
            }
        }

        activeProjectiles--;

        if (activeProjectiles <= 0)
        {
            ResolveDeaths();
            if (GameManager.Instance != null) GameManager.Instance.ForceResolveTurn();
        }
    }

    private void ApplyEffect(ChessPieceRuntime target, CombatEffect effect)
    {
        switch (effect.type)
        {
            case EffectType.Damage:
                if (target.hasShield)
                {
                    target.hasShield = false;
                    Debug.Log($"[CombatManager] {target.baseData.pieceName} ĐÃ CHẶN hoàn toàn sát thương nhờ KHIÊN!");
                    return;
                }

                // ==================================================================
                // >>> CODE CỦA HẬU: CHỈ KÍCH HOẠT KHI ĐÒN ĐÁNH LÀM VUA CHẾT <<<
                // ==================================================================
                if (target.baseData.pieceName.Contains("King"))
                {
                    // Check xem (Máu hiện tại - Sát thương) có từ 0 trở xuống hay không
                    if (target.currentHealth - effect.value <= 0)
                    {
                        ChessPieceRuntime friendlyQueen = FindFriendlyQueen(target.faction);

                        // Nếu tìm thấy Hậu đồng minh còn sống trên bàn cờ
                        if (friendlyQueen != null && friendlyQueen.currentHealth > 0)
                        {
                            Debug.Log($"[Skill Hộ Vệ] Vua trúng đòn KẾT LIỄU! Hậu xuất hiện giải vây.");

                            // 1. Tìm ô trống quanh Vua và dịch chuyển Hậu tới chắn đạn
                            Vector2Int? vacantPos = FindEmptyAdjacentPosition(target.currentGridPosition);
                            if (vacantPos.HasValue)
                            {
                                chessBoard.MovePieceOnBoard(friendlyQueen.currentGridPosition, vacantPos.Value);
                            }

                            // 2. Chuyển toàn bộ sát thương sang thanh máu của Hậu
                            friendlyQueen.currentHealth -= effect.value;
                            Debug.Log($"[Skill Hộ Vệ] Hậu gánh thay đòn chí mạng! HP Hậu còn: {friendlyQueen.currentHealth}. Vua an toàn giữ nguyên {target.currentHealth} HP.");

                            return; // Ngắt hàm luôn để Vua không bị trừ giọt máu nào!
                        }
                    }
                }
                // ==================================================================

                target.currentHealth -= effect.value;
                Debug.Log($"[CombatManager] {target.baseData.pieceName} took {effect.value} damage. HP remaining: {target.currentHealth}");
                break;

            case EffectType.Heal:
                target.currentHealth += effect.value;
                if (target.currentHealth > target.baseData.baseHealth)
                    target.currentHealth = target.baseData.baseHealth;
                Debug.Log($"[CombatManager] {target.baseData.pieceName} healed {effect.value} HP.");
                break;
        }
    }
    // Hàm tìm quân Hậu cùng phe đang còn sống trên bàn cờ
    private ChessPieceRuntime FindFriendlyQueen(ChessFaction faction)
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                ChessPieceRuntime piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                // Kiểm tra quân cờ trùng phe và tên có chứa chữ "Queen"
                if (piece != null && piece.faction == faction && piece.baseData.pieceName.Contains("Queen"))
                {
                    return piece;
                }
            }
        }
        return null;
    }

    // Hàm quét 8 ô xung quanh Vua để tìm vị trí trống cho Hậu nhảy vào
    private Vector2Int? FindEmptyAdjacentPosition(Vector2Int centerPos)
    {
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(0, 1),   // Trên
            new Vector2Int(0, -1),  // Dưới
            new Vector2Int(1, 0),   // Phải
            new Vector2Int(-1, 0),  // Trái
            new Vector2Int(1, 1),   // Chéo trên phải
            new Vector2Int(1, -1),  // Chéo dưới phải
            new Vector2Int(-1, 1),  // Chéo trên trái
            new Vector2Int(-1, -1)  // Chéo dưới trái
        };

        foreach (var dir in directions)
        {
            Vector2Int checkPos = centerPos + dir;
            // Nếu ô nằm trong bàn cờ và trống không có ai đứng chiếm chỗ
            if (chessBoard.boardData.IsValidPosition(checkPos.x, checkPos.y) &&
                chessBoard.boardData.IsTileEmptyForMovement(checkPos.x, checkPos.y))
            {
                return checkPos;
            }
        }
        return null; // Trường hợp hiếm: Không có ô nào trống quanh Vua
    }
    private void ResolveDeaths()
    {
        List<ChessPieceRuntime> deadPieces = new List<ChessPieceRuntime>();
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                ChessPieceRuntime piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.currentHealth <= 0)
                {
                    deadPieces.Add(piece);
                }
            }
        }

        foreach (var deadPiece in deadPieces)
        {
            Vector2Int pos = deadPiece.currentGridPosition;

            // >>> LOGIC SKILL 5: THU PHỤC QUÂN ĐỊCH <<<
            // Nếu quân bị chết thuộc phe địch VÀ hành động kết liễu diễn ra trong Turn của Rogue
            if (deadPiece.faction == ChessFaction.ChessAlliance && GameManager.Instance.currentTurnFaction == ChessFaction.ChessRogue)
            {
                Debug.Log($"[Skill Thu Phục] Kẻ địch {deadPiece.baseData.pieceName} đã bị thu phục làm lính che chắn!");

                // 1. Đổi phe dữ liệu trên RAM
                deadPiece.faction = ChessFaction.ChessRogue;
                deadPiece.currentHealth = Mathf.RoundToInt(deadPiece.baseData.baseHealth * 0.4f); // Sống lại với 40% HP
                if (deadPiece.currentHealth <= 0) deadPiece.currentHealth = 10;
                deadPiece.isCharmedAlly = true;

                // 2. Thay đổi màu sắc của View để nhận diện lính ma quỷ/bị điều khiển
                BoardTile tile = chessBoard.GetTileAt(pos);
                if (tile != null && tile.currentPiece != null)
                {
                    var sRenderer = tile.currentPiece.GetComponentInChildren<SpriteRenderer>();
                    if (sRenderer != null) sRenderer.color = new Color(0.4f, 0.4f, 1f, 1f); // Nhuộm xanh tím làm tay sai
                }

                continue; // Bỏ qua lệnh xóa bên dưới, cứu quân cờ này sống tiếp!
            }

            // --- NẾU KHÔNG THU PHỤC THÌ CHẾT NHƯ BÌNH THƯỜNG ---
            Debug.Log($"[CombatManager] {deadPiece.baseData.pieceName} defeated!");

            GameManager.Instance.graveyard.Add(new DeadPieceRecord
            {
                pieceData = deadPiece.baseData,
                faction = deadPiece.faction,
                deathPos = pos
            });

            chessBoard.boardData.RemoveEntity(deadPiece);
            BoardTile currentTile = chessBoard.GetTileAt(pos);
            if (currentTile != null && currentTile.currentPiece != null)
            {
                Destroy(currentTile.currentPiece.gameObject);
                currentTile.ClearPiece();
            }

            if (deadPiece.baseData.pieceName.Contains("King"))
            {
                GameManager.Instance.OnKingDefeated();
            }
        }
    }
}