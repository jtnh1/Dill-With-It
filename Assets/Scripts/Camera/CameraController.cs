using UnityEngine;
using Mirror;

// Follows the local player while keeping the camera aimed toward the net,
// so both sides always see the net and can read opponent positioning.
public class CameraController : MonoBehaviour
{
    [Header("Follow Offsets")]
    [Tooltip("Units above the player root.")]
    public float heightOffset  = 11f;
    [Tooltip("Units behind the player (away from the net).")]
    public float depthOffset   = 8f;

    [Header("Look-At")]
    [Tooltip("World-space Y the camera aims at (net height region).")]
    public float lookAtHeight  = 1.5f;
    [Tooltip("Units ahead of the player (toward the net) for the look-at point.")]
    public float lookAheadDist = 30f;

    [Header("Smoothing")]
    [Tooltip("Higher = tighter follow. 0 = no smoothing.")]
    public float positionSmooth = 6f;

    private Transform _target;
    private int       _teamId;
    private bool      _snapped;   // snap on first valid frame so PlayerController reads correct axes

    // Called by NetworkPlayerController.OnStartLocalPlayer so the camera is
    // registered at the exact moment Mirror marks this machine's player object.
    public void RegisterLocalPlayer(Transform player)
    {
        _target = player;
        _teamId = ResolveTeamId(player);
    }

    private void Awake()
    {
        // Player-tunable offsets persist via PlayerPrefs; inspector defaults
        // act as fallback when no saved value exists.
        heightOffset = CameraSettings.LoadHeightOffset();
        depthOffset  = CameraSettings.LoadDepthOffset();
    }

    private void Start()
    {
        // Multiplayer: camera target is pushed via RegisterLocalPlayer().
        // Single-player: find the only PlayerController directly.
        if (!NetworkClient.active && !NetworkServer.active)
            TryFindLocalPlayer();
    }

    // LateUpdate so player movement is settled before we reposition.
    private void LateUpdate()
    {
        if (_target == null)
        {
            // Fallback for single-player if Start() didn't find the player yet.
            if (!NetworkClient.active && !NetworkServer.active)
                TryFindLocalPlayer();
            if (_target == null) return;
        }

        // Team A (-Z side): camera goes further -Z. Team B (+Z side): camera goes further +Z.
        float behindSign = _teamId == 1 ? 1f : -1f;

        Vector3 desired = new Vector3(
            _target.position.x,
            _target.position.y + heightOffset,
            _target.position.z + behindSign * depthOffset
        );

        if (!_snapped)
        {
            transform.position = desired;
            _snapped = true;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, desired, positionSmooth * Time.deltaTime);
        }

        // Look-at X matches camera X so the camera never yaws horizontally —
        // this keeps the camera forward always pointing straight toward the net,
        // preserving the _camForward / _camRight axes PlayerController cached.
        Vector3 lookAt = new Vector3(
            _target.position.x,
            lookAtHeight,
            _target.position.z - behindSign * lookAheadDist
        );
        transform.LookAt(lookAt);
    }

    private void TryFindLocalPlayer()
    {
        if (NetworkClient.active || NetworkServer.active)
        {
            foreach (var ni in FindObjectsByType<NetworkIdentity>())
            {
                if (ni.isLocalPlayer)
                {
                    _target = ni.transform;
                    _teamId = ResolveTeamId(ni.transform);
                    return;
                }
            }
        }
        else
        {
            // Singleplayer: grab the PlayerController directly.
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc)
            {
                _target = pc.transform;
                _teamId = ResolveTeamId(pc.transform);
            }
        }
    }

    int ResolveTeamId(Transform player)
    {
        if (player != null && player.TryGetComponent(out PlayerIdentity identity) && identity.IsAssigned)
            return identity.TeamId;

        if (player != null && player.TryGetComponent(out NetworkPlayerController networkPlayer))
            return networkPlayer.TeamId;

        return NetworkClient.active && !NetworkServer.active ? 1 : 0;
    }
}
