using Mirror;

public struct LobbyRefreshMessage : NetworkMessage
{
    public LobbyPlayerData[] players;
}
