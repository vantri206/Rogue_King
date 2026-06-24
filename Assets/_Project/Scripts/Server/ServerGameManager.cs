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

    [Header("Turn Timer")]
    [Tooltip("Bật đồng hồ mỗi lượt. Server là nguồn sự thật; client chỉ đọc remaining time để hiển thị mm:ss.")]
    [SerializeField] private bool enableTurnTimer = true;

    [Tooltip("Thời gian mỗi lượt, tính bằng giây. Hết giờ thì server tự EndTurn, tức người chơi mất lượt.")]
    [SerializeField] private float turnDurationSeconds = 60f;

    [Networked, OnChangedRender(nameof(OnTurnTimerChanged))]
    public TickTimer turnTimer { get; private set; }

    [Networked] public int turnDurationNetworkSeconds { get; private set; }

    [Header("Match Result")]
    [Networked, OnChangedRender(nameof(OnMatchResultChanged))]
    public PlayerRef winnerPlayer { get; private set; }

    [Networked] public PlayerRef loserPlayer { get; private set; }
    [Networked] public NetworkString<_32> matchEndReason { get; private set; }
    [Networked] public int matchResultSerial { get; private set; }

    public List<DeadPieceRecord> graveyard = new List<DeadPieceRecord>();
    public bool hasUsedPawnShieldThisTurn { get; set; } = false;

    private bool manualResolveInProgress;
    private bool matchResultRecorded;

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
            matchResultRecorded = false;
            turnTimer = TickTimer.None;
            turnDurationNetworkSeconds = Mathf.CeilToInt(Mathf.Max(1f, turnDurationSeconds));
            ClearMatchResultFields();
            ChangeState(NetGameState.Init);
        }
    }

    public void AssignRoles(PlayerRef p1, PlayerRef p2)
    {
        if (!HasStateAuthority) return;

        kingPlayer = p1;
        chessPlayer = p2;
        matchResultRecorded = false;
        ClearMatchResultFields();

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

    public bool IsTurnTimerActive()
    {
        return enableTurnTimer && IsTurnState(currentGameState);
    }

    public float GetTurnRemainingSeconds()
    {
        if (Runner == null || !IsTurnTimerActive())
            return 0f;

        float? remaining = turnTimer.RemainingTime(Runner);
        return Mathf.Max(0f, remaining ?? 0f);
    }

    public int GetTurnDurationSeconds()
    {
        return Mathf.Max(1, turnDurationNetworkSeconds > 0 ? turnDurationNetworkSeconds : Mathf.CeilToInt(turnDurationSeconds));
    }

    public static bool IsTurnState(NetGameState state)
    {
        return state == NetGameState.KingTurn || state == NetGameState.ChessTurn;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (currentGameState == NetGameState.ResolvingAction && !manualResolveInProgress && actionDelayTimer.Expired(Runner))
        {
            actionDelayTimer = TickTimer.None;
            ChangeState(nextStateAfterResolve);
        }

        TickTurnTimer();
    }

    private void TickTurnTimer()
    {
        if (!enableTurnTimer) return;
        if (!IsTurnState(currentGameState)) return;
        if (!turnTimer.Expired(Runner)) return;

        PlayerRef timeoutPlayer = currentGameState == NetGameState.KingTurn ? kingPlayer : chessPlayer;
        Debug.Log($"[Server Timer] Player {timeoutPlayer} timed out during {currentGameState}. Turn is forfeited.");

        // Hết giờ = mất lượt. Không gây damage, không tính thua match.
        EndTurn();
    }

    public void ChangeState(NetGameState newState)
    {
        if (!HasStateAuthority) return;

        if (newState != NetGameState.ResolvingAction)
            manualResolveInProgress = false;

        currentGameState = newState;
        Debug.Log($"[Server] State changed to -> {newState}");

        if (IsTurnState(newState))
            StartTurnTimer(newState);
        else
            StopTurnTimer();

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

    private void StartTurnTimer(NetGameState state)
    {
        if (!enableTurnTimer)
        {
            turnTimer = TickTimer.None;
            return;
        }

        float duration = Mathf.Max(1f, turnDurationSeconds);
        turnDurationNetworkSeconds = Mathf.CeilToInt(duration);
        turnTimer = TickTimer.CreateFromSeconds(Runner, duration);

        PlayerRef activePlayer = state == NetGameState.KingTurn ? kingPlayer : chessPlayer;
        Debug.Log($"[Server Timer] Started {duration:0.0}s turn timer for {state}. ActivePlayer={activePlayer}");
    }

    private void StopTurnTimer()
    {
        turnTimer = TickTimer.None;
    }

    public void EndTurn()
    {
        if (!HasStateAuthority) return;
        if (!IsTurnState(currentGameState)) return;

        StopTurnTimer();
        hasUsedPawnShieldThisTurn = false;
        manualResolveInProgress = false;

        if (currentGameState == NetGameState.KingTurn)
        {
            TickPlayerCardCooldowns(chessPlayer);
            TriggerResolvePhase(NetGameState.ChessTurn);
        }
        else if (currentGameState == NetGameState.ChessTurn)
        {
            if (currentPhase == GamePhase.Phase1)
                phase1TurnCount++;
            else
                phase2TurnCount++;

            ServerBoardManager.Instance.TickTurnTimers(ChessFaction.ChessRogue);
            TickPlayerCardCooldowns(kingPlayer);
            TriggerResolvePhase(NetGameState.KingTurn);
        }
    }

    private void TickPlayerCardCooldowns(PlayerRef player)
    {
        if (player != PlayerRef.None)
        {
            var playerObj = Runner.GetPlayerObject(player);
            if (playerObj != null)
            {
                var controller = playerObj.GetComponent<PlayerNetworkController>();
                if (controller != null) controller.TickCardCooldowns();
            }
        }
    }

    public void BeginManualResolve(NetGameState nextState)
    {
        if (!HasStateAuthority) return;

        StopTurnTimer();
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
        StopTurnTimer();
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
            DetermineWinner();
            ChangeState(NetGameState.GameOver);
        }
    }

    public void AbortMatchBecausePlayerLeft(PlayerRef player)
    {
        if (!HasStateAuthority) return;
        if (currentGameState == NetGameState.Init || currentGameState == NetGameState.GameOver) return;

        if (ServerCombatManager.Instance != null)
            ServerCombatManager.Instance.CancelPendingAttackResolution();

        PlayerRef winner = PlayerRef.None;
        if (player == kingPlayer)
            winner = chessPlayer;
        else if (player == chessPlayer)
            winner = kingPlayer;

        if (winner != PlayerRef.None)
            RecordMatchResult(winner, player, "forfeit_disconnect");

        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.None;
        StopTurnTimer();
        nextStateAfterResolve = NetGameState.GameOver;
        currentGameState = NetGameState.GameOver;

        Debug.Log($"[Server] Match aborted because {player} left. Winner by forfeit={winner}. Session is locked until the room is empty.");
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
        matchResultRecorded = false;
        kingPlayer = PlayerRef.None;
        chessPlayer = PlayerRef.None;
        manualResolveInProgress = false;
        actionDelayTimer = TickTimer.None;
        turnTimer = TickTimer.None;
        turnDurationNetworkSeconds = Mathf.CeilToInt(Mathf.Max(1f, turnDurationSeconds));
        nextStateAfterResolve = NetGameState.Init;
        ClearMatchResultFields();
        currentGameState = NetGameState.Init;

        Debug.Log("[Server] Game state reset to lobby/init.");
    }

    private void DetermineWinner()
    {
        // At Phase 2, roles have already been swapped:
        // - current chessPlayer was the original Phase 1 King.
        // - current kingPlayer is the Phase 2 King.
        if (phase1TurnCount < phase2TurnCount)
        {
            PlayerRef winner = chessPlayer;
            PlayerRef loser = kingPlayer;
            Debug.Log($"[Server] Player 1 / Phase 1 King WINS! Winner={winner}, Loser={loser}");
            RecordMatchResult(winner, loser, "game_over_phase_score");
        }
        else if (phase2TurnCount < phase1TurnCount)
        {
            PlayerRef winner = kingPlayer;
            PlayerRef loser = chessPlayer;
            Debug.Log($"[Server] Player 2 / Phase 2 King WINS! Winner={winner}, Loser={loser}");
            RecordMatchResult(winner, loser, "game_over_phase_score");
        }
        else
        {
            Debug.Log("[Server] Match DRAW! Elo is unchanged in this patch.");
            RecordDrawResult("game_over_phase_draw");
        }
    }

    private void RecordMatchResult(PlayerRef winner, PlayerRef loser, string reason)
    {
        if (!HasStateAuthority) return;
        if (matchResultRecorded) return;
        if (winner == PlayerRef.None || loser == PlayerRef.None || winner == loser) return;

        matchResultRecorded = true;
        winnerPlayer = winner;
        loserPlayer = loser;
        matchEndReason = SanitizeReason(reason);
        matchResultSerial++;

        if (ServerLeaderboardManager.Instance != null)
            ServerLeaderboardManager.Instance.ApplyMatchResult(winner, loser, reason);
        else
            Debug.LogWarning("[Server] ServerLeaderboardManager missing. Match result was not saved to leaderboard.json.");
    }

    private void RecordDrawResult(string reason)
    {
        if (!HasStateAuthority) return;
        if (matchResultRecorded) return;

        matchResultRecorded = true;
        winnerPlayer = PlayerRef.None;
        loserPlayer = PlayerRef.None;
        matchEndReason = SanitizeReason(reason);
        matchResultSerial++;
    }

    private void ClearMatchResultFields()
    {
        winnerPlayer = PlayerRef.None;
        loserPlayer = PlayerRef.None;
        matchEndReason = string.Empty;
        matchResultSerial++;
    }

    private static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "match_result";

        reason = reason.Trim().Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        return reason.Length > 32 ? reason.Substring(0, 32) : reason;
    }

    private void OnStateChanged() { }
    private void OnPhaseChanged() { }
    private void OnTurnTimerChanged() { }
    private void OnMatchResultChanged() { }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }
}
