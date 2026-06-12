using UnityEngine;


[System.Serializable]
public struct CombatEffectData
{
    public EffectType type;
    public int value;

    public CombatEffectData(EffectType type, int value)
    {
        this.type = type;
        this.value = value;
    }
}