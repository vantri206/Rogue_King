using UnityEngine;

public class ChessPiece : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    public ChessPieceRuntime pieceData { get; private set; }
    public ChessFaction faction => pieceData != null ? pieceData.faction : ChessFaction.Neutral;

    public void Initialize(ChessPieceRuntime data)
    {
        pieceData = data;

        spriteRenderer.sprite = pieceData.baseData.pieceSprite;
        animator.runtimeAnimatorController = pieceData.baseData.pieceAnimator;

        gameObject.name = $"{pieceData.faction}_{pieceData.baseData.pieceName}";
    }
}