using System;
using System.Collections.Generic;

[Serializable]
public class LeaderboardEntryData
{
    public string guestId;
    public string displayName;
    public int avatarId;
    public int elo = 1000;
    public int wins;
    public int losses;
    public int draws;
    public int totalMatches;

    public LeaderboardEntryData Clone()
    {
        return new LeaderboardEntryData
        {
            guestId = guestId,
            displayName = displayName,
            avatarId = avatarId,
            elo = elo,
            wins = wins,
            losses = losses,
            draws = draws,
            totalMatches = totalMatches
        };
    }
}

[Serializable]
public class LeaderboardDatabase
{
    public List<LeaderboardEntryData> players = new List<LeaderboardEntryData>();
}

[Serializable]
public class LeaderboardSnapshot
{
    public List<LeaderboardEntryData> players = new List<LeaderboardEntryData>();
}
