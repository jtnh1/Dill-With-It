using UnityEngine;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }

    [Header("Particle System")]
    public ParticleSystem swingBurstVFX;
    public ParticleSystem rallyWinVFX;
    public ParticleSystem gameWinVFX;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void PlaySwingBurst(Vector3 position)
    {
        if (!swingBurstVFX) return;
        swingBurstVFX.transform.position = position;
        swingBurstVFX.Play();
    }

    public void PlayRallyWin() => rallyWinVFX?.Play();
    public void PlayGameWin() => gameWinVFX?.Play();
}
