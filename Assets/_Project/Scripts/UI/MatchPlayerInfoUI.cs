using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// PlayScene UI that displays the two networked player profiles and current turn.
/// It polls because player profile, role assignment, and scene spawn order can arrive in different frames.
/// </summary>
public class MatchPlayerInfoUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private AvatarCatalog avatarCatalog;

    [Header("Slots")]
    [SerializeField] private PlayerInfoSlotUI kingSlot;
    [SerializeField] private PlayerInfoSlotUI chessSlot;
    [SerializeField] private TextMeshProUGUI turnText;
    [Tooltip("Optional TextMeshPro text in PlayScene for the authoritative turn timer, formatted as mm:ss.")]
    [SerializeField] private TextMeshProUGUI turnTimerText;

    [Header("Labels")]
    [SerializeField] private string kingRoleLabel = "Rogue King";
    [SerializeField] private string chessRoleLabel = "Chess Alliance";

    [Header("Refresh")]
    [SerializeField] private float refreshIntervalSeconds = 0.2f;

    private float nextRefreshTime;

    private void OnEnable()
    {
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
        Refresh();
    }

    public void Refresh()
    {
        ServerGameManager gameManager = ServerGameManager.Instance;
        if (gameManager == null)
        {
            SetWaiting("Server waiting...");
            return;
        }

        PlayerRef kingPlayer = gameManager.kingPlayer;
        PlayerRef chessPlayer = gameManager.chessPlayer;
        PlayerRef turnPlayer = GetTurnPlayer(gameManager.currentGameState, kingPlayer, chessPlayer);
        PlayerRef localPlayer = GetLocalPlayerRef();

        PlayerNetworkController kingController = FindControllerForPlayer(kingPlayer);
        PlayerNetworkController chessController = FindControllerForPlayer(chessPlayer);

        ApplySlot(kingSlot, kingController, kingPlayer, kingRoleLabel, turnPlayer == kingPlayer, localPlayer == kingPlayer, "Waiting King...");
        ApplySlot(chessSlot, chessController, chessPlayer, chessRoleLabel, turnPlayer == chessPlayer, localPlayer == chessPlayer, "Waiting Chess...");
        UpdateTurnText(gameManager.currentGameState, turnPlayer, localPlayer, kingController, chessController);
        UpdateTurnTimerText(gameManager);
    }

    private void SetWaiting(string message)
    {
        if (kingSlot != null)
            kingSlot.SetEmpty("Waiting King...");

        if (chessSlot != null)
            chessSlot.SetEmpty("Waiting Chess...");

        if (turnText != null)
            turnText.text = message;

        if (turnTimerText != null)
            turnTimerText.text = "--";
    }

    private void ApplySlot(PlayerInfoSlotUI slot, PlayerNetworkController controller, PlayerRef playerRef, string role, bool isTurn, bool isLocalPlayer, string waitingText)
    {
        if (slot == null)
            return;

        if (playerRef == PlayerRef.None || controller == null)
        {
            slot.SetEmpty(waitingText);
            return;
        }

        string displayName = controller.GetDisplayNameOrFallback();
        int avatarId = controller.GetAvatarIdOrDefault();
        Sprite avatar = avatarCatalog != null ? avatarCatalog.GetAvatar(avatarId) : null;

        int elo = controller.GetEloOrDefault();
        int eloDelta = controller.GetLastEloDelta();
        slot.SetPlayer(avatar, displayName, role, isTurn, isLocalPlayer, elo, eloDelta);
    }

    private void UpdateTurnText(NetGameState state, PlayerRef turnPlayer, PlayerRef localPlayer, PlayerNetworkController kingController, PlayerNetworkController chessController)
    {
        if (turnText == null)
            return;

        if (state == NetGameState.Init || state == NetGameState.Setup)
        {
            turnText.text = "Start matching......";
            return;
        }

        if (state == NetGameState.ResolvingAction)
        {
            turnText.text = "Server resolving...";
            return;
        }

        if (state == NetGameState.GameOver)
        {
            turnText.text = "End match";
            return;
        }

        if (turnPlayer == PlayerRef.None)
        {
            turnText.text = "Wating player action...";
            return;
        }

        if (localPlayer == turnPlayer)
        {
            turnText.text = "Your turn";
            return;
        }

        PlayerNetworkController turnController = turnPlayer == ServerGameManager.Instance.kingPlayer ? kingController : chessController;
        string opponentName = turnController != null ? turnController.GetDisplayNameOrFallback() : $"Player {turnPlayer.PlayerId}";
        turnText.text = $"Waiting {opponentName}";
    }

    private void UpdateTurnTimerText(ServerGameManager gameManager)
    {
        if (turnTimerText == null)
            return;

        if (gameManager == null)
        {
            turnTimerText.text = "--";
            return;
        }

        if (gameManager.currentGameState == NetGameState.GameOver)
        {
            turnTimerText.text = "00";
            return;
        }

        if (!gameManager.IsTurnTimerActive())
        {
            turnTimerText.text = "--";
            return;
        }

        int seconds = Mathf.CeilToInt(gameManager.GetTurnRemainingSeconds());
        turnTimerText.text = FormatTimer(seconds);
    }

    private static string FormatTimer(int totalSeconds)
    {
        totalSeconds = Mathf.Max(0, totalSeconds);
        return $"{totalSeconds:00}";
    }

    private static PlayerRef GetTurnPlayer(NetGameState state, PlayerRef kingPlayer, PlayerRef chessPlayer)
    {
        if (state == NetGameState.KingTurn)
            return kingPlayer;

        if (state == NetGameState.ChessTurn)
            return chessPlayer;

        return PlayerRef.None;
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
