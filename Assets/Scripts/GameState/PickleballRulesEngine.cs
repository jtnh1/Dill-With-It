using System.Collections;
using Mirror;
using UnityEngine;

/*
Enforces singles pickleball rules via a state machine:
  PreServe → FirstBounce → SecondBounce → Rally → ResultOfRound → PreServe
Two-bounce rule is enforced structurally by the state sequence.
*/

public class PickleballRulesEngine : MonoBehaviour
{
    public static PickleballRulesEngine Instance { get; private set; }

    [Header("Score")]
    public int playerAScore = 0;
    public int playerBScore = 0;
    public int winningScore = 11;
    public int mustWinBy = 2;

    [Header("Ball")]
    public BallController ball;
    [Tooltip("Seconds to wait after a point before resetting the ball and transitioning to PreServe.")]
    public float ballResetDelay = 2f;
    [Tooltip("Y height and Z depth for Player A's serve position. X is computed from score.")]
    public Vector3 servePositionA = new Vector3(0f, 1.2f, -24.5f);
    [Tooltip("Y height and Z depth for Player B's serve position. X is computed from score.")]
    public Vector3 servePositionB = new Vector3(0f, 1.2f,  24.5f);
    [Tooltip("How far left/right of centre the ball is placed for each serve side.")]
    public float serveXOffset = 5f;

    // Companion states
    public LastHitState LastHit        { get; private set; } = LastHitState.None;
    public BallBounceCountState BounceCount { get; private set; } = BallBounceCountState.Zero;

    // Who is currently serving: 0 = Player A, 1 = Player B
    public int ServingPlayer { get; private set; } = 0;

    // Side that last absorbed a bounce in Rally (0 = A's side, 1 = B's side, -1 = none yet)
    private int lastBounceSide = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (ball == null) ball = FindAnyObjectByType<BallController>();
        if (ball != null) PlaceBallAtServePosition();
    }

    // ── Entry points ──────────────────────────────────────────────────────────

    // Fired whenever a paddle makes contact. Subscribers receive the player index (0 or 1).
    public event System.Action<int> OnHit;

    // Call after every successful paddle contact (player index: 0 = Player A, 1 = Player B).
    public void OnBallHit(int playerIndex)
    {
        if (NetworkClient.active && !NetworkServer.active) return; // clients don't run rules
        Debug.Log($"[OnBallHit] ----- Player = {playerIndex}");
        LastHit = playerIndex == 0 ? LastHitState.PlayerA : LastHitState.PlayerB;
        OnHit?.Invoke(playerIndex);

        // Serving player hitting in SecondBounce transitions to free Rally.
        if (GameStateManager.Instance.CurrentState == GameState.SecondBounce
            && playerIndex == ServingPlayer)
        {
            lastBounceSide = -1;
            GameStateManager.Instance.TransitionTo(GameState.Rally);
        }
    }

    public void OnBallBounced(ZoneID zone, int lastHitByPlayer)
    {
        if (NetworkClient.active && !NetworkServer.active) return;
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;

        switch (state)
        {
            case GameState.PreServe:
                Debug.Log($"[OnBallBounced] State ----> PreServe ||||||||| Zone ----> {zone}");
                HandleServeBounce(zone);
                break;

            case GameState.FirstBounce:
                Debug.Log($"[OnBallBounced] State ----> FirstBounce ||||||||| Zone ----> {zone}");
                HandleFirstBounceReturn(zone, lastHitByPlayer);
                break;

            case GameState.SecondBounce:
                // Server let the ball bounce a second time without hitting it.
                AwardPoint(OpponentOf(ServingPlayer), "Server failed to return the ball.");
                break;

            case GameState.Rally:
                HandleRallyBounce(zone, lastHitByPlayer);
                break;
        }
    }

    public void OnBallHitNet(int lastHitByPlayer)
    {
        if (NetworkClient.active && !NetworkServer.active) return;
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;
        AwardPoint(OpponentOf(lastHitByPlayer), "Ball hit the net.");
    }

    // ── State-specific bounce handlers ─────────────────────────────────────────

    void HandleServeBounce(ZoneID zone)
    {
        if (zone == ZoneID.OutOfBounds)
        {
            Debug.Log($"[HandleServeBounce] ||||||| zone = {zone}");
            AwardPoint(OpponentOf(ServingPlayer), "Serve landed out of bounds.");
            return;
        }
        if (!IsValidServeZone(zone, ServingPlayer))
        {
            Debug.Log($"[HandleServeBounce] ||||||| zone = {zone}");
            AwardPoint(OpponentOf(ServingPlayer), "Serve landed in wrong service box.");
            return;
        }
        BounceCount    = BallBounceCountState.One;
        lastBounceSide = SideOf(zone);
        GameStateManager.Instance.TransitionTo(GameState.FirstBounce);
    }

    void HandleFirstBounceReturn(ZoneID zone, int lastHitByPlayer)
    {
        Debug.Log($"[HandleFirstBounceReturn] ||||||| zone = {zone} ||||||| lastHitByPlayer = {lastHitByPlayer}");
        if (zone == ZoneID.OutOfBounds)
        {
            // Receiver failed to keep the ball in — always faults the receiver regardless of who last touched it.
            AwardPoint(ServingPlayer, "Receiver did not return the serve.");
            return;
        }

        int side = SideOf(zone);

        if (side == lastBounceSide)
        {
            // Ball double-bounced on the receiver's side before they returned it.
            AwardPoint(ServingPlayer, "Receiver let ball bounce twice.");
            return;
        }

        // Ball is on the server's side — only valid if the non-serving player actually hit it.
        // Without this check, physics bounciness can carry the ball back across without a hit.
        int nonServer = OpponentOf(ServingPlayer);
        LastHitState nonServerHit = nonServer == 0 ? LastHitState.PlayerA : LastHitState.PlayerB;
        if (LastHit != nonServerHit)
        {
            AwardPoint(ServingPlayer, "Receiver did not return the serve.");
            return;
        }

        // Two-bounce rule satisfied.
        BounceCount    = BallBounceCountState.Two;
        lastBounceSide = side;
        GameStateManager.Instance.TransitionTo(GameState.SecondBounce);
    }

    void HandleRallyBounce(ZoneID zone, int lastHitByPlayer)
    {
        if (zone == ZoneID.OutOfBounds)
        {
            AwardPoint(OpponentOf(lastHitByPlayer), "Ball landed out of bounds.");
            return;
        }

        int side = SideOf(zone);

        if (side == lastBounceSide)
        {
            // Double bounce on the same side — the owning player faulted.
            AwardPoint(OpponentOf(side), "Ball bounced twice on the same side.");
            return;
        }

        lastBounceSide = side;
    }

    // ── Scoring ────────────────────────────────────────────────────────────────

    public void AwardPoint(int player, string reason)
    {
        if (player == ServingPlayer)
        {
            if (player == 0) playerAScore++;
            else             playerBScore++;
        }
        else
        {
            ServingPlayer = OpponentOf(ServingPlayer); // side-out
        }

        Debug.Log($"[Rules] {reason} | Score: A={playerAScore} B={playerBScore} | Serving: {ServingPlayer}");
        UIManager.Instance?.UpdateScoreBoard(playerAScore, playerBScore, ServingPlayer);
        UIManager.Instance?.ShowAnnouncement(reason);

        // Broadcast score and announcement to clients in multiplayer.
        if (NetworkServer.active)
            NetworkGameManager.Instance?.BroadcastScore(playerAScore, playerBScore, ServingPlayer, reason);

        GameStateManager.Instance.TransitionTo(GameState.ResultOfRound);

        if (IsGameOver())
            GameStateManager.Instance.TransitionTo(GameState.GameOver);
        else
            StartCoroutine(DelayedReset());
    }

    IEnumerator DelayedReset()
    {
        yield return new WaitForSeconds(ballResetDelay);
        ResetRally();
    }

    public void ResetRally()
    {
        LastHit        = LastHitState.None;
        BounceCount    = BallBounceCountState.Zero;
        lastBounceSide = -1;
        PlaceBallAtServePosition();
        GameStateManager.Instance.TransitionTo(GameState.PreServe);
    }

    public void ResetMatch()
    {
        StopAllCoroutines();

        playerAScore = 0;
        playerBScore = 0;
        ServingPlayer = 0;
        LastHit = LastHitState.None;
        BounceCount = BallBounceCountState.Zero;
        lastBounceSide = -1;

        UIManager.Instance?.UpdateScoreBoard(playerAScore, playerBScore, ServingPlayer);

        if (NetworkServer.active)
            NetworkGameManager.Instance?.BroadcastScore(playerAScore, playerBScore, ServingPlayer, string.Empty);

        PlaceBallAtServePosition();
        GameStateManager.Instance?.TransitionTo(GameState.PreServe);
    }

    public void RegisterBall(BallController newBall, bool placeAtServePosition = true)
    {
        if (newBall == null) return;

        ball = newBall;
        if (placeAtServePosition)
            PlaceBallAtServePosition();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    void PlaceBallAtServePosition()
    {
        if (ball == null) return;

        Vector3 pos;
        if (ServingPlayer == 0)
        {
            float x = (playerAScore % 2 == 0) ? serveXOffset : -serveXOffset;
            pos = new Vector3(x, servePositionA.y, servePositionA.z);
        }
        else
        {
            float x = (playerBScore % 2 == 0) ? -serveXOffset : serveXOffset;
            pos = new Vector3(x, servePositionB.y, servePositionB.z);
        }

        Debug.Log($"[PlaceBall] Placing at {pos} | server={NetworkServer.active} | ball={ball != null}");
        ball.ResetToPosition(pos);
    }

    bool IsValidServeZone(ZoneID zone, int server)
    {
        // Diagonal serve rule: even score → serve from right, must land in opponent's right box.
        if (server == 0)
        {
            ZoneID target = (playerAScore % 2 == 0) ? ZoneID.Zone_B_Right : ZoneID.Zone_B_Left;
            return zone == target;
        }
        else
        {
            ZoneID target = (playerBScore % 2 == 0) ? ZoneID.Zone_A_Right : ZoneID.Zone_A_Left;
            return zone == target;
        }
    }

    // Returns which player's side a zone belongs to: 0 = Player A, 1 = Player B
    int SideOf(ZoneID zone)
    {
        switch (zone)
        {
            case ZoneID.Zone_A_Kitchen:
            case ZoneID.Zone_A_Serve_Left:
            case ZoneID.Zone_A_Serve_Right:
            case ZoneID.Zone_A_Left:
            case ZoneID.Zone_A_Right:
                return 0;
            default:
                return 1;
        }
    }

    int  OpponentOf(int player) => player == 0 ? 1 : 0;

    bool IsGameOver()
    {
        int max = Mathf.Max(playerAScore, playerBScore);
        int min = Mathf.Min(playerAScore, playerBScore);
        return max >= winningScore && (max - min) >= mustWinBy;
    }
}
