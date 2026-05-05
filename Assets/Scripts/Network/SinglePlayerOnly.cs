using Mirror;
using UnityEngine;

// Attach to scene objects (Player, etc.) that should only exist in singleplayer.
// In multiplayer, NetworkRoomManager spawns the proper networked prefabs instead.
public class SinglePlayerOnly : MonoBehaviour
{
    private void Start()
    {
        if (NetworkClient.active || NetworkServer.active)
            gameObject.SetActive(false);
    }
}
