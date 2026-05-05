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
    public event Action OnSwingBackhand;
    public event Action OnDink;
    public event Action OnLob;
    public event Action OnSmash;
    public event Action OnBlock;
    private PickleballInputActions.PlayerActions _player;

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

        _player.SwingForehand.performed += ctx => OnSwingForehand?.Invoke();
        _player.SwingBackhand.performed += ctx => OnSwingBackhand?.Invoke();
        _player.Dink.performed += ctx => OnDink?.Invoke();
        _player.Lob.performed += ctx => OnLob?.Invoke();
        _player.Smash.performed += ctx => OnSmash?.Invoke();
        _player.Block.performed += ctx => OnBlock?.Invoke();
    }

    private void OnDisable()
    {
        ClearInputState();
        inputManager?.Disable();
    }

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
