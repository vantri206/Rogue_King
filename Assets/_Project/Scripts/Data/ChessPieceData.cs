using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.ScrollRect;

public enum MovementType
{
    Slide,  // Can move in a direction until blocked
    Jump    // Can move to specific positions regardless of obstacles
}

[CreateAssetMenu(fileName = "NewChessPiece", menuName = "Chess/Piece Data")]
public class ChessPieceData : ScriptableObject
{
    [Header("Basic Info")]
    public string pieceName;
    public int baseHealth = 1;

    [Header("Movement Pattern")]
    public MovementType moveType;
    public int maxMoveRange = 1;

    [Header("Visuals")]
    public Sprite pieceSprite;
    public RuntimeAnimatorController pieceAnimator;

    [HideInInspector]
    public bool[] movePatternGrid = new bool[25];

    [HideInInspector]
    public List<Vector2Int> moveDirections = new List<Vector2Int>();
}