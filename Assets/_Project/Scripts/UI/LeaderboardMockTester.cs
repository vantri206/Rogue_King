using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor/dev helper for testing the MenuScene leaderboard UI without running Lobby Server.
/// Add this to a MenuScene GameObject, assign LeaderboardMenuUI, then use the context menu
/// or enable Apply On Start while testing in Play Mode.
///
/// This goes through ClientLeaderboardCache, so LeaderboardMenuUI receives data the same way it
/// receives server snapshots from the live lobby flow.
/// Remove or disable this component before shipping/release builds.
/// </summary>
public class LeaderboardMockTester : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private LeaderboardMenuUI leaderboardUI;

    [Header("Mock Behaviour")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool saveToPlayerPrefs = false;
    [SerializeField] private bool includeLocalPlayer = true;
    [SerializeField] private int mockPlayerCount = 5;

    [Header("Mock Data")]
    [SerializeField] private int baseElo = 1200;
    [SerializeField] private int eloStep = 37;
    [SerializeField] private int avatarCount = 8;

    private readonly string[] mockNames =
    {
        "Rogue King",
        "Chess Master",
        "Knight Fox",
        "Mirror Cat",
        "Board Wizard",
        "Queen Bee",
        "Sniper Pawn",
        "Grenade Rook"
    };

    private void Start()
    {
        if (applyOnStart)
            ApplyMockLeaderboard();
    }

    [ContextMenu("Apply Mock Leaderboard")]
    public void ApplyMockLeaderboard()
    {
        int count = Mathf.Clamp(mockPlayerCount, 1, 50);
        int safeAvatarCount = Mathf.Max(1, avatarCount);

        ClientLeaderboardCache.BeginSnapshot();

        List<LeaderboardEntryData> mockEntries = new List<LeaderboardEntryData>();

        if (includeLocalPlayer)
        {
            PlayerLocalProfile localProfile = PlayerLocalProfile.LoadOrCreate(safeAvatarCount);
            mockEntries.Add(new LeaderboardEntryData
            {
                guestId = localProfile.GuestId,
                displayName = localProfile.DisplayName,
                avatarId = localProfile.AvatarId,
                elo = baseElo + eloStep,
                wins = 3,
                losses = 1,
                draws = 0,
                totalMatches = 4
            });
        }

        for (int i = mockEntries.Count; i < count; i++)
        {
            int rankLikeIndex = i + 1;
            mockEntries.Add(new LeaderboardEntryData
            {
                guestId = $"mock_guest_{rankLikeIndex:00}",
                displayName = mockNames[i % mockNames.Length],
                avatarId = i % safeAvatarCount,
                elo = Mathf.Max(0, baseElo - i * eloStep),
                wins = Mathf.Max(0, 8 - i),
                losses = i % 4,
                draws = i % 2,
                totalMatches = Mathf.Max(1, 8 - i + (i % 4) + (i % 2))
            });
        }

        for (int i = 0; i < mockEntries.Count; i++)
        {
            LeaderboardEntryData entry = mockEntries[i];
            ClientLeaderboardCache.AddOrUpdateEntry(
                i + 1,
                entry.guestId,
                entry.displayName,
                entry.avatarId,
                entry.elo,
                entry.wins,
                entry.losses,
                entry.draws,
                entry.totalMatches);
        }

        if (saveToPlayerPrefs)
            ClientLeaderboardCache.CompleteSnapshotAndSave();

        if (leaderboardUI == null)
            leaderboardUI = FindFirstObjectByType<LeaderboardMenuUI>();

        if (leaderboardUI != null)
            leaderboardUI.RefreshFromCache();

        Debug.Log($"[LeaderboardMockTester] Applied {mockEntries.Count} mock leaderboard player(s). Saved={saveToPlayerPrefs}.");
    }

    [ContextMenu("Clear Mock Leaderboard Cache")]
    public void ClearMockLeaderboardCache()
    {
        ClientLeaderboardCache.BeginSnapshot();

        if (saveToPlayerPrefs)
        {
            ClientLeaderboardCache.CompleteSnapshotAndSave();
        }

        if (leaderboardUI == null)
            leaderboardUI = FindFirstObjectByType<LeaderboardMenuUI>();

        if (leaderboardUI != null)
            leaderboardUI.RefreshFromCache();

        Debug.Log("[LeaderboardMockTester] Cleared mock leaderboard cache.");
    }
}
