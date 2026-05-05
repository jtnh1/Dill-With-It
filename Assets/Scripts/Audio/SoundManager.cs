using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

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
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        PlayMusic(scene == "MainMenu" ? mainMenuMusic : ambientLoop);
    }

    public void PlayMusic(AudioClip clip)
    {
        if (musicSource.clip == clip) return;
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void PlaySwingSound() { if (swingClip) sfxSource.PlayOneShot(swingClip); }
    // public void PlayBounceSound() { if (ballBounceClip) sfxSource.PlayOneShot(ballBounceClip); }
    // public void PlayPointSound() { if (pointScoredClip) sfxSource.PlayOneShot(pointScoredClip); }
    // public void PlayGameWinSound() { if (gameWinClip) sfxSource.PlayOneShot(gameWinClip); }
}