using UnityEngine;

public class GhostPiece : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    private static readonly int isAirHash = Animator.StringToHash("isAir");

    public void Initialize(ChessPieceRuntime data)
    {
        if (data?.baseData == null) return;

        gameObject.SetActive(true);

        animator.runtimeAnimatorController = data.baseData.pieceAnimator;
        spriteRenderer.sprite = data.baseData.pieceSprite;

        animator.SetBool(isAirHash, true);

        gameObject.name = $"Ghost_{data.faction}_{data.baseData.pieceName}";
    }

    public void Hide()
    {
        if (animator.runtimeAnimatorController != null)
        {
            animator.SetBool(isAirHash, false);
        }

        gameObject.SetActive(false);
    }
}