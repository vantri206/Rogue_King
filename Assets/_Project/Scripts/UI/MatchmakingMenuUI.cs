using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchmakingMenuUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkRunnerHandler runnerHandler;
    [SerializeField] private Button playButton;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button cancelMatchmakingButton;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject rootToHideOnServer;

    [Header("Behaviour")]
    [Tooltip("Protect visual server scenes from accidentally running client matchmaking UI.")]
    [SerializeField] private bool disableOnServerProcess = true;

    [Tooltip("Connect MenuScene to the lobby session automatically so leaderboard/profile Elo can update live.")]
    [SerializeField] private bool autoConnectLobbyOnStart = true;

    [Tooltip("If true, Play only marks this player as ready in the lobby. The lobby server pairs 2 ready players and sends them to the match room.")]
    [SerializeField] private bool useLobbyMatchmaking = true;

    [Tooltip("Fallback only: if lobby matchmaking is disabled, Play joins any open dedicated-server match session.")]
    [SerializeField] private bool playButtonUsesQuickMatch = true;

    [Header("Retry")]
    [Tooltip("QuickMatch/lobby connect can fail if the server room has not appeared in Photon lobby yet. Keep this small for demo builds.")]
    [SerializeField] private int quickMatchRetryCount = 3;
    [SerializeField] private float retryDelaySeconds = 1.25f;

    [Header("Lobby Debug")]
    [Tooltip("If no match-found RPC arrives in this many seconds, re-enable the Play button and show a useful status instead of waiting forever.")]
    [SerializeField] private float lobbyMatchmakingTimeoutSeconds = 20f;

    private bool isMatchmaking;
    private bool lobbyConnectInProgress;
    private Coroutine lobbyWaitTimeoutCoroutine;

    private void Awake()
    {
        if (rootToHideOnServer == null)
            rootToHideOnServer = gameObject;

        if (disableOnServerProcess && IsServerProcess())
        {
            if (rootToHideOnServer != null)
                rootToHideOnServer.SetActive(false);

            enabled = false;
            return;
        }

        ResolveRunnerHandler();

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }

        if (createRoomButton != null)
        {
            createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);
            createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
        }

        if (cancelMatchmakingButton != null)
        {
            cancelMatchmakingButton.onClick.RemoveListener(OnCancelMatchmakingButtonClicked);
            cancelMatchmakingButton.onClick.AddListener(OnCancelMatchmakingButtonClicked);
            cancelMatchmakingButton.interactable = false;
        }
    }

    private async void Start()
    {
        if (autoConnectLobbyOnStart)
            await ConnectLobbyForMenuLiveData();
    }

    private void ResolveRunnerHandler()
    {
        if (NetworkRunnerHandler.Active != null)
        {
            runnerHandler = NetworkRunnerHandler.Active;
            return;
        }

        if (runnerHandler == null)
            runnerHandler = FindFirstObjectByType<NetworkRunnerHandler>();
    }

    private static bool IsServerProcess()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-dedicated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-lobbyserver", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return Application.isBatchMode;
    }

    private void OnDestroy()
    {
        if (playButton != null)
            playButton.onClick.RemoveListener(OnPlayButtonClicked);

        if (createRoomButton != null)
            createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);

        if (cancelMatchmakingButton != null)
            cancelMatchmakingButton.onClick.RemoveListener(OnCancelMatchmakingButtonClicked);

        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }
    }

    private void OnPlayButtonClicked()
    {
        if (isMatchmaking)
            return;

        if (useLobbyMatchmaking)
        {
            string roomCode = GetSanitizedRoomCodeInput();
            if (string.IsNullOrWhiteSpace(roomCode))
                RequestMatchFromLobby();
            else
                RequestJoinCustomRoomFromLobby(roomCode);
            return;
        }

        if (playButtonUsesQuickMatch)
            PlayQuickMatch();
        else
            JoinRoomByCode();
    }


    private void OnCancelMatchmakingButtonClicked()
    {
        CancelLobbySearchFromMenu("Đã hủy tìm trận.");
    }

    public void CancelLobbySearchFromMenu(string statusMessage = "Đã hủy tìm trận.")
    {
        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }

        ResolveRunnerHandler();
        if (runnerHandler != null)
            runnerHandler.ClientCancelLobbyMatchmaking();

        isMatchmaking = false;
        SetInteractable(true);
        SetStatus(string.IsNullOrWhiteSpace(statusMessage) ? "Đã hủy tìm trận." : statusMessage);
    }

    private void OnCreateRoomButtonClicked()
    {
        if (isMatchmaking)
            return;

        if (useLobbyMatchmaking)
        {
            RequestCreateCustomRoomFromLobby();
            return;
        }

        SetStatus("Create Room requires lobby matchmaking mode.");
    }

    public async Task<bool> ConnectLobbyForMenuLiveData()
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return false;
        }

        if (runnerHandler.IsClientConnectedToLobby)
        {
            SetStatus("Connected to live lobby.");
            return true;
        }

        if (runnerHandler.HasRunnerStarted)
        {
            SetStatus("Runner already started outside lobby.");
            return false;
        }

        if (lobbyConnectInProgress)
            return false;

        lobbyConnectInProgress = true;
        SetStatus("Connecting live leaderboard lobby...");

        bool success = false;
        int attempts = Mathf.Max(1, quickMatchRetryCount);

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                success = await runnerHandler.StartClientLobby();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MatchmakingMenuUI] Lobby connect exception: {exception}");
                success = false;
            }

            if (success)
                break;

            if (attempt < attempts)
            {
                SetStatus($"Lobby not found. Retrying... ({attempt + 1}/{attempts})");
                await Task.Delay(Mathf.RoundToInt(Mathf.Max(0.1f, retryDelaySeconds) * 1000f));
            }
        }

        lobbyConnectInProgress = false;
        SetStatus(success ? "Connected to live lobby. Leaderboard will update from server." : "Lobby connect failed. Start lobby server first.");
        return success;
    }

    public async void RequestMatchFromLobby()
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!connected)
            {
                SetStatus("Cannot find match because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        bool requested = runnerHandler.ClientRequestLobbyMatchmaking();
        if (requested)
        {
            SetStatus("Find-match request sent/queued. Waiting for another ready lobby player...");
            StartLobbyWaitTimeout();
        }
        else
        {
            SetStatus("Could not send find-match request. Check lobby connection and local PlayerController spawn.");
            SetInteractable(true);
            isMatchmaking = false;
        }
    }

    public async void RequestCreateCustomRoomFromLobby()
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!connected)
            {
                SetStatus("Cannot create room because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        bool requested = runnerHandler.ClientRequestCreateCustomRoom();
        if (requested)
        {
            SetStatus("Creating room...");
            StartLobbyWaitTimeout();
        }
        else
        {
            SetStatus("Could not send create-room request. Check lobby connection and local PlayerController spawn.");
            SetInteractable(true);
            isMatchmaking = false;
        }
    }

    public async void RequestJoinCustomRoomFromLobby(string roomCode)
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        roomCode = SanitizeRoomCode(roomCode);
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            RequestMatchFromLobby();
            return;
        }

        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!connected)
            {
                SetStatus("Cannot join room because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        bool requested = runnerHandler.ClientRequestJoinCustomRoom(roomCode);
        if (requested)
        {
            SetStatus($"Checking Room ID {roomCode}...");
            StartLobbyWaitTimeout();
        }
        else
        {
            SetStatus("Could not send join-room request. Check lobby connection and local PlayerController spawn.");
            SetInteractable(true);
            isMatchmaking = false;
        }
    }


    public void NotifyPreMatchCardSelectionOpened()
    {
        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }

        isMatchmaking = false;
        SetInteractable(false);
        SetStatus("Match found. Choose your cards, then press Fight!");
    }

    public void ShowLobbyRoomError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            message = "Sai Room ID";

        SetStatus(message);
        SetInteractable(true);
        isMatchmaking = false;

        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }
    }

    private void StartLobbyWaitTimeout()
    {
        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }

        float timeout = Mathf.Max(5f, lobbyMatchmakingTimeoutSeconds);
        lobbyWaitTimeoutCoroutine = StartCoroutine(LobbyWaitTimeoutRoutine(timeout));
    }

    private System.Collections.IEnumerator LobbyWaitTimeoutRoutine(float timeoutSeconds)
    {
        yield return new WaitForSecondsRealtime(timeoutSeconds);

        lobbyWaitTimeoutCoroutine = null;

        ResolveRunnerHandler();
        if (!isMatchmaking)
            yield break;

        // If match-found arrived, the lobby runner is shut down and this object is usually destroyed/scene-loaded.
        // If we are still in lobby after the timeout, the ready request did not form a pair.
        if (runnerHandler != null && runnerHandler.IsClientConnectedToLobby)
        {
            SetStatus("Still in lobby. Make sure BOTH clients are connected to RogueKingLobby and both pressed Find Match. Check lobby server log: Ready should become 2/2.");
            SetInteractable(true);
            isMatchmaking = false;
        }
    }

    public async void PlayQuickMatch()
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        int attempts = Mathf.Max(1, quickMatchRetryCount);
        await StartMatchmakingWithRetry(
            attempts,
            attempt => $"Finding match... ({attempt}/{attempts})",
            () => runnerHandler.StartClientQuickMatch()
        );
    }

    public async void JoinRoomByCode()
    {
        ResolveRunnerHandler();

        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            PlayQuickMatch();
            return;
        }

        await StartMatchmakingWithRetry(
            1,
            _ => $"Joining {roomCode}...",
            () => runnerHandler.StartClientJoinSession(roomCode)
        );
    }

    private async Task StartMatchmakingWithRetry(int attempts, Func<int, string> statusFactory, Func<Task<bool>> startAttempt)
    {
        isMatchmaking = true;
        SetInteractable(false);

        bool success = false;
        attempts = Mathf.Max(1, attempts);

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            SetStatus(statusFactory != null ? statusFactory(attempt) : "Finding match...");

            try
            {
                success = await startAttempt();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MatchmakingMenuUI] Matchmaking exception: {exception}");
                success = false;
            }

            if (success)
                break;

            if (attempt < attempts)
            {
                SetStatus($"Match not found. Retrying... ({attempt + 1}/{attempts})");
                await Task.Delay(Mathf.RoundToInt(Mathf.Max(0.1f, retryDelaySeconds) * 1000f));
            }
        }

        // Usually this object is destroyed immediately after the server scene is loaded.
        // If the menu remains visible for any reason, do not leave the player stuck on
        // "Finding match..." forever.
        if (success)
        {
            SetStatus("Match found. Loading battle...");
        }
        else
        {
            SetStatus("Match failed. Make sure the server room is open, not full, and in the same Photon region.");
            SetInteractable(true);
            isMatchmaking = false;
        }
    }

    private void SetInteractable(bool interactable)
    {
        if (playButton != null)
            playButton.interactable = interactable;

        if (createRoomButton != null)
            createRoomButton.interactable = interactable;

        if (roomCodeInput != null)
            roomCodeInput.interactable = interactable;

        if (cancelMatchmakingButton != null)
            cancelMatchmakingButton.interactable = !interactable;
    }

    private string GetSanitizedRoomCodeInput()
    {
        return roomCodeInput != null ? SanitizeRoomCode(roomCodeInput.text) : string.Empty;
    }

    private static string SanitizeRoomCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return string.Empty;

        roomCode = roomCode.Trim();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(8);
        for (int i = 0; i < roomCode.Length; i++)
        {
            char c = roomCode[i];
            if (char.IsDigit(c))
                builder.Append(c);
        }

        string sanitized = builder.ToString();
        if (sanitized.Length > 8)
            sanitized = sanitized.Substring(0, 8);

        return sanitized;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[MatchmakingMenuUI] {message}");
    }
}
