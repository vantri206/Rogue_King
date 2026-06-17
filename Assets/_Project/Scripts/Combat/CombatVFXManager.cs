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

    private static readonly int ShootHash = Animator.StringToHash("Shoot");

    public void PlayWeaponVFX(WeaponData weapon, Vector3 startWorldPos, List<Vector2Int> targetGrids, ChessBoard board, Action<Vector2Int> onProjectileHit)
    {
        if (targetGrids == null || targetGrids.Count == 0) return;

        if (weapon.projectilePrefab == null)
        {
            Debug.LogWarning("[CombatVFXManager] Missing Projectile Prefab. Applying damage instantly.");
            foreach (var grid in targetGrids) onProjectileHit?.Invoke(grid);
            return;
        }

        Vector3 firstTargetPos = board.GetTileAt(targetGrids[0]).transform.position + board.PiecePlacementOffset;
        Vector3 aimDirection = (firstTargetPos - startWorldPos).normalized;

        Vector3 spawnPos = startWorldPos + new Vector3(0f, gunSpawnOffsetY, 0f);

        if (weapon.gunPrefab != null)
        {
            GameObject gunInstance = Instantiate(weapon.gunPrefab, spawnPos, Quaternion.identity);

            float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
            gunInstance.transform.rotation = Quaternion.Euler(0, 0, angle);

            float faceDirection = aimDirection.x < 0 ? -1f : 1f;
            Vector3 currentScale = gunInstance.transform.localScale;
            gunInstance.transform.localScale = new Vector3(Mathf.Abs(currentScale.x), Mathf.Abs(currentScale.y) * faceDirection, currentScale.z);

            Animator gunAnim = gunInstance.GetComponent<Animator>();
            if (gunAnim != null)
            {
                gunAnim.SetTrigger(ShootHash);
            }

            DOVirtual.DelayedCall(gunFireDelay, () =>
            {
                if (gunInstance == null) return;

                SpawnProjectiles(weapon, spawnPos, targetGrids, board, onProjectileHit);

                Vector3 recoilPos = -aimDirection * 0.3f;
                gunInstance.transform.DOPunchPosition(recoilPos, 0.2f, 10, 1f);

                Vector3 recoilRot = new Vector3(0f, 0f, gunRecoilRotation * faceDirection);
                gunInstance.transform.DOPunchRotation(recoilRot, 0.2f, 10, 1f).OnComplete(() =>
                {
                    Destroy(gunInstance, 0.2f);
                });
            });
        }
        else
        {
            SpawnProjectiles(weapon, spawnPos, targetGrids, board, onProjectileHit);
        }
    }

    private void SpawnProjectiles(WeaponData weapon, Vector3 spawnPos, List<Vector2Int> targetGrids, ChessBoard board, Action<Vector2Int> onProjectileHit)
    {
        foreach (Vector2Int targetGrid in targetGrids)
        {
            BoardTile tile = board.GetTileAt(targetGrid);
            if (tile == null) continue;

            Vector3 endWorldPos = tile.transform.position + board.PiecePlacementOffset;

            GameObject projInstance = Instantiate(weapon.projectilePrefab, spawnPos, Quaternion.identity);
            Projectile projectileComponent = projInstance.GetComponent<Projectile>();

            if (projectileComponent != null)
            {
                projectileComponent.Initialize(spawnPos, endWorldPos, () =>
                {
                    onProjectileHit?.Invoke(targetGrid);
                });
            }
        }
    }
}