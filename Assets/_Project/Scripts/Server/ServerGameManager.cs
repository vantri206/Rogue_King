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

        [Header("Phase Result")]
        [Tooltip("Winner of Phase 1. Set when Phase 1 ends either by King defeated or all Chess Alliance pieces defeated.")]
        [Networked] public PlayerRef phase1Winner { get; private set; }

        [Tooltip("Winner of Phase 2. Set when Phase 2 ends either by King defeated or all Chess Alliance pieces defeated.")]
        [Networked] public PlayerRef phase2Winner { get; private set; }

        [Header("Phase Transition Delay")]
        [Tooltip("Bật delay giữa Phase 1 và Phase 2 để client thấy kết quả phase trước khi board/role bị đổi.")]
        [SerializeField] private bool enablePhaseTransitionDelay = true;

        [Tooltip("Thời gian đếm ngược trước khi chuyển từ Phase 1 sang Phase 2.")]
        [SerializeField] private float phaseTransitionDelaySeconds = 5f;

        [Networked, OnChangedRender(nameof(OnPhaseTransitionChanged))]
        public TickTimer phaseTransitionTimer { get; private set; }

        [Networked] public int phaseTransitionDurationNetworkSeconds { get; private set; }
        [Networked] public PlayerRef phaseTransitionWinner { get; private set; }
        [Networked] public int phaseTransitionPhaseNumber { get; private set; }

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

        [Header("Match Result Return Flow")]
        [Tooltip("When the authoritative match reaches GameOver, keep clients in PlayScene long enough to see MatchResultUI, then kick/reopen the room.")]
        [SerializeField] private bool kickAllPlayersAfterGameOver = true;

        [Tooltip("Delay before the match server kicks both clients after GameOver. MatchResultUI should use the same value for its local countdown.")]
        [SerializeField] private float kickAllPlayersAfterGameOverDelaySeconds = 5f;

        public List<DeadPieceRecord> graveyard = new List<DeadPieceRecord>();

        [Header("Revive Graveyard Sync")]
        [Tooltip("Các slot sync vị trí quân Pawn/Knight/Bishop đã chết để client highlight ô hồi sinh cho KingRevive.")]
        [Networked, Capacity(64)] public NetworkArray<ReviveGraveyardEntry> ReviveGraveyardSlots { get; }

        public bool hasUsedPawnShieldThisTurn { get; set; } = false;

        private bool manualResolveInProgress;
        private bool matchResultRecorded;
        private bool matchResultAutoKickQueued;

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
                ClearGraveyard();
                matchResultRecorded = false;
                matchResultAutoKickQueued = false;
                ClearPhaseResultFields();
                ClearPhaseTransitionFields();
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
            matchResultAutoKickQueued = false;
            ClearPhaseResultFields();
            ClearPhaseTransitionFields();
            ClearMatchResultFields();
            ClearGraveyard();
            ClearPlayerWeaponCooldowns(p1);
            ClearPlayerWeaponCooldowns(p2);
            ClearTemporaryPlayerCardStates(p1);
            ClearTemporaryPlayerCardStates(p2);
            RebuildPlayerCardsForCurrentRole(p1);
            RebuildPlayerCardsForCurrentRole(p2);

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

        public bool IsPhaseTransitionActive()
        {
            return currentGameState == NetGameState.PhaseTransition;
        }

        public float GetPhaseTransitionRemainingSeconds()
        {
            if (Runner == null || !IsPhaseTransitionActive())
                return 0f;

            float? remaining = phaseTransitionTimer.RemainingTime(Runner);
            return Mathf.Max(0f, remaining ?? 0f);
        }

        public int GetPhaseTransitionDurationSeconds()
        {
            if (phaseTransitionDurationNetworkSeconds > 0)
                return phaseTransitionDurationNetworkSeconds;

            return Mathf.CeilToInt(Mathf.Max(0f, phaseTransitionDelaySeconds));
        }

        public static bool IsTurnState(NetGameState state)
        {
            return state == NetGameState.KingTurn || state == NetGameState.ChessTurn;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (currentGameState == NetGameState.PhaseTransition && phaseTransitionTimer.Expired(Runner))
            {
                CompletePhaseTransitionToPhase2();
                return;
            }

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

            if (newState == NetGameState.GameOver)
                QueueMatchResultAutoKickIfNeeded();

            if (newState == NetGameState.Setup)
            {
                ServerBoardManager.Instance.SetupBoardFromData();
                ChangeState(NetGameState.KingTurn);
            }
            else if (newState == NetGameState.PhaseTransition)
            {
                // PhaseTransition is now a real, delayed state.
                // Keep Phase 1 board/roles visible for a few seconds so clients can show
                // "You Win Phase 1" / "You Lose Phase 1" instead of snapping instantly to Phase 2.
                StopTurnTimer();
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

            PlayerRef endingPlayer = currentGameState == NetGameState.KingTurn ? kingPlayer : chessPlayer;

            StopTurnTimer();
            hasUsedPawnShieldThisTurn = false;
            manualResolveInProgress = false;
            RestorePlayerCardUseSilence(endingPlayer);

            if (currentGameState == NetGameState.KingTurn)
            {
                // SuperBuff của Rogue King tính theo số lượt KingTurn.
                // Dùng card trong lượt hiện tại sẽ được hưởng damage xN trong lượt này,
                // sau đó giảm 1 stack khi KingTurn kết thúc.
                if (ServerBoardManager.Instance != null)
                    ServerBoardManager.Instance.TickKingDamageBuffs(ChessFaction.ChessRogue);

                TickPlayerCardCooldowns(chessPlayer);
                TriggerResolvePhase(NetGameState.ChessTurn);
            }
            else if (currentGameState == NetGameState.ChessTurn)
            {
                if (currentPhase == GamePhase.Phase1)
                    phase1TurnCount++;
                else
                    phase2TurnCount++;

                // PawnForwardAttack is a one-turn Chess Alliance buff.
                // Clear it when the Chess turn ends, before giving turn back to Rogue King.
                if (ServerBoardManager.Instance != null)
                    ServerBoardManager.Instance.ClearPawnForwardAttackBuffs(ChessFaction.ChessAlliance);

                ServerBoardManager.Instance.TickTurnTimers(ChessFaction.ChessRogue);
                TickPlayerCardCooldowns(kingPlayer);
                TickPlayerWeaponCooldowns(kingPlayer);
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

        private void RestorePlayerCardUseSilence(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return;

            var playerObj = Runner != null ? Runner.GetPlayerObject(player) : null;
            var controller = playerObj != null ? playerObj.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                controller.RestoreCardUsesAfterOneTurnSilence();
        }

        private void TickPlayerWeaponCooldowns(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return;

            var playerObj = Runner.GetPlayerObject(player);
            if (playerObj == null)
                return;

            var controller = playerObj.GetComponent<PlayerNetworkController>();
            if (controller != null)
                controller.TickWeaponCooldowns();
        }

        private void ClearPlayerWeaponCooldowns(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return;

            var playerObj = Runner != null ? Runner.GetPlayerObject(player) : null;
            var controller = playerObj != null ? playerObj.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                controller.ClearWeaponCooldowns();
        }
        private void ClearTemporaryPlayerCardStates(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return;

            var playerObj = Runner != null ? Runner.GetPlayerObject(player) : null;
            var controller = playerObj != null ? playerObj.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                controller.ClearTemporaryCardState();
        }

        private void RebuildPlayerCardsForCurrentRole(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return;

            var playerObj = Runner != null ? Runner.GetPlayerObject(player) : null;
            var controller = playerObj != null ? playerObj.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                controller.ServerRebuildHandForCurrentRole(force: true);
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

        public void OnKingDefeated(ChessFaction defeatedKingFaction)
        {
            if (!HasStateAuthority) return;
            if (currentGameState == NetGameState.GameOver || currentGameState == NetGameState.PhaseTransition) return;

            if (defeatedKingFaction == ChessFaction.ChessRogue)
            {
                CompleteCurrentPhase(chessPlayer, "rogue_king_defeated");
            }
            else
            {
                CompleteCurrentPhase(kingPlayer, "chess_king_defeated");
            }
        }

        public void OnChessAllianceEliminated()
        {
            if (!HasStateAuthority) return;
            if (currentGameState == NetGameState.GameOver || currentGameState == NetGameState.PhaseTransition) return;

            // Tất cả quân Chess Alliance bị hạ => người đang cầm Rogue King thắng phase hiện tại.
            CompleteCurrentPhase(kingPlayer, "all_chess_alliance_defeated");
        }

        private void CompleteCurrentPhase(PlayerRef phaseWinner, string phaseReason)
        {
            if (!HasStateAuthority) return;
            if (phaseWinner == PlayerRef.None)
            {
                Debug.LogWarning($"[Server Result] Cannot complete phase because phaseWinner is None. Reason={phaseReason}");
                return;
            }

            StopTurnTimer();
            manualResolveInProgress = false;
            actionDelayTimer = TickTimer.None;

            if (currentPhase == GamePhase.Phase1)
            {
                phase1Winner = phaseWinner;
                Debug.Log($"[Server Result] Phase 1 ended. Winner={phase1Winner}, Reason={phaseReason}, Phase1Turns={phase1TurnCount}");
                BeginPhaseTransitionToPhase2(phaseWinner, phaseReason);
                return;
            }

            phase2Winner = phaseWinner;
            Debug.Log($"[Server Result] Phase 2 ended. Winner={phase2Winner}, Reason={phaseReason}, Phase2Turns={phase2TurnCount}");

            DetermineMatchWinnerFromPhaseResults(phaseReason);
            ChangeState(NetGameState.GameOver);
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
            QueueMatchResultAutoKickIfNeeded();

            Debug.Log($"[Server] Match aborted because {player} left. Winner by forfeit={winner}. Session is locked until the room is empty.");
        }

        public void ResetToLobby()
        {
            if (!HasStateAuthority) return;

            if (ServerCombatManager.Instance != null)
                ServerCombatManager.Instance.CancelPendingAttackResolution();

            if (ServerBoardManager.Instance != null)
                ServerBoardManager.Instance.ClearBoard();

            ClearGraveyard();
            hasUsedPawnShieldThisTurn = false;
            ClearTemporaryPlayerCardStates(kingPlayer);
            ClearTemporaryPlayerCardStates(chessPlayer);
            phase1TurnCount = 0;
            phase2TurnCount = 0;
            currentPhase = GamePhase.Phase1;
            matchResultRecorded = false;
            matchResultAutoKickQueued = false;
            ClearPhaseResultFields();
            ClearPhaseTransitionFields();
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

        private void BeginPhaseTransitionToPhase2(PlayerRef phaseWinner, string phaseReason)
        {
            if (!HasStateAuthority) return;

            phaseTransitionWinner = phaseWinner;
            phaseTransitionPhaseNumber = 1;

            float delay = enablePhaseTransitionDelay ? Mathf.Max(0f, phaseTransitionDelaySeconds) : 0f;
            phaseTransitionDurationNetworkSeconds = Mathf.CeilToInt(delay);

            if (delay > 0f)
                phaseTransitionTimer = TickTimer.CreateFromSeconds(Runner, delay);
            else
                phaseTransitionTimer = TickTimer.None;

            Debug.Log($"[Server PhaseTransition] Phase 1 result window started. Winner={phaseWinner}, Reason={phaseReason}, Delay={delay:0.0}s");

            ChangeState(NetGameState.PhaseTransition);

            if (delay <= 0f)
                CompletePhaseTransitionToPhase2();
        }

        private void CompletePhaseTransitionToPhase2()
        {
            if (!HasStateAuthority) return;
            if (currentGameState != NetGameState.PhaseTransition) return;

            phaseTransitionTimer = TickTimer.None;
            phaseTransitionDurationNetworkSeconds = 0;

            currentPhase = GamePhase.Phase2;
            SwapRoles();
            ClearGraveyard();

            ClearTemporaryPlayerCardStates(kingPlayer);
            ClearTemporaryPlayerCardStates(chessPlayer);
            RebuildPlayerCardsForCurrentRole(kingPlayer);
            RebuildPlayerCardsForCurrentRole(chessPlayer);

            if (ServerBoardManager.Instance != null)
                ServerBoardManager.Instance.ClearBoard();

            ClearPhaseTransitionFields();

            Debug.Log("[Server PhaseTransition] Countdown finished. Switching to Phase 2 setup.");
            ChangeState(NetGameState.Setup);
        }

        private void DetermineMatchWinnerFromPhaseResults(string finalPhaseReason)
        {
            if (phase1Winner == PlayerRef.None || phase2Winner == PlayerRef.None)
            {
                Debug.LogWarning($"[Server Result] Missing phase winner. Phase1Winner={phase1Winner}, Phase2Winner={phase2Winner}. Recording draw fallback.");
                RecordDrawResult("game_over_missing_phase_result");
                return;
            }

            if (phase1Winner == phase2Winner)
            {
                PlayerRef winner = phase1Winner;
                PlayerRef loser = GetOpponentOf(winner);

                if (loser == PlayerRef.None)
                {
                    Debug.LogWarning($"[Server Result] Cannot resolve loser for sweep winner={winner}. Recording draw fallback.");
                    RecordDrawResult("game_over_invalid_sweep");
                    return;
                }

                Debug.Log($"[Server Result] Match winner by winning both phases. Winner={winner}, Loser={loser}, FinalReason={finalPhaseReason}");
                RecordMatchResult(winner, loser, "game_over_two_phase_win");
                return;
            }

            DetermineWinnerBySplitPhaseScore();
        }

        private void DetermineWinnerBySplitPhaseScore()
        {
            // Nếu mỗi player thắng 1 phase, dùng số lượt của phase thắng đó làm tie-break.
            // Phase nào được kết thúc với ít lượt hơn => winner của phase đó thắng match.
            // Nếu bằng lượt => Draw, Elo +0.
            if (phase1TurnCount < phase2TurnCount)
            {
                PlayerRef winner = phase1Winner;
                PlayerRef loser = GetOpponentOf(winner);
                Debug.Log($"[Server Result] Match winner by split-phase score. Phase1 faster ({phase1TurnCount} < {phase2TurnCount}). Winner={winner}, Loser={loser}");
                RecordMatchResult(winner, loser, "game_over_split_phase_score");
            }
            else if (phase2TurnCount < phase1TurnCount)
            {
                PlayerRef winner = phase2Winner;
                PlayerRef loser = GetOpponentOf(winner);
                Debug.Log($"[Server Result] Match winner by split-phase score. Phase2 faster ({phase2TurnCount} < {phase1TurnCount}). Winner={winner}, Loser={loser}");
                RecordMatchResult(winner, loser, "game_over_split_phase_score");
            }
            else
            {
                Debug.Log($"[Server Result] Match DRAW by split-phase score. Phase1Turns={phase1TurnCount}, Phase2Turns={phase2TurnCount}. Elo is unchanged.");
                RecordDrawResult("game_over_split_phase_draw");
            }
        }

        private PlayerRef GetOpponentOf(PlayerRef player)
        {
            if (player == PlayerRef.None)
                return PlayerRef.None;

            if (player == kingPlayer)
                return chessPlayer;

            if (player == chessPlayer)
                return kingPlayer;

            if (player == phase1Winner && phase2Winner != PlayerRef.None && phase2Winner != player)
                return phase2Winner;

            if (player == phase2Winner && phase1Winner != PlayerRef.None && phase1Winner != player)
                return phase1Winner;

            return PlayerRef.None;
        }

        private void ClearPhaseResultFields()
        {
            phase1Winner = PlayerRef.None;
            phase2Winner = PlayerRef.None;
        }

        private void ClearPhaseTransitionFields()
        {
            phaseTransitionTimer = TickTimer.None;
            phaseTransitionDurationNetworkSeconds = 0;
            phaseTransitionWinner = PlayerRef.None;
            phaseTransitionPhaseNumber = 0;
        }

        public void AddDeadPieceToGraveyard(ChessPieceData pieceData, ChessFaction faction, Vector2Int deathPos)
        {
            if (!HasStateAuthority || pieceData == null)
                return;

            graveyard.Add(new DeadPieceRecord
            {
                pieceData = pieceData,
                faction = faction,
                deathPos = deathPos
            });

            RebuildReviveGraveyardSlots();
        }

        public void RemoveDeadPieceRecord(DeadPieceRecord record)
        {
            if (!HasStateAuthority || record == null)
                return;

            graveyard.Remove(record);
            RebuildReviveGraveyardSlots();
        }

        public void ClearGraveyard()
        {
            if (!HasStateAuthority)
                return;

            graveyard.Clear();
            RebuildReviveGraveyardSlots();
        }

        public void RebuildReviveGraveyardSlots()
        {
            if (!HasStateAuthority)
                return;

            int capacity = ReviveGraveyardSlots.Length;
            for (int i = 0; i < capacity; i++)
                ReviveGraveyardSlots.Set(i, default);

            if (graveyard == null || ServerBoardManager.Instance == null)
                return;

            int slot = 0;
            for (int i = 0; i < graveyard.Count && slot < capacity; i++)
            {
                DeadPieceRecord record = graveyard[i];
                if (!IsKingReviveCandidateRecord(record))
                    continue;

                int pieceDataIndex = ServerBoardManager.Instance.GetPieceDataIndex(record.pieceData);
                if (pieceDataIndex < 0)
                    continue;

                ReviveGraveyardSlots.Set(slot, new ReviveGraveyardEntry
                {
                    isActive = true,
                    pieceDataIndex = pieceDataIndex,
                    faction = record.faction,
                    deathX = record.deathPos.x,
                    deathY = record.deathPos.y
                });
                slot++;
            }
        }

        public static bool IsKingReviveCandidateRecord(DeadPieceRecord record)
        {
            if (record == null || record.pieceData == null)
                return false;

            return IsKingReviveCandidatePieceName(record.pieceData.pieceName);
        }

        public static bool IsKingReviveCandidatePieceName(string pieceName)
        {
            if (string.IsNullOrWhiteSpace(pieceName))
                return false;

            return pieceName.Contains("Pawn") || pieceName.Contains("Knight") || pieceName.Contains("Bishop");
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

        private void QueueMatchResultAutoKickIfNeeded()
        {
            if (!HasStateAuthority) return;
            if (!kickAllPlayersAfterGameOver) return;
            if (matchResultAutoKickQueued) return;

            matchResultAutoKickQueued = true;

            NetworkRunnerHandler handler = NetworkRunnerHandler.Active;
            if (handler == null)
            {
                Debug.LogWarning("[Server Result] Cannot schedule post-result Kick All because NetworkRunnerHandler.Active is missing.");
                return;
            }

            float delay = Mathf.Max(0f, kickAllPlayersAfterGameOverDelaySeconds);
            bool queued = handler.ServerKickAllPlayersAndReopenAfterDelay(delay, "match_result_auto_kick");
            if (queued)
                Debug.Log($"[Server Result] MatchResultUI window opened. Server will kick/reopen room in {delay:0.0}s.");
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
        private void OnPhaseTransitionChanged() { }
        private void OnMatchResultChanged() { }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }
    }
