using System.Collections.Generic;
using UnityEngine;

public enum WeaponVFXProjectileMode
{
    Auto,
    SingleToSelectedTarget,
    MultiToAffectedTiles,
    MultiToMaxRangeTiles
}

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

    [Tooltip("Auto keeps legacy-friendly defaults: grenade/bomb/nade throws one projectile to the selected tile; shotgun/scatter/spray fires multiple projectiles toward max range tiles.")]
    public WeaponVFXProjectileMode vfxProjectileMode = WeaponVFXProjectileMode.Auto;

    [Tooltip("If enabled, this offset is added to the projectile impact point. Leave disabled to let CombatVFXManager auto-aim gun projectiles at body center and grenade-style projectiles at the tile ground.")]
    public bool overrideProjectileTargetOffset = false;

    [Tooltip("Custom world-space offset added to projectile impact point when Override Projectile Target Offset is enabled.")]
    public Vector3 projectileTargetWorldOffset = new Vector3(0f, 0.65f, 0f);

    [Tooltip("Effect prefab spawned at every piece that actually receives damage from this weapon, even if the piece survives. If null, CombatVFXManager.defaultDestroyedEffectPrefab is used.")]
    public GameObject destroyedEffectPrefab;

    [Tooltip("If enabled, this offset is used when spawning the destroyed/damage effect on a damaged piece. Leave disabled to use CombatVFXManager's body-center default.")]
    public bool overrideDestroyedEffectOffset = false;

    [Tooltip("Custom world-space offset for the destroyed/damage effect when Override Destroyed Effect Offset is enabled.")]
    public Vector3 destroyedEffectWorldOffset = new Vector3(0f, 0.55f, 0f);

    [Tooltip("Server waits this many seconds before applying damage/despawn so client VFX can finish first. Use 0 or negative for automatic estimate.")]
    public float serverDamageDelaySeconds = 0f;
}
