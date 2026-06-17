using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CombatManager : SingletonMB<CombatManager>
{
    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;

    private int activeProjectiles = 0;

    public void ExecuteAttack(ChessPieceRuntime attacker, WeaponData usedWeapon, Vector2Int lockedTarget)
    {
        if (attacker == null || usedWeapon == null) return;

        Dictionary<Vector2Int, List<CombatEffect>> effectMap = ActionResolver.CalculateWeaponGrid(
            usedWeapon,
            attacker.currentGridPosition,
            lockedTarget,
            chessBoard.boardData
        );

        List<Vector2Int> validTargets = effectMap.Keys.ToList();
        activeProjectiles = validTargets.Count;

        if (activeProjectiles == 0)
        {
            GameManager.Instance.ForceResolveTurn();
            return;
        }

        Vector3 startWorldPos = chessBoard.GetTileAt(attacker.currentGridPosition).transform.position + chessBoard.PiecePlacementOffset;
        CombatVFXManager.Instance.PlayWeaponVFX(usedWeapon, startWorldPos, validTargets, chessBoard,
            (hitGridPos) => OnProjectileHit(hitGridPos, effectMap)
        );
    }
    private void OnProjectileHit(Vector2Int gridPos, Dictionary<Vector2Int, List<CombatEffect>> effectMap)
    {
        if (effectMap.TryGetValue(gridPos, out List<CombatEffect> effects))
        {
            ChessPieceRuntime targetPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(gridPos.x, gridPos.y);
            if (targetPiece != null)
            {
                foreach (CombatEffect effect in effects)
                {
                    ApplyEffect(targetPiece, effect);
                }
            }
        }

        activeProjectiles--;

        if (activeProjectiles <= 0)
        {
            ResolveDeaths();
            if (GameManager.Instance != null) GameManager.Instance.ForceResolveTurn();
        }
    }

    private void ApplyEffect(ChessPieceRuntime target, CombatEffect effect)
    {
        switch (effect.type)
        {
            case EffectType.Damage:
                target.currentHealth -= effect.value;
                Debug.Log($"[CombatManager] {target.baseData.pieceName} took {effect.value} damage. HP remaining: {target.currentHealth}");
                break;

            case EffectType.Heal:
                target.currentHealth += effect.value;
                if (target.currentHealth > target.baseData.baseHealth)
                    target.currentHealth = target.baseData.baseHealth;
                Debug.Log($"[CombatManager] {target.baseData.pieceName} healed {effect.value} HP.");
                break;
        }
    }

    private void ResolveDeaths()
    {
        List<ChessPieceRuntime> deadPieces = new List<ChessPieceRuntime>();

        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                ChessPieceRuntime piece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(x, y);
                if (piece != null && piece.currentHealth <= 0)
                {
                    deadPieces.Add(piece);
                }
            }
        }

        foreach (var deadPiece in deadPieces)
        {
            Debug.Log($"[CombatManager] {deadPiece.baseData.pieceName} defeated!");

            Vector2Int pos = deadPiece.currentGridPosition;
            chessBoard.boardData.RemoveEntity(deadPiece);

            BoardTile tile = chessBoard.GetTileAt(pos);
            if (tile != null && tile.currentPiece != null)
            {
                Destroy(tile.currentPiece.gameObject);
                tile.ClearPiece();
            }

            if (deadPiece.baseData.pieceName.Contains("King"))
            {
                GameManager.Instance.OnKingDefeated();
            }
        }
    }
}