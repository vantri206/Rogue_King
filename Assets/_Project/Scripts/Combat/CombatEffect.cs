using UnityEngine;

public enum EffectType
{
    Damage,
    Heal,
}

public enum AttackMechanism
{
    GridPattern,
}

[System.Serializable]
public struct CombatEffect
{
    public EffectType type;
    public int value;

    public CombatEffect(EffectType type, int value)
    {
        this.type = type;
        this.value = value;
    }
}