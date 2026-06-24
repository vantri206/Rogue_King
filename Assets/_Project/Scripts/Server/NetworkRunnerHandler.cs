using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

#pragma warning disable IDE0051

public enum KingOnlineRunnerMode
{
    Auto,
    Server,
    Client,
    Host
}

public class NetworkRunnerHandler : MonoBehaviour, INetworkRunnerCallbacks
{
    private static NetworkRunnerHandler activeHandler;

    public static NetworkRunnerHandler Active => activeHandler;

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;

    [Header("Startup")]
    [SerializeField] private KingOnlineRunnerMode startupMode = KingOnlineRunnerMode.Auto;
    [SerializeField] private string defaultSessionName = "RogueKingRoom";
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private bool autoStart = true;

    [Header("Matchmaking / Menu")]
    [Tooltip("Use -1 to publish the current active scene. If the server starts from a menu scene, set this to the gameplay scene build index.")]
    [SerializeField] private int gameplaySceneBuildIndex = -1;

    [Tooltip("Scene build index used by clients when they are disconnected/kicked back to menu.")]
    [SerializeField] private int menuSceneBuildIndex = 0;

    [Tooltip("When a client is disconnected from a match, load MenuScene again instead of leaving the player stuck in PlayScene.")]
    [SerializeField] private bool returnClientToMenuOnDisconnect = true;

    [Tooltip("Delay before client loads menu after a server kick/disconnect. A short delay lets Fusion finish shutdown cleanly.")]
    [SerializeField] private float returnToMenuDelaySeconds = 0.25f;

    [Tooltip("After Kick All, wait briefly for Fusion OnPlayerLeft callbacks before reopening the room.")]
    [SerializeField] private float reopenAfterKickDelaySeconds = 0.75f;

    [Tooltip("If true, reduce Unity log stack traces for normal Log/Warning messages in player builds so server UI button logs are readable.")]
    [SerializeField] private bool reduceRuntimeStackTraces = true;

    [Tooltip("If true, server scene can be different from client PlayScene as long as both builds keep the same gameplay scene index and do not rely on mismatched scene NetworkObjects.")]
    [SerializeField] private bool allowSeparateServerSceneAtSameBuildIndex = true;

    [Tooltip("Prefix used by generated server session names when -uniqueSession is supplied.")]
    [SerializeField] private string generatedSessionPrefix = "RogueKingRoom";

    [Header("Live Lobby / Menu Leaderboard")]
    [Tooltip("Fusion session used by MenuScene clients to receive live leaderboard snapshots without occupying the match room.")]
    [SerializeField] private string lobbySessionName = "RogueKingLobby";

    [Tooltip("Maximum players allowed to stay connected in the menu/lobby session.")]
    [SerializeField] private int lobbyMaxPlayers = 10;

    [Tooltip("Scene index published by the lobby server. Keep this as MenuScene index 0.")]
    [SerializeField] private int lobbySceneBuildIndex = 0;

    [Tooltip("Match session that paired lobby players will join. Start a normal match server with this same session name.")]
    [SerializeField] private string lobbyMatchSessionName = "RogueKingRoom";

    [Tooltip("Small delay before a client leaves lobby and joins the match room after receiving the match-found RPC.")]
    [SerializeField] private float lobbyToMatchSwitchDelaySeconds = 0.15f;

    [Tooltip("Maximum time a client waits for the lobby runner shutdown task before forcing cleanup and joining the match room.")]
    [SerializeField] private float lobbyShutdownTimeoutSeconds = 3f;

    [Tooltip("Small delay after destroying the lobby runner before creating the match runner on the same bootstrap object.")]
    [SerializeField] private float lobbyJoinDelayAfterShutdownSeconds = 0.25f;

    [Header("Editor / ParrelSync Test")]
    [Tooltip("Auto mode: the original Editor becomes the dedicated server; ParrelSync clones become clients.")]
    [SerializeField] private bool originalEditorIsServer = true;
    [SerializeField] private bool parrelSyncClonesAreClients = true;

    [Header("Prefabs Setup")]
    [SerializeField] private NetworkPrefabRef serverManagersPrefab;
    [SerializeField] private NetworkPrefabRef playerControllerPrefab;

    private readonly List<PlayerRef> connectedPlayers = new List<PlayerRef>();
    private readonly List<PlayerRef> activeMatchPlayers = new List<PlayerRef>();
    private readonly Dictionary<PlayerRef, NetworkObject> playerControllers = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkObject serverManagersObject;
    private bool matchStarted;
    private bool matchAbandoned;
    private bool serverSceneReady;
    private bool isKickingAllPlayers;
    private bool clientReturnToMenuQueued;

    private bool runtimeForceClient;
    private bool runtimeQuickMatch;
    private bool runtimeLobbyMode;
    private bool currentRunIsLobby;
    private bool clientSwitchingFromLobbyToMatch;
    private bool pendingLobbyMatchRequest;
    private bool pendingLeaderboardRefreshRequest;
    private string runtimeSessionOverride;
    private readonly List<PlayerRef> lobbyReadyPlayers = new List<PlayerRef>();

    private void Awake()
    {
        if (activeHandler != null && activeHandler != this)
        {
            Debug.LogWarning("[NetworkRunnerHandler] Duplicate NetworkRunnerHandler found after scene load. Destroying duplicate bootstrap.");
            Destroy(gameObject);
            return;
        }

        activeHandler = this;

        ApplyRuntimeLogSettings();

        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }

    private void ApplyRuntimeLogSettings()
    {
        if (!reduceRuntimeStackTraces)
            return;

        // Development/visual server builds can print a full stack trace for every Debug.Log,
        // which makes normal button/status logs look like errors. Keep stack traces only for real errors.
        Application.SetStackTraceLogType(UnityEngine.LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(UnityEngine.LogType.Warning, StackTraceLogType.None);
    }

    private async void Start()
    {
        if (ShouldAutoStartOnBoot())
        {
            await StartGame();
        }
        else
        {
            Debug.Log("[NetworkRunnerHandler] Boot ready. AutoStart is disabled, waiting for menu/action.");
        }
    }

    private void Update()
    {
        FlushPendingClientLobbyRequests();
    }

    private void FlushPendingClientLobbyRequests()
    {
        if (runner == null || runner.IsServer || !currentRunIsLobby)
            return;

        PlayerNetworkController controller = GetLocalPlayerController();
        if (controller == null)
            return;

        if (pendingLeaderboardRefreshRequest)
        {
            pendingLeaderboardRefreshRequest = false;
            Debug.Log("[Lobby] Sending queued leaderboard refresh request now that local player object is ready.");
            controller.ClientRequestLeaderboardRefresh();
        }

        if (pendingLobbyMatchRequest)
        {
            pendingLobbyMatchRequest = false;
            Debug.Log("[Lobby] Sending queued find-match request now that local player object is ready.");
            controller.ClientRequestFindMatchFromLobby();
        }
    }

    private bool ShouldAutoStartOnBoot()
    {
        string[] args = Environment.GetCommandLineArgs();

        // A dedicated server build can now share the same Build Settings as clients:
        // 0 = MenuScene, 1 = PlayScene. Even if MenuScene Auto Start is disabled
        // for player builds, command-line server/batchmode must still start automatically.
        if (HasArg(args, "-server") || HasArg(args, "-dedicated") || HasArg(args, "-lobbyserver") || Application.isBatchMode)
            return true;

        if (HasArg(args, "-host"))
            return true;

        if (HasArg(args, "-lobbyclient"))
            return true;

        // Optional direct client matchmaking from command line. Normal menu clients should
        // use only -client and wait for the Find Match button.
        if (HasArg(args, "-quickmatch") || HasArg(args, "-join") || HasArg(args, "-autojoin"))
            return true;

        return autoStart;
    }

    public async void UI_StartQuickMatch()
    {
        await StartClientQuickMatch();
    }

    public async void UI_JoinDefaultSession()
    {
        await StartClientJoinSession(defaultSessionName);
    }

    public Task<bool> StartClientQuickMatch()
    {
        runtimeForceClient = true;
        runtimeQuickMatch = true;
        runtimeLobbyMode = false;
        runtimeSessionOverride = null;
        return StartGame();
    }

    public Task<bool> StartClientJoinSession(string sessionName)
    {
        runtimeForceClient = true;
        runtimeQuickMatch = false;
        runtimeLobbyMode = false;
        runtimeSessionOverride = sessionName;
        return StartGame();
    }

    public Task<bool> StartClientLobby()
    {
        runtimeForceClient = true;
        runtimeQuickMatch = false;
        runtimeLobbyMode = true;
        runtimeSessionOverride = null;
        return StartGame();
    }

    public bool ClientRequestLobbyMatchmaking()
    {
        if (!currentRunIsLobby || runner == null || runner.IsServer)
        {
            Debug.LogWarning("[Lobby] Cannot request lobby matchmaking because this client is not connected to the lobby session.");
            return false;
        }

        PlayerNetworkController controller = GetLocalPlayerController();
        if (controller == null)
        {
            // PlayerObject replication can arrive a few frames after the lobby connection succeeds.
            // Do not fail the button click; queue it and send as soon as the local controller is ready.
            pendingLobbyMatchRequest = true;
            Debug.LogWarning("[Lobby] Local PlayerNetworkController is not ready yet. Find-match request queued.");
            return true;
        }

        pendingLobbyMatchRequest = false;
        controller.ClientRequestFindMatchFromLobby();
        return true;
    }

    public bool ClientRequestLeaderboardRefresh()
    {
        if (runner == null || runner.IsServer || !currentRunIsLobby)
            return false;

        PlayerNetworkController controller = GetLocalPlayerController();
        if (controller == null)
        {
            pendingLeaderboardRefreshRequest = true;
            Debug.LogWarning("[Lobby] Local PlayerNetworkController is not ready yet. Leaderboard refresh queued.");
            return true;
        }

        pendingLeaderboardRefreshRequest = false;
        controller.ClientRequestLeaderboardRefresh();
        return true;
    }

    private PlayerNetworkController GetLocalPlayerController()
    {
        if (runner == null || runner.IsServer)
            return null;

        NetworkObject playerObject = runner.GetPlayerObject(runner.LocalPlayer);
        PlayerNetworkController mappedController = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
        if (mappedController != null)
            return mappedController;

        // Fallback for the first frames after spawn, before Runner.GetPlayerObject is visible on the client.
        PlayerNetworkController[] controllers = FindObjectsByType<PlayerNetworkController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerNetworkController controller = controllers[i];
            if (controller != null && controller.Object != null && controller.HasInputAuthority)
                return controller;
        }

        return null;
    }

    public bool TryGetPlayerController(PlayerRef player, out PlayerNetworkController controller)
    {
        controller = null;

        if (runner != null)
        {
            NetworkObject playerObject = runner.GetPlayerObject(player);
            controller = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                return true;
        }

        PlayerNetworkController[] controllers = FindObjectsByType<PlayerNetworkController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerNetworkController candidate = controllers[i];
            if (candidate != null && candidate.Object != null && candidate.Object.InputAuthority == player)
            {
                controller = candidate;
                return true;
            }
        }

        return false;
    }

    public void ClientSwitchFromLobbyToMatchSession(string matchSessionName)
    {
        if (string.IsNullOrWhiteSpace(matchSessionName))
            matchSessionName = lobbyMatchSessionName;

        if (clientSwitchingFromLobbyToMatch)
            return;

        StartCoroutine(ClientSwitchFromLobbyToMatchRoutine(matchSessionName.Trim()));
    }

    private System.Collections.IEnumerator ClientSwitchFromLobbyToMatchRoutine(string matchSessionName)
    {
        clientSwitchingFromLobbyToMatch = true;

        if (string.IsNullOrWhiteSpace(matchSessionName))
            matchSessionName = lobbyMatchSessionName;

        matchSessionName = matchSessionName.Trim();
        Debug.Log($"[Lobby] Match found. Preparing lobby->match switch. Target match session='{matchSessionName}'.");

        // Important: set the next run state before shutting down the lobby runner.
        // If OnShutdown/OnDisconnected fires during Shutdown(), this flag prevents the normal
        // disconnect path from loading MenuScene again.
        runtimeForceClient = true;
        runtimeQuickMatch = false;
        runtimeLobbyMode = false;
        runtimeSessionOverride = matchSessionName;

        pendingLobbyMatchRequest = false;
        pendingLeaderboardRefreshRequest = false;
        clientReturnToMenuQueued = false;

        float delay = Mathf.Max(0f, lobbyToMatchSwitchDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        NetworkRunner oldRunner = runner;
        NetworkSceneManagerDefault oldSceneManager = sceneManager;

        // Detach callbacks before shutdown so the intentional lobby leave cannot enqueue the
        // normal "return to menu" fallback or mutate state while this coroutine is switching sessions.
        if (oldRunner != null)
        {
            oldRunner.RemoveCallbacks(this);

            Task shutdownTask = null;
            try
            {
                Debug.Log("[Lobby] Shutting down lobby runner before joining match session...");
                shutdownTask = oldRunner.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Lobby] Exception while shutting down lobby runner. Will force cleanup and continue. {exception.GetType().Name}: {exception.Message}");
            }

            float timeout = Mathf.Max(0.5f, lobbyShutdownTimeoutSeconds);
            float startTime = Time.realtimeSinceStartup;
            while (shutdownTask != null && !shutdownTask.IsCompleted && Time.realtimeSinceStartup - startTime < timeout)
                yield return null;

            if (shutdownTask != null && !shutdownTask.IsCompleted)
            {
                Debug.LogWarning($"[Lobby] Lobby runner shutdown did not complete within {timeout:0.0}s. Forcing local cleanup and continuing to match join.");
            }
            else if (shutdownTask != null && shutdownTask.IsFaulted)
            {
                Debug.LogWarning($"[Lobby] Lobby runner shutdown task faulted. Forcing local cleanup and continuing to match join. {shutdownTask.Exception?.GetBaseException().Message}");
            }
            else
            {
                Debug.Log("[Lobby] Lobby runner shutdown completed.");
            }
        }

        DestroyRunnerObject(oldRunner, oldSceneManager);

        runner = null;
        sceneManager = null;
        connectedPlayers.Clear();
        activeMatchPlayers.Clear();
        lobbyReadyPlayers.Clear();
        playerControllers.Clear();
        serverManagersObject = null;
        matchStarted = false;
        matchAbandoned = false;
        serverSceneReady = false;
        clientReturnToMenuQueued = false;
        currentRunIsLobby = false;

        float joinDelay = Mathf.Max(0f, lobbyJoinDelayAfterShutdownSeconds);
        if (joinDelay > 0f)
            yield return new WaitForSecondsRealtime(joinDelay);
        else
            yield return null;

        Debug.Log($"[Lobby] Starting match client runner. Session='{matchSessionName}'.");

        Task<bool> joinTask = StartGame();
        while (joinTask != null && !joinTask.IsCompleted)
            yield return null;

        bool joinSucceeded = joinTask != null && joinTask.IsCompleted && !joinTask.IsFaulted && !joinTask.IsCanceled && joinTask.Result;
        if (joinSucceeded)
        {
            Debug.Log($"[Lobby] Successfully joined match session '{matchSessionName}'.");
        }
        else
        {
            if (joinTask != null && joinTask.IsFaulted)
                Debug.LogWarning($"[Lobby] Failed to join match session '{matchSessionName}'. Exception={joinTask.Exception?.GetBaseException().Message}");
            else
                Debug.LogWarning($"[Lobby] Failed to join match session '{matchSessionName}'. Make sure the match server is running, open, visible, and in the same Photon region.");
        }

        clientSwitchingFromLobbyToMatch = false;
    }

    public async Task<bool> StartGame()
    {
        if (runner != null)
        {
            Debug.LogWarning("[NetworkRunnerHandler] Runner already started.");
            return true;
        }

        GameMode gameMode = ResolveGameMode();
        currentRunIsLobby = ResolveLobbySessionMode(gameMode);
        maxPlayers = ResolveMaxPlayers();

        bool quickMatch = currentRunIsLobby ? false : ResolveQuickMatchMode(gameMode);
        string sessionName = ResolveSessionName(gameMode, quickMatch);
        string region = ResolveRegion();

        // Keep the NetworkRunner on its own child GameObject.
        // Fusion Shutdown() may destroy the runner GameObject. If the runner lives on this
        // NetworkRunnerHandler bootstrap object, the lobby->match coroutine can be killed
        // immediately after leaving the lobby and never reach StartGame() for RogueKingRoom.
        GameObject runnerObject = new GameObject(currentRunIsLobby ? "LobbyNetworkRunner" : "MatchNetworkRunner");
        runnerObject.transform.SetParent(transform, false);

        runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = gameMode == GameMode.Client || gameMode == GameMode.Host;
        runner.AddCallbacks(this);

        sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

        StartGameArgs args = new StartGameArgs()
        {
            GameMode = gameMode,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            EnableClientSessionCreation = false,
            OnGameStarted = OnGameStarted
        };

        // In Server/Host mode the authoritative peer publishes the gameplay scene.
        // Clients will join the session and follow the server scene.
        if (gameMode == GameMode.Server || gameMode == GameMode.Host)
        {
            args.Scene = BuildServerSceneInfo();
            args.IsOpen = true;
            args.IsVisible = true;
        }

        string sessionLog = string.IsNullOrWhiteSpace(sessionName) ? "<QuickMatch/RandomOpenSession>" : sessionName;
        Debug.Log($"[NetworkRunnerHandler] Starting Fusion as {gameMode}, Session='{sessionLog}', QuickMatch={quickMatch}, MaxPlayers={maxPlayers}, ProvideInput={runner.ProvideInput}");

        if (!string.IsNullOrWhiteSpace(region))
        {
            Debug.Log($"[NetworkRunnerHandler] Region arg requested: '{region}'. Make sure server and clients use the same Photon Fixed Region/Allowlist.");
        }

        StartGameResult result = await runner.StartGame(args);
        if (!result.Ok)
        {
            Debug.LogError($"[NetworkRunnerHandler] StartGame failed: {result.ShutdownReason}");
            CleanupFailedRunner();
            return false;
        }

        Debug.Log("[NetworkRunnerHandler] StartGame succeeded.");
        return true;
    }

    private void CleanupFailedRunner()
    {
        if (runner == null)
            return;

        runner.RemoveCallbacks(this);
        DestroyRunnerObject(runner, sceneManager);
        runner = null;
        sceneManager = null;

        runtimeForceClient = false;
        runtimeQuickMatch = false;
        runtimeLobbyMode = false;
        currentRunIsLobby = false;
        pendingLobbyMatchRequest = false;
        pendingLeaderboardRefreshRequest = false;
        runtimeSessionOverride = null;
    }

    private void DestroyRunnerObject(NetworkRunner runnerToDestroy, NetworkSceneManagerDefault sceneManagerToDestroy)
    {
        if (runnerToDestroy == null)
        {
            if (sceneManagerToDestroy != null)
                Destroy(sceneManagerToDestroy);
            return;
        }

        GameObject runnerGameObject = runnerToDestroy.gameObject;

        // The runner should normally live on a dedicated child object. Never destroy this
        // bootstrap GameObject during lobby->match switching, or the coroutine/state is lost.
        if (runnerGameObject != null && runnerGameObject != gameObject)
        {
            Destroy(runnerGameObject);
            return;
        }

        Destroy(runnerToDestroy);
        if (sceneManagerToDestroy != null)
            Destroy(sceneManagerToDestroy);
    }


    public bool HasRunnerStarted => runner != null;
    public bool IsServerRunner => runner != null && runner.IsServer;
    public bool IsMatchStarted => matchStarted;
    public bool IsMatchAbandoned => matchAbandoned;
    public bool IsServerSceneReady => serverSceneReady;
    public bool IsKickOperationRunning => isKickingAllPlayers;
    public int ConnectedPlayerCount => connectedPlayers.Count;
    public int ActiveMatchPlayerCount => activeMatchPlayers.Count;
    public int ConfiguredMaxPlayers => maxPlayers;
    public int LobbyReadyPlayerCount => lobbyReadyPlayers.Count;
    public bool IsLobbyRunner => currentRunIsLobby;
    public bool IsClientConnectedToLobby => runner != null && !runner.IsServer && currentRunIsLobby;

    public string CurrentSessionName
    {
        get
        {
            if (runner == null || runner.SessionInfo == null)
                return string.Empty;

            return runner.SessionInfo.Name;
        }
    }

    public bool IsCurrentSessionJoinable
    {
        get
        {
            if (runner == null || runner.SessionInfo == null)
                return false;

            return runner.SessionInfo.IsOpen && runner.SessionInfo.IsVisible;
        }
    }

    public IReadOnlyList<PlayerRef> GetConnectedPlayersSnapshot()
    {
        return new List<PlayerRef>(connectedPlayers);
    }

    public string BuildServerStatusText()
    {
        StringBuilder builder = new StringBuilder(512);
        builder.AppendLine("KING ONLINE SERVER");
        builder.AppendLine($"Runner: {(runner == null ? "Not started" : runner.IsServer ? "Server" : "Non-server")}");
        builder.AppendLine($"Scene Ready: {serverSceneReady}");
        builder.AppendLine($"Mode: {(currentRunIsLobby ? "Lobby/MenuLeaderboard" : "Match")}");
        builder.AppendLine($"Session: {(string.IsNullOrWhiteSpace(CurrentSessionName) ? "<none>" : CurrentSessionName)}");
        builder.AppendLine($"Joinable: {IsCurrentSessionJoinable}");
        builder.AppendLine($"Players: {connectedPlayers.Count}/{maxPlayers}");
        if (currentRunIsLobby)
            builder.AppendLine($"Lobby Ready Queue: {lobbyReadyPlayers.Count}");
        builder.AppendLine($"Match Started: {matchStarted}");
        builder.AppendLine($"Match Abandoned: {matchAbandoned}");
        builder.AppendLine($"Kick/Reopen Busy: {isKickingAllPlayers}");

        if (ServerGameManager.Instance != null)
        {
            builder.AppendLine($"State: {ServerGameManager.Instance.currentGameState}");
            builder.AppendLine($"Phase: {ServerGameManager.Instance.currentPhase}");
            builder.AppendLine($"King: {FormatPlayer(ServerGameManager.Instance.kingPlayer)}");
            builder.AppendLine($"Chess: {FormatPlayer(ServerGameManager.Instance.chessPlayer)}");
        }
        else
        {
            builder.AppendLine("State: <ServerGameManager missing>");
        }

        if (connectedPlayers.Count > 0)
        {
            builder.AppendLine("Connected:");
            for (int i = 0; i < connectedPlayers.Count; i++)
                builder.AppendLine($"- Slot {i + 1}: {FormatPlayer(connectedPlayers[i])}");
        }

        return builder.ToString();
    }

    public bool ServerRestartCurrentMatch()
    {
        if (runner == null || !runner.IsServer)
        {
            Debug.LogWarning("[Server Control] Restart ignored: runner is not a server.");
            return false;
        }

        if (connectedPlayers.Count < maxPlayers)
        {
            Debug.LogWarning($"[Server Control] Restart ignored: not enough players ({connectedPlayers.Count}/{maxPlayers}).");
            SetSessionJoinable(connectedPlayers.Count == 0);
            return false;
        }

        if (ServerGameManager.Instance != null)
            ServerGameManager.Instance.ResetToLobby();

        matchStarted = false;
        matchAbandoned = false;
        activeMatchPlayers.Clear();

        SpawnServerManagersIfNeeded(runner);

        foreach (PlayerRef player in connectedPlayers)
            SpawnPlayerControllerIfNeeded(runner, player);

        TryStartMatch();

        Debug.Log("[Server Control] Current connected pair restarted.");
        return true;
    }

    public bool ServerKickAllPlayersAndReopen()
    {
        if (runner == null || !runner.IsServer)
        {
            Debug.LogWarning("[Server Control] Kick/reopen ignored: runner is not a server.");
            return false;
        }

        if (isKickingAllPlayers)
        {
            Debug.LogWarning("[Server Control] Kick/reopen ignored: a kick operation is already running.");
            return false;
        }

        StartCoroutine(ServerKickAllPlayersAndReopenRoutine());
        return true;
    }

    private System.Collections.IEnumerator ServerKickAllPlayersAndReopenRoutine()
    {
        isKickingAllPlayers = true;
        SetSessionJoinable(false);

        List<PlayerRef> playersToKick = new List<PlayerRef>(connectedPlayers);
        Debug.Log($"[Server Control] Kicking {playersToKick.Count} player(s) and preparing to reopen the room.");

        foreach (PlayerRef player in playersToKick)
        {
            if (runner == null || !runner.IsServer)
                break;

            // Disconnect first. Clients handle the disconnect locally and load MenuScene.
            runner.Disconnect(player);
        }

        float waitSeconds = Mathf.Max(0.1f, reopenAfterKickDelaySeconds);
        yield return new WaitForSecondsRealtime(waitSeconds);

        if (runner == null || !runner.IsServer)
        {
            isKickingAllPlayers = false;
            yield break;
        }

        foreach (PlayerRef player in playersToKick)
        {
            if (playerControllers.TryGetValue(player, out NetworkObject controller) && controller != null)
            {
                runner.Despawn(controller);
            }

            playerControllers.Remove(player);
            runner.SetPlayerObject(player, null);
        }

        connectedPlayers.Clear();
        activeMatchPlayers.Clear();
        playerControllers.Clear();
        matchStarted = false;
        matchAbandoned = false;

        if (ServerGameManager.Instance != null)
            ServerGameManager.Instance.ResetToLobby();

        SetSessionJoinable(true);
        isKickingAllPlayers = false;
        Debug.Log("[Server Control] All players disconnected/kicked. Match state reset and session reopened.");
    }

    public bool ServerLockSession()
    {
        if (runner == null || !runner.IsServer)
            return false;

        SetSessionJoinable(false);
        return true;
    }

    public bool ServerUnlockSessionIfIdle()
    {
        if (runner == null || !runner.IsServer)
            return false;

        if (connectedPlayers.Count > 0 || matchStarted)
        {
            Debug.LogWarning("[Server Control] Unlock ignored: disconnect players or reset the room before reopening.");
            return false;
        }

        matchAbandoned = false;
        activeMatchPlayers.Clear();

        if (ServerGameManager.Instance != null)
            ServerGameManager.Instance.ResetToLobby();

        SetSessionJoinable(true);
        return true;
    }

    public void ServerQuitApplication()
    {
        if (runner != null && runner.IsServer)
        {
            Debug.Log("[Server Control] Quit requested from server debug UI.");
            Application.Quit();
        }
    }

    private static string FormatPlayer(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return "<none>";

        return $"Player {player.PlayerId}";
    }

    private void OnGameStarted(NetworkRunner startedRunner)
    {
        // Do not spawn server managers here.
        // With NetworkSceneManagerDefault the scene can still be loading, so spawning here can create a manager
        // that later gets duplicated when scene objects are registered. Spawn after OnSceneLoadDone instead.
    }

    private NetworkSceneInfo BuildServerSceneInfo()
    {
        int sceneIndex = currentRunIsLobby ? ResolveLobbySceneBuildIndex() : ResolveGameplaySceneBuildIndex();
        SceneRef sceneRef = SceneRef.FromIndex(sceneIndex);
        NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
        sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);
        return sceneInfo;
    }

    private int ResolveGameplaySceneBuildIndex()
    {
        string[] args = Environment.GetCommandLineArgs();
        int fromCommandLine = GetArgIntValue(args, "-sceneIndex", gameplaySceneBuildIndex);

        if (fromCommandLine >= 0)
            return fromCommandLine;

        return SceneManager.GetActiveScene().buildIndex;
    }

    private int ResolveLobbySceneBuildIndex()
    {
        string[] args = Environment.GetCommandLineArgs();
        int fromCommandLine = GetArgIntValue(args, "-sceneIndex", lobbySceneBuildIndex);
        return Mathf.Max(0, fromCommandLine);
    }

    private bool ResolveLobbySessionMode(GameMode gameMode)
    {
        if (runtimeLobbyMode)
            return true;

        string[] args = Environment.GetCommandLineArgs();
        return HasArg(args, "-lobby") || HasArg(args, "-lobbyserver") || HasArg(args, "-lobbyclient");
    }

    private GameMode ResolveGameMode()
    {
        if (runtimeForceClient)
            return GameMode.Client;

        string[] args = Environment.GetCommandLineArgs();

        if (HasArg(args, "-lobbyserver"))
            return GameMode.Server;

        if (HasArg(args, "-lobbyclient"))
            return GameMode.Client;

        if (HasArg(args, "-server") || HasArg(args, "-dedicated") || Application.isBatchMode)
            return GameMode.Server;

        if (HasArg(args, "-client") || HasArg(args, "-quickmatch") || HasArg(args, "-join") || HasArg(args, "-autojoin"))
            return GameMode.Client;

        if (HasArg(args, "-host"))
            return GameMode.Host;

        switch (startupMode)
        {
            case KingOnlineRunnerMode.Server: return GameMode.Server;
            case KingOnlineRunnerMode.Client: return GameMode.Client;
            case KingOnlineRunnerMode.Host: return GameMode.Host;
        }

#if UNITY_EDITOR
        if (parrelSyncClonesAreClients && IsParrelSyncClone())
            return GameMode.Client;

        if (originalEditorIsServer)
            return GameMode.Server;
#endif

        // Safer default for production: a normal player build should not accidentally host.
        return GameMode.Client;
    }

    private int ResolveMaxPlayers()
    {
        string[] args = Environment.GetCommandLineArgs();
        int fallback = currentRunIsLobby ? Mathf.Max(1, lobbyMaxPlayers) : maxPlayers;
        int resolved = GetArgIntValue(args, currentRunIsLobby ? "-lobbyMaxPlayers" : "-maxPlayers", fallback);
        resolved = GetArgIntValue(args, "-maxPlayers", resolved);
        return Mathf.Max(1, resolved);
    }

    private bool ResolveQuickMatchMode(GameMode gameMode)
    {
        if (gameMode != GameMode.Client)
            return false;

        if (runtimeQuickMatch)
            return true;

        string[] args = Environment.GetCommandLineArgs();
        return HasArg(args, "-quickmatch");
    }

    private string ResolveSessionName(GameMode gameMode, bool quickMatch)
    {
        string[] args = Environment.GetCommandLineArgs();

        if (currentRunIsLobby)
            return GetArgValue(args, "-lobbySession", lobbySessionName).Trim();

        if (gameMode == GameMode.Client && quickMatch)
            return null;

        if (!string.IsNullOrWhiteSpace(runtimeSessionOverride))
            return runtimeSessionOverride.Trim();

        string explicitSession = GetArgValue(args, "-session", GetArgValue(args, "-room", null));
        if (!string.IsNullOrWhiteSpace(explicitSession))
            return explicitSession.Trim();

        if ((gameMode == GameMode.Server || gameMode == GameMode.Host) && HasArg(args, "-uniqueSession"))
            return GenerateUniqueSessionName(args);

        return defaultSessionName;
    }

    private string GenerateUniqueSessionName(string[] args)
    {
        string prefix = GetArgValue(args, "-sessionPrefix", generatedSessionPrefix);
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "RogueKingRoom";

        return $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(1000, 9999)}";
    }

    private string ResolveRegion()
    {
        string[] args = Environment.GetCommandLineArgs();
        return GetArgValue(args, "-region", string.Empty);
    }

    private static bool HasArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string GetArgValue(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return fallback;
    }

    private static int GetArgIntValue(string[] args, string name, int fallback)
    {
        string value = GetArgValue(args, name, null);
        if (int.TryParse(value, out int parsed))
            return parsed;

        return fallback;
    }

#if UNITY_EDITOR
    private static bool IsParrelSyncClone()
    {
        // Reflection keeps this script compilable even when ParrelSync is not installed.
        string[] typeNames =
        {
            "ParrelSync.ClonesManager, ParrelSync",
            "ParrelSync.ClonesManager, ParrelSync.Editor",
            "ParrelSync.ClonesManager, Assembly-CSharp-Editor"
        };

        foreach (string typeName in typeNames)
        {
            Type cloneManagerType = Type.GetType(typeName);
            if (cloneManagerType == null) continue;

            MethodInfo isCloneMethod = cloneManagerType.GetMethod("IsClone", BindingFlags.Public | BindingFlags.Static);
            if (isCloneMethod != null && isCloneMethod.ReturnType == typeof(bool))
            {
                return (bool)isCloneMethod.Invoke(null, null);
            }
        }

        return false;
    }
#endif

    private bool IsMatchLockedForNewPlayers()
    {
        // Once a 2-player match has started, this runner owns that match until it is reset.
        // This remains locked even after one player leaves, so Player 3 cannot fill the freed slot
        // and accidentally join Player 1's in-progress/abandoned board.
        return matchStarted || matchAbandoned || activeMatchPlayers.Count >= maxPlayers;
    }

    private void SetSessionJoinable(bool joinable)
    {
        if (runner == null || runner.SessionInfo == null)
            return;

        runner.SessionInfo.IsOpen = joinable;
        runner.SessionInfo.IsVisible = joinable;

        Debug.Log($"[Server Matchmaking] Session joinable={joinable}. IsOpen={runner.SessionInfo.IsOpen}, IsVisible={runner.SessionInfo.IsVisible}");
    }

    private void RejectLateJoiner(NetworkRunner activeRunner, PlayerRef player, string reason)
    {
        Debug.LogWarning($"[Server Matchmaking] Rejecting Player {player.PlayerId}: {reason}");

        connectedPlayers.Remove(player);

        if (playerControllers.TryGetValue(player, out NetworkObject controller) && controller != null)
            activeRunner.Despawn(controller);

        playerControllers.Remove(player);
        activeRunner.SetPlayerObject(player, null);
        activeRunner.Disconnect(player);
    }

    private void ResetMatchWhenRoomEmpty()
    {
        if (connectedPlayers.Count > 0)
            return;

        matchStarted = false;
        matchAbandoned = false;
        activeMatchPlayers.Clear();
        playerControllers.Clear();

        if (ServerGameManager.Instance != null)
            ServerGameManager.Instance.ResetToLobby();

        SetSessionJoinable(true);
        Debug.Log("[Server Matchmaking] Room is empty. Match state reset and session reopened for a fresh pair.");
    }

    private void SpawnServerManagersIfNeeded(NetworkRunner activeRunner)
    {
        if (!activeRunner.IsServer) return;

        // Existing singleton can happen when a scene object was registered by Fusion.
        if (ServerGameManager.Instance != null && ServerGameManager.Instance.Object != null)
        {
            serverManagersObject = ServerGameManager.Instance.Object;
            return;
        }

        if (serverManagersObject != null)
            return;

        if (!serverManagersPrefab.IsValid)
        {
            Debug.LogError("[Server] Server Managers Prefab is not assigned.");
            return;
        }

        serverManagersObject = activeRunner.Spawn(serverManagersPrefab, Vector3.zero, Quaternion.identity);
        Debug.Log("[Server] Server managers spawned with StateAuthority only. No local player is attached.");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        if (connectedPlayers.Contains(player))
        {
            Debug.LogWarning($"[Server] Ignored duplicate OnPlayerJoined for Player {player.PlayerId}.");
            SpawnPlayerControllerIfNeeded(runner, player);
            return;
        }

        if (!currentRunIsLobby && IsMatchLockedForNewPlayers())
        {
            RejectLateJoiner(runner, player, "match already started/locked");
            return;
        }

        if (connectedPlayers.Count >= maxPlayers)
        {
            RejectLateJoiner(runner, player, currentRunIsLobby ? "lobby is full" : "room is full");
            return;
        }

        connectedPlayers.Add(player);

        Debug.Log($"[Server] Player {player.PlayerId} joined {(currentRunIsLobby ? "lobby" : "match room")}. Players={connectedPlayers.Count}/{maxPlayers}");

        SetSessionJoinable(connectedPlayers.Count < maxPlayers);

        if (serverSceneReady)
        {
            SpawnServerManagersIfNeeded(runner);
            SpawnPlayerControllerIfNeeded(runner, player);

            if (!currentRunIsLobby)
                TryStartMatch();
        }
        else
        {
            Debug.Log("[Server] Player joined before scene was ready. Player controller and match start will continue after scene load done.");
        }
    }


    public bool ServerPlayerRequestedLobbyMatch(PlayerRef player)
    {
        if (runner == null || !runner.IsServer || !currentRunIsLobby)
            return false;

        if (!connectedPlayers.Contains(player))
        {
            Debug.LogWarning($"[Lobby] Ignored ready request from non-connected Player {player.PlayerId}.");
            return false;
        }

        if (!lobbyReadyPlayers.Contains(player))
            lobbyReadyPlayers.Add(player);

        Debug.Log($"[Lobby] Player {player.PlayerId} is ready. Ready={lobbyReadyPlayers.Count}/2, Connected={connectedPlayers.Count}/{maxPlayers}");
        TryDispatchLobbyMatch();
        return true;
    }

    public bool ServerPlayerRequestedLeaderboardRefresh(PlayerRef player)
    {
        if (runner == null || !runner.IsServer)
            return false;

        NetworkObject playerObject = runner.GetPlayerObject(player);
        PlayerNetworkController controller = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
        if (controller == null || ServerLeaderboardManager.Instance == null)
            return false;

        ServerLeaderboardManager.Instance.PushLeaderboardToPlayer(controller);
        return true;
    }

    private void TryDispatchLobbyMatch()
    {
        if (!currentRunIsLobby)
            return;

        lobbyReadyPlayers.RemoveAll(player => !connectedPlayers.Contains(player));

        if (lobbyReadyPlayers.Count < 2)
            return;

        PlayerRef p1 = lobbyReadyPlayers[0];
        PlayerRef p2 = lobbyReadyPlayers[1];
        lobbyReadyPlayers.RemoveRange(0, 2);

        string sessionName = string.IsNullOrWhiteSpace(lobbyMatchSessionName) ? defaultSessionName : lobbyMatchSessionName.Trim();

        Debug.Log($"[Lobby] Matched Player {p1.PlayerId} + Player {p2.PlayerId}. Sending both to match session '{sessionName}'.");
        SendLobbyMatchFound(p1, sessionName);
        SendLobbyMatchFound(p2, sessionName);
    }

    private void SendLobbyMatchFound(PlayerRef player, string sessionName)
    {
        NetworkObject playerObject = runner != null ? runner.GetPlayerObject(player) : null;
        PlayerNetworkController controller = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
        if (controller == null)
        {
            Debug.LogWarning($"[Lobby] Cannot send match-found to Player {player.PlayerId}: PlayerNetworkController missing.");
            return;
        }

        controller.ServerSendLobbyMatchFound(sessionName);
    }

    private void SpawnPlayerControllerIfNeeded(NetworkRunner activeRunner, PlayerRef player)
    {
        NetworkObject mappedObject = activeRunner.GetPlayerObject(player);
        if (mappedObject != null)
        {
            playerControllers[player] = mappedObject;
            Debug.Log($"[Server] Ignored duplicate controller spawn for Player {player.PlayerId}: Runner already has PlayerObject {mappedObject.name}.");
            return;
        }

        if (playerControllers.TryGetValue(player, out NetworkObject existingController) && existingController != null)
        {
            activeRunner.SetPlayerObject(player, existingController);
            Debug.Log($"[Server] Reused existing PlayerController for Player {player.PlayerId} and restored Runner PlayerObject mapping.");
            return;
        }

        if (!playerControllerPrefab.IsValid)
        {
            Debug.LogError("[Server] Player Controller Prefab is not assigned.");
            return;
        }

        NetworkObject controller = activeRunner.Spawn(playerControllerPrefab, Vector3.zero, Quaternion.identity, player);
        playerControllers[player] = controller;
        activeRunner.SetPlayerObject(player, controller);

        Debug.Log($"[Server] Spawned PlayerController for Player {player.PlayerId}. InputAuthority={controller.InputAuthority}.");
    }

    private void TryStartMatch()
    {
        if (matchStarted) return;
        if (!serverSceneReady) return;
        if (connectedPlayers.Count < maxPlayers) return;
        if (ServerGameManager.Instance == null)
        {
            Debug.LogWarning("[Server] Waiting for ServerGameManager before starting match.");
            return;
        }

        matchStarted = true;
        matchAbandoned = false;
        activeMatchPlayers.Clear();
        activeMatchPlayers.Add(connectedPlayers[0]);
        activeMatchPlayers.Add(connectedPlayers[1]);
        SetSessionJoinable(false);

        ServerGameManager.Instance.AssignRoles(connectedPlayers[0], connectedPlayers[1]);
        ServerGameManager.Instance.ChangeState(NetGameState.Setup);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;

        bool playerWasInActiveMatch = activeMatchPlayers.Contains(player);

        if (!currentRunIsLobby && !isKickingAllPlayers && matchStarted && playerWasInActiveMatch)
        {
            matchAbandoned = true;
            SetSessionJoinable(false);

            // Record forfeit before despawning/removing the player's controller so the leaderboard
            // manager can still read the leaving player's GuestId/Elo profile.
            if (ServerGameManager.Instance != null)
                ServerGameManager.Instance.AbortMatchBecausePlayerLeft(player);
        }

        connectedPlayers.Remove(player);
        lobbyReadyPlayers.Remove(player);

        if (ServerLeaderboardManager.Instance != null)
            ServerLeaderboardManager.Instance.ForgetActivePlayer(player);

        if (playerControllers.TryGetValue(player, out NetworkObject controller) && controller != null)
        {
            runner.Despawn(controller);
        }
        playerControllers.Remove(player);
        runner.SetPlayerObject(player, null);

        Debug.Log($"[Server] Player {player.PlayerId} left {(currentRunIsLobby ? "lobby" : "match room")}.");

        if (currentRunIsLobby)
        {
            SetSessionJoinable(connectedPlayers.Count < maxPlayers);
            return;
        }

        if (!isKickingAllPlayers)
            ResetMatchWhenRoomEmpty();
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[Client] Connected to server/session.");
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Client] Disconnected from server/session. Reason={reason}");
        if (!clientSwitchingFromLobbyToMatch)
            QueueClientReturnToMenu($"Disconnected: {reason}");
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[NetworkRunnerHandler] Runner shutdown: {shutdownReason}");

        if (runner != null && !runner.IsServer && !clientSwitchingFromLobbyToMatch)
            QueueClientReturnToMenu($"Runner shutdown: {shutdownReason}");
    }

    private void QueueClientReturnToMenu(string reason)
    {
        if (!returnClientToMenuOnDisconnect)
            return;

        if (clientReturnToMenuQueued)
            return;

        // Server process should never load the player menu because of a disconnect callback.
        if (runner != null && runner.IsServer)
            return;

        clientReturnToMenuQueued = true;
        Debug.Log($"[Client] Returning to menu. Reason={reason}");
        StartCoroutine(ClientReturnToMenuRoutine());
    }

    private System.Collections.IEnumerator ClientReturnToMenuRoutine()
    {
        float delay = Mathf.Max(0f, returnToMenuDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        if (runner != null)
        {
            runner.RemoveCallbacks(this);
            Destroy(runner);
            runner = null;
        }

        if (sceneManager != null)
        {
            Destroy(sceneManager);
            sceneManager = null;
        }

        connectedPlayers.Clear();
        activeMatchPlayers.Clear();
        lobbyReadyPlayers.Clear();
        playerControllers.Clear();
        serverManagersObject = null;
        matchStarted = false;
        matchAbandoned = false;
        serverSceneReady = false;
        runtimeForceClient = false;
        runtimeQuickMatch = false;
        runtimeLobbyMode = false;
        currentRunIsLobby = false;
        pendingLobbyMatchRequest = false;
        pendingLeaderboardRefreshRequest = false;
        runtimeSessionOverride = null;

        int sceneIndex = Mathf.Max(0, menuSceneBuildIndex);
        SceneManager.LoadScene(sceneIndex, LoadSceneMode.Single);

        yield return null;
        clientReturnToMenuQueued = false;
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner != null && runner.IsServer)
        {
            if (currentRunIsLobby)
            {
                if (connectedPlayers.Count >= maxPlayers)
                {
                    Debug.LogWarning("[Lobby] Refused connection request: lobby is full.");
                    request.Refuse();
                    return;
                }

                request.Accept();
                return;
            }

            if (IsMatchLockedForNewPlayers())
            {
                Debug.LogWarning("[Server Matchmaking] Refused connection request: match already started, full, or abandoned.");
                request.Refuse();
                return;
            }

            if (connectedPlayers.Count >= maxPlayers)
            {
                Debug.LogWarning("[Server Matchmaking] Refused connection request: room is full.");
                request.Refuse();
                return;
            }
        }

        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogWarning($"[NetworkRunnerHandler] Connect failed: {reason} remote={remoteAddress}");
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (runner.IsServer)
        {
            serverSceneReady = true;
            SpawnServerManagersIfNeeded(runner);

            foreach (PlayerRef player in connectedPlayers)
            {
                SpawnPlayerControllerIfNeeded(runner, player);
            }

            if (!currentRunIsLobby)
                TryStartMatch();
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }

    private void OnDestroy()
    {
        if (activeHandler == this)
            activeHandler = null;

        if (runner != null)
            runner.RemoveCallbacks(this);
    }
}

#pragma warning restore IDE0051
