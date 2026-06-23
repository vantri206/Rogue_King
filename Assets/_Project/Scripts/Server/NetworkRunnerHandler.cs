using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Reflection;
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

    [Tooltip("Prefix used by generated server session names when -uniqueSession is supplied.")]
    [SerializeField] private string generatedSessionPrefix = "RogueKingRoom";

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

    private bool runtimeForceClient;
    private bool runtimeQuickMatch;
    private string runtimeSessionOverride;

    private void Awake()
    {
        if (activeHandler != null && activeHandler != this)
        {
            Debug.LogWarning("[NetworkRunnerHandler] Duplicate NetworkRunnerHandler found after scene load. Destroying duplicate bootstrap.");
            Destroy(gameObject);
            return;
        }

        activeHandler = this;

        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
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

    private bool ShouldAutoStartOnBoot()
    {
        string[] args = Environment.GetCommandLineArgs();

        // A dedicated server build can now share the same Build Settings as clients:
        // 0 = MenuScene, 1 = PlayScene. Even if MenuScene Auto Start is disabled
        // for player builds, command-line server/batchmode must still start automatically.
        if (HasArg(args, "-server") || HasArg(args, "-dedicated") || Application.isBatchMode)
            return true;

        if (HasArg(args, "-host"))
            return true;

        // Optional direct client matchmaking from command line. Normal menu clients should
        // use only -client and wait for the Find Match button.
        if (HasArg(args, "-quickmatch"))
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
        runtimeSessionOverride = null;
        return StartGame();
    }

    public Task<bool> StartClientJoinSession(string sessionName)
    {
        runtimeForceClient = true;
        runtimeQuickMatch = false;
        runtimeSessionOverride = sessionName;
        return StartGame();
    }

    public async Task<bool> StartGame()
    {
        if (runner != null)
        {
            Debug.LogWarning("[NetworkRunnerHandler] Runner already started.");
            return true;
        }

        GameMode gameMode = ResolveGameMode();
        maxPlayers = ResolveMaxPlayers();

        bool quickMatch = ResolveQuickMatchMode(gameMode);
        string sessionName = ResolveSessionName(gameMode, quickMatch);
        string region = ResolveRegion();

        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = gameMode == GameMode.Client || gameMode == GameMode.Host;
        runner.AddCallbacks(this);

        sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

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
        Destroy(runner);
        runner = null;

        if (sceneManager != null)
        {
            Destroy(sceneManager);
            sceneManager = null;
        }

        runtimeForceClient = false;
        runtimeQuickMatch = false;
        runtimeSessionOverride = null;
    }

    private void OnGameStarted(NetworkRunner startedRunner)
    {
        // Do not spawn server managers here.
        // With NetworkSceneManagerDefault the scene can still be loading, so spawning here can create a manager
        // that later gets duplicated when scene objects are registered. Spawn after OnSceneLoadDone instead.
    }

    private NetworkSceneInfo BuildServerSceneInfo()
    {
        int sceneIndex = ResolveGameplaySceneBuildIndex();
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

    private GameMode ResolveGameMode()
    {
        if (runtimeForceClient)
            return GameMode.Client;

        string[] args = Environment.GetCommandLineArgs();

        if (HasArg(args, "-server") || HasArg(args, "-dedicated") || Application.isBatchMode)
            return GameMode.Server;

        if (HasArg(args, "-client") || HasArg(args, "-quickmatch"))
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
        int resolved = GetArgIntValue(args, "-maxPlayers", maxPlayers);
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

        if (IsMatchLockedForNewPlayers())
        {
            RejectLateJoiner(runner, player, "match already started/locked");
            return;
        }

        if (connectedPlayers.Count >= maxPlayers)
        {
            RejectLateJoiner(runner, player, "room is full");
            return;
        }

        connectedPlayers.Add(player);

        Debug.Log($"[Server] Player {player.PlayerId} joined. Players={connectedPlayers.Count}/{maxPlayers}");

        if (connectedPlayers.Count >= maxPlayers)
            SetSessionJoinable(false);

        if (serverSceneReady)
        {
            SpawnServerManagersIfNeeded(runner);
            SpawnPlayerControllerIfNeeded(runner, player);
            TryStartMatch();
        }
        else
        {
            Debug.Log("[Server] Player joined before scene was ready. Player controller and match start will continue after scene load done.");
        }
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

        connectedPlayers.Remove(player);

        if (playerControllers.TryGetValue(player, out NetworkObject controller) && controller != null)
        {
            runner.Despawn(controller);
        }
        playerControllers.Remove(player);
        runner.SetPlayerObject(player, null);

        Debug.Log($"[Server] Player {player.PlayerId} left.");

        if (matchStarted && playerWasInActiveMatch)
        {
            matchAbandoned = true;
            SetSessionJoinable(false);

            if (ServerGameManager.Instance != null)
                ServerGameManager.Instance.AbortMatchBecausePlayerLeft(player);
        }

        ResetMatchWhenRoomEmpty();
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[Client] Connected to server/session.");
    }

    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Client] Disconnected from server/session. Reason={reason}");
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[NetworkRunnerHandler] Runner shutdown: {shutdownReason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (runner != null && runner.IsServer)
        {
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
