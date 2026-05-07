using Mirror;
using UnityEngine;

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

    // Called by NetworkPlayerController.CmdRequestRestart — resets the active match for all clients.
    [Server]
    public void RestartMatch()
    {
        if (NetworkServer.isLoadingScene) return;

        NetworkLobbyManager.Instance?.ResetGamePlayersForRestart();
        PickleballRulesEngine.Instance?.ResetMatch();
        RpcPrepareRestartUi(0, 0, 0);
    }

    [ClientRpc]
    void RpcPrepareRestartUi(int scoreA, int scoreB, int serving)
    {
        if (!isServer)
            PickleballRulesEngine.Instance?.ApplyRemoteScore(scoreA, scoreB, serving);
        UIManager.Instance?.ShowGameplayForRestart(scoreA, scoreB, serving);
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
        PickleballRulesEngine.Instance?.ApplyRemoteScore(scoreA, scoreB, serving);
        UIManager.Instance?.UpdateScoreBoard(scoreA, scoreB, serving);
        if (!string.IsNullOrEmpty(announcement))
            UIManager.Instance?.ShowAnnouncement(announcement);
    }

    [Server]
    public void BroadcastServeHit()
    {
        RpcSyncServeHit();
    }

    [ClientRpc]
    void RpcSyncServeHit()
    {
        if (isServer) return;
        PickleballRulesEngine.Instance?.ApplyRemoteServeHit();
    }
}
