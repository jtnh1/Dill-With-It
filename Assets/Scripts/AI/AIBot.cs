using System.Collections;
using Mirror;
using UnityEngine;

public class AIBot : MonoBehaviour
{
    public enum Difficulty { Easy, Medium, Hard }

    // ── Internal phase (drives behaviour each Update) ─────────────────────────
    enum AIPhase
    {
        Idle,          // PointScored / GameOver — stand still
        ReadyToServe,  // PreServe, AI serves — walk to baseline then serve
        Serving,       // Serve ball just launched — wait for it to bounce
        Receiving,     // PreServe, player serves — move to ready position
        WaitBounce,    // InRally after AI's serve — wait for return bounce
        Rally,         // InRally, free to chase and swing
    }

    [Header("Settings")]
    public Difficulty difficulty = Difficulty.Easy;
    public float walkSpeed = 5f;
    public float reactionDelay = 0.4f;
    public float swingCooldown = 0.6f;

    [Header("Positioning")]
    [Tooltip("Base court position when receiving or in a rally.")]
    public Vector3 basePosition = new Vector3(0f, 0f, 18f);

    [Header("Serve")]
    [Tooltip("Z and Y for the AI's serve stand position. X is computed from score (matches PickleballRulesEngine.serveXOffset).")]
    public Vector3 serveStandPosition = new Vector3(0f, 0f, 23f);
    [Tooltip("X distance from centre to the serve lane. Keep in sync with PickleballRulesEngine.serveXOffset.")]
    public float serveXOffset = 5f;
    [Tooltip("Pause before hitting the serve.")]
    public float serveWindupDelay = 1.0f;

    [Header("References")]
    public SwingExecutor swingExecutor;
    public StaminaSystem stamina;
    public Animator animator;
    public PaddleAttacher paddleAttacher;

    // ── Private ───────────────────────────────────────────────────────────────
    private BallController ball;
    private PaddleHitZone hitZone;
    private CharacterController characterController;
    private Vector3 targetPos;
    private float reactionTimer;
    private float swingCooldownTimer;
    private AIPhase phase = AIPhase.Idle;

    private static readonly int HashMoveZ        = Animator.StringToHash("MoveZ");
    private static readonly int HashSwing        = Animator.StringToHash("SwingType");
    private static readonly int HashSwingTrigger = Animator.StringToHash("Swing");

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        ball              = FindAnyObjectByType<BallController>();
        characterController = GetComponent<CharacterController>();
        if (paddleAttacher == null) paddleAttacher = GetComponent<PaddleAttacher>();

        reactionDelay = difficulty switch
        {
            Difficulty.Easy   => 0.7f,
            Difficulty.Medium => 0.4f,
            Difficulty.Hard   => 0.1f,
            _                 => 0.4f
        };
    }

    private void Start()
    {
        // No AI in multiplayer — both slots are human players.
        if (NetworkClient.active || NetworkServer.active)
        {
            gameObject.SetActive(false);
            return;
        }

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnStateChanged;

        if (PickleballRulesEngine.Instance != null)
            PickleballRulesEngine.Instance.OnHit += OnAnyBallHit;

        StartCoroutine(LateStart());
    }

    private IEnumerator LateStart()
    {
        yield return null; // wait one frame so PaddleAttacher.Start() has run

        if (paddleAttacher != null)
        {
            Transform paddle = paddleAttacher.GetPaddleTransform();
            if (paddle != null) hitZone = paddle.GetComponentInChildren<PaddleHitZone>();
        }

        // Bootstrap phase from whatever state the game is already in
        InitPhase();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;

        if (PickleballRulesEngine.Instance != null)
            PickleballRulesEngine.Instance.OnHit -= OnAnyBallHit;
    }

    // ── Hit reaction ──────────────────────────────────────────────────────────

    // Called the moment the human player makes paddle contact.
    // Starts AI movement immediately rather than waiting for the bounce.
    // Swing permission is still controlled by the state machine (WaitBounce never calls TrySwing).
    void OnAnyBallHit(int playerIndex)
    {
        if (playerIndex != 0) return; // only react to human player hits

        reactionTimer = 0f; // snap target on next Update — no delay on first sample

        // Serving: AI just served and is standing still waiting; start tracking now.
        // Receiving: player is serving; AI was drifting to base position; track instead.
        if (phase == AIPhase.Serving || phase == AIPhase.Receiving)
            phase = AIPhase.WaitBounce;
    }

    // ── State machine ─────────────────────────────────────────────────────────

    void OnStateChanged(GameState from, GameState to)
    {
        bool aiServes = PickleballRulesEngine.Instance?.ServingPlayer == 1;
        switch (to)
        {
            case GameState.PreServe:
                InitPhase();
                break;

            case GameState.FirstBounce:
                // Ball had first bounce. If AI served → ball on player's side, wait for return.
                // If player served → ball on AI's side, AI must return it.
                phase = aiServes ? AIPhase.WaitBounce : AIPhase.Rally;
                break;

            case GameState.SecondBounce:
                // Ball had second bounce. If AI served → ball on AI's side, AI must hit to rally.
                // If player served → ball on player's side, they must return; AI waits.
                if (aiServes) phase = AIPhase.Rally;
                break;

            case GameState.Rally:
                phase = AIPhase.Rally;
                break;

            case GameState.ResultOfRound:
            case GameState.GameOver:
                StopAllCoroutines();
                phase = AIPhase.Idle;
                break;
        }
    }

    // Decide phase based on the current game state (used at start-up and on PreServe).
    void InitPhase()
    {
        StopAllCoroutines();

        bool aiServes = PickleballRulesEngine.Instance?.ServingPlayer == 1;
        if (aiServes)
        {
            phase     = AIPhase.ReadyToServe;
            targetPos = CurrentServePosition();
            StartCoroutine(ServeRoutine());
        }
        else
        {
            phase     = AIPhase.Receiving;
            targetPos = CurrentServeZonePosition();
        }
    }

    public void ResetForServePosition(Vector3 position, Quaternion rotation)
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (characterController != null) characterController.enabled = controllerWasEnabled;

        targetPos = position;
        reactionTimer = 0f;
        swingCooldownTimer = 0f;
        SetMoveAnim(0f);
    }

    // Returns the serve position for the current score (Player B faces −Z: even = right = −X).
    Vector3 CurrentServePosition()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules != null)
            return rules.GetServeZonePositionForPlayer(1);

        int score = PickleballRulesEngine.Instance?.playerBScore ?? 0;
        float x = (score % 2 == 0) ? -serveXOffset : serveXOffset;
        return new Vector3(x, serveStandPosition.y, serveStandPosition.z);
    }

    Vector3 CurrentServeZonePosition()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null ? rules.GetServeZonePositionForPlayer(1) : CurrentServePosition();
    }

    // ── Zone targeting ────────────────────────────────────────────────────────

    // Zone_A landing rectangles (xMin, xMax, zMin, zMax) with 1-unit margin from edges.
    // Index 0 = Kitchen, 1 = Right, 2 = Left, 3 = Serve_Right (deep), 4 = Serve_Left (deep).
    static readonly float[,] ZoneATargets =
    {
        { -8f,   8f,  -6f,  -1f  },   // Kitchen
        {  0.5f, 9.5f, -21f, -8f  },  // Right
        { -9.5f,-0.5f, -21f, -8f  },  // Left
        {  0.5f, 9.5f, -26f, -23f },  // Serve_Right (deep)
        { -9.5f,-0.5f, -26f, -23f },  // Serve_Left  (deep)
    };

    // Random point inside the diagonal service box the rules engine expects for this score.
    Vector3 PickServeTargetPos(bool evenScore)
    {
        // Even B score → Zone_A_Right (x: 0.5–9.5, z: -8 to -21)
        // Odd  B score → Zone_A_Left  (x: -9.5–-0.5, z: -8 to -21)
        float xMin = evenScore ?  0.5f : -9.5f;
        float xMax = evenScore ?  9.5f : -0.5f;
        return new Vector3(Random.Range(xMin, xMax), 0f, Random.Range(-8f, -29f));
    }

    // Random landing spot in one of Player A's zones.
    Vector3 PickRallyTargetPos()
    {
        int i = Random.Range(0, ZoneATargets.GetLength(0));
        return new Vector3(
            Random.Range(ZoneATargets[i, 0], ZoneATargets[i, 1]),
            0f,
            Random.Range(ZoneATargets[i, 2], ZoneATargets[i, 3]));
    }

    // Horizontal direction (XZ plane, normalised) from current position toward a landing spot.
    Vector3 AimDirTo(Vector3 landingPos)
    {
        Vector3 d = landingPos - transform.position;
        d.y = 0f;
        return d.sqrMagnitude > 0.01f ? d.normalized : -transform.forward;
    }

    // ── Serve coroutine ───────────────────────────────────────────────────────

    IEnumerator ServeRoutine()
    {
        // Walk to the correct serve lane for this score
        Vector3 servePos = CurrentServePosition();
        targetPos = servePos;

        float timeout = 5f;
        while (timeout > 0f)
        {
            float flat = new Vector2(transform.position.x - servePos.x,
                                     transform.position.z - servePos.z).magnitude;
            if (flat < 0.7f) break;
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Windup pause (simulate holding the ball)
        yield return new WaitForSeconds(serveWindupDelay);

        if (phase != AIPhase.ReadyToServe || ball == null) yield break;

        bool evenScore = (PickleballRulesEngine.Instance?.playerBScore ?? 0) % 2 == 0;

        // Pick a random landing spot in the correct diagonal service box,
        // then compute velocity direction from the current stand position toward it.
        Vector3 landingSpot  = PickServeTargetPos(evenScore);
        Vector3 dir          = AimDirTo(landingSpot);
        float servePower = Random.Range(0.65f, 1f);

        if (animator != null)
        {
            animator.SetInteger(HashSwing, (int)SwingType.Forehand);
            animator.SetTrigger(HashSwingTrigger);
        }

        swingExecutor.ExecuteServe(ball, transform, servePower, dir);
        ball.SetLastHitBy(1);
        PickleballRulesEngine.Instance?.OnBallHit(1);

        phase = AIPhase.Serving;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (swingCooldownTimer > 0f) swingCooldownTimer -= Time.deltaTime;
        if (ball == null)
        {
            ball = FindAnyObjectByType<BallController>();
            if (ball == null) return;
        }

        switch (phase)
        {
            case AIPhase.Idle:
                ApplyGravity();
                SetMoveAnim(0f);
                break;

            case AIPhase.ReadyToServe:
                MoveToTarget();
                FaceToward(ball.transform.position);
                break;

            case AIPhase.Serving:
                // Just launched — idle briefly while ball travels to opponent
                ApplyGravity();
                SetMoveAnim(0f);
                FaceToward(ball.transform.position);
                break;

            case AIPhase.Receiving:
                // Move to base and wait for the serve to bounce on our side
                MoveToTarget();
                FaceToward(ball.transform.position);
                break;

            case AIPhase.WaitBounce:
                // AI served; track the ball's landing spot but don't swing
                UpdateReactionTarget();
                MoveToTarget();
                FaceToward(ball.transform.position);
                break;

            case AIPhase.Rally:
                UpdateReactionTarget();
                MoveToTarget();
                TrySwing();
                break;
        }
    }

    // ── Movement helpers ──────────────────────────────────────────────────────

    void UpdateReactionTarget()
    {
        reactionTimer -= Time.deltaTime;
        if (reactionTimer <= 0f)
        {
            Vector3 vel = ball.GetVelocity();
            targetPos     = ball.transform.position + vel * 0.5f;
            reactionTimer = reactionDelay;
        }
    }

    void MoveToTarget()
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        float dist = dir.magnitude;

        if (dist > 0.3f)
        {
            Vector3 move = dir.normalized * walkSpeed * Time.deltaTime;
            characterController.Move(new Vector3(move.x, Physics.gravity.y * Time.deltaTime, move.z));
            ClampToServeZone();
            SetMoveAnim(1f);
        }
        else
        {
            ApplyGravity();
            ClampToServeZone();
            SetMoveAnim(0f);
        }
    }

    void ApplyGravity() =>
        characterController.Move(new Vector3(0f, Physics.gravity.y * Time.deltaTime, 0f));

    void FaceToward(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                                   Quaternion.LookRotation(dir),
                                                   8f * Time.deltaTime);
    }

    void SetMoveAnim(float value) => animator?.SetFloat(HashMoveZ, value);

    void ClampToServeZone()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null || !rules.IsServeSetupActive) return;

        Vector3 clamped = rules.ClampPositionToServeZone(1, transform.position);
        if ((clamped - transform.position).sqrMagnitude < 0.0001f) return;

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.position = clamped;
        if (characterController != null) characterController.enabled = controllerWasEnabled;
    }

    // ── Swing ─────────────────────────────────────────────────────────────────

    bool IsInKitchen() => transform.position.z >= 0f && transform.position.z <= 7f;

    void TrySwing()
    {
        if (swingCooldownTimer > 0f) return;

        GameState state = GameStateManager.Instance?.CurrentState ?? GameState.PreServe;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;

        if (IsInKitchen()) return;

        float dist = Vector3.Distance(transform.position, ball.transform.position);
        if (dist > 2.5f) return;

        SwingType type = ball.transform.position.y >= 1.8f && stamina.HasEnoughForSmash()
            ? SwingType.Smash
            : SwingType.Forehand;

        float forceScale = Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(2.5f, 0.75f, dist));

        // Pick a random landing zone in Player A's court and compute aim direction.
        Vector3 aimDir = AimDirTo(PickRallyTargetPos());

        swingCooldownTimer = swingCooldown;

        if (animator != null)
        {
            animator.SetInteger(HashSwing, (int)type);
            animator.SetTrigger(HashSwingTrigger);
        }

        VFXManager.Instance?.PlaySwingBurst(ball.transform.position);
        SoundManager.Instance?.PlaySwingSound();
        swingExecutor.Execute(type, ball, transform, forceScale, aimDir);
        ball.SetLastHitBy(1);
        PickleballRulesEngine.Instance?.OnBallHit(1);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(basePosition, 0.3f);
        Gizmos.DrawWireSphere(serveStandPosition, 0.3f);
    }
}
