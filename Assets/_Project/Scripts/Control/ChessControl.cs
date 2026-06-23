using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class ChessControl : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private ChessBoard chessBoard;

    private InputSystem_Actions inputActions;
    private Vector2 pointerPosition;

    public Vector2Int hoveredCell { get; private set; } = new Vector2Int(-1, -1);
    public BoardTile hoveredTile { get; private set; }
    public Vector3 mouseWorldPosition { get; private set; }
    public bool isHoveringUI { get; private set; }

    public Action<BoardTile, Vector2Int> onPointerDown;
    public Action<BoardTile, Vector2Int> onPointerUp;
    public Action onCancelTriggered;

    private void Awake()
    {
        inputActions = new InputSystem_Actions();
        ResolveReferences();
    }

    private void OnEnable()
    {
        inputActions.Enable();
        inputActions.Gameplay.PointerPosition.performed += OnPointerPositionChanged;

        inputActions.Gameplay.PointerClick.started += OnPointerDownStarted;
        inputActions.Gameplay.PointerClick.canceled += OnPointerUpCanceled;

        inputActions.Gameplay.Cancel.performed += OnCancelInput;
    }

    private void OnDisable()
    {
        inputActions.Gameplay.PointerPosition.performed -= OnPointerPositionChanged;
        inputActions.Gameplay.PointerClick.started -= OnPointerDownStarted;
        inputActions.Gameplay.PointerClick.canceled -= OnPointerUpCanceled;
        inputActions.Gameplay.Cancel.performed -= OnCancelInput;
        inputActions.Disable();
    }

    private void Update()
    {
        ResolveReferences();

        isHoveringUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        UpdateHoverData();
    }

    private void ResolveReferences()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (chessBoard == null) chessBoard = FindFirstObjectByType<ChessBoard>();
    }

    private void OnPointerPositionChanged(InputAction.CallbackContext context)
    {
        pointerPosition = context.ReadValue<Vector2>();
    }

    private void UpdateHoverData()
    {
        if (isHoveringUI || mainCamera == null)
        {
            hoveredTile = null;
            hoveredCell = new Vector2Int(-1, -1);
            return;
        }

        mouseWorldPosition = mainCamera.ScreenToWorldPoint(pointerPosition);
        Vector3 rayPos = mouseWorldPosition;
        rayPos.z = 0f;

        // NetworkChessPiece prefabs may have their own Collider2D and can sit above the tile.
        // RaycastAll lets us skip piece colliders and still find the BoardTile below.
        Collider2D[] hits = Physics2D.OverlapPointAll(rayPos);
        foreach (Collider2D hit in hits)
        {
            if (hit == null) continue;

            BoardTile tile = hit.GetComponent<BoardTile>();
            if (tile == null)
                tile = hit.GetComponentInParent<BoardTile>();

            if (tile != null)
            {
                hoveredTile = tile;
                hoveredCell = new Vector2Int(tile.boardX, tile.boardY);
                return;
            }
        }

        // Fallback for cases where piece colliders block raycasts or tiles have no collider.
        if (chessBoard != null)
        {
            Vector2Int fallbackCell = chessBoard.WorldToGrid(rayPos);
            BoardTile fallbackTile = chessBoard.GetTileAt(fallbackCell);
            if (fallbackTile != null)
            {
                hoveredTile = fallbackTile;
                hoveredCell = fallbackCell;
                return;
            }
        }

        hoveredTile = null;
        hoveredCell = new Vector2Int(-1, -1);
    }

    private void OnPointerDownStarted(InputAction.CallbackContext context)
    {
        if (isHoveringUI) return;
        onPointerDown?.Invoke(hoveredTile, hoveredCell);
    }

    private void OnPointerUpCanceled(InputAction.CallbackContext context)
    {
        onPointerUp?.Invoke(hoveredTile, hoveredCell);
    }

    private void OnCancelInput(InputAction.CallbackContext context)
    {
        onCancelTriggered?.Invoke();
    }
}
