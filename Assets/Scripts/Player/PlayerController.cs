using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private const float NetworkSwingReachAllowance = 0.75f;
    private const float NetworkSwingFrontArcAllowance = 0.15f;

    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 10f;
    public float gravity = -20f;

    [Header("References")]
    public PlayerInputHandler inputHandler;
    public StaminaSystem stamina;
    public SwingResolver swingResolver;
    public SwingExecutor swingExecutor;
    public PaddleAttacher paddleAttacher;
    public PlayerAnimationDriver animationDriver;
    public Animator animator;

    [Header("Hit Zone (Frontal Cylinder)")]
    [Tooltip("Max XZ distance from player root to ball.")]
    public float swingReach = 2.5f;
    [Tooltip("Ball must be at least this height relative to player root (prevents hitting through the floor).")]
    public float hitMinHeight = -0.5f;
    [Tooltip("Ball must be at most this height relative to player root.")]
    public float hitMaxHeight = 2.8f;
    [Tooltip("Dot-product threshold for frontal arc. 0 = front hemisphere (ball must be in front). " +
             "Increase toward 1 to tighten the cone. Negative values allow hits slightly behind.")]
    [Range(-1f, 1f)]
    public float hitFrontArc = 0f;

    [Header("Look / Rotation")]
    [Tooltip("Degrees of body rotation per pixel of mouse X delta.")]
    public float mouseSensitivity = 0.18f;
    [Tooltip("Body rotation speed (degrees/sec) at full gamepad right-stick deflection.")]
    public float stickRotateSpeed = 160f;

    [Header("Swing")]
    public float swingCooldown = 0.5f;
    [Tooltip("Stamina floor for forehand/backhand. Below this fraction of max stamina, force scales down to 50 %.")]
    [Range(0f, 1f)]
    public float staminaFullPowerFrac = 0.1f;

    [Header("Serve")]
    [Tooltip("Seconds required to fill the serve power bar.")]
    public float serveChargeSeconds = 1.25f;

    private CharacterController characterController;
    private BallController ball;
    private float verticalVelocity;
    private float swingCooldownTimer;
    private float inputGraceTimer = 0.2f;
    private bool serveCharging;
    private float serveChargeTimer;
    private int serveChargeDirection = 1;
    private WorldServePowerBar servePowerBar;
    private Vector3 _camForward;
    private Vector3 _camRight;

    private static readonly int HashMoveX        = Animator.StringToHash("MoveX");
    private static readonly int HashMoveZ        = Animator.StringToHash("MoveZ");
    private static readonly int HashIsSprint     = Animator.StringToHash("IsSprinting");

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private IEnumerator Start()
    {
        yield return null;

        // Cache camera axes after CameraController.Start() has run.
        // Project onto XZ plane so vertical tilt doesn't affect movement.
        Camera cam = Camera.main;
        if (cam != null)
        {
            _camForward = cam.transform.forward;
            _camRight   = cam.transform.right;
            _camForward.y = 0f; _camForward.Normalize();
            _camRight.y   = 0f; _camRight.Normalize();
        }
        else
        {
            _camForward = Vector3.forward;
            _camRight   = Vector3.right;
        }

        ball = FindAnyObjectByType<BallController>();

        inputHandler.OnSwingForehandStarted += BeginServeCharge;
        inputHandler.OnSwingForehandCanceled += ReleaseServeCharge;
        inputHandler.OnSwingForehand += () => HandleSwingPerformed(SwingType.Forehand);
        inputHandler.OnSwingBackhand += () => HandleSwingPerformed(SwingType.Backhand);
        inputHandler.OnDink         += () => HandleSwingPerformed(SwingType.Dink);
        inputHandler.OnLob          += () => HandleSwingPerformed(SwingType.Lob);
        inputHandler.OnSmash        += () => HandleSwingPerformed(SwingType.Smash);
        inputHandler.OnBlock        += () => HandleSwingPerformed(SwingType.Block);

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
    }

    void OnGameStateChanged(GameState from, GameState to)
    {
        if (from == GameState.GameOver && to == GameState.PreServe)
        {
            EnableGameplayControl();
            return;
        }

        if (to != GameState.GameOver) return;

        // Stop gameplay control completely so the UI module owns pointer/button input.
        inputHandler?.ClearInputState();
        if (inputHandler != null) inputHandler.enabled = false;
        enabled = false;
    }

    private void Update()
    {
        var netId = GetComponent<NetworkIdentity>();
        if (netId != null && (NetworkClient.active || NetworkServer.active) && !netId.isLocalPlayer) return;

        if (swingCooldownTimer > 0f) swingCooldownTimer -= Time.deltaTime;
        if (inputGraceTimer   > 0f) inputGraceTimer   -= Time.deltaTime;
        UpdateServeCharge();
        HandleRotation();
        HandleMovement();

        if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame
            && (NetworkClient.active || NetworkServer.active))
            GetComponent<NetworkPlayerController>()?.CmdRequestRestart();
    }

    public void ResetForMatch(Vector3 position, Quaternion rotation, bool enableLocalControl)
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (characterController != null) characterController.enabled = controllerWasEnabled;

        verticalVelocity = 0f;
        swingCooldownTimer = 0f;
        inputGraceTimer = 0.2f;
        inputHandler?.ClearInputState();
        stamina?.ResetStamina();
        stamina?.SetSprinting(false);
        animator?.SetFloat(HashMoveX, 0f);
        animator?.SetFloat(HashMoveZ, 0f);
        animator?.SetBool(HashIsSprint, false);
        CancelServeCharge();

        if (enableLocalControl)
            EnableGameplayControl();
    }

    public void ResetForServePosition(Vector3 position, Quaternion rotation, bool enableLocalControl)
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (characterController != null) characterController.enabled = controllerWasEnabled;

        verticalVelocity = 0f;
        swingCooldownTimer = 0f;
        inputGraceTimer = 0.2f;
        inputHandler?.ClearInputState();
        stamina?.SetSprinting(false);
        animator?.SetFloat(HashMoveX, 0f);
        animator?.SetFloat(HashMoveZ, 0f);
        animator?.SetBool(HashIsSprint, false);
        CancelServeCharge();

        if (enableLocalControl)
            EnableGameplayControl();
    }

    public void EnableGameplayControl()
    {
        var netId = GetComponent<NetworkIdentity>();
        if (netId != null && (NetworkClient.active || NetworkServer.active) && !netId.isLocalPlayer)
            return;

        enabled = true;
        if (inputHandler != null)
        {
            inputHandler.ClearInputState();
            inputHandler.enabled = true;
        }
    }

    void HandleRotation()
    {
        float deg = inputHandler.LookMouseDeltaX * mouseSensitivity
                  + inputHandler.LookStickX * stickRotateSpeed * Time.deltaTime;
        if (Mathf.Abs(deg) > 0.001f)
            transform.Rotate(0f, deg, 0f, Space.World);
    }

    void HandleMovement()
    {
        Vector2 move2D = inputHandler.MoveInput;
        bool wantSprint = inputHandler.SprintHeld && stamina.CanSprint();

        stamina.SetSprinting(wantSprint);
        float speed = wantSprint ? sprintSpeed : walkSpeed;

        // Camera-relative horizontal movement: W/S along camera forward, A/D along camera right.
        Vector3 horizontal = (_camRight * move2D.x + _camForward * move2D.y) * speed;

        if (characterController.isGrounded) verticalVelocity = -1f;
        else verticalVelocity += gravity * Time.deltaTime;

        Vector3 move = horizontal;
        move.y = verticalVelocity;
        characterController.Move(move * Time.deltaTime);

        // Feed animator in character-local space so blend tree directions stay correct.
        Vector3 localVel = transform.InverseTransformDirection(horizontal);
        animator?.SetFloat(HashMoveX, speed > 0f ? localVel.x / speed : 0f);
        animator?.SetFloat(HashMoveZ, speed > 0f ? localVel.z / speed : 0f);
        animator?.SetBool(HashIsSprint, wantSprint);

        ClampToServeZone();
        StopServeChargeIfOutOfRange();
    }

    void HandleSwingPerformed(SwingType requestedType)
    {
        if (IsPreServePhase()) return;
        AttemptSwing(requestedType);
    }

    void AttemptSwing(SwingType requestedType)
    {
        var netId = GetComponent<NetworkIdentity>();
        if (netId != null && (NetworkClient.active || NetworkServer.active) && !netId.isLocalPlayer) return;
        if (swingCooldownTimer > 0f)
        {
            Debug.Log($"[PlayerSwing] Rejected {requestedType}: cooldown {swingCooldownTimer:F2}s remaining.", this);
            return;
        }

        bool isMultiplayer = NetworkClient.active;
        bool isServer      = NetworkServer.active;
        bool remoteClient  = isMultiplayer && !isServer;
        float reachAllowance = remoteClient ? NetworkSwingReachAllowance : 0f;
        float frontArcAllowance = remoteClient ? NetworkSwingFrontArcAllowance : 0f;

        if (!TryValidateSwing(requestedType, reachAllowance, frontArcAllowance, out float forceScale, out string rejectReason))
        {
            Debug.Log($"[PlayerSwing] Rejected {requestedType}: {rejectReason}", this);
            animationDriver.PlaySwing(requestedType);
            SoundManager.Instance?.PlaySwingSound();
            swingCooldownTimer = swingCooldown;
            return;
        }

        animationDriver.PlaySwing(requestedType);
        VFXManager.Instance?.PlaySwingBurst(ball.transform.position);
        SoundManager.Instance?.PlaySwingSound();
        swingCooldownTimer = swingCooldown;

        if (!isMultiplayer || isServer)
        {
            // Singleplayer or host (always Player A, index 0).
            ExecuteConfirmedSwing(requestedType, ball, 0, forceScale);
        }
        else
        {
            // Client (Player B, index 1) — send to server for authoritative execution.
            GetComponent<NetworkPlayerController>()?.CmdSwing((int)requestedType, forceScale);
        }
    }

    public bool TryValidateSwing(SwingType requestedType, float reachAllowance, float frontArcAllowance, out float forceScale, out string rejectReason, BallController targetBall = null)
    {
        forceScale = 1f;
        rejectReason = string.Empty;

        BallController activeBall = targetBall != null ? targetBall : ball;
        if (activeBall == null) activeBall = FindAnyObjectByType<BallController>();
        if (activeBall == null)
        {
            rejectReason = "no ball found";
            return false;
        }
        ball = activeBall;

        if (requestedType == SwingType.Smash && stamina != null && !stamina.HasEnoughForSmash())
        {
            rejectReason = "not enough stamina for smash";
            return false;
        }

        Vector3 toBall = activeBall.transform.position - transform.position;
        float height = toBall.y;
        if (height < hitMinHeight || height > hitMaxHeight)
        {
            rejectReason = $"ball height {height:F2} outside [{hitMinHeight:F2}, {hitMaxHeight:F2}]";
            return false;
        }

        float xzDist = new Vector2(toBall.x, toBall.z).magnitude;
        float allowedReach = swingReach + Mathf.Max(0f, reachAllowance);
        if (xzDist > allowedReach)
        {
            rejectReason = $"ball too far xz={xzDist:F2}, allowed={allowedReach:F2}";
            return false;
        }

        if (xzDist > 0.05f)
        {
            Vector3 fwdFlat    = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            Vector3 toBallFlat = new Vector3(toBall.x,            0f, toBall.z           ).normalized;
            float dot = Vector3.Dot(fwdFlat, toBallFlat);
            float allowedArc = hitFrontArc - Mathf.Max(0f, frontArcAllowance);
            if (dot < allowedArc)
            {
                rejectReason = $"ball outside front arc dot={dot:F2}, required={allowedArc:F2}";
                return false;
            }
        }

        float distScale = Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(allowedReach, swingReach * 0.4f, xzDist));

        float staminaScale = 1f;
        if ((requestedType == SwingType.Forehand || requestedType == SwingType.Backhand) && stamina != null)
        {
            float threshold = stamina.maxStamina * staminaFullPowerFrac;
            staminaScale = threshold > 0f
                ? Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(stamina.CurrentStamina / threshold))
                : 1f;
        }

        forceScale = distScale * staminaScale;
        return true;
    }

    public void ExecuteConfirmedSwing(SwingType requestedType, BallController targetBall, int playerIndex, float forceScale)
    {
        if (targetBall == null) return;

        swingExecutor.Execute(requestedType, targetBall, transform, forceScale);
        targetBall.SetLastHitBy(playerIndex);
        PickleballRulesEngine.Instance?.OnBallHit(playerIndex);
    }

    public void ExecuteConfirmedServe(BallController targetBall, int playerIndex, float normalizedPower)
    {
        if (targetBall == null) return;

        swingExecutor.ExecuteServe(targetBall, transform, normalizedPower);
        targetBall.SetLastHitBy(playerIndex);
        PickleballRulesEngine.Instance?.OnBallHit(playerIndex);
    }

    void BeginServeCharge()
    {
        if (!CanChargeServe()) return;

        serveCharging = true;
        serveChargeTimer = 0f;
        serveChargeDirection = 1;
        EnsureServePowerBar();
        servePowerBar.SetTarget(transform);
        servePowerBar.SetPower(0f);
        servePowerBar.Show();
    }

    void ReleaseServeCharge()
    {
        if (!serveCharging) return;

        float normalizedPower = Mathf.Clamp01(serveChargeTimer / Mathf.Max(0.01f, serveChargeSeconds));
        CancelServeCharge();

        if (!CanChargeServe()) return;

        var netId = GetComponent<NetworkIdentity>();
        bool isMultiplayer = NetworkClient.active;
        bool isServer = NetworkServer.active;

        animationDriver.PlaySwing(SwingType.Forehand);
        VFXManager.Instance?.PlaySwingBurst(ball != null ? ball.transform.position : transform.position);
        SoundManager.Instance?.PlaySwingSound();
        swingCooldownTimer = swingCooldown;

        if (!isMultiplayer || isServer)
        {
            if (ball == null) ball = FindAnyObjectByType<BallController>();
            ExecuteConfirmedServe(ball, GetLocalPlayerIndex(), normalizedPower);
        }
        else
        {
            PickleballRulesEngine.Instance?.ApplyRemoteServeHit();
            netId?.GetComponent<NetworkPlayerController>()?.CmdServe(normalizedPower);
        }
    }

    void UpdateServeCharge()
    {
        if (!serveCharging) return;

        if (!CanChargeServe())
        {
            CancelServeCharge();
            return;
        }

        serveChargeTimer += Time.deltaTime * serveChargeDirection;
        if (serveChargeTimer >= serveChargeSeconds)
        {
            serveChargeTimer = serveChargeSeconds;
            serveChargeDirection = -1;
        }
        else if (serveChargeTimer <= 0f)
        {
            serveChargeTimer = 0f;
            serveChargeDirection = 1;
        }

        servePowerBar?.SetPower(serveChargeTimer / Mathf.Max(0.01f, serveChargeSeconds));
    }

    void CancelServeCharge()
    {
        serveCharging = false;
        serveChargeTimer = 0f;
        serveChargeDirection = 1;
        servePowerBar?.Hide();
    }

    bool CanChargeServe()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null
            && rules.IsServeSetupActive
            && rules.ServingPlayer == GetLocalPlayerIndex()
            && IsLocalGameplayOwner()
            && IsServeBallInRange();
    }

    bool IsServeBallInRange()
    {
        return TryValidateSwing(SwingType.Forehand, 0f, 0f, out _, out _);
    }

    bool IsPreServePhase()
    {
        return GameStateManager.Instance != null
            && GameStateManager.Instance.CurrentState == GameState.PreServe;
    }

    bool IsLocalGameplayOwner()
    {
        var netId = GetComponent<NetworkIdentity>();
        return netId == null || !(NetworkClient.active || NetworkServer.active) || netId.isLocalPlayer;
    }

    int GetLocalPlayerIndex()
    {
        return NetworkClient.active && !NetworkServer.active ? 1 : 0;
    }

    void EnsureServePowerBar()
    {
        if (servePowerBar == null)
            servePowerBar = WorldServePowerBar.Create();
    }

    void ClampToServeZone()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null || !rules.IsServeSetupActive || !IsLocalGameplayOwner()) return;

        Vector3 clamped = rules.ClampPositionToServeZone(GetLocalPlayerIndex(), transform.position);
        if ((clamped - transform.position).sqrMagnitude < 0.0001f) return;

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.position = clamped;
        if (characterController != null) characterController.enabled = controllerWasEnabled;
    }

    void StopServeChargeIfOutOfRange()
    {
        if (serveCharging && !CanChargeServe())
            CancelServeCharge();
    }
}
