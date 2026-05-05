using Mirror;
using System;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }
    public GameState CurrentState { get; private set; } = GameState.PreServe;
    public event Action<GameState, GameState> OnStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Called by server-side code (rules engine, AI, etc.).
    public void TransitionTo(GameState newState)
    {
        if (newState == CurrentState) return;
        GameState previous = CurrentState;
        CurrentState = newState;
        Debug.Log($"[GameState] {previous} -> {newState}");
        OnStateChanged?.Invoke(previous, newState);

        // In multiplayer, broadcast to all clients.
        if (NetworkServer.active)
            NetworkGameManager.Instance?.BroadcastState(newState);
    }

    // Called on clients by NetworkGameManager RPC — applies state without re-broadcasting.
    public void ApplyRemoteTransition(GameState newState)
    {
        if (newState == CurrentState) return;
        GameState previous = CurrentState;
        CurrentState = newState;
        Debug.Log($"[GameState][Remote] {previous} -> {newState}");
        OnStateChanged?.Invoke(previous, newState);
    }
}
