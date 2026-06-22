using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class BoardData
{
    public int width { get; private set; }
    public int height { get; private set; }

    private bool[,] walkableTiles;
    private List<GridEntity>[,] entitiesGrid;

    public BoardData(int width, int height, IReadOnlyList<bool> tileExistenceMap)
    {
        this.width = width;
        this.height = height;

        walkableTiles = new bool[width, height];
        entitiesGrid = new List<GridEntity>[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int index = y * width + x;
                bool exists = tileExistenceMap != null && index < tileExistenceMap.Count ? tileExistenceMap[index] : true;
                walkableTiles[x, y] = exists;
                entitiesGrid[x, y] = new List<GridEntity>();
            }
        }
    }

    public bool IsValidPosition(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height && walkableTiles[x, y];
    }

    public bool IsTileEmptyForMovement(int x, int y)
    {
        if (!IsValidPosition(x, y)) return false;

        foreach (var entity in entitiesGrid[x, y])
        {
            if (entity.IsBlockingMovement()) return false;
        }
        return true;
    }

    public void AddEntity(GridEntity entity, int x, int y)
    {
        if (IsValidPosition(x, y) && !entitiesGrid[x, y].Contains(entity))
        {
            entitiesGrid[x, y].Add(entity);
            entity.currentGridPosition = new Vector2Int(x, y);
        }
    }

    public void RemoveEntity(GridEntity entity)
    {
        int x = entity.currentGridPosition.x;
        int y = entity.currentGridPosition.y;

        if (IsValidPosition(x, y))
        {
            entitiesGrid[x, y].Remove(entity);
        }
    }

    public T GetEntityAt<T>(int x, int y) where T : GridEntity
    {
        if (!IsValidPosition(x, y)) return null;
        return entitiesGrid[x, y].OfType<T>().FirstOrDefault();
    }

    public void MoveEntity(GridEntity entity, Vector2Int finishPos)
    {
        if (entity != null && IsValidPosition(finishPos.x, finishPos.y))
        {
            if (entity is ChessPieceRuntime runtime)
            {
                runtime.previousGridPosition = runtime.currentGridPosition;
            }

            RemoveEntity(entity);
            AddEntity(entity, finishPos.x, finishPos.y);
        }
    }

    public List<Vector2Int> GetValidMoves(ChessPieceRuntime pieceRuntime)
    {
        List<Vector2Int> validMoves = new List<Vector2Int>();
        if (pieceRuntime == null) return validMoves;

        Vector2Int currentPos = pieceRuntime.currentGridPosition;

        // ---> TẠO DANH SÁCH HƯỚNG ĐI LINH HOẠT
        List<Vector2Int> directionsToCheck = new List<Vector2Int>(pieceRuntime.currentMoveDirections);

        // NẾU CÓ HIỆU ỨNG TỐT ĂN THẲNG: Bổ sung 4 hướng thẳng kịch kim (Trực diện biên)
        if (pieceRuntime.baseData.pieceName.Contains("Pawn") && pieceRuntime.canAttackStraight)
        {
            if (!directionsToCheck.Contains(Vector2Int.up)) directionsToCheck.Add(Vector2Int.up);
            if (!directionsToCheck.Contains(Vector2Int.down)) directionsToCheck.Add(Vector2Int.down);
            if (!directionsToCheck.Contains(Vector2Int.left)) directionsToCheck.Add(Vector2Int.left);
            if (!directionsToCheck.Contains(Vector2Int.right)) directionsToCheck.Add(Vector2Int.right);
        }

        // Đổi vòng lặp gốc từ pieceRuntime.currentMoveDirections sang directionsToCheck
        foreach (Vector2Int dir in directionsToCheck)
        {
            for (int i = 1; i <= pieceRuntime.currentMoveRange; i++)
            {
                Vector2Int checkPos = currentPos + (dir * i);

                if (!IsValidPosition(checkPos.x, checkPos.y))
                    break;

                ChessPieceRuntime targetPiece = GetEntityAt<ChessPieceRuntime>(checkPos.x, checkPos.y);

                if (targetPiece != null)
                {
                    if (targetPiece.faction != pieceRuntime.faction)
                    {
                        validMoves.Add(checkPos);
                    }

                    if (pieceRuntime.currentMoveType == MovementType.Slide)
                        break;
                }
                else if (IsTileEmptyForMovement(checkPos.x, checkPos.y))
                {
                    validMoves.Add(checkPos);
                }
                else
                {
                    break;
                }
            }
        }
        return validMoves;
    }
}