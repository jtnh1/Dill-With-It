using Mirror;
using UnityEngine;

// Attach alongside BallController + NetworkTransform on the multiplayer ball prefab.
// Server has authority: clients disable their Rigidbody so NetworkTransform
// controls position and no local collision events fire incorrect rules.
public class NetworkBallController : NetworkBehaviour
{
    private BallController _ball;
    private Rigidbody      _rb;

    private void Awake()
    {
        _ball = GetComponent<BallController>();
        _rb   = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        if (_ball == null) _ball = GetComponent<BallController>();
        if (_ball != null) _ball.ResetPositionApplied += OnResetPositionApplied;
    }

    private void OnDisable()
    {
        if (_ball != null) _ball.ResetPositionApplied -= OnResetPositionApplied;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (_ball != null)
            PickleballRulesEngine.Instance?.RegisterBall(_ball, false);

        if (isServer) return; // host already runs physics

        // Client: let NetworkTransform drive position; no local physics simulation.
        if (_rb) _rb.isKinematic = true;
    }

    private void OnResetPositionApplied(Vector3 position)
    {
        if (!NetworkServer.active) return;
        if (TryGetComponent(out NetworkTransformBase networkTransform))
            networkTransform.ServerTeleport(position, transform.rotation);
    }
}
