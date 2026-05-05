using UnityEngine;

public class PlayerAnimationDriver : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    
    [Header("Layer")]
    [Tooltip("The Swing layer index in the Animator Controller")]
    public int swingLayerIndex = 1;

    [Header("State Names")]
    public string forehandState = "Forehand";
    public string backhandState = "Backhand";
    public string dinkState = "Dink";
    public string blockState = "Block";
    public string lobState = "Lob";
    public string smashState = "Smash";
    public string whiffState = "Whiff";

    [Header("Blend")]
    [Tooltip("How fast to blend into the swing clip. 0 = instant")]
    public float crossFadeDuration = 0.02f;

    public void PlaySwing(SwingType type)
    {
        string stateName = type switch
        {
            SwingType.Forehand => forehandState,
            SwingType.Backhand => backhandState,
            SwingType.Dink => dinkState,
            SwingType.Block => blockState,
            SwingType.Lob => lobState,
            SwingType.Smash => smashState,
            _ => null
        };

        if (stateName == null) return;
        animator.CrossFadeInFixedTime(stateName, crossFadeDuration, swingLayerIndex);
    }
    
    public void PlayWhiff()
    {
        Debug.Log("[PlayerAnimationDriver] - PlayWhiff() called");
        // animator.CrossFadeInFixedTime(whiffState, crossFadeDuration, swingLayerIndex);
    }
}
