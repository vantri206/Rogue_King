using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CardTooltipMockTester : MonoBehaviour
{
    public enum MockLayoutMode
    {
        All,
        CardOptionsOnly,
        SelectedSlotsOnly,
        PlayCardsOnly
    }

    [Header("Test Cards")]
    [Tooltip("Kéo các CardData SO thật vào đây để test mô tả popup.")]
    [SerializeField] private List<CardData> testCards = new List<CardData>();

    [Header("Optional Real Prefabs")]
    [Tooltip("Kéo Card Option Prefab của menu chọn card vào đây nếu muốn test đúng prefab thật.")]
    [SerializeField] private GameObject cardOptionPrefab;

    [Tooltip("Kéo Card Slot Prefab của menu chọn card vào đây nếu muốn test đúng prefab thật.")]
    [SerializeField] private GameObject cardSlotPrefab;

    [Tooltip("Kéo CardUI prefab ở PlayScene vào đây nếu muốn test đúng prefab thật.")]
    [SerializeField] private GameObject playCardPrefab;

    [Header("Mock Placement")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform mockRoot;
    [SerializeField] private MockLayoutMode layoutMode = MockLayoutMode.All;
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool clearOldMockOnBuild = true;

    [Header("Fallback Card Visual")]
    [SerializeField] private Vector2 fallbackCardSize = new Vector2(170f, 230f);
    [SerializeField] private Color fallbackCardBgColor = new Color(0.12f, 0.12f, 0.16f, 0.95f);
    [SerializeField] private Color fallbackTextColor = Color.white;
    [SerializeField, Min(8)] private int fallbackNameFontSize = 20;
    [SerializeField, Min(8)] private int fallbackSmallFontSize = 14;

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        if (buildOnStart)
            BuildMock();
    }

    [ContextMenu("Build Tooltip Mock Cards")]
    public void BuildMock()
    {
        EnsureCanvas();
        EnsureEventSystem();

        if (clearOldMockOnBuild)
            ClearMock();

        if (testCards == null || testCards.Count == 0)
        {
            Debug.LogWarning("[CardTooltipMockTester] Chưa có CardData trong Test Cards. Kéo vài CardData SO vào để test tooltip.");
            return;
        }

        if (mockRoot == null)
            mockRoot = CreateRootPanel("CardTooltipMockRoot");

        if (layoutMode == MockLayoutMode.All || layoutMode == MockLayoutMode.CardOptionsOnly)
            CreateSection("CARD OPTIONS - Menu Available Cards", cardOptionPrefab, CreateAsOption);

        if (layoutMode == MockLayoutMode.All || layoutMode == MockLayoutMode.SelectedSlotsOnly)
            CreateSection("CARD SLOTS - Menu Selected Slots", cardSlotPrefab, CreateAsSlot);

        if (layoutMode == MockLayoutMode.All || layoutMode == MockLayoutMode.PlayCardsOnly)
            CreateSection("PLAY CARDS - In Match Hand Cards", playCardPrefab, CreateAsPlayCard);

        Debug.Log("[CardTooltipMockTester] Built mock cards. Rê chuột lên card để test CardDescriptionPopup.");
    }

    [ContextMenu("Clear Tooltip Mock Cards")]
    public void ClearMock()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedObjects[i] != null)
            {
                if (Application.isPlaying)
                    Destroy(spawnedObjects[i]);
                else
                    DestroyImmediate(spawnedObjects[i]);
            }
        }

        spawnedObjects.Clear();

        CardDescriptionPopup.HideGlobal();
    }

    private void CreateSection(string title, GameObject prefab, System.Action<GameObject, CardData, int> setupAction)
    {
        RectTransform section = CreateSectionRoot(title);

        for (int i = 0; i < testCards.Count; i++)
        {
            CardData card = testCards[i];
            GameObject cardGO = CreateCardObject(prefab, section, title, card, i);
            setupAction?.Invoke(cardGO, card, i);
        }
    }

    private GameObject CreateCardObject(GameObject prefab, RectTransform parent, string sectionTitle, CardData card, int index)
    {
        GameObject go;

        if (prefab != null)
        {
            go = Instantiate(prefab, parent);
            go.name = $"Mock_{sectionTitle}_{index}_{(card != null ? card.cardName : "NullCard")}";
            EnsureCardObjectReceivesPointer(go, card);
        }
        else
        {
            go = CreateFallbackCard(parent, card, index);
        }

        spawnedObjects.Add(go);
        return go;
    }

    private void CreateAsOption(GameObject go, CardData card, int index)
    {
        CardLoadoutOptionUI option = go.GetComponent<CardLoadoutOptionUI>();
        if (option != null)
        {
            option.Setup(null, card, index, card != null ? card.cardRole : CardRoleType.RogueKing);
        }

        AttachTooltip(go, card);
        AttachClickLog(go, card, "Option");
        FillCommonCardVisual(go, card, $"OPTION {index + 1}");
    }

    private void CreateAsSlot(GameObject go, CardData card, int index)
    {
        CardLoadoutSlotUI slot = go.GetComponent<CardLoadoutSlotUI>();
        if (slot != null)
        {
            slot.Setup(null, card != null ? card.cardRole : CardRoleType.RogueKing, index);
            slot.SetCard(card, index);
        }

        AttachTooltip(go, card);
        AttachClickLog(go, card, "Slot");
        FillCommonCardVisual(go, card, $"SLOT {index + 1}");
    }

    private void CreateAsPlayCard(GameObject go, CardData card, int index)
    {
        CardUI cardUI = go.GetComponent<CardUI>();
        if (cardUI != null)
        {
            if (cardUI.cardNameText != null)
                cardUI.cardNameText.text = card != null ? card.cardName : "Empty";

            if (cardUI.cardImage != null)
            {
                cardUI.cardImage.sprite = card != null ? card.cardArtwork : null;
                cardUI.cardImage.color = card != null && card.cardArtwork != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            }

            if (cardUI.usesText != null)
                cardUI.usesText.text = card != null ? $"Uses: {card.maxUses}/{card.maxUses}" : "Uses: 0/0";

            if (cardUI.cooldownText != null)
                cardUI.cooldownText.text = "";

            if (cardUI.cooldownOverlay != null)
                cardUI.cooldownOverlay.gameObject.SetActive(false);

            if (cardUI.cardButton != null)
            {
                cardUI.cardButton.interactable = true;
                cardUI.cardButton.onClick.RemoveAllListeners();
                cardUI.cardButton.onClick.AddListener(() => Debug.Log($"[CardTooltipMockTester] Click Play Card: {(card != null ? card.cardName : "NullCard")}"));
            }
        }

        AttachTooltip(go, card);
        AttachClickLog(go, card, "PlayCard");
        FillCommonCardVisual(go, card, $"PLAY {index + 1}");
    }

    private GameObject CreateFallbackCard(RectTransform parent, CardData card, int index)
    {
        GameObject root = new GameObject($"Mock_FallbackCard_{index}_{(card != null ? card.cardName : "NullCard")}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = fallbackCardSize;

        Image bg = root.GetComponent<Image>();
        bg.color = fallbackCardBgColor;
        bg.raycastTarget = true;

        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => Debug.Log($"[CardTooltipMockTester] Click Fallback Card: {(card != null ? card.cardName : "NullCard")}"));

        GameObject artGO = new GameObject("Artwork", typeof(RectTransform), typeof(Image));
        artGO.transform.SetParent(root.transform, false);
        RectTransform artRect = artGO.GetComponent<RectTransform>();
        artRect.anchorMin = new Vector2(0.12f, 0.34f);
        artRect.anchorMax = new Vector2(0.88f, 0.88f);
        artRect.offsetMin = Vector2.zero;
        artRect.offsetMax = Vector2.zero;

        Image art = artGO.GetComponent<Image>();
        art.sprite = card != null ? card.cardArtwork : null;
        art.color = card != null && card.cardArtwork != null ? Color.white : new Color(1f, 1f, 1f, 0.18f);
        art.raycastTarget = false;
        art.preserveAspect = true;

        GameObject nameGO = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(root.transform, false);
        RectTransform nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.08f, 0.14f);
        nameRect.anchorMax = new Vector2(0.92f, 0.32f);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        TextMeshProUGUI nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = card != null ? card.cardName : "Empty";
        nameText.color = fallbackTextColor;
        nameText.fontSize = fallbackNameFontSize;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = true;
        nameText.raycastTarget = false;

        GameObject roleGO = new GameObject("RoleText", typeof(RectTransform), typeof(TextMeshProUGUI));
        roleGO.transform.SetParent(root.transform, false);
        RectTransform roleRect = roleGO.GetComponent<RectTransform>();
        roleRect.anchorMin = new Vector2(0.08f, 0.03f);
        roleRect.anchorMax = new Vector2(0.92f, 0.13f);
        roleRect.offsetMin = Vector2.zero;
        roleRect.offsetMax = Vector2.zero;

        TextMeshProUGUI roleText = roleGO.GetComponent<TextMeshProUGUI>();
        roleText.text = card != null ? card.cardRole.ToString() : "No Card";
        roleText.color = new Color(1f, 1f, 1f, 0.72f);
        roleText.fontSize = fallbackSmallFontSize;
        roleText.alignment = TextAlignmentOptions.Center;
        roleText.raycastTarget = false;

        AttachTooltip(root, card);
        return root;
    }

    private void FillCommonCardVisual(GameObject go, CardData card, string fallbackLabel)
    {
        if (go == null)
            return;

        TextMeshProUGUI[] texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text == null)
                continue;

            string lower = text.gameObject.name.ToLowerInvariant();
            if (lower.Contains("name") || lower.Contains("title") || text.text == "Empty")
            {
                text.text = card != null ? card.cardName : fallbackLabel;
                break;
            }
        }

        Image[] images = go.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image == null)
                continue;

            string lower = image.gameObject.name.ToLowerInvariant();
            if (lower.Contains("art") || lower.Contains("icon") || lower.Contains("image"))
            {
                image.sprite = card != null ? card.cardArtwork : null;
                image.color = card != null && card.cardArtwork != null ? Color.white : new Color(1f, 1f, 1f, 0.18f);
                image.preserveAspect = true;
                break;
            }
        }
    }

    private void AttachTooltip(GameObject go, CardData card)
    {
        if (go == null)
            return;

        CardDescriptionTooltipTarget tooltip = go.GetComponent<CardDescriptionTooltipTarget>();
        if (tooltip == null)
            tooltip = go.AddComponent<CardDescriptionTooltipTarget>();

        tooltip.SetCardData(card);
    }

    private void AttachClickLog(GameObject go, CardData card, string source)
    {
        if (go == null)
            return;

        Button button = go.GetComponent<Button>();
        if (button == null)
            button = go.GetComponentInChildren<Button>(true);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Debug.Log($"[CardTooltipMockTester] Click {source}: {(card != null ? card.cardName : "NullCard")}"));
        }
    }

    private void EnsureCardObjectReceivesPointer(GameObject go, CardData card)
    {
        if (go == null)
            return;

        Graphic graphic = go.GetComponent<Graphic>();
        if (graphic == null)
            graphic = go.AddComponent<Image>();

        graphic.raycastTarget = true;

        if (graphic is Image img && img.color.a <= 0.01f)
            img.color = new Color(1f, 1f, 1f, 0.02f);

        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null)
            group = go.AddComponent<CanvasGroup>();

        group.interactable = true;
        group.blocksRaycasts = true;

        AttachTooltip(go, card);
    }

    private RectTransform CreateRootPanel(string rootName)
    {
        GameObject root = new GameObject(rootName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        root.transform.SetParent(targetCanvas.transform, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.02f, 0.06f);
        rect.anchorMax = new Vector2(0.98f, 0.94f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = root.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        spawnedObjects.Add(root);
        return rect;
    }

    private RectTransform CreateSectionRoot(string title)
    {
        GameObject section = new GameObject(title, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        section.transform.SetParent(mockRoot, false);
        spawnedObjects.Add(section);

        RectTransform sectionRect = section.GetComponent<RectTransform>();

        VerticalLayoutGroup sectionLayout = section.GetComponent<VerticalLayoutGroup>();
        sectionLayout.spacing = 8f;
        sectionLayout.childAlignment = TextAnchor.UpperLeft;
        sectionLayout.childControlWidth = true;
        sectionLayout.childControlHeight = true;
        sectionLayout.childForceExpandWidth = false;
        sectionLayout.childForceExpandHeight = false;

        ContentSizeFitter sectionFitter = section.GetComponent<ContentSizeFitter>();
        sectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject labelGO = new GameObject("SectionLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(section.transform, false);
        TextMeshProUGUI label = labelGO.GetComponent<TextMeshProUGUI>();
        label.text = title;
        label.fontSize = 24f;
        label.color = Color.yellow;
        label.raycastTarget = false;

        GameObject row = new GameObject("CardsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        row.transform.SetParent(section.transform, false);

        RectTransform rowRect = row.GetComponent<RectTransform>();
        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.UpperLeft;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        ContentSizeFitter rowFitter = row.GetComponent<ContentSizeFitter>();
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rowRect;
    }

    private void EnsureCanvas()
    {
        if (targetCanvas != null)
            return;

        targetCanvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

        if (targetCanvas != null)
            return;

        GameObject canvasGO = new GameObject("CardTooltipMockCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        targetCanvas = canvasGO.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
    }

    private void EnsureEventSystem()
    {
        EventSystem existing = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        GameObject eventSystemGO = new GameObject("EventSystem", typeof(EventSystem));

#if ENABLE_INPUT_SYSTEM
        eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        eventSystemGO.AddComponent<StandaloneInputModule>();
#endif
    }
}
