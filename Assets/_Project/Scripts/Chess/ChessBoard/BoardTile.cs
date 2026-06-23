using UnityEngine;

public enum TileState
{
    None,
    ValidMove,
    HoverInvalid,
    HoverValid,
    AttackTarget, // Orange AoE / impact preview
    AttackRange   // Blue targeting range for weapon aiming
}

public class BoardTile : MonoBehaviour
{
    public int boardX;
    public int boardY;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer highlightRenderer;

    [Header("Tile Color Sprites")]
    [SerializeField] private Sprite whiteSprite;
    [SerializeField] private Sprite blackSprite;

    [Header("Highlights Colors")]
    public Color colorValidMove = Color.green;
    public Color colorHoverValid = Color.cyan;
    public Color colorHoverInvalid = Color.red;
    public Color colorAttackTarget = Color.orange;
    public Color colorAttackRange = Color.cyan;

    private bool isWhiteTile;

    public ChessPiece currentPiece { get; private set; }

    private readonly int isWhiteHash = Animator.StringToHash("isWhite");
    private readonly int isSelectedHash = Animator.StringToHash("isSelected");

    public void Initialize(int x, int y)
    {
        boardX = x;
        boardY = y;

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

    public void SetTileState(TileState state)
    {
        if (highlightRenderer == null)
            return;

        if (state == TileState.None)
        {
            highlightRenderer.gameObject.SetActive(false);
            return;
        }

        highlightRenderer.gameObject.SetActive(true);

        switch (state)
        {
            case TileState.ValidMove:
                highlightRenderer.color = colorValidMove;
                break;
            case TileState.HoverValid:
                highlightRenderer.color = colorHoverValid;
                break;
            case TileState.HoverInvalid:
                highlightRenderer.color = colorHoverInvalid;
                break;
            case TileState.AttackTarget:
                highlightRenderer.color = colorAttackTarget;
                break;
            case TileState.AttackRange:
                highlightRenderer.color = colorAttackRange;
                break;
        }
    }
}