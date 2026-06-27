using Fusion;
using UnityEngine;
using System.Collections.Generic;

public class ServerCardManager : NetworkBehaviour
{
    public static ServerCardManager Instance { get; private set; }

    [Header("Global Card Database")]
    [Tooltip("Kéo thả TOÀN BỘ CardData có trong game vào đây để Server làm từ điển đối chiếu. Thứ tự list này là global card index dùng để sync selected deck.")]
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

    public int CardCount => availableCards != null ? availableCards.Count : 0;

    public IReadOnlyList<CardData> GetAllCards()
    {
        return availableCards;
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

    public bool IsCardAllowedForFaction(CardData data, ChessFaction faction)
    {
        if (data == null)
            return false;

        if (faction == ChessFaction.ChessRogue)
            return data.cardRole == CardRoleType.RogueKing;

        if (faction == ChessFaction.ChessAlliance)
            return data.cardRole == CardRoleType.ChessAlliance;

        return false;
    }

    public bool IsCardIndexAllowedForFaction(int cardIndex, ChessFaction faction)
    {
        return IsCardAllowedForFaction(GetCardData(cardIndex), faction);
    }

    public List<CardData> GetCardsByRole(CardRoleType role)
    {
        List<CardData> result = new List<CardData>();
        if (availableCards == null)
            return result;

        for (int i = 0; i < availableCards.Count; i++)
        {
            CardData data = availableCards[i];
            if (data != null && data.cardRole == role)
                result.Add(data);
        }

        return result;
    }

    // Server-side card flow:
    // Client bấm card -> server validate lượt/card/target/role -> apply gameplay effect -> trừ Uses/Cooldown -> sync lại UI.
    public bool ProcessCardRequest(PlayerRef player, PlayerNetworkController controller, int handIndex, Vector2Int targetPos)
    {
        if (!HasStateAuthority || controller == null) return false;
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null) return false;

        if (handIndex < 0 || handIndex >= controller.HandCards.Length) return false;

        NetworkCardInstance cardInstance = controller.HandCards[handIndex];
        if (!cardInstance.isInitialized || cardInstance.currentCooldown > 0 || cardInstance.remainingUses <= 0) return false;

        CardData data = GetCardData(cardInstance.cardDataIndex);
        if (data == null) return false;

        ChessFaction myFaction = ServerGameManager.Instance.IsKingPlayer(player) ? ChessFaction.ChessRogue : ChessFaction.ChessAlliance;
        if (!IsCardAllowedForFaction(data, myFaction))
        {
            Debug.LogWarning($"[Server Card] Player {player} tried to use card '{data.cardName}' role={data.cardRole} while faction={myFaction}. Rejected.");
            return false;
        }

        if (!ValidateTargetIfNeeded(data, myFaction, targetPos)) return false;

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
            case CardEffectType.PawnShield:
                isSuccess = TryApplyPawnShield(data, myFaction, targetRuntime);
                break;

            case CardEffectType.BishopSilence:
                isSuccess = TryApplyBishopSilence(data, myFaction);
                break;

            case CardEffectType.KingRevive:
                isSuccess = TryApplyKingRevive(data, myFaction);
                break;

            case CardEffectType.KingSweep:
                isSuccess = TryApplyKingSweep(data, myFaction, targetRuntime);
                break;

            case CardEffectType.KingDash:
                isSuccess = TryApplyKingDash(data, myFaction, targetRuntime);
                break;

            case CardEffectType.SuperBuff:
                if (targetRuntime != null && targetNetPiece != null && targetRuntime.faction == myFaction)
                {
                    targetRuntime.currentAttack += data.effectValue1;
                    targetRuntime.currentHealth += data.effectValue2;
                    targetRuntime.isSuperBuffed = true;
                    targetNetPiece.currentHp = targetRuntime.currentHealth;
                }
                else isSuccess = false;
                break;

            case CardEffectType.ExtraTurn:
                controller.hasExtraTurn = true;
                break;

            case CardEffectType.March:
                ForEachFriendlyPiece(myFaction, "Pawn", (runtime, netPiece) => {
                    runtime.currentMoveRange += Mathf.Max(0, data.effectValue1);
                    runtime.currentHealth += Mathf.Max(0, data.effectValue2);
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

        if (isSuccess)
        {
            cardInstance.remainingUses--;
            cardInstance.currentCooldown = data.effectType == CardEffectType.SummonCapturedPawn ? 0 : Mathf.Max(0, data.baseCooldown);
            controller.HandCards.Set(handIndex, cardInstance);
            Debug.Log($"[Server Card] Player {player} used card '{data.cardName}' successfully. RemainingUses={cardInstance.remainingUses}, Cooldown={cardInstance.currentCooldown}");
        }

        return isSuccess;
    }

    private bool TryApplyPawnShield(CardData data, ChessFaction myFaction, ChessPieceRuntime targetRuntime)
    {
        if (targetRuntime == null || targetRuntime.faction != myFaction || targetRuntime.baseData == null)
            return false;

        if (!targetRuntime.baseData.pieceName.Contains("Pawn"))
            return false;

        targetRuntime.hasShield = true;
        if (ServerGameManager.Instance != null)
            ServerGameManager.Instance.hasUsedPawnShieldThisTurn = true;

        return true;
    }

    private bool TryApplyBishopSilence(CardData data, ChessFaction myFaction)
    {
        ChessPieceRuntime enemyKing = FindKingRuntime(myFaction == ChessFaction.ChessRogue ? ChessFaction.ChessAlliance : ChessFaction.ChessRogue);
        if (enemyKing == null)
            return false;

        enemyKing.silencedTurnsLeft = Mathf.Max(1, data.effectValue1 <= 0 ? 1 : data.effectValue1);
        return true;
    }

    private bool TryApplyKingRevive(CardData data, ChessFaction myFaction)
    {
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
            return false;

        List<DeadPieceRecord> graveyard = ServerGameManager.Instance.graveyard;
        if (graveyard == null || graveyard.Count == 0)
            return false;

        for (int i = graveyard.Count - 1; i >= 0; i--)
        {
            DeadPieceRecord record = graveyard[i];
            if (record == null || record.faction != myFaction || record.pieceData == null)
                continue;

            Vector2Int pos = record.deathPos;
            if (ServerBoardManager.Instance.logicBoard == null || !ServerBoardManager.Instance.logicBoard.IsValidPosition(pos.x, pos.y))
                continue;

            if (!ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(pos.x, pos.y) || ServerBoardManager.Instance.GetPieceAt(pos) != null)
                continue;

            bool spawned = ServerBoardManager.Instance.TrySpawnRuntimePiece(record.pieceData, pos, record.faction);
            if (spawned)
            {
                graveyard.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private bool TryApplyKingSweep(CardData data, ChessFaction myFaction, ChessPieceRuntime targetRuntime)
    {
        if (targetRuntime == null || targetRuntime.faction != myFaction || targetRuntime.baseData == null)
            return false;

        if (!targetRuntime.baseData.pieceName.Contains("King"))
            return false;

        targetRuntime.sweepUsesLeft += Mathf.Max(1, data.effectValue1 <= 0 ? 1 : data.effectValue1);
        return true;
    }

    private bool TryApplyKingDash(CardData data, ChessFaction myFaction, ChessPieceRuntime targetRuntime)
    {
        if (targetRuntime == null || targetRuntime.faction != myFaction || targetRuntime.baseData == null)
            return false;

        if (!targetRuntime.baseData.pieceName.Contains("King"))
            return false;

        targetRuntime.currentMoveRange += Mathf.Max(1, data.effectValue1 <= 0 ? 3 : data.effectValue1);
        targetRuntime.isSuperBuffed = true;
        return true;
    }

    private ChessPieceRuntime FindKingRuntime(ChessFaction faction)
    {
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
            return null;

        BoardData board = ServerBoardManager.Instance.logicBoard;
        for (int x = 0; x < board.width; x++)
        {
            for (int y = 0; y < board.height; y++)
            {
                ChessPieceRuntime runtime = board.GetEntityAt<ChessPieceRuntime>(x, y);
                if (runtime != null && runtime.faction == faction && runtime.baseData != null && runtime.baseData.pieceName.Contains("King"))
                    return runtime;
            }
        }

        return null;
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

    public bool DoesCardNeedBoardTarget(CardData data)
    {
        if (data == null)
            return false;

        if (!string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        switch (data.effectType)
        {
            case CardEffectType.SuperBuff:
            case CardEffectType.Recall:
            case CardEffectType.SummonCapturedPawn:
            case CardEffectType.PawnShield:
            case CardEffectType.KingDash:
            case CardEffectType.KingSweep:
                return true;
            default:
                return false;
        }
    }

    private bool ValidateTargetIfNeeded(CardData data, ChessFaction myFaction, Vector2Int targetPos)
    {
        if (data == null)
            return false;

        if (data.effectType == CardEffectType.SummonCapturedPawn)
        {
            if (targetPos.x < 0 || targetPos.y < 0)
                return false;

            return ServerBoardManager.Instance != null &&
                   ServerBoardManager.Instance.logicBoard != null &&
                   ServerBoardManager.Instance.logicBoard.IsValidPosition(targetPos.x, targetPos.y) &&
                   ServerBoardManager.Instance.logicBoard.IsTileEmptyForMovement(targetPos.x, targetPos.y) &&
                   ServerBoardManager.Instance.GetPieceAt(targetPos) == null;
        }

        if (!DoesCardNeedBoardTarget(data))
            return true;

        if (targetPos.x < 0 || targetPos.y < 0)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires a board target, but client sent no target.");
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

        if (!string.IsNullOrEmpty(data.requiredTargetName) && !targetRuntime.baseData.pieceName.Contains(data.requiredTargetName))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires target containing '{data.requiredTargetName}', got '{targetRuntime.baseData.pieceName}'.");
            return false;
        }

        if (data.effectType == CardEffectType.PawnShield && (targetRuntime.faction != myFaction || !targetRuntime.baseData.pieceName.Contains("Pawn")))
            return false;

        if ((data.effectType == CardEffectType.KingDash || data.effectType == CardEffectType.KingSweep) &&
            (targetRuntime.faction != myFaction || !targetRuntime.baseData.pieceName.Contains("King")))
            return false;

        return true;
    }
}
