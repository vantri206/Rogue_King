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

        if (IsPawn(pieceRuntime))
        {
            AddClassicPawnMoves(validMoves, pieceRuntime, currentPos);
        }
        else
        {
            List<Vector2Int> directionsToCheck = new List<Vector2Int>(pieceRuntime.currentMoveDirections);

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
        }

        return validMoves;
    }

    private void AddClassicPawnMoves(List<Vector2Int> validMoves, ChessPieceRuntime pawnRuntime, Vector2Int currentPos)
    {
        // Classic pawn rule for this board:
        // - Forward direction is configured in ChessPieceData.pawnForwardDirection.
        // - Normal movement: one forward cell only, and only when that cell is empty.
        // - First movement only: two forward cells, and only when both forward cells are empty.
        // - Normal capture: one diagonal-forward cell, and only when occupied by an enemy.
        // - Blocked: any piece directly in front blocks forward movement.
        // - PawnForwardAttack card/buff: pawn may capture the enemy directly in front during that turn only.
        Vector2Int forward = GetPawnForwardDirection(pawnRuntime);
        Vector2Int oneForward = currentPos + forward;

        if (IsValidPosition(oneForward.x, oneForward.y))
        {
            ChessPieceRuntime straightTarget = GetEntityAt<ChessPieceRuntime>(oneForward.x, oneForward.y);
            bool oneForwardIsEmpty = IsTileEmptyForMovement(oneForward.x, oneForward.y);

            if (oneForwardIsEmpty)
            {
                AddUnique(validMoves, oneForward);

                if (!pawnRuntime.hasMoved)
                {
                    Vector2Int twoForward = currentPos + (forward * 2);
                    if (IsValidPosition(twoForward.x, twoForward.y) && IsTileEmptyForMovement(twoForward.x, twoForward.y))
                    {
                        AddUnique(validMoves, twoForward);
                    }
                }
            }
            else if (pawnRuntime.canAttackStraight && straightTarget != null && straightTarget.faction != pawnRuntime.faction)
            {
                AddUnique(validMoves, oneForward);
            }
        }

        Vector2Int side = new Vector2Int(-forward.y, forward.x);
        AddPawnDiagonalCapture(validMoves, pawnRuntime, currentPos + forward + side);
        AddPawnDiagonalCapture(validMoves, pawnRuntime, currentPos + forward - side);
    }

    private Vector2Int GetPawnForwardDirection(ChessPieceRuntime pawnRuntime)
    {
        Vector2Int forward = Vector2Int.right;

        if (pawnRuntime != null && pawnRuntime.baseData != null)
        {
            forward = pawnRuntime.baseData.pawnForwardDirection;

            if (forward == Vector2Int.zero && pawnRuntime.baseData.moveDirections != null && pawnRuntime.baseData.moveDirections.Count > 0)
            {
                forward = pawnRuntime.baseData.moveDirections[0];
            }

            if (pawnRuntime.baseData.mirrorPawnForwardForRogueFaction && pawnRuntime.faction == ChessFaction.ChessRogue)
            {
                forward = -forward;
            }
        }

        return NormalizeCardinalDirection(forward, Vector2Int.right);
    }

    private static Vector2Int NormalizeCardinalDirection(Vector2Int direction, Vector2Int fallback)
    {
        if (direction == Vector2Int.zero)
            return fallback;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return new Vector2Int(direction.x >= 0 ? 1 : -1, 0);

        return new Vector2Int(0, direction.y >= 0 ? 1 : -1);
    }

    private void AddPawnDiagonalCapture(List<Vector2Int> validMoves, ChessPieceRuntime pawnRuntime, Vector2Int targetPos)
    {
        if (!IsValidPosition(targetPos.x, targetPos.y))
            return;

        ChessPieceRuntime target = GetEntityAt<ChessPieceRuntime>(targetPos.x, targetPos.y);
        if (target != null && target.faction != pawnRuntime.faction)
        {
            AddUnique(validMoves, targetPos);
        }
    }

    private static bool IsPawn(ChessPieceRuntime pieceRuntime)
    {
        return pieceRuntime != null &&
               pieceRuntime.baseData != null &&
               !string.IsNullOrEmpty(pieceRuntime.baseData.pieceName) &&
               pieceRuntime.baseData.pieceName.IndexOf("Pawn", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddUnique(List<Vector2Int> list, Vector2Int value)
    {
        if (!list.Contains(value))
            list.Add(value);
    }
}
