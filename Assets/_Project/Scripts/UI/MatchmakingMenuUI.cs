using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchmakingMenuUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkRunnerHandler runnerHandler;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Behaviour")]
    [Tooltip("If true, Play joins any open dedicated-server session. If false, Play joins the room code/default room.")]
    [SerializeField] private bool playButtonUsesQuickMatch = true;

    private void Awake()
    {
        if (runnerHandler == null)
            runnerHandler = FindFirstObjectByType<NetworkRunnerHandler>();

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }
    }

    private void OnDestroy()
    {
        if (playButton != null)
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
    }

    private void OnPlayButtonClicked()
    {
        if (playButtonUsesQuickMatch)
            PlayQuickMatch();
        else
            JoinRoomByCode();
    }

    public async void PlayQuickMatch()
    {
        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        await StartMatchmakingTask("Finding match...", runnerHandler.StartClientQuickMatch());
    }

    public async void JoinRoomByCode()
    {
        if (runnerHandler == null)
        {
            SetStatus("Missing NetworkRunnerHandler.");
            return;
        }

        string roomCode = roomCodeInput != null ? roomCodeInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            await StartMatchmakingTask("Finding match...", runnerHandler.StartClientQuickMatch());
            return;
        }

        await StartMatchmakingTask($"Joining {roomCode}...", runnerHandler.StartClientJoinSession(roomCode));
    }

    private async Task StartMatchmakingTask(string status, Task<bool> matchmakingTask)
    {
        SetInteractable(false);
        SetStatus(status);

        bool success = false;

        try
        {
            success = await matchmakingTask;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[MatchmakingMenuUI] Matchmaking exception: {exception}");
            success = false;
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
            SetStatus("Match failed. Please try again.");
            SetInteractable(true);
        }
    }

    private void SetInteractable(bool interactable)
    {
        if (playButton != null)
            playButton.interactable = interactable;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[MatchmakingMenuUI] {message}");
    }
}
