using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional visual-server control panel. Put this on a PlayScene Canvas that is also protected by ServerOnlySceneObject.
/// It is intentionally simple: one dedicated server process still owns one active match at a time.
/// </summary>
public class ServerMatchDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject rootToHide;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button restartCurrentMatchButton;
    [SerializeField] private Button kickAllAndReopenButton;
    [SerializeField] private Button lockRoomButton;
    [SerializeField] private Button unlockIdleRoomButton;
    [SerializeField] private Button quitServerButton;

    [Header("Refresh")]
    [SerializeField] private float refreshIntervalSeconds = 0.5f;
    [SerializeField] private bool hideOnNonServer = true;

    private float nextRefreshTime;

    private void Awake()
    {
        if (rootToHide == null)
            rootToHide = gameObject;

        if (restartCurrentMatchButton != null)
            restartCurrentMatchButton.onClick.AddListener(RestartCurrentMatch);

        if (kickAllAndReopenButton != null)
            kickAllAndReopenButton.onClick.AddListener(KickAllAndReopen);

        if (lockRoomButton != null)
            lockRoomButton.onClick.AddListener(LockRoom);

        if (unlockIdleRoomButton != null)
            unlockIdleRoomButton.onClick.AddListener(UnlockIdleRoom);

        if (quitServerButton != null)
            quitServerButton.onClick.AddListener(QuitServer);
    }

    private void OnDestroy()
    {
        if (restartCurrentMatchButton != null)
            restartCurrentMatchButton.onClick.RemoveListener(RestartCurrentMatch);

        if (kickAllAndReopenButton != null)
            kickAllAndReopenButton.onClick.RemoveListener(KickAllAndReopen);

        if (lockRoomButton != null)
            lockRoomButton.onClick.RemoveListener(LockRoom);

        if (unlockIdleRoomButton != null)
            unlockIdleRoomButton.onClick.RemoveListener(UnlockIdleRoom);

        if (quitServerButton != null)
            quitServerButton.onClick.RemoveListener(QuitServer);
    }

    private void Update()
    {
        NetworkRunnerHandler handler = NetworkRunnerHandler.Active;
        bool isServer = handler != null && handler.IsServerRunner;

        if (!isServer)
        {
            if (hideOnNonServer && rootToHide != null && rootToHide.activeSelf)
                rootToHide.SetActive(false);

            return;
        }

        if (rootToHide != null && !rootToHide.activeSelf)
            rootToHide.SetActive(true);

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, refreshIntervalSeconds);
        RefreshStatus(handler);
    }

    public void RefreshStatus(NetworkRunnerHandler handler = null)
    {
        if (handler == null)
            handler = NetworkRunnerHandler.Active;

        if (statusText != null)
            statusText.text = handler != null ? handler.BuildServerStatusText() : "KING ONLINE SERVER\nRunner: <missing>";

        bool serverReady = handler != null && handler.IsServerRunner;
        bool busyKicking = serverReady && handler.IsKickOperationRunning;
        bool hasPlayers = serverReady && handler.ConnectedPlayerCount > 0;
        bool hasFullPair = serverReady && handler.ConnectedPlayerCount >= handler.ConfiguredMaxPlayers;
        bool canUnlockIdle = serverReady && !hasPlayers && !handler.IsMatchStarted;

        if (restartCurrentMatchButton != null)
            restartCurrentMatchButton.interactable = hasFullPair && !busyKicking;

        if (kickAllAndReopenButton != null)
            kickAllAndReopenButton.interactable = serverReady && !busyKicking;

        if (lockRoomButton != null)
            lockRoomButton.interactable = serverReady && !busyKicking && handler.IsCurrentSessionJoinable;

        if (unlockIdleRoomButton != null)
            unlockIdleRoomButton.interactable = canUnlockIdle && !busyKicking && !handler.IsCurrentSessionJoinable;

        if (quitServerButton != null)
            quitServerButton.interactable = serverReady;
    }

    private void RestartCurrentMatch()
    {
        NetworkRunnerHandler.Active?.ServerRestartCurrentMatch();
        RefreshStatus();
    }

    private void KickAllAndReopen()
    {
        NetworkRunnerHandler.Active?.ServerKickAllPlayersAndReopen();
        RefreshStatus();
    }

    private void LockRoom()
    {
        NetworkRunnerHandler.Active?.ServerLockSession();
        RefreshStatus();
    }

    private void UnlockIdleRoom()
    {
        NetworkRunnerHandler.Active?.ServerUnlockSessionIfIdle();
        RefreshStatus();
    }

    private void QuitServer()
    {
        NetworkRunnerHandler.Active?.ServerQuitApplication();
    }
}
