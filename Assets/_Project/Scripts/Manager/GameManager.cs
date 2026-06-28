using System;
using System.Collections.Generic;
using UnityEngine;
public class DeadPieceRecord
{
    public ChessPieceData pieceData;
    public ChessFaction faction;
    public Vector2Int deathPos;
}
public class GameManager : SingletonMB<GameManager>
{
    public List<DeadPieceRecord> graveyard = new List<DeadPieceRecord>();

    public GameState currentState { get; private set; }
    public GamePhase currentPhase { get; private set; }
    public ChessFaction currentTurnFaction { get; private set; }

    public int phase1TurnCount { get; private set; } = 0;
    public int phase2TurnCount { get; private set; } = 0;

    public bool hasUsedPawnShieldThisTurn { get; set; } = false;

    [SerializeField] private ChessBoard chessBoard;

    public Action<Vector2Int, Vector2Int> OnPieceMoved;
    public Action<ChessFaction> OnTurnChanged;
    public Action<GameState> OnStateChanged;

    private void Start()
    {
        ChangeState(GameState.Setup);
        currentPhase = GamePhase.Phase1;

        Invoke(nameof(StartGame), 0.2f);
        Debug.Log("[GameManager] Initialization delayed. Waiting for all systems to be ready.");
    }

    public void StartGame()
    {
        currentTurnFaction = ChessFaction.ChessRogue;
        ChangeState(GameState.PlayerTurn);
        OnTurnChanged?.Invoke(currentTurnFaction);
    }


    public void RequestMovePiece(Vector2Int start, Vector2Int finish)
    {
        if (currentState != GameState.PlayerTurn) return;

        var movingPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(start.x, start.y);
        if (movingPiece == null) return;

        var validMoves = chessBoard.boardData.GetValidMoves(movingPiece);

        if (validMoves.Contains(finish))
        {
            ChangeState(GameState.ResolvingAction);
            chessBoard.boardData.MoveEntity(movingPiece, finish);
            OnPieceMoved?.Invoke(start, finish);
        }
        else
        {
            Debug.LogWarning("[GameManager] Invalid move requested!");
        }
    }
    public void RequestSpecialMovePiece(Vector2Int start, Vector2Int finish)
    {
        if (currentState != GameState.PlayerTurn) return;

        var movingPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(start.x, start.y);
        if (movingPiece == null) return;

        ChangeState(GameState.ResolvingAction);
        chessBoard.boardData.MoveEntity(movingPiece, finish);
        OnPieceMoved?.Invoke(start, finish);
    }
    public void ActionCompleted(bool consumesTurn)
    {
        if (currentState != GameState.ResolvingAction) return;

        if (consumesTurn)
        {
            EndTurn();
        }
        else
        {
            ChangeState(GameState.PlayerTurn);
        }
    }
    public void ForceResolveTurn()
    {
        if (currentState == GameState.PlayerTurn)
        {
            ChangeState(GameState.ResolvingAction);
            ActionCompleted(true);
        }
    }

    private void EndTurn()
    {
        hasUsedPawnShieldThisTurn = false;

        if (currentPhase == GamePhase.Phase1) phase1TurnCount++;
        else phase2TurnCount++;
        currentTurnFaction = currentTurnFaction == ChessFaction.ChessRogue
            ? ChessFaction.ChessAlliance
            : ChessFaction.ChessRogue;

        TickTurnTimers(currentTurnFaction);

        ChangeState(GameState.PlayerTurn);
        OnTurnChanged?.Invoke(currentTurnFaction);
    }

    private void ChangeState(GameState newState)
    {
        currentState = newState;
        OnStateChanged?.Invoke(currentState);
        Debug.Log($"[GameManager] State changed to -> {newState}");
    }
    private void TickTurnTimers(ChessFaction nextTurnFaction)
    {
        if (chessBoard == null || chessBoard.boardData == null) return;

        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                var piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.faction == nextTurnFaction)
                {
                    if (piece.currentSkillCooldown > 0) piece.currentSkillCooldown--;
                    if (piece.silencedTurnsLeft > 0) piece.silencedTurnsLeft--;
                }
            }
        }
    }
    public bool CanPlayerAction(ChessFaction playerFaction)
    {
        return currentState == GameState.PlayerTurn && currentTurnFaction == playerFaction;
    }

    public void OnKingDefeated()
    {
        if (currentPhase == GamePhase.Phase1)
        {
            ChangeState(GameState.PhaseTransition);
            Debug.Log($"[GameManager] Phase 1 ended. Total Turns: {phase1TurnCount}. Preparing role swap...");
        }
        else
        {
            ChangeState(GameState.GameOver);
            DetermineWinner();
        }
    }

    private void DetermineWinner()
    {
        if (phase1TurnCount < phase2TurnCount)
            Debug.Log("[GameManager] Player 1 (King in Phase 1) WINS!");
        else if (phase2TurnCount < phase1TurnCount)
            Debug.Log("[GameManager] Player 2 (King in Phase 2) WINS!");
        else
            Debug.Log("[GameManager] Match DRAW!");
    }
}