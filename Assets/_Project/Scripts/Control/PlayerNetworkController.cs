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
        Animating,
        AimingCard 
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

    private const int MaxWeaponCooldownSlots = 8;

    [Networked, Capacity(MaxWeaponCooldownSlots), OnChangedRender(nameof(OnWeaponCooldownsChanged))]
    public NetworkArray<int> WeaponCooldowns { get; }

    [Header("Debug")]
    [SerializeField] private bool debugInputLogs = true;

    [Header("Card/Skill Highlight Preview")]
    [Tooltip("Bật highlight các ô hợp lệ khi đang chọn mục tiêu cho card/skill.")]
    [SerializeField] private bool showCardTargetHighlights = true;

    [Tooltip("Màu/state dùng cho ô có thể chọn khi card/skill cần target. ValidMove thường là xanh.")]
    [SerializeField] private TileState cardSelectableTargetHighlightState = TileState.ValidMove;

    [Tooltip("Bật highlight nhanh các quân sẽ bị/được ảnh hưởng với card không cần target.")]
    [SerializeField] private bool flashInstantCardAffectedTiles = true;

    [Tooltip("Màu/state dùng để flash các ô bị/được ảnh hưởng bởi card không cần target.")]
    [SerializeField] private TileState instantCardAffectedHighlightState = TileState.ValidMove;

    [Tooltip("Thời gian giữ highlight preview cho card không cần target, tính bằng giây.")]
    [SerializeField, Min(0.05f)] private float instantCardAffectedFlashSeconds = 0.45f;

    [Header("Multiplayer Flow")]
    [Tooltip("Fallback only: if no MatchResultUI exists in PlayScene, leave PlayScene and return to MenuScene automatically when GameOver is reached.")]
    [SerializeField] private bool autoReturnToMenuOnGameOver = true;

    [Tooltip("Fallback delay used only when no MatchResultUI exists in PlayScene.")]
    [SerializeField] private float autoReturnToMenuAfterGameOverDelaySeconds = 0.75f;

    private static PlayerNetworkController activeLocalInputController;
    private bool localInputEnabled;
    private bool localProfileSubmitted;
    private bool localCardLoadoutSubmitted;
    private bool gameOverReturnQueued;

    private ClientInputState currentState = ClientInputState.Idle;

    private NetworkChessPiece selectedPiece;
    private Vector2Int selectedFromPos = new Vector2Int(-1, -1);

    private readonly List<Vector2Int> currentValidMoves = new List<Vector2Int>();
    private readonly List<Vector2Int> currentValidAttacks = new List<Vector2Int>();
    private readonly List<Vector2Int> currentAoETiles = new List<Vector2Int>();
    private readonly List<Vector2Int> currentCardTargetTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> currentCardAffectedTiles = new List<Vector2Int>();

    private Coroutine cardAffectedFlashRoutine;

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

    private bool hasInteractionObservedState;
    private NetGameState lastInteractionObservedState = NetGameState.Init;
    private PlayerRef lastInteractionObservedKingPlayer;
    private PlayerRef lastInteractionObservedChessPlayer;

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
            ClearSubmittedCardLoadout();
            TryInitializeDeckOnServer();
        }

        if (!TryAcquireLocalInput())
            return;

        TrySubmitLocalProfileToServer();
        TrySubmitLocalCardLoadoutToServer();
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
        localCardLoadoutSubmitted = false;
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


    public void ClientRequestFindMatchFromLobby(int requestSerial = 0)
    {
        if (!HasInputAuthority)
            return;

        Rpc_RequestFindMatchFromLobby(requestSerial);
    }

    public void ClientRequestCreateCustomRoomFromLobby(int requestSerial = 0)
    {
        if (!HasInputAuthority)
            return;

        Rpc_RequestCreateCustomRoomFromLobby(requestSerial);
    }

    public void ClientRequestJoinCustomRoomFromLobby(string roomCode, int requestSerial = 0)
    {
        if (!HasInputAuthority)
            return;

        string safeCode = SanitizeRoomCode(roomCode);
        Rpc_RequestJoinCustomRoomFromLobby(safeCode, requestSerial);
    }

    public void ClientCancelLobbyRequestFromLobby(int requestSerial = 0)
    {
        if (!HasInputAuthority)
            return;

        Rpc_CancelLobbyRequest(requestSerial);
    }

    public void ClientRequestLeaderboardRefresh()
    {
        if (!HasInputAuthority)
            return;

        Rpc_RequestLeaderboardRefresh();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestFindMatchFromLobby(int requestSerial, RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerRequestedLobbyMatch(info.Source, requestSerial);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestCreateCustomRoomFromLobby(int requestSerial, RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerRequestedCreateCustomRoom(info.Source, requestSerial);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestJoinCustomRoomFromLobby(NetworkString<_32> roomCode, int requestSerial, RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerRequestedJoinCustomRoom(info.Source, roomCode.ToString(), requestSerial);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_CancelLobbyRequest(int requestSerial, RpcInfo info = default)
    {
        if (!HasStateAuthority)
            return;

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ServerPlayerCancelledLobbyRequest(info.Source, requestSerial);
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
        ServerSendLobbyMatchFound(matchSessionName, string.Empty);
    }

    public void ServerSendLobbyMatchFound(string matchSessionName, string roomCode)
    {
        if (!HasStateAuthority)
            return;

        string safeSession = string.IsNullOrWhiteSpace(matchSessionName) ? "RogueKingRoom" : matchSessionName.Trim();
        if (safeSession.Length > 32)
            safeSession = safeSession.Substring(0, 32);

        string safeRoomCode = SanitizeRoomCode(roomCode);
        Rpc_LobbyMatchFound(safeSession, safeRoomCode);
    }

    public void ServerSendLobbyRoomRequestFailed(string message)
    {
        if (!HasStateAuthority)
            return;

        string safeMessage = string.IsNullOrWhiteSpace(message) ? "Sai Room ID" : message.Trim();
        if (safeMessage.Length > 64)
            safeMessage = safeMessage.Substring(0, 64);

        Rpc_LobbyRoomRequestFailed(safeMessage);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_LobbyMatchFound(NetworkString<_32> matchSessionName, NetworkString<_32> roomCode)
    {
        string session = matchSessionName.ToString();
        string code = SanitizeRoomCode(roomCode.ToString());
        ClientMatchRoomContext.SetRoomCode(code);
        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ClientSwitchFromLobbyToMatchSession(session, code);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void Rpc_LobbyRoomRequestFailed(NetworkString<_64> message)
    {
        MatchmakingMenuUI menu = FindFirstObjectByType<MatchmakingMenuUI>(FindObjectsInactive.Include);
        if (menu != null)
            menu.ShowLobbyRoomError(message.ToString());
        else
            Debug.LogWarning($"[Lobby] Room request failed: {message}");
    }

    private static string SanitizeRoomCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return string.Empty;

        roomCode = roomCode.Trim();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(8);
        for (int i = 0; i < roomCode.Length; i++)
        {
            char c = roomCode[i];
            if (char.IsDigit(c))
                builder.Append(c);
        }

        string sanitized = builder.ToString();
        if (sanitized.Length > 8)
            sanitized = sanitized.Substring(0, 8);

        return sanitized;
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
        TrySubmitLocalCardLoadoutToServer();
        ResolveSceneReferences();
        InitializeWeaponUIIfPossible();
        RefreshWeaponCooldownUI();
        UpdateLocalRoleAndTurnUI();
        CleanupInteractionOnNetworkStateChange();

        if (ServerGameManager.Instance != null && ServerGameManager.Instance.currentGameState == NetGameState.GameOver)
        {
            QueueAutoReturnToMenuAfterGameOver();

            if (currentState != ClientInputState.Idle)
                CancelCurrentInteraction();

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
            case ClientInputState.AimingCard:
                // Card target highlights are generated once when the card starts aiming.
                // Do not repaint every frame; this avoids stale highlight races.
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
        public bool HasMoved;
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
            snapshot.HasMoved = piece.hasMoved;
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
            weaponUI.onCancelPressed -= OnRogueCancelPressed;

            weaponUI.onActionPressed += OnRogueActionPressed;
            weaponUI.onWeaponSelected += OnRogueWeaponSelected;
            weaponUI.onCancelPressed += OnRogueCancelPressed;

            weaponUISubscribed = true;
        }

        bool hasWeapons = equippedWeapons != null && equippedWeapons.Count > 0;
        if (hasWeapons && (!weaponSlotsConfigured || configuredWeaponCount != equippedWeapons.Count))
        {
            currentSelectedWeaponIndex = Mathf.Clamp(currentSelectedWeaponIndex, 0, equippedWeapons.Count - 1);
            weaponUI.SetupWeaponSlots(equippedWeapons);
            weaponUI.UpdateActiveWeaponHighlight(currentSelectedWeaponIndex);
            weaponUI.SetActionMode(currentState == ClientInputState.ConfirmingAttack);
            weaponUI.SetCancelActionVisible(IsAttackAimingState());
            RefreshWeaponCooldownUI();

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
        weaponUI.onCancelPressed -= OnRogueCancelPressed;
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


    private void CleanupInteractionOnNetworkStateChange()
    {
        if (ServerGameManager.Instance == null)
            return;

        NetGameState state = ServerGameManager.Instance.currentGameState;
        PlayerRef king = ServerGameManager.Instance.kingPlayer;
        PlayerRef chess = ServerGameManager.Instance.chessPlayer;

        bool changed = !hasInteractionObservedState ||
                       state != lastInteractionObservedState ||
                       king != lastInteractionObservedKingPlayer ||
                       chess != lastInteractionObservedChessPlayer;

        if (changed)
        {
            hasInteractionObservedState = true;
            lastInteractionObservedState = state;
            lastInteractionObservedKingPlayer = king;
            lastInteractionObservedChessPlayer = chess;

            // A turn/role/phase change is a hard interaction boundary.
            // This prevents old green/card highlights from surviving forever when the server advances state.
            if (currentState != ClientInputState.Idle && !CanLocalPlayerActNow())
            {
                CancelCurrentInteraction();
                return;
            }

            if (currentState == ClientInputState.Idle)
                ClearAllHighlights();
        }

        if (currentState != ClientInputState.Idle && !CanLocalPlayerActNow())
            CancelCurrentInteraction();
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
        weaponUI.SetCancelActionVisible(shouldShow && IsAttackAimingState());
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

        if (currentState == ClientInputState.AimingCard)
        {
            if (!IsPendingCardTargetLocallyAllowed(cellPos))
            {
                if (debugInputLogs)
                    Debug.Log($"[Client Card] Ignored target {cellPos} for card '{pendingCardData?.cardName}': not in highlighted valid target list.");
                return;
            }

            Debug.Log($"🟨 [Client Input] Đã chỉ định ô {cellPos} cho thẻ '{pendingCardData?.cardName}'. Đang gửi RPC!");
            Rpc_RequestPlayCard(pendingCardSlotIndex, cellPos);
            CancelCurrentInteraction(); // Xài xong thì cất thẻ, quay về Idle
            return;
        }

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

    private void OnRogueCancelPressed()
    {
        if (!IsLocalInputActive()) return;

        if (currentState == ClientInputState.AimingAttack || currentState == ClientInputState.ConfirmingAttack)
        {
            if (debugInputLogs)
                Debug.Log("[Client Input] Cancel attack pressed from WeaponControllerUI.");

            CancelCurrentInteraction();
        }
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
            if (IsWeaponOnCooldown(currentSelectedWeaponIndex))
            {
                if (debugInputLogs)
                    Debug.Log($"[Client Input] Ignored Attack button: weapon {currentSelectedWeaponIndex} is on cooldown.");
                return;
            }

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

        if (IsWeaponOnCooldown(requestWeaponIndex))
        {
            attackRequestPending = false;
            if (debugInputLogs)
                Debug.Log($"[Client Input] Cancelled attack request: weapon {requestWeaponIndex} is on cooldown.");
            return;
        }

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
        if (IsWeaponOnCooldown(currentSelectedWeaponIndex))
        {
            if (debugInputLogs)
                Debug.Log($"[Client Input] Cannot aim weapon {currentSelectedWeaponIndex}: cooldown={GetWeaponCooldown(currentSelectedWeaponIndex)}.");
            currentState = ClientInputState.Idle;
            return;
        }

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
        {
            weaponUI.SetActionMode(false);
            weaponUI.SetCancelActionVisible(true);
        }

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
        {
            weaponUI.SetActionMode(true);
            weaponUI.SetCancelActionVisible(true);
        }

        Debug.Log($"[Client Input] Attack target locked at {lockedAttackTarget}. Press FIRE to confirm.");
    }

    private void UpdateAttackPreviewVisuals()
    {
        ClearHighlightTiles(currentAoETiles);

        WeaponData activeWeapon = GetActiveWeapon();
        if (IsWeaponOnCooldown(currentSelectedWeaponIndex))
        {
            if (debugInputLogs)
                Debug.Log($"[Client Input] Cannot aim weapon {currentSelectedWeaponIndex}: cooldown={GetWeaponCooldown(currentSelectedWeaponIndex)}.");
            currentState = ClientInputState.Idle;
            return;
        }

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

    public int GetWeaponCooldown(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= WeaponCooldowns.Length)
            return 0;

        return Mathf.Max(0, WeaponCooldowns[weaponIndex]);
    }

    public void ServerStartWeaponCooldown(int weaponIndex, int cooldownTurns)
    {
        if (!HasStateAuthority) return;
        if (weaponIndex < 0 || weaponIndex >= WeaponCooldowns.Length) return;

        WeaponCooldowns.Set(weaponIndex, Mathf.Max(0, cooldownTurns));
    }

    public void TickWeaponCooldowns()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < WeaponCooldowns.Length; i++)
        {
            int cooldown = WeaponCooldowns[i];
            if (cooldown > 0)
                WeaponCooldowns.Set(i, cooldown - 1);
        }
    }

    public void ClearWeaponCooldowns()
    {
        if (!HasStateAuthority) return;

        for (int i = 0; i < WeaponCooldowns.Length; i++)
            WeaponCooldowns.Set(i, 0);
    }

    private void RefreshWeaponCooldownUI()
    {
        if (weaponUI == null)
            return;

        List<int> cooldowns = new List<int>();
        int count = equippedWeapons != null ? equippedWeapons.Count : 0;
        count = Mathf.Min(count, WeaponCooldowns.Length);

        for (int i = 0; i < count; i++)
            cooldowns.Add(Mathf.Max(0, WeaponCooldowns[i]));

        weaponUI.SetWeaponCooldowns(cooldowns);
    }

    private bool IsWeaponOnCooldown(int weaponIndex)
    {
        return GetWeaponCooldown(weaponIndex) > 0;
    }

    private void OnWeaponCooldownsChanged()
    {
        RefreshWeaponCooldownUI();
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
        lockedAttackTarget = new Vector2Int(-1, -1);
        selectedPiece = null;

        pendingCardSlotIndex = -1; 
        pendingCardData = null;

        currentState = ClientInputState.Idle;

        if (weaponUI != null)
        {
            weaponUI.SetActionMode(false);
            weaponUI.SetCancelActionVisible(false);
        }
    }

    private bool IsAttackAimingState()
    {
        return currentState == ClientInputState.AimingAttack || currentState == ClientInputState.ConfirmingAttack;
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

        ChessPieceRuntime runtime = new ChessPieceRuntime(pieceData, pieceSnapshot.GridPos, pieceSnapshot.Faction);
        runtime.hasMoved = pieceSnapshot.HasMoved;
        runtime.currentHealth = pieceSnapshot.CurrentHp;
        runtime.currentSkillCooldown = pieceSnapshot.CurrentSkillCooldown;
        runtime.silencedTurnsLeft = pieceSnapshot.SilencedTurnsLeft;
        return runtime;
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
        StopCardAffectedFlashRoutine();

        if (chessBoard != null)
            chessBoard.ResetAllTileHighlights();

        currentValidMoves.Clear();
        currentValidAttacks.Clear();
        currentAoETiles.Clear();
        currentCardTargetTiles.Clear();
        currentCardAffectedTiles.Clear();

        if (lastHoveredTile != null)
        {
            lastHoveredTile.ToggleSelection(false);
            lastHoveredTile = null;
        }
    }

    private void StopCardAffectedFlashRoutine()
    {
        if (cardAffectedFlashRoutine == null)
            return;

        StopCoroutine(cardAffectedFlashRoutine);
        cardAffectedFlashRoutine = null;
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

            // MovePiece returns false when the move already triggered a phase/game-state transition
            // or when another delayed resolver owns the turn end, for example Hidden Mine explosion.
            // In that case, do not consume ExtraTurn and do not call EndTurn again.
            if (!shouldEndTurn)
                return;

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

        WeaponData weapon = ServerCombatManager.Instance.GetWeaponDataByIndex(weaponIndex);
        if (weapon != null && weapon.cooldownTurns > 0)
            ServerStartWeaponCooldown(weaponIndex, weapon.cooldownTurns);

        bool shouldEndTurn = ServerCombatManager.Instance.ProcessAttack(requestingPlayer, targetPos, weaponIndex);

        if (shouldEndTurn)
        {
            ServerGameManager.Instance.EndTurn();
        }
    }// ==============================================================
    // KHOẢNG KHÔNG GIAN DÀNH RIÊNG CHO HỆ THỐNG THẺ BÀI (CARD SYSTEM)
    // ==============================================================

    [Header("Card System (Networked)")]
    [Tooltip("LEGACY/FALLBACK: Deck cũ. Giữ lại để prefab không mất setup cũ. Nếu Rogue King Deck hoặc Chess Alliance Deck trống, server sẽ fallback sang list này.")]
    [SerializeField] private List<CardData> startingDeck = new List<CardData>();

    [Header("Role Based Card Decks")]
    [Tooltip("3 card dùng khi player đang cầm Rogue King. Ví dụ: SummonCapturedPawn, SuperBuff, ExtraTurn.")]
    [SerializeField] private List<CardData> rogueKingDeck = new List<CardData>();

    [Tooltip("3 card dùng khi player đang cầm Chess Alliance. Ví dụ: PawnShield, Recall, March.")]
    [SerializeField] private List<CardData> chessAllianceDeck = new List<CardData>();

    [Tooltip("Số card tối đa khi player đang cầm Rogue King.")]
    [SerializeField, Min(1)] private int maxRogueKingActiveCards = PlayerSelectedCardLoadout.MaxRogueKingCards;

    [Tooltip("Số card tối đa khi player đang cầm Chess Alliance.")]
    [SerializeField, Min(1)] private int maxChessAllianceActiveCards = PlayerSelectedCardLoadout.MaxChessAllianceCards;

    [Tooltip("Đúng theo rule chọn bài trước trận: mỗi card đã chọn chỉ có 1 lần dùng trong match. SummonCapturedPawn vẫn bắt đầu 0 charge như cũ.")]
    [SerializeField] private bool selectedLoadoutCardsUseSingleUse = true;

    [Header("Selected Card Loadout (Client -> Server)")]
    [Networked] public NetworkBool HasSubmittedCardLoadout { get; private set; }
    [Networked] public int SelectedRogueKingCard0 { get; private set; }
    [Networked] public int SelectedRogueKingCard1 { get; private set; }
    [Networked] public int SelectedRogueKingCard2 { get; private set; }
    [Networked] public int SelectedChessAllianceCard0 { get; private set; }
    [Networked] public int SelectedChessAllianceCard1 { get; private set; }
    [Networked] public int SelectedChessAllianceCard2 { get; private set; }
    [Networked] public int SelectedChessAllianceCard3 { get; private set; }

    private int pendingCardSlotIndex = -1;
    private CardData pendingCardData = null;

    [Networked] public NetworkBool hasExtraTurn { get; set; }
    private bool deckInitializedOnServer;
    private ChessFaction lastInitializedDeckFaction = ChessFaction.Neutral;

    // Server-only backup dùng cho BishopSilence: khóa card 1 lượt bằng cách set remainingUses=0,
    // sau đó restore lại uses cũ khi lượt bị silence kết thúc.
    private readonly int[] cardSilenceStoredUses = new int[10];
    private readonly int[] cardSilenceStoredCardDataIndex = new int[10];
    private readonly bool[] cardSilenceStoredSlotActive = new bool[10];
    private bool cardUseSilenceActive;

    // Mảng thẻ bài đồng bộ thời gian thực. Khi Server thay đổi, hàm OnHandCardsChanged sẽ tự động chạy ở Client.
    [Networked, Capacity(10), OnChangedRender(nameof(OnHandCardsChanged))]
    public NetworkArray<NetworkCardInstance> HandCards { get; }

    public static PlayerNetworkController Local => activeLocalInputController;

    // Server gọi hàm này lúc mới Spawn để phát bài cho người chơi.
    // Trả về false khi ServerCardManager/ServerGameManager chưa sẵn sàng để FixedUpdateNetwork retry ở tick sau.
    private bool TryInitializeDeckOnServer()
    {
        if (!HasStateAuthority) return false;
        if (deckInitializedOnServer) return true;

        return ServerRebuildHandForCurrentRole(force: false);
    }

    /// <summary>
    /// Server rebuild hand theo role hiện tại của player.
    /// Phase 1: player đang là Rogue King sẽ nhận rogueKingDeck, player đang là Chess Alliance sẽ nhận chessAllianceDeck.
    /// Phase 2: sau SwapRoles(), server gọi lại hàm này để đổi hand theo phe mới.
    /// </summary>
    public bool ServerRebuildHandForCurrentRole(bool force = true)
    {
        if (!HasStateAuthority) return false;
        if (ServerCardManager.Instance == null || ServerGameManager.Instance == null) return false;

        ChessFaction deckFaction = ResolveCurrentDeckFaction();
        if (deckFaction == ChessFaction.Neutral)
            return false;

        if (!force && deckInitializedOnServer && lastInitializedDeckFaction == deckFaction)
            return true;

        List<CardData> deck = GetDeckForFaction(deckFaction);
        if (deck == null)
            return false;

        ClearAllHandCards();
        hasExtraTurn = false;

        int initializedCount = 0;
        int maxCards = Mathf.Clamp(GetMaxCardsForFaction(deckFaction), 1, HandCards.Length);

        for (int sourceIndex = 0; sourceIndex < deck.Count && initializedCount < maxCards; sourceIndex++)
        {
            CardData cardData = deck[sourceIndex];
            int globalIndex = ServerCardManager.Instance.GetCardIndex(cardData);
            if (cardData == null || globalIndex < 0)
            {
                Debug.LogWarning($"[Server Card] Bỏ qua card role={deckFaction} slot {sourceIndex}: CardData null hoặc chưa được đăng ký trong ServerCardManager.availableCards.");
                continue;
            }

            NetworkCardInstance card = new NetworkCardInstance
            {
                cardDataIndex = globalIndex,
                currentCooldown = 0,
                // SummonCapturedPawn dùng charge kiếm được khi Rogue hạ Tốt đối thủ, nên bắt đầu từ 0.
                // Các card trong loadout chọn trước trận mặc định chỉ dùng 1 lần/card.
                remainingUses = ResolveInitialCardUses(cardData),
                isInitialized = true
            };

            HandCards.Set(initializedCount, card);
            initializedCount++;
        }

        deckInitializedOnServer = true;
        lastInitializedDeckFaction = deckFaction;

        Debug.Log($"[Server Card] Initialized {initializedCount}/{maxCards} active card(s) for player {Object.InputAuthority}. RoleDeck={deckFaction}, NetworkObject={Object.Id}.");
        return true;
    }

    private ChessFaction ResolveCurrentDeckFaction()
    {
        if (ServerGameManager.Instance == null)
            return ChessFaction.Neutral;

        if (ServerGameManager.Instance.IsKingPlayer(Object.InputAuthority))
            return ChessFaction.ChessRogue;

        if (ServerGameManager.Instance.IsChessPlayer(Object.InputAuthority))
            return ChessFaction.ChessAlliance;

        return ChessFaction.Neutral;
    }

    private List<CardData> GetDeckForFaction(ChessFaction faction)
    {
        List<CardData> selectedDeck = GetSubmittedDeckForFaction(faction);
        if (selectedDeck != null && selectedDeck.Count > 0)
            return selectedDeck;

        List<CardData> roleDeck = faction == ChessFaction.ChessRogue ? rogueKingDeck : chessAllianceDeck;
        List<CardData> filteredRoleDeck = FilterDeckByFaction(roleDeck, faction);
        if (filteredRoleDeck.Count > 0)
            return filteredRoleDeck;

        // Fallback giữ tương thích với prefab cũ đang chỉ có startingDeck.
        List<CardData> filteredLegacyDeck = FilterDeckByFaction(startingDeck, faction);
        if (filteredLegacyDeck.Count > 0)
        {
            Debug.LogWarning($"[Server Card] Role deck {faction} is empty on PlayerNetworkController {Object.Id}. Falling back to filtered legacy startingDeck.");
            return filteredLegacyDeck;
        }

        Debug.LogWarning($"[Server Card] No card deck assigned/submitted for role {faction} on PlayerNetworkController {Object.Id}.");
        return null;
    }

    private List<CardData> GetSubmittedDeckForFaction(ChessFaction faction)
    {
        List<CardData> result = new List<CardData>();
        if (!HasSubmittedCardLoadout || ServerCardManager.Instance == null)
            return result;

        int[] indices = faction == ChessFaction.ChessRogue
            ? new[] { SelectedRogueKingCard0, SelectedRogueKingCard1, SelectedRogueKingCard2 }
            : new[] { SelectedChessAllianceCard0, SelectedChessAllianceCard1, SelectedChessAllianceCard2, SelectedChessAllianceCard3 };

        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if (index < 0 || used.Contains(index))
                continue;

            CardData data = ServerCardManager.Instance.GetCardData(index);
            if (data == null || !ServerCardManager.Instance.IsCardAllowedForFaction(data, faction))
            {
                Debug.LogWarning($"[Server Card] Ignored submitted card index {index} for faction {faction}. Card missing or wrong cardRole.");
                continue;
            }

            used.Add(index);
            result.Add(data);
        }

        return result;
    }

    private List<CardData> FilterDeckByFaction(List<CardData> source, ChessFaction faction)
    {
        List<CardData> result = new List<CardData>();
        if (source == null || ServerCardManager.Instance == null)
            return result;

        HashSet<CardData> used = new HashSet<CardData>();
        for (int i = 0; i < source.Count; i++)
        {
            CardData data = source[i];
            if (data == null || used.Contains(data))
                continue;

            if (!ServerCardManager.Instance.IsCardAllowedForFaction(data, faction))
            {
                Debug.LogWarning($"[Server Card] Card '{data.cardName}' is in the {faction} deck but cardRole={data.cardRole}. Skipped.");
                continue;
            }

            used.Add(data);
            result.Add(data);
        }

        return result;
    }

    private int GetMaxCardsForFaction(ChessFaction faction)
    {
        return faction == ChessFaction.ChessRogue
            ? Mathf.Max(1, maxRogueKingActiveCards)
            : Mathf.Max(1, maxChessAllianceActiveCards);
    }

    private int ResolveInitialCardUses(CardData cardData)
    {
        if (cardData == null)
            return 0;

        if (cardData.effectType == CardEffectType.SummonCapturedPawn)
            return 0;

        return selectedLoadoutCardsUseSingleUse ? 1 : Mathf.Max(1, cardData.maxUses);
    }

    private void ClearAllHandCards()
    {
        for (int i = 0; i < HandCards.Length; i++)
        {
            HandCards.Set(i, default);
        }

        ClearCardUseSilenceBackup(restoreStoredUses: false);
    }

    public bool IsCardUseSilenced => cardUseSilenceActive;

    public bool ApplyOneTurnCardUseSilence(string reason = null)
    {
        if (!HasStateAuthority) return false;

        if (cardUseSilenceActive)
        {
            Debug.Log($"[Server Card] Card-use silence is already active for {Object.InputAuthority}. Reason={reason}");
            return true;
        }

        ClearCardUseSilenceBackup(restoreStoredUses: false);

        int limit = Mathf.Min(HandCards.Length, cardSilenceStoredUses.Length);
        bool foundAnyCard = false;

        for (int i = 0; i < limit; i++)
        {
            NetworkCardInstance card = HandCards[i];
            if (!card.isInitialized)
                continue;

            foundAnyCard = true;
            cardSilenceStoredSlotActive[i] = true;
            cardSilenceStoredUses[i] = card.remainingUses;
            cardSilenceStoredCardDataIndex[i] = card.cardDataIndex;

            if (card.remainingUses != 0)
            {
                card.remainingUses = 0;
                HandCards.Set(i, card);
            }
        }

        cardUseSilenceActive = true;
        Debug.Log($"[Server Card] BishopSilence locked card uses for {Object.InputAuthority}. CardsFound={foundAnyCard}, Reason={reason}");
        return true;
    }

    public bool RestoreCardUsesAfterOneTurnSilence()
    {
        if (!HasStateAuthority || !cardUseSilenceActive)
            return false;

        int limit = Mathf.Min(HandCards.Length, cardSilenceStoredUses.Length);
        int restoredCount = 0;

        for (int i = 0; i < limit; i++)
        {
            if (!cardSilenceStoredSlotActive[i])
                continue;

            NetworkCardInstance card = HandCards[i];
            if (!card.isInitialized || card.cardDataIndex != cardSilenceStoredCardDataIndex[i])
                continue;

            card.remainingUses = Mathf.Max(0, cardSilenceStoredUses[i]);
            HandCards.Set(i, card);
            restoredCount++;
        }

        ClearCardUseSilenceBackup(restoreStoredUses: false);
        Debug.Log($"[Server Card] BishopSilence ended for {Object.InputAuthority}. RestoredUsesSlots={restoredCount}.");
        return true;
    }

    private void ClearCardUseSilenceBackup(bool restoreStoredUses)
    {
        if (restoreStoredUses)
            RestoreCardUsesAfterOneTurnSilence();

        cardUseSilenceActive = false;

        for (int i = 0; i < cardSilenceStoredUses.Length; i++)
        {
            cardSilenceStoredUses[i] = 0;
            cardSilenceStoredCardDataIndex[i] = -1;
            cardSilenceStoredSlotActive[i] = false;
        }
    }

    private void TrySubmitLocalCardLoadoutToServer()
    {
        if (!HasInputAuthority || localCardLoadoutSubmitted)
            return;

        PlayerSelectedCardLoadout.Load();
        int[] rogue = PlayerSelectedCardLoadout.GetRogueKingCardIndices();
        int[] chess = PlayerSelectedCardLoadout.GetChessAllianceCardIndices();

        Rpc_SubmitSelectedCardLoadout(
            GetArrayValueOrDefault(rogue, 0),
            GetArrayValueOrDefault(rogue, 1),
            GetArrayValueOrDefault(rogue, 2),
            GetArrayValueOrDefault(chess, 0),
            GetArrayValueOrDefault(chess, 1),
            GetArrayValueOrDefault(chess, 2),
            GetArrayValueOrDefault(chess, 3)
        );

        localCardLoadoutSubmitted = true;
    }

    private static int GetArrayValueOrDefault(int[] values, int index)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : -1;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_SubmitSelectedCardLoadout(int r0, int r1, int r2, int c0, int c1, int c2, int c3, RpcInfo info = default)
    {
        if (!HasStateAuthority || info.Source != Object.InputAuthority)
            return;

        ApplySubmittedCardLoadout(r0, r1, r2, c0, c1, c2, c3);
        ServerRebuildHandForCurrentRole(force: true);
    }

    private void ApplySubmittedCardLoadout(int r0, int r1, int r2, int c0, int c1, int c2, int c3)
    {
        SelectedRogueKingCard0 = SanitizeSubmittedCardIndex(r0, ChessFaction.ChessRogue, -1, -1, -1);
        SelectedRogueKingCard1 = SanitizeSubmittedCardIndex(r1, ChessFaction.ChessRogue, SelectedRogueKingCard0, -1, -1);
        SelectedRogueKingCard2 = SanitizeSubmittedCardIndex(r2, ChessFaction.ChessRogue, SelectedRogueKingCard0, SelectedRogueKingCard1, -1);

        SelectedChessAllianceCard0 = SanitizeSubmittedCardIndex(c0, ChessFaction.ChessAlliance, -1, -1, -1);
        SelectedChessAllianceCard1 = SanitizeSubmittedCardIndex(c1, ChessFaction.ChessAlliance, SelectedChessAllianceCard0, -1, -1);
        SelectedChessAllianceCard2 = SanitizeSubmittedCardIndex(c2, ChessFaction.ChessAlliance, SelectedChessAllianceCard0, SelectedChessAllianceCard1, -1);
        SelectedChessAllianceCard3 = SanitizeSubmittedCardIndex(c3, ChessFaction.ChessAlliance, SelectedChessAllianceCard0, SelectedChessAllianceCard1, SelectedChessAllianceCard2);

        HasSubmittedCardLoadout = true;

        Debug.Log($"[Server CardLoadout] Player {Object.InputAuthority} submitted selected cards. Rogue=[{SelectedRogueKingCard0},{SelectedRogueKingCard1},{SelectedRogueKingCard2}], Chess=[{SelectedChessAllianceCard0},{SelectedChessAllianceCard1},{SelectedChessAllianceCard2},{SelectedChessAllianceCard3}]");
    }

    private int SanitizeSubmittedCardIndex(int cardIndex, ChessFaction faction, int used0, int used1, int used2)
    {
        if (cardIndex < 0)
            return -1;

        if (cardIndex == used0 || cardIndex == used1 || cardIndex == used2)
            return -1;

        if (ServerCardManager.Instance == null || !ServerCardManager.Instance.IsCardIndexAllowedForFaction(cardIndex, faction))
            return -1;

        return cardIndex;
    }

    private void ClearSubmittedCardLoadout()
    {
        if (!HasStateAuthority)
            return;

        HasSubmittedCardLoadout = false;
        SelectedRogueKingCard0 = -1;
        SelectedRogueKingCard1 = -1;
        SelectedRogueKingCard2 = -1;
        SelectedChessAllianceCard0 = -1;
        SelectedChessAllianceCard1 = -1;
        SelectedChessAllianceCard2 = -1;
        SelectedChessAllianceCard3 = -1;
    }

    // Kênh RPC: Client Gửi Yêu Cầu Xài Bài Lên Server
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void Rpc_RequestPlayCard(int handIndex, Vector2Int targetPos, RpcInfo info = default)
    {
        if (ServerCardManager.Instance == null || ServerGameManager.Instance == null) return;
        if (!ServerGameManager.Instance.CanPlayerAct(info.Source)) return;
        if (cardUseSilenceActive)
        {
            Debug.Log($"[Server Card] Player {info.Source} cannot use cards this turn because BishopSilence is active.");
            return;
        }

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

    public void ClearTemporaryCardState()
    {
        if (!HasStateAuthority) return;

        // Runtime-only card effects must not leak through PhaseTransition/GameOver/reset.
        hasExtraTurn = false;
        ClearCardUseSilenceBackup(restoreStoredUses: false);
        ResetDynamicCardUses(CardEffectType.SummonCapturedPawn);
    }

    public void AddCardUses(CardEffectType effectType, int amount)
    {
        if (!HasStateAuthority || amount <= 0 || ServerCardManager.Instance == null) return;

        for (int i = 0; i < HandCards.Length; i++)
        {
            NetworkCardInstance card = HandCards[i];
            if (!card.isInitialized) continue;

            CardData data = ServerCardManager.Instance.GetCardData(card.cardDataIndex);
            if (data == null || data.effectType != effectType) continue;

            int cap = Mathf.Max(1, data.maxUses);
            int before = card.remainingUses;
            card.remainingUses = Mathf.Clamp(card.remainingUses + amount, 0, cap);
            HandCards.Set(i, card);

            if (card.remainingUses != before)
            {
                Debug.Log($"[Server Card] Added {card.remainingUses - before} use(s) to '{data.cardName}' for {Object.InputAuthority}. Uses={card.remainingUses}/{cap}");
            }

            return;
        }
    }

    private void ResetDynamicCardUses(CardEffectType effectType)
    {
        if (!HasStateAuthority || ServerCardManager.Instance == null) return;

        for (int i = 0; i < HandCards.Length; i++)
        {
            NetworkCardInstance card = HandCards[i];
            if (!card.isInitialized) continue;

            CardData data = ServerCardManager.Instance.GetCardData(card.cardDataIndex);
            if (data == null || data.effectType != effectType) continue;

            if (card.remainingUses != 0 || card.currentCooldown != 0)
            {
                card.remainingUses = 0;
                card.currentCooldown = 0;
                HandCards.Set(i, card);
            }
        }
    }

    public void StartAimingCard(int slotIndex, CardData data)
    {
        if (data == null)
            return;

        bool needsTarget = ServerCardManager.Instance != null
            ? ServerCardManager.Instance.DoesCardNeedBoardTarget(data)
            : DoesCardNeedBoardTargetFallback(data);

        if (needsTarget)
        {
            pendingCardSlotIndex = slotIndex;
            pendingCardData = data;
            currentState = ClientInputState.AimingCard;

            ClearAllHighlights();
            BuildAndShowCardTargetHighlights(data);

            Debug.Log($"🟨 [Client Input] Đang ngắm thẻ '{data.cardName}'. Hãy CLICK CHỌN 1 Ô ĐƯỢC HIGHLIGHT TRÊN BÀN CỜ để sử dụng!");
        }
        else
        {
            Debug.Log($"🟩 [Client Input] Thẻ '{data.cardName}' không cần mục tiêu. Highlight nhanh các quân bị/được ảnh hưởng rồi kích hoạt!");
            FlashInstantCardAffectedTiles(data);
            Rpc_RequestPlayCard(slotIndex, new Vector2Int(-1, -1));
        }
    }

    private void BuildAndShowCardTargetHighlights(CardData data)
    {
        currentCardTargetTiles.Clear();

        if (!showCardTargetHighlights || data == null || chessBoard == null)
            return;

        currentCardTargetTiles.AddRange(BuildCardTargetTiles(data));
        ShowHighlightTiles(currentCardTargetTiles, cardSelectableTargetHighlightState);

        if (debugInputLogs)
            Debug.Log($"[Client Card] Highlighted {currentCardTargetTiles.Count} valid target tile(s) for card '{data.cardName}'.");
    }

    private ChessPieceData ResolvePieceData(NetworkChessPiece piece, NetworkPieceSnapshot snapshot)
    {
        if (piece == null)
            return null;

        ChessPieceData pieceData = null;

        try
        {
            pieceData = piece.PieceData;
        }
        catch (System.InvalidOperationException)
        {
            return null;
        }

        if (pieceData == null && ServerBoardManager.Instance != null)
            pieceData = ServerBoardManager.Instance.GetPieceDataByIndex(snapshot.PieceDataIndex);

        return pieceData;
    }

    private List<Vector2Int> BuildCardTargetTiles(CardData data)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        if (data == null)
            return result;

        ChessFaction myFaction = ResolveLocalCardFaction();
        if (myFaction == ChessFaction.Neutral)
            return result;

        if (data.effectType == CardEffectType.SummonCapturedPawn || data.effectType == CardEffectType.KingRevive)
        {
            AddAllEmptyBoardTiles(result);
            return result;
        }

        if (data.effectType == CardEffectType.KingDash)
        {
            AddKingDashTargetTiles(result, myFaction, Mathf.Max(1, data.effectValue1 <= 0 ? 3 : data.effectValue1));
            return result;
        }

        foreach (NetworkChessPiece piece in FindObjectsByType<NetworkChessPiece>(FindObjectsSortMode.None))
        {
            if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot snapshot))
                continue;

            ChessPieceData pieceData = ResolvePieceData(piece, snapshot);
            if (pieceData == null)
                continue;

            bool isFriendly = snapshot.Faction == myFaction;
            bool isEnemy = snapshot.Faction != myFaction && snapshot.Faction != ChessFaction.Neutral;
            string pieceName = pieceData.pieceName ?? string.Empty;

            bool allowed = false;

            switch (data.effectType)
            {
                case CardEffectType.Recall:
                    allowed = isFriendly;
                    break;

                case CardEffectType.PawnShield:
                    allowed = isFriendly && pieceName.Contains("Pawn");
                    break;

                case CardEffectType.KingDash:
                case CardEffectType.KingSweep:
                    allowed = isFriendly && pieceName.Contains("King");
                    break;

                default:
                    if (!string.IsNullOrEmpty(data.requiredTargetName))
                        allowed = isFriendly && pieceName.Contains(data.requiredTargetName);
                    break;
            }

            if (allowed && !result.Contains(snapshot.GridPos))
                result.Add(snapshot.GridPos);
        }

        return result;
    }

    private void AddKingDashTargetTiles(List<Vector2Int> result, ChessFaction myFaction, int range)
    {
        if (result == null || chessBoard == null)
            return;

        NetworkChessPiece kingPiece = FindRogueKingPiece();
        if (kingPiece == null || !TryGetNetworkPieceSnapshot(kingPiece, out NetworkPieceSnapshot kingSnapshot))
            return;

        if (kingSnapshot.Faction != myFaction)
            return;

        BoardData previewBoard = BuildClientPreviewBoard(out _);
        if (previewBoard == null)
            return;

        Vector2Int[] directions = new Vector2Int[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        Vector2Int start = kingSnapshot.GridPos;
        range = Mathf.Max(1, range);

        for (int dirIndex = 0; dirIndex < directions.Length; dirIndex++)
        {
            Vector2Int dir = directions[dirIndex];
            for (int step = 1; step <= range; step++)
            {
                Vector2Int pos = start + dir * step;
                if (!previewBoard.IsValidPosition(pos.x, pos.y))
                    break;

                if (!previewBoard.IsTileEmptyForMovement(pos.x, pos.y))
                    break;

                if (!result.Contains(pos))
                    result.Add(pos);
            }
        }
    }

    private void FlashInstantCardAffectedTiles(CardData data)
    {
        if (!flashInstantCardAffectedTiles || data == null || chessBoard == null)
            return;

        StopCardAffectedFlashRoutine();

        currentCardAffectedTiles.Clear();
        currentCardAffectedTiles.AddRange(BuildInstantCardAffectedTiles(data));

        if (currentCardAffectedTiles.Count == 0)
            return;

        ShowHighlightTiles(currentCardAffectedTiles, instantCardAffectedHighlightState);
        cardAffectedFlashRoutine = StartCoroutine(CardAffectedFlashRoutine());

        if (debugInputLogs)
            Debug.Log($"[Client Card] Flash highlighted {currentCardAffectedTiles.Count} affected tile(s) for instant card '{data.cardName}'.");
    }

    private System.Collections.IEnumerator CardAffectedFlashRoutine()
    {
        float delay = Mathf.Max(0.05f, instantCardAffectedFlashSeconds);
        yield return new WaitForSeconds(delay);

        ClearHighlightTiles(currentCardAffectedTiles);
        cardAffectedFlashRoutine = null;
    }

    private List<Vector2Int> BuildInstantCardAffectedTiles(CardData data)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        if (data == null)
            return result;

        ChessFaction myFaction = ResolveLocalCardFaction();
        if (myFaction == ChessFaction.Neutral)
            return result;

        ChessFaction enemyFaction = myFaction == ChessFaction.ChessRogue ? ChessFaction.ChessAlliance : ChessFaction.ChessRogue;

        foreach (NetworkChessPiece piece in FindObjectsByType<NetworkChessPiece>(FindObjectsSortMode.None))
        {
            if (!TryGetNetworkPieceSnapshot(piece, out NetworkPieceSnapshot snapshot))
                continue;

            ChessPieceData pieceData = ResolvePieceData(piece, snapshot);
            if (pieceData == null)
                continue;

            string pieceName = pieceData.pieceName ?? string.Empty;
            bool affected = false;

            switch (data.effectType)
            {
                case CardEffectType.BishopSilence:
                    affected = snapshot.Faction == enemyFaction && pieceName.Contains("King");
                    break;

                case CardEffectType.March:
                case CardEffectType.PawnForwardAttack:
                    affected = snapshot.Faction == myFaction && pieceName.Contains("Pawn");
                    break;

                case CardEffectType.SuperBuff:
                case CardEffectType.ExtraTurn:
                    affected = snapshot.Faction == myFaction && pieceName.Contains("King");
                    break;

                case CardEffectType.KingRevive:
                    // Revive affects a graveyard piece, not a current board piece.
                    // There is no visible board target to preview safely.
                    affected = false;
                    break;
            }

            if (affected && !result.Contains(snapshot.GridPos))
                result.Add(snapshot.GridPos);
        }

        return result;
    }

    private void AddAllEmptyBoardTiles(List<Vector2Int> result)
    {
        if (result == null || chessBoard == null)
            return;

        BoardData previewBoard = BuildClientPreviewBoard(out _);
        if (previewBoard == null)
            return;

        for (int x = 0; x < previewBoard.width; x++)
        {
            for (int y = 0; y < previewBoard.height; y++)
            {
                if (previewBoard.IsTileEmptyForMovement(x, y))
                    result.Add(new Vector2Int(x, y));
            }
        }
    }

    private bool IsPendingCardTargetLocallyAllowed(Vector2Int cellPos)
    {
        if (pendingCardData == null)
            return false;

        // If no highlight list was built, allow the click and let the server be authoritative.
        // This keeps compatibility if a future card needs an unusual target.
        if (currentCardTargetTiles.Count == 0)
            return true;

        return currentCardTargetTiles.Contains(cellPos);
    }

    private ChessFaction ResolveLocalCardFaction()
    {
        if (ServerGameManager.Instance == null)
            return ChessFaction.Neutral;

        if (ServerGameManager.Instance.IsKingPlayer(Object.InputAuthority))
            return ChessFaction.ChessRogue;

        if (ServerGameManager.Instance.IsChessPlayer(Object.InputAuthority))
            return ChessFaction.ChessAlliance;

        return ChessFaction.Neutral;
    }

    private static bool DoesCardNeedBoardTargetFallback(CardData data)
    {
        if (data == null)
            return false;

        if (data.effectType == CardEffectType.SuperBuff)
            return false;

        if (!string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        switch (data.effectType)
        {
            case CardEffectType.Recall:
            case CardEffectType.SummonCapturedPawn:
            case CardEffectType.KingRevive:
            case CardEffectType.PawnShield:
            case CardEffectType.KingDash:
            case CardEffectType.KingSweep:
                return true;
            default:
                return false;
        }
    }

    private void OnHandCardsChanged()
    {
        // Khi Server cập nhật mảng bài (VD: vừa xài xong, vừa trừ Cooldown, hoặc vừa đổi phase/deck), báo cho giao diện Client Refresh
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
