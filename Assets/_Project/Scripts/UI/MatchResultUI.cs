using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayScene result UI for the final GameOver flow.
/// - Shows Win/Lose panels based on ServerGameManager.winnerPlayer/loserPlayer.
/// - Displays Elo before, Elo after, and signed Elo delta.
/// - Back button lets the local client leave immediately.
/// - ServerGameManager/NetworkRunnerHandler still owns the authoritative 5s Kick All/Reopen flow.
/// </summary>
public class MatchResultUI : MonoBehaviour
{
    private static MatchResultUI activeInstance;

    public static bool ExistsInScene => activeInstance != null && activeInstance.isActiveAndEnabled;

    [Header("Panels")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [Tooltip("Optional. If empty, draw will reuse the lose panel with neutral text/color.")]
    [SerializeField] private GameObject drawPanel;

    [Header("Win Panel Text")]
    [SerializeField] private TextMeshProUGUI winTitleText;
    [SerializeField] private TextMeshProUGUI winEloBeforeText;
    [SerializeField] private TextMeshProUGUI winEloAfterText;
    [SerializeField] private TextMeshProUGUI winEloDeltaText;
    [SerializeField] private TextMeshProUGUI winCountdownText;
    [SerializeField] private Button winBackButton;

    [Header("Lose Panel Text")]
    [SerializeField] private TextMeshProUGUI loseTitleText;
    [SerializeField] private TextMeshProUGUI loseEloBeforeText;
    [SerializeField] private TextMeshProUGUI loseEloAfterText;
    [SerializeField] private TextMeshProUGUI loseEloDeltaText;
    [SerializeField] private TextMeshProUGUI loseCountdownText;
    [SerializeField] private Button loseBackButton;

    [Header("Draw Panel Text Optional")]
    [SerializeField] private TextMeshProUGUI drawTitleText;
    [SerializeField] private TextMeshProUGUI drawEloBeforeText;
    [SerializeField] private TextMeshProUGUI drawEloAfterText;
    [SerializeField] private TextMeshProUGUI drawEloDeltaText;
    [SerializeField] private TextMeshProUGUI drawCountdownText;
    [SerializeField] private Button drawBackButton;

    [Header("Labels")]
    [SerializeField] private string winTitle = "YOU WIN";
    [SerializeField] private string loseTitle = "YOU LOSE";
    [SerializeField] private string drawTitle = "DRAW";
    [SerializeField] private string eloBeforeFormat = "Elo Before: {0}";
    [SerializeField] private string eloAfterFormat = "Elo After: {0}";
    [SerializeField] private string countdownFormat = "Back to menu in {0}s";

    [Header("Colors")]
    [SerializeField] private Color winColor = new Color(0.2f, 0.9f, 0.45f, 1f);
    [SerializeField] private Color loseColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color drawColor = new Color(1f, 0.9f, 0.2f, 1f);
    [SerializeField] private Color eloDeltaColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Header("Return Flow")]
    [Tooltip("Use the same value as ServerGameManager -> Kick All Players After GameOver Delay Seconds.")]
    [SerializeField] private float localCountdownSeconds = 5f;
    [Tooltip("Safety fallback. If server kick does not arrive shortly after the countdown, this client leaves locally.")]
    [SerializeField] private bool clientSafetyReturnAfterCountdown = true;
    [SerializeField] private float clientSafetyReturnBufferSeconds = 1f;

    private bool resultVisible;
    private bool localReturnRequested;
    private int shownResultSerial = -1;
    private float countdownEndsAtRealtime;
    private float safetyReturnAtRealtime;

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning("[MatchResultUI] Duplicate MatchResultUI found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        activeInstance = this;
        HideAllPanels();
    }

    private void OnEnable()
    {
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();

        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    private void Update()
    {
        ServerGameManager gameManager = ServerGameManager.Instance;
        if (gameManager == null || gameManager.currentGameState != NetGameState.GameOver)
        {
            if (resultVisible)
                HideAllPanels();

            return;
        }

        if (!resultVisible || shownResultSerial != gameManager.matchResultSerial)
            ShowResult(gameManager);
        else
            RefreshVisibleResultValues(gameManager);

        UpdateCountdownTexts();
        TrySafetyReturnToMenu();
    }

    private void RegisterButtons()
    {
        if (winBackButton != null)
            winBackButton.onClick.AddListener(RequestBackToMenuNow);

        if (loseBackButton != null)
            loseBackButton.onClick.AddListener(RequestBackToMenuNow);

        if (drawBackButton != null)
            drawBackButton.onClick.AddListener(RequestBackToMenuNow);
    }

    private void UnregisterButtons()
    {
        if (winBackButton != null)
            winBackButton.onClick.RemoveListener(RequestBackToMenuNow);

        if (loseBackButton != null)
            loseBackButton.onClick.RemoveListener(RequestBackToMenuNow);

        if (drawBackButton != null)
            drawBackButton.onClick.RemoveListener(RequestBackToMenuNow);
    }

    private void HideAllPanels()
    {
        resultVisible = false;

        if (winPanel != null)
            winPanel.SetActive(false);

        if (losePanel != null)
            losePanel.SetActive(false);

        if (drawPanel != null)
            drawPanel.SetActive(false);
    }

    private void ShowResult(ServerGameManager gameManager)
    {
        resultVisible = true;
        shownResultSerial = gameManager.matchResultSerial;
        localReturnRequested = false;

        countdownEndsAtRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, localCountdownSeconds);
        safetyReturnAtRealtime = countdownEndsAtRealtime + Mathf.Max(0f, clientSafetyReturnBufferSeconds);

        HideAllPanels();
        resultVisible = true;
        RefreshVisibleResultValues(gameManager);
        SetButtonsInteractable(true);
    }

    private void RefreshVisibleResultValues(ServerGameManager gameManager)
    {
        if (gameManager == null)
            return;

        PlayerRef localPlayer = GetLocalPlayerRef();
        PlayerRef winner = gameManager.winnerPlayer;
        PlayerRef loser = gameManager.loserPlayer;

        bool isDraw = winner == PlayerRef.None || loser == PlayerRef.None;
        bool isWin = !isDraw && localPlayer != PlayerRef.None && localPlayer == winner;
        bool isLose = !isDraw && localPlayer != PlayerRef.None && localPlayer == loser;

        PlayerNetworkController localController = FindControllerForPlayer(localPlayer);
        int eloAfter = localController != null ? localController.GetEloOrDefault() : 1000;
        int eloDelta = localController != null ? localController.GetLastEloDelta() : 0;
        int eloBefore = Mathf.Max(0, eloAfter - eloDelta);

        if (isDraw)
        {
            ShowDraw(eloBefore, eloAfter, eloDelta);
        }
        else if (isWin)
        {
            ShowWin(eloBefore, eloAfter, eloDelta);
        }
        else if (isLose)
        {
            ShowLose(eloBefore, eloAfter, eloDelta);
        }
        else
        {
            // Spectator/debug fallback: do not crash or leave the screen blank.
            ShowDraw(eloBefore, eloAfter, eloDelta);
        }
    }

    private void ShowWin(int eloBefore, int eloAfter, int eloDelta)
    {
        SetPanelActive(winPanel, true);
        SetPanelActive(losePanel, false);
        SetPanelActive(drawPanel, false);

        ApplyText(winTitleText, winTitle, winColor);
        ApplyEloTexts(winEloBeforeText, winEloAfterText, winEloDeltaText, eloBefore, eloAfter, eloDelta);
    }

    private void ShowLose(int eloBefore, int eloAfter, int eloDelta)
    {
        SetPanelActive(winPanel, false);
        SetPanelActive(losePanel, true);
        SetPanelActive(drawPanel, false);

        ApplyText(loseTitleText, loseTitle, loseColor);
        ApplyEloTexts(loseEloBeforeText, loseEloAfterText, loseEloDeltaText, eloBefore, eloAfter, eloDelta);
    }

    private void ShowDraw(int eloBefore, int eloAfter, int eloDelta)
    {
        SetPanelActive(winPanel, false);

        if (drawPanel != null)
        {
            SetPanelActive(losePanel, false);
            SetPanelActive(drawPanel, true);
            ApplyText(drawTitleText, drawTitle, drawColor);
            ApplyEloTexts(drawEloBeforeText, drawEloAfterText, drawEloDeltaText, eloBefore, eloAfter, eloDelta);
        }
        else
        {
            SetPanelActive(losePanel, true);
            ApplyText(loseTitleText, drawTitle, drawColor);
            ApplyEloTexts(loseEloBeforeText, loseEloAfterText, loseEloDeltaText, eloBefore, eloAfter, eloDelta);
        }
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null && panel.activeSelf != active)
            panel.SetActive(active);
    }

    private void ApplyEloTexts(TextMeshProUGUI beforeText, TextMeshProUGUI afterText, TextMeshProUGUI deltaText, int eloBefore, int eloAfter, int eloDelta)
    {
        ApplyText(beforeText, string.Format(eloBeforeFormat, eloBefore));
        ApplyText(afterText, string.Format(eloAfterFormat, eloAfter));

        if (deltaText != null)
        {
            deltaText.text = FormatSignedDelta(eloDelta);
            deltaText.color = eloDeltaColor;
        }
    }

    private void ApplyText(TextMeshProUGUI target, string value)
    {
        if (target == null)
            return;

        target.text = value;
    }

    private void ApplyText(TextMeshProUGUI target, string value, Color color)
    {
        if (target == null)
            return;

        target.text = value;
        target.color = color;
    }

    private void UpdateCountdownTexts()
    {
        int remaining = Mathf.Max(0, Mathf.CeilToInt(countdownEndsAtRealtime - Time.realtimeSinceStartup));
        string text = string.Format(countdownFormat, remaining);

        if (winPanel != null && winPanel.activeSelf)
            ApplyText(winCountdownText, text);

        if (losePanel != null && losePanel.activeSelf)
            ApplyText(loseCountdownText, text);

        if (drawPanel != null && drawPanel.activeSelf)
            ApplyText(drawCountdownText, text);
    }

    private void TrySafetyReturnToMenu()
    {
        if (!clientSafetyReturnAfterCountdown) return;
        if (localReturnRequested) return;
        if (Time.realtimeSinceStartup < safetyReturnAtRealtime) return;

        RequestBackToMenuNow();
    }

    private void RequestBackToMenuNow()
    {
        if (localReturnRequested)
            return;

        localReturnRequested = true;
        SetButtonsInteractable(false);

        if (NetworkRunnerHandler.Active != null)
        {
            NetworkRunnerHandler.Active.ClientLeaveCurrentSessionAndReturnToMenu("match_result_back_to_menu");
        }
        else
        {
            Debug.LogWarning("[MatchResultUI] Cannot return to menu because NetworkRunnerHandler.Active is missing.");
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (winBackButton != null)
            winBackButton.interactable = interactable;

        if (loseBackButton != null)
            loseBackButton.interactable = interactable;

        if (drawBackButton != null)
            drawBackButton.interactable = interactable;
    }

    private static string FormatSignedDelta(int delta)
    {
        if (delta >= 0)
            return $"+{delta}";

        return delta.ToString();
    }

    private static PlayerRef GetLocalPlayerRef()
    {
        PlayerNetworkController local = PlayerNetworkController.Local;
        if (local == null || local.Object == null)
            return PlayerRef.None;

        return local.Object.InputAuthority;
    }

    private static PlayerNetworkController FindControllerForPlayer(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return null;

        PlayerNetworkController[] controllers = FindObjectsByType<PlayerNetworkController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PlayerNetworkController controller in controllers)
        {
            if (controller != null && controller.Object != null && controller.Object.InputAuthority == player)
                return controller;
        }

        return null;
    }
}
