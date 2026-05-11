using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    [Header("Reference")]
    public InputManager inputManager;

    // Continuous State, polled every frame
    public Vector2 MoveInput { get; private set; }
    public bool SprintHeld { get; private set; }

    // Mouse X delta (pixels/frame) and gamepad right-stick X (-1..1).
    // PlayerController scales these independently so mouse feels precise
    // and the stick feels like a smooth rotation rate.
    public float LookMouseDeltaX { get; private set; }
    public float LookStickX      { get; private set; }

    // One shot events, fire the exact same frame the key is pressed.
    public event Action OnSwingForehand;
    public event Action OnSwingForehandStarted;
    public event Action OnSwingForehandCanceled;
    public event Action OnSwingBackhand;
    public event Action OnSwingBackhandStarted;
    public event Action OnSwingBackhandCanceled;
    public event Action OnDink;
    public event Action OnDinkStarted;
    public event Action OnDinkCanceled;
    public event Action OnLob;
    public event Action OnLobStarted;
    public event Action OnLobCanceled;
    public event Action OnSmash;
    public event Action OnSmashStarted;
    public event Action OnSmashCanceled;
    public event Action OnBlock;
    public event Action OnBlockStarted;
    public event Action OnBlockCanceled;
    private PickleballInputActions.PlayerActions _player;
    private bool _callbacksRegistered;

    private void OnEnable()
    {
        if (inputManager == null)
        {
            Debug.LogError($"[{nameof(PlayerInputHandler)}] InputManager is not assigned.", this);
            enabled = false;
            return;
        }

        inputManager.Initialize();
        _player = inputManager.actions.Player;
        RegisterActionCallbacks();
        ClearInputState();
    }

    private void RegisterActionCallbacks()
    {
        if (_callbacksRegistered) return;

        _player.SwingForehand.started += HandleSwingForehandStarted;
        _player.SwingForehand.performed += HandleSwingForehand;
        _player.SwingForehand.canceled += HandleSwingForehandCanceled;
        _player.SwingBackhand.started += HandleSwingBackhandStarted;
        _player.SwingBackhand.performed += HandleSwingBackhand;
        _player.SwingBackhand.canceled += HandleSwingBackhandCanceled;
        _player.Dink.started += HandleDinkStarted;
        _player.Dink.performed += HandleDink;
        _player.Dink.canceled += HandleDinkCanceled;
        _player.Lob.started += HandleLobStarted;
        _player.Lob.performed += HandleLob;
        _player.Lob.canceled += HandleLobCanceled;
        _player.Smash.started += HandleSmashStarted;
        _player.Smash.performed += HandleSmash;
        _player.Smash.canceled += HandleSmashCanceled;
        _player.Block.started += HandleBlockStarted;
        _player.Block.performed += HandleBlock;
        _player.Block.canceled += HandleBlockCanceled;
        _callbacksRegistered = true;
    }

    private void UnregisterActionCallbacks()
    {
        if (!_callbacksRegistered) return;

        _player.SwingForehand.started -= HandleSwingForehandStarted;
        _player.SwingForehand.performed -= HandleSwingForehand;
        _player.SwingForehand.canceled -= HandleSwingForehandCanceled;
        _player.SwingBackhand.started -= HandleSwingBackhandStarted;
        _player.SwingBackhand.performed -= HandleSwingBackhand;
        _player.SwingBackhand.canceled -= HandleSwingBackhandCanceled;
        _player.Dink.started -= HandleDinkStarted;
        _player.Dink.performed -= HandleDink;
        _player.Dink.canceled -= HandleDinkCanceled;
        _player.Lob.started -= HandleLobStarted;
        _player.Lob.performed -= HandleLob;
        _player.Lob.canceled -= HandleLobCanceled;
        _player.Smash.started -= HandleSmashStarted;
        _player.Smash.performed -= HandleSmash;
        _player.Smash.canceled -= HandleSmashCanceled;
        _player.Block.started -= HandleBlockStarted;
        _player.Block.performed -= HandleBlock;
        _player.Block.canceled -= HandleBlockCanceled;
        _callbacksRegistered = false;
    }

    private void OnDisable()
    {
        UnregisterActionCallbacks();
        ClearInputState();
        inputManager?.Disable();
    }

    private void HandleSwingForehandStarted(InputAction.CallbackContext ctx) => OnSwingForehandStarted?.Invoke();
    private void HandleSwingForehand(InputAction.CallbackContext ctx) => OnSwingForehand?.Invoke();
    private void HandleSwingForehandCanceled(InputAction.CallbackContext ctx) => OnSwingForehandCanceled?.Invoke();
    private void HandleSwingBackhandStarted(InputAction.CallbackContext ctx) => OnSwingBackhandStarted?.Invoke();
    private void HandleSwingBackhand(InputAction.CallbackContext ctx) => OnSwingBackhand?.Invoke();
    private void HandleSwingBackhandCanceled(InputAction.CallbackContext ctx) => OnSwingBackhandCanceled?.Invoke();
    private void HandleDinkStarted(InputAction.CallbackContext ctx) => OnDinkStarted?.Invoke();
    private void HandleDink(InputAction.CallbackContext ctx) => OnDink?.Invoke();
    private void HandleDinkCanceled(InputAction.CallbackContext ctx) => OnDinkCanceled?.Invoke();
    private void HandleLobStarted(InputAction.CallbackContext ctx) => OnLobStarted?.Invoke();
    private void HandleLob(InputAction.CallbackContext ctx) => OnLob?.Invoke();
    private void HandleLobCanceled(InputAction.CallbackContext ctx) => OnLobCanceled?.Invoke();
    private void HandleSmashStarted(InputAction.CallbackContext ctx) => OnSmashStarted?.Invoke();
    private void HandleSmash(InputAction.CallbackContext ctx) => OnSmash?.Invoke();
    private void HandleSmashCanceled(InputAction.CallbackContext ctx) => OnSmashCanceled?.Invoke();
    private void HandleBlockStarted(InputAction.CallbackContext ctx) => OnBlockStarted?.Invoke();
    private void HandleBlock(InputAction.CallbackContext ctx) => OnBlock?.Invoke();
    private void HandleBlockCanceled(InputAction.CallbackContext ctx) => OnBlockCanceled?.Invoke();

    public void ClearInputState()
    {
        MoveInput = Vector2.zero;
        SprintHeld = false;
        LookMouseDeltaX = 0f;
        LookStickX = 0f;
    }

    private void Update()
    {
        MoveInput = _player.Move.ReadValue<Vector2>();
        SprintHeld = _player.Sprint.IsPressed();

        LookMouseDeltaX = Mouse.current?.delta.ReadValue().x ?? 0f;
        LookStickX      = Gamepad.current?.rightStick.ReadValue().x ?? 0f;
    }
}
