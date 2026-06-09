using System.Collections.Generic;
using UnityEngine;

public class BoardData
{
    public int width { get; private set; }
    public int height { get; private set; }

    private bool[,] walkableTiles;
    private ChessPieceRuntime[,] piecesGrid;

    public BoardData(int width, int height, IReadOnlyList<bool> tileExistenceMap)
    {
        this.width = width;
        this.height = height;

        walkableTiles = new bool[width, height];
        piecesGrid = new ChessPieceRuntime[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int index = y * width + x;
                bool exists = tileExistenceMap != null && index < tileExistenceMap.Count ? tileExistenceMap[index] : true;
                walkableTiles[x, y] = exists;
            }
        }
    }

    public bool IsValidPosition(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height && walkableTiles[x, y];
    }

    public bool IsTileEmpty(int x, int y)
    {
        return IsValidPosition(x, y) && piecesGrid[x, y] == null;
    }

    public ChessPieceRuntime GetPieceAt(int x, int y)
    {
        if (!IsValidPosition(x, y)) return null;
        return piecesGrid[x, y];
    }

    public void SetPiece(int x, int y, ChessPieceRuntime piece)
    {
        if (IsValidPosition(x, y))
        {
            piecesGrid[x, y] = piece;
        }
    }

    public void MovePiece(Vector2Int startPos, Vector2Int finishPos)
    {
        var piece = GetPieceAt(startPos.x, startPos.y);
        if (piece != null)
        {
            piecesGrid[startPos.x, startPos.y] = null;
            SetPiece(finishPos.x, finishPos.y, piece);
            piece.currentGridPosition = finishPos;
        }
    }

    public List<Vector2Int> GetValidMoves(ChessPieceRuntime pieceRuntime)
    {
        List<Vector2Int> validMoves = new List<Vector2Int>();
        if (pieceRuntime == null) return validMoves;

        Vector2Int currentPos = pieceRuntime.currentGridPosition;

        foreach (Vector2Int dir in pieceRuntime.currentMoveDirections)
        {
            for (int i = 1; i <= pieceRuntime.currentMoveRange; i++)
            {
                Vector2Int checkPos = currentPos + (dir * i);

                if (!IsValidPosition(checkPos.x, checkPos.y))
                    break;

                ChessPieceRuntime targetPiece = GetPieceAt(checkPos.x, checkPos.y);

                if (targetPiece != null)
                {
                    if (targetPiece.chessFaction != pieceRuntime.chessFaction)
                    {
                        validMoves.Add(checkPos);
                    }

                    if (pieceRuntime.currentMoveType == MovementType.Slide)
                        break;
                }
                else
                {
                    validMoves.Add(checkPos);
                }
            }
        }
        return validMoves;
    }
}