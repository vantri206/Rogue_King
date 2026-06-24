using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Client-side cached copy of the last leaderboard snapshot received from the server.
/// This lets MenuScene show a leaderboard tab after a client has connected at least once.
/// It is not authoritative; the server JSON file is the source of truth.
/// </summary>
public static class ClientLeaderboardCache
{
    private const string CacheKey = "kingonline.leaderboard.cache";
    private static readonly List<LeaderboardEntryData> entries = new List<LeaderboardEntryData>();

    public static event Action Changed;

    public static IReadOnlyList<LeaderboardEntryData> Entries => entries;
    public static int Count => entries.Count;

    public static void LoadFromPlayerPrefs()
    {
        string json = PlayerPrefs.GetString(CacheKey, string.Empty);
        entries.Clear();

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                LeaderboardSnapshot snapshot = JsonUtility.FromJson<LeaderboardSnapshot>(json);
                if (snapshot != null && snapshot.players != null)
                {
                    entries.AddRange(snapshot.players
                        .Where(e => e != null)
                        .Select(e => e.Clone()));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Leaderboard Cache] Failed to load cached leaderboard: {exception.Message}");
            }
        }

        SortLocal();
        Changed?.Invoke();
    }

    public static void BeginSnapshot()
    {
        entries.Clear();
        Changed?.Invoke();
    }

    // Backward-compatible overload in case an old call site still exists while patching.
    public static void AddOrUpdateEntry(int rank, string displayName, int avatarId, int elo, int wins, int losses, int draws, int totalMatches)
    {
        AddOrUpdateEntry(rank, string.Empty, displayName, avatarId, elo, wins, losses, draws, totalMatches);
    }

    public static void AddOrUpdateEntry(int rank, string guestId, string displayName, int avatarId, int elo, int wins, int losses, int draws, int totalMatches)
    {
        string safeGuestId = SanitizeGuestId(guestId, rank);
        string safeName = SanitizeDisplayName(displayName, rank);

        LeaderboardEntryData entry = new LeaderboardEntryData
        {
            guestId = safeGuestId,
            displayName = safeName,
            avatarId = Mathf.Max(0, avatarId),
            elo = Mathf.Max(0, elo),
            wins = Mathf.Max(0, wins),
            losses = Mathf.Max(0, losses),
            draws = Mathf.Max(0, draws),
            totalMatches = Mathf.Max(0, totalMatches)
        };

        int existingIndex = entries.FindIndex(e => e != null && !string.IsNullOrWhiteSpace(e.guestId) && string.Equals(e.guestId, safeGuestId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            entries[existingIndex] = entry;
        }
        else if (rank > 0 && rank <= entries.Count)
        {
            entries[rank - 1] = entry;
        }
        else
        {
            int insertIndex = Mathf.Clamp(rank - 1, 0, entries.Count);
            entries.Insert(insertIndex, entry);
        }

        SortLocal();
        Changed?.Invoke();
    }

    public static void CompleteSnapshotAndSave()
    {
        SortLocal();

        LeaderboardSnapshot snapshot = new LeaderboardSnapshot();
        snapshot.players.AddRange(entries
            .Where(e => e != null)
            .Select(e => e.Clone()));

        string json = JsonUtility.ToJson(snapshot);
        PlayerPrefs.SetString(CacheKey, json);
        PlayerPrefs.Save();

        Changed?.Invoke();
    }

    public static LeaderboardEntryData GetEntryByGuestId(string guestId)
    {
        if (string.IsNullOrWhiteSpace(guestId))
            return null;

        LeaderboardEntryData entry = entries.FirstOrDefault(e => e != null && string.Equals(e.guestId, guestId, StringComparison.OrdinalIgnoreCase));
        return entry != null ? entry.Clone() : null;
    }

    public static int GetEloForGuestId(string guestId, int fallbackElo = 1000)
    {
        LeaderboardEntryData entry = GetEntryByGuestId(guestId);
        return entry != null ? Mathf.Max(0, entry.elo) : Mathf.Max(0, fallbackElo);
    }

    public static List<LeaderboardEntryData> GetSortedEntries(int maxCount = -1)
    {
        SortLocal();

        IEnumerable<LeaderboardEntryData> query = entries
            .Where(e => e != null)
            .Select(e => e.Clone());

        if (maxCount > 0)
            query = query.Take(maxCount);

        return query.ToList();
    }

    private static void SortLocal()
    {
        entries.RemoveAll(e => e == null);
        entries.Sort((a, b) =>
        {
            int eloCompare = b.elo.CompareTo(a.elo);
            if (eloCompare != 0) return eloCompare;

            int winsCompare = b.wins.CompareTo(a.wins);
            if (winsCompare != 0) return winsCompare;

            int matchesCompare = b.totalMatches.CompareTo(a.totalMatches);
            if (matchesCompare != 0) return matchesCompare;

            return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string SanitizeGuestId(string guestId, int rank)
    {
        string value = string.IsNullOrWhiteSpace(guestId) ? $"rank_{Mathf.Max(1, rank)}" : guestId.Trim();
        value = value.Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty);

        if (value.Length > 64)
            value = value.Substring(0, 64);

        return string.IsNullOrWhiteSpace(value) ? $"rank_{Mathf.Max(1, rank)}" : value;
    }

    private static string SanitizeDisplayName(string displayName, int rank)
    {
        string value = string.IsNullOrWhiteSpace(displayName) ? $"Player {Mathf.Max(1, rank):00}" : displayName.Trim();
        value = value.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

        while (value.Contains("  "))
            value = value.Replace("  ", " ");

        if (value.Length > 24)
            value = value.Substring(0, 24);

        return string.IsNullOrWhiteSpace(value) ? $"Player {Mathf.Max(1, rank):00}" : value;
    }
}
