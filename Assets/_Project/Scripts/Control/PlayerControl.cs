using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class PlayerControl : MonoBehaviour
{
    public enum PlayerState { Idle, DraggingPiece, Aiming, ConfirmingAttack, Animating, AimingSkill }

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
    private List<Vector2Int> currentSpecialSkillTiles = new List<Vector2Int>();
    private ChessPiece selectedPiece;
    private BoardTile lastHoveredTile;

    private ChessPiece currentHoveredPieceForUI;

    private List<Vector2Int> currentValidMoves = new List<Vector2Int>();

    private List<Vector2Int> currentValidAttacks = new List<Vector2Int>();

    private List<Vector2Int> currentAoETiles = new List<Vector2Int>();

    private Vector2Int lockedAttackTarget;
    public bool hasExtraTurn = false;

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

        if (pieceContextUI != null) pieceContextUI.OnSkillButtonClicked += HandleSkillButtonClicked;

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

        if (pieceContextUI != null) pieceContextUI.OnSkillButtonClicked -= HandleSkillButtonClicked;

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

    private void HandleSkillButtonClicked(ChessPiece piece)
    {
        if (piece == null || piece.pieceData == null) return;

        if (!gameManager.CanPlayerAction(piece.faction)) return;

        ChessPieceRuntime data = piece.pieceData;
        pieceContextUI.Show(piece);
    }
    private void ActivateKingRevive(ChessPieceRuntime kingData)
    {
        if (kingData.hasUsedRevive) return;

        var graveyard = gameManager.graveyard;
        DeadPieceRecord targetRecord = null;
        int recordIndex = -1;

        for (int i = graveyard.Count - 1; i >= 0; i--)
        {
            if (graveyard[i].faction == kingData.faction)
            {
                if (chessBoard.boardData.IsTileEmptyForMovement(graveyard[i].deathPos.x, graveyard[i].deathPos.y))
                {
                    targetRecord = graveyard[i];
                    recordIndex = i;
                    break;
                }
            }
        }

        if (targetRecord == null)
        {
            Debug.LogWarning("[Skill] Có đồng đội chết nhưng tọa độ hồi sinh đang bị kẻ khác chiếm chỗ!");
            return; // Trả về, chưa bị mất lượt và chưa bị tính là đã xài skill
        }

        // HỒI SINH! (Dùng chung hàm SpawnPiece của Bàn cờ)
        // Lưu ý: Cần truyền prefab piece, do PlayerControl không giữ, ta tìm 1 con cờ bất kỳ trên bảng để lấy prefab,
        // hoặc để nhanh nhất, hàm SpawnPiece của ChessBoard đang cần prefab, ta phải lấy nó.
        // Cách lấy: ta dùng con Vua hiện tại làm mồi nhử để gọi Instantiate, nhưng tốt nhất nên refactor hàm SpawnPiece ở bước sau, tạm thời ta sẽ dùng prefab của chính con Vua.

        chessBoard.SpawnPiece(targetRecord.pieceData, selectedPiece != null ? selectedPiece : FindRogueKingPiece(), targetRecord.deathPos, targetRecord.faction);

        // Xóa khỏi nghĩa trang
        graveyard.RemoveAt(recordIndex);

        kingData.hasUsedRevive = true;
        hasMovedThisTurn = true;
        Debug.Log($"[Skill] Vua đã HỒI SINH {targetRecord.pieceData.pieceName} tại {targetRecord.deathPos}!");

        gameManager.ActionCompleted(true);
    }
    private void ActivateBishopSilence(ChessPieceRuntime data)
    {
        ChessPieceRuntime enemyKing = FindEnemyKing(data.faction);
        if (enemyKing == null)
        {
            Debug.LogWarning("[Skill] Không tìm thấy Vua địch trên bàn cờ!");
            return;
        }

        // Áp dụng hiệu ứng
        enemyKing.silencedTurnsLeft = 1; // Cấm Vua địch 1 lượt
        data.currentSkillCooldown = 3;   // Tượng phải đợi 3 lượt mới được xài lại

        hasMovedThisTurn = true;
        Debug.Log($"[Skill] Tượng tại {data.currentGridPosition} tung Tia Phán Xét! Vua địch đã bị CẤM SKILL.");

        // Tùy chọn: Gọi hiệu ứng VFX laze ở đây

        gameManager.ActionCompleted(true); // Kết thúc lượt
    }

    private ChessPieceRuntime FindEnemyKing(ChessFaction myFaction)
    {
        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                var piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                // Tìm quân cờ TRÁI PHE và có tên chứa chữ "King" (Bạn nhớ đặt tên file ChessPieceData của Vua có chữ King nhé)
                if (piece != null && piece.faction != myFaction && piece.baseData.pieceName.Contains("King"))
                {
                    return piece;
                }
            }
        }
        return null;
    }
    private void ActivatePawnShield(ChessPieceRuntime data)
    {
        data.hasShield = true;
        gameManager.hasUsedPawnShieldThisTurn = true;
        hasMovedThisTurn = true;

        Debug.Log($"[Skill] Tốt tại {data.currentGridPosition} đã bật khiên!");

        // Tùy chọn: Thêm VFX bật khiên ở đây
        // ...

        gameManager.ActionCompleted(true);
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
            case PlayerState.AimingSkill:
                if (currentSpecialSkillTiles.Contains(cellPos))
                {
                    currentState = PlayerState.Animating;
                    ExecuteKingSweepMove(cellPos);
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
            ReturnPieceToOriginalPosition();
        }
    }

    private void ReturnPieceToOriginalPosition()
    {
        if (selectedPiece == null) return;

        ClearHighlightTiles(currentValidMoves);
        chessBoard.ResetAllTileHighlights();

        currentState = PlayerState.Animating;
        ClearHighlightTiles(currentValidMoves);
        currentValidMoves.Clear();

        Vector2Int originalPos = selectedPiece.pieceData.currentGridPosition;
        BoardTile originalTile = chessBoard.GetTileAt(originalPos);
        Vector3 originalWorldPos = originalTile.transform.position + chessBoard.PiecePlacementOffset;

        ghostPiece.transform.DOMove(originalWorldPos, ghostSnapDuration).SetEase(Ease.OutQuad).OnComplete(() =>
        {
            ghostPiece.Hide();
            selectedPiece.gameObject.SetActive(true);

            currentState = PlayerState.Idle;
            selectedPiece = null;
            Debug.Log("[PlayerControl] Piece returned to origin. Input unlocked.");
        });
    }

    private void UpdateIdleHover(BoardTile currentTile)
    {
        if (chessControl.isHoveringUI) return;

        if (lastHoveredTile != null && lastHoveredTile != currentTile)
            lastHoveredTile.ToggleSelection(false);

        if (currentTile != null)
            currentTile.ToggleSelection(true);

        lastHoveredTile = currentTile;

        if (currentTile != null && currentTile.currentPiece != null)
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
        chessBoard.ResetAllTileHighlights();

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
        ClearHighlightTiles(currentSpecialSkillTiles);

        currentValidMoves.Clear();
        currentValidAttacks.Clear();
        currentAoETiles.Clear();
        currentSpecialSkillTiles.Clear();

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

            // ---> FIX: XÓA SẠCH DATA TRONG RAM VÀ ĐƯA VÀO NGHĨA TRANG <---
            ChessPieceRuntime deadData = finishTile.currentPiece.pieceData;
            if (deadData != null)
            {
                // 1. Nhổ cỏ tận gốc trên ma trận logic
                chessBoard.boardData.RemoveEntity(deadData);

                // 2. Cất vào nghĩa trang
                if (gameManager != null && gameManager.graveyard != null)
                {
                    gameManager.graveyard.Add(new DeadPieceRecord
                    {
                        pieceData = deadData.baseData,
                        faction = deadData.faction,
                        deathPos = deadData.currentGridPosition
                    });
                }

                // 3. Nếu ăn trúng Vua đích thân bằng tay không
                if (deadData.baseData.pieceName.Contains("King") && gameManager != null)
                {
                    gameManager.OnKingDefeated();
                }
            }
            // ------------------------------------------------------------

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
                // ---> SỬA Ở ĐÂY: NẾU ĐANG CÓ BUFF BỨT TỐC THÌ KHÔNG QUA TURN
                if (hasExtraTurn)
                {
                    hasExtraTurn = false;      // Tiêu hao bùa
                    hasMovedThisTurn = false;  // Reset lại khóa di chuyển để đi tiếp
                    Debug.Log("🔥 [Bứt Tốc] Đã tiêu hao buff Bứt Tốc! Bạn không bị mất lượt, hãy chọn quân đi tiếp!");
                    // KHÔNG gọi gameManager.ActionCompleted(true);
                }
                else
                {
                    hasMovedThisTurn = true;
                    gameManager.ActionCompleted(true); // Nếu không có bùa thì hết lượt bình thường
                }
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

                // ---> THÊM ĐIỀU KIỆN CHỨA CHỮ "King" <---
                if (tile != null && tile.currentPiece != null &&
                    tile.currentPiece.faction == ChessFaction.ChessRogue &&
                    tile.currentPiece.pieceData.baseData.pieceName.Contains("King"))
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
    private void ActivateKingDash(ChessPiece piece, ChessPieceRuntime kingData)
    {
        if (kingData.currentSkillCooldown > 0) return;

        selectedPiece = piece;
        currentState = PlayerState.AimingSkill;
        currentSpecialSkillTiles.Clear();

        Vector2Int kingPos = kingData.currentGridPosition;
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        int maxDashRange = 3; 

        foreach (var d in directions)
        {
            for (int i = 1; i <= maxDashRange; i++)
            {
                Vector2Int checkPos = kingPos + d * i;
                if (!chessBoard.boardData.IsValidPosition(checkPos.x, checkPos.y)) break;

                var targetPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(checkPos.x, checkPos.y);
                if (targetPiece != null) break; 

                currentSpecialSkillTiles.Add(checkPos);
            }
        }

        ShowHighlightTiles(currentSpecialSkillTiles, TileState.ValidMove);
        if (pieceContextUI != null) pieceContextUI.Hide();
    }

    private void CheckAndMeleeAttackEnemyKing(Vector2Int kingPos)
    {
        Vector2Int[] adjacentDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var dir in adjacentDirs)
        {
            Vector2Int checkPos = kingPos + dir;
            if (!chessBoard.boardData.IsValidPosition(checkPos.x, checkPos.y)) continue;

            var target = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(checkPos.x, checkPos.y);
            // Nếu giáp mặt kẻ địch có tên chứa chữ King
            if (target != null && target.faction == ChessFaction.ChessAlliance && target.baseData.pieceName.Contains("King"))
            {
                Debug.Log($"[Passive Cận Chiến] Giáp mặt Vua địch! Vua Rogue tự động vả đòn cận chiến.");

                // Khởi tạo một hiệu ứng đấm trực tiếp 50 sát thương vào đầu Vua địch
                CombatEffect meleeDamage = new CombatEffect(EffectType.Damage, 50);

                // Trực tiếp gọi hàm dính đòn của CombatManager
                // Lưu ý: Sử dụng phương thức phản xạ để gọi ApplyEffect vì nó là private trong CombatManager gốc
                var method = combatManager.GetType().GetMethod("ApplyEffect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(combatManager, new object[] { target, meleeDamage });
                }
                break;
            }
        }
    }
    private void ActivateKingSweep(ChessPiece piece, ChessPieceRuntime kingData)
    {
        if (kingData.sweepUsesLeft <= 0) return;

        selectedPiece = piece;
        currentState = PlayerState.AimingSkill;
        currentSpecialSkillTiles.Clear();

        Vector2Int kingPos = kingData.currentGridPosition;
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var d in directions)
        {
            int i = 1;
            while (true)
            {
                Vector2Int checkPos = kingPos + d * i;

                // 1. Nếu chạm rìa bàn cờ thì dừng quét hướng này
                if (!chessBoard.boardData.IsValidPosition(checkPos.x, checkPos.y)) break;

                // 2. Kiểm tra vật cản ô tiếp theo
                var targetPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(checkPos.x, checkPos.y);

                // NẾU GẶP BẤT KỲ QUÂN CỜ NÀO (BẤT KỂ BẠN HAY ĐỊCH):
                // Hướng lướt bị chặn đứng hoàn toàn tại đây, không cho phép chọn ô này (Không thể ăn quân)
                if (targetPiece != null) break;

                // 3. Ô hoàn toàn trống trải -> Hợp lệ để chọn làm điểm đáp trốn thoát
                currentSpecialSkillTiles.Add(checkPos);
                i++;
            }
        }

        // Bôi xanh hiển thị các ô trống an toàn dọc tuyến đường
        ShowHighlightTiles(currentSpecialSkillTiles, TileState.ValidMove);
        if (pieceContextUI != null) pieceContextUI.Hide();
    }

    private void ExecuteKingSweepMove(Vector2Int targetGridPos)
    {
        ClearHighlightTiles(currentSpecialSkillTiles);
        chessBoard.ResetAllTileHighlights();

        Vector2Int originalPos = selectedPiece.pieceData.currentGridPosition;
        BoardTile targetTile = chessBoard.GetTileAt(targetGridPos);
        if (targetTile == null)
        {
            CancelTargeting();
            return;
        }

        Vector3 targetWorldPos = targetTile.transform.position + chessBoard.PiecePlacementOffset;

        // Bật diễn họa lướt nhanh
        selectedPiece.gameObject.SetActive(false);
        ghostPiece.Initialize(selectedPiece.pieceData);
        ghostPiece.transform.position = selectedPiece.transform.position;

        // Thời gian lướt 0.15s siêu tốc
        ghostPiece.transform.DOMove(targetWorldPos, 0.15f).SetEase(Ease.InQuad).OnComplete(() =>
        {
            ghostPiece.Hide();
            selectedPiece.pieceData.sweepUsesLeft--; // Trừ số lượt sử dụng

            if (gameManager != null)
            {
                gameManager.RequestSpecialMovePiece(originalPos, targetGridPos);
            }
        });
    }
}