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

        if (controller.IsCardUseSilenced)
        {
            Debug.LogWarning($"[Server Card] Player {player} tried to use a card while BishopSilence is active. Rejected.");
            return false;
        }

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
                isSuccess = TryApplyKingRevive(data, myFaction, targetPos);
                break;

            case CardEffectType.KingSweep:
                isSuccess = TryApplyKingSweep(data, myFaction, targetRuntime);
                break;

            case CardEffectType.KingDash:
                isSuccess = TryApplyKingDash(data, myFaction, targetRuntime, targetPos);
                break;

            case CardEffectType.SuperBuff:
                isSuccess = TryApplyKingDamageSuperBuff(data, myFaction);
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

    private bool TryApplyKingDamageSuperBuff(CardData data, ChessFaction myFaction)
    {
        if (data == null || ServerBoardManager.Instance == null)
            return false;

        ChessPieceRuntime kingRuntime = FindKingRuntime(myFaction);
        if (kingRuntime == null || kingRuntime.baseData == null)
        {
            Debug.LogWarning($"[Server Card] SuperBuff failed: cannot find friendly King for faction={myFaction}.");
            return false;
        }

        string pieceName = kingRuntime.baseData.pieceName ?? string.Empty;
        if (!pieceName.Contains("King"))
            return false;

        int multiplier = Mathf.Max(1, data.effectValue1 <= 0 ? 2 : data.effectValue1);
        int durationTurns = Mathf.Max(1, data.effectValue2 <= 0 ? 3 : data.effectValue2);

        kingRuntime.kingDamageMultiplier = multiplier;
        kingRuntime.kingDamageBuffTurnsLeft = durationTurns;
        kingRuntime.currentAttack = kingRuntime.baseData.baseAttack * multiplier;
        kingRuntime.isSuperBuffed = true;

        NetworkChessPiece kingNetPiece = ServerBoardManager.Instance.GetPieceAt(kingRuntime.currentGridPosition);
        if (kingNetPiece != null)
            ServerBoardManager.Instance.SyncNetworkPieceFromRuntime(kingNetPiece, kingRuntime);

        Debug.Log($"[Server Card] SuperBuff applied to King. Faction={myFaction}, Multiplier=x{multiplier}, Turns={durationTurns}, RuntimeAttack={kingRuntime.currentAttack}.");
        return true;
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
        if (ServerGameManager.Instance == null || Runner == null)
            return false;

        ChessFaction enemyFaction = myFaction == ChessFaction.ChessRogue
            ? ChessFaction.ChessAlliance
            : ChessFaction.ChessRogue;

        ChessPieceRuntime enemyKing = FindKingRuntime(enemyFaction);
        if (enemyKing == null)
        {
            Debug.LogWarning($"[Server Card] BishopSilence failed: cannot find enemy King for faction {enemyFaction}.");
            return false;
        }

        // Giữ lại field cũ để các UI/effect/logic cũ vẫn biết King đang bị silence.
        enemyKing.silencedTurnsLeft = Mathf.Max(1, data.effectValue1 <= 0 ? 1 : data.effectValue1);

        PlayerRef targetPlayer = ResolvePlayerRefForFaction(enemyFaction);
        if (targetPlayer == PlayerRef.None)
        {
            Debug.LogWarning($"[Server Card] BishopSilence failed: cannot resolve target player for faction {enemyFaction}.");
            return false;
        }

        NetworkObject targetObject = Runner.GetPlayerObject(targetPlayer);
        PlayerNetworkController targetController = targetObject != null ? targetObject.GetComponent<PlayerNetworkController>() : null;
        if (targetController == null)
        {
            Debug.LogWarning($"[Server Card] BishopSilence failed: target PlayerNetworkController missing for {targetPlayer}.");
            return false;
        }

        bool lockedCards = targetController.ApplyOneTurnCardUseSilence(data.cardName);
        Debug.Log($"[Server Card] BishopSilence applied. TargetPlayer={targetPlayer}, TargetFaction={enemyFaction}, LockedCards={lockedCards}.");
        return true;
    }

    private PlayerRef ResolvePlayerRefForFaction(ChessFaction faction)
    {
        if (ServerGameManager.Instance == null)
            return PlayerRef.None;

        if (faction == ChessFaction.ChessRogue)
            return ServerGameManager.Instance.kingPlayer;

        if (faction == ChessFaction.ChessAlliance)
            return ServerGameManager.Instance.chessPlayer;

        return PlayerRef.None;
    }

    private bool TryApplyKingRevive(CardData data, ChessFaction myFaction, Vector2Int targetPos)
    {
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
            return false;

        DeadPieceRecord selected = FindKingReviveRecordAtTarget(targetPos);
        if (selected == null || selected.pieceData == null)
        {
            Debug.LogWarning($"[Server Card] KingRevive failed: no valid dead Pawn/Knight/Bishop record at {targetPos}, or the tile is occupied.");
            return false;
        }

        bool spawned = ServerBoardManager.Instance.TrySpawnRuntimePiece(selected.pieceData, targetPos, selected.faction);
        if (spawned)
        {
            ServerGameManager.Instance.RemoveDeadPieceRecord(selected);
            Debug.Log($"[Server Card] KingRevive revived '{selected.pieceData.pieceName}' as {selected.faction} at its death tile {targetPos}.");
            return true;
        }

        return false;
    }

    private DeadPieceRecord FindKingReviveRecordAtTarget(Vector2Int targetPos)
    {
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
            return null;

        if (targetPos.x < 0 || targetPos.y < 0)
            return null;

        BoardData board = ServerBoardManager.Instance.logicBoard;
        if (board == null || !board.IsValidPosition(targetPos.x, targetPos.y))
            return null;

        // Không được hồi sinh nếu ô chết đã có quân khác đè lên.
        if (!board.IsTileEmptyForMovement(targetPos.x, targetPos.y) || ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
            return null;

        List<DeadPieceRecord> graveyard = ServerGameManager.Instance.graveyard;
        if (graveyard == null || graveyard.Count == 0)
            return null;

        // Duyệt từ cuối để nếu có nhiều record cùng ô, hồi sinh quân chết gần nhất tại ô đó.
        for (int i = graveyard.Count - 1; i >= 0; i--)
        {
            DeadPieceRecord record = graveyard[i];
            if (record == null || record.deathPos != targetPos)
                continue;

            if (!ServerGameManager.IsKingReviveCandidateRecord(record))
                continue;

            return record;
        }

        return null;
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

    private bool TryApplyKingDash(CardData data, ChessFaction myFaction, ChessPieceRuntime ignoredTargetRuntime, Vector2Int targetPos)
    {
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
            return false;

        ChessPieceRuntime kingRuntime = FindKingRuntime(myFaction);
        if (kingRuntime == null || kingRuntime.baseData == null || !kingRuntime.baseData.pieceName.Contains("King"))
            return false;

        Vector2Int fromPos = kingRuntime.currentGridPosition;
        int range = Mathf.Max(1, data.effectValue1 <= 0 ? 3 : data.effectValue1);

        if (!IsValidKingDashTarget(fromPos, targetPos, range))
        {
            Debug.LogWarning($"[Server Card] KingDash rejected. From={fromPos}, Target={targetPos}, Range={range}.");
            return false;
        }

        bool shouldEndTurn = ServerBoardManager.Instance.MovePiece(fromPos, targetPos);
        kingRuntime.isSuperBuffed = true;
        Debug.Log($"[Server Card] KingDash moved King from {fromPos} to {targetPos}. Range={range}, ShouldEndTurnFromMove={shouldEndTurn}.");
        return true;
    }

    private bool IsValidKingDashTarget(Vector2Int fromPos, Vector2Int targetPos, int range)
    {
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
            return false;

        BoardData board = ServerBoardManager.Instance.logicBoard;
        if (!board.IsValidPosition(targetPos.x, targetPos.y))
            return false;

        if (!board.IsTileEmptyForMovement(targetPos.x, targetPos.y) || ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
            return false;

        Vector2Int delta = targetPos - fromPos;
        if (delta == Vector2Int.zero)
            return false;

        int absX = Mathf.Abs(delta.x);
        int absY = Mathf.Abs(delta.y);
        bool straight = delta.x == 0 || delta.y == 0;
        bool diagonal = absX == absY;
        if (!straight && !diagonal)
            return false;

        int distance = Mathf.Max(absX, absY);
        if (distance < 1 || distance > Mathf.Max(1, range))
            return false;

        Vector2Int dir = new Vector2Int(delta.x == 0 ? 0 : delta.x / absX, delta.y == 0 ? 0 : delta.y / absY);
        for (int step = 1; step <= distance; step++)
        {
            Vector2Int pos = fromPos + dir * step;
            if (!board.IsValidPosition(pos.x, pos.y))
                return false;

            if (!board.IsTileEmptyForMovement(pos.x, pos.y) || ServerBoardManager.Instance.GetPieceAt(pos) != null)
                return false;
        }

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

        if (!IsValidSummonCapturedPawnTarget(ChessFaction.ChessRogue, targetPos, out string rejectReason))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' rejected summon target {targetPos}: {rejectReason}");
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

    private bool IsValidSummonCapturedPawnTarget(ChessFaction kingFaction, Vector2Int targetPos, out string rejectReason)
    {
        rejectReason = string.Empty;

        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
        {
            rejectReason = "server board is not ready";
            return false;
        }

        if (kingFaction != ChessFaction.ChessRogue)
        {
            rejectReason = $"SummonCapturedPawn is Rogue King only, but faction={kingFaction}";
            return false;
        }

        if (targetPos.x < 0 || targetPos.y < 0)
        {
            rejectReason = "missing board target";
            return false;
        }

        BoardData board = ServerBoardManager.Instance.logicBoard;
        if (!board.IsValidPosition(targetPos.x, targetPos.y))
        {
            rejectReason = "target is outside the board";
            return false;
        }

        ChessPieceRuntime kingRuntime = FindKingRuntime(kingFaction);
        if (kingRuntime == null || kingRuntime.baseData == null || !kingRuntime.baseData.pieceName.Contains("King"))
        {
            rejectReason = "cannot find Rogue King on board";
            return false;
        }

        Vector2Int delta = targetPos - kingRuntime.currentGridPosition;
        int absX = Mathf.Abs(delta.x);
        int absY = Mathf.Abs(delta.y);
        bool isOneOfEightNeighborTiles = (absX <= 1 && absY <= 1 && (absX + absY) > 0);
        if (!isOneOfEightNeighborTiles)
        {
            rejectReason = $"target is not in the 8 tiles around the Rogue King at {kingRuntime.currentGridPosition}";
            return false;
        }

        if (!board.IsTileEmptyForMovement(targetPos.x, targetPos.y) || ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
        {
            rejectReason = "target is occupied; summon requires an empty tile with no enemy and no friendly piece";
            return false;
        }

        return true;
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

        if (data.effectType == CardEffectType.SuperBuff)
            return false;

        if (!string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        switch (data.effectType)
        {
            case CardEffectType.Recall:
            case CardEffectType.SummonCapturedPawn:
            case CardEffectType.KingRevive:
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
            return IsValidSummonCapturedPawnTarget(myFaction, targetPos, out _);
        }

        if (data.effectType == CardEffectType.KingRevive)
        {
            return FindKingReviveRecordAtTarget(targetPos) != null;
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

        if (data.effectType == CardEffectType.KingDash)
        {
            ChessPieceRuntime kingRuntime = FindKingRuntime(myFaction);
            int range = Mathf.Max(1, data.effectValue1 <= 0 ? 3 : data.effectValue1);
            return kingRuntime != null && IsValidKingDashTarget(kingRuntime.currentGridPosition, targetPos, range);
        }

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
