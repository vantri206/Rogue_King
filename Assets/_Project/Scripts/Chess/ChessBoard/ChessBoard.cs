using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class ChessBoard : MonoBehaviour
{
    [Header("Level Data")]
    [SerializeField] private LevelData currentLevelData;

    [Header("Prefabs")]
    [SerializeField] private BoardTile tilePrefab;
    [SerializeField] private ChessPiece piecePrefab;

    [Header("Containers")]
    [SerializeField] private Transform interactableTilesContainer;
    [SerializeField] private Transform entitiesContainer;

    [Header("Settings")]
    [SerializeField] private float tileSize = 1f;
    [SerializeField] private Vector3 piecePlacementOffset = new Vector3(0f, 0.4f, 0f);
    private BoardTile[,] grid;

    public int BoardWidth { get; private set; }
    public int BoardHeight { get; private set; }

    private void Start()
    {
        if (Application.isPlaying)
        {
            if (currentLevelData != null)
            {
                GenerateBoard();
                SpawnInitialPieces();
            }
            else
            {
                Debug.LogError("No Board Shape Data assigned to ChessBoard!");
            }
        }
    }

    private void SpawnInitialPieces()
    {
        if (currentLevelData.initialPieces == null) return;
        
        foreach (var piece in currentLevelData.initialPieces)
        {
            if (piece.pieceData != null && piecePrefab != null)
            {
                SpawnPiece(piece.pieceData, piecePrefab, piece.startPosition, piece.faction);
            }
        }
    }

    private void GenerateBoard()
    {
        if (currentLevelData == null || tilePrefab == null || interactableTilesContainer == null) return;

        BoardWidth = currentLevelData.boardWidth;
        BoardHeight = currentLevelData.boardHeight;
        grid = new BoardTile[BoardWidth, BoardHeight];

        for (int x = 0; x < BoardWidth; x++)
        {
            for (int y = 0; y < BoardHeight; y++)
            {
                int index = y * BoardWidth + x;

                if (currentLevelData.tileExistenceMap != null && index < currentLevelData.tileExistenceMap.Count() && currentLevelData.tileExistenceMap[index] == true)
                {
                    Vector3 localPos = new Vector3(x * tileSize, -y * tileSize, 0f);
                    
                    BoardTile newTile = Instantiate(tilePrefab, interactableTilesContainer);
                    newTile.transform.localPosition = localPos;
                    newTile.Initialize(x, y);

                    grid[x, y] = newTile;
                }
                else
                {
                    grid[x, y] = null;
                }
            }
        }
    }

    public void SpawnPiece(ChessPieceData pieceData, ChessPiece piecePrefab, Vector2Int startPos, ChessFaction faction)
    {
        if (startPos.x < 0 || startPos.x >= BoardWidth || startPos.y < 0 || startPos.y >= BoardHeight)
        {
            Debug.LogError("Spawn position out of bounds!");
            return;
        }

        if (grid[startPos.x, startPos.y] == null)
        {
            Debug.LogError($"Cannot spawn at {startPos} because there is no tile here!");
            return;
        }

        Vector3 tilePos = grid[startPos.x, startPos.y].transform.position;
        Vector3 worldPos = tilePos + piecePlacementOffset;

        ChessPiece newPiece = Instantiate(piecePrefab, worldPos, Quaternion.identity, entitiesContainer);

        ChessPieceRuntime newRuntime = new ChessPieceRuntime(pieceData, startPos, faction);

        newPiece.Initialize(newRuntime);
        grid[startPos.x, startPos.y].SetPiece(newPiece);
    }

    public BoardTile GetTileAt(Vector2Int pos)
    {
        if (grid == null) return null;
        if (pos.x >= 0 && pos.x < BoardWidth && pos.y >= 0 && pos.y < BoardHeight)
        {
            return grid[pos.x, pos.y];
        }
        return null;
    }

    public List<Vector2Int> GetValidMoves(ChessPieceRuntime pieceRuntime)
    {
        List<Vector2Int> validMoves = new List<Vector2Int>();
        if (grid == null) return validMoves;

        Vector2Int currentPos = pieceRuntime.currentGridPosition;

        foreach (Vector2Int dir in pieceRuntime.currentMoveDirections)
        {
            for (int i = 1; i <= pieceRuntime.currentMoveRange; i++)
            {
                Vector2Int checkPos = currentPos + (dir * i);
                BoardTile tile = GetTileAt(checkPos);

                if (tile == null) break;

                if (tile.currentPiece != null)
                {
                    validMoves.Add(checkPos);

                    if (pieceRuntime.currentMoveType == MovementType.Jump)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                validMoves.Add(checkPos);
            }
        }
        return validMoves;
    }

    // Context menu


    [ContextMenu("Generate Board and Pieces")]
    public void GenerateBoardAndPieces()
    {
        ClearBoard();
        if (currentLevelData != null)
        {
            GenerateBoard();
            SpawnInitialPieces();
        }
    }

    [ContextMenu("Clear Board")]
    public void ClearBoard()
    {
        if (interactableTilesContainer != null)
        {
            for (int i = interactableTilesContainer.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(interactableTilesContainer.GetChild(i).gameObject);
            }
        }

        if (entitiesContainer != null)
        {
            for (int i = entitiesContainer.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(entitiesContainer.GetChild(i).gameObject);
            }
        }

        grid = null;
    }

}