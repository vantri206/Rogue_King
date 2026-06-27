using System;
using UnityEngine;

public static class PlayerSelectedCardLoadout
{
    public const int MaxRogueKingCards = 3;
    public const int MaxChessAllianceCards = 4;

    private const string RogueKey = "KingOnline_SelectedRogueKingCards";
    private const string ChessKey = "KingOnline_SelectedChessAllianceCards";

    private static readonly int[] rogueKingCardIndices = CreateEmpty(MaxRogueKingCards);
    private static readonly int[] chessAllianceCardIndices = CreateEmpty(MaxChessAllianceCards);
    private static bool loaded;

    public static void Load()
    {
        if (loaded)
            return;

        LoadArray(RogueKey, rogueKingCardIndices);
        LoadArray(ChessKey, chessAllianceCardIndices);
        loaded = true;
    }

    public static void Save(int[] rogueIndices, int[] chessIndices)
    {
        CopySanitized(rogueIndices, rogueKingCardIndices);
        CopySanitized(chessIndices, chessAllianceCardIndices);

        PlayerPrefs.SetString(RogueKey, SerializeArray(rogueKingCardIndices));
        PlayerPrefs.SetString(ChessKey, SerializeArray(chessAllianceCardIndices));
        PlayerPrefs.Save();
        loaded = true;
    }

    public static void Clear()
    {
        Fill(rogueKingCardIndices, -1);
        Fill(chessAllianceCardIndices, -1);
        PlayerPrefs.DeleteKey(RogueKey);
        PlayerPrefs.DeleteKey(ChessKey);
        PlayerPrefs.Save();
        loaded = true;
    }

    public static int[] GetRogueKingCardIndices()
    {
        Load();
        return CopyOf(rogueKingCardIndices);
    }

    public static int[] GetChessAllianceCardIndices()
    {
        Load();
        return CopyOf(chessAllianceCardIndices);
    }

    private static int[] CreateEmpty(int length)
    {
        int[] values = new int[length];
        Fill(values, -1);
        return values;
    }

    private static void Fill(int[] values, int value)
    {
        if (values == null)
            return;

        for (int i = 0; i < values.Length; i++)
            values[i] = value;
    }

    private static int[] CopyOf(int[] source)
    {
        if (source == null)
            return Array.Empty<int>();

        int[] copy = new int[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private static void CopySanitized(int[] source, int[] destination)
    {
        Fill(destination, -1);
        if (source == null || destination == null)
            return;

        int count = Mathf.Min(source.Length, destination.Length);
        for (int i = 0; i < count; i++)
            destination[i] = source[i] >= 0 ? source[i] : -1;
    }

    private static string SerializeArray(int[] values)
    {
        if (values == null || values.Length == 0)
            return string.Empty;

        return string.Join(",", values);
    }

    private static void LoadArray(string key, int[] target)
    {
        Fill(target, -1);

        string raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string[] parts = raw.Split(',');
        int count = Mathf.Min(parts.Length, target.Length);
        for (int i = 0; i < count; i++)
        {
            if (int.TryParse(parts[i], out int value) && value >= 0)
                target[i] = value;
        }
    }
}
