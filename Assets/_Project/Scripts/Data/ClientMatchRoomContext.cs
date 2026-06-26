public static class ClientMatchRoomContext
{
    public static string CurrentRoomCode { get; private set; } = string.Empty;

    public static bool HasRoomCode => !string.IsNullOrWhiteSpace(CurrentRoomCode);

    public static void SetRoomCode(string roomCode)
    {
        CurrentRoomCode = string.IsNullOrWhiteSpace(roomCode) ? string.Empty : roomCode.Trim();
    }

    public static void Clear()
    {
        CurrentRoomCode = string.Empty;
    }
}
