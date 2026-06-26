using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SimpleUIImageAnimation : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Image targetImage;

    [Header("Animation")]
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 12f;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private bool hideWhenFinished = false;
    [SerializeField] private bool setNativeSizeEachFrame = false;

    [Header("Start")]
    [SerializeField] private bool randomStartFrame = false;

    [Header("SFX")]
    [Tooltip("One-shot UI sound played when this UI animation starts.")]
    [SerializeField] private AudioClip startSfx;
    [SerializeField] private bool playSfxWhenAnimationStarts = true;
    [SerializeField] private bool playSfxOnlyOncePerEnable = false;

    private int currentFrame;
    private float timer;
    private bool isPlaying;
    private bool hasPlayedSfxThisEnable;

    public bool IsPlaying => isPlaying;
    public int CurrentFrame => currentFrame;

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        hasPlayedSfxThisEnable = false;

        if (playOnEnable)
            PlayFromStart();
    }

    private void Update()
    {
        if (!isPlaying)
            return;

        if (frames == null || frames.Length == 0)
            return;

        if (framesPerSecond <= 0f)
            return;

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        timer += deltaTime;

        float frameDuration = 1f / framesPerSecond;

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            NextFrame();
        }
    }

    public void Play()
    {
        if (frames == null || frames.Length == 0)
            return;

        isPlaying = true;

        if (targetImage != null)
            targetImage.enabled = true;

        PlayStartSfxIfNeeded();
        ApplyFrame();
    }

    public void PlayFromStart()
    {
        if (frames == null || frames.Length == 0)
            return;

        timer = 0f;
        currentFrame = randomStartFrame ? Random.Range(0, frames.Length) : 0;

        isPlaying = true;

        if (targetImage != null)
            targetImage.enabled = true;

        PlayStartSfxIfNeeded();
        ApplyFrame();
    }

    public void Stop()
    {
        isPlaying = false;
    }

    public void StopAndHide()
    {
        isPlaying = false;

        if (targetImage != null)
            targetImage.enabled = false;
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Resume()
    {
        if (frames == null || frames.Length == 0)
            return;

        isPlaying = true;

        if (targetImage != null)
            targetImage.enabled = true;
    }

    public void SetFrame(int frameIndex)
    {
        if (frames == null || frames.Length == 0)
            return;

        currentFrame = Mathf.Clamp(frameIndex, 0, frames.Length - 1);
        ApplyFrame();
    }

    public void SetFrames(Sprite[] newFrames, bool playImmediately = true)
    {
        frames = newFrames;
        currentFrame = 0;
        timer = 0f;

        if (playImmediately)
            PlayFromStart();
        else
            ApplyFrame();
    }

    private void NextFrame()
    {
        currentFrame++;

        if (currentFrame >= frames.Length)
        {
            if (loop)
            {
                currentFrame = 0;
            }
            else
            {
                currentFrame = frames.Length - 1;
                ApplyFrame();

                isPlaying = false;

                if (hideWhenFinished && targetImage != null)
                    targetImage.enabled = false;

                return;
            }
        }

        ApplyFrame();
    }

    private void ApplyFrame()
    {
        if (targetImage == null)
            return;

        if (frames == null || frames.Length == 0)
            return;

        Sprite frame = frames[currentFrame];

        if (frame == null)
            return;

        targetImage.sprite = frame;

        if (setNativeSizeEachFrame)
            targetImage.SetNativeSize();
    }

    private void PlayStartSfxIfNeeded()
    {
        if (!playSfxWhenAnimationStarts || startSfx == null)
            return;

        if (playSfxOnlyOncePerEnable && hasPlayedSfxThisEnable)
            return;

        hasPlayedSfxThisEnable = true;
        GameAudioManager.PlaySfx(startSfx);
    }
}
