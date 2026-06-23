using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class CombatVFXManager : SingletonMB<CombatVFXManager>
{
    [Header("Gun Spawning")]
    [SerializeField] private float gunSpawnOffsetY = 1.0f;
    [SerializeField] private float gunFireDelay = 0.5f;
    [SerializeField] private float gunRecoilRotation = 20f;

    [Header("Projectile Impact Offsets")]
    [SerializeField] private float defaultGunProjectileTargetOffsetY = 0.65f;
    [SerializeField] private float defaultDestroyedEffectOffsetY = 0.55f;

    [Header("Damage / Destroyed Effect")]
    [SerializeField] private GameObject defaultDestroyedEffectPrefab;

    private static readonly int ShootHash = Animator.StringToHash("Shoot");

    // Backward-compatible entry point for old/offline code.
    public void PlayWeaponVFX(WeaponData weapon, Vector3 startWorldPos, List<Vector2Int> targetGrids, ChessBoard board, Action<Vector2Int> onProjectileHit)
    {
        if (targetGrids == null || targetGrids.Count == 0) return;
        PlayWeaponVFX(weapon, startWorldPos, targetGrids[0], targetGrids[0], targetGrids, board, onProjectileHit);
    }

    // Kept for compatibility with the previous network patch.
    public void PlayWeaponVFX(WeaponData weapon, Vector3 startWorldPos, Vector2Int selectedTargetGrid, List<Vector2Int> affectedGrids, ChessBoard board, Action<Vector2Int> onProjectileHit)
    {
        PlayWeaponVFX(weapon, startWorldPos, selectedTargetGrid, selectedTargetGrid, affectedGrids, board, onProjectileHit);
    }

    // Dedicated-server friendly entry point.
    // The callback is fired only after the visual has reached/finished its impact. This keeps damage/despawn visually synced.
    public void PlayWeaponVFX(
        WeaponData weapon,
        Vector3 startWorldPos,
        Vector2Int startGrid,
        Vector2Int selectedTargetGrid,
        List<Vector2Int> affectedGrids,
        ChessBoard board,
        Action<Vector2Int> onProjectileHit)
    {
        if (weapon == null || board == null) return;

        List<Vector2Int> resolvedAffectedGrids = ResolveAffectedGrids(selectedTargetGrid, affectedGrids);

        if (weapon.projectilePrefab == null)
        {
            Debug.LogWarning("[CombatVFXManager] Missing Projectile Prefab. Resolving VFX hit callbacks instantly.");
            InvokeAffectedCallbacks(resolvedAffectedGrids, onProjectileHit);
            return;
        }

        WeaponVFXProjectileMode mode = ResolveProjectileMode(weapon);
        List<Vector2Int> projectileTargetGrids = ResolveProjectileTargetGrids(mode, startGrid, selectedTargetGrid, resolvedAffectedGrids);

        if (projectileTargetGrids.Count == 0)
            projectileTargetGrids.Add(selectedTargetGrid);

        Vector2Int aimGrid = projectileTargetGrids[0];
        BoardTile aimTile = board.GetTileAt(aimGrid);
        if (aimTile == null)
        {
            Debug.LogWarning($"[CombatVFXManager] Missing aim tile at {aimGrid}. Resolving VFX hit callbacks instantly.");
            InvokeAffectedCallbacks(resolvedAffectedGrids, onProjectileHit);
            return;
        }

        Vector3 aimWorldPos = GetProjectileTargetWorldPosition(weapon, mode, aimGrid, board);
        Vector3 aimDirection = (aimWorldPos - startWorldPos).normalized;
        Vector3 spawnPos = startWorldPos + new Vector3(0f, gunSpawnOffsetY, 0f);

        Action fireProjectiles = () =>
        {
            SpawnProjectileGroup(weapon, mode, spawnPos, projectileTargetGrids, resolvedAffectedGrids, board, onProjectileHit);
        };

        if (weapon.gunPrefab != null)
        {
            SpawnGunAndFire(weapon, spawnPos, aimDirection, fireProjectiles);
        }
        else
        {
            fireProjectiles.Invoke();
        }
    }

    private List<Vector2Int> ResolveAffectedGrids(Vector2Int selectedTargetGrid, List<Vector2Int> affectedGrids)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (affectedGrids != null)
        {
            foreach (Vector2Int grid in affectedGrids)
            {
                if (!result.Contains(grid))
                    result.Add(grid);
            }
        }

        if (result.Count == 0)
            result.Add(selectedTargetGrid);

        return result;
    }

    private WeaponVFXProjectileMode ResolveProjectileMode(WeaponData weapon)
    {
        if (weapon == null) return WeaponVFXProjectileMode.SingleToSelectedTarget;
        if (weapon.vfxProjectileMode != WeaponVFXProjectileMode.Auto)
            return weapon.vfxProjectileMode;

        string weaponName = weapon.weaponName != null ? weapon.weaponName.ToLowerInvariant() : string.Empty;

        if (weaponName.Contains("grenade") || weaponName.Contains("nade") || weaponName.Contains("bomb"))
            return WeaponVFXProjectileMode.SingleToSelectedTarget;

        if (weaponName.Contains("shotgun") || weaponName.Contains("scatter") || weaponName.Contains("spray"))
            return WeaponVFXProjectileMode.MultiToMaxRangeTiles;

        if (weapon.gunPrefab != null && weapon.isOriginRelative && weapon.isDirectional)
            return WeaponVFXProjectileMode.MultiToMaxRangeTiles;

        return WeaponVFXProjectileMode.SingleToSelectedTarget;
    }

    private List<Vector2Int> ResolveProjectileTargetGrids(
        WeaponVFXProjectileMode mode,
        Vector2Int startGrid,
        Vector2Int selectedTargetGrid,
        List<Vector2Int> affectedGrids)
    {
        switch (mode)
        {
            case WeaponVFXProjectileMode.MultiToAffectedTiles:
                return new List<Vector2Int>(affectedGrids);

            case WeaponVFXProjectileMode.MultiToMaxRangeTiles:
                return GetMaxRangeTiles(startGrid, selectedTargetGrid, affectedGrids);

            case WeaponVFXProjectileMode.SingleToSelectedTarget:
            case WeaponVFXProjectileMode.Auto:
            default:
                return new List<Vector2Int> { selectedTargetGrid };
        }
    }

    private List<Vector2Int> GetMaxRangeTiles(Vector2Int startGrid, Vector2Int fallbackTargetGrid, List<Vector2Int> affectedGrids)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        if (affectedGrids == null || affectedGrids.Count == 0)
        {
            result.Add(fallbackTargetGrid);
            return result;
        }

        int maxDistance = -1;
        foreach (Vector2Int grid in affectedGrids)
        {
            int distance = Mathf.Max(Mathf.Abs(grid.x - startGrid.x), Mathf.Abs(grid.y - startGrid.y));
            if (distance > maxDistance)
            {
                maxDistance = distance;
                result.Clear();
                result.Add(grid);
            }
            else if (distance == maxDistance && !result.Contains(grid))
            {
                result.Add(grid);
            }
        }

        if (result.Count == 0)
            result.Add(fallbackTargetGrid);

        return result;
    }

    private void SpawnGunAndFire(WeaponData weapon, Vector3 spawnPos, Vector3 aimDirection, Action fireProjectiles)
    {
        GameObject gunInstance = Instantiate(weapon.gunPrefab, spawnPos, Quaternion.identity);

        if (aimDirection != Vector3.zero)
        {
            float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
            gunInstance.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        float faceDirection = aimDirection.x < 0 ? -1f : 1f;
        Vector3 currentScale = gunInstance.transform.localScale;
        gunInstance.transform.localScale = new Vector3(Mathf.Abs(currentScale.x), Mathf.Abs(currentScale.y) * faceDirection, currentScale.z);

        Animator gunAnim = gunInstance.GetComponent<Animator>();
        if (gunAnim != null)
            gunAnim.SetTrigger(ShootHash);

        DOVirtual.DelayedCall(gunFireDelay, () =>
        {
            if (gunInstance == null) return;

            fireProjectiles?.Invoke();

            Vector3 recoilPos = -aimDirection * 0.3f;
            gunInstance.transform.DOPunchPosition(recoilPos, 0.2f, 10, 1f);

            Vector3 recoilRot = new Vector3(0f, 0f, gunRecoilRotation * faceDirection);
            gunInstance.transform.DOPunchRotation(recoilRot, 0.2f, 10, 1f).OnComplete(() =>
            {
                Destroy(gunInstance, 0.2f);
            });
        });
    }

    private void SpawnProjectileGroup(
        WeaponData weapon,
        WeaponVFXProjectileMode mode,
        Vector3 spawnPos,
        List<Vector2Int> projectileTargetGrids,
        List<Vector2Int> affectedGrids,
        ChessBoard board,
        Action<Vector2Int> onProjectileHit)
    {
        if (projectileTargetGrids == null || projectileTargetGrids.Count == 0)
        {
            InvokeAffectedCallbacks(affectedGrids, onProjectileHit);
            return;
        }

        int pendingProjectiles = 0;
        bool hasResolved = false;

        Action resolveOnce = () =>
        {
            if (hasResolved) return;
            hasResolved = true;
            InvokeAffectedCallbacks(affectedGrids, onProjectileHit);
        };

        foreach (Vector2Int targetGrid in projectileTargetGrids)
        {
            BoardTile tile = board.GetTileAt(targetGrid);
            if (tile == null)
                continue;

            Vector3 endWorldPos = GetProjectileTargetWorldPosition(weapon, mode, targetGrid, board);
            GameObject projInstance = Instantiate(weapon.projectilePrefab, spawnPos, Quaternion.identity);
            Projectile projectileComponent = projInstance.GetComponent<Projectile>();

            if (projectileComponent != null)
            {
                pendingProjectiles++;
                projectileComponent.Initialize(spawnPos, endWorldPos, () =>
                {
                    pendingProjectiles--;
                    if (pendingProjectiles <= 0)
                        resolveOnce.Invoke();
                });
            }
            else
            {
                Destroy(projInstance);
            }
        }

        if (pendingProjectiles <= 0)
            resolveOnce.Invoke();
    }


    private Vector3 GetProjectileTargetWorldPosition(WeaponData weapon, WeaponVFXProjectileMode mode, Vector2Int targetGrid, ChessBoard board)
    {
        BoardTile tile = board != null ? board.GetTileAt(targetGrid) : null;
        if (tile == null)
            return Vector3.zero;

        return tile.transform.position + board.PiecePlacementOffset + ResolveProjectileTargetOffset(weapon, mode);
    }

    private Vector3 ResolveProjectileTargetOffset(WeaponData weapon, WeaponVFXProjectileMode mode)
    {
        if (weapon != null && weapon.overrideProjectileTargetOffset)
            return weapon.projectileTargetWorldOffset;

        bool gunLikeProjectile = weapon != null && weapon.gunPrefab != null;
        bool multiProjectile = mode == WeaponVFXProjectileMode.MultiToAffectedTiles || mode == WeaponVFXProjectileMode.MultiToMaxRangeTiles;

        // Most pieces are positioned with their pivot at the feet. Gun/shotgun/sniper bullets should fly into body center,
        // while grenade-style projectiles should still land on the selected tile unless explicitly overridden per weapon.
        if (gunLikeProjectile || multiProjectile)
            return new Vector3(0f, defaultGunProjectileTargetOffsetY, 0f);

        return Vector3.zero;
    }

    public void PlayDestroyedEffect(WeaponData weapon, Vector2Int damagedGrid, ChessBoard board)
    {
        if (board == null) return;

        GameObject effectPrefab = null;
        if (weapon != null && weapon.destroyedEffectPrefab != null)
            effectPrefab = weapon.destroyedEffectPrefab;
        else
            effectPrefab = defaultDestroyedEffectPrefab;

        if (effectPrefab == null)
            return;

        BoardTile tile = board.GetTileAt(damagedGrid);
        if (tile == null)
            return;

        Vector3 spawnPos = tile.transform.position + board.PiecePlacementOffset + ResolveDestroyedEffectOffset(weapon);
        Instantiate(effectPrefab, spawnPos, Quaternion.identity);
    }

    private Vector3 ResolveDestroyedEffectOffset(WeaponData weapon)
    {
        if (weapon != null && weapon.overrideDestroyedEffectOffset)
            return weapon.destroyedEffectWorldOffset;

        return new Vector3(0f, defaultDestroyedEffectOffsetY, 0f);
    }

    private void InvokeAffectedCallbacks(List<Vector2Int> affectedGrids, Action<Vector2Int> onProjectileHit)
    {
        if (affectedGrids == null) return;

        foreach (Vector2Int affectedGrid in affectedGrids)
            onProjectileHit?.Invoke(affectedGrid);
    }
}
