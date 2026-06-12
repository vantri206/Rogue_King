using System.Collections.Generic;
using UnityEngine;

public class CombatManager : SingletonMB<CombatManager>
{
    [Header("Core References")]
    [SerializeField] private ChessBoard chessBoard;
  
    public void ExecuteAttack(ChessPieceRuntime attacker, Vector2Int targetPos)
    {
        if (attacker == null || attacker.currentWeapon == null)
        {
            Debug.LogWarning("[CombatManager] Attacker or Weapon is null!");
            return;
        }

        Vector2 rawDir = (Vector2)(targetPos - attacker.currentGridPosition);
        Vector2Int targetDir = new Vector2Int(Mathf.RoundToInt(rawDir.normalized.x), Mathf.RoundToInt(rawDir.normalized.y));

        if (targetDir == Vector2Int.zero) targetDir = Vector2Int.right;

        Dictionary<Vector2Int, List<CombatEffect>> effectMap = ActionResolver.CalculateEffectMap(
            attacker.currentWeapon,
            attacker.currentGridPosition,
            targetDir,
            chessBoard.boardData
        );

        foreach (var kvp in effectMap)
        {
            Vector2Int pos = kvp.Key;
            List<CombatEffect> effects = kvp.Value;

            ChessPieceRuntime targetPiece = chessBoard.GetPieceRuntimeAt(pos);
            if (targetPiece != null)
            {
                foreach (CombatEffect effect in effects)
                {
                    ApplyEffect(targetPiece, effect);
                }
            }
        }

        ResolveDeaths();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ForceResolveTurn();
        }
    }

    private void ApplyEffect(ChessPieceRuntime target, CombatEffect effect)
    {
        switch (effect.type)
        {
            case EffectType.Damage:
                target.currentHealth -= effect.value;
                Debug.Log($"[COMBAT MANAGER] {target.baseData.pieceName} taken {effect.value} damage. HP reamaining: {target.currentHealth}");

                // UI Event Or VFX Trigger can be placed here to show damage numbers or hit effects
                break;

            case EffectType.Heal:
                target.currentHealth += effect.value;

                if (target.currentHealth > target.baseData.baseHealth)
                    target.currentHealth = target.baseData.baseHealth;
                Debug.Log($"[COMBAT MANAGER] {target.baseData.pieceName} healed {effect.value} health.");

                // UI Event Or VFX Trigger can be placed here to show damage numbers or hit effects

                break;
        }
    }

    private void ResolveDeaths()
    {
        List<Vector2Int> deadPiecePositions = new List<Vector2Int>();

        for (int x = 0; x < chessBoard.boardWidth; x++)
        {
            for (int y = 0; y < chessBoard.boardHeight; y++)
            {
                ChessPieceRuntime piece = chessBoard.boardData.GetPieceAt(x, y);
                if (piece != null && piece.currentHealth <= 0)
                {
                    deadPiecePositions.Add(new Vector2Int(x, y));
                }
            }
        }

        foreach (Vector2Int pos in deadPiecePositions)
        {
            ChessPieceRuntime deadPiece = chessBoard.boardData.GetPieceAt(pos.x, pos.y);
            Debug.Log($"[COMBAT MANAGER] {deadPiece.baseData.pieceName} defeated!");

            chessBoard.boardData.SetPiece(pos.x, pos.y, null);

            BoardTile tile = chessBoard.GetTileAt(pos);
            if (tile != null && tile.currentPiece != null)
            {
                Destroy(tile.currentPiece.gameObject);
                tile.ClearPiece();
            }
        }
    }
}