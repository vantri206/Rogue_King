using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerInfoSlotUI : MonoBehaviour
{
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI roleText;
    [SerializeField] private TextMeshProUGUI eloText;
    [SerializeField] private TextMeshProUGUI eloDeltaText;
    [SerializeField] private GameObject turnHighlight;
    [SerializeField] private GameObject localPlayerBadge;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float inactiveAlpha = 0.65f;

    public void SetEmpty(string label = "Waiting...")
    {
        if (avatarImage != null)
        {
            avatarImage.sprite = null;
            avatarImage.enabled = false;
        }

        if (nameText != null)
            nameText.text = label;

        if (roleText != null)
            roleText.text = string.Empty;

        if (eloText != null)
            eloText.text = string.Empty;

        if (eloDeltaText != null)
            eloDeltaText.text = string.Empty;

        if (turnHighlight != null)
            turnHighlight.SetActive(false);

        if (localPlayerBadge != null)
            localPlayerBadge.SetActive(false);

        if (canvasGroup != null)
            canvasGroup.alpha = inactiveAlpha;
    }

    public void SetPlayer(Sprite avatar, string displayName, string role, bool isTurn, bool isLocalPlayer, int elo = -1, int eloDelta = 0)
    {
        if (avatarImage != null)
        {
            avatarImage.sprite = avatar;
            avatarImage.enabled = avatar != null;
        }

        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;

        if (roleText != null)
            roleText.text = role;

        if (eloText != null)
            eloText.text = elo >= 0 ? $"Elo {elo}" : string.Empty;

        if (eloDeltaText != null)
        {
            if (eloDelta > 0)
                eloDeltaText.text = $"+{eloDelta}";
            else if (eloDelta < 0)
                eloDeltaText.text = eloDelta.ToString();
            else
                eloDeltaText.text = string.Empty;
        }

        if (turnHighlight != null)
            turnHighlight.SetActive(isTurn);

        if (localPlayerBadge != null)
            localPlayerBadge.SetActive(isLocalPlayer);

        if (canvasGroup != null)
            canvasGroup.alpha = isTurn ? 1f : inactiveAlpha;
    }
}
