using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MenuScene leaderboard tab.
/// Rows are spawned only from Row Prefab into Row Parent. No static row list is used.
/// Live data comes from the lobby session through ClientLeaderboardCache snapshots.
/// </summary>
public class LeaderboardMenuUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private AvatarCatalog avatarCatalog;
    [SerializeField] private NetworkRunnerHandler runnerHandler;

    [Header("Rows - prefab only")]
    [Tooltip("The ScrollView Content / Row Container. Runtime rows will be instantiated as direct children of this transform.")]
    [SerializeField] private RectTransform rowParent;

    [Tooltip("Prefab asset that has LeaderboardRowUI on its root.")]
    [SerializeField] private LeaderboardRowUI rowPrefab;

    [SerializeField] private int maxRows = 10;

    [Tooltip("Optional. If Row Parent is empty, this ScrollRect.content is used as Row Parent.")]
    [SerializeField] private ScrollRect scrollRectFallback;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button refreshButton;

    [Header("Live")]
    [SerializeField] private bool requestLiveRefreshOnEnable = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;
    [SerializeField] private bool forceRebuildRowsOnAwake = true;

    private readonly List<LeaderboardRowUI> runtimeRows = new List<LeaderboardRowUI>();
    private bool rowsBuilt;

    private void Awake()
    {
        ResolveRunnerHandler();
        ResolveRowParentFallback();

        if (refreshButton != null)
            refreshButton.onClick.AddListener(RequestLiveRefreshOrReloadCache);

        if (forceRebuildRowsOnAwake)
            RebuildRuntimeRows();
        else
            BuildRowsIfNeeded();
    }

    private void OnEnable()
    {
        ResolveRunnerHandler();
        ResolveRowParentFallback();

        ClientLeaderboardCache.Changed += RefreshFromCache;
        ClientLeaderboardCache.LoadFromPlayerPrefs();
        RefreshFromCache();

        if (requestLiveRefreshOnEnable)
            RequestLiveRefreshOrReloadCache();
    }

    private void OnDisable()
    {
        ClientLeaderboardCache.Changed -= RefreshFromCache;
    }

    private void OnDestroy()
    {
        if (refreshButton != null)
            refreshButton.onClick.RemoveListener(RequestLiveRefreshOrReloadCache);
    }

    [ContextMenu("Rebuild Runtime Leaderboard Rows")]
    public void RebuildRuntimeRows()
    {
        ResolveRowParentFallback();

        for (int i = runtimeRows.Count - 1; i >= 0; i--)
        {
            LeaderboardRowUI row = runtimeRows[i];
            if (row == null)
                continue;

            if (Application.isPlaying)
                Destroy(row.gameObject);
            else
                DestroyImmediate(row.gameObject);
        }

        runtimeRows.Clear();
        rowsBuilt = false;
        BuildRowsIfNeeded();
    }

    public void RequestLiveRefreshOrReloadCache()
    {
        ResolveRunnerHandler();

        if (runnerHandler != null && runnerHandler.IsClientConnectedToLobby)
        {
            bool requested = runnerHandler.ClientRequestLeaderboardRefresh();
            SetStatus(requested ? "Refreshing live leaderboard..." : "Waiting for lobby connection/player object...");
            return;
        }

        ClientLeaderboardCache.LoadFromPlayerPrefs();
        RefreshFromCache();
        SetStatus(ClientLeaderboardCache.Count > 0
            ? $"Showing cached leaderboard: {ClientLeaderboardCache.Count} player(s). Connect to lobby for live data."
            : "No leaderboard yet. Start/connect the lobby server to receive live data.");
    }

    public void RefreshFromCache()
    {
        BuildRowsIfNeeded();

        List<LeaderboardEntryData> entries = ClientLeaderboardCache.GetSortedEntries(maxRows);
        if (runtimeRows.Count <= 0)
        {
            SetStatus(BuildMissingRowsMessage(entries.Count));
            return;
        }

        for (int i = 0; i < runtimeRows.Count; i++)
        {
            LeaderboardRowUI row = runtimeRows[i];
            if (row == null)
                continue;

            row.gameObject.SetActive(true);
            row.transform.SetSiblingIndex(i);

            if (i < entries.Count)
                row.SetData(i + 1, entries[i], avatarCatalog);
            else
                row.SetEmpty();
        }

        ForceLayoutRefresh();

        SetStatus(entries.Count > 0
            ? $"Leaderboard live/cache: {entries.Count} player(s). Rows spawned: {runtimeRows.Count}. Parent: {(rowParent != null ? rowParent.name : "NULL")}."
            : $"Leaderboard is empty. Rows spawned: {runtimeRows.Count}. First lobby player will appear after profile submit.");
    }

    private void BuildRowsIfNeeded()
    {
        runtimeRows.RemoveAll(row => row == null);
        ResolveRowParentFallback();

        if (rowsBuilt && runtimeRows.Count >= Mathf.Max(1, maxRows))
            return;

        if (rowPrefab == null || rowParent == null)
        {
            if (verboseLogs)
                Debug.LogWarning($"[LeaderboardMenuUI] Cannot build rows. RowPrefab={(rowPrefab != null ? rowPrefab.name : "NULL")}, RowParent={(rowParent != null ? rowParent.name : "NULL")}");
            return;
        }

        int targetCount = Mathf.Max(1, maxRows);
        while (runtimeRows.Count < targetCount)
        {
            LeaderboardRowUI row = Instantiate(rowPrefab);
            RectTransform rowRect = row.transform as RectTransform;
            if (rowRect != null)
            {
                rowRect.SetParent(rowParent, false);
                rowRect.localScale = Vector3.one;
                rowRect.localRotation = Quaternion.identity;
                rowRect.anchoredPosition3D = Vector3.zero;
            }
            else
            {
                row.transform.SetParent(rowParent, false);
                row.transform.localScale = Vector3.one;
                row.transform.localRotation = Quaternion.identity;
                row.transform.localPosition = Vector3.zero;
            }

            row.gameObject.name = $"{rowPrefab.gameObject.name}_Runtime_{runtimeRows.Count + 1:00}";
            row.gameObject.SetActive(true);
            row.transform.SetAsLastSibling();
            row.SetEmpty();
            runtimeRows.Add(row);

            if (verboseLogs)
                Debug.Log($"[LeaderboardMenuUI] Spawned row {runtimeRows.Count}/{targetCount}: {GetTransformPath(row.transform)} under {GetTransformPath(rowParent)}");
        }

        rowsBuilt = runtimeRows.Count >= targetCount;
        ForceLayoutRefresh();

        if (verboseLogs)
            Debug.Log($"[LeaderboardMenuUI] Runtime prefab rows ready. Count={runtimeRows.Count}, Parent={rowParent.name}, Prefab={rowPrefab.name}");
    }

    private void ResolveRowParentFallback()
    {
        if (rowParent != null)
            return;

        if (scrollRectFallback == null)
            scrollRectFallback = GetComponentInChildren<ScrollRect>(true);

        if (scrollRectFallback != null && scrollRectFallback.content != null)
        {
            rowParent = scrollRectFallback.content;
            if (verboseLogs)
                Debug.Log($"[LeaderboardMenuUI] Row Parent auto-resolved from ScrollRect.content: {GetTransformPath(rowParent)}");
        }
    }

    private void ForceLayoutRefresh()
    {
        if (rowParent == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowParent);

        RectTransform parent = rowParent.parent as RectTransform;
        if (parent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    private void ResolveRunnerHandler()
    {
        if (runnerHandler != null)
            return;

        if (NetworkRunnerHandler.Active != null)
        {
            runnerHandler = NetworkRunnerHandler.Active;
            return;
        }

        runnerHandler = FindFirstObjectByType<NetworkRunnerHandler>();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        if (verboseLogs)
            Debug.Log($"[LeaderboardMenuUI] {message}");
    }

    private string BuildMissingRowsMessage(int cachedCount)
    {
        if (rowPrefab == null && rowParent == null)
            return $"Leaderboard has {cachedCount} player(s), but Row Prefab and Row Parent are not assigned.";

        if (rowPrefab == null)
            return $"Leaderboard has {cachedCount} player(s), but Row Prefab is not assigned.";

        if (rowParent == null)
            return $"Leaderboard has {cachedCount} player(s), but Row Parent is not assigned.";

        return $"Leaderboard has {cachedCount} player(s), but no runtime row UI is available.";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "NULL";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
