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

    [Header("Name Colors")]
    [SerializeField] private bool colorNameByLocalRelation = true;
    [SerializeField] private Color localPlayerNameColor = new Color(0.2f, 0.55f, 1f, 1f);
    [SerializeField] private Color opponentNameColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private bool restoreOriginalNameColorWhenEmpty = true;

    private Color originalNameColor = Color.white;
    private bool capturedOriginalNameColor;

    private void Awake()
    {
        CaptureOriginalNameColor();
    }

    public void SetEmpty(string label = "Waiting...")
    {
        CaptureOriginalNameColor();

        if (avatarImage != null)
        {
            avatarImage.sprite = null;
            avatarImage.enabled = false;
        }

        if (nameText != null)
        {
            nameText.text = label;
            if (restoreOriginalNameColorWhenEmpty)
                nameText.color = originalNameColor;
        }

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

    public void SetPlayer(Sprite avatar, string displayName, string role, bool isTurn, bool isLocalPlayer, int elo = -1, int eloDelta = 0, bool hasLocalPlayer = true)
    {
        CaptureOriginalNameColor();

        if (avatarImage != null)
        {
            avatarImage.sprite = avatar;
            avatarImage.enabled = avatar != null;
        }

        if (nameText != null)
        {
            nameText.text = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
            ApplyNameColor(isLocalPlayer, hasLocalPlayer);
        }

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

    private void ApplyNameColor(bool isLocalPlayer, bool hasLocalPlayer)
    {
        if (nameText == null || !colorNameByLocalRelation)
            return;

        if (!hasLocalPlayer)
        {
            nameText.color = originalNameColor;
            return;
        }

        nameText.color = isLocalPlayer ? localPlayerNameColor : opponentNameColor;
    }

    private void CaptureOriginalNameColor()
    {
        if (capturedOriginalNameColor || nameText == null)
            return;

        originalNameColor = nameText.color;
        capturedOriginalNameColor = true;
    }
}
