using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class PlayerControl : MonoBehaviour
{
    public enum PlayerState { Idle, MoveTargeting, AttackTargeting, Animating }

    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private ChessControl chessControl;

    [Header("UI & Visuals")]
    [SerializeField] private PieceContextUI pieceContextUI;
    [SerializeField] private GhostPiece ghostPiece;
    [SerializeField] private float ghostSnapDuration = 0.2f;
    [SerializeField] private Vector3 ghostDragOffset = new Vector3(0f, 0.6f, -1f);

    [Header("Player Metadata")]
    [SerializeField] private ChessFaction localPlayerFaction = ChessFaction.ChessAlliance;

    private PlayerState currentState = PlayerState.Idle;
    private ChessPiece selectedPiece;
    private ChessPiece hoveredPiece;
    private BoardTile lastHoveredTile;

    private List<Vector2Int> currentValidMoves = new List<Vector2Int>();
    private List<Vector2Int> currentValidAttacks = new List<Vector2Int>();
    private bool isAnimating = false;

    private void OnEnable()
    {
        chessControl.onTileClicked += HandleTileClicked;
        chessControl.onCancelTriggered += HandleCancel;

        if (gameManager != null) gameManager.OnPieceMoved += HandlePieceMoved;
    }

    private void OnDisable()
    {
        chessControl.onTileClicked -= HandleTileClicked;
        chessControl.onCancelTriggered -= HandleCancel;

        if (gameManager != null) gameManager.OnPieceMoved -= HandlePieceMoved;
    }

    private void Start()
    {
        ghostPiece.Hide();
        pieceContextUI.Hide();
    }

    private void Update()
    {
        BoardTile currentTile = chessControl.hoveredTile;
        Vector2Int currentCell = chessControl.hoveredCell;

        switch (currentState)
        {
            case PlayerState.Idle:
                UpdateIdleHover(currentTile, currentCell);
                break;
            case PlayerState.MoveTargeting:
                UpdateMoveVisuals(currentTile, currentCell);
                break;
            case PlayerState.AttackTargeting:
                UpdateAttackVisuals(currentTile, currentCell);
                break;
        }
    }

    private void HandleTileClicked(BoardTile clickedTile, Vector2Int cellPos)
    {
        if (currentState == PlayerState.Animating) return;

        switch (currentState)
        {
            case PlayerState.MoveTargeting:
                if (currentValidMoves.Contains(cellPos))
                {
                    currentState = PlayerState.Animating;
                    if (lastHoveredTile != null) lastHoveredTile.SetTileState(TileState.None);

                    SnapPiece(cellPos, true);
                }
                else CancelTargeting();
                break;

            case PlayerState.AttackTargeting:
                CancelTargeting();
                break;
        }
    }

    private void HandleCancel()
    {
        if (currentState != PlayerState.Idle) CancelTargeting();
    }

    private void UpdateIdleHover(BoardTile currentTile, Vector2Int currentCell)
    {
        if (lastHoveredTile != null && lastHoveredTile != currentTile)
            lastHoveredTile.ToggleSelection(false);

        if (currentTile != null)
            currentTile.ToggleSelection(true);

        lastHoveredTile = currentTile;

        if (currentTile != null && currentTile.currentPiece != null)
        {
            ChessPieceRuntime runtime = currentTile.currentPiece.pieceData;
            if (runtime != null && runtime.chessFaction == localPlayerFaction)
            {
                if (hoveredPiece != currentTile.currentPiece)
                {
                    hoveredPiece = currentTile.currentPiece;
                    pieceContextUI.Show(hoveredPiece, OnMoveButtonClicked, OnAttackButtonClicked);
                }
            }

            else if (!chessControl.isHoveringUI) HideContextMenu();
        }
        else if (!chessControl.isHoveringUI) HideContextMenu();
    }

    private void HideContextMenu()
    {
        if (hoveredPiece != null)
        {
            pieceContextUI.Hide();
            hoveredPiece = null;
        }
    }

    private void OnMoveButtonClicked()
    {
        if (hoveredPiece == null) return;
        currentState = PlayerState.MoveTargeting;
        selectedPiece = hoveredPiece;
        pieceContextUI.Hide();

        currentValidMoves = chessBoard.boardData.GetValidMoves(selectedPiece.pieceData);
        ShowHighlightTiles(currentValidMoves, TileState.ValidMove);

        selectedPiece.gameObject.SetActive(false);
        ghostPiece.Initialize(selectedPiece.pieceData);
    }

    private void OnAttackButtonClicked()
    {
        if (hoveredPiece == null) return;
        currentState = PlayerState.AttackTargeting;
        selectedPiece = hoveredPiece;
        pieceContextUI.Hide();

        // currentValidAttacks = chessBoard.boardData.GetValidAttacks(selectedPiece.pieceData);
        // ShowHighlightTiles(currentValidAttacks, TileState.AttackTarget); 
    }

    private void UpdateMoveVisuals(BoardTile currentTile, Vector2Int currentCell)
    {
        if (ghostPiece != null)
        {
            Vector3 targetPos = chessControl.mouseWorldPosition;
            targetPos.z = 0;
            ghostPiece.transform.position = Vector3.Lerp(ghostPiece.transform.position, targetPos + ghostDragOffset, 0.5f);
        }
        UpdateDropShadow(currentTile, currentCell, currentValidMoves);
    }

    private void UpdateAttackVisuals(BoardTile currentTile, Vector2Int currentCell)
    {
        UpdateDropShadow(currentTile, currentCell, currentValidAttacks);
    }

    private void UpdateDropShadow(BoardTile currentTile, Vector2Int currentCell, List<Vector2Int> validTiles)
    {
        if (lastHoveredTile != null && lastHoveredTile != currentTile)
        {
            Vector2Int lastPos = new Vector2Int(lastHoveredTile.boardX, lastHoveredTile.boardY);
            lastHoveredTile.SetTileState(validTiles.Contains(lastPos) ? TileState.ValidMove : TileState.None);
        }

        if (currentTile != null)
        {
            bool isValid = validTiles.Contains(currentCell);
            currentTile.SetTileState(isValid ? TileState.HoverValid : TileState.HoverInvalid);
        }

        lastHoveredTile = currentTile;
    }

    private void CancelTargeting()
    {
        if (currentState == PlayerState.MoveTargeting)
        {
            selectedPiece.gameObject.SetActive(true);
            ghostPiece.Hide();
            ClearHighlightTiles(currentValidMoves);
        }
        else if (currentState == PlayerState.AttackTargeting)
        {
            ClearHighlightTiles(currentValidAttacks);
        }

        if (lastHoveredTile != null) lastHoveredTile.SetTileState(TileState.None);

        currentState = PlayerState.Idle;
        selectedPiece = null;
    }

    private void SnapPiece(Vector2Int targetGridPos, bool isRequestingMove)
    {
        ClearHighlightTiles(currentValidMoves);

        Vector2Int originalPos = selectedPiece.pieceData.currentGridPosition;
        BoardTile targetTile = chessBoard.GetTileAt(targetGridPos);

        if (targetTile == null) { CancelTargeting(); return; }

        Vector3 targetWorldPos = targetTile.transform.position + chessBoard.PiecePlacementOffset;

        ghostPiece.transform.DOMove(targetWorldPos, ghostSnapDuration).SetEase(Ease.OutQuad).OnComplete(() =>
        {
            ghostPiece.Hide();
            if (isRequestingMove && gameManager != null)
            {
                gameManager.RequestMovePiece(originalPos, targetGridPos);
            }
        });
    }

    private void HandlePieceMoved(Vector2Int start, Vector2Int finish)
    {
        BoardTile startTile = chessBoard.GetTileAt(start);
        BoardTile finishTile = chessBoard.GetTileAt(finish);
        if (startTile == null || finishTile == null) return;

        ChessPiece movingPieceUI = selectedPiece != null ? selectedPiece : startTile.currentPiece;

        if (finishTile.currentPiece != null && finishTile.currentPiece != movingPieceUI)
            Destroy(finishTile.currentPiece.gameObject);

        startTile.ClearPiece();
        finishTile.SetPiece(movingPieceUI);

        if (movingPieceUI != null)
        {
            movingPieceUI.transform.position = finishTile.transform.position + chessBoard.PiecePlacementOffset;
            movingPieceUI.gameObject.SetActive(true);
        }

        if (gameManager != null) gameManager.OnTurnResolved();

        currentState = PlayerState.Idle;
        selectedPiece = null;
        isAnimating = false;
    }

    private void ShowHighlightTiles(List<Vector2Int> validTiles, TileState state)
    {
        foreach (var pos in validTiles) chessBoard.GetTileAt(pos)?.SetTileState(state);
    }

    private void ClearHighlightTiles(List<Vector2Int> validTiles)
    {
        foreach (var pos in validTiles) chessBoard.GetTileAt(pos)?.SetTileState(TileState.None);
        validTiles.Clear();
    }
}