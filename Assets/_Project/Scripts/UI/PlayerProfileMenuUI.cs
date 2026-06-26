using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MenuScene UI for local guest name. Avatar is server-assigned from GuestId in this quick-test patch.
/// The profile is submitted to the server by PlayerNetworkController after the match connection is ready.
/// </summary>
public class PlayerProfileMenuUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private AvatarCatalog avatarCatalog;

    [Header("UI")]
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI guestIdText;
    [SerializeField] private TextMeshProUGUI currentEloText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button previousAvatarButton;
    [SerializeField] private Button nextAvatarButton;
    [SerializeField] private Button randomAvatarButton;
    [SerializeField] private Button saveButton;

    [Header("Elo")]
    [SerializeField] private int defaultElo = 1000;
    [SerializeField] private string currentEloLabel = "Current Elo";
    [SerializeField] private float eloRefreshIntervalSeconds = 0.5f;

    private PlayerLocalProfile currentProfile;
    private int currentAvatarId;
    private float nextEloRefreshTime;

    private int AvatarCount => avatarCatalog != null && avatarCatalog.Count > 0 ? avatarCatalog.Count : 8;

    private void Awake()
    {
        // Avatar is assigned by the server and cannot be changed in this quick-test patch.
        DisableAvatarChangeButton(previousAvatarButton);
        DisableAvatarChangeButton(nextAvatarButton);
        DisableAvatarChangeButton(randomAvatarButton);

        if (saveButton != null)
            saveButton.onClick.AddListener(SaveProfileFromUI);
    }

    private void OnEnable()
    {
        ClientLeaderboardCache.Changed += RefreshCurrentEloText;
        ClientLeaderboardCache.LoadFromPlayerPrefs();
        RefreshCurrentEloText();
    }

    private void Start()
    {
        LoadProfileToUI();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextEloRefreshTime)
            return;

        nextEloRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, eloRefreshIntervalSeconds);
        RefreshCurrentEloText();
    }

    private void OnDisable()
    {
        ClientLeaderboardCache.Changed -= RefreshCurrentEloText;
    }

    private void OnDestroy()
    {
        if (saveButton != null)
            saveButton.onClick.RemoveListener(SaveProfileFromUI);
    }

    public void LoadProfileToUI()
    {
        currentProfile = PlayerLocalProfile.LoadOrCreate(AvatarCount);
        currentAvatarId = avatarCatalog != null ? avatarCatalog.ClampAvatarId(currentProfile.AvatarId) : Mathf.Clamp(currentProfile.AvatarId, 0, AvatarCount - 1);

        if (nameInput != null)
            nameInput.text = currentProfile.DisplayName;

        if (guestIdText != null)
            guestIdText.text = $"GuestId: {ShortGuestId(currentProfile.GuestId)}";

        RefreshAvatarImage();
        RefreshCurrentEloText();
        SetStatus("Profile loaded.");
    }

    public void SaveProfileFromUI()
    {
        string newName = nameInput != null ? nameInput.text : currentProfile?.DisplayName;
        PlayerLocalProfile.SaveDisplayName(newName);
        currentProfile = PlayerLocalProfile.LoadOrCreate(AvatarCount);
        currentAvatarId = currentProfile.AvatarId;

        if (nameInput != null)
            nameInput.text = currentProfile.DisplayName;

        RefreshAvatarImage();
        RefreshCurrentEloText();
        SetStatus("Name saved. Avatar is assigned by server.");
    }

    public void PreviousAvatar()
    {
        AvatarChangeBlocked();
    }

    public void NextAvatar()
    {
        AvatarChangeBlocked();
    }

    public void RandomAvatar()
    {
        AvatarChangeBlocked();
    }

    private void AvatarChangeBlocked()
    {
        currentProfile = PlayerLocalProfile.LoadOrCreate(AvatarCount);
        currentAvatarId = currentProfile.AvatarId;
        RefreshAvatarImage();
        RefreshCurrentEloText();
        SetStatus("Avatar is assigned by server and cannot be changed.");
    }

    private void DisableAvatarChangeButton(Button button)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.interactable = false;
        button.gameObject.SetActive(false);
    }

    private void RefreshAvatarImage()
    {
        if (avatarImage == null)
            return;

        avatarImage.sprite = avatarCatalog != null ? avatarCatalog.GetAvatar(currentAvatarId) : null;
        avatarImage.enabled = avatarImage.sprite != null;
    }

    private void RefreshCurrentEloText()
    {
        if (currentEloText == null)
            return;

        if (currentProfile == null)
            currentProfile = PlayerLocalProfile.LoadOrCreate(AvatarCount);

        LeaderboardEntryData cachedEntry = ClientLeaderboardCache.GetEntryByGuestId(currentProfile.GuestId);
        int elo = cachedEntry != null ? cachedEntry.elo : Mathf.Max(0, defaultElo);
        currentEloText.text = $"{currentEloLabel}: {elo}";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private static string ShortGuestId(string guestId)
    {
        if (string.IsNullOrWhiteSpace(guestId))
            return "<none>";

        return guestId.Length <= 8 ? guestId : guestId.Substring(0, 8);
    }
}
