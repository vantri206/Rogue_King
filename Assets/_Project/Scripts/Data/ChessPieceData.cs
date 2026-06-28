using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewChessPiece", menuName = "Chess/Piece Data")]
public class ChessPieceData : ScriptableObject
{
    [Header("Basic Info")]
    public string pieceName;
    public int baseAttack = 1;
    public int baseHealth = 1;

    [Header("Movement Pattern")]
    public MovementType moveType;
    public int maxMoveRange = 1;

    [Header("Pawn Rule Settings")]
    [Tooltip("Only used by pieces whose pieceName contains Pawn. Use (1, 0) for right, (-1, 0) for left, (0, 1) for up, (0, -1) for down.")]
    public Vector2Int pawnForwardDirection = Vector2Int.right;

    [Tooltip("If enabled, ChessRogue pawns use the opposite of Pawn Forward Direction. Keep OFF if all pawns should move in the same board direction.")]
    public bool mirrorPawnForwardForRogueFaction = false;

    [Header("Visuals")]
    public Sprite pieceSprite;
    public RuntimeAnimatorController pieceAnimator;

    [HideInInspector]
    public bool[] movePatternGrid = new bool[25];

    [HideInInspector]
    public List<Vector2Int> moveDirections = new List<Vector2Int>();

    [Header("Combat Settings")]
    public WeaponData equippedWeapon;

}