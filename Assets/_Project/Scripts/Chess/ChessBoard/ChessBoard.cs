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

    [Header("Offline / Local Preview")]
    [Tooltip("Dedicated-server multiplayer scenes should leave this OFF. Network pieces are spawned by ServerBoardManager.")]
    [SerializeField] private bool spawnInitialLocalPieces = false;

    [Header("Runtime Duplicate Guard")]
    [Tooltip("Bật ON cho multiplayer. Khi PlayScene được load lại/rematch, ChessBoard sẽ xóa tile visual cũ trong container trước khi GenerateBoard để tránh chồng 2 bàn cờ.")]
    [SerializeField] private bool clearExistingTilesBeforeRuntimeGenerate = true;

    [Tooltip("Bật ON cho multiplayer. Chỉ ảnh hưởng local/offline pieces nếu Spawn Initial Local Pieces đang bật.")]
    [SerializeField] private bool clearExistingLocalPiecesBeforeRuntimeSpawn = true;

    [Tooltip("Log cảnh báo nếu runtime đang có nhiều ChessBoard active/inactive. Hữu ích để phát hiện scene bị load/sót object cũ.")]
    [SerializeField] private bool warnIfMultipleChessBoardsAtRuntime = true;

    public BoardData boardData { get; private set; }

    private BoardTile[,] grid;

    public LevelData CurrentLevelData => currentLevelData;
    public Vector3 PiecePlacementOffset => piecePlacementOffset;
    public float TileSize => tileSize;
    public Transform EntitiesContainer => entitiesContainer;

    public int boardWidth { get; private set; }
    public int boardHeight { get; private set; }

    private void Start()
    {
        if (Application.isPlaying)
        {
            WarnIfMultipleBoardsAtRuntime();

            if (currentLevelData != null)
            {
                if (clearExistingTilesBeforeRuntimeGenerate)
                    ClearRuntimeTileContainerOnly();

                GenerateBoard();

                if (spawnInitialLocalPieces)
                {
                    if (clearExistingLocalPiecesBeforeRuntimeSpawn)
                        ClearRuntimeEntitiesContainerOnly();

                    SpawnInitialPieces();
                }
            }
            else
            {
                Debug.LogError("No Board Shape Data assigned to ChessBoard!");
            }
        }
    }

    private void WarnIfMultipleBoardsAtRuntime()
    {
        if (!warnIfMultipleChessBoardsAtRuntime || !Application.isPlaying)
            return;

        ChessBoard[] boards = FindObjectsByType<ChessBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boards != null && boards.Length > 1)
        {
            Debug.LogWarning($"[ChessBoard] Found {boards.Length} ChessBoard objects at runtime. If you see duplicated boards after rematch, remove duplicate scene objects or make sure old PlayScene objects are cleaned before joining the next match.", this);
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

        boardWidth = currentLevelData.boardWidth;
        boardHeight = currentLevelData.boardHeight;
        grid = new BoardTile[boardWidth, boardHeight];

        var existenceList = currentLevelData.tileExistenceMap != null
            ? currentLevelData.tileExistenceMap.ToList()
            : null;

        boardData = new BoardData(boardWidth, boardHeight, existenceList);

        for (int x = 0; x < boardWidth; x++)
        {
            for (int y = 0; y < boardHeight; y++)
            {
                if (boardData.IsValidPosition(x, y))
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

    public Vector3 GetTileWorldPosition(Vector2Int gridPos)
    {
        if (grid != null && boardData != null && boardData.IsValidPosition(gridPos.x, gridPos.y))
        {
            BoardTile tile = grid[gridPos.x, gridPos.y];
            if (tile != null)
            {
                return tile.transform.position;
            }
        }

        Transform origin = interactableTilesContainer != null ? interactableTilesContainer : transform;
        Vector3 localPos = new Vector3(gridPos.x * tileSize, -gridPos.y * tileSize, 0f);
        return origin.TransformPoint(localPos);
    }

    public Vector3 GetPieceWorldPosition(Vector2Int gridPos)
    {
        return GetTileWorldPosition(gridPos) + piecePlacementOffset;
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        Transform origin = interactableTilesContainer != null ? interactableTilesContainer : transform;
        Vector3 localPosition = origin.InverseTransformPoint(worldPosition);

        float safeTileSize = Mathf.Approximately(tileSize, 0f) ? 1f : tileSize;

        return new Vector2Int(
            Mathf.RoundToInt(localPosition.x / safeTileSize),
            Mathf.RoundToInt(-localPosition.y / safeTileSize)
        );
    }

    public void SpawnPiece(ChessPieceData pieceData, ChessPiece piecePrefab, Vector2Int startPos, ChessFaction faction)
    {
        if (boardData == null || !boardData.IsValidPosition(startPos.x, startPos.y))
        {
            Debug.LogError("Spawn position out of bounds or tile is invalid!");
            return;
        }

        if (!boardData.IsTileEmptyForMovement(startPos.x, startPos.y))
        {
            Debug.LogError($"Cannot spawn at {startPos} because tile is blocked!");
            return;
        }

        Vector3 worldPos = GetPieceWorldPosition(startPos);

        ChessPiece newPiece = Instantiate(piecePrefab, worldPos, Quaternion.identity, entitiesContainer);
        ChessPieceRuntime newRuntime = new ChessPieceRuntime(pieceData, startPos, faction);

        newPiece.Initialize(newRuntime);
        grid[startPos.x, startPos.y].SetPiece(newPiece);

        boardData.AddEntity(newRuntime, startPos.x, startPos.y);
    }

    public ChessPieceRuntime GetPieceRuntimeAt(Vector2Int gridPos)
    {
        if (boardData != null)
        {
            return boardData.GetEntityAt<ChessPieceRuntime>(gridPos.x, gridPos.y);
        }

        return null;
    }

    public BoardTile GetTileAt(Vector2Int pos)
    {
        if (grid == null) return null;

        if (boardData != null && boardData.IsValidPosition(pos.x, pos.y))
        {
            return grid[pos.x, pos.y];
        }

        return null;
    }

    public List<Vector2Int> GetValidMoves(ChessPieceRuntime pieceRuntime)
    {
        if (boardData == null)
            return new List<Vector2Int>();

        return boardData.GetValidMoves(pieceRuntime);
    }

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
        ClearRuntimeTileContainerOnly();
        ClearRuntimeEntitiesContainerOnly();

        grid = null;
        boardData = null;
    }

    /// <summary>
    /// Runtime-safe cleanup used by NetworkRunnerHandler before returning to menu or joining a new match.
    /// This removes only local visual board children, not the ChessBoard component itself.
    /// </summary>
    public void ClearRuntimeVisualChildren()
    {
        ClearRuntimeTileContainerOnly();
        ClearRuntimeEntitiesContainerOnly();

        grid = null;
        boardData = null;
    }

    private void ClearRuntimeTileContainerOnly()
    {
        ClearChildren(interactableTilesContainer);
    }

    private void ClearRuntimeEntitiesContainerOnly()
    {
        ClearChildren(entitiesContainer);
    }

    private void ClearChildren(Transform container)
    {
        if (container == null)
            return;

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            if (child == null)
                continue;

            DestroyObjectSafe(child.gameObject);
        }
    }

    private static void DestroyObjectSafe(GameObject obj)
    {
        if (obj == null)
            return;

        if (!Application.isPlaying)
            DestroyImmediate(obj);
        else
            Destroy(obj);
    }

    public void MovePieceOnBoard(Vector2Int start, Vector2Int finish)
    {
        if (boardData == null) return;

        BoardTile startTile = GetTileAt(start);
        BoardTile finishTile = GetTileAt(finish);

        if (startTile == null || finishTile == null) return;

        ChessPiece movingPiece = startTile.currentPiece;

        if (finishTile.currentPiece != null)
        {
            Destroy(finishTile.currentPiece.gameObject);
        }

        startTile.ClearPiece();
        finishTile.SetPiece(movingPiece);

        if (movingPiece != null)
        {
            movingPiece.transform.position = finishTile.transform.position + piecePlacementOffset;
        }

        var entity = boardData.GetEntityAt<ChessPieceRuntime>(start.x, start.y);
        if (entity != null)
        {
            boardData.MoveEntity(entity, finish);
        }
    }

    public void ResetAllTileHighlights()
    {
        if (grid == null) return;

        for (int x = 0; x < boardWidth; x++)
        {
            for (int y = 0; y < boardHeight; y++)
            {
                if (grid[x, y] != null)
                {
                    grid[x, y].SetTileState(TileState.None);
                    grid[x, y].ToggleSelection(false);
                }
            }
        }
    }

    private void OnDestroy()
    {
        grid = null;
        boardData = null;
    }
}
