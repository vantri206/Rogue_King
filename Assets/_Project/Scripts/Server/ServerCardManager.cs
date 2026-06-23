using Fusion;
using UnityEngine;
using System.Collections.Generic;

public class ServerCardManager : NetworkBehaviour
{
    public static ServerCardManager Instance { get; private set; }

    [Header("Global Card Database")]
    [Tooltip("Kéo thả TOÀN BỘ CardData có trong game vào đây để Server làm từ điển đối chiếu")]
    [SerializeField] private List<CardData> availableCards;

    public override void Spawned()
    {
        if (Instance == null) Instance = this;
        else Runner.Despawn(Object);
    }

    public CardData GetCardData(int index)
    {
        if (availableCards == null || index < 0 || index >= availableCards.Count) return null;
        return availableCards[index];
    }

    public int GetCardIndex(CardData data)
    {
        if (availableCards == null || data == null) return -1;
        return availableCards.IndexOf(data);
    }

    // Hàm Phán xử khi Client gửi RPC yêu cầu dùng bài
    public bool ProcessCardRequest(PlayerRef player, PlayerNetworkController controller, int handIndex, Vector2Int targetPos)
    {
        if (!HasStateAuthority) return false;

        NetworkCardInstance cardInstance = controller.HandCards[handIndex];
        if (!cardInstance.isInitialized) return false;
        if (cardInstance.currentCooldown > 0 || cardInstance.remainingUses <= 0) return false;

        CardData data = GetCardData(cardInstance.cardDataIndex);
        if (data == null) return false;

        ChessPieceRuntime targetRuntime = null;
        NetworkChessPiece targetNetPiece = null;

        // Nếu client có gửi kèm tọa độ mục tiêu (Dùng cho Siêu Buff, Đánh Úp...)
        if (targetPos.x >= 0 && targetPos.y >= 0)
        {
            targetRuntime = ServerBoardManager.Instance.GetRuntimeAt(targetPos);
            targetNetPiece = ServerBoardManager.Instance.GetPieceAt(targetPos);

            // Xác thực xem mục tiêu có đúng tên yêu cầu không
            if (targetRuntime != null && !string.IsNullOrEmpty(data.requiredTargetName))
            {
                if (!targetRuntime.baseData.pieceName.Contains(data.requiredTargetName))
                    return false;
            }
        }

        ChessFaction myFaction = ServerGameManager.Instance.IsKingPlayer(player) ? ChessFaction.ChessRogue : ChessFaction.ChessAlliance;
        bool isSuccess = true;

        // --- BẮT ĐẦU THỰC THI HIỆU ỨNG TRÊN SERVER ---
        switch (data.effectType)
        {
            case CardEffectType.SuperBuff:
                if (targetRuntime != null && targetNetPiece != null)
                {
                    targetRuntime.currentAttack += data.effectValue1;
                    targetRuntime.baseData.baseHealth += data.effectValue2;
                    targetRuntime.currentHealth += data.effectValue2;
                    targetNetPiece.currentHp = targetRuntime.currentHealth; // Đồng bộ UI cho Client
                }
                else isSuccess = false;
                break;

            case CardEffectType.ExtraTurn:
                controller.hasExtraTurn = true; // Buff trực tiếp vào controller của người chơi đó
                break;

            case CardEffectType.March:
                ForEachFriendlyPiece(myFaction, "Pawn", (runtime, netPiece) => {
                    runtime.currentMoveRange += data.effectValue1;
                    runtime.baseData.baseHealth += data.effectValue2;
                    runtime.currentHealth += data.effectValue2;
                    netPiece.currentHp = runtime.currentHealth;
                });
                break;

            case CardEffectType.PawnForwardAttack:
                ForEachFriendlyPiece(myFaction, "Pawn", (runtime, netPiece) => {
                    runtime.canAttackStraight = true;
                });
                break;

            case CardEffectType.Recall:
                if (targetRuntime != null)
                {
                    Vector2Int oldPos = targetRuntime.previousGridPosition;
                    if (ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(oldPos.x, oldPos.y))
                    {
                        ServerBoardManager.Instance.MovePiece(targetRuntime.currentGridPosition, oldPos);
                    }
                    else isSuccess = false; // Bị kẹt vật cản
                }
                else isSuccess = false;
                break;

                // (Bạn có thể bổ sung lại các Case BishopSilence, PawnShield... tương tự)
        }

        // Nếu xài thành công, trừ số lần dùng và bắt đầu tính Cooldown
        if (isSuccess)
        {
            cardInstance.remainingUses--;
            cardInstance.currentCooldown = data.baseCooldown;
            controller.HandCards.Set(handIndex, cardInstance); // Lưu ngược lại vào mảng Network
            Debug.Log($"[Server Card] Player {player} xài thẻ {data.cardName} thành công!");
        }

        return isSuccess;
    }

    private void ForEachFriendlyPiece(ChessFaction faction, string requiredNamePart, System.Action<ChessPieceRuntime, NetworkChessPiece> action)
    {
        var board = ServerBoardManager.Instance.logicBoard;
        for (int x = 0; x < board.width; x++)
        {
            for (int y = 0; y < board.height; y++)
            {
                var runtime = board.GetEntityAt<ChessPieceRuntime>(x, y);
                if (runtime != null && runtime.faction == faction && (string.IsNullOrEmpty(requiredNamePart) || runtime.baseData.pieceName.Contains(requiredNamePart)))
                {
                    var netPiece = ServerBoardManager.Instance.GetPieceAt(new Vector2Int(x, y));
                    action?.Invoke(runtime, netPiece);
                }
            }
        }
    }
}