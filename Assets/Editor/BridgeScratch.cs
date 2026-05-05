using Mirror;
using UnityEditor;
using UnityEngine;

public static class BridgeScratch
{
    public static string Run()
    {
        if (Application.isPlaying)
            return "Stop play mode first.";

        var ball = GameObject.Find("Ball");
        if (ball == null) return "Ball not found in scene.";

        // NetworkBallController (NetworkBehaviour — must be added before NetworkIdentity is finalized)
        if (ball.GetComponent<NetworkBallController>() == null)
        {
            ball.AddComponent<NetworkBallController>();
            Debug.Log("[BridgeScratch] Added NetworkBallController to Ball.");
        }
        else
        {
            Debug.Log("[BridgeScratch] NetworkBallController already on Ball.");
        }

        // Verify NetworkTransformReliable syncDirection is ServerToClient
        var ntr = ball.GetComponent<NetworkTransformReliable>();
        if (ntr != null)
        {
            ntr.syncDirection = SyncDirection.ServerToClient;
            Debug.Log($"[BridgeScratch] NetworkTransformReliable syncDirection set to {ntr.syncDirection}.");
        }

        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        return "Done. Ball has NetworkBallController + NetworkTransformReliable (ServerToClient). Scene saved.";
    }
}
