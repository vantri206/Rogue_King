using Fusion;

public struct NetworkCardInstance : INetworkStruct
{
    public int cardDataIndex; // Vị trí thẻ bài trong Kho (Database) của Server
    public int currentCooldown;
    public int remainingUses;
    public NetworkBool isInitialized;
}