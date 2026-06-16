using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ChessPieceRuntime : GridEntity
{
    public ChessPieceData baseData { get; private set; }

    public int currentHealth;
    public int currentAttack;

    public int currentMoveRange;
    public MovementType currentMoveType;
    public List<Vector2Int> currentMoveDirections;

    public ChessPieceRuntime(ChessPieceData data, Vector2Int startPos, ChessFaction assignedFaction)
        : base(startPos, assignedFaction)
    {
        baseData = data;
        currentHealth = data.baseHealth;
        currentAttack = data.baseAttack;

        currentMoveRange = data.maxMoveRange;
        currentMoveType = data.moveType;
        currentMoveDirections = new List<Vector2Int>(data.moveDirections);
    }

    public override bool IsBlockingMovement() => true;
    public override bool IsBlockingLineOfSight() => true;
}