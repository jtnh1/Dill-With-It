using System.Collections;
using Mirror;
using UnityEngine;

public class AIBot : MonoBehaviour
{
    public enum Difficulty { Easy, Medium, Hard }

    enum AIPhase
    {
        Idle,
        ReadyToServe,
        Serving,
        Receiving,
        WaitBounce,
        Rally,
    }

    [Header("Settings")]
    public Difficulty difficulty = Difficulty.Easy;
    public float walkSpeed = 5f;
    public float reactionDelay = 0.4f;
    public float swingCooldown = 0.6f;
    [Tooltip("Used when this bot has not been explicitly assigned a PlayerIdentity.")]
    public int fallbackPlayerId = 1;

    [Header("Positioning")]
    [Tooltip("Base court position when receiving or in a rally.")]
    public Vector3 basePosition = new Vector3(0f, 0f, 18f);
    [Tooltip("Clamp AI movement to its half of the court.")]
    public Vector2 courtXBounds = new Vector2(-9.25f, 9.25f);
    public Vector2 courtZBounds = new Vector2(1.25f, 28.25f);
    [Tooltip("AI stands this far behind the predicted contact point so the ball arrives in front of it.")]
    public float contactStandoff = 1.1f;

    [Header("Prediction")]
    public float predictionMaxTime = 2.75f;
    public float predictionStep = 0.05f;
    public float predictionMinHitHeight = 0.55f;
    public float predictionMaxHitHeight = 2.25f;
    public float fallbackLookAheadTime = 0.35f;

    [Header("Serve")]
    public Vector3 serveStandPosition = new Vector3(0f, 0f, 23f);
    public float serveXOffset = 5f;
    public float serveWindupDelay = 1.0f;

    [Header("References")]
    public SwingExecutor swingExecutor;
    public StaminaSystem stamina;
    public Animator animator;
    public PaddleAttacher paddleAttacher;

    private BallController ball;
    private PaddleHitZone hitZone;
    private CharacterController characterController;
    private PlayerIdentity identity;
    private Vector3 targetPos;
    private float reactionTimer;
    private float swingCooldownTimer;
    private AIPhase phase = AIPhase.Idle;

    private static readonly int HashMoveZ        = Animator.StringToHash("MoveZ");
    private static readonly int HashSwing        = Animator.StringToHash("SwingType");
    private static readonly int HashSwingTrigger = Animator.StringToHash("Swing");

    static readonly float[,] ZoneATargets =
    {
        { -8f,    8f,  -6f,  -1f },
        {  0.5f,  9.5f, -21f, -8f },
        { -9.5f, -0.5f, -21f, -8f },
        {  0.5f,  9.5f, -26f, -23f },
        { -9.5f, -0.5f, -26f, -23f },
    };

    static readonly float[,] ZoneBTargets =
    {
        { -8f,    8f,   1f,   6f },
        { -9.5f, -0.5f,  8f,  21f },
        {  0.5f,  9.5f,  8f,  21f },
        { -9.5f, -0.5f, 23f,  26f },
        {  0.5f,  9.5f, 23f,  26f },
    };

    int PlayerId => identity != null && identity.IsAssigned ? identity.PlayerId : fallbackPlayerId;
    int TeamId => PlayerIdentity.TeamOfPlayer(PlayerId);
    int TeamSlot => PlayerIdentity.SlotOfPlayer(PlayerId);

    private void Awake()
    {
        ball = FindAnyObjectByType<BallController>();
        characterController = GetComponent<CharacterController>();
        if (paddleAttacher == null) paddleAttacher = GetComponent<PaddleAttacher>();
        EnsureIdentity();
        ApplySideDefaults();
        ApplyDifficultyDefaults();
    }

    private void Start()
    {
        if (NetworkClient.active || NetworkServer.active)
        {
            gameObject.SetActive(false);
            return;
        }

        BootstrapLocalDoublesIfNeeded();
        ApplySideDefaults();

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnStateChanged;

        if (PickleballRulesEngine.Instance != null)
            PickleballRulesEngine.Instance.OnHit += OnAnyBallHit;

        StartCoroutine(LateStart());
    }

    private IEnumerator LateStart()
    {
        yield return null;

        if (paddleAttacher != null)
        {
            Transform paddle = paddleAttacher.GetPaddleTransform();
            if (paddle != null) hitZone = paddle.GetComponentInChildren<PaddleHitZone>();
        }

        InitPhase();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;

        if (PickleballRulesEngine.Instance != null)
            PickleballRulesEngine.Instance.OnHit -= OnAnyBallHit;
    }

    public void ConfigureIdentity(int playerId)
    {
        fallbackPlayerId = playerId;
        EnsureIdentity().Apply(playerId, PlayerIdentity.TeamOfPlayer(playerId), PlayerIdentity.SlotOfPlayer(playerId));
        ApplySideDefaults();
    }

    void BootstrapLocalDoublesIfNeeded()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null || rules.ActiveMatchMode != MatchMode.Doubles)
            return;

        EnsureHumanIdentity();

        if (!HasBotForPlayer(1))
            ConfigureIdentity(1);

        EnsureBotForPlayer(2);
        EnsureBotForPlayer(3);
        ResetLocalParticipantsToServePositions();
    }

    void EnsureHumanIdentity()
    {
        foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.GetComponent<AIBot>() != null) continue;
            PlayerIdentity playerIdentity = EnsureIdentity(player.gameObject);
            if (!playerIdentity.IsAssigned)
                playerIdentity.Apply(0, 0, 0);
            return;
        }
    }

    bool HasBotForPlayer(int playerId)
    {
        foreach (AIBot bot in FindObjectsByType<AIBot>(FindObjectsSortMode.None))
        {
            PlayerIdentity botIdentity = bot.EnsureIdentity();
            if (botIdentity.IsAssigned && botIdentity.PlayerId == playerId)
                return true;
        }

        return false;
    }

    void EnsureBotForPlayer(int playerId)
    {
        if (HasBotForPlayer(playerId)) return;

        AIBot bot = playerId == PlayerId ? this : Instantiate(this, transform.parent);
        bot.name = $"AIBot_P{playerId + 1}";
        bot.ConfigureIdentity(playerId);
    }

    void ResetLocalParticipantsToServePositions()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null) return;

        foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.GetComponent<AIBot>() != null) continue;
            PlayerIdentity playerIdentity = EnsureIdentity(player.gameObject);
            int playerId = playerIdentity.IsAssigned ? playerIdentity.PlayerId : 0;
            player.ResetForServePosition(
                rules.GetServeZonePositionForPlayer(playerId),
                rules.GetServeRotationForPlayer(playerId),
                true);
        }

        foreach (AIBot bot in FindObjectsByType<AIBot>(FindObjectsSortMode.None))
        {
            int playerId = bot.PlayerId;
            bot.ResetForServePosition(
                rules.GetServeZonePositionForPlayer(playerId),
                rules.GetServeRotationForPlayer(playerId));
        }
    }

    void OnAnyBallHit(int playerIndex)
    {
        if (PlayerIdentity.TeamOfPlayer(playerIndex) == TeamId) return;

        reactionTimer = 0f;
        if (phase == AIPhase.Serving || phase == AIPhase.Receiving)
        {
            phase = AIPhase.WaitBounce;
            targetPos = PredictBounceTarget();
        }
        else
        {
            targetPos = PredictReactionTarget();
        }
    }

    void OnStateChanged(GameState from, GameState to)
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        bool aiServes = rules != null && rules.ServingPlayer == PlayerId;
        bool myTeamServes = rules != null && rules.ServingTeam == TeamId;

        switch (to)
        {
            case GameState.PreServe:
                InitPhase();
                break;

            case GameState.FirstBounce:
                phase = myTeamServes ? AIPhase.WaitBounce : AIPhase.Rally;
                break;

            case GameState.SecondBounce:
                phase = myTeamServes ? AIPhase.Rally : AIPhase.WaitBounce;
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

    void InitPhase()
    {
        StopAllCoroutines();

        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        bool aiServes = rules != null && rules.ServingPlayer == PlayerId;
        if (aiServes)
        {
            phase = AIPhase.ReadyToServe;
            targetPos = CurrentServePosition();
            StartCoroutine(ServeRoutine());
            return;
        }

        phase = AIPhase.Receiving;
        targetPos = CurrentServeZonePosition();
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

    Vector3 CurrentServePosition()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null ? rules.GetServeZonePositionForPlayer(PlayerId) : HomePosition();
    }

    Vector3 CurrentServeZonePosition()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null ? rules.GetServeZonePositionForPlayer(PlayerId) : HomePosition();
    }

    IEnumerator ServeRoutine()
    {
        Vector3 servePos = CurrentServePosition();
        targetPos = servePos;

        float timeout = 5f;
        while (timeout > 0f)
        {
            float flat = new Vector2(transform.position.x - servePos.x, transform.position.z - servePos.z).magnitude;
            if (flat < 0.7f) break;
            timeout -= Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(serveWindupDelay);

        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (phase != AIPhase.ReadyToServe || ball == null || rules == null || rules.ServingPlayer != PlayerId)
            yield break;

        Vector3 landingSpot = PickServeTargetPos(GetTeamScore() % 2 == 0);
        Vector3 dir = AimDirTo(landingSpot);
        float servePower = Random.Range(0.65f, 1f);

        if (animator != null)
        {
            animator.SetInteger(HashSwing, (int)SwingType.Forehand);
            animator.SetTrigger(HashSwingTrigger);
        }

        swingExecutor.ExecuteServe(ball, transform, servePower, dir);
        ball.SetLastHitBy(PlayerId);
        PickleballRulesEngine.Instance?.OnBallHit(PlayerId, transform.position);

        phase = AIPhase.Serving;
    }

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
                ApplyGravity();
                SetMoveAnim(0f);
                FaceToward(ball.transform.position);
                break;

            case AIPhase.Receiving:
                MoveToTarget();
                FaceToward(ball.transform.position);
                break;

            case AIPhase.WaitBounce:
                UpdateReactionTarget(true);
                MoveToTarget();
                FaceToward(ball.transform.position);
                break;

            case AIPhase.Rally:
                UpdateReactionTarget(false);
                MoveToTarget();
                TrySwing();
                break;
        }
    }

    void UpdateReactionTarget(bool requireBounceTarget)
    {
        reactionTimer -= Time.deltaTime;
        if (reactionTimer <= 0f)
        {
            targetPos = requireBounceTarget ? PredictBounceTarget() : PredictReactionTarget();
            reactionTimer = reactionDelay;
        }
    }

    Vector3 PredictReactionTarget()
    {
        Vector3 ballPos = ball.transform.position;
        Vector3 velocity = ball.GetVelocity();

        if (velocity.sqrMagnitude < 0.25f)
            return ClampCourtPosition(basePosition);

        if (TryPredictHittablePoint(ballPos, velocity, out Vector3 predicted) && IsResponsibleForPoint(predicted))
            return ClampCourtPosition(StandPositionForContact(predicted));

        Vector3 fallback = ballPos + velocity * fallbackLookAheadTime;
        fallback.y = 0f;
        return IsResponsibleForPoint(fallback)
            ? ClampCourtPosition(StandPositionForContact(fallback))
            : ClampCourtPosition(basePosition);
    }

    Vector3 PredictBounceTarget()
    {
        Vector3 ballPos = ball.transform.position;
        Vector3 velocity = ball.GetVelocity();

        if (velocity.sqrMagnitude < 0.25f)
            return ClampCourtPosition(basePosition);

        if (TryPredictBouncePoint(ballPos, velocity, out Vector3 predicted) && IsResponsibleForPoint(predicted))
            return ClampCourtPosition(StandPositionForContact(predicted));

        Vector3 fallback = ballPos + velocity * fallbackLookAheadTime;
        fallback.y = 0f;
        return IsResponsibleForPoint(fallback)
            ? ClampCourtPosition(StandPositionForContact(fallback))
            : ClampCourtPosition(basePosition);
    }

    bool TryPredictHittablePoint(Vector3 start, Vector3 velocity, out Vector3 point)
    {
        Vector3 gravity = Physics.gravity;
        Vector3 previous = start;
        bool hasEnteredAISide = IsPointOnAISide(previous);

        for (float t = predictionStep; t <= predictionMaxTime; t += predictionStep)
        {
            Vector3 sample = start + velocity * t + 0.5f * gravity * t * t;
            if (IsPointOnAISide(sample)) hasEnteredAISide = true;

            if (hasEnteredAISide && IsPointOnAISide(sample) && IsHittablePredictionHeight(sample.y))
            {
                point = sample;
                point.y = 0f;
                return true;
            }

            if (hasEnteredAISide && previous.y > 0f && sample.y <= 0f)
            {
                float segmentT = Mathf.InverseLerp(previous.y, sample.y, 0f);
                point = Vector3.Lerp(previous, sample, segmentT);
                point.y = 0f;
                return true;
            }

            previous = sample;
        }

        point = default;
        return false;
    }

    bool TryPredictBouncePoint(Vector3 start, Vector3 velocity, out Vector3 point)
    {
        Vector3 gravity = Physics.gravity;
        Vector3 previous = start;
        bool hasEnteredAISide = IsPointOnAISide(previous);

        for (float t = predictionStep; t <= predictionMaxTime; t += predictionStep)
        {
            Vector3 sample = start + velocity * t + 0.5f * gravity * t * t;
            if (IsPointOnAISide(sample)) hasEnteredAISide = true;

            if (hasEnteredAISide && previous.y > 0f && sample.y <= 0f)
            {
                float segmentT = Mathf.InverseLerp(previous.y, sample.y, 0f);
                point = Vector3.Lerp(previous, sample, segmentT);
                point.y = 0f;
                return IsPointOnAISide(point);
            }

            previous = sample;
        }

        point = default;
        return false;
    }

    bool IsPointOnAISide(Vector3 point)
    {
        return point.z >= courtZBounds.x && point.z <= courtZBounds.y
            && point.x >= courtXBounds.x && point.x <= courtXBounds.y;
    }

    bool IsHittablePredictionHeight(float y)
    {
        return y >= predictionMinHitHeight && y <= predictionMaxHitHeight;
    }

    Vector3 StandPositionForContact(Vector3 contactPoint)
    {
        Vector3 stand = contactPoint;
        stand.z += TeamId == 0 ? -contactStandoff : contactStandoff;
        stand.y = 0f;
        return stand;
    }

    Vector3 ClampCourtPosition(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, courtXBounds.x, courtXBounds.y);
        position.z = Mathf.Clamp(position.z, courtZBounds.x, courtZBounds.y);
        position.y = 0f;
        return position;
    }

    void MoveToTarget()
    {
        Vector3 clampedTarget = ClampCourtPosition(targetPos);
        Vector3 dir = clampedTarget - transform.position;
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

    void ApplyGravity()
    {
        if (characterController != null)
            characterController.Move(new Vector3(0f, Physics.gravity.y * Time.deltaTime, 0f));
    }

    void FaceToward(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    void SetMoveAnim(float value) => animator?.SetFloat(HashMoveZ, value);

    void ClampToServeZone()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null || !rules.IsServeSetupActive) return;

        Vector3 clamped = rules.ClampPositionToServeZone(PlayerId, transform.position);
        if ((clamped - transform.position).sqrMagnitude < 0.0001f) return;

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.position = clamped;
        if (characterController != null) characterController.enabled = controllerWasEnabled;
    }

    bool IsInKitchen()
    {
        return TeamId == 0
            ? transform.position.z >= -7f && transform.position.z <= 0f
            : transform.position.z >= 0f && transform.position.z <= 7f;
    }

    void TrySwing()
    {
        if (swingCooldownTimer > 0f) return;

        GameState state = GameStateManager.Instance?.CurrentState ?? GameState.PreServe;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;
        if (!IsResponsibleForPoint(ball.transform.position)) return;

        float dist = Vector3.Distance(transform.position, ball.transform.position);
        if (dist > 2.5f) return;

        bool inKitchen = IsInKitchen();
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (inKitchen && (rules == null || !rules.HasBallBouncedOnPlayerSideSinceOpponentHit(PlayerId)))
            return;

        SwingType type = inKitchen
            ? SwingType.Dink
            : ball.transform.position.y >= 1.8f && stamina.HasEnoughForSmash()
            ? SwingType.Smash
            : SwingType.Forehand;

        float forceScale = Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(2.5f, 0.75f, dist));
        Vector3 aimDir = AimDirTo(PickRallyTargetPos());
        float chargedPower = difficulty switch
        {
            Difficulty.Easy => Random.Range(0.2f, 0.8f),
            Difficulty.Medium => Random.Range(0.35f, 0.95f),
            Difficulty.Hard => Random.Range(0.5f, 1f),
            _ => Random.Range(0.35f, 0.95f)
        };
        float horizontalPowerScale = swingExecutor != null
            ? swingExecutor.GetSwingPowerMultiplier(type, chargedPower)
            : 1f;

        swingCooldownTimer = swingCooldown;

        if (animator != null)
        {
            animator.SetInteger(HashSwing, (int)type);
            animator.SetTrigger(HashSwingTrigger);
        }

        VFXManager.Instance?.PlaySwingBurst(ball.transform.position);
        SoundManager.Instance?.PlaySwingSound();
        swingExecutor.Execute(type, ball, transform, forceScale, aimDir, horizontalPowerScale);
        ball.SetLastHitBy(PlayerId);
        PickleballRulesEngine.Instance?.OnBallHit(PlayerId, transform.position);
    }

    Vector3 PickServeTargetPos(bool evenScore)
    {
        if (TeamId == 0)
        {
            float xMin = evenScore ? -9.5f : 0.5f;
            float xMax = evenScore ? -0.5f : 9.5f;
            return new Vector3(Random.Range(xMin, xMax), 0f, Random.Range(8f, 21f));
        }

        float aXMin = evenScore ? 0.5f : -9.5f;
        float aXMax = evenScore ? 9.5f : -0.5f;
        return new Vector3(Random.Range(aXMin, aXMax), 0f, Random.Range(-21f, -8f));
    }

    Vector3 PickRallyTargetPos()
    {
        float[,] targets = TeamId == 0 ? ZoneBTargets : ZoneATargets;
        int i = Random.Range(0, targets.GetLength(0));
        return new Vector3(
            Random.Range(targets[i, 0], targets[i, 1]),
            0f,
            Random.Range(targets[i, 2], targets[i, 3]));
    }

    Vector3 AimDirTo(Vector3 landingPos)
    {
        Vector3 d = landingPos - transform.position;
        d.y = 0f;
        return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
    }

    bool IsResponsibleForPoint(Vector3 point)
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null || rules.ActiveMatchMode != MatchMode.Doubles)
            return true;

        if (!IsPointOnAISide(point))
            return false;

        if (TeamId == 0)
            return TeamSlot == 0 ? point.x >= 0f : point.x < 0f;

        return TeamSlot == 0 ? point.x <= 0f : point.x > 0f;
    }

    void ApplyDifficultyDefaults()
    {
        switch (difficulty)
        {
            case Difficulty.Easy:
                reactionDelay = Mathf.Min(reactionDelay, 0.35f);
                predictionMaxTime = Mathf.Max(predictionMaxTime, 2.2f);
                break;
            case Difficulty.Medium:
                reactionDelay = Mathf.Min(reactionDelay, 0.18f);
                predictionMaxTime = Mathf.Max(predictionMaxTime, 2.5f);
                break;
            case Difficulty.Hard:
                reactionDelay = Mathf.Min(reactionDelay, 0.05f);
                predictionMaxTime = Mathf.Max(predictionMaxTime, 3f);
                break;
        }
    }

    void ApplySideDefaults()
    {
        float laneX = HomeLaneX();
        float sideZ = TeamId == 0 ? -18f : 18f;
        basePosition = new Vector3(laneX, 0f, sideZ);
        serveStandPosition = new Vector3(laneX, 0f, TeamId == 0 ? -23f : 23f);
        courtXBounds = new Vector2(-9.25f, 9.25f);
        courtZBounds = TeamId == 0 ? new Vector2(-28.25f, -1.25f) : new Vector2(1.25f, 28.25f);
    }

    Vector3 HomePosition() => ClampCourtPosition(basePosition);

    float HomeLaneX()
    {
        if (TeamId == 0)
            return TeamSlot == 0 ? 4.5f : -4.5f;

        return TeamSlot == 0 ? -4.5f : 4.5f;
    }

    int GetTeamScore()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null) return 0;
        return TeamId == 0 ? rules.playerAScore : rules.playerBScore;
    }

    PlayerIdentity EnsureIdentity()
    {
        return identity = EnsureIdentity(gameObject);
    }

    static PlayerIdentity EnsureIdentity(GameObject target)
    {
        if (!target.TryGetComponent(out PlayerIdentity targetIdentity))
            targetIdentity = target.AddComponent<PlayerIdentity>();
        return targetIdentity;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(basePosition, 0.3f);
        Gizmos.DrawWireSphere(serveStandPosition, 0.3f);
    }
}
