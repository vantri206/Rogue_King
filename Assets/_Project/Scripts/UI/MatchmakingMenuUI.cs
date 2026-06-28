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

    [Tooltip("How long a created Room ID can stay waiting in the menu before the client auto-cancels the reservation.")]
    [SerializeField] private float customRoomWaitTimeoutSeconds = 180f;

    [Tooltip("After connecting to the lobby session, wait briefly for the local PlayerNetworkController to spawn before sending Play/Create/Join RPCs. This avoids the first click being lost and needing Cancel -> Play again.")]
    [SerializeField] private float localLobbyPlayerObjectWaitTimeoutSeconds = 5f;

    [Tooltip("If another lobby connect operation is already running from Start(), button clicks wait this long for it instead of failing immediately.")]
    [SerializeField] private float lobbyConnectInProgressWaitTimeoutSeconds = 8f;

    [Tooltip("Safety for Play -> Cancel -> Play demo flow: while waiting for quick match, resend the ready request every few seconds. Server-side request serial makes this idempotent and prevents stale cancel RPCs from deleting the newest ready request.")]
    [SerializeField] private bool autoReassertQuickMatchWhileWaiting = true;

    [SerializeField, Min(1f)] private float quickMatchReassertIntervalSeconds = 3f;

    private bool isMatchmaking;
    private bool lobbyConnectInProgress;
    private bool currentLobbyWaitIsQuickMatch;
    private Coroutine lobbyWaitTimeoutCoroutine;
    private Coroutine quickMatchReassertCoroutine;
    private int lobbyUiOperationSerial;
    private bool roomCodeWasAutoFilledByCreatedRoom;

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

        StopQuickMatchReassert();
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
        AdvanceLobbyUiOperationSerial();
        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;

        if (roomCodeWasAutoFilledByCreatedRoom && roomCodeInput != null)
            roomCodeInput.text = string.Empty;
        roomCodeWasAutoFilledByCreatedRoom = false;

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

        if (lobbyConnectInProgress)
        {
            float waitTimeout = Mathf.Max(0.5f, lobbyConnectInProgressWaitTimeoutSeconds);
            float startTime = Time.realtimeSinceStartup;
            while (lobbyConnectInProgress && Time.realtimeSinceStartup - startTime < waitTimeout)
                await Task.Yield();

            return runnerHandler != null && runnerHandler.IsClientConnectedToLobby;
        }

        if (runnerHandler.HasRunnerStarted)
        {
            SetStatus("Runner already started outside lobby.");
            return false;
        }

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

        int operationSerial = BeginLobbyUiOperation();
        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;
        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!IsLobbyUiOperationCurrent(operationSerial))
                return;

            if (!connected)
            {
                SetStatus("Cannot find match because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        await WaitForLocalLobbyPlayerControllerReady(operationSerial, "Đang chuẩn bị lobby player object trước khi tìm trận...");
        if (!IsLobbyUiOperationCurrent(operationSerial))
            return;

        currentLobbyWaitIsQuickMatch = true;
        bool requested = runnerHandler.ClientRequestLobbyMatchmaking();
        if (requested)
        {
            SetStatus("Đang tìm trận... chờ người chơi khác bấm Play.");
            StartLobbyWaitTimeout(lobbyMatchmakingTimeoutSeconds);
            StartQuickMatchReassert();
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

        int operationSerial = BeginLobbyUiOperation();
        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;
        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!IsLobbyUiOperationCurrent(operationSerial))
                return;

            if (!connected)
            {
                SetStatus("Cannot create room because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        await WaitForLocalLobbyPlayerControllerReady(operationSerial, "Đang chuẩn bị lobby player object trước khi tạo Room ID...");
        if (!IsLobbyUiOperationCurrent(operationSerial))
            return;

        bool requested = runnerHandler.ClientRequestCreateCustomRoom();
        if (requested)
        {
            SetStatus("Creating room...");
            StartLobbyWaitTimeout(customRoomWaitTimeoutSeconds);
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

        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;
        roomCodeWasAutoFilledByCreatedRoom = false;
        roomCode = SanitizeRoomCode(roomCode);
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            RequestMatchFromLobby();
            return;
        }

        int operationSerial = BeginLobbyUiOperation();
        isMatchmaking = true;
        SetInteractable(false);

        if (!runnerHandler.IsClientConnectedToLobby)
        {
            bool connected = await ConnectLobbyForMenuLiveData();
            if (!IsLobbyUiOperationCurrent(operationSerial))
                return;

            if (!connected)
            {
                SetStatus("Cannot join room because lobby is not connected.");
                SetInteractable(true);
                isMatchmaking = false;
                return;
            }
        }

        await WaitForLocalLobbyPlayerControllerReady(operationSerial, "Đang chuẩn bị lobby player object trước khi join Room ID...");
        if (!IsLobbyUiOperationCurrent(operationSerial))
            return;

        bool requested = runnerHandler.ClientRequestJoinCustomRoom(roomCode);
        if (requested)
        {
            SetStatus($"Checking Room ID {roomCode}...");
            StartLobbyWaitTimeout(lobbyMatchmakingTimeoutSeconds);
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
        AdvanceLobbyUiOperationSerial();
        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;

        isMatchmaking = false;
        SetInteractable(false);
        SetStatus("Match found. Choose your cards, then press Fight!");
    }

    public void ShowLobbyRoomCreated(string roomCode)
    {
        roomCode = SanitizeRoomCode(roomCode);

        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;
        isMatchmaking = true;
        SetInteractable(false);

        if (roomCodeInput != null)
            roomCodeInput.text = roomCode;
        roomCodeWasAutoFilledByCreatedRoom = true;

        SetStatus(string.IsNullOrWhiteSpace(roomCode)
            ? "Room created. Waiting for another player to join by code..."
            : $"Room ID: {roomCode}. Gửi mã này cho người chơi thứ 2. Có thể bấm Cancel để hủy phòng.");

        StartLobbyWaitTimeout(customRoomWaitTimeoutSeconds);
    }

    public void ShowLobbyRoomError(string message)
    {
        AdvanceLobbyUiOperationSerial();

        if (string.IsNullOrWhiteSpace(message))
            message = "Sai Room ID";

        SetStatus(message);
        SetInteractable(true);
        isMatchmaking = false;

        if (roomCodeWasAutoFilledByCreatedRoom && roomCodeInput != null)
            roomCodeInput.text = string.Empty;
        roomCodeWasAutoFilledByCreatedRoom = false;

        StopLobbyWaitTimeout();
        StopQuickMatchReassert();
        currentLobbyWaitIsQuickMatch = false;
    }

    private void StartQuickMatchReassert()
    {
        StopQuickMatchReassert();

        if (!autoReassertQuickMatchWhileWaiting || !currentLobbyWaitIsQuickMatch)
            return;

        quickMatchReassertCoroutine = StartCoroutine(QuickMatchReassertRoutine());
    }

    private void StopQuickMatchReassert()
    {
        if (quickMatchReassertCoroutine != null)
        {
            StopCoroutine(quickMatchReassertCoroutine);
            quickMatchReassertCoroutine = null;
        }
    }

    private System.Collections.IEnumerator QuickMatchReassertRoutine()
    {
        float delay = Mathf.Max(1f, quickMatchReassertIntervalSeconds);

        while (isMatchmaking && currentLobbyWaitIsQuickMatch)
        {
            yield return new WaitForSecondsRealtime(delay);

            if (!isMatchmaking || !currentLobbyWaitIsQuickMatch)
                break;

            ResolveRunnerHandler();
            if (runnerHandler != null && runnerHandler.IsClientConnectedToLobby)
            {
                bool resent = runnerHandler.ClientRequestLobbyMatchmaking();
                if (resent)
                    SetStatus("Đang tìm trận... đã gửi lại ready request để chống kẹt lobby.");
            }
        }

        quickMatchReassertCoroutine = null;
    }

    private void StartLobbyWaitTimeout(float timeoutSeconds)
    {
        StopLobbyWaitTimeout();

        float timeout = Mathf.Max(5f, timeoutSeconds);
        lobbyWaitTimeoutCoroutine = StartCoroutine(LobbyWaitTimeoutRoutine(timeout));
    }

    private void StopLobbyWaitTimeout()
    {
        if (lobbyWaitTimeoutCoroutine != null)
        {
            StopCoroutine(lobbyWaitTimeoutCoroutine);
            lobbyWaitTimeoutCoroutine = null;
        }

        StopQuickMatchReassert();
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
            // Important: if a client times out visually but remains in the lobby ready queue,
            // the next Play click can look stuck because the server still has stale intent.
            // Cancel on server first, then let the player press Play again cleanly.
            runnerHandler.ClientCancelLobbyMatchmaking();
            StopQuickMatchReassert();
            currentLobbyWaitIsQuickMatch = false;
            SetStatus("Hết thời gian chờ lobby. Đã hủy request/phòng cũ, bạn có thể bấm Play hoặc Create Room lại ngay.");
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

    private int BeginLobbyUiOperation()
    {
        AdvanceLobbyUiOperationSerial();
        return lobbyUiOperationSerial;
    }

    private void AdvanceLobbyUiOperationSerial()
    {
        lobbyUiOperationSerial++;
        if (lobbyUiOperationSerial <= 0)
            lobbyUiOperationSerial = 1;
    }

    private bool IsLobbyUiOperationCurrent(int operationSerial)
    {
        return operationSerial == lobbyUiOperationSerial && isMatchmaking;
    }

    private async Task<bool> WaitForLocalLobbyPlayerControllerReady(int operationSerial, string waitingStatus)
    {
        ResolveRunnerHandler();

        if (runnerHandler == null || !runnerHandler.IsClientConnectedToLobby)
            return false;

        if (runnerHandler.IsLocalLobbyPlayerControllerReady)
            return true;

        if (!string.IsNullOrWhiteSpace(waitingStatus))
            SetStatus(waitingStatus);

        float timeout = Mathf.Max(0.25f, localLobbyPlayerObjectWaitTimeoutSeconds);
        float startTime = Time.realtimeSinceStartup;
        while (IsLobbyUiOperationCurrent(operationSerial) &&
               runnerHandler != null &&
               runnerHandler.IsClientConnectedToLobby &&
               !runnerHandler.IsLocalLobbyPlayerControllerReady &&
               Time.realtimeSinceStartup - startTime < timeout)
        {
            await Task.Yield();
        }

        return runnerHandler != null && runnerHandler.IsClientConnectedToLobby && runnerHandler.IsLocalLobbyPlayerControllerReady;
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
