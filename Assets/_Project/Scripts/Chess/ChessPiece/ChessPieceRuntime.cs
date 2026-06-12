using UnityEngine;
using System.Collections.Generic;

public enum ChessFaction
{
    ChessAlliance,
    ChessRogue
}

[System.Serializable]
public class ChessPieceRuntime
{
    public ChessPieceData baseData { get; private set; }
    public ChessFaction chessFaction { get; private set; }

    public int currentHealth;
    public int currentAttack;
    public Vector2Int currentGridPosition;

    public int currentMoveRange;
    public MovementType currentMoveType;
    public List<Vector2Int> currentMoveDirections;

    public WeaponData currentWeapon { get; private set; }

    public ChessPieceRuntime(ChessPieceData data, Vector2Int startPos, ChessFaction assignedFaction)
    {
        baseData = data;
        currentGridPosition = startPos;
        chessFaction = assignedFaction;

        currentHealth = data.baseHealth;
        currentAttack = data.baseAttack;

        currentMoveRange = data.maxMoveRange;
        currentMoveType = data.moveType;
        currentMoveDirections = new List<Vector2Int>(data.moveDirections);

        currentWeapon = data.equippedWeapon;
        UpdateAttackStat();
    }
    public void UpdateAttackStat()
    {
        if (currentWeapon == null)
        {
            currentAttack = 0;
            return;
        }

        if (currentWeapon.attackMechanism == AttackMechanism.GridPattern)
        {
            currentAttack = currentWeapon.baseDamage;
        }
    }
}