using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Chess/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic Settings")]
    public string weaponName;
    public AttackMechanism attackMechanism;
    public EffectType defaultEffectType = EffectType.Damage;

    [Header("Grid Pattern Settings")]
    [HideInInspector] public int patternSize = 7;
    [HideInInspector] public bool[] attackPatternGrid = new bool[49];
    public int baseDamage = 1;
}