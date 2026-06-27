using Fusion;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ServerBoardManager : NetworkBehaviour
{
    public static ServerBoardManager Instance { get; private set; }

    [Header("Data Configuration")]
    public LevelData currentLevelData;

    [SerializeField] private NetworkPrefabRef networkPiecePrefab;

    [Header("Runtime Spawn Piece Catalog")]
    [Tooltip("Các ChessPieceData có thể được spawn trong lúc match bằng card/skill, nhưng không nhất thiết nằm trong Initial Pieces. Ví dụ: Tốt Rogue màu xanh cho card SummonCapturedPawn.")]
    [SerializeField] private List<ChessPieceData> runtimeSpawnablePieceData = new List<ChessPieceData>();

    [Header("Scene Visual Board")]
    [Tooltip("Optional. If empty, it is found in the scene. Used only for matching offline board world positions.")]
    [SerializeField] private ChessBoard visualBoard;

    public BoardData logicBoard { get; private set; }

    private readonly Dictionary<Vector2Int, NetworkChessPiece> boardState = new Dictionary<Vector2Int, NetworkChessPiece>();

    public override void Spawned()
    {
        if (Instance == null) Instance = this;
        else { Runner.Despawn(Object); return; }

        ResolveSceneReferences();
    }

    private void ResolveSceneReferences()
    {
        if (visualBoard == null)
            visualBoard = FindFirstObjectByType<ChessBoard>();

        if (currentLevelData == null && visualBoard != null)
            currentLevelData = visualBoard.CurrentLevelData;
    }

    public void SetupBoardFromData()
    {
        if (!HasStateAuthority) return;

        ResolveSceneReferences();

        if (currentLevelData == null)
        {
            Debug.LogError("[ServerBoardManager] Current Level Data is not assigned.");
            return;
        }

        if (!networkPiecePrefab.IsValid)
        {
            Debug.LogError("[ServerBoardManager] Network Piece Prefab is not assigned.");
            return;
        }

        ClearBoard();

        var existenceList = currentLevelData.tileExistenceMap != null
            ? currentLevelData.tileExistenceMap.ToList()
            : null;

        logicBoard = new BoardData(
            currentLevelData.boardWidth,
            currentLevelData.boardHeight,
            existenceList
        );

        for (int i = 0; i < currentLevelData.initialPieces.Count; i++)
        {
            SpawnNetworkPiece(currentLevelData.initialPieces[i], i);
        }

        Debug.Log("[Server] Board initialized with authoritative logic data.");
    }

    private void SpawnNetworkPiece(InitialPieceSetup setup, int dataIndex)
    {
        if (setup.pieceData == null)
        {
            Debug.LogWarning($"[ServerBoardManager] Skipped null piece at {setup.startPosition}.");
            return;
        }

        if (logicBoard == null || !logicBoard.IsValidPosition(setup.startPosition.x, setup.startPosition.y))
        {
            Debug.LogWarning($"[ServerBoardManager] Invalid spawn position {setup.startPosition} for {setup.pieceData.pieceName}.");
            return;
        }

        if (!logicBoard.IsTileEmptyForMovement(setup.startPosition.x, setup.startPosition.y))
        {
            Debug.LogWarning($"[ServerBoardManager] Spawn position {setup.startPosition} is occupied.");
            return;
        }

        Vector3 worldPos = GridToWorld(setup.startPosition);

        NetworkObject netObj = Runner.Spawn(
            networkPiecePrefab,
            worldPos,
            Quaternion.identity,
            null,
            (runner, spawnedObject) =>
            {
                NetworkChessPiece networkPiece = spawnedObject.GetComponent<NetworkChessPiece>();
                if (networkPiece != null)
                {
                    networkPiece.InitializeFromServerSpawn(dataIndex, setup);
                }
            }
        );

        NetworkChessPiece piece = netObj.GetComponent<NetworkChessPiece>();
        if (piece == null)
        {
            Debug.LogError("[ServerBoardManager] Network Piece Prefab must contain NetworkChessPiece.");
            Runner.Despawn(netObj);
            return;
        }

        boardState[setup.startPosition] = piece;

        ChessPieceRuntime runtime = new ChessPieceRuntime(
            setup.pieceData,
            setup.startPosition,
            setup.faction
        );

        runtime.currentHealth = setup.pieceData.baseHealth;
        runtime.silencedTurnsLeft = 0;

        logicBoard.AddEntity(runtime, setup.startPosition.x, setup.startPosition.y);
    }

    public bool TrySpawnRuntimePiece(ChessPieceData pieceData, Vector2Int spawnPos, ChessFaction faction)
    {
        if (!HasStateAuthority) return false;
        if (pieceData == null)
        {
            Debug.LogWarning("[ServerBoardManager] Cannot spawn runtime piece: pieceData is null.");
            return false;
        }

        if (logicBoard == null)
        {
            Debug.LogWarning("[ServerBoardManager] Cannot spawn runtime piece: logicBoard is null.");
            return false;
        }

        if (!networkPiecePrefab.IsValid)
        {
            Debug.LogError("[ServerBoardManager] Cannot spawn runtime piece: Network Piece Prefab is not assigned.");
            return false;
        }

        if (!logicBoard.IsValidPosition(spawnPos.x, spawnPos.y))
        {
            Debug.LogWarning($"[ServerBoardManager] Cannot spawn runtime piece at {spawnPos}: invalid tile.");
            return false;
        }

        if (!logicBoard.IsTileEmptyForMovement(spawnPos.x, spawnPos.y) || boardState.ContainsKey(spawnPos))
        {
            Debug.LogWarning($"[ServerBoardManager] Cannot spawn runtime piece at {spawnPos}: tile is occupied.");
            return false;
        }

        int dataIndex = GetPieceDataIndex(pieceData);
        if (dataIndex < 0)
        {
            Debug.LogWarning($"[ServerBoardManager] Runtime piece '{pieceData.pieceName}' is not registered. Add it to LevelData.initialPieces or ServerBoardManager.runtimeSpawnablePieceData.");
            return false;
        }

        InitialPieceSetup setup = new InitialPieceSetup
        {
            pieceData = pieceData,
            startPosition = spawnPos,
            faction = faction
        };

        Vector3 worldPos = GridToWorld(spawnPos);
        NetworkObject netObj = Runner.Spawn(
            networkPiecePrefab,
            worldPos,
            Quaternion.identity,
            null,
            (runner, spawnedObject) =>
            {
                NetworkChessPiece networkPiece = spawnedObject.GetComponent<NetworkChessPiece>();
                if (networkPiece != null)
                {
                    networkPiece.InitializeFromServerSpawn(dataIndex, setup);
                }
            }
        );

        NetworkChessPiece piece = netObj.GetComponent<NetworkChessPiece>();
        if (piece == null)
        {
            Debug.LogError("[ServerBoardManager] Network Piece Prefab must contain NetworkChessPiece.");
            Runner.Despawn(netObj);
            return false;
        }

        boardState[spawnPos] = piece;

        ChessPieceRuntime runtime = new ChessPieceRuntime(pieceData, spawnPos, faction);
        runtime.currentHealth = pieceData.baseHealth;
        runtime.silencedTurnsLeft = 0;
        logicBoard.AddEntity(runtime, spawnPos.x, spawnPos.y);

        Debug.Log($"[ServerBoardManager] Spawned runtime piece '{pieceData.pieceName}' as {faction} at {spawnPos}.");
        return true;
    }

    public void ClearBoard()
    {
        if (!HasStateAuthority) return;

        foreach (var kvp in boardState)
        {
            if (kvp.Value != null && kvp.Value.Object != null)
            {
                Runner.Despawn(kvp.Value.Object);
            }
        }

        boardState.Clear();
        logicBoard = null;

        if (ServerCombatManager.Instance != null)
            ServerCombatManager.Instance.ClearHiddenMines();
    }

    public bool IsValidMove(Vector2Int fromPos, Vector2Int toPos, PlayerRef requestingPlayer)
    {
        if (!HasStateAuthority) return false;
        if (logicBoard == null) return false;
        if (ServerGameManager.Instance == null) return false;
        if (!ServerGameManager.Instance.CanPlayerAct(requestingPlayer)) return false;

        ChessPieceRuntime pieceRuntime = logicBoard.GetEntityAt<ChessPieceRuntime>(fromPos.x, fromPos.y);
        if (pieceRuntime == null) return false;

        if (ServerGameManager.Instance.currentGameState == NetGameState.KingTurn)
        {
            if (!ServerGameManager.Instance.IsKingPlayer(requestingPlayer)) return false;
            if (pieceRuntime.faction != ChessFaction.ChessRogue) return false;
        }
        else if (ServerGameManager.Instance.currentGameState == NetGameState.ChessTurn)
        {
            if (!ServerGameManager.Instance.IsChessPlayer(requestingPlayer)) return false;
            if (pieceRuntime.faction != ChessFaction.ChessAlliance) return false;
        }
        else
        {
            return false;
        }

        List<Vector2Int> validMoves = logicBoard.GetValidMoves(pieceRuntime);
        return validMoves.Contains(toPos);
    }

    /// <summary>
    /// Moves a piece on the authoritative board.
    /// Returns true when the caller should end the turn. Returns false when the move already caused a phase/game-state transition.
    /// </summary>
    public bool MovePiece(Vector2Int fromPos, Vector2Int toPos)
    {
        if (!HasStateAuthority) return false;
        if (logicBoard == null) return false;

        if (!boardState.TryGetValue(fromPos, out NetworkChessPiece piece) || piece == null)
            return false;

        ChessFaction movedFaction = piece.faction;
        ChessPieceRuntime movingRuntimeBeforeMove = logicBoard.GetEntityAt<ChessPieceRuntime>(fromPos.x, fromPos.y);
        if (movingRuntimeBeforeMove != null)
            movedFaction = movingRuntimeBeforeMove.faction;

        NetGameState stateBeforeMove = ServerGameManager.Instance != null
            ? ServerGameManager.Instance.currentGameState
            : NetGameState.Init;

        if (boardState.TryGetValue(toPos, out NetworkChessPiece capturedPiece) && capturedPiece != null)
        {
            PlayerRef killerPlayer = PlayerRef.None;
            if (ServerGameManager.Instance != null)
                killerPlayer = movedFaction == ChessFaction.ChessRogue ? ServerGameManager.Instance.kingPlayer : ServerGameManager.Instance.chessPlayer;

            bool capturedKing = ServerCombatManager.Instance != null && ServerCombatManager.Instance.ProcessInstaKill(capturedPiece, killerPlayer);

            if (capturedKing || ServerGameManager.Instance == null || ServerGameManager.Instance.currentGameState != stateBeforeMove)
            {
                return false;
            }
        }

        boardState.Remove(fromPos);
        boardState[toPos] = piece;

        piece.currentGridPos = toPos;

        ChessPieceRuntime runtime = movingRuntimeBeforeMove != null
            ? movingRuntimeBeforeMove
            : logicBoard.GetEntityAt<ChessPieceRuntime>(fromPos.x, fromPos.y);

        if (runtime != null)
        {
            logicBoard.MoveEntity(runtime, toPos);
            runtime.hasMoved = true;
        }

        if (ServerCombatManager.Instance != null && ServerCombatManager.Instance.TryTriggerHiddenMineForMovedPiece(movedFaction, fromPos, toPos))
        {
            Debug.Log($"[ServerBoardManager] Move from {fromPos} to {toPos} triggered a hidden mine. Turn end is delayed until mine explosion resolves.");
            return false;
        }

        return true;
    }

    public NetworkChessPiece GetPieceAt(Vector2Int pos)
    {
        boardState.TryGetValue(pos, out NetworkChessPiece piece);
        return piece;
    }

    public bool HasAnyPieceOfFaction(ChessFaction faction)
    {
        if (!HasStateAuthority) return false;

        foreach (var kvp in boardState)
        {
            NetworkChessPiece piece = kvp.Value;
            if (piece != null && piece.faction == faction)
                return true;
        }

        return false;
    }

    public int CountPiecesOfFaction(ChessFaction faction)
    {
        if (!HasStateAuthority) return 0;

        int count = 0;
        foreach (var kvp in boardState)
        {
            NetworkChessPiece piece = kvp.Value;
            if (piece != null && piece.faction == faction)
                count++;
        }

        return count;
    }

    public ChessPieceRuntime GetRuntimeAt(Vector2Int pos)
    {
        if (logicBoard == null) return null;
        return logicBoard.GetEntityAt<ChessPieceRuntime>(pos.x, pos.y);
    }

    public ChessPieceRuntime RemovePieceAt(Vector2Int pos)
    {
        boardState.Remove(pos);

        ChessPieceRuntime runtime = GetRuntimeAt(pos);
        if (runtime != null)
        {
            logicBoard.RemoveEntity(runtime);
        }

        return runtime;
    }

    public void SyncRuntimeFromNetworkPiece(NetworkChessPiece piece)
    {
        if (!HasStateAuthority || piece == null || logicBoard == null) return;

        ChessPieceRuntime runtime = GetRuntimeAt(piece.currentGridPos);
        if (runtime == null) return;

        runtime.currentHealth = piece.currentHp;
        runtime.currentSkillCooldown = piece.currentSkillCooldown;
        runtime.silencedTurnsLeft = piece.silencedTurnsLeft;
        runtime.faction = piece.faction;
    }

    public void TickTurnTimers(ChessFaction nextTurnFaction)
    {
        if (!HasStateAuthority) return;

        foreach (var kvp in boardState)
        {
            NetworkChessPiece piece = kvp.Value;

            if (piece != null && piece.faction == nextTurnFaction)
            {
                if (piece.currentSkillCooldown > 0)
                    piece.currentSkillCooldown--;

                if (piece.silencedTurnsLeft > 0)
                    piece.silencedTurnsLeft--;

                SyncRuntimeFromNetworkPiece(piece);
            }
        }
    }

    public ChessPieceData GetPieceDataByIndex(int dataIndex)
    {
        ResolveSceneReferences();

        if (dataIndex < 0)
            return null;

        int initialCount = currentLevelData != null && currentLevelData.initialPieces != null
            ? currentLevelData.initialPieces.Count
            : 0;

        if (dataIndex < initialCount)
            return currentLevelData.initialPieces[dataIndex].pieceData;

        int runtimeIndex = dataIndex - initialCount;
        if (runtimeSpawnablePieceData != null && runtimeIndex >= 0 && runtimeIndex < runtimeSpawnablePieceData.Count)
            return runtimeSpawnablePieceData[runtimeIndex];

        return null;
    }

    public int GetPieceDataIndex(ChessPieceData data)
    {
        ResolveSceneReferences();

        if (data == null)
            return -1;

        if (currentLevelData != null && currentLevelData.initialPieces != null)
        {
            for (int i = 0; i < currentLevelData.initialPieces.Count; i++)
            {
                if (currentLevelData.initialPieces[i].pieceData == data)
                    return i;
            }
        }

        if (runtimeSpawnablePieceData != null)
        {
            for (int i = 0; i < runtimeSpawnablePieceData.Count; i++)
            {
                if (runtimeSpawnablePieceData[i] == data)
                {
                    int initialCount = currentLevelData != null && currentLevelData.initialPieces != null
                        ? currentLevelData.initialPieces.Count
                        : 0;
                    return initialCount + i;
                }
            }
        }

        return -1;
    }

    public ChessPieceData FindFirstPawnData(ChessFaction preferredFaction)
    {
        ResolveSceneReferences();

        if (runtimeSpawnablePieceData != null)
        {
            foreach (ChessPieceData data in runtimeSpawnablePieceData)
            {
                if (data != null && !string.IsNullOrEmpty(data.pieceName) && data.pieceName.Contains("Pawn"))
                    return data;
            }
        }

        if (currentLevelData != null && currentLevelData.initialPieces != null)
        {
            foreach (InitialPieceSetup setup in currentLevelData.initialPieces)
            {
                if (setup.pieceData != null && setup.faction == preferredFaction && !string.IsNullOrEmpty(setup.pieceData.pieceName) && setup.pieceData.pieceName.Contains("Pawn"))
                    return setup.pieceData;
            }

            foreach (InitialPieceSetup setup in currentLevelData.initialPieces)
            {
                if (setup.pieceData != null && !string.IsNullOrEmpty(setup.pieceData.pieceName) && setup.pieceData.pieceName.Contains("Pawn"))
                    return setup.pieceData;
            }
        }

        return null;
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        ResolveSceneReferences();

        if (visualBoard != null)
        {
            return visualBoard.GetPieceWorldPosition(gridPos);
        }

        return new Vector3(gridPos.x, -gridPos.y, 0f);
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        ResolveSceneReferences();

        if (visualBoard != null)
        {
            return visualBoard.WorldToGrid(worldPosition);
        }

        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x),
            Mathf.RoundToInt(-worldPosition.y)
        );
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }
}
