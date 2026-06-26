using TMPro;
using UnityEngine;

public class MatchRoomInfoUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI roomIdText;
    [SerializeField] private string labelPrefix = "Room ID: ";
    [SerializeField] private bool hideWhenEmpty = true;

    private string lastRoomCode;

    private void Awake()
    {
        if (roomIdText == null)
            roomIdText = GetComponent<TextMeshProUGUI>();

        Refresh(true);
    }

    private void Update()
    {
        Refresh(false);
    }

    private void Refresh(bool force)
    {
        string roomCode = ResolveRoomCode();
        if (!force && roomCode == lastRoomCode)
            return;

        lastRoomCode = roomCode;

        if (roomIdText == null)
            return;

        bool hasCode = !string.IsNullOrWhiteSpace(roomCode);
        roomIdText.gameObject.SetActive(hasCode || !hideWhenEmpty);
        roomIdText.text = hasCode ? $"{labelPrefix}{roomCode}" : string.Empty;
    }

    private string ResolveRoomCode()
    {
        if (NetworkRunnerHandler.Active != null && !string.IsNullOrWhiteSpace(NetworkRunnerHandler.Active.CurrentRoomCode))
            return NetworkRunnerHandler.Active.CurrentRoomCode;

        return ClientMatchRoomContext.CurrentRoomCode;
    }
}
