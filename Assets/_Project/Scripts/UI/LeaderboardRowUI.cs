using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class LeaderboardRowUI : MonoBehaviour
{
    [Header("Ranking")]
    [Tooltip("Text object used only for leaderboard ranking/order. This replaces the old hard-coded '#1/#2/#3' rank text.")]
    [FormerlySerializedAs("rankText")]
    [SerializeField] private TextMeshProUGUI rankingText;

    [Tooltip("If enabled, this row writes its ranking number automatically from LeaderboardMenuUI. Disable only if you use separate pre-made row prefabs with fixed ranking text.")]
    [SerializeField] private bool autoSetRankingText = true;

    [Tooltip("Optional prefix for auto ranking. Leave empty for '1, 2, 3'. Use '#' only if you want '#1, #2, #3'.")]
    [SerializeField] private string rankingPrefix = string.Empty;

    [Tooltip("Optional suffix for auto ranking.")]
    [SerializeField] private string rankingSuffix = string.Empty;

    [Tooltip("Clear ranking text when the row is empty/no player data.")]
    [SerializeField] private bool clearRankingWhenEmpty = true;

    [Header("Player Data")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI eloText;
    [SerializeField] private TextMeshProUGUI winLossText;

    [Header("Roots")]
    [SerializeField] private GameObject emptyRoot;
    [SerializeField] private GameObject contentRoot;

    public void SetEmpty()
    {
        if (emptyRoot != null)
            emptyRoot.SetActive(true);

        if (contentRoot != null)
            contentRoot.SetActive(false);

        if (rankingText != null && clearRankingWhenEmpty)
            rankingText.text = string.Empty;

        if (nameText != null) nameText.text = string.Empty;
        if (eloText != null) eloText.text = string.Empty;
        if (winLossText != null) winLossText.text = string.Empty;

        if (avatarImage != null)
        {
            avatarImage.sprite = null;
            avatarImage.enabled = false;
        }
    }

    public void SetData(int ranking, LeaderboardEntryData entry, AvatarCatalog avatarCatalog)
    {
        if (entry == null)
        {
            SetEmpty();
            return;
        }

        if (emptyRoot != null)
            emptyRoot.SetActive(false);

        if (contentRoot != null)
            contentRoot.SetActive(true);

        ApplyRanking(ranking);

        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(entry.displayName) ? "Player" : entry.displayName;

        if (eloText != null)
            eloText.text = entry.elo.ToString();

        if (winLossText != null)
            winLossText.text = $"{entry.wins}W / {entry.losses}L";

        if (avatarImage != null)
        {
            avatarImage.sprite = avatarCatalog != null ? avatarCatalog.GetAvatar(entry.avatarId) : null;
            avatarImage.enabled = avatarImage.sprite != null;
        }
    }

    private void ApplyRanking(int ranking)
    {
        if (rankingText == null || !autoSetRankingText)
            return;

        rankingText.text = $"{rankingPrefix}{ranking}{rankingSuffix}";
    }
}
