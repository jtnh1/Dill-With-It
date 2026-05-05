using Mirror;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
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

    private CharacterController characterController;
    private BallController ball;
    private float verticalVelocity;
    private float swingCooldownTimer;
    private float inputGraceTimer = 0.2f;
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

        inputHandler.OnSwingForehand += () => AttemptSwing(SwingType.Forehand);
        inputHandler.OnSwingBackhand += () => AttemptSwing(SwingType.Backhand);
        inputHandler.OnDink         += () => AttemptSwing(SwingType.Dink);
        inputHandler.OnLob          += () => AttemptSwing(SwingType.Lob);
        inputHandler.OnSmash        += () => AttemptSwing(SwingType.Smash);
        inputHandler.OnBlock        += () => AttemptSwing(SwingType.Block);

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
    }

    void OnGameStateChanged(GameState from, GameState to)
    {
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
        HandleRotation();
        HandleMovement();

        if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame
            && (NetworkClient.active || NetworkServer.active))
            GetComponent<NetworkPlayerController>()?.CmdRequestRestart();
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
    }

    void AttemptSwing(SwingType requestedType)
    {
        var netId = GetComponent<NetworkIdentity>();
        if (netId != null && (NetworkClient.active || NetworkServer.active) && !netId.isLocalPlayer) return;

        if (ball == null) ball = FindAnyObjectByType<BallController>();
        if (ball == null) { animationDriver.PlayWhiff(); return; }

        if (requestedType == SwingType.Smash && !stamina.HasEnoughForSmash())
        { animationDriver.PlayWhiff(); return; }

        animationDriver.PlaySwing(requestedType);

        Vector3 toBall = ball.transform.position - transform.position;
        float   xzDist = new Vector2(toBall.x, toBall.z).magnitude;

        if (xzDist > swingReach) { animationDriver.PlayWhiff(); return; }

        if (xzDist > 0.05f)
        {
            Vector3 fwdFlat    = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            Vector3 toBallFlat = new Vector3(toBall.x,            0f, toBall.z           ).normalized;
            if (Vector3.Dot(fwdFlat, toBallFlat) < hitFrontArc) { animationDriver.PlayWhiff(); return; }
        }

        float distScale = Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(swingReach, swingReach * 0.4f, xzDist));

        float staminaScale = 1f;
        if (requestedType == SwingType.Forehand || requestedType == SwingType.Backhand)
        {
            float threshold = stamina.maxStamina * staminaFullPowerFrac;
            staminaScale = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(stamina.CurrentStamina / threshold));
        }

        float forceScale = distScale * staminaScale;

        VFXManager.Instance?.PlaySwingBurst(ball.transform.position);
        SoundManager.Instance?.PlaySwingSound();

        bool isMultiplayer = NetworkClient.active;
        bool isServer      = NetworkServer.active;

        if (!isMultiplayer || isServer)
        {
            // Singleplayer or host (always Player A, index 0).
            swingExecutor.Execute(requestedType, ball, transform, forceScale);
            ball.SetLastHitBy(0);
            PickleballRulesEngine.Instance?.OnBallHit(0);
        }
        else
        {
            // Client (Player B, index 1) — send to server for authoritative execution.
            GetComponent<NetworkPlayerController>()?.CmdSwing((int)requestedType, forceScale);
        }
    }
}
