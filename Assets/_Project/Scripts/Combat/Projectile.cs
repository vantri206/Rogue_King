using System;
using System.Collections;
using UnityEngine;

public enum WeaponTrajectory
{
    Direct,
    Arc
}

public class Projectile : MonoBehaviour
{
    [Header("Flight Settings")]
    [SerializeField] private WeaponTrajectory trajectory = WeaponTrajectory.Direct;
    [SerializeField] private float speed = 15f;
    [SerializeField] private float arcHeight = 2f;

    [Header("Visual Behaviours")]
    [SerializeField] private bool spinWhileFlying = false;
    [SerializeField] private float spinSpeed = 360f;

    [Header("Explosion Settings")]
    [SerializeField] private float explosionDelay = 0f;
    [SerializeField] private GameObject explosionPrefab;

    [Header("SFX")]
    [Tooltip("Default one-shot sound when this projectile starts flying. Can be overridden by WeaponData.projectileLaunchSfx.")]
    [SerializeField] private AudioClip launchSfx;
    [Tooltip("Default one-shot sound when this projectile reaches target. Can be overridden by WeaponData.projectileImpactSfx.")]
    [SerializeField] private AudioClip impactSfx;
    [SerializeField, Range(0f, 1f)] private float sfxSpatialBlend = 0f;

    private Vector3 startPos;
    private Vector3 targetPos;
    private Action onHitCallback;

    private AudioClip overrideLaunchSfx;
    private AudioClip overrideImpactSfx;

    private float journeyLength;
    private float startTime;
    private bool hasReachedTarget = false;

    public void Initialize(Vector3 start, Vector3 target, Action hitCallback)
    {
        Initialize(start, target, hitCallback, null, null, -1f);
    }

    public void Initialize(Vector3 start, Vector3 target, Action hitCallback, AudioClip launchSfxOverride, AudioClip impactSfxOverride, float spatialBlendOverride = -1f)
    {
        startPos = start;
        targetPos = target;
        onHitCallback = hitCallback;

        overrideLaunchSfx = launchSfxOverride;
        overrideImpactSfx = impactSfxOverride;

        if (spatialBlendOverride >= 0f)
            sfxSpatialBlend = Mathf.Clamp01(spatialBlendOverride);

        transform.position = startPos;
        startTime = Time.time;
        journeyLength = Vector3.Distance(startPos, targetPos);
        hasReachedTarget = false;

        if (!spinWhileFlying && trajectory == WeaponTrajectory.Direct)
        {
            Vector3 direction = (target - start).normalized;
            if (direction != Vector3.zero)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0, 0, angle);
            }
        }

        PlayLaunchSfx();

        if (journeyLength <= 0.0001f)
        {
            hasReachedTarget = true;
            StartCoroutine(ExplodeRoutine());
        }
    }

    private void Update()
    {
        if (journeyLength == 0 || hasReachedTarget) return;

        float distCovered = (Time.time - startTime) * speed;
        float fractionOfJourney = distCovered / journeyLength;

        if (trajectory == WeaponTrajectory.Direct)
        {
            transform.position = Vector3.Lerp(startPos, targetPos, fractionOfJourney);
        }
        else if (trajectory == WeaponTrajectory.Arc)
        {
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, fractionOfJourney);
            currentPos.y += Mathf.Sin(fractionOfJourney * Mathf.PI) * arcHeight;
            transform.position = currentPos;
        }

        if (spinWhileFlying)
        {
            transform.Rotate(0, 0, spinSpeed * Time.deltaTime);
        }

        if (fractionOfJourney >= 1f)
        {
            hasReachedTarget = true;
            StartCoroutine(ExplodeRoutine());
        }
    }

    private IEnumerator ExplodeRoutine()
    {
        transform.position = targetPos;

        if (explosionDelay > 0)
        {
            yield return new WaitForSeconds(explosionDelay);
        }

        PlayImpactSfx();

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, targetPos, Quaternion.identity);
        }

        onHitCallback?.Invoke();
        Destroy(gameObject);
    }

    private void PlayLaunchSfx()
    {
        AudioClip clip = overrideLaunchSfx != null ? overrideLaunchSfx : launchSfx;
        if (clip != null)
            GameAudioManager.PlaySfx(clip, transform.position, sfxSpatialBlend);
    }

    private void PlayImpactSfx()
    {
        AudioClip clip = overrideImpactSfx != null ? overrideImpactSfx : impactSfx;
        if (clip != null)
            GameAudioManager.PlaySfx(clip, targetPos, sfxSpatialBlend);
    }
}
