using UnityEngine;

public class BoardTile : MonoBehaviour
{
    public int gridX { get; private set; }
    public int gridY { get; private set; }

    public ChessPiece currentPiece { get; private set; }

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    [Header("Tile Color Sprites")]
    [SerializeField] private Sprite whiteSprite;
    [SerializeField] private Sprite blackSprite;
     
    private bool isWhiteTile;

    private readonly int isWhiteHash = Animator.StringToHash("isWhite");
    private readonly int isSelectedHash = Animator.StringToHash("isSelected");

    public void Initialize(int x, int y)
    {
        gridX = x;
        gridY = y;

        isWhiteTile = ((x + y) % 2 == 0);
        spriteRenderer.sprite = isWhiteTile ? whiteSprite : blackSprite;
        gameObject.name = $"Tile_{x}_{y}";

        if (animator != null)
        {
            animator.SetBool(isWhiteHash, isWhiteTile);
        }
    }

    public void SetPiece(ChessPiece piece)
    {
        currentPiece = piece;
    }

    public void ClearPiece()
    {
        currentPiece = null;
    }

    public void ToggleSelection(bool isSelected)
    {
        if (animator != null)
        {
            animator.SetBool(isSelectedHash, isSelected);
        }
    }
}