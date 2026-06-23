using Fusion;
using UnityEngine;
using System.Collections.Generic;

public class ServerGameManager : NetworkBehaviour
{
    public static ServerGameManager Instance { get; private set; }

    [Networked, OnChangedRender(nameof(OnStateChanged))]
    public NetGameState currentGameState { get; set; }

    [Networked, OnChangedRender(nameof(OnPhaseChanged))]
    public GamePhase currentPhase { get; private set; }

    [Networked] public int phase1TurnCount { get; private set; }
    [Networked] public int phase2TurnCount { get; private set; }

    [Networked] public TickTimer actionDelayTimer { get; set; }
    [Networked] private NetGameState nextStateAfterResolve { get; set; }
    [Networked] public PlayerRef kingPlayer { get; set; }
    [Networked] public PlayerRef chessPlayer { get; set; }

    public List<DeadPieceRecord> graveyard = new List<DeadPieceRecord>();
    public bool hasUsedPawnShieldThisTurn { get; set; } = false;

    private bool manualResolveInProgress;

    [Header("Settings")]
    [SerializeField] private float visualResolveTime = 1.5f;

    public override void Spawned()
    {
        if (Instance == null) Instance = this;
        else { Runner.Despawn(Object); return; }

        if (HasStateAuthority)
        {
            phase1TurnCount = 0;
            phase2TurnCount = 0;
            currentPhase = GamePhase.Phase1;
            ChangeState(NetGameState.Init);
        }
    }

    public void AssignRoles(PlayerRef p1, PlayerRef p2)
    {
        if (!HasStateAuthority) return;

        kingPlayer = p1;
        chessPlayer = p2;

        Debug.Log($"[Server] Assigned Roles - King: {p1}, Chess: {p2}");
    }

    public void SwapRoles()
    {
        if (!HasStateAuthority) return;

        PlayerRef temp = kingPlayer;
        kingPlayer = chessPlayer;
        chessPlayer = temp;

        Debug.Log($"[Server] Swapped Roles - King: {kingPlayer}, Chess: {chessPlayer}");
    }

    public bool CanPlayerAct(PlayerRef player)
    {
        if (currentGameState == NetGameState.KingTurn)
            return player == kingPlayer;

        if (currentGameState == NetGameState.ChessTurn)
            return player == chessPlayer;

        return false;
    }

    public bool IsKingPlayer(PlayerRef player)
    {
        return player == kingPlayer;
    }

    public bool IsChessPlayer(PlayerRef player)
    {
        return player == chessPlayer;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (currentGameState == NetGameState.ResolvingAction && !manualResolveInProgress && actionDelayTimer.Expired(Runner))
        {
            actionDelayTimer = TickTimer.None;
            ChangeState(nextStateAfterResolve);
        }
    }

    public void ChangeState(NetGameState newState)
    {
        if (!HasStateAuthority) return;

        if (newState != NetGameState.ResolvingAction)
            manualResolveInProgress = false;

        currentGameState = newState;
        Debug.Log($"[Server] State changed to -> {newState}");

        if (newState == NetGameState.Setup)
        {
            ServerBoardManager.Instance.SetupBoardFromData();
            ChangeState(NetGameState.KingTurn);
        }
        else if (newState == NetGameState.PhaseTransition)
        {
            currentPhase = GamePhase.Phase2;
            SwapRoles();

            ServerBoardManager.Instance.ClearBoard();

            ChangeState(NetGameState.Setup);
        }
    }

    public void EndTurn()
    {
        if (!HasStateAuthority) return;

        hasUsedPawnShieldThisTurn = false;
        manualResolveInProgress = false;

        if (currentGameState == NetGameState.KingTurn)
        {
            TriggerResolvePhase(NetGameState.ChessTurn);
        }
        else if (currentGameState == NetGameState.ChessTurn)
        {
            if (currentPhase == GamePhase.Phase1)
                phase1TurnCount++;
            else
                phase2TurnCount++;

            ServerBoardManager.Instance.TickTurnTimers(ChessFaction.ChessRogue);

            TriggerResolvePhase(NetGameState.KingTurn);
        }
    }


    public void BeginManualResolve(NetGameState nextState)
    {
        if (!HasStateAuthority) return;

        hasUsedPawnShieldThisTurn = false;
        manualResolveInProgress = true;
        actionDelayTimer = TickTimer.None;
        nextStateAfterResolve = nextState;
        ChangeState(NetGameState.ResolvingAction);
    }

    public void CompleteManualResolve()
    {
        if (!HasStateAuthority) return;
        if (currentGameState != NetGameState.ResolvingAction) return;

        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.None;
        ChangeState(nextStateAfterResolve);
    }

    private void TriggerResolvePhase(NetGameState nextState)
    {
        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.CreateFromSeconds(Runner, visualResolveTime);
        nextStateAfterResolve = nextState;

        ChangeState(NetGameState.ResolvingAction);
    }

    public void OnKingDefeated()
    {
        if (!HasStateAuthority) return;

        if (currentPhase == GamePhase.Phase1)
        {
            ChangeState(NetGameState.PhaseTransition);
        }
        else
        {
            ChangeState(NetGameState.GameOver);
            DetermineWinner();
        }
    }

    public void AbortMatchBecausePlayerLeft(PlayerRef player)
    {
        if (!HasStateAuthority) return;
        if (currentGameState == NetGameState.Init || currentGameState == NetGameState.GameOver) return;

        if (ServerCombatManager.Instance != null)
            ServerCombatManager.Instance.CancelPendingAttackResolution();

        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.None;
        nextStateAfterResolve = NetGameState.GameOver;
        currentGameState = NetGameState.GameOver;

        Debug.Log($"[Server] Match aborted because {player} left. Session is locked until the room is empty.");
    }

    public void ResetToLobby()
    {
        if (!HasStateAuthority) return;

        if (ServerCombatManager.Instance != null)
            ServerCombatManager.Instance.CancelPendingAttackResolution();

        if (ServerBoardManager.Instance != null)
            ServerBoardManager.Instance.ClearBoard();

        graveyard.Clear();
        hasUsedPawnShieldThisTurn = false;
        phase1TurnCount = 0;
        phase2TurnCount = 0;
        currentPhase = GamePhase.Phase1;
        kingPlayer = PlayerRef.None;
        chessPlayer = PlayerRef.None;
        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.None;
        nextStateAfterResolve = NetGameState.Init;
        currentGameState = NetGameState.Init;

        Debug.Log("[Server] Game state reset to lobby/init.");
    }

    private void DetermineWinner()
    {
        if (phase1TurnCount < phase2TurnCount)
        {
            Debug.Log("[Server] Player 1 (King in Phase 1) WINS!");
        }
        else if (phase2TurnCount < phase1TurnCount)
        {
            Debug.Log("[Server] Player 2 (King in Phase 2) WINS!");
        }
        else
        {
            Debug.Log("[Server] Match DRAW!");
        }
    }

    private void OnStateChanged() { }
    private void OnPhaseChanged() { }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }
}