using UnityEngine;

public class PlayerAnimationEvents : MonoBehaviour
{
    public SwingExecutor executor;
    public PlayerController controller;

    public void OnSwingHitFrame()
    {
        // Visual Feedback - VFX already fired in SwingExecutor
        // Can hook additional sounds here
        SoundManager.Instance?.PlaySwingSound();
    }
}