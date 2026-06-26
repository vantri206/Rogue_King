using UnityEngine;

/// <summary>
/// Lightweight audio manager for King Online.
/// - Put one instance in MenuScene or PlayScene to control background music and shared SFX volume.
/// - If no instance exists, static SFX calls fall back to AudioSource.PlayClipAtPoint so old prefabs still work.
/// </summary>
public class GameAudioManager : MonoBehaviour
{
    public static GameAudioManager Instance { get; private set; }

    [Header("Lifetime")]
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool replacePreviousInstance = false;

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField] private bool playMusicOnStart = true;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.45f;
    [SerializeField] private bool loopMusic = true;

    [Header("SFX")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (!replacePreviousInstance)
            {
                Destroy(gameObject);
                return;
            }

            Destroy(Instance.gameObject);
        }

        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        EnsureSources();
        ApplyVolumes();
    }

    private void Start()
    {
        if (playMusicOnStart && backgroundMusic != null)
            PlayMusic(backgroundMusic, true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnValidate()
    {
        ApplyVolumes();
    }

    private void EnsureSources()
    {
        if (musicSource == null)
        {
            GameObject musicObject = new GameObject("MusicSource");
            musicObject.transform.SetParent(transform, false);
            musicSource = musicObject.AddComponent<AudioSource>();
        }

        if (sfxSource == null)
        {
            GameObject sfxObject = new GameObject("SfxSource");
            sfxObject.transform.SetParent(transform, false);
            sfxSource = sfxObject.AddComponent<AudioSource>();
        }

        musicSource.playOnAwake = false;
        musicSource.loop = loopMusic;
        musicSource.spatialBlend = 0f;

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
    }

    private void ApplyVolumes()
    {
        if (musicSource != null)
            musicSource.volume = musicVolume;

        if (sfxSource != null)
            sfxSource.volume = sfxVolume;
    }

    public void PlayConfiguredMusic()
    {
        if (backgroundMusic != null)
            PlayMusic(backgroundMusic, true);
    }

    public void PlayMusic(AudioClip clip, bool restartIfSameClip = false)
    {
        if (clip == null)
            return;

        EnsureSources();

        if (!restartIfSameClip && musicSource.clip == clip && musicSource.isPlaying)
            return;

        musicSource.clip = clip;
        musicSource.loop = loopMusic;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        ApplyVolumes();
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        ApplyVolumes();
    }

    public void PlaySfxOneShot(AudioClip clip)
    {
        if (clip == null)
            return;

        EnsureSources();
        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    public void PlaySfxAtPoint(AudioClip clip, Vector3 worldPosition, float spatialBlend = 0f)
    {
        if (clip == null)
            return;

        if (spatialBlend <= 0f)
        {
            PlaySfxOneShot(clip);
            return;
        }

        GameObject tempObject = new GameObject("OneShotSfx");
        tempObject.transform.position = worldPosition;

        AudioSource source = tempObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = sfxVolume;
        source.spatialBlend = Mathf.Clamp01(spatialBlend);
        source.Play();

        Destroy(tempObject, clip.length + 0.05f);
    }

    public static void PlaySfx(AudioClip clip)
    {
        if (clip == null)
            return;

        if (Instance != null)
            Instance.PlaySfxOneShot(clip);
        else
            AudioSource.PlayClipAtPoint(clip, Vector3.zero, 1f);
    }

    public static void PlaySfx(AudioClip clip, Vector3 worldPosition, float spatialBlend = 0f)
    {
        if (clip == null)
            return;

        if (Instance != null)
            Instance.PlaySfxAtPoint(clip, worldPosition, spatialBlend);
        else
            AudioSource.PlayClipAtPoint(clip, worldPosition, 1f);
    }
}
