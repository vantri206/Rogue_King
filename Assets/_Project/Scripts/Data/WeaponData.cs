using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Chess/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic Settings")]
    public string weaponName;
    public Sprite weaponIcon;
    public int baseDamage = 1;
    public EffectType defaultEffectType = EffectType.Damage;

    [Header("Pattern Settings")]
    [Tooltip("Check if the damage pattern needs to rotate based on the targeting direction")]
    public bool isDirectional = false;

    public bool isOriginRelative = false;

    public AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [HideInInspector] public List<Vector2Int> targetingPattern = new List<Vector2Int>();
    [HideInInspector] public List<Vector2Int> effectPattern = new List<Vector2Int>();

    [Header("VFX")]
    public GameObject gunPrefab;
    public GameObject projectilePrefab;
}