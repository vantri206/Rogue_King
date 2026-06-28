using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardDescriptionPopup : MonoBehaviour
{
    public static CardDescriptionPopup Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform rootPanel;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Auto Create Settings")]
    [SerializeField] private Vector2 panelSize = new Vector2(460f, 220f);
    [SerializeField] private Vector2 screenOffset = new Vector2(24f, -24f);
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.86f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField, Min(8)] private int fontSize = 24;

    private bool autoCreated;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildIfNeeded();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Show(CardData data, Vector2 screenPosition)
    {
        if (data == null)
            return;

        CardDescriptionPopup popup = GetOrCreate();
        if (popup == null)
            return;

        popup.ShowInternal(data, screenPosition);
    }

    public static void Move(Vector2 screenPosition)
    {
        if (Instance == null)
            return;

        Instance.UpdatePosition(screenPosition);
    }

    public static void HideGlobal()
    {
        if (Instance != null)
            Instance.Hide();
    }

    private static CardDescriptionPopup GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        CardDescriptionPopup existing = FindFirstObjectByType<CardDescriptionPopup>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Instance = existing;
            existing.BuildIfNeeded();
            return existing;
        }

        GameObject go = new GameObject("CardDescriptionPopup_Auto");
        CardDescriptionPopup popup = go.AddComponent<CardDescriptionPopup>();
        popup.autoCreated = true;
        return popup;
    }

    private void BuildIfNeeded()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
                targetCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        }

        if (targetCanvas == null)
        {
            GameObject canvasGO = new GameObject("CardTooltipCanvas_Auto");
            targetCanvas = canvasGO.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        transform.SetParent(targetCanvas.transform, false);

        if (rootPanel == null)
        {
            GameObject panelGO = new GameObject("CardDescriptionPanel", typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(targetCanvas.transform, false);
            rootPanel = panelGO.GetComponent<RectTransform>();
            rootPanel.sizeDelta = panelSize;
            rootPanel.pivot = new Vector2(0f, 1f);
            backgroundImage = panelGO.GetComponent<Image>();
            autoCreated = true;
        }

        if (backgroundImage == null)
            backgroundImage = rootPanel.GetComponent<Image>();

        if (backgroundImage != null)
            backgroundImage.color = backgroundColor;

        if (descriptionText == null)
        {
            GameObject textGO = new GameObject("DescriptionText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(rootPanel, false);

            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 14f);
            textRect.offsetMax = new Vector2(-18f, -14f);

            descriptionText = textGO.GetComponent<TextMeshProUGUI>();
            autoCreated = true;
        }

        if (descriptionText != null)
        {
            descriptionText.color = textColor;
            descriptionText.fontSize = fontSize;
            descriptionText.enableWordWrapping = true;
            descriptionText.alignment = TextAlignmentOptions.TopLeft;
            descriptionText.overflowMode = TextOverflowModes.Overflow;
        }

        if (rootPanel != null)
            rootPanel.SetAsLastSibling();
    }

    private void ShowInternal(CardData data, Vector2 screenPosition)
    {
        BuildIfNeeded();

        if (descriptionText != null)
            descriptionText.text = BuildDescription(data);

        if (rootPanel != null)
        {
            rootPanel.gameObject.SetActive(true);
            rootPanel.SetAsLastSibling();
        }

        UpdatePosition(screenPosition);
    }

    private string BuildDescription(CardData data)
    {
        if (data == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(data.cardDescription))
            return data.cardDescription.Trim();

        return string.IsNullOrWhiteSpace(data.cardName)
            ? "Chưa có mô tả card."
            : $"{data.cardName}\n\nChưa có mô tả card.";
    }

    private void UpdatePosition(Vector2 screenPosition)
    {
        if (rootPanel == null)
            return;

        Vector2 pos = screenPosition + screenOffset;
        Vector2 size = rootPanel.rect.size;

        float minX = 8f;
        float maxX = Mathf.Max(minX, Screen.width - size.x - 8f);
        float minY = size.y + 8f;
        float maxY = Mathf.Max(minY, Screen.height - 8f);

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        rootPanel.position = pos;
    }

    public void Hide()
    {
        if (rootPanel != null)
            rootPanel.gameObject.SetActive(false);
    }
}
