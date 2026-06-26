using Fusion;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class ServerCombatManager : NetworkBehaviour
{
    public static ServerCombatManager Instance { get; private set; }

    [Header("Server Loadout Library")]
    [SerializeField] private List<WeaponData> availableWeapons;

    [Header("Server VFX/Damage Timing")]
    [SerializeField] private float fallbackDamageDelaySeconds = 1.0f;
    [SerializeField] private float grenadeAutoDamageDelaySeconds = 1.35f;
    [SerializeField] private float shotgunAutoDamageDelaySeconds = 0.9f;
    [SerializeField] private float maxAutoDamageDelaySeconds = 2.0f;

    [Header("Hidden Mine")]
    [SerializeField] private float hiddenMinePlaceDelaySeconds = 0.75f;
    [SerializeField] private float hiddenMineExplosionDamageDelaySeconds = 0.45f;
    [SerializeField] private Vector3 hiddenMineExplosionOffset = new Vector3(0f, 0.35f, 0f);

    private struct HiddenMineInstance
    {
        public Vector2Int GridPos;
        public PlayerRef Owner;
        public int WeaponIndex;
        public int Damage;
        public int AoERange;
    }

    private readonly List<HiddenMineInstance> hiddenMines = new List<HiddenMineInstance>();
    private bool attackResolutionInProgress;
    private Coroutine pendingAttackCoroutine;
    private Coroutine pendingMineCoroutine;

    public IReadOnlyList<WeaponData> AvailableWeapons => availableWeapons;
    public bool IsAttackResolutionInProgress => attackResolutionInProgress;

    public List<WeaponData> GetAvailableWeaponsForClientUI()
    {
        return availableWeapons;
    }

    public WeaponData GetWeaponDataByIndex(int index)
    {
        if (availableWeapons == null) return null;
        if (index < 0 || index >= availableWeapons.Count) return null;
        return availableWeapons[index];
    }

    public override void Spawned()
    {
        if (Instance == null) Instance = this;
        else Runner.Despawn(Object);
    }

    public bool IsValidAttack(PlayerRef requestingPlayer, Vector2Int targetPos, int weaponIndex)
    {
        if (!HasStateAuthority) return false;
        if (attackResolutionInProgress) return false;
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null) return false;

        if (ServerGameManager.Instance.currentGameState != NetGameState.KingTurn) return false;
        if (!ServerGameManager.Instance.IsKingPlayer(requestingPlayer)) return false;
        if (!ServerGameManager.Instance.CanPlayerAct(requestingPlayer)) return false;

        if (weaponIndex < 0 || availableWeapons == null || weaponIndex >= availableWeapons.Count) return false;

        if (TryGetPlayerController(requestingPlayer, out PlayerNetworkController controller) && controller.GetWeaponCooldown(weaponIndex) > 0)
        {
            Debug.Log($"[Server Combat] Rejected weapon {weaponIndex} from {requestingPlayer}: cooldown={controller.GetWeaponCooldown(weaponIndex)}");
            return false;
        }

        NetworkChessPiece kingPiece = FindRogueKingPiece();
        if (kingPiece == null) return false;

        WeaponData usedWeapon = availableWeapons[weaponIndex];

        List<Vector2Int> validTargets = ActionResolver.GetTargetingRange(
            usedWeapon,
            kingPiece.currentGridPos,
            ServerBoardManager.Instance.logicBoard
        );

        if (!validTargets.Contains(targetPos))
            return false;

        if (usedWeapon.specialType == WeaponSpecialType.HiddenMine)
        {
            if (usedWeapon.hiddenMineRequireEmptyTarget && ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
                return false;

            if (HasHiddenMineAt(targetPos))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Starts a server-authoritative attack.
    /// Damage/despawn is intentionally delayed until the client VFX should have completed.
    /// Returns false because this method owns resolving/end-turn timing asynchronously.
    /// </summary>
    public bool ProcessAttack(PlayerRef requestingPlayer, Vector2Int targetPos, int weaponIndex)
    {
        if (!HasStateAuthority) return false;
        if (attackResolutionInProgress) return false;
        if (weaponIndex < 0 || availableWeapons == null || weaponIndex >= availableWeapons.Count) return false;
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null) return false;
        if (ServerGameManager.Instance == null) return false;

        NetworkChessPiece kingPiece = FindRogueKingPiece();
        if (kingPiece == null) return false;

        WeaponData usedWeapon = availableWeapons[weaponIndex];

        if (usedWeapon.specialType == WeaponSpecialType.HiddenMine)
            return ProcessHiddenMinePlacement(requestingPlayer, kingPiece.currentGridPos, targetPos, usedWeapon, weaponIndex);

        Dictionary<Vector2Int, List<CombatEffect>> effectMap = ActionResolver.CalculateWeaponGrid(
            usedWeapon,
            kingPiece.currentGridPos,
            targetPos,
            ServerBoardManager.Instance.logicBoard
        );

        float damageDelay = ResolveServerDamageDelaySeconds(usedWeapon, kingPiece.currentGridPos, targetPos, effectMap);

        attackResolutionInProgress = true;
        ServerGameManager.Instance.BeginManualResolve(NetGameState.ChessTurn);

        Rpc_PlayCombatVFX(kingPiece.currentGridPos, targetPos, weaponIndex);

        pendingAttackCoroutine = StartCoroutine(ApplyAttackAfterVFXRoutine(effectMap, usedWeapon, weaponIndex, damageDelay));

        Debug.Log($"[Server Combat] Queued attack {usedWeapon.weaponName} at {targetPos}. Damage will apply after {damageDelay:0.00}s so VFX can finish first.");

        return false;
    }

    private bool ProcessHiddenMinePlacement(PlayerRef owner, Vector2Int startPos, Vector2Int targetPos, WeaponData usedWeapon, int weaponIndex)
    {
        if (usedWeapon.hiddenMineRequireEmptyTarget && ServerBoardManager.Instance.GetPieceAt(targetPos) != null)
            return false;

        if (HasHiddenMineAt(targetPos))
            return false;

        float placeDelay = usedWeapon.serverDamageDelaySeconds > 0f
            ? usedWeapon.serverDamageDelaySeconds
            : Mathf.Max(0.05f, hiddenMinePlaceDelaySeconds + Vector2Int.Distance(startPos, targetPos) * 0.05f);

        attackResolutionInProgress = true;
        ServerGameManager.Instance.BeginManualResolve(NetGameState.ChessTurn);

        Rpc_PlayCombatVFX(startPos, targetPos, weaponIndex);
        pendingMineCoroutine = StartCoroutine(PlaceHiddenMineAfterVFXRoutine(owner, targetPos, usedWeapon, weaponIndex, placeDelay));

        Debug.Log($"[Server Mine] Queued hidden mine placement by {owner} at {targetPos}. Mine will arm after {placeDelay:0.00}s.");
        return false;
    }

    private IEnumerator PlaceHiddenMineAfterVFXRoutine(PlayerRef owner, Vector2Int targetPos, WeaponData usedWeapon, int weaponIndex, float delaySeconds)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        pendingMineCoroutine = null;

        if (!HasStateAuthority || ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        if (ServerGameManager.Instance.currentGameState == NetGameState.GameOver ||
            ServerGameManager.Instance.currentGameState == NetGameState.Init)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        if (!HasHiddenMineAt(targetPos))
        {
            hiddenMines.Add(new HiddenMineInstance
            {
                GridPos = targetPos,
                Owner = owner,
                WeaponIndex = weaponIndex,
                Damage = ResolveHiddenMineDamage(usedWeapon),
                AoERange = Mathf.Max(0, usedWeapon.hiddenMineAOERange)
            });

            Debug.Log($"[Server Mine] Hidden mine armed at {targetPos}. TotalMines={hiddenMines.Count}");
        }

        attackResolutionInProgress = false;

        if (ServerGameManager.Instance.currentGameState == NetGameState.ResolvingAction)
            ServerGameManager.Instance.CompleteManualResolve();
    }

    private IEnumerator ApplyAttackAfterVFXRoutine(Dictionary<Vector2Int, List<CombatEffect>> effectMap, WeaponData usedWeapon, int weaponIndex, float delaySeconds)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        pendingAttackCoroutine = null;

        if (!HasStateAuthority)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        if (ServerGameManager.Instance.currentGameState == NetGameState.GameOver ||
            ServerGameManager.Instance.currentGameState == NetGameState.Init)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        bool killedKing = ApplyResolvedEffects(effectMap, usedWeapon, weaponIndex);
        attackResolutionInProgress = false;

        if (!killedKing && ServerGameManager.Instance.currentGameState == NetGameState.ResolvingAction)
        {
            ServerGameManager.Instance.CompleteManualResolve();
        }
    }

    private bool ApplyResolvedEffects(Dictionary<Vector2Int, List<CombatEffect>> effectMap, WeaponData usedWeapon, int weaponIndex)
    {
        if (effectMap == null || effectMap.Count == 0) return false;

        bool killedKing = false;

        foreach (var kvp in effectMap)
        {
            Vector2Int pos = kvp.Key;
            List<CombatEffect> effects = kvp.Value;

            NetworkChessPiece targetPiece = ServerBoardManager.Instance.GetPieceAt(pos);
            if (targetPiece == null) continue;

            int totalDamage = 0;

            foreach (var fx in effects)
            {
                if (fx.type == EffectType.Damage)
                    totalDamage += fx.value;
            }

            if (totalDamage <= 0)
                continue;

            Rpc_PlayPieceDamageVFX(pos, weaponIndex);
            killedKing = ApplyDamage(targetPiece, totalDamage);

            string weaponName = usedWeapon != null ? usedWeapon.weaponName : "UnknownWeapon";
            Debug.Log($"[Server Combat] Target at {pos} took {totalDamage} damage from weapon {weaponName} after VFX resolved. Piece damage/destroyed VFX spawned.");

            if (killedKing || ServerGameManager.Instance == null || ServerGameManager.Instance.currentGameState != NetGameState.ResolvingAction)
                break;
        }

        return killedKing;
    }

    public void CancelPendingAttackResolution()
    {
        if (pendingAttackCoroutine != null)
        {
            StopCoroutine(pendingAttackCoroutine);
            pendingAttackCoroutine = null;
        }

        if (pendingMineCoroutine != null)
        {
            StopCoroutine(pendingMineCoroutine);
            pendingMineCoroutine = null;
        }

        attackResolutionInProgress = false;
    }

    public void ClearHiddenMines()
    {
        hiddenMines.Clear();
    }

    public bool TryTriggerHiddenMineForMovedPiece(ChessFaction movedFaction, Vector2Int fromPos, Vector2Int toPos)
    {
        if (!HasStateAuthority) return false;
        if (hiddenMines.Count == 0) return false;
        if (ServerGameManager.Instance == null || ServerBoardManager.Instance == null) return false;
        if (movedFaction != ChessFaction.ChessAlliance) return false;
        if (attackResolutionInProgress) return false;

        List<Vector2Int> path = BuildMovementPathIncludingDestination(fromPos, toPos);
        int mineIndex = -1;
        HiddenMineInstance mine = default;

        for (int i = 0; i < hiddenMines.Count; i++)
        {
            HiddenMineInstance candidate = hiddenMines[i];
            if (path.Contains(candidate.GridPos))
            {
                mineIndex = i;
                mine = candidate;
                break;
            }
        }

        if (mineIndex < 0)
            return false;

        hiddenMines.RemoveAt(mineIndex);
        attackResolutionInProgress = true;
        ServerGameManager.Instance.BeginManualResolve(NetGameState.KingTurn);
        pendingMineCoroutine = StartCoroutine(ResolveHiddenMineExplosionRoutine(mine, toPos));
        return true;
    }

    private IEnumerator ResolveHiddenMineExplosionRoutine(HiddenMineInstance mine, Vector2Int movedPieceFinalPos)
    {
        WeaponData weapon = GetWeaponDataByIndex(mine.WeaponIndex);
        Rpc_PlayHiddenMineExplosionVFX(mine.GridPos, mine.WeaponIndex);

        float delay = Mathf.Max(0f, hiddenMineExplosionDamageDelaySeconds);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        pendingMineCoroutine = null;

        if (!HasStateAuthority || ServerGameManager.Instance == null || ServerBoardManager.Instance == null)
        {
            attackResolutionInProgress = false;
            yield break;
        }

        List<Vector2Int> affected = GetHiddenMineAffectedTiles(mine.GridPos, mine.AoERange);
        if (!affected.Contains(movedPieceFinalPos))
            affected.Add(movedPieceFinalPos);

        bool phaseEnded = false;
        for (int i = 0; i < affected.Count; i++)
        {
            Vector2Int pos = affected[i];
            NetworkChessPiece targetPiece = ServerBoardManager.Instance.GetPieceAt(pos);
            if (targetPiece == null || targetPiece.faction != ChessFaction.ChessAlliance)
                continue;

            Rpc_PlayPieceDamageVFX(pos, mine.WeaponIndex);
            ApplyDamage(targetPiece, mine.Damage);

            if (ServerGameManager.Instance.currentGameState != NetGameState.ResolvingAction)
            {
                phaseEnded = true;
                break;
            }
        }

        attackResolutionInProgress = false;

        if (!phaseEnded && ServerGameManager.Instance.currentGameState == NetGameState.ResolvingAction)
            ServerGameManager.Instance.CompleteManualResolve();
    }

    private List<Vector2Int> GetHiddenMineAffectedTiles(Vector2Int center, int aoeRange)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
            return result;

        int range = Mathf.Max(0, aoeRange);
        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                Vector2Int pos = center + new Vector2Int(x, y);
                if (ServerBoardManager.Instance.logicBoard.IsValidPosition(pos.x, pos.y))
                    result.Add(pos);
            }
        }

        return result;
    }

    private List<Vector2Int> BuildMovementPathIncludingDestination(Vector2Int fromPos, Vector2Int toPos)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int delta = toPos - fromPos;
        int steps = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        if (steps <= 0)
        {
            path.Add(toPos);
            return path;
        }

        bool straightOrDiagonal = delta.x == 0 || delta.y == 0 || Mathf.Abs(delta.x) == Mathf.Abs(delta.y);
        if (!straightOrDiagonal)
        {
            path.Add(toPos);
            return path;
        }

        Vector2Int step = new Vector2Int(delta.x == 0 ? 0 : delta.x / Mathf.Abs(delta.x), delta.y == 0 ? 0 : delta.y / Mathf.Abs(delta.y));
        Vector2Int current = fromPos;
        for (int i = 0; i < steps; i++)
        {
            current += step;
            path.Add(current);
        }

        return path;
    }

    private bool HasHiddenMineAt(Vector2Int pos)
    {
        for (int i = 0; i < hiddenMines.Count; i++)
        {
            if (hiddenMines[i].GridPos == pos)
                return true;
        }

        return false;
    }

    private int ResolveHiddenMineDamage(WeaponData weapon)
    {
        if (weapon == null) return 1;
        return Mathf.Max(1, weapon.hiddenMineDamage > 0 ? weapon.hiddenMineDamage : weapon.baseDamage);
    }

    private bool TryGetPlayerController(PlayerRef player, out PlayerNetworkController controller)
    {
        controller = null;

        if (NetworkRunnerHandler.Active != null && NetworkRunnerHandler.Active.TryGetPlayerController(player, out controller) && controller != null)
            return true;

        NetworkObject playerObject = Runner != null ? Runner.GetPlayerObject(player) : null;
        controller = playerObject != null ? playerObject.GetComponent<PlayerNetworkController>() : null;
        return controller != null;
    }

    private float ResolveServerDamageDelaySeconds(
        WeaponData weapon,
        Vector2Int startPos,
        Vector2Int targetPos,
        Dictionary<Vector2Int, List<CombatEffect>> effectMap)
    {
        if (weapon != null && weapon.serverDamageDelaySeconds > 0f)
            return weapon.serverDamageDelaySeconds;

        WeaponVFXProjectileMode mode = ResolveProjectileMode(weapon);
        float distance = Mathf.Max(Mathf.Abs(targetPos.x - startPos.x), Mathf.Abs(targetPos.y - startPos.y));
        float distanceEstimate = distance * 0.08f;

        if (mode == WeaponVFXProjectileMode.SingleToSelectedTarget)
        {
            return Mathf.Clamp(grenadeAutoDamageDelaySeconds + distanceEstimate, 0.1f, maxAutoDamageDelaySeconds);
        }

        if (mode == WeaponVFXProjectileMode.MultiToAffectedTiles || mode == WeaponVFXProjectileMode.MultiToMaxRangeTiles)
        {
            int maxEffectDistance = 0;
            if (effectMap != null)
            {
                foreach (Vector2Int grid in effectMap.Keys)
                {
                    int d = Mathf.Max(Mathf.Abs(grid.x - startPos.x), Mathf.Abs(grid.y - startPos.y));
                    if (d > maxEffectDistance) maxEffectDistance = d;
                }
            }

            return Mathf.Clamp(shotgunAutoDamageDelaySeconds + maxEffectDistance * 0.05f, 0.1f, maxAutoDamageDelaySeconds);
        }

        return Mathf.Clamp(fallbackDamageDelaySeconds + distanceEstimate, 0.1f, maxAutoDamageDelaySeconds);
    }

    private WeaponVFXProjectileMode ResolveProjectileMode(WeaponData weapon)
    {
        if (weapon == null) return WeaponVFXProjectileMode.SingleToSelectedTarget;
        if (weapon.vfxProjectileMode != WeaponVFXProjectileMode.Auto)
            return weapon.vfxProjectileMode;

        string weaponName = weapon.weaponName != null ? weapon.weaponName.ToLowerInvariant() : string.Empty;

        if (weapon.specialType == WeaponSpecialType.HiddenMine)
            return WeaponVFXProjectileMode.SingleToSelectedTarget;

        if (weaponName.Contains("grenade") || weaponName.Contains("nade") || weaponName.Contains("bomb") || weaponName.Contains("mine"))
            return WeaponVFXProjectileMode.SingleToSelectedTarget;

        if (weaponName.Contains("shotgun") || weaponName.Contains("scatter") || weaponName.Contains("spray"))
            return WeaponVFXProjectileMode.MultiToMaxRangeTiles;

        if (weapon.gunPrefab != null && weapon.isOriginRelative && weapon.isDirectional)
            return WeaponVFXProjectileMode.MultiToMaxRangeTiles;

        return WeaponVFXProjectileMode.SingleToSelectedTarget;
    }

    private NetworkChessPiece FindRogueKingPiece()
    {
        if (ServerBoardManager.Instance == null || ServerBoardManager.Instance.logicBoard == null)
            return null;

        for (int x = 0; x < ServerBoardManager.Instance.logicBoard.width; x++)
        {
            for (int y = 0; y < ServerBoardManager.Instance.logicBoard.height; y++)
            {
                NetworkChessPiece piece = ServerBoardManager.Instance.GetPieceAt(new Vector2Int(x, y));

                if (piece != null && piece.isKing && piece.faction == ChessFaction.ChessRogue)
                {
                    return piece;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Applies damage to a network piece.
    /// Returns true if this damage defeated a king and caused phase/game-state transition.
    /// </summary>
    private bool ApplyDamage(NetworkChessPiece targetPiece, int damage)
    {
        if (!HasStateAuthority || targetPiece == null || damage <= 0) return false;

        ChessPieceRuntime runtime = ServerBoardManager.Instance.GetRuntimeAt(targetPiece.currentGridPos);

        if (runtime != null && runtime.hasShield)
        {
            runtime.hasShield = false;
            Debug.Log("[Server Combat] Damage blocked by shield.");
            return false;
        }

        targetPiece.currentHp -= damage;
        if (targetPiece.currentHp < 0)
            targetPiece.currentHp = 0;

        if (runtime != null)
        {
            runtime.currentHealth = targetPiece.currentHp;
        }

        if (targetPiece.currentHp <= 0)
        {
            return ProcessInstaKill(targetPiece);
        }

        return false;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayCombatVFX(Vector2Int startPos, Vector2Int targetPos, int weaponIndex)
    {
        if (Application.isBatchMode)
            return;

        if (availableWeapons == null || weaponIndex < 0 || weaponIndex >= availableWeapons.Count)
            return;

        WeaponData usedWeapon = availableWeapons[weaponIndex];
        ChessBoard visualBoard = FindFirstObjectByType<ChessBoard>();
        if (visualBoard == null)
        {
            Debug.LogWarning("[Client VFX] Missing ChessBoard. Cannot play weapon VFX.");
            return;
        }

        BoardData vfxBoard = BuildLocalVFXBoard(visualBoard);
        List<Vector2Int> affectedTiles = vfxBoard != null
            ? ActionResolver.GetAoE(usedWeapon, startPos, targetPos, vfxBoard)
            : new List<Vector2Int> { targetPos };

        Vector3 startWorldPos = visualBoard.GetPieceWorldPosition(startPos);

        Debug.Log($"[Client VFX] Playing VFX for weapon {usedWeapon.weaponName} from {startPos} to selected target {targetPos}. AffectedTiles={affectedTiles.Count}");

        CombatVFXManager.Instance.PlayWeaponVFX(
            usedWeapon,
            startWorldPos,
            startPos,
            targetPos,
            affectedTiles,
            visualBoard,
            null
        );
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayPieceDamageVFX(Vector2Int damagedPos, int weaponIndex)
    {
        if (Application.isBatchMode)
            return;

        WeaponData usedWeapon = null;
        if (availableWeapons != null && weaponIndex >= 0 && weaponIndex < availableWeapons.Count)
            usedWeapon = availableWeapons[weaponIndex];

        ChessBoard visualBoard = FindFirstObjectByType<ChessBoard>();
        if (visualBoard == null)
        {
            Debug.LogWarning($"[Client VFX] Missing ChessBoard. Cannot play piece damage/destroyed effect at {damagedPos}.");
            return;
        }

        CombatVFXManager vfxManager = CombatVFXManager.Instance;
        if (vfxManager == null)
        {
            Debug.LogWarning($"[Client VFX] Missing CombatVFXManager. Cannot play piece damage/destroyed effect at {damagedPos}.");
            return;
        }

        vfxManager.PlayDestroyedEffect(usedWeapon, damagedPos, visualBoard);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayHiddenMineExplosionVFX(Vector2Int minePos, int weaponIndex)
    {
        if (Application.isBatchMode)
            return;

        WeaponData weapon = GetWeaponDataByIndex(weaponIndex);
        ChessBoard visualBoard = FindFirstObjectByType<ChessBoard>();
        if (visualBoard == null)
            return;

        BoardTile tile = visualBoard.GetTileAt(minePos);
        if (tile == null)
            return;

        Vector3 spawnPos = tile.transform.position + visualBoard.PiecePlacementOffset + hiddenMineExplosionOffset;

        if (weapon != null && weapon.hiddenMineExplosionPrefab != null)
        {
            Instantiate(weapon.hiddenMineExplosionPrefab, spawnPos, Quaternion.identity);
        }
        else if (CombatVFXManager.Instance != null)
        {
            CombatVFXManager.Instance.PlayDestroyedEffect(weapon, minePos, visualBoard);
        }

        if (weapon != null && weapon.hiddenMineExplosionSfx != null)
            GameAudioManager.PlaySfx(weapon.hiddenMineExplosionSfx, spawnPos, weapon.sfxSpatialBlend);
    }

    private BoardData BuildLocalVFXBoard(ChessBoard visualBoard)
    {
        LevelData levelData = null;

        if (visualBoard != null)
            levelData = visualBoard.CurrentLevelData;

        if (levelData == null && ServerBoardManager.Instance != null)
            levelData = ServerBoardManager.Instance.currentLevelData;

        if (levelData == null)
            return null;

        return new BoardData(
            levelData.boardWidth,
            levelData.boardHeight,
            levelData.tileExistenceMap != null ? levelData.tileExistenceMap.ToList() : null
        );
    }

    /// <summary>
    /// Removes a piece from the authoritative board.
    /// Returns true if the removed piece was a king and caused phase/game-state transition.
    /// </summary>
    public bool ProcessInstaKill(NetworkChessPiece targetPiece)
    {
        if (!HasStateAuthority || targetPiece == null) return false;
        if (ServerBoardManager.Instance == null) return false;

        Vector2Int deathPos = targetPiece.currentGridPos;
        bool wasKing = targetPiece.isKing;
        ChessFaction defeatedFaction = targetPiece.faction;

        ChessPieceRuntime runtime = ServerBoardManager.Instance.GetRuntimeAt(deathPos);
        if (runtime != null)
            defeatedFaction = runtime.faction;

        if (runtime != null && ServerGameManager.Instance != null)
        {
            ServerGameManager.Instance.graveyard.Add(new DeadPieceRecord
            {
                pieceData = runtime.baseData,
                faction = runtime.faction,
                deathPos = deathPos
            });
        }

        ServerBoardManager.Instance.RemovePieceAt(deathPos);

        if (targetPiece.Object != null)
        {
            Runner.Despawn(targetPiece.Object);
        }

        if (ServerGameManager.Instance != null)
        {
            if (wasKing)
            {
                ServerGameManager.Instance.OnKingDefeated();
            }
            else if (defeatedFaction == ChessFaction.ChessAlliance &&
                     !ServerBoardManager.Instance.HasAnyPieceOfFaction(ChessFaction.ChessAlliance))
            {
                Debug.Log("[Server Combat] All Chess Alliance pieces have been defeated. Rogue King wins the current phase.");
                ServerGameManager.Instance.OnChessAllianceEliminated();
            }
        }

        return wasKing;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        CancelPendingAttackResolution();
        ClearHiddenMines();

        if (Instance == this)
            Instance = null;
    }
}
