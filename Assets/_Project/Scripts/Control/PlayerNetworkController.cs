using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;

public class PlayerNetworkController : NetworkBehaviour
{
    private enum ClientInputState
    {
        Idle,
        DraggingPiece,
        AimingAttack,
        ConfirmingAttack,
        Animating
    }

    [Header("Scene References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private ChessBoard chessBoard;
    [SerializeField] private ChessControl chessControl;

    [Header("Drag Visual")]
    [SerializeField] private GhostPiece ghostPiece;
    [SerializeField] private float ghostSnapDuration = 0.2f;
    [SerializeField] private Vector3 ghostDragOffset = new Vector3(0f, 0.6f, -1f);

    [Header("Rogue Weapon UI")]
    [SerializeField] private WeaponControllerUI weaponUI;
    [SerializeField] private List<WeaponData> equippedWeapons = new List<WeaponData>();
    [SerializeField] private int currentSelectedWeaponIndex = 0;

    [Header("Piece Context UI")]
    [SerializeField] private PieceContextUI pieceContextUI;

    [Header("Player Profile")]
    [Tooltip("AvatarId is assigned by the server from GuestId. Keep this synced with AvatarCatalog size - 1.")]
    [SerializeField] private int maxAvatarId = 7;

    [Networked] public NetworkString<_64> GuestId { get; set; }
    [Networked] public NetworkString<_32> DisplayName { get; set; }
    [Networked] public int AvatarId { get; set; }
    [Networked] public int Elo { get; set; }
    [Networked] public int LastEloDelta { get; set; }
    [Networked] public NetworkBool IsProfileReady { get; set; }

    [Header("Debug")]
    [SerializeField] private bool debugInputLogs = true;

    [Header("Multiplayer Flow")]
    [Tooltip("Fallback only: if no MatchResultUI exists in PlayScene, leave PlayScene and return to MenuScene automatically when GameOver is reached.")]
    [SerializeField] private bool autoReturnToMenuOnGameOver = true;

    [Tooltip("Fallback delay used only when no MatchResultUI exists in PlayScene.")]
    [SerializeField] private float autoReturnToMenuAfterGameOverDelaySeconds = 0.75f;

    private static PlayerNetworkController activeLocalInputController;
    private bool localInputEnabled;
    private bool localProfileSubmitted;
    private bool gameOverReturnQueued;

    private ClientInputState currentState = ClientInputState.Idle;

    private NetworkChessPiece selectedPiece;
    private Vector2Int selectedFromPos = new Vector2Int(-1, -1);

    private readonly List<Vector2Int> currentValidMoves = new List<Vector2Int>();
    private readonly List<Vector2Int> currentValidAttacks = new List<Vector2Int>();
    private readonly List<Vector2Int> currentAoETiles = new List<Vector2Int>();

    private Vector2Int lockedAttackTarget = new Vector2Int(-1, -1);
    private BoardTile lastHoveredTile;
    private NetworkChessPiece currentHoveredNetworkPieceForUI;

    private bool weaponUIInitialized;
    private bool weaponUISubscribed;
    private bool weaponSlotsConfigured;
    private int configuredWeaponCount = -1;
    private bool lastWeaponPanelState;
    private NetGameState lastObservedGameState = NetGameState.Init;
    private PlayerRef lastObservedKingPlayer;
    private PlayerRef lastObservedChessPlayer;

    private bool attackRequestPending;
    private float attackRequestPendingStartedTime;
    private const float AttackRequestFallbackUnlockSeconds = 3f;

    public override void Spawned()
    {
        gameOverReturnQueued = false;

        // Dedicated Server thường có StateAuthority nhưng không có InputAuthority local.
        // Vì vậy phải phát bài trước khi kiểm tra TryAcquireLocalInput(), nếu không server sẽ return sớm
        // và HandCards sẽ không bao giờ được initialize cho client.
        if (HasStateAuthority)
        {
            TryInitializeDeckOnServer();
        }

        if (!TryAcquireLocalInput())
            return;

        TrySubmitLocalProfileToServer();
        ResolveSceneReferences();
        InitializeWeaponUIIfPossible();

        if (ghostPiece != null)
            ghostPiece.Hide();

        if (pieceContextUI != null)
            pieceContextUI.Hide();

        if (debugInputLogs)
        {
            Debug.Log($"[Client Input] PlayerNetworkController ready. MyInputAuthority={Object.InputAuthority}, NetworkObject={Object.Id}");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ReleaseLocalInput();
        UnsubscribeWeaponUI();
    }

    private void OnDestroy()
    {
        ReleaseLocalInput();
        UnsubscribeWeaponUI();
    }

    private bool TryAcquireLocalInput()
    {
        if (!HasInputAuthority)
        {
            localInputEnabled = false;
            return false;
        }

        if (activeLocalInputController != null && activeLocalInputController != this)
        {
            localInputEnabled = false;
            UnsubscribeWeaponUI();

            if (debugInputLogs)
            {
                Debug.LogWarning($"[Client Input] Duplicate local PlayerNetworkController disabled. Active={activeLocalInputController.Object?.Id}, Duplicate={Object?.Id}, InputAuthority={Object.InputAuthority}");
            }

            enabled = false;
            return false;
        }

        activeLocalInputController = this;
        localInputEnabled = true;
        return true;
    }

    private void ReleaseLocalInput()
    {
        if (activeLocalInputController == this)
            activeLocalInputController = null;

        localInputEnabled = false;
        localProfileSubmitted = false;
    }

    private bool IsLocalInputActive()
    {
        return HasInputAuthority && localInputEnabled && activeLocalInputController == this;
    }

    private void TrySubmitLocalProfileToServer()
    {
        if (!HasInputAuthority || localProfileSubmitted)
            return;

        PlayerLocalProfile profile = PlayerLocalProfile.LoadOrCreate(maxAvatarId + 1);
        // AvatarId is intentionally not trusted. The server assigns it from GuestId.
        Rpc_SubmitPlayerProfile(profile.GuestId, profile.DisplayName, 0);
        localProfileSubmitted = true;

        if (debugInputLogs)
        {
            Debug.Log($"[Client Profile] Submitted profile GuestId={profile.GuestId}, Name={profile.DisplayName}. Avatar will be assigned by server.");
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void Rpc_SubmitPlayerProfile(NetworkString<_64> guestId, NetworkString<_32> displayName, int avatarId, RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        string sanitizedGuestId = SanitizeGuestId(guestId.ToString(), info.Source);
        string sanitizedName = SanitizeDisplayName(displayName.ToString(), info.Source);
        int safeAvatarId = GetServerAssignedAvatarId(sanitizedGuestId);
        int safeElo = 1000;

        if (ServerLeaderboardManager.Instance != null)
        {
            LeaderboardEntryData entry = ServerLeaderboardManager.Instance.RegisterOrUpdatePlayer(info.Source, sanitizedGuestId, sanitizedName, safeAvatarId);
            if (entry != null)
            {
                sanitizedGuestId = entry.guestId;
                sanitizedName = entry.displayName;
                safeAvatarId = entry.avatarId;
                safeElo = entry.elo;
            }
        }
        else
        {
            sanitizedName = ResolveFallbackServerName(sanitizedName, sanitizedGuestId, info.Source);
        }

        GuestId = sanitizedGuestId;
        DisplayName = sanitizedName;
        AvatarId = safeAvatarId;
        Elo = Mathf.Max(0, safeElo);
        LastEloDelta = 0;
        IsProfileReady = true;

        if (ServerLeaderboardManager.Instance != null)
            ServerLeaderboardManager.Instance.PushLeaderboardToAllActivePlayers();

        if (debugInputLogs)
        {
            Debug.Log($"[Server Profile] Player={info.Source} GuestId={sanitizedGuestId}, Name={sanitizedName}, ServerAvatarId={safeAvatarId}, Elo={Elo}");
        }
    }

    public void ServerSetElo(int elo, int delta)
    {
        if (!HasStateAuthority)
            return;

        Elo = Mathf.Max(0, elo);
        LastEloDelta = delta;
    }

    public void ServerPushLeaderboardSnapshot(IReadOnlyList<LeaderboardEntryData> topEntries)
    {
        if (!HasStateAuthority || topEntries == null)
            return;

        Rpc_ClearLeaderboardSnapshot();

        int count = Mathf.Min(10, topEntries.Count);
        for (int i = 0; i < count; i++)
        {
            LeaderboardEntryData entry = topEntries[i];
            if (entry == null)
                continue;

            string safeName = string.IsNullOrWhiteSpace(entry.displayName) ? $"Player {i + 1:00}" : entry.displayName;
            if (safeName.Length > 24)
                safeName = safeName.Substring(0, 24);

            string safeGuestId = string.IsNullOrWhiteSpace(entry.guestId) ? $"rank_{i + 1}" : entry.guestId;
            if (safeGuestId.Length > 64)
                safeGuestId = safeGuestId.Substring(0, 64);

            Rpc_ReceiveLeaderboardEntry(
                i + 1,
                safeGuestId,
                safeName,
                Mathf.Max(0, entry.avatarId),
                Mathf.Max(0, entry.elo),
                Mathf.Max(0, entry.wins),
                Mathf.Max(0, entry.losses),
                Mathf.Max(0, entry.draws),
                Mathf.Max(0, entry.totalMatches));
        }

        Rpc_CompleteLeaderboardSnapshot();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_ClearLeaderboardSnapshot()
    {
        ClientLeaderboardCache.BeginSnapshot();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_ReceiveLeaderboardEntry(int rank, NetworkString<_64> guestId, NetworkString<_32> displayName, int avatarId, int elo, int wins, int losses, int draws, int totalMatches)
    {
        ClientLeaderboardCache.AddOrUpdateEntry(rank, guestId.ToString(), displayName.ToString(), avatarId, elo, wins, losses, draws, totalMatches);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_CompleteLeaderboardSnapshot()
    {
        ClientLeaderboardCache.CompleteSnapshotAndSave();
    }


    public void ClientRequestFindMatchFromLobby()
    {
        if (!HasInputAuthority)
            return;

        Rpc_RequestFindMatchFromLobby();
    }

    public void ClientRequestLeaderboardRefresh()
    {
        if (!HasInputAuthority)
            return;

        Rpc_RequestLeaderboardRefresh();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestFindMatchFromLobby(RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerRequestedLobbyMatch(info.Source);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestLeaderboardRefresh(RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerRequestedLeaderboardRefresh(info.Source);
    }

    public void ServerSendLobbyMatchFound(string matchSessionName)
    {
        if (!HasStateAuthority)
            return;

        string safeSession = string.IsNullOrWhiteSpace(matchSessionName) ? "RogueKingRoom" : matchSessionName.Trim();
        if (safeSession.Length > 32)
            safeSession = safeSession.Substring(0, 32);

        Rpc_LobbyMatchFound(safeSession);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_LobbyMatchFound(NetworkString<_32> matchSessionName)
    {
        string session = matchSessionName.ToString();
        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ClientSwitchFromLobbyToMatchSession(session);
    }

    private static string ResolveFallbackServerName(string sanitizedName, string guestId, PlayerRef source)
    {
        if (!string.IsNullOrWhiteSpace(sanitizedName) && !LooksLikeGeneratedName(sanitizedName))
            return sanitizedName;

        int number = 1000 + (GetStablePositiveHash(string.IsNullOrWhiteSpace(guestId) ? $"player_{source.PlayerId}" : guestId) % 9000);
        return $"Player {number}";
    }

    private static bool LooksLikeGeneratedName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();
        return value.StartsWith("Guest_", System.StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Guest ", System.StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Player_", System.StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Player ", System.StringComparison.OrdinalIgnoreCase);
    }

    private static int GetStablePositiveHash(string value)
    {
        unchecked
        {
            int hash = 23;
            if (string.IsNullOrEmpty(value))
                value = "kingonline";

            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];

            return hash & int.MaxValue;
        }
    }

    private int GetServerAssignedAvatarId(string guestId)
    {
        int avatarCount = Mathf.Max(1, maxAvatarId + 1);

        if (string.IsNullOrWhiteSpace(guestId))
            return 0;

        unchecked
        {
            int hash = 23;
            for (int i = 0; i < guestId.Length; i++)
                hash = hash * 31 + guestId[i];

            return (hash & int.MaxValue) % avatarCount;
        }
    }

    private static string SanitizeGuestId(string rawGuestId, PlayerRef source)
    {
        string value = string.IsNullOrWhiteSpace(rawGuestId)
            ? $"guest_{source.PlayerId}"
            : rawGuestId.Trim();

        value = value.Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty);

        if (value.Length > 64)
            value = value.Substring(0, 64);

        if (string.IsNullOrWhiteSpace(value))
            value = $"guest_{source.PlayerId}";

        return value;
    }

    private static string SanitizeDisplayName(string rawDisplayName, PlayerRef source)
    {
        string value = string.IsNullOrWhiteSpace(rawDisplayName)
            ? $"Guest_{source.PlayerId:0000}"
            : rawDisplayName.Trim();

        value = value.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

        while (value.Contains("  "))
            value = value.Replace("  ", " ");

        if (value.Length > 24)
            value = value.Substring(0, 24);

        if (string.IsNullOrWhiteSpace(value))
            value = $"Guest_{source.PlayerId:0000}";

        return value;
    }

    public string GetGuestIdOrFallback()
    {
        string value = GuestId.ToString();
        return string.IsNullOrWhiteSpace(value) ? $"guest_{Object.InputAuthority.PlayerId}" : value;
    }

    public string GetDisplayNameOrFallback()
    {
        string value = DisplayName.ToString();
        return string.IsNullOrWhiteSpace(value) ? $"Player {Object.InputAuthority.PlayerId}" : value;
    }

    public int GetAvatarIdOrDefault()
    {
        return Mathf.Max(0, AvatarId);
    }

    public int GetEloOrDefault()
    {
        return Elo > 0 ? Elo : 1000;
    }

    public int GetLastEloDelta()
    {
        return LastEloDelta;
    }

    private void Update()
    {
        if (!IsLocalInputActive()) return;

        TrySubmitLocalProfileToServer();
        ResolveSceneReferences();
        InitializeWeaponUIIfPossible();
        UpdateLocalRoleAndTurnUI();

        if (ServerGameManager.Instance != null && ServerGameManager.Instance.currentGameState == NetGameState.GameOver)
        {
            QueueAutoReturnToMenuAfterGameOver();

            if (currentState != ClientInputState.Idle)
                CancelCurrentInteraction();

            ToggleWeaponPanel(false);
            return;
        }

        if (ServerGameManager.Instance != null && ServerGameManager.Instance.currentGameState == NetGameState.PhaseTransition)
        {
            // During the Phase 1 -> Phase 2 result countdown, the board must be fully locked.
            // This prevents dragging/aiming/weapon UI from staying alive while the server is waiting
            // to swap roles and rebuild the board for Phase 2.
            if (currentState != ClientInputState.Idle)
                CancelCurrentInteraction();
            else
            {
                HidePieceContextUI();
                ClearAllHighlights();
            }

            ToggleWeaponPanel(false);
            return;
        }

        switch (currentState)
        {
            case ClientInputState.Idle:
                UpdateIdleHover();
                break;
            case ClientInputState.DraggingPiece:
                UpdateDragVisuals();
                break;
            case ClientInputState.AimingAttack:
            case ClientInputState.ConfirmingAttack:
                UpdateAimingHover();
                break;
        }

        PollMouseInput();
    }

    public override void FixedUpdateNetwork()
    {
        // Do not read Unity input here: Fusion can resimulate FixedUpdateNetwork ticks.
        // Server chỉ dùng hàm này để retry phát bài nếu ServerCardManager spawn sau PlayerNetworkController.
        if (HasStateAuthority && !deckInitializedOnServer)
        {
            TryInitializeDeckOnServer();
        }
    }

    private struct NetworkPieceSnapshot
    {
        public Vector2Int GridPos;
        public int CurrentHp;
        public int CurrentSkillCooldown;
        public int SilencedTurnsLeft;
        public int PieceDataIndex;
        public ChessFaction Faction;
        public bool IsKing;
    }

    private bool TryGetNetworkPieceSnapshot(NetworkChessPiece piece, out NetworkPieceSnapshot snapshot)
    {
        snapshot = default;

        if (piece == null || !piece.isActiveAndEnabled)
            return false;

        try
        {
            snapshot.GridPos = piece.currentGridPos;
            snapshot.CurrentHp = piece.currentHp;
            snapshot.CurrentSkillCooldown = piece.currentSkillCooldown;
            snapshot.SilencedTurnsLeft = piece.silencedTurnsLeft;
            snapshot.PieceDataIndex = piece.pieceDataIndex;
            snapshot.Faction = piece.faction;
            snapshot.IsKing = piece.isKing;
            return true;
        }
        catch (System.InvalidOperationException)
        {
            // Fusion throws this when a NetworkBehaviour exists in the scene hierarchy
            // but Spawned() has not been called yet, or when it is already being despawned.
            // Client-side hover/preview code can safely ignore that transient object.
            return false;
        }
    }

    private void ResolveSceneReferences()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (chessBoard == null)
            chessBoard = FindFirstObjectByType<ChessBoard>();

        if (chessControl == null)
            chessControl = FindFirstObjectByType<ChessControl>();

        if (ghostPiece == null)
        {
            GhostPiece foundGhost = FindFirstObjectByType<GhostPiece>(FindObjectsInactive.Include);
            if (foundGhost != null)
                ghostPiece = foundGhost;
        }

        if (weaponUI == null)
        {
            WeaponControllerUI foundWeaponUI = FindFirstObjectByType<WeaponControllerUI>(FindObjectsInactive.Include);
            if (foundWeaponUI != null)
                weaponUI = foundWeaponUI;
        }

        if (pieceContextUI == null)
        {
            PieceContextUI foundPieceContextUI = FindFirstObjectByType<PieceContextUI>(FindObjectsInactive.Include);
            if (foundPieceContextUI != null)
                pieceContextUI = foundPieceContextUI;
        }
    }

    private void InitializeWeaponUIIfPossible()
    {
        ResolveWeaponsFromServerCombatIfNeeded();

        if (weaponUI == null)
            return;

        if (!weaponUISubscribed)
        {
            weaponUI.onActionPressed -= OnRogueActionPressed;
            weaponUI.onWeaponSelected -= OnRogueWeaponSelected;

            weaponUI.onActionPressed += OnRogueActionPressed;
            weaponUI.onWeaponSelected += OnRogueWeaponSelected;

            weaponUISubscribed = true;
        }

        bool hasWeapons = equippedWeapons != null && equippedWeapons.Count > 0;
        if (hasWeapons && (!weaponSlotsConfigured || configuredWeaponCount != equippedWeapons.Count))
        {
            currentSelectedWeaponIndex = Mathf.Clamp(currentSelectedWeaponIndex, 0, equippedWeapons.Count - 1);
            weaponUI.SetupWeaponSlots(equippedWeapons);
            weaponUI.UpdateActiveWeaponHighlight(currentSelectedWeaponIndex);
            weaponUI.SetActionMode(currentState == ClientInputState.ConfirmingAttack);

            weaponSlotsConfigured = true;
            configuredWeaponCount = equippedWeapons.Count;

            if (debugInputLogs)
            {
                Debug.Log($"[Client Input] Weapon UI slots configured. WeaponCount={equippedWeapons.Count}");
            }
        }

        if (!weaponUIInitialized)
        {
            weaponUI.TogglePanel(false);
            lastWeaponPanelState = false;
            weaponUIInitialized = true;

            if (debugInputLogs)
            {
                Debug.Log($"[Client Input] Weapon UI initialized. HasWeapons={hasWeapons}, WeaponCount={(hasWeapons ? equippedWeapons.Count : 0)}");
            }
        }
    }

    private void ResolveWeaponsFromServerCombatIfNeeded()
    {
        if (equippedWeapons != null && equippedWeapons.Count > 0)
            return;

        if (ServerCombatManager.Instance == null)
            return;

        List<WeaponData> serverWeapons = ServerCombatManager.Instance.GetAvailableWeaponsForClientUI();
        if (serverWeapons == null || serverWeapons.Count == 0)
            return;

        equippedWeapons = serverWeapons;
    }

    private void UnsubscribeWeaponUI()
    {
        if (weaponUI == null || !weaponUISubscribed) return;

        weaponUI.onActionPressed -= OnRogueActionPressed;
        weaponUI.onWeaponSelected -= OnRogueWeaponSelected;
        weaponUISubscribed = false;
    }

    private void UpdateLocalRoleAndTurnUI()
    {
        if (ServerGameManager.Instance == null)
        {
            ToggleWeaponPanel(false);
            return;
        }

        NetGameState state = ServerGameManager.Instance.currentGameState;
        PlayerRef king = ServerGameManager.Instance.kingPlayer;
        PlayerRef chess = ServerGameManager.Instance.chessPlayer;

        UpdateAttackRequestPendingState(state);

        if (debugInputLogs && (state != lastObservedGameState || king != lastObservedKingPlayer || chess != lastObservedChessPlayer))
        {
            Debug.Log($"[Client Input] Turn={state}, Me={Object.InputAuthority}, King={king}, Chess={chess}, CanAct={CanLocalPlayerActNow()}");
            lastObservedGameState = state;
            lastObservedKingPlayer = king;
            lastObservedChessPlayer = chess;
        }

        bool shouldShowWeaponPanel = IsLocalKingPlayer() && state == NetGameState.KingTurn;
        ToggleWeaponPanel(shouldShowWeaponPanel);
    }


    private void UpdateAttackRequestPendingState(NetGameState state)
    {
        if (!attackRequestPending)
            return;

        // Once the server advances away from KingTurn/ResolvingAction, the pending attack has been consumed.
        if (state != NetGameState.KingTurn && state != NetGameState.ResolvingAction)
        {
            attackRequestPending = false;
            return;
        }

        // Safety fallback: if the server rejected the request and stayed in KingTurn, do not lock the player forever.
        if (state == NetGameState.KingTurn && Time.unscaledTime - attackRequestPendingStartedTime > AttackRequestFallbackUnlockSeconds)
        {
            attackRequestPending = false;
            if (debugInputLogs)
                Debug.Log("[Client Input] Attack request pending timeout cleared. You can act again if it is still your turn.");
        }
    }

    private void ToggleWeaponPanel(bool shouldShow)
    {
        if (weaponUI == null) return;
        if (lastWeaponPanelState == shouldShow) return;

        weaponUI.TogglePanel(shouldShow);
        weaponUI.SetActionMode(currentState == ClientInputState.ConfirmingAttack);
        lastWeaponPanelState = shouldShow;
    }

    private void QueueAutoReturnToMenuAfterGameOver()
    {
        // New result-flow patch: when MatchResultUI is present, it owns the GameOver UI,
        // the Back button, and the safety return. Keep this old auto-return only as fallback
        // for scenes that have not been set up with a result panel yet.
        if (MatchResultUI.ExistsInScene) return;
        if (!autoReturnToMenuOnGameOver) return;
        if (gameOverReturnQueued) return;
        if (!IsLocalInputActive()) return;

        gameOverReturnQueued = true;
        StartCoroutine(AutoReturnToMenuAfterGameOverRoutine());
    }

    private System.Collections.IEnumerator AutoReturnToMenuAfterGameOverRoutine()
    {
        float delay = Mathf.Max(0f, autoReturnToMenuAfterGameOverDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        if (NetworkRunnerHandler.Active != null)
        {
            NetworkRunnerHandler.Active.ClientLeaveCurrentSessionAndReturnToMenu("match_game_over_auto_return");
        }
        else
        {
            Debug.LogWarning("[Client Flow] Cannot return to MenuScene after GameOver because NetworkRunnerHandler.Active is missing.");
        }
    }

    private void PollMouseInput()
    {
        if (Mouse.current == null) return;

        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelCurrentInteraction();
            return;
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelCurrentInteraction();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && !pointerOverUI)
        {
            BoardTile tile = GetTileUnderPointer(out Vector2Int cell);
            HandleBoardPointerDown(tile, cell);
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            // Release can happen above UI after starting a drag; still resolve safely.
            BoardTile tile = GetTileUnderPointer(out Vector2Int cell);
            HandleBoardPointerUp(tile, cell);
        }
    }

    private BoardTile GetTileUnderPointer(out Vector2Int cell)
    {
        cell = new Vector2Int(-1, -1);

        if (mainCamera == null || Mouse.current == null)
            return null;

        Vector2 pointerScreenPos = Mouse.current.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(pointerScreenPos);
        worldPos.z = 0f;

        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        foreach (Collider2D hit in hits)
        {
            if (hit == null) continue;

            BoardTile tile = hit.GetComponent<BoardTile>();
            if (tile == null)
                tile = hit.GetComponentInParent<BoardTile>();

            if (tile != null)
            {
                cell = new Vector2Int(tile.boardX, tile.boardY);
                return tile;
            }
        }

        if (chessBoard != null)
        {
            cell = chessBoard.WorldToGrid(worldPos);
            return chessBoard.GetTileAt(cell);
        }

        return null;
    }

    private Vector3 GetPointerWorldPosition()
    {
        if (mainCamera != null && Mouse.current != null)
        {
            Vector3 pos = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            pos.z = 0f;
            return pos;
        }

        if (chessControl != null)
        {
            Vector3 pos = chessControl.mouseWorldPosition;
            pos.z = 0f;
            return pos;
        }

        return Vector3.zero;
    }

    private void HandleBoardPointerDown(BoardTile clickedTile, Vector2Int cellPos)
    {
        if (clickedTile == null) return;

        if (currentState == ClientInputState.Idle)
        {
            if (!CanLocalPlayerActNow())
            {
                if (debugInputLogs)
                    Debug.Log($"[Client Input] Ignored click at {cellPos}: not my turn/role. Me={Object.InputAuthority}");
                return;
            }

            NetworkChessPiece targetPiece = FindNetworkPieceAt(cellPos);
            if (targetPiece == null)
            {
                if (debugInputLogs)
                    Debug.Log($"[Client Input] Ignored click at {cellPos}: no NetworkChessPiece at this cell.");
                return;
            }

            if (!CanLocalPlayerControlPiece(targetPiece))
            {
                if (debugInputLogs)
                {
                    string factionText = TryGetNetworkPieceSnapshot(targetPiece, out NetworkPieceSnapshot blockedSnapshot)
                        ? blockedSnapshot.Faction.ToString()
                        : "Unknown/NotSpawned";
                    Debug.Log($"[Client Input] Ignored piece at {cellPos}: cannot control faction={factionText} on state={ServerGameManager.Instance.currentGameState}.");
                }
                return;
            }

            StartDragging(targetPiece);
            return;
        }

        if (currentState == ClientInputState.AimingAttack || currentState == ClientInputState.ConfirmingAttack)
        {
            TryLockAttackTarget(cellPos);
        }
    }

    private void HandleBoardPointerUp(BoardTile releasedTile, Vector2Int cellPos)
    {
        if (currentState != ClientInputState.DraggingPiece) return;

        if (releasedTile != null && currentValidMoves.Contains(cellPos))
        {
            RequestDropMove(cellPos);
        }
        else
        {
            ReturnPieceToOriginalPosition();
        }
    }

    private void StartDragging(NetworkChessPiece piece)
    {
        ResolveSceneReferences();

        if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot))
        {
            if (debugInputLogs)
                Debug.Log("[Client Input] Ignored drag start: selected NetworkChessPiece is not spawned/ready yet.");
            selectedPiece = null;
            currentState = ClientInputState.Idle;
            return;
        }

        selectedPiece = piece;
        selectedFromPos = pieceSnapshot.GridPos;
        currentState = ClientInputState.DraggingPiece;

        HidePieceContextUI();
        ClearAllHighlights();

        ChessPieceRuntime selectedRuntime;
        BoardData clientPreviewBoard = BuildClientPreviewBoard(out selectedRuntime);

        currentValidMoves.Clear();
        if (selectedRuntime != null && clientPreviewBoard != null)
        {
            currentValidMoves.AddRange(clientPreviewBoard.GetValidMoves(selectedRuntime));
        }

        ShowHighlightTiles(currentValidMoves, TileState.ValidMove);

        if (lastHoveredTile != null)
        {
            lastHoveredTile.ToggleSelection(false);
            lastHoveredTile = null;
        }

        if (ghostPiece != null)
        {
            ChessPieceRuntime ghostRuntime = CreateRuntimeFromNetworkPiece(piece, pieceSnapshot);
            if (ghostRuntime != null)
            {
                ghostPiece.Initialize(ghostRuntime);
                ghostPiece.transform.position = chessBoard != null
                    ? chessBoard.GetPieceWorldPosition(pieceSnapshot.GridPos)
                    : piece.transform.position;
            }
        }

        piece.SetLocalVisualVisible(false);

        if (debugInputLogs)
            Debug.Log($"[Client Input] Started dragging {pieceSnapshot.Faction} piece from {selectedFromPos}. ValidMoves={currentValidMoves.Count}");
    }

    private void UpdateDragVisuals()
    {
        if (ghostPiece != null && ghostPiece.gameObject.activeSelf)
        {
            Vector3 targetPos = GetPointerWorldPosition();
            ghostPiece.transform.position = Vector3.Lerp(
                ghostPiece.transform.position,
                targetPos + ghostDragOffset,
                0.5f
            );
        }

        BoardTile tile = GetTileUnderPointer(out Vector2Int cell);
        UpdateDropShadow(tile, cell, currentValidMoves);
    }

    private void UpdateIdleHover()
    {
        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector2Int cell = new Vector2Int(-1, -1);
        BoardTile currentTile = pointerOverUI ? null : GetTileUnderPointer(out cell);

        if (lastHoveredTile != null && lastHoveredTile != currentTile)
            lastHoveredTile.ToggleSelection(false);

        if (currentTile != null)
            currentTile.ToggleSelection(true);

        lastHoveredTile = currentTile;
        UpdatePieceContextHover(currentTile, pointerOverUI ? new Vector2Int(-1, -1) : cell);
    }

    private void UpdatePieceContextHover(BoardTile currentTile, Vector2Int cell)
    {
        if (pieceContextUI == null)
            return;

        if (currentTile == null)
        {
            HidePieceContextUI();
            return;
        }

        NetworkChessPiece hoveredPiece = FindNetworkPieceAt(cell);
        if (hoveredPiece == null)
        {
            HidePieceContextUI();
            return;
        }

        currentHoveredNetworkPieceForUI = hoveredPiece;
        pieceContextUI.Show(hoveredPiece);
    }

    private void HidePieceContextUI()
    {
        currentHoveredNetworkPieceForUI = null;
        if (pieceContextUI != null)
            pieceContextUI.Hide();
    }

    private void UpdateAimingHover()
    {
        BoardTile tile = GetTileUnderPointer(out Vector2Int cell);
        UpdateDropShadow(tile, cell, currentValidAttacks, TileState.AttackRange, true);
    }

    private void UpdateDropShadow(
        BoardTile currentTile,
        Vector2Int currentCell,
        List<Vector2Int> validTiles,
        TileState validBaseState = TileState.ValidMove,
        bool preserveAttackAoE = false)
    {
        if (lastHoveredTile != null && lastHoveredTile != currentTile)
        {
            Vector2Int lastPos = new Vector2Int(lastHoveredTile.boardX, lastHoveredTile.boardY);
            RestoreTileBaseState(lastHoveredTile, lastPos, validTiles, validBaseState, preserveAttackAoE);
        }

        if (currentTile != null)
        {
            bool isValid = validTiles != null && validTiles.Contains(currentCell);

            if (isValid)
            {
                currentTile.SetTileState(TileState.HoverValid);
            }
            else if (preserveAttackAoE && currentAoETiles.Contains(currentCell))
            {
                currentTile.SetTileState(TileState.AttackTarget);
            }
            else
            {
                currentTile.SetTileState(TileState.HoverInvalid);
            }
        }

        lastHoveredTile = currentTile;
    }

    private void RestoreTileBaseState(
        BoardTile tile,
        Vector2Int tilePos,
        List<Vector2Int> validTiles,
        TileState validBaseState,
        bool preserveAttackAoE)
    {
        if (tile == null) return;

        if (preserveAttackAoE && currentAoETiles.Contains(tilePos))
        {
            tile.SetTileState(TileState.AttackTarget);
        }
        else if (validTiles != null && validTiles.Contains(tilePos))
        {
            tile.SetTileState(validBaseState);
        }
        else
        {
            tile.SetTileState(TileState.None);
        }
    }

    private void RequestDropMove(Vector2Int targetGridPos)
    {
        if (selectedPiece == null)
        {
            ResetDragState();
            return;
        }

        currentState = ClientInputState.Animating;
        ClearAllHighlights();

        Vector2Int originalPos = selectedFromPos;
        Vector3 targetWorldPos = chessBoard != null
            ? chessBoard.GetPieceWorldPosition(targetGridPos)
            : selectedPiece.transform.position;

        if (ghostPiece != null && ghostPiece.gameObject.activeSelf)
        {
            ghostPiece.transform
                .DOMove(targetWorldPos, ghostSnapDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => SendMoveRequestAndReset(originalPos, targetGridPos));
        }
        else
        {
            SendMoveRequestAndReset(originalPos, targetGridPos);
        }
    }

    private void SendMoveRequestAndReset(Vector2Int originalPos, Vector2Int targetGridPos)
    {
        Debug.Log($"[Client Input] Requesting drag-drop move from {originalPos} to {targetGridPos}");

        Rpc_RequestMove(originalPos, targetGridPos);

        // Server is authoritative. The local ghost is only UX.
        ResetDragState();
    }

    private void ReturnPieceToOriginalPosition()
    {
        if (selectedPiece == null)
        {
            ResetDragState();
            return;
        }

        currentState = ClientInputState.Animating;
        ClearAllHighlights();

        Vector3 originalWorldPos = chessBoard != null
            ? chessBoard.GetPieceWorldPosition(selectedFromPos)
            : selectedPiece.transform.position;

        if (ghostPiece != null && ghostPiece.gameObject.activeSelf)
        {
            ghostPiece.transform
                .DOMove(originalWorldPos, ghostSnapDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(ResetDragState);
        }
        else
        {
            ResetDragState();
        }
    }

    private void ResetDragState()
    {
        ClearAllHighlights();

        if (ghostPiece != null)
            ghostPiece.Hide();

        if (selectedPiece != null)
            selectedPiece.SetLocalVisualVisible(true);

        selectedPiece = null;
        selectedFromPos = new Vector2Int(-1, -1);
        currentValidMoves.Clear();
        currentState = ClientInputState.Idle;
    }

    private void OnRogueActionPressed()
    {
        if (!IsLocalInputActive()) return;

        if (attackRequestPending)
        {
            if (debugInputLogs)
                Debug.Log("[Client Input] Ignored Attack button: attack request already sent and is waiting for server resolve.");
            return;
        }
        if (!IsLocalKingPlayer() || ServerGameManager.Instance == null || ServerGameManager.Instance.currentGameState != NetGameState.KingTurn)
        {
            if (debugInputLogs)
                Debug.Log("[Client Input] Ignored Attack button: local player is not active King player.");
            return;
        }

        if (currentState == ClientInputState.Idle)
        {
            StartAimingAttack();
            return;
        }

        if (currentState == ClientInputState.ConfirmingAttack)
        {
            SendAttackRequestAndReset();
        }
        else if (currentState == ClientInputState.AimingAttack)
        {
            CancelCurrentInteraction();
        }
    }


    private void SendAttackRequestAndReset()
    {
        if (attackRequestPending)
            return;

        attackRequestPending = true;
        attackRequestPendingStartedTime = Time.unscaledTime;

        Vector2Int requestTarget = lockedAttackTarget;
        int requestWeaponIndex = currentSelectedWeaponIndex;

        Debug.Log($"[Client Input] Requesting attack target={requestTarget}, weaponIndex={requestWeaponIndex}");
        Rpc_RequestAttack(requestTarget, requestWeaponIndex);

        // Clear the local aiming UI immediately so the same button click/double click cannot send another RPC.
        CancelCurrentInteraction();
    }

    private void OnRogueWeaponSelected(int index)
    {
        if (!IsLocalInputActive()) return;

        ResolveWeaponsFromServerCombatIfNeeded();

        if (equippedWeapons == null || index < 0 || index >= equippedWeapons.Count) return;
        if (currentState != ClientInputState.Idle && currentState != ClientInputState.AimingAttack) return;

        currentSelectedWeaponIndex = index;
        if (weaponUI != null)
            weaponUI.UpdateActiveWeaponHighlight(currentSelectedWeaponIndex);

        if (debugInputLogs)
            Debug.Log($"[Client Input] Selected weapon {index}: {equippedWeapons[index].weaponName}");

        if (currentState == ClientInputState.AimingAttack)
        {
            StartAimingAttack();
        }
    }

    private void StartAimingAttack()
    {
        if (attackRequestPending)
        {
            if (debugInputLogs)
                Debug.Log("[Client Input] Ignored attack aiming: previous attack request is still pending.");
            return;
        }

        ResolveWeaponsFromServerCombatIfNeeded();
        HidePieceContextUI();
        ClearAllHighlights();

        WeaponData activeWeapon = GetActiveWeapon();
        NetworkChessPiece kingPiece = FindRogueKingPiece();
        BoardData previewBoard = BuildClientPreviewBoard(out _);

        if (activeWeapon == null || kingPiece == null || previewBoard == null ||
            !TryGetNetworkPieceSnapshot(kingPiece, out NetworkPieceSnapshot kingSnapshot))
        {
            Debug.LogWarning($"[Client Input] Cannot start aiming attack. weapon={activeWeapon}, king={kingPiece}, previewBoard={previewBoard}");
            currentState = ClientInputState.Idle;
            return;
        }

        selectedPiece = kingPiece;
        lockedAttackTarget = new Vector2Int(-1, -1);
        currentAoETiles.Clear();
        currentValidAttacks.Clear();
        currentValidAttacks.AddRange(ActionResolver.GetTargetingRange(activeWeapon, kingSnapshot.GridPos, previewBoard));

        ShowHighlightTiles(currentValidAttacks, TileState.AttackRange);
        currentState = ClientInputState.AimingAttack;

        if (weaponUI != null)
            weaponUI.SetActionMode(false);

        Debug.Log($"[Client Input] Entered attack aiming. Weapon={activeWeapon.weaponName}, Targets={currentValidAttacks.Count}");
    }

    private void TryLockAttackTarget(Vector2Int cellPos)
    {
        if (!currentValidAttacks.Contains(cellPos))
        {
            CancelCurrentInteraction();
            return;
        }

        lockedAttackTarget = cellPos;
        currentState = ClientInputState.ConfirmingAttack;
        UpdateAttackPreviewVisuals();

        if (weaponUI != null)
            weaponUI.SetActionMode(true);

        Debug.Log($"[Client Input] Attack target locked at {lockedAttackTarget}. Press FIRE to confirm.");
    }

    private void UpdateAttackPreviewVisuals()
    {
        ClearHighlightTiles(currentAoETiles);

        WeaponData activeWeapon = GetActiveWeapon();
        NetworkChessPiece kingPiece = FindRogueKingPiece();
        BoardData previewBoard = BuildClientPreviewBoard(out _);

        if (activeWeapon == null || kingPiece == null || previewBoard == null ||
            !TryGetNetworkPieceSnapshot(kingPiece, out NetworkPieceSnapshot kingSnapshot))
            return;

        currentAoETiles.Clear();
        currentAoETiles.AddRange(ActionResolver.GetAoE(activeWeapon, kingSnapshot.GridPos, lockedAttackTarget, previewBoard));

        ShowHighlightTiles(currentValidAttacks, TileState.AttackRange);
        ShowHighlightTiles(currentAoETiles, TileState.AttackTarget);
    }

    private WeaponData GetActiveWeapon()
    {
        ResolveWeaponsFromServerCombatIfNeeded();

        if (equippedWeapons == null || equippedWeapons.Count == 0)
            return null;

        currentSelectedWeaponIndex = Mathf.Clamp(currentSelectedWeaponIndex, 0, equippedWeapons.Count - 1);
        return equippedWeapons[currentSelectedWeaponIndex];
    }

    private void CancelCurrentInteraction()
    {
        if (currentState == ClientInputState.DraggingPiece || currentState == ClientInputState.Animating)
        {
            ReturnPieceToOriginalPosition();
            return;
        }

        HidePieceContextUI();
        ClearAllHighlights();
        ClearHighlightTiles(currentValidAttacks);
        ClearHighlightTiles(currentAoETiles);
        currentValidAttacks.Clear();
        currentAoETiles.Clear();
        lockedAttackTarget = new Vector2Int(-1, -1);
        selectedPiece = null;
        currentState = ClientInputState.Idle;

        if (weaponUI != null)
            weaponUI.SetActionMode(false);
    }

    private bool CanLocalPlayerActNow()
    {
        if (attackRequestPending) return false;
        if (ServerGameManager.Instance == null) return false;

        PlayerRef me = Object.InputAuthority;
        NetGameState state = ServerGameManager.Instance.currentGameState;

        if (state == NetGameState.KingTurn)
            return ServerGameManager.Instance.kingPlayer == me;

        if (state == NetGameState.ChessTurn)
            return ServerGameManager.Instance.chessPlayer == me;

        return false;
    }

    private bool IsLocalKingPlayer()
    {
        return ServerGameManager.Instance != null && ServerGameManager.Instance.kingPlayer == Object.InputAuthority;
    }

    private bool CanLocalPlayerControlPiece(NetworkChessPiece piece)
    {
        if (piece == null || ServerGameManager.Instance == null) return false;
        if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot)) return false;

        PlayerRef me = Object.InputAuthority;
        NetGameState state = ServerGameManager.Instance.currentGameState;

        if (state == NetGameState.KingTurn)
        {
            return ServerGameManager.Instance.kingPlayer == me && pieceSnapshot.Faction == ChessFaction.ChessRogue;
        }

        if (state == NetGameState.ChessTurn)
        {
            return ServerGameManager.Instance.chessPlayer == me && pieceSnapshot.Faction == ChessFaction.ChessAlliance;
        }

        return false;
    }

    private NetworkChessPiece FindNetworkPieceAt(Vector2Int gridPos)
    {
        NetworkChessPiece[] pieces = FindObjectsByType<NetworkChessPiece>(FindObjectsSortMode.None);

        foreach (NetworkChessPiece piece in pieces)
        {
            if (TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot) && pieceSnapshot.GridPos == gridPos)
                return piece;
        }

        return null;
    }

    private NetworkChessPiece FindRogueKingPiece()
    {
        NetworkChessPiece[] pieces = FindObjectsByType<NetworkChessPiece>(FindObjectsSortMode.None);

        foreach (NetworkChessPiece piece in pieces)
        {
            if (TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot) &&
                pieceSnapshot.IsKing &&
                pieceSnapshot.Faction == ChessFaction.ChessRogue)
                return piece;
        }

        return null;
    }

    private BoardData BuildClientPreviewBoard(out ChessPieceRuntime selectedRuntime)
    {
        selectedRuntime = null;

        ResolveSceneReferences();

        LevelData levelData = null;

        if (chessBoard != null)
            levelData = chessBoard.CurrentLevelData;

        if (levelData == null && ServerBoardManager.Instance != null)
            levelData = ServerBoardManager.Instance.currentLevelData;

        if (levelData == null) return null;

        BoardData previewBoard = new BoardData(
            levelData.boardWidth,
            levelData.boardHeight,
            levelData.tileExistenceMap != null ? levelData.tileExistenceMap.ToList() : null
        );

        NetworkChessPiece[] pieces = FindObjectsByType<NetworkChessPiece>(FindObjectsSortMode.None);

        foreach (NetworkChessPiece piece in pieces)
        {
            if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot))
                continue;

            ChessPieceRuntime runtime = CreateRuntimeFromNetworkPiece(piece, pieceSnapshot);
            if (runtime == null) continue;

            runtime.currentHealth = pieceSnapshot.CurrentHp;
            runtime.currentSkillCooldown = pieceSnapshot.CurrentSkillCooldown;
            runtime.silencedTurnsLeft = pieceSnapshot.SilencedTurnsLeft;

            previewBoard.AddEntity(runtime, pieceSnapshot.GridPos.x, pieceSnapshot.GridPos.y);

            if (piece == selectedPiece)
                selectedRuntime = runtime;
        }

        return previewBoard;
    }

    private ChessPieceRuntime CreateRuntimeFromNetworkPiece(NetworkChessPiece piece)
    {
        if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot pieceSnapshot))
            return null;

        return CreateRuntimeFromNetworkPiece(piece, pieceSnapshot);
    }

    private ChessPieceRuntime CreateRuntimeFromNetworkPiece(NetworkChessPiece piece, NetworkPieceSnapshot pieceSnapshot)
    {
        if (piece == null) return null;

        ChessPieceData pieceData = null;

        try
        {
            pieceData = piece.PieceData;
        }
        catch (System.InvalidOperationException)
        {
            // Piece was despawned between snapshot read and visual-data resolution.
            // Skip it from client-side preview this frame.
            return null;
        }

        if (pieceData == null && ServerBoardManager.Instance != null)
            pieceData = ServerBoardManager.Instance.GetPieceDataByIndex(pieceSnapshot.PieceDataIndex);

        if (pieceData == null) return null;

        return new ChessPieceRuntime(pieceData, pieceSnapshot.GridPos, pieceSnapshot.Faction);
    }

    private void ShowHighlightTiles(List<Vector2Int> validTiles, TileState state)
    {
        if (chessBoard == null || validTiles == null) return;

        foreach (Vector2Int pos in validTiles)
        {
            chessBoard.GetTileAt(pos)?.SetTileState(state);
        }
    }

    private void ClearHighlightTiles(List<Vector2Int> tiles)
    {
        if (chessBoard == null || tiles == null) return;

        foreach (Vector2Int pos in tiles)
        {
            chessBoard.GetTileAt(pos)?.SetTileState(TileState.None);
        }

        tiles.Clear();
    }

    private void ClearAllHighlights()
    {
        if (chessBoard != null)
            chessBoard.ResetAllTileHighlights();

        currentValidMoves.Clear();

        if (lastHoveredTile != null)
        {
            lastHoveredTile.ToggleSelection(false);
            lastHoveredTile = null;
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestMove(Vector2Int currentPos, Vector2Int targetPos, RpcInfo info = default)
    {
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null) return;

        PlayerRef requestingPlayer = info.Source;
        if (!ServerGameManager.Instance.CanPlayerAct(requestingPlayer)) return;

        if (ServerBoardManager.Instance.IsValidMove(currentPos, targetPos, requestingPlayer))
        {
            bool shouldEndTurn = ServerBoardManager.Instance.MovePiece(currentPos, targetPos);

            if (hasExtraTurn)
            {
                hasExtraTurn = false; 
                Debug.Log($"[Server] Player {requestingPlayer} kích hoạt Thêm Lượt, KHÔNG qua Turn!");
            }
            else
            {
                ServerGameManager.Instance.EndTurn();
            }
        }
        else
        {
            Debug.LogWarning($"[Server] Rejected invalid move from {currentPos} to {targetPos} by {requestingPlayer}");
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestAttack(Vector2Int targetPos, int weaponIndex, RpcInfo info = default)
    {
        if (ServerGameManager.Instance == null || ServerCombatManager.Instance == null) return;

        PlayerRef requestingPlayer = info.Source;

        if (ServerCombatManager.Instance.IsAttackResolutionInProgress)
        {
            Debug.Log($"[Server] Ignored duplicate/late attack at {targetPos} with weapon {weaponIndex} by {requestingPlayer}: another attack is already resolving.");
            return;
        }

        if (!ServerCombatManager.Instance.IsValidAttack(requestingPlayer, targetPos, weaponIndex))
        {
            Debug.LogWarning($"[Server] Rejected invalid attack at {targetPos} with weapon {weaponIndex} by {requestingPlayer}");
            return;
        }

        bool shouldEndTurn = ServerCombatManager.Instance.ProcessAttack(targetPos, weaponIndex);

        if (shouldEndTurn)
        {
            ServerGameManager.Instance.EndTurn();
        }
    }// ==============================================================
    // KHOẢNG KHÔNG GIAN DÀNH RIÊNG CHO HỆ THỐNG THẺ BÀI (CARD SYSTEM)
    // ==============================================================

    [Header("Card System (Networked)")]
    [SerializeField] private List<CardData> startingDeck; // Kéo thả 1 lá test card vào đây nếu chỉ cần test UI/RPC.

    [Networked] public NetworkBool hasExtraTurn { get; set; }
    private bool deckInitializedOnServer;

    // Mảng thẻ bài đồng bộ thời gian thực. Khi Server thay đổi, hàm OnHandCardsChanged sẽ tự động chạy ở Client.
    [Networked, Capacity(10), OnChangedRender(nameof(OnHandCardsChanged))]
    public NetworkArray<NetworkCardInstance> HandCards { get; }

    public static PlayerNetworkController Local => activeLocalInputController;

    // Server gọi hàm này lúc mới Spawn để phát bài cho người chơi.
    // Trả về false khi ServerCardManager chưa sẵn sàng để FixedUpdateNetwork retry ở tick sau.
    private bool TryInitializeDeckOnServer()
    {
        if (!HasStateAuthority) return false;
        if (deckInitializedOnServer) return true;
        if (startingDeck == null || ServerCardManager.Instance == null) return false;

        for (int i = 0; i < HandCards.Length; i++)
        {
            HandCards.Set(i, default);
        }

        int initializedCount = 0;
        for (int i = 0; i < startingDeck.Count && i < HandCards.Length; i++)
        {
            CardData cardData = startingDeck[i];
            int globalIndex = ServerCardManager.Instance.GetCardIndex(cardData);
            if (cardData == null || globalIndex < 0)
            {
                Debug.LogWarning($"[Server Card] Bỏ qua card slot {i}: CardData null hoặc chưa được đăng ký trong ServerCardManager.availableCards.");
                continue;
            }

            NetworkCardInstance card = new NetworkCardInstance
            {
                cardDataIndex = globalIndex,
                currentCooldown = 0,
                remainingUses = Mathf.Max(1, cardData.maxUses),
                isInitialized = true
            };

            HandCards.Set(i, card);
            initializedCount++;
        }

        deckInitializedOnServer = true;

        if (debugInputLogs)
        {
            Debug.Log($"[Server Card] Initialized {initializedCount} card(s) for player {Object.InputAuthority} on NetworkObject={Object.Id}.");
        }

        return true;
    }

    // Kênh RPC: Client Gửi Yêu Cầu Xài Bài Lên Server
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void Rpc_RequestPlayCard(int handIndex, Vector2Int targetPos, RpcInfo info = default)
    {
        if (ServerCardManager.Instance == null || ServerGameManager.Instance == null) return;
        if (!ServerGameManager.Instance.CanPlayerAct(info.Source)) return;

        ServerCardManager.Instance.ProcessCardRequest(info.Source, this, handIndex, targetPos);
    }

    // Server trừ Cooldown thẻ bài mỗi khi qua Turn
    public void TickCardCooldowns()
    {
        if (!HasStateAuthority) return;
        for (int i = 0; i < HandCards.Length; i++)
        {
            NetworkCardInstance card = HandCards[i];
            if (card.isInitialized && card.currentCooldown > 0)
            {
                card.currentCooldown--;
                HandCards.Set(i, card);
            }
        }
    }

    private void OnHandCardsChanged()
    {
        // Khi Server cập nhật mảng bài (VD: vừa xài xong, vừa trừ Cooldown), báo cho giao diện Client Refresh
        InventoryUI ui = FindFirstObjectByType<InventoryUI>();
        if (ui != null) ui.RefreshAllCards();
    }

    public Vector2Int GetSelectedPieceGridPos()
    {
        if (TryGetNetworkPieceSnapshot(selectedPiece, out NetworkPieceSnapshot selectedSnapshot))
            return selectedSnapshot.GridPos;

        return new Vector2Int(-1, -1);
    }
}
