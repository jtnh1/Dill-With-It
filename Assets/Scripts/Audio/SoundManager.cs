using UnityEngine;
using UnityEngine.SceneManagement;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    public const string MainMenuMusicMutedKey = "MainMenuMusicMuted";

    [Header("Music")]
    public AudioSource musicSource;
    public AudioClip mainMenuMusic;
    public AudioClip ambientLoop;

    [Header("SFX")]
    public AudioSource sfxSource;
    public AudioClip swingClip;
    // public AudioClip ballBounceClip;
    // public AudioClip pointScoredClip;
    // public AudioClip gameWinClip;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        ApplySceneMusic(SceneManager.GetActiveScene().name);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneMusic(scene.name);
    }

    void ApplySceneMusic(string sceneName)
    {
        bool isMainMenu = sceneName == "MainMenu";
        PlayMusic(isMainMenu ? mainMenuMusic : ambientLoop);
        ApplyMainMenuMusicMute(isMainMenu);
    }

    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;
        if (musicSource.clip == clip) return;
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public bool IsMainMenuMusicMuted()
    {
        return PlayerPrefs.GetInt(MainMenuMusicMutedKey, 0) == 1;
    }

    public bool ToggleMainMenuMusic()
    {
        bool muted = !IsMainMenuMusicMuted();
        SetMainMenuMusicMuted(muted);
        return muted;
    }

    public void SetMainMenuMusicMuted(bool muted)
    {
        PlayerPrefs.SetInt(MainMenuMusicMutedKey, muted ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMainMenuMusicMute(SceneManager.GetActiveScene().name == "MainMenu");
    }

    void ApplyMainMenuMusicMute(bool isMainMenu)
    {
        if (musicSource == null) return;
        musicSource.mute = isMainMenu && IsMainMenuMusicMuted();
    }

    public void PlaySwingSound() { if (swingClip) sfxSource.PlayOneShot(swingClip); }
    // public void PlayBounceSound() { if (ballBounceClip) sfxSource.PlayOneShot(ballBounceClip); }
    // public void PlayPointSound() { if (pointScoredClip) sfxSource.PlayOneShot(pointScoredClip); }
    // public void PlayGameWinSound() { if (gameWinClip) sfxSource.PlayOneShot(gameWinClip); }
}
