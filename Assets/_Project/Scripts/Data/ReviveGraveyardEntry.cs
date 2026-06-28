using Fusion;
using UnityEngine;

public struct ReviveGraveyardEntry : INetworkStruct
{
    public NetworkBool isActive;
    public int pieceDataIndex;
    public ChessFaction faction;
    public int deathX;
    public int deathY;

    public Vector2Int DeathPos => new Vector2Int(deathX, deathY);
}
