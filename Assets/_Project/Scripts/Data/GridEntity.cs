using UnityEngine;

public abstract class GridEntity
{
    public Vector2Int currentGridPosition { get; set; }
    public ChessFaction faction { get; protected set; }

    protected GridEntity(Vector2Int startPos, ChessFaction assignedFaction)
    {
        currentGridPosition = startPos;
        faction = assignedFaction;
    }
    public abstract bool IsBlockingMovement();

    public abstract bool IsBlockingLineOfSight();
}