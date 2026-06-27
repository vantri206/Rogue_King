using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PreMatchCardSelectionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private Transform dragRoot;

    [Header("Lobby / Match Info")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI roomCodeText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Available Card Lists")]
    [SerializeField] private Transform rogueKingCardContainer;
    [SerializeField] private Transform chessAllianceCardContainer;

    [Header("Selected Slots")]
    [SerializeField] private Transform rogueKingSlotContainer;
    [SerializeField] private Transform chessAllianceSlotContainer;

    [Header("Prefabs")]
    [SerializeField] private GameObject cardOptionPrefab;
    [SerializeField] private GameObject cardSlotPrefab;

    [Header("Buttons")]
    [SerializeField] private Button fightButton;
    [SerializeField] private Button cancelButton;

    [Header("Fallback Card Database")]
    [Tooltip("Nếu MenuScene client chưa thấy ServerCardManager, kéo toàn bộ CardData vào đây để UI chọn card vẫn sinh được.")]
    [SerializeField] private List<CardData> fallbackCards = new List<CardData>();

    [Header("Rules")]
    [SerializeField, Min(1)] private int maxRogueKingCards = PlayerSelectedCardLoadout.MaxRogueKingCards;
    [SerializeField, Min(1)] private int maxChessAllianceCards = PlayerSelectedCardLoadout.MaxChessAllianceCards;
    [SerializeField] private bool requireFullSelectionBeforeFight = true;

    [Header("Editor / Mock Test")]
    [Tooltip("Chỉ dùng để test trong Editor. Khi bật, nút Fight chỉ lưu deck và KHÔNG gọi NetworkRunnerHandler để join match thật.")]
    [SerializeField] private bool editorMockModeSaveOnly = false;

    private readonly List<CardLoadoutOptionUI> optionUIs = new List<CardLoadoutOptionUI>();
    private readonly List<CardLoadoutSlotUI> rogueSlots = new List<CardLoadoutSlotUI>();
    private readonly List<CardLoadoutSlotUI> chessSlots = new List<CardLoadoutSlotUI>();
    private readonly HashSet<int> selectedCardIndices = new HashSet<int>();

    private string pendingSessionName;
    private string pendingRoomCode;
    private bool fightInProgress;

    public Transform DragRoot => dragRoot;

    private void Awake()
    {
        if (rootPanel == null)
            rootPanel = gameObject;

        Hide();

        if (fightButton != null)
        {
            fightButton.onClick.RemoveListener(OnFightClicked);
            fightButton.onClick.AddListener(OnFightClicked);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(OnCancelClicked);
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
    }

    private void OnDestroy()
    {
        if (fightButton != null)
            fightButton.onClick.RemoveListener(OnFightClicked);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(OnCancelClicked);
    }

    public void ShowForMatch(string matchSessionName, string roomCode)
    {
        pendingSessionName = string.IsNullOrWhiteSpace(matchSessionName) ? "RogueKingRoom" : matchSessionName.Trim();
        pendingRoomCode = SanitizeRoomCode(roomCode);
        fightInProgress = false;

        if (rootPanel != null)
            rootPanel.SetActive(true);

        if (titleText != null)
            titleText.text = "Choose Cards";

        if (roomCodeText != null)
            roomCodeText.text = string.IsNullOrWhiteSpace(pendingRoomCode) ? "Quick Match" : $"Room ID: {pendingRoomCode}";

        BuildUI();
        RefreshStatus();
        RefreshFightButton();
    }

    public void Hide()
    {
        if (rootPanel != null)
            rootPanel.SetActive(false);
    }

    public void SetEditorMockModeSaveOnly(bool enabled)
    {
        editorMockModeSaveOnly = enabled;

        if (!enabled)
            fightInProgress = false;

        RefreshFightButton();

        Debug.Log($"[PreMatchCardSelectionUI] Editor mock save-only mode = {editorMockModeSaveOnly}");
    }

    public bool IsEditorMockModeSaveOnly()
    {
        return editorMockModeSaveOnly;
    }

    private void BuildUI()
    {
        ClearChildren(rogueKingCardContainer);
        ClearChildren(chessAllianceCardContainer);
        ClearChildren(rogueKingSlotContainer);
        ClearChildren(chessAllianceSlotContainer);
        optionUIs.Clear();
        rogueSlots.Clear();
        chessSlots.Clear();
        selectedCardIndices.Clear();

        SpawnSlots(CardRoleType.RogueKing, Mathf.Clamp(maxRogueKingCards, 1, PlayerSelectedCardLoadout.MaxRogueKingCards), rogueKingSlotContainer, rogueSlots);
        SpawnSlots(CardRoleType.ChessAlliance, Mathf.Clamp(maxChessAllianceCards, 1, PlayerSelectedCardLoadout.MaxChessAllianceCards), chessAllianceSlotContainer, chessSlots);

        IReadOnlyList<CardData> cards = ResolveCardDatabase();
        if (cards == null || cards.Count == 0)
        {
            SetStatus("Không tìm thấy CardData. Kiểm tra ServerCardManager.availableCards hoặc Fallback Cards.");
            return;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            CardData card = cards[i];
            if (card == null)
                continue;

            Transform parent = card.cardRole == CardRoleType.RogueKing ? rogueKingCardContainer : chessAllianceCardContainer;
            SpawnOption(card, i, card.cardRole, parent);
        }

        RestorePreviousSelectionIfPossible(cards);
        RefreshOptionAvailability();
    }

    private IReadOnlyList<CardData> ResolveCardDatabase()
    {
        if (ServerCardManager.Instance != null && ServerCardManager.Instance.GetAllCards() != null && ServerCardManager.Instance.CardCount > 0)
            return ServerCardManager.Instance.GetAllCards();

        return fallbackCards;
    }

    private void SpawnOption(CardData card, int cardIndex, CardRoleType role, Transform parent)
    {
        if (cardOptionPrefab == null || parent == null)
            return;

        GameObject go = Instantiate(cardOptionPrefab, parent);
        CardLoadoutOptionUI ui = go.GetComponent<CardLoadoutOptionUI>();
        if (ui == null)
            ui = go.AddComponent<CardLoadoutOptionUI>();

        ui.Setup(this, card, cardIndex, role);
        optionUIs.Add(ui);
    }

    private void SpawnSlots(CardRoleType role, int count, Transform parent, List<CardLoadoutSlotUI> targetList)
    {
        if (cardSlotPrefab == null || parent == null)
            return;

        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(cardSlotPrefab, parent);
            CardLoadoutSlotUI slot = go.GetComponent<CardLoadoutSlotUI>();
            if (slot == null)
                slot = go.AddComponent<CardLoadoutSlotUI>();

            slot.Setup(this, role, i);
            targetList.Add(slot);
        }
    }

    public bool CanPickCard(int cardIndex)
    {
        return cardIndex >= 0 && !selectedCardIndices.Contains(cardIndex);
    }

    public bool TryAddCardToFirstEmptySlot(CardRoleType role, CardData card, int cardIndex)
    {
        List<CardLoadoutSlotUI> slots = role == CardRoleType.RogueKing ? rogueSlots : chessSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].CardIndex < 0)
                return TrySetSlot(role, i, card, cardIndex);
        }

        SetStatus(role == CardRoleType.RogueKing ? "Rogue King cards đã đầy." : "Chess Alliance cards đã đầy.");
        return false;
    }

    public bool TrySetSlot(CardRoleType role, int slotIndex, CardData card, int cardIndex)
    {
        if (card == null || cardIndex < 0)
            return false;

        if (card.cardRole != role)
        {
            SetStatus("Card không đúng loại phe.");
            return false;
        }

        List<CardLoadoutSlotUI> slots = role == CardRoleType.RogueKing ? rogueSlots : chessSlots;
        if (slotIndex < 0 || slotIndex >= slots.Count || slots[slotIndex] == null)
            return false;

        if (selectedCardIndices.Contains(cardIndex) && slots[slotIndex].CardIndex != cardIndex)
        {
            SetStatus("Mỗi card chỉ được chọn 1 lần.");
            return false;
        }

        if (slots[slotIndex].CardIndex >= 0)
            selectedCardIndices.Remove(slots[slotIndex].CardIndex);

        slots[slotIndex].SetCard(card, cardIndex);
        selectedCardIndices.Add(cardIndex);

        RefreshOptionAvailability();
        RefreshStatus();
        RefreshFightButton();
        return true;
    }

    public void ClearSlot(CardRoleType role, int slotIndex)
    {
        List<CardLoadoutSlotUI> slots = role == CardRoleType.RogueKing ? rogueSlots : chessSlots;
        if (slotIndex < 0 || slotIndex >= slots.Count || slots[slotIndex] == null)
            return;

        if (slots[slotIndex].CardIndex >= 0)
            selectedCardIndices.Remove(slots[slotIndex].CardIndex);

        slots[slotIndex].Clear();
        RefreshOptionAvailability();
        RefreshStatus();
        RefreshFightButton();
    }

    private void RestorePreviousSelectionIfPossible(IReadOnlyList<CardData> cards)
    {
        PlayerSelectedCardLoadout.Load();
        RestoreArray(cards, PlayerSelectedCardLoadout.GetRogueKingCardIndices(), CardRoleType.RogueKing, rogueSlots);
        RestoreArray(cards, PlayerSelectedCardLoadout.GetChessAllianceCardIndices(), CardRoleType.ChessAlliance, chessSlots);
    }

    private void RestoreArray(IReadOnlyList<CardData> cards, int[] indices, CardRoleType role, List<CardLoadoutSlotUI> slots)
    {
        if (cards == null || indices == null || slots == null)
            return;

        int count = Mathf.Min(indices.Length, slots.Count);
        for (int i = 0; i < count; i++)
        {
            int index = indices[i];
            if (index < 0 || index >= cards.Count || cards[index] == null)
                continue;

            if (cards[index].cardRole != role || selectedCardIndices.Contains(index))
                continue;

            slots[i].SetCard(cards[index], index);
            selectedCardIndices.Add(index);
        }
    }

    private void RefreshOptionAvailability()
    {
        for (int i = 0; i < optionUIs.Count; i++)
        {
            if (optionUIs[i] != null)
                optionUIs[i].SetAvailable(!selectedCardIndices.Contains(optionUIs[i].CardIndex));
        }
    }

    private void RefreshStatus()
    {
        SetStatus($"Chọn {FilledCount(rogueSlots)}/{rogueSlots.Count} Rogue King card và {FilledCount(chessSlots)}/{chessSlots.Count} Chess Alliance card.");
    }

    private void RefreshFightButton()
    {
        bool valid = !requireFullSelectionBeforeFight || (FilledCount(rogueSlots) == rogueSlots.Count && FilledCount(chessSlots) == chessSlots.Count);
        if (fightButton != null)
            fightButton.interactable = valid && !fightInProgress;
    }

    private static int FilledCount(List<CardLoadoutSlotUI> slots)
    {
        if (slots == null)
            return 0;

        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].CardIndex >= 0)
                count++;
        }

        return count;
    }

    private void OnFightClicked()
    {
        if (fightInProgress)
        {
            SetStatus("Đang xử lý Fight, vui lòng chờ...");
            return;
        }

        if (requireFullSelectionBeforeFight && (FilledCount(rogueSlots) < rogueSlots.Count || FilledCount(chessSlots) < chessSlots.Count))
        {
            SetStatus("Chưa chọn đủ card cho cả 2 phe.");
            return;
        }

        int[] rogueIndices = BuildIndexArray(rogueSlots, PlayerSelectedCardLoadout.MaxRogueKingCards);
        int[] chessIndices = BuildIndexArray(chessSlots, PlayerSelectedCardLoadout.MaxChessAllianceCards);
        PlayerSelectedCardLoadout.Save(rogueIndices, chessIndices);

        if (editorMockModeSaveOnly)
        {
            SetStatus("MOCK: Đã lưu deck. Không join match thật.");
            Debug.Log(
                "[PreMatchCardSelectionUI] MOCK Fight clicked. Saved deck only; skipped NetworkRunnerHandler join.\n" +
                $"Rogue King Cards: {FormatArray(rogueIndices)}\n" +
                $"Chess Alliance Cards: {FormatArray(chessIndices)}"
            );
            return;
        }

        fightInProgress = true;
        RefreshFightButton();
        SetStatus("Đã lưu deck. Đang vào trận...");

        if (NetworkRunnerHandler.Active != null)
        {
            NetworkRunnerHandler.Active.ClientConfirmCardSelectionAndJoinMatch();
        }
        else
        {
            fightInProgress = false;
            RefreshFightButton();
            SetStatus("Không tìm thấy NetworkRunnerHandler.Active.");
        }
    }

    private void OnCancelClicked()
    {
        fightInProgress = false;
        PlayerSelectedCardLoadout.Clear();
        Hide();

        if (NetworkRunnerHandler.Active != null)
            NetworkRunnerHandler.Active.ClientCancelCardSelectionAndStayInLobby();
    }

    private static int[] BuildIndexArray(List<CardLoadoutSlotUI> slots, int length)
    {
        int[] values = new int[length];
        for (int i = 0; i < values.Length; i++)
            values[i] = -1;

        if (slots == null)
            return values;

        int count = Mathf.Min(values.Length, slots.Count);
        for (int i = 0; i < count; i++)
            values[i] = slots[i] != null ? slots[i].CardIndex : -1;

        return values;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
    }

    private static string FormatArray(int[] values)
    {
        if (values == null || values.Length == 0)
            return "[]";

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.Append("[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(values[i] >= 0 ? values[i].ToString() : "-");
        }

        builder.Append("]");
        return builder.ToString();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[PreMatchCardSelectionUI] {message}");
    }

    private static string SanitizeRoomCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return string.Empty;

        roomCode = roomCode.Trim();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(8);
        for (int i = 0; i < roomCode.Length; i++)
        {
            char c = roomCode[i];
            if (char.IsDigit(c))
                builder.Append(c);
        }

        string sanitized = builder.ToString();
        if (sanitized.Length > 8)
            sanitized = sanitized.Substring(0, 8);

        return sanitized;
    }
}
