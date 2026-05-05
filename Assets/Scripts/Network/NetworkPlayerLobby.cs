using System;
using Mirror;
/*
    Attach this alongside the built-in NetworkRoomPlayer on your room player prefabs.
    Carries extra data (display name) that NetworkRoomPlayer doesn't have by default
*/
public class NetworkPlayerLobby : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [Command]
    public void CmdSetName(string name) => playerName = name;

    void OnNameChanged(String _, String newVal)
    {
        NetworkLobbyManager.Instance?.RefreshLobbyUI();
    }
}
