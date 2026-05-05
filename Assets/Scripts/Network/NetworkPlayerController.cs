using Mirror;
using UnityEngine;

// Sibling NetworkBehaviour on the Player prefab.
// Disables input/control for non-local players and routes client swings to the server.
[RequireComponent(typeof(PlayerController))]
public class NetworkPlayerController : NetworkBehaviour
{
    private PlayerController   _player;
    private PlayerInputHandler _input;
    private BallController     _ball;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _input  = GetComponent<PlayerInputHandler>();

        // Disable input and movement for ALL spawned network players up-front.
        // OnStartLocalPlayer() re-enables them for the local player only.
        // This prevents the shared InputManager ScriptableObject from being
        // initialized or disabled by remote player instances.
        if (NetworkClient.active || NetworkServer.active)
        {
            _player.enabled = false;
            if (_input) _input.enabled = false;
        }
    }

    // Called only on the client for the player object that belongs to this client.
    public override void OnStartLocalPlayer()
    {
        _player.enabled = true;
        if (_input) _input.enabled = true;

        // Both the camera and stamina bar subscribe at scene load before players exist.
        // Push references here so they're wired at the exact moment Mirror marks this object local.
        var stamina = GetComponent<StaminaSystem>();
        if (stamina != null && UIManager.Instance != null)
            stamina.OnStaminaChanged += UIManager.Instance.UpdateStamina;

        FindAnyObjectByType<CameraController>()?.RegisterLocalPlayer(transform);
    }

    // Called on all clients for every spawned player object.
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isLocalPlayer) return;

        // Remote player — disable cosmetic/local-only components.
        var aim = GetComponent<SwingAimIndicator>();
        if (aim) aim.enabled = false;

        var stamina = GetComponent<StaminaSystem>();
        if (stamina) stamina.enabled = false;
    }

    // Sent by any player to request a full match restart on the server.
    [Command]
    public void CmdRequestRestart()
    {
        NetworkGameManager.Instance?.RestartMatch();
    }

    // Called by PlayerController when the client (Player B) hits the ball.
    // Executes on the server using the server-synced transform position.
    [Command]
    public void CmdSwing(int swingTypeInt, float forceScale)
    {
        if (_ball == null) _ball = FindAnyObjectByType<BallController>();
        if (_ball == null) return;

        var type = (SwingType)swingTypeInt;
        _player.swingExecutor.Execute(type, _ball, transform, forceScale);
        _ball.SetLastHitBy(1); // client is always Player B (index 1)
        PickleballRulesEngine.Instance?.OnBallHit(1);
    }
}
