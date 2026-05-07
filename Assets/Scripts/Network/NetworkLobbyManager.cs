using System;
using System.Collections.Generic;
using Mirror;
using Steamworks;
using UnityEngine;

public class NetworkLobbyManager : NetworkRoomManager
{
    public static NetworkLobbyManager Instance { get; private set; }

    public bool IsHost => NetworkServer.active && NetworkClient.isConnected;

    [Header("Spawn Positions")]
    public Vector3 playerASpawnPos = new Vector3( 5f, 0.1f, -24.5f);
    public Vector3 playerBSpawnPos = new Vector3(-5f, 0.1f,  24.5f);
    public Vector3 playerASpawnEuler = new Vector3(0f,   0f, 0f);
    public Vector3 playerBSpawnEuler = new Vector3(0f, 180f, 0f);

    // Fired when a lobby list search completes — JoinPanel subscribes.
    public event Action<List<(CSteamID id, string hostName, int current, int max)>> OnLobbyListReceived;

    private CSteamID _currentLobby;
    private bool     _joiningLobby;   // true when we initiated a JoinLobby call

    private Callback<LobbyCreated_t>          _lobbyCreated;
    private Callback<LobbyEnter_t>            _lobbyEnter;
    private Callback<LobbyMatchList_t>        _lobbyMatchList;
    private Callback<GameLobbyJoinRequested_t> _joinRequested;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void Awake()
    {
        base.Awake();
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (!SteamManager.Initialized) return;
        _lobbyCreated   = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        _lobbyEnter     = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        _lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
        _joinRequested  = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
    }

    private void OnDisable()
    {
        _lobbyCreated?.Dispose();
        _lobbyEnter?.Dispose();
        _lobbyMatchList?.Dispose();
        _joinRequested?.Dispose();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void HostLobby()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogWarning("[NetworkLobbyManager] Steam not initialized — cannot host.");
            return;
        }
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxConnections);
        // StartHost() is called inside OnLobbyCreated once Steam confirms.
    }

    public void JoinLobby(CSteamID lobbyID)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogWarning("[NetworkLobbyManager] Steam not initialized — cannot join.");
            return;
        }
        _joiningLobby = true;
        SteamMatchmaking.JoinLobby(lobbyID);
        // StartClient() is called inside OnLobbyEntered.
    }

    public void SearchFriendLobbies()
    {
        if (!SteamManager.Initialized) return;
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", "DillWithIt", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.RequestLobbyList();
    }

    public void SetReady(bool ready)
    {
        NetworkRoomPlayer local = GetLocalRoomPlayer();
        if (local != null) local.CmdChangeReadyState(ready);
    }

    public void StartMatch()
    {
        if (!IsHost) return;
        ServerChangeScene(GameplayScene);
    }

    [Server]
    public void ResetGamePlayersForRestart()
    {
        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot == null) continue;

            NetworkIdentity roomIdentity = slot.GetComponent<NetworkIdentity>();
            NetworkConnectionToClient conn = roomIdentity != null ? roomIdentity.connectionToClient : null;
            NetworkPlayerController player = conn?.identity != null
                ? conn.identity.GetComponent<NetworkPlayerController>()
                : null;

            if (player == null) continue;

            bool isA = slot.index == 0;
            Vector3 pos = isA ? playerASpawnPos : playerBSpawnPos;
            Quaternion rot = Quaternion.Euler(isA ? playerASpawnEuler : playerBSpawnEuler);
            player.ServerResetForMatch(pos, rot);
        }
    }

    [Server]
    public void ResetGamePlayersForServe(Vector3 posA, Quaternion rotA, Vector3 posB, Quaternion rotB)
    {
        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot == null) continue;

            NetworkIdentity roomIdentity = slot.GetComponent<NetworkIdentity>();
            NetworkConnectionToClient conn = roomIdentity != null ? roomIdentity.connectionToClient : null;
            NetworkPlayerController player = conn?.identity != null
                ? conn.identity.GetComponent<NetworkPlayerController>()
                : null;

            if (player == null) continue;

            bool isA = slot.index == 0;
            player.ServerResetForServe(isA ? posA : posB, isA ? rotA : rotB);
        }
    }

    // ── Steam Callbacks ────────────────────────────────────────────────────────

    void OnLobbyCreated(LobbyCreated_t cb)
    {
        if (cb.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[NetworkLobbyManager] Lobby creation failed: {cb.m_eResult}");
            return;
        }
        _currentLobby = new CSteamID(cb.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(_currentLobby, "hostName", SteamFriends.GetPersonaName());
        SteamMatchmaking.SetLobbyData(_currentLobby, "game",     "DillWithIt");
        StartHost();
    }

    void OnLobbyEntered(LobbyEnter_t cb)
    {
        if (!_joiningLobby) return; // host entering their own lobby — ignore
        _joiningLobby = false;
        _currentLobby = new CSteamID(cb.m_ulSteamIDLobby);

        CSteamID hostID = SteamMatchmaking.GetLobbyOwner(_currentLobby);
        networkAddress  = hostID.ToString();
        StartClient();
    }

    void OnLobbyMatchList(LobbyMatchList_t cb)
    {
        var list = new List<(CSteamID, string, int, int)>();
        for (int i = 0; i < (int)cb.m_nLobbiesMatching; i++)
        {
            CSteamID id      = SteamMatchmaking.GetLobbyByIndex(i);
            string   host    = SteamMatchmaking.GetLobbyData(id, "hostName");
            if (string.IsNullOrEmpty(host)) host = "Unknown";
            int      current = SteamMatchmaking.GetNumLobbyMembers(id);
            int      max     = SteamMatchmaking.GetLobbyMemberLimit(id);
            list.Add((id, host, current, max));
        }
        OnLobbyListReceived?.Invoke(list);
    }

    void OnJoinRequested(GameLobbyJoinRequested_t cb)
    {
        // Friend sent a Steam invite.
        UIManager.Instance?.OnJoinButton();
        JoinLobby(cb.m_steamIDLobby);
    }

    // ── Room Client Overrides ─────────────────────────────────────────────────

    // Called on both host and joining client once the Mirror connection is live.
    // Ensures the LobbyPanel is visible on the client side.
    public override void OnRoomClientConnect()
    {
        UIManager.Instance?.ShowLobby();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.ReplaceHandler<LobbyRefreshMessage>(OnLobbyRefreshReceived);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        NetworkClient.UnregisterHandler<LobbyRefreshMessage>();
    }

    void OnLobbyRefreshReceived(LobbyRefreshMessage msg)
    {
        var lobbyUI = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
        lobbyUI?.RefreshPlayerList(new List<LobbyPlayerData>(msg.players));
    }

    // ── Room Server Overrides ──────────────────────────────────────────────────

    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerConnect(conn);
        RefreshLobbyUI();
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnRoomServerDisconnect(conn);
        RefreshLobbyUI();
    }

    public override void OnRoomServerPlayersReady()
    {
        // Don't auto-start — host presses Start manually.
        RefreshLobbyUI();
    }

    public override void OnRoomServerPlayersNotReady()
    {
        RefreshLobbyUI();
    }

    // Spawn each game player at the correct side of the court.
    public override GameObject OnRoomServerCreateGamePlayer(
        NetworkConnectionToClient conn, GameObject roomPlayerGO)
    {
        var rp   = roomPlayerGO.GetComponent<NetworkRoomPlayer>();
        bool isA = rp == null || rp.index == 0;

        Vector3    pos = isA ? playerASpawnPos   : playerBSpawnPos;
        Quaternion rot = Quaternion.Euler(isA ? playerASpawnEuler : playerBSpawnEuler);

        return Instantiate(playerPrefab, pos, rot);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    NetworkRoomPlayer GetLocalRoomPlayer()
    {
        foreach (var slot in roomSlots)
            if (slot != null && slot.isLocalPlayer) return slot as NetworkRoomPlayer;
        return null;
    }

    public void RefreshLobbyUI()
    {
        if (!NetworkServer.active) return;
        NetworkServer.SendToAll(new LobbyRefreshMessage { players = BuildPlayerDataList().ToArray() });
    }

    public List<LobbyPlayerData> BuildPlayerDataList()
    {
        var list = new List<LobbyPlayerData>();
        foreach (var slot in roomSlots)
        {
            if (slot == null) continue;
            var rp = slot as NetworkRoomPlayer;
            if (rp == null) continue;
            list.Add(new LobbyPlayerData
            {
                name    = $"Player {rp.index + 1}",
                isReady = rp.readyToBegin
            });
        }
        return list;
    }
}
