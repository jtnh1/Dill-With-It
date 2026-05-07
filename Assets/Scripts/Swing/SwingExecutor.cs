using UnityEngine;

public class SwingExecutor : MonoBehaviour
{
    [Header("Forehand / Backhand")]
    public float forehandForce = 18f;
    public float backhandForce = 16f;
    [Tooltip("Fixed upward component on forehand/backhand — gives arc without stealing forward velocity.")]
    public float hitLift = 9f;

    [Header("Dink")]
    public float dinkForce = 7f;
    [Tooltip("Upward component on dink — enough arc to clear the net from the kitchen line.")]
    public float dinkLift = 9f;

    [Header("Lob")]
    public float lobForce = 5f;
    [Tooltip("Large upward component — ball goes very high and lands deep.")]
    public float lobUpward = 20f;

    [Header("Smash")]
    public float smashForce = 35f;
    [Tooltip("Tiny upward nudge so the smash clears the net; gravity does the rest.")]
    public float smashLift = 3f;

    [Header("Block")]
    public float blockForce = 15f;
    [Tooltip("Upward component added to block so the return clears the net.")]
    public float blockLift = 4f;

    [Header("Serve")]
    public float serveMinForce = 17f;
    public float serveMaxForce = 30f;
    public float serveLift = 8f;

    [Header("References")]
    public StaminaSystem stamina;

    // aimDir: optional horizontal direction override (XZ plane, already normalised).
    // Pass Vector3.zero (default) to fall back to playerTransform.forward.
    public void Execute(SwingType type, BallController ball, Transform playerTransform, float forceScale = 1f, Vector3 aimDir = default)
    {
        if (type == SwingType.None || ball == null) return;

        Vector3 fwd = (aimDir.sqrMagnitude > 0.01f)
            ? new Vector3(aimDir.x, 0f, aimDir.z).normalized
            : new Vector3(playerTransform.forward.x, 0f, playerTransform.forward.z).normalized;

        Vector3 force = Vector3.zero;

        switch (type)
        {
            case SwingType.Forehand:
                force = fwd * forehandForce + Vector3.up * hitLift;
                break;

            case SwingType.Backhand:
                force = fwd * backhandForce + Vector3.up * hitLift;
                break;

            case SwingType.Dink:
                force = fwd * dinkForce + Vector3.up * dinkLift;
                break;

            case SwingType.Lob:
                // Slow forward, big upward — high arc that lands deep in opponent's court.
                force = fwd * lobForce + Vector3.up * lobUpward;
                break;

            case SwingType.Smash:
                // Minimal upward nudge ensures the smash clears the net; gravity brings it down.
                stamina?.ConsumeSmash();
                force = fwd * smashForce + Vector3.up * smashLift;
                break;

            case SwingType.Block:
            {
                // Reflect incoming ball off the player's forward plane.
                // If ball is nearly stationary, just push it forward.
                Vector3 incoming = ball.GetVelocity();
                Vector3 blockDir = incoming.sqrMagnitude > 1f
                    ? Vector3.Reflect(incoming.normalized, playerTransform.forward)
                    : fwd;

                Vector3 blockFlat = new Vector3(blockDir.x, 0f, blockDir.z).normalized;
                force = blockFlat * blockForce + Vector3.up * blockLift;
                break;
            }
        }

        ball.ApplyForce(force * forceScale);
    }

    public void ExecuteServe(BallController ball, Transform playerTransform, float normalizedPower, Vector3 aimDir = default)
    {
        if (ball == null) return;

        Vector3 fwd = (aimDir.sqrMagnitude > 0.01f)
            ? new Vector3(aimDir.x, 0f, aimDir.z).normalized
            : new Vector3(playerTransform.forward.x, 0f, playerTransform.forward.z).normalized;

        float force = Mathf.Lerp(serveMinForce, serveMaxForce, Mathf.Clamp01(normalizedPower));
        ball.ApplyForce(fwd * force + Vector3.up * serveLift);
    }
}
