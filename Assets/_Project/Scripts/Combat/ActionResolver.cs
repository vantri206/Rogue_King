using System.Collections.Generic;
using UnityEngine;

public static class ActionResolver
{
    public static List<Vector2Int> GetTargetingRange(WeaponData weapon, Vector2Int startPos, BoardData board)
    {
        List<Vector2Int> validTargets = new List<Vector2Int>();
        if (weapon == null || board == null) return validTargets;

        foreach (Vector2Int offset in weapon.targetingPattern)
        {
            Vector2Int pos = startPos + offset;
            if (board.IsValidPosition(pos.x, pos.y))
            {
                validTargets.Add(pos);
            }
        }
        return validTargets;
    }

    public static Dictionary<Vector2Int, List<CombatEffect>> CalculateWeaponGrid(WeaponData weapon, Vector2Int startPos, Vector2Int targetPos, BoardData board)
    {
        Dictionary<Vector2Int, List<CombatEffect>> effectMap = new Dictionary<Vector2Int, List<CombatEffect>>();
        if (weapon == null || board == null) return effectMap;

        Vector2Int dir = targetPos - startPos;
        Vector2Int normalizedDir = Vector2Int.up;

        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            normalizedDir = new Vector2Int(System.Math.Sign(dir.x), 0);
        else if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x) && dir != Vector2Int.zero)
            normalizedDir = new Vector2Int(0, System.Math.Sign(dir.y));

        int maxClickDistance = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));

        int maxPatternRange = 1;
        if (weapon.isOriginRelative)
        {
            foreach (Vector2Int offset in weapon.effectPattern)
            {
                int dist = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
                if (dist > maxPatternRange) maxPatternRange = dist;
            }
        }

        foreach (Vector2Int offset in weapon.effectPattern)
        {
            Vector2Int finalOffset = offset;

            if (weapon.isDirectional)
            {
                finalOffset = RotateOffset(offset, normalizedDir);
            }

            Vector2Int pos;
            int cellDistance = 0;

            if (weapon.isOriginRelative)
            {
                pos = startPos + finalOffset;
                cellDistance = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));

                if (cellDistance > maxClickDistance) continue;
            }
            else
            {
                pos = targetPos + finalOffset;
            }

            if (board.IsValidPosition(pos.x, pos.y))
            {
                float falloffRatio = 0f;
                if (weapon.isOriginRelative && maxPatternRange > 0)
                {
                    falloffRatio = (float)cellDistance / maxPatternRange;
                }

                float damageMultiplier = weapon.damageFalloff.Evaluate(falloffRatio);
                int finalDamage = Mathf.RoundToInt(weapon.baseDamage * damageMultiplier);

                if (finalDamage <= 0) finalDamage = 1;

                if (!effectMap.ContainsKey(pos))
                    effectMap[pos] = new List<CombatEffect>();

                effectMap[pos].Add(new CombatEffect(weapon.defaultEffectType, finalDamage));
            }
        }
        return effectMap;
    }

    public static List<Vector2Int> GetAoE(WeaponData weapon, Vector2Int startPos, Vector2Int targetPos, BoardData board)
    {
        Dictionary<Vector2Int, List<CombatEffect>> effectMap = CalculateWeaponGrid(weapon, startPos, targetPos, board);
        return new List<Vector2Int>(effectMap.Keys);
    }

    private static Vector2Int RotateOffset(Vector2Int offset, Vector2Int dir)
    {
        if (dir == Vector2Int.right) return new Vector2Int(offset.y, -offset.x);
        if (dir == Vector2Int.down) return new Vector2Int(-offset.x, -offset.y);
        if (dir == Vector2Int.left) return new Vector2Int(-offset.y, offset.x);
        return offset;
    }
}