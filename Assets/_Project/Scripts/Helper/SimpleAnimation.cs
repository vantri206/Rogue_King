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

    private SpriteRenderer spriteRenderer;
    private float timer;
    private int currentFrameIndex;
    private bool isPlaying = true;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
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
        float frameInterval = 1f / framesPerSecond;

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
}