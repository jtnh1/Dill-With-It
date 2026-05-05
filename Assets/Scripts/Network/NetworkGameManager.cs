using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

// Server-authoritative singleton that broadcasts game state and score to all clients.
// Place one instance in GameScene with a NetworkIdentity component.
public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Called by GameStateManager on server after every TransitionTo.
    [Server]
    public void BroadcastState(GameState state)
    {
        RpcSyncState(state);
    }

    [ClientRpc]
    void RpcSyncState(GameState state)
    {
        if (isServer) return; // host already applied this locally
        GameStateManager.Instance?.ApplyRemoteTransition(state);
    }

    // Called by NetworkPlayerController.CmdRequestRestart — reloads the game scene for all clients.
    [Server]
    public void RestartMatch()
    {
        NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
    }

    // Called by PickleballRulesEngine on server when a point is awarded.
    [Server]
    public void BroadcastScore(int scoreA, int scoreB, int serving, string announcement)
    {
        RpcSyncScore(scoreA, scoreB, serving, announcement);
    }

    [ClientRpc]
    void RpcSyncScore(int scoreA, int scoreB, int serving, string announcement)
    {
        if (isServer) return;
        UIManager.Instance?.UpdateScoreBoard(scoreA, scoreB, serving);
        if (!string.IsNullOrEmpty(announcement))
            UIManager.Instance?.ShowAnnouncement(announcement);
    }
}
