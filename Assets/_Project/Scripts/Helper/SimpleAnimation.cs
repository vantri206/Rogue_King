using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SimpleAnimation : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 12f;
    [SerializeField] private bool isLooping = false;
    [SerializeField] private bool isDestroyed = true;
    [SerializeField] private float lifeTime = 0f;

    [Header("SFX")]
    [Tooltip("One-shot sound played when this simple sprite effect starts. Use this for explosion, hit, death, buff, skill cast effects, etc.")]
    [SerializeField] private AudioClip startSfx;
    [SerializeField, Range(0f, 1f)] private float sfxSpatialBlend = 0f;
    [SerializeField] private bool playSfxOnStart = true;
    [SerializeField] private bool playSfxOnlyOnce = true;

    private SpriteRenderer spriteRenderer;
    private float timer;
    private int currentFrameIndex;
    private bool isPlaying = true;
    private bool hasPlayedStartSfx;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        if (!playSfxOnStart)
            return;

        if (playSfxOnlyOnce && hasPlayedStartSfx)
            return;

        PlayStartSfx();
    }

    private void Start()
    {
        if (frames == null || frames.Length == 0)
        {
            isPlaying = false;
            Debug.LogWarning($"[SimpleSpriteAnimation] No frames assigned to {gameObject.name}");
            return;
        }

        spriteRenderer.sprite = frames[0];

        if (lifeTime > 0f)
        {
            Destroy(gameObject, lifeTime);
        }
    }

    private void Update()
    {
        if (!isPlaying) return;

        timer += Time.deltaTime;
        float frameInterval = 1f / Mathf.Max(1f, framesPerSecond);

        if (timer >= frameInterval)
        {
            timer -= frameInterval;
            currentFrameIndex++;

            if (currentFrameIndex >= frames.Length)
            {
                if (isLooping)
                {
                    currentFrameIndex = 0;
                }
                else
                {
                    currentFrameIndex = frames.Length - 1;
                    isPlaying = false;

                    if (isDestroyed)
                    {
                        Destroy(gameObject);
                    }
                }
            }

            spriteRenderer.sprite = frames[currentFrameIndex];
        }
    }

    public void ReplayFromStart(bool replaySfx = true)
    {
        timer = 0f;
        currentFrameIndex = 0;
        isPlaying = true;

        if (spriteRenderer != null && frames != null && frames.Length > 0)
            spriteRenderer.sprite = frames[0];

        if (replaySfx)
        {
            hasPlayedStartSfx = false;
            PlayStartSfx();
        }
    }

    private void PlayStartSfx()
    {
        if (startSfx == null)
            return;

        hasPlayedStartSfx = true;
        GameAudioManager.PlaySfx(startSfx, transform.position, sfxSpatialBlend);
    }
}
