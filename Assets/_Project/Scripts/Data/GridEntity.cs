using UnityEngine;

public abstract class GridEntity
{
    public Vector2Int currentGridPosition { get; set; }
    public ChessFaction faction { get; set; }

    protected GridEntity(Vector2Int startPos, ChessFaction assignedFaction)
    {
        currentGridPosition = startPos;
        faction = assignedFaction;
    }
    public abstract bool IsBlockingMovement();

    public abstract bool IsBlockingLineOfSight();
}