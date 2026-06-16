using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class PlayerControl : MonoBehaviour
{
    public enum PlayerState { Idle, DraggingPiece, Aiming, ConfirmingAttack, Animating }

    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private CombatManager combatManager;
    [SerializeField] private ChessControl chessControl;

    [Header("Visuals")]
    [SerializeField] private GhostPiece ghostPiece;
    [SerializeField] private float ghostSnapDuration = 0.2f;
    [SerializeField] private Vector3 ghostDragOffset = new Vector3(0f, 0.6f, -1f);

    [Header("UI")]
    [SerializeField] private PieceContextUI pieceContextUI;
    [SerializeField] private WeaponControllerUI weaponUI;

    [Header("Rogue Player Loadout")]
    [SerializeField] private List<WeaponData> equippedWeapons = new List<WeaponData>();
    private int currentWeaponIndex = 0;

    private bool hasMovedThisTurn = false;

    private PlayerState currentState = PlayerState.Idle;
    private ChessPiece selectedPiece;
    private BoardTile lastHoveredTile;

    private ChessPiece currentHoveredPieceForUI;

    private List<Vector2Int> currentValidMoves = new List<Vector2Int>();

    private List<Vector2Int> currentValidAttacks = new List<Vector2Int>();

    private List<Vector2Int> currentAoETiles = new List<Vector2Int>();

    private Vector2Int lockedAttackTarget;

    private void Awake()
    {
        if (gameManager == null) gameManager = GameManager.Instance;
        if (combatManager == null) combatManager = CombatManager.Instance;
    }

    private void Start()
    {
        ghostPiece.Hide();
        if (pieceContextUI != null) pieceContextUI.Hide();

        InitializeWeaponUI();
        UpdateWeaponUIDisplay();

        if (gameManager != null)
        {
            HandleTurnChanged(gameManager.currentTurnFaction);
        }
    }

    private void OnEnable()
    {
        chessControl.onPointerDown += HandlePointerDown;
        chessControl.onPointerUp += HandlePointerUp;
        chessControl.onCancelTriggered += CancelTargeting;

        if (gameManager != null)
        {
            gameManager.OnPieceMoved += HandlePieceMoved;
            gameManager.OnTurnChanged += HandleTurnChanged;
        }

        if (weaponUI != null)
        {
            weaponUI.onActionPressed += OnRogueActionPressed;
            weaponUI.onWeaponSelected += OnRogueWeaponSelected;
        }
    }

    private void OnDisable()
    {
        chessControl.onPointerDown -= HandlePointerDown;
        chessControl.onPointerUp -= HandlePointerUp;
        chessControl.onCancelTriggered -= CancelTargeting;

        if (gameManager != null)
        {
            gameManager.OnPieceMoved -= HandlePieceMoved;
            gameManager.OnTurnChanged -= HandleTurnChanged;
        }

        if (weaponUI != null)
        {
            weaponUI.onActionPressed -= OnRogueActionPressed;
            weaponUI.onWeaponSelected -= OnRogueWeaponSelected;
        }
    }

    private void InitializeWeaponUI()
    {
        if (weaponUI != null && equippedWeapons != null && equippedWeapons.Count > 0)
        {
            weaponUI.SetupWeaponSlots(equippedWeapons);
            Debug.Log("[PlayerControl] Successfully pushed Weapon Data to UI.");
        }
        else
        {
            Debug.LogWarning("[PlayerControl] Failed to init Weapon UI. Weapons list is empty or UI is null.");
        }
    }


    private void Update()
    {
        BoardTile currentTile = chessControl.hoveredTile;
        Vector2Int currentCell = chessControl.hoveredCell;

        switch (currentState)
        {
            case PlayerState.Idle:
                UpdateIdleHover(currentTile);
                break;
            case PlayerState.DraggingPiece:
                UpdateDragVisuals(currentTile, currentCell);
                break;
        }
    }

    private void HandlePointerDown(BoardTile clickedTile, Vector2Int cellPos)
    {
        if (clickedTile == null) return;

        switch (currentState)
        {
            case PlayerState.Idle:
                ChessPiece targetPiece = clickedTile.currentPiece;
                if (targetPiece != null && targetPiece.pieceData != null)
                {
                    if (gameManager.CanPlayerAction(targetPiece.pieceData.faction))
                    {
                        if (targetPiece.faction == ChessFaction.ChessRogue && hasMovedThisTurn)
                        {
                            return;
                        }
                        StartDragging(targetPiece);
                    }
                }
                break;

            case PlayerState.Aiming:
            case PlayerState.ConfirmingAttack:
                if (currentValidAttacks.Contains(cellPos))
                {
                    lockedAttackTarget = cellPos;
                    currentState = PlayerState.ConfirmingAttack;
                    UpdateAttackPreviewVisuals();

                    if (weaponUI != null) weaponUI.SetActionMode(true);
                    Debug.Log($"[PlayerControl] Target locked at {cellPos}. Ready to fire.");
                }
                else
                {
                    CancelTargeting();
                }
                break;
        }
    }

    private void HandlePointerUp(BoardTile releasedTile, Vector2Int cellPos)
    {
        if (currentState != PlayerState.DraggingPiece) return;

        if (currentValidMoves.Contains(cellPos))
        {
            currentState = PlayerState.Animating;
            if (lastHoveredTile != null) lastHoveredTile.SetTileState(TileState.None);
            SnapPieceAndMove(cellPos);
        }
        else
        {
            CancelTargeting();
        }
    }

    private void UpdateIdleHover(BoardTile currentTile)
    {
        if (lastHoveredTile != null && lastHoveredTile != currentTile)
            lastHoveredTile.ToggleSelection(false);

        if (currentTile != null)
            currentTile.ToggleSelection(true);

        lastHoveredTile = currentTile;

        if (currentTile != null && currentTile.currentPiece != null && !chessControl.isHoveringUI)
        {
            if (currentHoveredPieceForUI != currentTile.currentPiece)
            {
                currentHoveredPieceForUI = currentTile.currentPiece;
                if (pieceContextUI != null) pieceContextUI.Show(currentHoveredPieceForUI);
            }
        }
        else
        {
            HideStatsUI();
        }
    }
    private void HideStatsUI()
    {
        if (currentHoveredPieceForUI != null)
        {
            currentHoveredPieceForUI = null;
            if (pieceContextUI != null) pieceContextUI.Hide();
        }
    }

    private void StartDragging(ChessPiece piece)
    {
        currentState = PlayerState.DraggingPiece;
        selectedPiece = piece;

        HideStatsUI();

        if (lastHoveredTile != null)
        {
            lastHoveredTile.ToggleSelection(false);
        }

        currentValidMoves = chessBoard.boardData.GetValidMoves(selectedPiece.pieceData);
        ShowHighlightTiles(currentValidMoves, TileState.ValidMove);

        selectedPiece.gameObject.SetActive(false);
        ghostPiece.Initialize(selectedPiece.pieceData);
    }

    private void UpdateDragVisuals(BoardTile currentTile, Vector2Int currentCell)
    {
        if (ghostPiece != null)
        {
            Vector3 targetPos = chessControl.mouseWorldPosition;
            targetPos.z = 0;
            ghostPiece.transform.position = Vector3.Lerp(ghostPiece.transform.position, targetPos + ghostDragOffset, 0.5f);
        }

        UpdateDropShadow(currentTile, currentCell, currentValidMoves);
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

    private void SnapPieceAndMove(Vector2Int targetGridPos)
    {
        ClearHighlightTiles(currentValidMoves);
        Vector2Int originalPos = selectedPiece.pieceData.currentGridPosition;
        BoardTile targetTile = chessBoard.GetTileAt(targetGridPos);

        if (targetTile == null)
        {
            CancelTargeting();
            return;
        }

        Vector3 targetWorldPos = targetTile.transform.position + chessBoard.PiecePlacementOffset;

        ghostPiece.transform.DOMove(targetWorldPos, ghostSnapDuration).SetEase(Ease.OutQuad).OnComplete(() =>
        {
            ghostPiece.Hide();
            if (gameManager != null)
            {
                gameManager.RequestMovePiece(originalPos, targetGridPos);
            }
        });
    }

    private void CancelTargeting()
    {
        ClearHighlightTiles(currentValidMoves);
        ClearHighlightTiles(currentValidAttacks);
        ClearHighlightTiles(currentAoETiles);

        currentValidMoves.Clear();
        currentValidAttacks.Clear();
        currentAoETiles.Clear();

        currentState = PlayerState.Idle;
        lockedAttackTarget = new Vector2Int(-1, -1);

        if (weaponUI != null) weaponUI.SetActionMode(false);
    }

    private void HandlePieceMoved(Vector2Int start, Vector2Int finish)
    {
        BoardTile startTile = chessBoard.GetTileAt(start);
        BoardTile finishTile = chessBoard.GetTileAt(finish);

        if (startTile == null || finishTile == null) return;

        ChessPiece movingPieceUI = selectedPiece != null ? selectedPiece : startTile.currentPiece;

        if (finishTile.currentPiece != null && finishTile.currentPiece != movingPieceUI)
        {
            Debug.Log($"[PlayerControl] Destroyed piece at {finish}");
            Destroy(finishTile.currentPiece.gameObject);
        }

        startTile.ClearPiece();
        finishTile.SetPiece(movingPieceUI);

        if (movingPieceUI != null)
        {
            movingPieceUI.transform.position = finishTile.transform.position + chessBoard.PiecePlacementOffset;
            movingPieceUI.gameObject.SetActive(true);
        }

        if (lastHoveredTile != null)
        {
            lastHoveredTile.SetTileState(TileState.None);
            lastHoveredTile.ToggleSelection(false);
            lastHoveredTile = null;
        }

        if (gameManager != null)
        {
            if (movingPieceUI.faction == ChessFaction.ChessRogue)
            {
                hasMovedThisTurn = true;
                gameManager.ActionCompleted(true);
            }
            else
            {
                gameManager.ActionCompleted(true);
            }
        }

        currentState = PlayerState.Idle;
        selectedPiece = null;
    }

    private WeaponData GetActiveWeapon()
    {
        if (equippedWeapons == null || equippedWeapons.Count == 0) return null;
        return equippedWeapons[currentWeaponIndex];
    }

    private void OnRogueActionPressed()
    {
        if (currentState == PlayerState.Idle)
        {
            ChessPiece rogueKing = FindRogueKingPiece();
            WeaponData activeWeapon = GetActiveWeapon();

            if (rogueKing == null || rogueKing.pieceData == null || activeWeapon == null) return;

            selectedPiece = rogueKing;
            currentState = PlayerState.Aiming;

            currentValidAttacks = ActionResolver.GetTargetingRange(activeWeapon, selectedPiece.pieceData.currentGridPosition, chessBoard.boardData);
            ShowHighlightTiles(currentValidAttacks, TileState.HoverValid);

            if (weaponUI != null) weaponUI.SetActionMode(false);
            Debug.Log("[PlayerControl] Entered Aiming State.");
        }
        else if (currentState == PlayerState.ConfirmingAttack)
        {
            if (selectedPiece != null && selectedPiece.pieceData != null)
            {
                combatManager.ExecuteAttack(selectedPiece.pieceData, GetActiveWeapon(), lockedAttackTarget);
                hasMovedThisTurn = true;
                CancelTargeting();
                gameManager.ActionCompleted(true);
            }
            CancelTargeting();
        }
    }

    private void OnRogueWeaponSelected(int index)
    {
        if (currentState != PlayerState.Idle) return;

        if (index >= 0 && index < equippedWeapons.Count && index != currentWeaponIndex)
        {
            currentWeaponIndex = index;

            if (weaponUI != null) weaponUI.UpdateActiveWeaponHighlight(index);

            Debug.Log($"[PlayerControl] Swapped weapon to {GetActiveWeapon().weaponName}. Turn ends.");
            if (gameManager != null) gameManager.ForceResolveTurn();
        }
    }
    private void UpdateAttackPreviewVisuals()
    {
        foreach (var pos in currentAoETiles)
        {
            BoardTile tile = chessBoard.GetTileAt(pos);
            if (tile != null)
            {
                if (currentValidAttacks.Contains(pos))
                {
                    tile.SetTileState(TileState.HoverValid);
                }
                else
                {
                    tile.SetTileState(TileState.None);
                }
            }
        }
        currentAoETiles.Clear();

        if (currentState == PlayerState.ConfirmingAttack)
        {
            WeaponData activeWeapon = GetActiveWeapon();
            currentAoETiles = ActionResolver.GetAoE(activeWeapon, selectedPiece.pieceData.currentGridPosition, lockedAttackTarget, chessBoard.boardData);

            ShowHighlightTiles(currentAoETiles, TileState.AttackTarget);
        }
    }

    private ChessPiece FindRogueKingPiece()
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                BoardTile tile = chessBoard.GetTileAt(new Vector2Int(x, y));
                if (tile != null && tile.currentPiece != null && tile.currentPiece.faction == ChessFaction.ChessRogue)
                {
                    return tile.currentPiece;
                }
            }
        }
        return null;
    }

    private void HandleTurnChanged(ChessFaction currentTurnFaction)
    {
        hasMovedThisTurn = false;
        currentState = PlayerState.Idle;
        CancelTargeting();


        bool isRogueTurn = (currentTurnFaction == ChessFaction.ChessRogue);

        if (weaponUI != null)
        {
            weaponUI.TogglePanel(isRogueTurn);

            if (isRogueTurn)
            {
                weaponUI.SetActionMode(false);
            }
        }

        Debug.Log($"[PlayerControl] Turn changed to: {currentTurnFaction}. Weapon UI Active: {isRogueTurn}");
    }

    private void UpdateWeaponUIDisplay()
    {
        if (weaponUI != null)
        {
            weaponUI.SetupWeaponSlots(equippedWeapons);
            weaponUI.UpdateActiveWeaponHighlight(currentWeaponIndex);
        }
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