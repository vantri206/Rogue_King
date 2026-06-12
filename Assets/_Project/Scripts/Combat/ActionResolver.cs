using System.Collections.Generic;
using UnityEngine;

public static class ActionResolver
{
    public static Dictionary<Vector2Int, List<CombatEffect>> CalculateEffectMap(WeaponData weapon, Vector2Int attackerPos, Vector2Int targetDir, BoardData boardData)
    {
        if (weapon.attackMechanism == AttackMechanism.GridPattern)
        {
            return CalculateGridDamage(weapon, attackerPos, targetDir, boardData);
        }
        return null;
    }

    private static Dictionary<Vector2Int, List<CombatEffect>> CalculateGridDamage(WeaponData weapon, Vector2Int attackerPos, Vector2Int targetDir, BoardData boardData)
    {
        Dictionary<Vector2Int, List<CombatEffect>> effectMap = new Dictionary<Vector2Int, List<CombatEffect>>();
        int centerIndex = weapon.patternSize / 2;

        for (int y = 0; y < weapon.patternSize; y++)
        {
            for (int x = 0; x < weapon.patternSize; x++)
            {
                int index = y * weapon.patternSize + x;
                if (weapon.attackPatternGrid[index])
                {
                    Vector2Int offset = new Vector2Int(x - centerIndex, centerIndex - y);
                    Vector2Int finalOffset = RotateOffset(offset, targetDir);
                    Vector2Int hitPos = attackerPos + finalOffset;

                    if (boardData != null && !boardData.IsValidPosition(hitPos.x, hitPos.y))
                        continue;

                    if (!effectMap.ContainsKey(hitPos))
                        effectMap[hitPos] = new List<CombatEffect>();

                    effectMap[hitPos].Add(new CombatEffect(weapon.defaultEffectType, weapon.baseDamage));
                }
            }
        }
        return effectMap;
    }
    private static Vector2Int RotateOffset(Vector2Int offset, Vector2Int direction)
    {
        if (direction == Vector2Int.right) return new Vector2Int(offset.y, -offset.x);
        if (direction == Vector2Int.down) return new Vector2Int(-offset.x, -offset.y);
        if (direction == Vector2Int.left) return new Vector2Int(-offset.y, offset.x);
        return offset; // Up
    }
}