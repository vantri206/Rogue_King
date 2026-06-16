using System.Collections.Generic;
using UnityEngine;

public class CombatManager : SingletonMB<CombatManager>
{
    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;

    // Thêm tham số WeaponData vào hàm
    public void ExecuteAttack(ChessPieceRuntime attacker, WeaponData usedWeapon, Vector2Int lockedTarget)
    {
        if (attacker == null || usedWeapon == null) return;

        Dictionary<Vector2Int, List<CombatEffect>> effectMap = ActionResolver.CalculateWeaponGrid(
            usedWeapon,
            attacker.currentGridPosition,
            lockedTarget,
            chessBoard.boardData
        );

        foreach (var kvp in effectMap)
        {
            Vector2Int pos = kvp.Key;
            List<CombatEffect> effects = kvp.Value;

            ChessPieceRuntime targetPiece = chessBoard.boardData.GetEntityAt<ChessPieceRuntime>(pos.x, pos.y);
            if (targetPiece != null)
            {
                foreach (CombatEffect effect in effects)
                {
                    ApplyEffect(targetPiece, effect);
                }
            }
        }

        ResolveDeaths();
        if (GameManager.Instance != null) GameManager.Instance.ForceResolveTurn();
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

            // Remove from data grid
            chessBoard.boardData.RemoveEntity(deadPiece);

            // Visual removal
            BoardTile tile = chessBoard.GetTileAt(pos);
            if (tile != null && tile.currentPiece != null)
            {
                Destroy(tile.currentPiece.gameObject);
                tile.ClearPiece();
            }

            // Check win condition based on GDD
            if (deadPiece.baseData.pieceName.Contains("King"))
            {
                GameManager.Instance.OnKingDefeated();
            }
        }
    }
}