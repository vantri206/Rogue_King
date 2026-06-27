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

    public int currentSkillCooldown;
    public bool hasShield;
    public bool hasUsedRevive;
    public int sweepUsesLeft;
    public bool isCharmedAlly;
    public int silencedTurnsLeft;

    public Vector2Int previousGridPosition; 
    public bool canAttackStraight;          
    public bool isSuperBuffed;
    public bool hasMoved;

    public ChessPieceRuntime(ChessPieceData data, Vector2Int startPos, ChessFaction assignedFaction)
        : base(startPos, assignedFaction)
    {
        baseData = data;
        currentHealth = data.baseHealth;
        currentAttack = data.baseAttack;

        currentMoveRange = data.maxMoveRange;
        currentMoveType = data.moveType;
        currentMoveDirections = new List<Vector2Int>(data.moveDirections);

        currentSkillCooldown = 0;
        hasShield = false;
        hasUsedRevive = false;
        silencedTurnsLeft = 0;
        sweepUsesLeft = 1;
        isCharmedAlly = false;

        previousGridPosition = startPos;
        canAttackStraight = false;
        isSuperBuffed = false;
        hasMoved = false;
    }

    public override bool IsBlockingMovement() => true;
    public override bool IsBlockingLineOfSight() => true;
}