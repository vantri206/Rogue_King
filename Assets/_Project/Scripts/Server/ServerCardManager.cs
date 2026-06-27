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
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Runner.Despawn(Object);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }

    public CardData GetCardData(int index)
    {
        if (availableCards == null || index < 0 || index >= availableCards.Count)
            return null;

        return availableCards[index];
    }

    public int GetCardIndex(CardData data)
    {
        if (availableCards == null || data == null)
            return -1;

        return availableCards.IndexOf(data);
    }

    // Server-side card flow:
    // Client bấm card -> server validate lượt/card/target -> apply gameplay effect -> trừ Uses/Cooldown -> sync lại UI.
    public bool ProcessCardRequest(PlayerRef player, PlayerNetworkController controller, int handIndex, Vector2Int targetPos)
    {
        if (!HasStateAuthority || controller == null) return false;

        if (handIndex < 0 || handIndex >= controller.HandCards.Length) return false;

        NetworkCardInstance cardInstance = controller.HandCards[handIndex];
        if (!cardInstance.isInitialized || cardInstance.currentCooldown > 0 || cardInstance.remainingUses <= 0) return false;

        CardData data = GetCardData(cardInstance.cardDataIndex);
        if (data == null) return false;

        if (!ValidateTargetIfNeeded(data, targetPos)) return false;

        // --- BẮT ĐẦU THỰC THI HIỆU ỨNG TRÊN SERVER ---
        ChessFaction myFaction = ServerGameManager.Instance.IsKingPlayer(player) ? ChessFaction.ChessRogue : ChessFaction.ChessAlliance;
        bool isSuccess = true;

        ChessPieceRuntime targetRuntime = null;
        NetworkChessPiece targetNetPiece = null;

        if (targetPos.x >= 0 && targetPos.y >= 0 && ServerBoardManager.Instance != null)
        {
            targetRuntime = ServerBoardManager.Instance.GetRuntimeAt(targetPos);
            targetNetPiece = ServerBoardManager.Instance.GetPieceAt(targetPos);
        }

        switch (data.effectType)
        {
            case CardEffectType.SuperBuff:
                if (targetRuntime != null && targetNetPiece != null && targetRuntime.faction == myFaction)
                {
                    // IMPORTANT:
                    // Do NOT modify targetRuntime.baseData/base ScriptableObject here.
                    // baseData is shared by all spawned pieces and future phase setup, so changing it
                    // makes a temporary card buff leak into Phase 2 / later matches.
                    targetRuntime.currentAttack += data.effectValue1;
                    targetRuntime.currentHealth += data.effectValue2;
                    targetRuntime.isSuperBuffed = true;
                    targetNetPiece.currentHp = targetRuntime.currentHealth; // Sync HP UI for clients.
                }
                else isSuccess = false;
                break;

            case CardEffectType.ExtraTurn:
                controller.hasExtraTurn = true;
                break;

            case CardEffectType.March:
                ForEachFriendlyPiece(myFaction, "Pawn", (runtime, netPiece) => {
                    // Temporary runtime-only buff. Never mutate ChessPieceData/baseData.
                    runtime.currentMoveRange += data.effectValue1;
                    runtime.currentHealth += data.effectValue2;
                    runtime.isSuperBuffed = true;

                    if (netPiece != null)
                        netPiece.currentHp = runtime.currentHealth;
                });
                break;

            case CardEffectType.PawnForwardAttack:
                ForEachFriendlyPiece(myFaction, "Pawn", (runtime, netPiece) => {
                    runtime.canAttackStraight = true;
                });
                break;

            case CardEffectType.Recall:
                if (targetRuntime != null && targetRuntime.faction == myFaction && ServerBoardManager.Instance != null)
                {
                    Vector2Int oldPos = targetRuntime.previousGridPosition;
                    if (ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(oldPos.x, oldPos.y))
                    {
                        ServerBoardManager.Instance.MovePiece(targetRuntime.currentGridPosition, oldPos);
                    }
                    else isSuccess = false;
                }
                else isSuccess = false;
                break;

            case CardEffectType.SummonCapturedPawn:
                isSuccess = TrySummonCapturedPawnCard(player, data, targetPos);
                break;

            default:
                isSuccess = false;
                Debug.LogWarning($"[Server Card] Unsupported card effect type: {data.effectType}");
                break;
        }

        // Nếu xài thành công, trừ Uses, gán Cooldown và báo về Client
        if (isSuccess)
        {
            cardInstance.remainingUses--;
            cardInstance.currentCooldown = data.effectType == CardEffectType.SummonCapturedPawn ? 0 : Mathf.Max(0, data.baseCooldown);
            controller.HandCards.Set(handIndex, cardInstance);
            Debug.Log($"[Server Card] Player {player} used card '{data.cardName}' successfully!");
        }

        return isSuccess;
    }

    private bool TrySummonCapturedPawnCard(PlayerRef player, CardData data, Vector2Int targetPos)
    {
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
            return false;

        if (!ServerGameManager.Instance.IsKingPlayer(player))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' can only be used by the Rogue King player.");
            return false;
        }

        if (targetPos.x < 0 || targetPos.y < 0)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires a board target.");
            return false;
        }

        if (ServerBoardManager.Instance.logicBoard == null || !ServerBoardManager.Instance.logicBoard.IsValidPosition(targetPos.x, targetPos.y))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' target {targetPos} is invalid.");
            return false;
        }

        if (!ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(targetPos.x, targetPos.y) || ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' target {targetPos} is occupied.");
            return false;
        }

        ChessPieceData pawnData = data.summonPieceData != null
            ? data.summonPieceData
            : ServerBoardManager.Instance.FindFirstPawnData(ChessFaction.ChessRogue);

        if (pawnData == null)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' has no summonPieceData and no Pawn data could be found.");
            return false;
        }

        return ServerBoardManager.Instance.TrySpawnRuntimePiece(pawnData, targetPos, ChessFaction.ChessRogue);
    }

    // Hàm bổ trợ quét bàn cờ
    private void ForEachFriendlyPiece(ChessFaction faction, string requiredNamePart, System.Action<ChessPieceRuntime, NetworkChessPiece> action)
    {
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null) return;
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

    private bool ValidateTargetIfNeeded(CardData data, Vector2Int targetPos)
    {
        if (data != null && data.effectType == CardEffectType.SummonCapturedPawn)
        {
            if (targetPos.x < 0 || targetPos.y < 0)
                return false;

            return ServerBoardManager.Instance != null &&
                   ServerBoardManager.Instance.logicBoard != null &&
                   ServerBoardManager.Instance.logicBoard.IsValidPosition(targetPos.x, targetPos.y) &&
                   ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(targetPos.x, targetPos.y) &&
                   ServerBoardManager.Instance.GetPieceAt(targetPos) == null;
        }

        if (string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        if (targetPos.x < 0 || targetPos.y < 0)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires target containing '{data.requiredTargetName}', but client sent no target.");
            return false;
        }

        if (ServerBoardManager.Instance == null)
            return false;

        ChessPieceRuntime targetRuntime = ServerBoardManager.Instance.GetRuntimeAt(targetPos);
        if (targetRuntime == null || targetRuntime.baseData == null)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' target at {targetPos} is empty or invalid.");
            return false;
        }

        if (!targetRuntime.baseData.pieceName.Contains(data.requiredTargetName))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires target containing '{data.requiredTargetName}', got '{targetRuntime.baseData.pieceName}'.");
            return false;
        }

        return true;
    }
}
