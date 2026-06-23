using Fusion;
using UnityEngine;
using DG.Tweening;

public class NetworkChessPiece : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnPositionChanged))]
    public Vector2Int currentGridPos { get; set; }

    [Networked, OnChangedRender(nameof(OnHpChanged))]
    public int currentHp { get; set; }

    [Networked, OnChangedRender(nameof(OnVisualDataChanged))]
    public int pieceDataIndex { get; set; }

    [Networked, OnChangedRender(nameof(OnVisualDataChanged))]
    public ChessFaction faction { get; set; }

    [Networked] public int currentSkillCooldown { get; set; }
    [Networked] public int silencedTurnsLeft { get; set; }
    [Networked] public NetworkBool isKing { get; set; }

    [Header("Visual References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator animator;

    [Header("Visual Settings")]
    [SerializeField] private float moveDuration = 0.3f;
    [SerializeField] private Vector3 fallbackPiecePlacementOffset = new Vector3(0f, 0.4f, 0f);

    private ChessPieceData cachedPieceData;
    private Renderer[] cachedRenderers;

    public ChessPieceData PieceData => cachedPieceData != null ? cachedPieceData : ResolvePieceData();

    public override void Spawned()
    {
        CacheVisualReferences();
        ApplyVisualsFromPieceData();
        SetWorldPositionImmediate();
    }

    private void Awake()
    {
        CacheVisualReferences();
    }

    private void Update()
    {
        if (cachedPieceData == null && pieceDataIndex >= 0)
        {
            ApplyVisualsFromPieceData();
        }
    }

    public void InitializeFromServerSpawn(int dataIndex, InitialPieceSetup setup)
    {
        pieceDataIndex = dataIndex;
        currentGridPos = setup.startPosition;
        faction = setup.faction;
        currentHp = setup.pieceData != null ? setup.pieceData.baseHealth : 1;
        silencedTurnsLeft = 0;
        isKing = setup.pieceData != null && setup.pieceData.pieceName.Contains("King");
    }

    public void TakeDamage(int damageAmount)
    {
        if (!HasStateAuthority) return;
        if (damageAmount <= 0) return;

        currentHp -= damageAmount;

        if (currentHp < 0)
            currentHp = 0;

        if (ServerBoardManager.Instance != null)
        {
            ServerBoardManager.Instance.SyncRuntimeFromNetworkPiece(this);
        }

        if (currentHp <= 0 && ServerCombatManager.Instance != null)
        {
            ServerCombatManager.Instance.ProcessInstaKill(this);
        }
    }

    public void SetLocalVisualVisible(bool visible)
    {
        CacheVisualReferences();

        if (cachedRenderers == null) return;

        foreach (Renderer r in cachedRenderers)
        {
            if (r != null)
                r.enabled = visible;
        }
    }

    private void OnPositionChanged()
    {
        Vector3 newWorldPos = GridToWorld(currentGridPos);

        transform.DOKill();
        transform.DOMove(newWorldPos, moveDuration).SetEase(Ease.OutQuad);
    }

    private void OnHpChanged()
    {
        // Client-side UI refresh only. Server gameplay logic must not run here.
        PieceContextUI contextUI = FindFirstObjectByType<PieceContextUI>(FindObjectsInactive.Include);
        if (contextUI != null)
            contextUI.RefreshIfShowing(this);
    }

    private void OnVisualDataChanged()
    {
        ApplyVisualsFromPieceData();
    }

    private void CacheVisualReferences()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void ApplyVisualsFromPieceData()
    {
        CacheVisualReferences();

        cachedPieceData = ResolvePieceData();

        if (cachedPieceData == null)
        {
            gameObject.name = $"NetworkPiece_{pieceDataIndex}_{faction}";
            return;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = cachedPieceData.pieceSprite;
        }

        if (animator != null)
        {
            animator.runtimeAnimatorController = cachedPieceData.pieceAnimator;
        }

        gameObject.name = $"{faction}_{cachedPieceData.pieceName}_Network";
    }

    private ChessPieceData ResolvePieceData()
    {
        if (ServerBoardManager.Instance != null)
        {
            ChessPieceData dataFromServerBoard = ServerBoardManager.Instance.GetPieceDataByIndex(pieceDataIndex);
            if (dataFromServerBoard != null)
                return dataFromServerBoard;
        }

        ChessBoard visualBoard = FindFirstObjectByType<ChessBoard>();
        if (visualBoard != null && visualBoard.CurrentLevelData != null)
        {
            LevelData levelData = visualBoard.CurrentLevelData;
            if (levelData.initialPieces != null && pieceDataIndex >= 0 && pieceDataIndex < levelData.initialPieces.Count)
            {
                return levelData.initialPieces[pieceDataIndex].pieceData;
            }
        }

        return null;
    }

    private void SetWorldPositionImmediate()
    {
        transform.DOKill();
        transform.position = GridToWorld(currentGridPos);
    }

    private Vector3 GridToWorld(Vector2Int gridPos)
    {
        if (ServerBoardManager.Instance != null)
        {
            return ServerBoardManager.Instance.GridToWorld(gridPos);
        }

        ChessBoard visualBoard = FindFirstObjectByType<ChessBoard>();
        if (visualBoard != null)
        {
            return visualBoard.GetPieceWorldPosition(gridPos);
        }

        return new Vector3(gridPos.x, -gridPos.y, 0f) + fallbackPiecePlacementOffset;
    }
}
