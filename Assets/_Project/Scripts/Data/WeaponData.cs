using System.Collections.Generic;
using UnityEngine;

public enum WeaponVFXProjectileMode
{
    Auto,
    SingleToSelectedTarget,
    MultiToAffectedTiles,
    MultiToMaxRangeTiles
}

public enum WeaponSpecialType
{
    None,
    HiddenMine
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

    [Header("Cooldown")]
    [Tooltip("Cooldown tính theo số lượt KingTurn của player dùng vũ khí. 0 = không cooldown.")]
    public int cooldownTurns = 0;

    [Header("Special Weapon Behaviour")]
    [Tooltip("None = gây damage ngay như vũ khí cũ. HiddenMine = bắn/đặt mìn ẩn, không gây damage ngay; mìn nổ khi quân Chess Alliance đi qua/đứng lên ô đó.")]
    public WeaponSpecialType specialType = WeaponSpecialType.None;

    [Tooltip("Chỉ dùng cho HiddenMine. Nếu bật, chỉ cho đặt mìn vào ô đang trống để tránh nổ/đè lên quân ngay lúc đặt.")]
    public bool hiddenMineRequireEmptyTarget = true;

    [Tooltip("Chỉ dùng cho HiddenMine. Damage gây ra khi mìn nổ. Nếu <= 0 thì dùng Base Damage.")]
    public int hiddenMineDamage = 0;

    [Tooltip("Chỉ dùng cho HiddenMine. Bán kính AoE theo Chebyshev distance. 0 = chỉ ô mìn, 1 = 3x3 quanh mìn.")]
    public int hiddenMineAOERange = 1;

    [Tooltip("Chỉ dùng cho HiddenMine. Nếu gán prefab này, khi mìn nổ sẽ spawn VFX ở ô mìn. Nếu để trống sẽ dùng Destroyed Effect Prefab/default hit effect.")]
    public GameObject hiddenMineExplosionPrefab;

    [Tooltip("Chỉ dùng cho HiddenMine. Âm phát tại ô mìn khi nổ. Nếu để trống có thể dùng âm trên prefab SimpleAnimation.")]
    public AudioClip hiddenMineExplosionSfx;

    [HideInInspector] public List<Vector2Int> targetingPattern = new List<Vector2Int>();
    [HideInInspector] public List<Vector2Int> effectPattern = new List<Vector2Int>();

    [Header("VFX")]
    public GameObject gunPrefab;
    public GameObject projectilePrefab;

    [Header("SFX")]
    [Tooltip("One-shot sound when this weapon/skill starts resolving. Good for skill-cast or weapon-use sound.")]
    public AudioClip useSfx;

    [Tooltip("One-shot sound at the moment the gun/weapon fires. If null, projectile prefab SFX can still play.")]
    public AudioClip fireSfx;

    [Tooltip("Optional override launch sound passed to Projectile.Initialize. If null, the Projectile prefab's own launchSfx is used.")]
    public AudioClip projectileLaunchSfx;

    [Tooltip("Optional override impact sound passed to Projectile.Initialize. If null, the Projectile prefab's own impactSfx is used, or the explosion prefab SimpleAnimation can play its own SFX.")]
    public AudioClip projectileImpactSfx;

    [Tooltip("Optional sound played when the damage/hit/death effect is spawned on a piece. If null, put SFX directly on the effect prefab's SimpleAnimation instead.")]
    public AudioClip damageEffectSfx;

    [Tooltip("Spatial blend for world SFX. 0 = 2D, 1 = fully 3D. For this 2D game, 0 is usually safest.")]
    [Range(0f, 1f)] public float sfxSpatialBlend = 0f;

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
