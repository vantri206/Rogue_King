using Fusion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Server-side JSON leaderboard for the current dedicated server build.
/// Source of truth lives in leaderboard.json next to the server exe.
/// </summary>
public class ServerLeaderboardManager : MonoBehaviour
{
    public static ServerLeaderboardManager Instance { get; private set; }

    [Header("Storage")]
    [SerializeField] private string leaderboardFileName = "leaderboard.json";
    [Tooltip("If true, save leaderboard.json next to the built exe. In Editor, this falls back to persistentDataPath.")]
    [SerializeField] private bool saveNextToBuildExe = true;

    [Header("Defaults")]
    [SerializeField] private int startingElo = 1000;
    [SerializeField] private int simpleWinDelta = 16;
    [SerializeField] private int minimumElo = 0;
    [SerializeField] private int maxAvatarId = 7;
    [SerializeField] private int defaultTopCount = 10;

    [Tooltip("Important when lobby server and match server are separate processes sharing the same leaderboard.json. Reload before reads to avoid overwriting newer Elo results.")]
    [SerializeField] private bool reloadJsonBeforeRead = true;

    [Header("Names")]
    [SerializeField] private bool serverAssignsDefaultNames = true;
    [SerializeField] private string generatedNamePrefix = "Player";

    private readonly Dictionary<string, LeaderboardEntryData> byGuestId = new Dictionary<string, LeaderboardEntryData>();
    private readonly Dictionary<PlayerRef, string> activeGuestIdByPlayer = new Dictionary<PlayerRef, string>();
    private readonly Dictionary<string, PlayerRef> activePlayerByGuestId = new Dictionary<string, PlayerRef>();

    private LeaderboardDatabase database = new LeaderboardDatabase();
    private bool loaded;
    private string cachedPath;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public LeaderboardEntryData RegisterOrUpdatePlayer(PlayerRef player, string rawGuestId, string rawDisplayName, int requestedAvatarId)
    {
        PrepareDatabaseForFreshRead();

        string guestId = SanitizeGuestId(rawGuestId, player);
        guestId = ResolveDuplicateActiveGuestId(player, guestId);

        int avatarId = GetServerAssignedAvatarId(guestId);
        LeaderboardEntryData entry = GetOrCreateEntry(guestId, avatarId);

        string resolvedName = ResolveDisplayName(entry, guestId, rawDisplayName);
        entry.displayName = resolvedName;
        entry.avatarId = avatarId;

        activeGuestIdByPlayer[player] = guestId;
        activePlayerByGuestId[guestId] = player;

        SaveDatabase();
        Debug.Log($"[Leaderboard] Registered {player}: {entry.displayName}, GuestId={guestId}, AvatarId={avatarId}, Elo={entry.elo}");

        return entry.Clone();
    }

    public void ForgetActivePlayer(PlayerRef player)
    {
        if (activeGuestIdByPlayer.TryGetValue(player, out string guestId))
        {
            activeGuestIdByPlayer.Remove(player);

            if (activePlayerByGuestId.TryGetValue(guestId, out PlayerRef mappedPlayer) && mappedPlayer == player)
                activePlayerByGuestId.Remove(guestId);
        }
    }

    public bool ApplyMatchResult(PlayerRef winner, PlayerRef loser, string reason = "match_result")
    {
        PrepareDatabaseForFreshRead();

        PlayerNetworkController winnerController = GetController(winner);
        PlayerNetworkController loserController = GetController(loser);

        if (winnerController == null || loserController == null)
        {
            Debug.LogWarning($"[Leaderboard] Cannot apply result. Missing controller. Winner={winner}, Loser={loser}, Reason={reason}");
            return false;
        }

        string winnerGuestId = winnerController.GetGuestIdOrFallback();
        string loserGuestId = loserController.GetGuestIdOrFallback();

        if (string.IsNullOrWhiteSpace(winnerGuestId) || string.IsNullOrWhiteSpace(loserGuestId) || winnerGuestId == loserGuestId)
        {
            Debug.LogWarning($"[Leaderboard] Cannot apply result because GuestIds are invalid or equal. WinnerId={winnerGuestId}, LoserId={loserGuestId}");
            return false;
        }

        LeaderboardEntryData winnerEntry = GetOrCreateEntry(winnerGuestId, winnerController.GetAvatarIdOrDefault());
        LeaderboardEntryData loserEntry = GetOrCreateEntry(loserGuestId, loserController.GetAvatarIdOrDefault());

        winnerEntry.displayName = winnerController.GetDisplayNameOrFallback();
        winnerEntry.avatarId = winnerController.GetAvatarIdOrDefault();
        winnerEntry.wins++;
        winnerEntry.totalMatches++;

        loserEntry.displayName = loserController.GetDisplayNameOrFallback();
        loserEntry.avatarId = loserController.GetAvatarIdOrDefault();
        loserEntry.losses++;
        loserEntry.totalMatches++;

        int delta = Mathf.Max(1, simpleWinDelta);
        winnerEntry.elo = Mathf.Max(minimumElo, winnerEntry.elo + delta);
        loserEntry.elo = Mathf.Max(minimumElo, loserEntry.elo - delta);

        winnerController.ServerSetElo(winnerEntry.elo, delta);
        loserController.ServerSetElo(loserEntry.elo, -delta);

        SaveDatabase();
        Debug.Log($"[Leaderboard] Result saved ({reason}). Winner={winnerEntry.displayName} +{delta} => {winnerEntry.elo}; Loser={loserEntry.displayName} -{delta} => {loserEntry.elo}");

        PushLeaderboardToAllActivePlayers();
        return true;
    }

    public List<LeaderboardEntryData> GetTopPlayers(int count = -1)
    {
        PrepareDatabaseForFreshRead();
        int takeCount = count > 0 ? count : defaultTopCount;

        return database.players
            .Where(e => e != null && !string.IsNullOrWhiteSpace(e.guestId))
            .OrderByDescending(e => e.elo)
            .ThenByDescending(e => e.wins)
            .ThenByDescending(e => e.totalMatches)
            .ThenBy(e => e.displayName)
            .Take(Mathf.Max(1, takeCount))
            .Select(e => e.Clone())
            .ToList();
    }

    public void PushLeaderboardToAllActivePlayers()
    {
        List<LeaderboardEntryData> top = GetTopPlayers(defaultTopCount);

        foreach (PlayerRef player in activeGuestIdByPlayer.Keys.ToList())
        {
            PlayerNetworkController controller = GetController(player);
            if (controller != null)
                controller.ServerPushLeaderboardSnapshot(top);
        }
    }

    public void PushLeaderboardToPlayer(PlayerNetworkController controller)
    {
        if (controller == null)
            return;

        controller.ServerPushLeaderboardSnapshot(GetTopPlayers(defaultTopCount));
    }

    private LeaderboardEntryData GetOrCreateEntry(string guestId, int avatarId)
    {
        EnsureLoaded();

        if (byGuestId.TryGetValue(guestId, out LeaderboardEntryData existing))
            return existing;

        LeaderboardEntryData entry = new LeaderboardEntryData
        {
            guestId = guestId,
            displayName = GenerateStableDisplayName(guestId),
            avatarId = Mathf.Clamp(avatarId, 0, Mathf.Max(0, maxAvatarId)),
            elo = Mathf.Max(0, startingElo),
            wins = 0,
            losses = 0,
            draws = 0,
            totalMatches = 0
        };

        database.players.Add(entry);
        byGuestId[guestId] = entry;
        return entry;
    }

    private void PrepareDatabaseForFreshRead()
    {
        if (reloadJsonBeforeRead && loaded)
        {
            loaded = false;
            database = new LeaderboardDatabase();
            byGuestId.Clear();
        }

        EnsureLoaded();
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        cachedPath = ResolveLeaderboardPath();

        try
        {
            if (File.Exists(cachedPath))
            {
                string json = File.ReadAllText(cachedPath);
                LeaderboardDatabase loadedDatabase = JsonUtility.FromJson<LeaderboardDatabase>(json);
                if (loadedDatabase != null && loadedDatabase.players != null)
                    database = loadedDatabase;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Leaderboard] Failed to read {cachedPath}: {exception.Message}. A new database will be created.");
            database = new LeaderboardDatabase();
        }

        if (database.players == null)
            database.players = new List<LeaderboardEntryData>();

        byGuestId.Clear();
        foreach (LeaderboardEntryData entry in database.players)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.guestId))
                continue;

            if (string.IsNullOrWhiteSpace(entry.displayName))
                entry.displayName = GenerateStableDisplayName(entry.guestId);

            entry.elo = Mathf.Max(minimumElo, entry.elo <= 0 ? startingElo : entry.elo);
            entry.avatarId = Mathf.Clamp(entry.avatarId, 0, Mathf.Max(0, maxAvatarId));
            byGuestId[entry.guestId] = entry;
        }

        SaveDatabase();
        Debug.Log($"[Leaderboard] Loaded {database.players.Count} player(s) from {cachedPath}");
    }

    private void SaveDatabase()
    {
        EnsureLoaded();

        try
        {
            string directory = Path.GetDirectoryName(cachedPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            database.players = database.players
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.guestId))
                .OrderByDescending(e => e.elo)
                .ThenByDescending(e => e.wins)
                .ThenByDescending(e => e.totalMatches)
                .ThenBy(e => e.displayName)
                .ToList();

            string json = JsonUtility.ToJson(database, true);
            File.WriteAllText(cachedPath, json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Leaderboard] Failed to save {cachedPath}: {exception}");
        }
    }

    private string ResolveLeaderboardPath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.persistentDataPath, leaderboardFileName);
#else
        if (saveNextToBuildExe)
        {
            string exeFolder = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(exeFolder, leaderboardFileName);
        }

        return Path.Combine(Application.persistentDataPath, leaderboardFileName);
#endif
    }

    private string ResolveDuplicateActiveGuestId(PlayerRef player, string guestId)
    {
        if (activePlayerByGuestId.TryGetValue(guestId, out PlayerRef existingPlayer) && existingPlayer != player)
        {
            string duplicateId = $"{guestId}_session_{player.PlayerId}";
            Debug.LogWarning($"[Leaderboard] Duplicate GuestId detected in the same match. Player {player.PlayerId} will use temporary test id: {duplicateId}");
            return duplicateId;
        }

        return guestId;
    }

    private string ResolveDisplayName(LeaderboardEntryData entry, string guestId, string rawDisplayName)
    {
        string safeRaw = SanitizeDisplayName(rawDisplayName);

        if (!serverAssignsDefaultNames && !string.IsNullOrWhiteSpace(safeRaw))
            return safeRaw;

        if (string.IsNullOrWhiteSpace(safeRaw) || LooksLikeGeneratedName(safeRaw))
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.displayName) && !LooksLikeGeneratedName(entry.displayName))
                return entry.displayName;

            return GenerateStableDisplayName(guestId);
        }

        return safeRaw;
    }

    private string GenerateStableDisplayName(string guestId)
    {
        int number = 1000 + (StablePositiveHash(guestId) % 9000);
        return $"{generatedNamePrefix} {number}";
    }

    private int GetServerAssignedAvatarId(string guestId)
    {
        int avatarCount = Mathf.Max(1, maxAvatarId + 1);
        return StablePositiveHash(guestId) % avatarCount;
    }

    private static int StablePositiveHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            value = Guid.NewGuid().ToString("N");

        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];

            return hash & int.MaxValue;
        }
    }

    private static bool LooksLikeGeneratedName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();
        return value.StartsWith("Guest_", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Guest ", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Player_", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("Player ", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeGuestId(string rawGuestId, PlayerRef player)
    {
        string value = string.IsNullOrWhiteSpace(rawGuestId) ? $"guest_{player.PlayerId}" : rawGuestId.Trim();
        value = value.Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty);

        if (value.Length > 64)
            value = value.Substring(0, 64);

        return string.IsNullOrWhiteSpace(value) ? $"guest_{player.PlayerId}" : value;
    }

    private static string SanitizeDisplayName(string rawDisplayName)
    {
        string value = string.IsNullOrWhiteSpace(rawDisplayName) ? string.Empty : rawDisplayName.Trim();
        value = value.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

        while (value.Contains("  "))
            value = value.Replace("  ", " ");

        if (value.Length > 24)
            value = value.Substring(0, 24);

        return value;
    }

    private PlayerNetworkController GetController(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return null;

        // Lobby server does not need/own ServerGameManager, so do not depend on it here.
        // The previous implementation returned null in lobby mode, which meant the server
        // loaded/updated leaderboard.json but could not push live snapshots to lobby clients.
        if (NetworkRunnerHandler.Active != null && NetworkRunnerHandler.Active.TryGetPlayerController(player, out PlayerNetworkController handlerController))
            return handlerController;

        if (ServerGameManager.Instance != null && ServerGameManager.Instance.Runner != null)
        {
            NetworkObject playerObject = ServerGameManager.Instance.Runner.GetPlayerObject(player);
            PlayerNetworkController controller = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
            if (controller != null)
                return controller;
        }

        PlayerNetworkController[] controllers = FindObjectsByType<PlayerNetworkController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerNetworkController candidate = controllers[i];
            if (candidate != null && candidate.Object != null && candidate.Object.InputAuthority == player)
                return candidate;
        }

        return null;
    }
}
