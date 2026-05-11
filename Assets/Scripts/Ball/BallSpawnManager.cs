using System.Collections;
using Mirror;
using UnityEngine;

public class BallSpawnManager : MonoBehaviour
{
    [Header("Prefabs")]
    public BallController singlePlayerBallPrefab;
    public BallController multiplayerBallPrefab;

    [Header("Multiplayer")]
    [Tooltip("The multiplayer ball is spawned only after this many network players exist on the server.")]
    public int requiredMultiplayerPlayers = 2;

    private BallController spawnedBall;

    private IEnumerator Start()
    {
        yield return null;

        if (NetworkClient.active || NetworkServer.active)
        {
            if (NetworkServer.active)
                yield return SpawnMultiplayerBallWhenReady();
            yield break;
        }

        SpawnSinglePlayerBall();
    }

    void SpawnSinglePlayerBall()
    {
        if (spawnedBall != null) return;
        if (singlePlayerBallPrefab == null)
        {
            Debug.LogError("[BallSpawnManager] singlePlayerBallPrefab is not assigned.");
            return;
        }

        spawnedBall = Instantiate(singlePlayerBallPrefab);
        spawnedBall.name = "SinglePlayerBall";
        PickleballRulesEngine.Instance?.RegisterBall(spawnedBall);
    }

    IEnumerator SpawnMultiplayerBallWhenReady()
    {
        if (spawnedBall != null) yield break;
        if (multiplayerBallPrefab == null)
        {
            Debug.LogError("[BallSpawnManager] multiplayerBallPrefab is not assigned.");
            yield break;
        }

        int requiredPlayers = NetworkLobbyManager.Instance != null
            ? NetworkLobbyManager.Instance.RequiredPlayerCount
            : requiredMultiplayerPlayers;

        while (FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None).Length < requiredPlayers)
            yield return null;

        spawnedBall = Instantiate(multiplayerBallPrefab);
        spawnedBall.name = "MultiplayerBall";
        NetworkServer.Spawn(spawnedBall.gameObject);
        PickleballRulesEngine.Instance?.RegisterBall(spawnedBall);
    }
}
