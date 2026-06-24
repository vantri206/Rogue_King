using System;
using UnityEngine;

/// <summary>
/// Local-only guest profile. This is not trusted gameplay data.
/// Normal double-click client uses one stable PlayerPrefs profile for this machine/build.
/// Local multi-client testing can use -profileSlot ClientA / ClientB to get two separate guest profiles on one PC.
/// </summary>
[Serializable]
public class PlayerLocalProfile
{
    public const string GuestIdKey = "kingonline.profile.guestId";
    public const string DisplayNameKey = "kingonline.profile.displayName";
    public const string AvatarIdKey = "kingonline.profile.avatarId";

    private const string ProfileSlotArg = "-profileSlot";
    private const string GuestSlotArg = "-guestSlot";

    private static string ScopedGuestIdKey => $"{GuestIdKey}.{GetProfileScopeHash()}";
    private static string ScopedDisplayNameKey => $"{DisplayNameKey}.{GetProfileScopeHash()}";
    private static string ScopedAvatarIdKey => $"{AvatarIdKey}.{GetProfileScopeHash()}";

    public string GuestId;
    public string DisplayName;
    public int AvatarId;

    public static PlayerLocalProfile LoadOrCreate(int avatarCount = 8)
    {
        avatarCount = Mathf.Max(1, avatarCount);

        // No -profileSlot: one stable guest profile for this machine/build path.
        // With -profileSlot ClientA / ClientB: two stable test guests on the same PC.
        string guestId = PlayerPrefs.GetString(ScopedGuestIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(guestId))
        {
            guestId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(ScopedGuestIdKey, guestId);
        }

        string displayName = PlayerPrefs.GetString(ScopedDisplayNameKey, string.Empty);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = GenerateDefaultDisplayName(guestId);
            PlayerPrefs.SetString(ScopedDisplayNameKey, displayName);
        }

        // Avatar is server-assigned. Keep a deterministic menu preview based on GuestId.
        int avatarId = GetStableAvatarId(guestId, avatarCount);
        PlayerPrefs.SetInt(ScopedAvatarIdKey, avatarId);
        PlayerPrefs.Save();

        return new PlayerLocalProfile
        {
            GuestId = guestId,
            DisplayName = SanitizeDisplayName(displayName),
            AvatarId = avatarId
        };
    }

    public static void SaveDisplayName(string displayName)
    {
        displayName = SanitizeDisplayName(displayName);
        PlayerPrefs.SetString(ScopedDisplayNameKey, displayName);
        PlayerPrefs.Save();
    }

    public static void SaveAvatarId(int avatarId, int avatarCount = 8)
    {
        // Avatar is server-assigned. This method is kept for backward compatibility.
        avatarCount = Mathf.Max(1, avatarCount);
        string guestId = PlayerPrefs.GetString(ScopedGuestIdKey, string.Empty);
        int stableAvatarId = GetStableAvatarId(guestId, avatarCount);
        PlayerPrefs.SetInt(ScopedAvatarIdKey, stableAvatarId);
        PlayerPrefs.Save();
    }

    public static void SaveProfile(string displayName, int avatarId, int avatarCount = 8)
    {
        SaveDisplayName(displayName);
        SaveAvatarId(avatarId, avatarCount);
    }

    public static string GetCurrentProfileSlot()
    {
        string[] args = Environment.GetCommandLineArgs();
        string slot = GetArgValue(args, ProfileSlotArg, GetArgValue(args, GuestSlotArg, string.Empty));
        return SanitizeProfileSlot(slot);
    }

    public static int GetStableAvatarId(string guestId, int avatarCount = 8)
    {
        avatarCount = Mathf.Max(1, avatarCount);

        if (string.IsNullOrWhiteSpace(guestId))
            return UnityEngine.Random.Range(0, avatarCount);

        unchecked
        {
            int hash = 23;
            for (int i = 0; i < guestId.Length; i++)
                hash = hash * 31 + guestId[i];

            return (hash & int.MaxValue) % avatarCount;
        }
    }

    public static string SanitizeDisplayName(string raw)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? GenerateDefaultDisplayName(string.Empty) : raw.Trim();
        value = value.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

        while (value.Contains("  "))
            value = value.Replace("  ", " ");

        if (value.Length > 24)
            value = value.Substring(0, 24);

        if (string.IsNullOrWhiteSpace(value))
            value = GenerateDefaultDisplayName(string.Empty);

        return value;
    }

    private static string GenerateDefaultDisplayName(string guestId)
    {
        int number = 1000 + (GetStablePositiveHash(string.IsNullOrWhiteSpace(guestId) ? Guid.NewGuid().ToString("N") : guestId) % 9000);
        return $"Player {number}";
    }

    private static string GetProfileScopeHash()
    {
        string slot = GetCurrentProfileSlot();
        string raw = Application.dataPath ?? Application.productName;

        if (!string.IsNullOrWhiteSpace(slot))
            raw = $"{raw}|slot:{slot}";

        return GetStablePositiveHash(raw).ToString("X8");
    }

    private static string SanitizeProfileSlot(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string value = raw.Trim();
        value = value.Replace("\n", string.Empty).Replace("\r", string.Empty).Replace("\t", string.Empty);
        value = value.Replace(" ", "_");

        if (value.Length > 32)
            value = value.Substring(0, 32);

        return value;
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
}
