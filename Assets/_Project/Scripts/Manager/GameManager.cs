using System;
using UnityEngine;


public enum GameState 
{ 
    Setup, 
    PlayerTurn, 
    ResolvingTurn, 
    GameOver 
}

public class GameManager : SingletonMB<GameManager>
{

    public GameState currentState { get; private set; }
    public ChessFaction currentTurnFaction { get; private set; }

    [SerializeField] private ChessBoard chessBoard;

    public Action<Vector2Int, Vector2Int> OnPieceMoved;
    public Action<ChessFaction> OnTurnChanged;

    private void Start()
    {
        currentState = GameState.Setup;

        StartGame();
    }

    public void StartGame()
    {
        currentTurnFaction = ChessFaction.ChessAlliance;

        currentState = GameState.PlayerTurn;

        OnTurnChanged?.Invoke(currentTurnFaction);
    }

    public void RequestMovePiece(Vector2Int start, Vector2Int finish)
    {
        if (currentState != GameState.PlayerTurn) return;

        var movingPiece = chessBoard.GetPieceRuntimeAt(start);

        if (movingPiece == null) return;

        var validMoves = chessBoard.boardData.GetValidMoves(movingPiece);

        if (validMoves.Contains(finish))
        {
            currentState = GameState.ResolvingTurn;

            chessBoard.boardData.MovePiece(start, finish);

            OnPieceMoved?.Invoke(start, finish);
        }
        else
        {
            Debug.Log("Invalid move requested");
        }
    }

    public void OnTurnResolved()
    {
        if (currentState == GameState.ResolvingTurn)
        {
            currentTurnFaction = currentTurnFaction == ChessFaction.ChessAlliance ? ChessFaction.ChessRogue : ChessFaction.ChessAlliance;

            currentState = GameState.PlayerTurn;
            OnTurnChanged?.Invoke(currentTurnFaction);
        }
    }

    public bool CanPlayerAction(ChessFaction playerFaction)
    {
        return currentState == GameState.PlayerTurn && currentTurnFaction == playerFaction;
    }
}