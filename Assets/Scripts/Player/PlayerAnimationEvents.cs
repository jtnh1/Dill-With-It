using UnityEngine;

public class PlayerAnimationEvents : MonoBehaviour
{
    public SwingExecutor executor;
    public PlayerController controller;

    public void OnSwingHitFrame()
    {
        // Contact audio is played only from confirmed hit paths in PlayerController/AIBot.
    }
}
