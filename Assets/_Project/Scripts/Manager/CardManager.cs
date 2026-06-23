using UnityEngine;

public class CardManager : SingletonMB<CardManager>
{
    [SerializeField] private ChessBoard chessBoard;
    [SerializeField] private PlayerControl playerControl;

    public void ActivateCard(CardInstance cardInstance, ChessPieceRuntime targetPiece = null)
    {
        if (!PlayerInventory.Instance.CanUseCard(cardInstance))
        {
            Debug.LogWarning($"[Card] Thẻ {cardInstance.data.cardName} chưa sẵn sàng (CD: {cardInstance.currentCooldown}, Uses: {cardInstance.remainingUses})");
            return;
        }

        CardData data = cardInstance.data;
        ChessFaction myFaction = ChessFaction.ChessRogue;

        // Kiểm tra điều kiện mục tiêu bằng String
        if (targetPiece != null && !string.IsNullOrEmpty(data.requiredTargetName))
        {
            if (!targetPiece.baseData.pieceName.Contains(data.requiredTargetName))
            {
                Debug.LogWarning($"[Card] Thẻ này chỉ được dùng lên quân cờ có tên chứa chữ '{data.requiredTargetName}'!");
                return;
            }
        }

        bool isCardSuccessfullyPlayed = true;

        switch (data.effectType)
        {
            case CardEffectType.PawnShield:
                if (targetPiece != null) targetPiece.hasShield = true;
                break;

            case CardEffectType.BishopSilence:
                ChessPieceRuntime enemyKing = FindEnemyKing(myFaction);
                if (enemyKing != null) enemyKing.silencedTurnsLeft = data.effectValue1 > 0 ? data.effectValue1 : 1;
                break;

            case CardEffectType.KingRevive:
                isCardSuccessfullyPlayed = ActivateKingRevive(myFaction);
                break;

            case CardEffectType.KingDash:
                int dashRange = data.effectValue1 > 0 ? data.effectValue1 : 3;
                isCardSuccessfullyPlayed = TriggerKingMovementSkill(targetPiece, dashRange, isSweep: false);
                break;

            case CardEffectType.KingSweep:
                isCardSuccessfullyPlayed = TriggerKingMovementSkill(targetPiece, 99, isSweep: true);
                break;

            case CardEffectType.SuperBuff:
                if (targetPiece != null)
                {
                    targetPiece.currentAttack += data.effectValue1;
                    targetPiece.baseData.baseHealth += data.effectValue2;
                    targetPiece.currentHealth += data.effectValue2;
                    Debug.Log($"[Card] Super Buff! ATK +{data.effectValue1}, HP +{data.effectValue2}.");
                }
                break;

            case CardEffectType.ExtraTurn:
                playerControl.hasExtraTurn = true;
                break;

            case CardEffectType.March:
                ForEachFriendlyPiece(myFaction, "Pawn", (piece) => {
                    piece.currentMoveRange += data.effectValue1;
                    piece.baseData.baseHealth += data.effectValue2;
                    piece.currentHealth += data.effectValue2;
                });
                break;

            case CardEffectType.PawnForwardAttack:
                ForEachFriendlyPiece(myFaction, "Pawn", (piece) => {
                    piece.canAttackStraight = true;
                });
                break;

            case CardEffectType.Recall:
                if (targetPiece != null)
                {
                    Vector2Int targetPos = targetPiece.previousGridPosition;
                    if (chessBoard.boardData.IsTileEmptyForMovement(targetPos.x, targetPos.y))
                        chessBoard.MovePieceOnBoard(targetPiece.currentGridPosition, targetPos);
                    else
                        isCardSuccessfullyPlayed = false;
                }
                break;
        }

        if (isCardSuccessfullyPlayed)
        {
            PlayerInventory.Instance.ConsumeCard(cardInstance);
            chessBoard.ResetAllTileHighlights();
        }
    }

    // --- CÁC HÀM BỔ TRỢ ---
    private bool ActivateKingRevive(ChessFaction myFaction)
    {
        var graveyard = GameManager.Instance.graveyard;
        for (int i = graveyard.Count - 1; i >= 0; i--)
        {
            if (graveyard[i].faction == myFaction && chessBoard.boardData.IsTileEmptyForMovement(graveyard[i].deathPos.x, graveyard[i].deathPos.y))
            {
                ChessPiece tempPrefab = playerControl.GetType().GetField("selectedPiece", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(playerControl) as ChessPiece;
                chessBoard.SpawnPiece(graveyard[i].pieceData, tempPrefab, graveyard[i].deathPos, myFaction);
                graveyard.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    private bool TriggerKingMovementSkill(ChessPieceRuntime kingData, int range, bool isSweep)
    {
        if (kingData == null || !kingData.baseData.pieceName.Contains("King")) return false;
        Debug.Log($"[Card] Wait for player to aim... (Range: {range}, IsSweep: {isSweep})");
        return true;
    }

    private ChessPieceRuntime FindEnemyKing(ChessFaction myFaction)
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                var piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.faction != myFaction && piece.baseData.pieceName.Contains("King"))
                {
                    return piece;
                }
            }
        }
        return null;
    }

    private void ForEachFriendlyPiece(ChessFaction faction, string requiredNamePart, System.Action<ChessPieceRuntime> action)
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                var piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.faction == faction && (string.IsNullOrEmpty(requiredNamePart) || piece.baseData.pieceName.Contains(requiredNamePart)))
                {
                    action?.Invoke(piece);
                }
            }
        }
    }
}