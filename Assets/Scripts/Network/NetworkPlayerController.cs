using Mirror;
using UnityEngine;

// Sibling NetworkBehaviour on the Player prefab.
// Disables input/control for non-local players and routes client swings to the server.
[RequireComponent(typeof(PlayerController))]
public class NetworkPlayerController : NetworkBehaviour
{
    private const float ServerSwingReachAllowance = 0.9f;
    private const float ServerSwingFrontArcAllowance = 0.2f;

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

    [Server]
    public void ServerResetForMatch(Vector3 position, Quaternion rotation)
    {
        ApplyMatchReset(position, rotation);
        RpcResetForMatch(position, rotation);
    }

    [ClientRpc]
    void RpcResetForMatch(Vector3 position, Quaternion rotation)
    {
        if (isServer) return;
        ApplyMatchReset(position, rotation);
    }

    void ApplyMatchReset(Vector3 position, Quaternion rotation)
    {
        if (_player == null) _player = GetComponent<PlayerController>();
        _player?.ResetForMatch(position, rotation, isLocalPlayer);
    }

    // Called by PlayerController when the client (Player B) hits the ball.
    // Executes on the server using the server-synced transform position.
    [Command]
    public void CmdSwing(int swingTypeInt, float forceScale)
    {
        if (_ball == null) _ball = FindAnyObjectByType<BallController>();
        if (_player == null) _player = GetComponent<PlayerController>();
        if (_ball == null || _player == null)
        {
            Debug.LogWarning($"[NetworkSwing] Rejected client swing: ball={_ball != null}, player={_player != null}", this);
            return;
        }

        var type = (SwingType)swingTypeInt;
        if (!_player.TryValidateSwing(type, ServerSwingReachAllowance, ServerSwingFrontArcAllowance, out float serverForceScale, out string rejectReason, _ball))
        {
            Debug.LogWarning($"[NetworkSwing] Rejected client swing {type}: {rejectReason}", this);
            return;
        }

        Debug.Log($"[NetworkSwing] Accepted client swing {type}. clientScale={forceScale:F2}, serverScale={serverForceScale:F2}, state={GameStateManager.Instance?.CurrentState}");
        _player.ExecuteConfirmedSwing(type, _ball, 1, serverForceScale); // client is always Player B (index 1)
    }
}
