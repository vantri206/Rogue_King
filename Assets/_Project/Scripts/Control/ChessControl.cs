using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class ChessControl : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;

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
        if (mainCamera == null) mainCamera = Camera.main;
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
        isHoveringUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        UpdateHoverData();
    }

    private void OnPointerPositionChanged(InputAction.CallbackContext context)
    {
        pointerPosition = context.ReadValue<Vector2>();
    }

    private void UpdateHoverData()
    {
        if (isHoveringUI)
        {
            hoveredTile = null;
            hoveredCell = new Vector2Int(-1, -1);
            return;
        }

        mouseWorldPosition = mainCamera.ScreenToWorldPoint(pointerPosition);
        Vector3 rayPos = mouseWorldPosition;
        rayPos.z = 0;

        RaycastHit2D hit = Physics2D.Raycast(rayPos, Vector2.zero);
        if (hit.collider != null)
        {
            hoveredTile = hit.collider.GetComponent<BoardTile>();
            if (hoveredTile != null)
            {
                hoveredCell = new Vector2Int(hoveredTile.boardX, hoveredTile.boardY);
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