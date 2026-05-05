using UnityEngine;
using System.Collections;
using UnityEngine.VFX;

public class PaddleHitZone : MonoBehaviour
{
    [Header("Radii")]
    [Tooltip("Ball inside this raidus = perfect instant hint.")]
    public float perfectHitRadius = 0.35f;

    [Tooltip("Ball inside this radius = assist pull toward paddle, then hit.")]
    public float assistRadius = 0.75f;

    [Header("Assist Settings")]
    [Tooltip("Force multiplier when the ball is in the assist zone but not perfect zone")]
    [Range(0.5f, 1f)]
    public float assistForceMultiplier = 0.75f;

    // Deprecated -- too laggy
    // [Tooltip("Max time the system waits during an assit pull before giving up.")]
    // public float assistTimeout = 0.18f;

    // [Header("Whiff")]
    // [Tooltip("If the ball is beyond assist radius, play a whiff animation.")]
    // public string whiffTrigger = "Whiff";

    // Results
    public bool DidConnect { get; private set; }
    public bool WasAssisted { get; private set; }
    public float ForceScale { get; private set; }
    

    // Internal

    private BallController ball;
    // private Animator animator;
    // private bool assistInProgress = false;

    // Raised when a confirmed hit should be executed
    // public event System.Action<SwingType> OnConfirmedHit;
    
    private void Awake()
    {
        ball = FindAnyObjectByType<BallController>();
        // animator = GetComponentInParent<Animator>();

        // Visualize zones in editor
        // var sc = GetComponent<SphereCollider>();
        // if (sc) sc.radius = assistRadius;
    }

    public bool TryHit(SwingType swingType)
    {
        DidConnect = false;
        WasAssisted = false;
        ForceScale = 0f;

        if (ball == null)
        {
            ball = FindAnyObjectByType<BallController>();
            if(ball == null) return false;
        }

        float distance = Vector3.Distance(transform.position, ball.transform.position);

        if (distance <= perfectHitRadius)
        {
            DidConnect = true;
            WasAssisted = false;
            ForceScale = 1f;
            VFXManager.Instance?.PlaySwingBurst(transform.position);
            SoundManager.Instance?.PlaySwingSound();
            return true;
        }
        else if (distance <= assistRadius)
        {
            DidConnect = true;
            WasAssisted = true;

            float t = Mathf.InverseLerp(assistRadius, perfectHitRadius, distance);
            ForceScale = Mathf.Lerp(assistForceMultiplier, 1f, t);
            
            VFXManager.Instance?.PlaySwingBurst(transform.position);
            SoundManager.Instance?.PlaySwingSound();
            return true;
        }

        return false;

        // if (ball == null || assistInProgress) return false;

        // float distance = Vector3.Distance(transform.position, ball.transform.position);

        // if (distance <= perfectHitRadius)
        // {
        //     OnConfirmedHit?.Invoke(swingType);
        //     return true;
        // }
        // else if (distance <= assistRadius)
        // {
        //     StartCoroutine(AssistPull(swingType));
        //     return true;
        // }
        // else
        // {
        //     animator?.SetTrigger(whiffTrigger);
        //     return false;
        // }
    }

    // private IEnumerator AssistPull(SwingType swingType)
    // {
    //     assistInProgress = true;
    //     float elapsed = 0f;

    //     while (elapsed < assistTimeout)
    //     {
    //         if (ball == null) break;

    //         float distance = Vector3.Distance(transform.position, ball.transform.position);

    //         if (distance <= perfectHitRadius)
    //         {
    //             // Ball arrived, fire the hit
    //             OnConfirmedHit(swingType);
    //             assistInProgress = false;
    //             yield break;
    //         }

    //         // Pull ball toward paddle
    //         Vector3 dir = (transform.position - ball.transform.position).normalized;
    //         ball.GetComponent<Rigidbody>().linearVelocity = dir * assistPullSpeed;
    //         elapsed += Time.deltaTime;
    //         yield return null;
    //     }

    //     // Timed out, ball didn't arrive in time, whiff
    //     animator?.SetTrigger(whiffTrigger);
    //     assistInProgress = false;
    // }

    // private void ConfirmHit(SwingType swingType)
    // {
    //     OnConfirmedHit?.Invoke(swingType);
    //     // VFXManager.Instance?.PlaySwingBurst(transform.position);
    //     // SoundManager.Instance?.PlaySwingSound();
    // }

    // Gizmos

    private void OnDrawGizmosSelected()
    {
        // Perfect hit zone - Green
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, perfectHitRadius);

        // Assist zone - Yellow
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawSphere(transform.position, assistRadius);
    }
}
