using TMPro;
using UnityEngine;

/// <summary>
/// Client-side helper that spawns red floating "-x" text when a network chess piece loses HP.
/// Add one to PlayScene for custom settings, or rely on the fallback auto text.
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    public static DamagePopupManager Instance { get; private set; }

    [Header("Popup Prefab")]
    [Tooltip("Optional prefab with DamagePopupUI. If null, the manager creates a runtime TextMeshPro popup automatically.")]
    [SerializeField] private DamagePopupUI popupPrefab;

    [Header("Position")]
    [SerializeField] private Vector3 popupWorldOffset = new Vector3(0f, 1.25f, 0f);

    [Header("Fallback Text")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float fallbackFontSize = 3.2f;
    [SerializeField] private int fallbackSortingOrder = 5000;
    [SerializeField] private TextAlignmentOptions fallbackAlignment = TextAlignmentOptions.Center;

    [Header("Animation")]
    [SerializeField] private float popupLifetime = 0.85f;
    [SerializeField] private Vector3 popupRiseOffset = new Vector3(0f, 0.75f, 0f);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void ShowDamagePopup(Vector2Int gridPos, int damageAmount, ChessBoard board)
    {
        if (damageAmount <= 0 || Application.isBatchMode)
            return;

        ChessBoard resolvedBoard = board != null ? board : FindFirstObjectByType<ChessBoard>();
        if (resolvedBoard == null)
        {
            Debug.LogWarning($"[DamagePopup] Missing ChessBoard. Cannot show damage popup at {gridPos}.");
            return;
        }

        BoardTile tile = resolvedBoard.GetTileAt(gridPos);
        if (tile == null)
        {
            Debug.LogWarning($"[DamagePopup] Missing BoardTile at {gridPos}. Cannot show damage popup.");
            return;
        }

        DamagePopupManager manager = Instance != null ? Instance : FindFirstObjectByType<DamagePopupManager>();
        if (manager != null)
        {
            manager.SpawnPopup(tile.transform.position + resolvedBoard.PiecePlacementOffset, damageAmount);
            return;
        }

        SpawnFallbackPopup(tile.transform.position + resolvedBoard.PiecePlacementOffset, damageAmount);
    }

    private void SpawnPopup(Vector3 baseWorldPos, int damageAmount)
    {
        Vector3 spawnPos = baseWorldPos + popupWorldOffset;

        DamagePopupUI popup = null;
        if (popupPrefab != null)
        {
            popup = Instantiate(popupPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            popup = CreateFallbackPopup(spawnPos, damageColor, fallbackFontSize, fallbackSortingOrder, fallbackAlignment);
        }

        if (popup != null)
            popup.Play(damageAmount, damageColor, popupLifetime, popupRiseOffset);
    }

    private static void SpawnFallbackPopup(Vector3 baseWorldPos, int damageAmount)
    {
        DamagePopupUI popup = CreateFallbackPopup(
            baseWorldPos + new Vector3(0f, 1.25f, 0f),
            Color.red,
            3.2f,
            5000,
            TextAlignmentOptions.Center
        );

        if (popup != null)
            popup.Play(damageAmount, Color.red, 0.85f, new Vector3(0f, 0.75f, 0f));
    }

    private static DamagePopupUI CreateFallbackPopup(Vector3 worldPos, Color color, float fontSize, int sortingOrder, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject("DamagePopup_RuntimeText");
        go.transform.position = worldPos;

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = "-0";
        text.color = color;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.sortingOrder = sortingOrder;

        // Keep popup readable in a 2D orthographic board. Users can replace this with a prefab later.
        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(3f, 1f);

        DamagePopupUI popup = go.AddComponent<DamagePopupUI>();
        popup.BindRuntimeText(text);
        return popup;
    }
}
