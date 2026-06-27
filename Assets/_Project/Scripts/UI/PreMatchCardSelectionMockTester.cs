using System.Linq;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PreMatchCardSelectionMockTester : MonoBehaviour
{
    [Header("Target UI")]
    [SerializeField] private PreMatchCardSelectionUI selectionUI;
    [SerializeField] private bool autoFindSelectionUI = true;

    [Header("Mock Match Info")]
    [SerializeField] private string mockMatchSessionName = "RogueKingRoom";
    [SerializeField] private string mockRoomCode = "123456";

    [Header("Auto Test")]
    [Tooltip("Bật panel chọn card ngay khi Play trong Editor/scene.")]
    [SerializeField] private bool showOnStart = true;

    [Tooltip("Xóa deck đã lưu trước khi mở mock UI. Nên bật nếu muốn test từ trạng thái sạch.")]
    [SerializeField] private bool clearSavedLoadoutOnStart = false;

#if ENABLE_INPUT_SYSTEM
    [Header("Input System Hotkeys")]
    [Tooltip("Dùng Input System package, không dùng UnityEngine.Input cũ.")]
    [SerializeField] private Key openMockKey = Key.F6;

    [SerializeField] private Key mockFightKey = Key.F7;
    [SerializeField] private Key printSavedLoadoutKey = Key.F8;
    [SerializeField] private Key clearSavedLoadoutKey = Key.F9;
#else
    [Header("Hotkeys Disabled")]
    [SerializeField, Tooltip("Project không bật Input System, hotkey sẽ bị tắt. Hãy dùng mock buttons.")]
    private bool hotkeysDisabled = true;
#endif

    [Header("Optional Mock Buttons")]
    [Tooltip("Button này chỉ để test trong Editor: mở UI chọn card mock.")]
    [SerializeField] private Button openMockButton;

    [Tooltip("Button này chỉ để test trong Editor: lưu deck hiện đang chọn, không join match thật.")]
    [SerializeField] private Button mockFightButton;

    [Tooltip("Button này chỉ để test trong Editor: in deck đã lưu ra Console.")]
    [SerializeField] private Button printSavedLoadoutButton;

    [Tooltip("Button này chỉ để test trong Editor: xóa deck đã lưu.")]
    [SerializeField] private Button clearSavedLoadoutButton;

    [Header("Behavior")]
    [Tooltip("Nếu bật, khi mở mock UI thì nút Fight thật trong PreMatchCardSelectionUI cũng chỉ lưu deck, không join match thật.")]
    [SerializeField] private bool forceSelectionUIMockSaveOnly = true;

    [Tooltip("Nếu bật, sau khi mock save deck thì panel chọn card sẽ bị ẩn. Nếu tắt, panel vẫn mở để tiếp tục test.")]
    [SerializeField] private bool hidePanelAfterMockFight = false;

    private void Awake()
    {
        ResolveSelectionUI();

        if (openMockButton != null)
        {
            openMockButton.onClick.RemoveListener(OpenMockSelection);
            openMockButton.onClick.AddListener(OpenMockSelection);
        }

        if (mockFightButton != null)
        {
            mockFightButton.onClick.RemoveListener(MockFightSaveSelectedCardsOnly);
            mockFightButton.onClick.AddListener(MockFightSaveSelectedCardsOnly);
        }

        if (printSavedLoadoutButton != null)
        {
            printSavedLoadoutButton.onClick.RemoveListener(PrintSavedLoadout);
            printSavedLoadoutButton.onClick.AddListener(PrintSavedLoadout);
        }

        if (clearSavedLoadoutButton != null)
        {
            clearSavedLoadoutButton.onClick.RemoveListener(ClearSavedLoadout);
            clearSavedLoadoutButton.onClick.AddListener(ClearSavedLoadout);
        }
    }

    private void Start()
    {
        if (clearSavedLoadoutOnStart)
            ClearSavedLoadout();

        if (showOnStart)
            OpenMockSelection();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (openMockKey != Key.None && WasPressed(keyboard, openMockKey))
            OpenMockSelection();

        if (mockFightKey != Key.None && WasPressed(keyboard, mockFightKey))
            MockFightSaveSelectedCardsOnly();

        if (printSavedLoadoutKey != Key.None && WasPressed(keyboard, printSavedLoadoutKey))
            PrintSavedLoadout();

        if (clearSavedLoadoutKey != Key.None && WasPressed(keyboard, clearSavedLoadoutKey))
            ClearSavedLoadout();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool WasPressed(Keyboard keyboard, Key key)
    {
        if (keyboard == null || key == Key.None)
            return false;

        try
        {
            return keyboard[key].wasPressedThisFrame;
        }
        catch
        {
            return false;
        }
    }
#endif

    private void OnDestroy()
    {
        if (selectionUI != null)
            selectionUI.SetEditorMockModeSaveOnly(false);

        if (openMockButton != null)
            openMockButton.onClick.RemoveListener(OpenMockSelection);

        if (mockFightButton != null)
            mockFightButton.onClick.RemoveListener(MockFightSaveSelectedCardsOnly);

        if (printSavedLoadoutButton != null)
            printSavedLoadoutButton.onClick.RemoveListener(PrintSavedLoadout);

        if (clearSavedLoadoutButton != null)
            clearSavedLoadoutButton.onClick.RemoveListener(ClearSavedLoadout);
    }

    [ContextMenu("Open Mock Card Selection")]
    public void OpenMockSelection()
    {
        if (!ResolveSelectionUI())
            return;

        selectionUI.SetEditorMockModeSaveOnly(forceSelectionUIMockSaveOnly);
        selectionUI.ShowForMatch(mockMatchSessionName, mockRoomCode);

#if ENABLE_INPUT_SYSTEM
        string hint = $"Select cards, then press {mockFightKey} or call MockFightSaveSelectedCardsOnly().";
#else
        string hint = "Select cards, then use Mock Fight Button or ContextMenu MockFightSaveSelectedCardsOnly().";
#endif

        Debug.Log(
            $"[PreMatchCardSelectionMockTester] Opened mock card selection. " +
            $"Session='{mockMatchSessionName}', RoomCode='{mockRoomCode}', " +
            $"ForceSaveOnly={forceSelectionUIMockSaveOnly}. {hint}"
        );
    }

    [ContextMenu("Mock Fight - Save Selected Cards Only")]
    public void MockFightSaveSelectedCardsOnly()
    {
        if (!ResolveSelectionUI())
            return;

        CardLoadoutSlotUI[] allSlots = selectionUI.GetComponentsInChildren<CardLoadoutSlotUI>(true);
        if (allSlots == null || allSlots.Length == 0)
        {
            Debug.LogWarning(
                "[PreMatchCardSelectionMockTester] Không tìm thấy CardLoadoutSlotUI nào. " +
                "Hãy mở UI trước bằng OpenMockSelection/F6, hoặc kiểm tra Card Slot Prefab và Slot Containers."
            );
            return;
        }

        int[] rogueIndices = BuildArrayFromSlots(allSlots, CardRoleType.RogueKing, PlayerSelectedCardLoadout.MaxRogueKingCards);
        int[] chessIndices = BuildArrayFromSlots(allSlots, CardRoleType.ChessAlliance, PlayerSelectedCardLoadout.MaxChessAllianceCards);

        PlayerSelectedCardLoadout.Save(rogueIndices, chessIndices);

        Debug.Log(
            "[PreMatchCardSelectionMockTester] MOCK FIGHT: Đã lưu selected deck nhưng KHÔNG join match thật.\n" +
            $"Rogue King Cards: {FormatArray(rogueIndices)}\n" +
            $"Chess Alliance Cards: {FormatArray(chessIndices)}"
        );

        if (hidePanelAfterMockFight)
            selectionUI.Hide();
    }

    [ContextMenu("Print Saved Loadout")]
    public void PrintSavedLoadout()
    {
        PlayerSelectedCardLoadout.Load();

        int[] rogueIndices = PlayerSelectedCardLoadout.GetRogueKingCardIndices();
        int[] chessIndices = PlayerSelectedCardLoadout.GetChessAllianceCardIndices();

        Debug.Log(
            "[PreMatchCardSelectionMockTester] Saved loadout hiện tại:\n" +
            $"Rogue King Cards: {FormatArray(rogueIndices)}\n" +
            $"Chess Alliance Cards: {FormatArray(chessIndices)}"
        );
    }

    [ContextMenu("Clear Saved Loadout")]
    public void ClearSavedLoadout()
    {
        PlayerSelectedCardLoadout.Clear();
        Debug.Log("[PreMatchCardSelectionMockTester] Đã xóa selected card loadout trong PlayerPrefs/cache.");
    }

    private bool ResolveSelectionUI()
    {
        if (selectionUI != null)
            return true;

        if (autoFindSelectionUI)
            selectionUI = FindFirstObjectByType<PreMatchCardSelectionUI>(FindObjectsInactive.Include);

        if (selectionUI == null)
        {
            Debug.LogWarning(
                "[PreMatchCardSelectionMockTester] Không tìm thấy PreMatchCardSelectionUI trong scene. " +
                "Hãy tạo PreMatchCardSelectionUI_Manager active và kéo reference vào field Selection UI."
            );
            return false;
        }

        return true;
    }

    private static int[] BuildArrayFromSlots(CardLoadoutSlotUI[] allSlots, CardRoleType role, int maxLength)
    {
        int[] values = new int[Mathf.Max(1, maxLength)];
        for (int i = 0; i < values.Length; i++)
            values[i] = -1;

        if (allSlots == null)
            return values;

        CardLoadoutSlotUI[] slots = allSlots
            .Where(slot => slot != null && slot.Role == role)
            .OrderBy(slot => slot.SlotIndex)
            .ToArray();

        int count = Mathf.Min(values.Length, slots.Length);
        for (int i = 0; i < count; i++)
            values[i] = slots[i] != null ? slots[i].CardIndex : -1;

        return values;
    }

    private static string FormatArray(int[] values)
    {
        if (values == null || values.Length == 0)
            return "[]";

        return "[" + string.Join(", ", values.Select(value => value >= 0 ? value.ToString() : "-")) + "]";
    }
}
