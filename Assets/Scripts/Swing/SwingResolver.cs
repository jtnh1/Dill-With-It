using UnityEngine;

public class SwingResolver : MonoBehaviour
{
    [Header("Thresholds")]
    public float dinkMaxBallSpeed = 5f;
    public float smashMinBallHeight = 2.5f;
    public float blockDistanceMax = 1.5f;

    // private Transform _playerTransform;
    private BallController ball;

    private void Awake()
    {
        // _playerTransform = transform;
        ball = FindAnyObjectByType<BallController>();
    }

    // Given the raw input, resolve which SwingType to execute
    // public SwingType Resolve(PlayerInputHandler input, StaminaSystem stamina)
    // {
    //     if(_ball == null) return SwingType.None;
        
    //     Vector3 ballPos = _ball.transform.position;
    //     Vector3 playerPos = _playerTransform.position;
    //     Vector3 toBall = ballPos - playerPos;
    //     float ballSpeed = _ball.CurrentSpeed;
    //     float ballHeight = ballPos.y;

    //     // Explicit Key Inputs (highest priority)
    //     if (input.LobPressed) return SwingType.Lob;
    //     if (input.DinkPressed && ballSpeed <= dinkMaxBallSpeed) return SwingType.Dink;

    //     // Smash: ball is high + stamina available
    //     bool anySwing = input.SwingForehand || input.SwingBackhand || input.SmashPressed;
    //     if (anySwing && ballHeight >= smashMinBallHeight && stamina.HasEnoughForSmash()) return SwingType.Smash;

    //     // Block: ball is directly in front
    //     Vector3 forward = _playerTransform.forward;
    //     float dot = Vector3.Dot(forward, toBall.normalized);
    //     float dist = toBall.magnitude;
    //     if (anySwing && dot > 0.85f && dist <= blockDistanceMax) return SwingType.Block;

    //     // Forehand / Backhand: based on ball's side relative to the player
    //     // Right side of player -> Forehand, Left side of player -> Backhand
    //     Vector3 right = _playerTransform.right;
    //     float sideDot = Vector3.Dot(right, toBall.normalized);
    //     if (input.SwingForehand && sideDot >= 0f) return SwingType.Forehand;
    //     if (input.SwingBackhand && sideDot < 0f) return SwingType.Backhand;

    //     // Fallback: auto detect side on any swing key
    //     if (anySwing) return sideDot >= 0f ? SwingType.Forehand : SwingType.Backhand;

    //     return SwingType.None;
    // }

    public SwingType ResolveOverride(SwingType requested, StaminaSystem stamina)
    {
        // Lob and Block are explicit player choices — always honour them exactly.
        if (requested == SwingType.Lob || requested == SwingType.Block) return requested;

        if (ball == null)
        {
            ball = FindAnyObjectByType<BallController>();
            if (ball == null) return requested;
        }

        Vector3 ballPos = ball.transform.position;
        float ballSpeed = ball.CurrentSpeed;
        float ballHeight = ballPos.y;

        Vector3 toBall = ballPos - transform.position;
        float distance = toBall.magnitude;

        // Contextual overrides (skipped for Lob/Block above)
        if (ballHeight >= smashMinBallHeight && stamina.HasEnoughForSmash()) return SwingType.Smash;

        if (requested == SwingType.Dink && ballSpeed > dinkMaxBallSpeed)
        {
            float sideDot = Vector3.Dot(transform.right, toBall.normalized);
            return sideDot >= 0f ? SwingType.Forehand : SwingType.Backhand;
        }

        if (requested == SwingType.Forehand || requested == SwingType.Backhand)
        {
            float sideDot = Vector3.Dot(transform.right, toBall.normalized);
            return sideDot >= 0f ? SwingType.Forehand : SwingType.Backhand;
        }

        return requested;
    }
}
