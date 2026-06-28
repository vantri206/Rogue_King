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
                runtime.hasMoved = true;
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


        if (pieceRuntime.baseData.pieceName.Contains("Pawn"))
        {
            // ==================================================
            // 1. LOGIC ĐẶC BIỆT DÀNH RIÊNG CHO QUÂN TỐT (PAWN) - BÀN CỜ NGANG
            // ==================================================
            if (pieceRuntime.baseData.pieceName.Contains("Pawn"))
            {
                // Xác định hướng tiến theo trục X (Ngang).
                // Giả sử: ChessAlliance ở bên Trái (X tăng dần -> 1). Rogue ở bên Phải (X giảm dần -> -1).
                // NẾU VÀO GAME THẤY TỐT ĐI LÙI, BẠN CHỈ CẦN ĐẢO NGƯỢC SỐ 1 VÀ -1 Ở DÒNG DƯỚI ĐÂY NHÉ:
                int forwardDir = (pieceRuntime.faction == ChessFaction.ChessAlliance) ? 1 : -1;

                // --- A. ĐI THẲNG (Theo Trục X) ---
                for (int i = 1; i <= pieceRuntime.currentMoveRange; i++)
                {
                    // CỘNG forwardDir VÀO TRỤC X
                    Vector2Int forwardMove = new Vector2Int(currentPos.x + (forwardDir * i), currentPos.y);

                    if (!IsValidPosition(forwardMove.x, forwardMove.y)) break;

                    // Nếu ô trống -> Cho đi tiếp
                    if (IsTileEmptyForMovement(forwardMove.x, forwardMove.y))
                    {
                        validMoves.Add(forwardMove);
                    }
                    else
                    {
                        // Gặp vật cản. NẾU đang có thẻ bài "Ăn Thẳng" và cản ở ngay trước mặt (i=1)
                        if (i == 1 && pieceRuntime.canAttackStraight)
                        {
                            var targetStraight = GetEntityAt<ChessPieceRuntime>(forwardMove.x, forwardMove.y);
                            if (targetStraight != null && targetStraight.faction != pieceRuntime.faction)
                            {
                                validMoves.Add(forwardMove); // Cho phép chém thẳng
                            }
                        }
                        break; // Bị chặn thì dừng tiến lên
                    }
                }

                // --- B. NƯỚC ĐẦU TIÊN NHẢY 2 Ô (Theo Trục X) ---
                if (!pieceRuntime.hasMoved)
                {
                    Vector2Int forward1 = new Vector2Int(currentPos.x + forwardDir, currentPos.y);
                    Vector2Int forward2 = new Vector2Int(currentPos.x + (forwardDir * 2), currentPos.y);

                    if (IsValidPosition(forward1.x, forward1.y) && IsTileEmptyForMovement(forward1.x, forward1.y) &&
                        IsValidPosition(forward2.x, forward2.y) && IsTileEmptyForMovement(forward2.x, forward2.y))
                    {
                        if (!validMoves.Contains(forward2)) validMoves.Add(forward2);
                    }
                }

                // --- C. ĂN CHÉO (Tiến lên theo X, Chéo lên/xuống theo Y) ---
                Vector2Int diagUp = new Vector2Int(currentPos.x + forwardDir, currentPos.y + 1);
                Vector2Int diagDown = new Vector2Int(currentPos.x + forwardDir, currentPos.y - 1);

                // Kiểm tra chéo lên trên
                if (IsValidPosition(diagUp.x, diagUp.y))
                {
                    var targetUp = GetEntityAt<ChessPieceRuntime>(diagUp.x, diagUp.y);
                    if (targetUp != null && targetUp.faction != pieceRuntime.faction)
                        validMoves.Add(diagUp);
                }

                // Kiểm tra chéo xuống dưới
                if (IsValidPosition(diagDown.x, diagDown.y))
                {
                    var targetDown = GetEntityAt<ChessPieceRuntime>(diagDown.x, diagDown.y);
                    if (targetDown != null && targetDown.faction != pieceRuntime.faction)
                        validMoves.Add(diagDown);
                }
            }
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
}