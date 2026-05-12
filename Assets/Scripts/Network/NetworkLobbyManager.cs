using System;
using System.Collections.Generic;
using Mirror;
using Steamworks;
using UnityEngine;

public class NetworkLobbyManager : NetworkRoomManager
{
    public static NetworkLobbyManager Instance { get; private set; }

    public bool IsHost => NetworkServer.active && NetworkClient.isConnected;
    public int RequiredPlayerCount => matchMode == MatchMode.Doubles ? 4 : 2;

    [Header("Match")]
    public MatchMode matchMode = MatchMode.Doubles;

    [Header("Spawn Positions")]
    public Vector3 playerASpawnPos = new Vector3( 5f, 0.1f, -24.5f);
    public Vector3 playerBSpawnPos = new Vector3(-5f, 0.1f,  24.5f);
    public Vector3 playerAPartnerSpawnPos = new Vector3(-5f, 0.1f, -24.5f);
    public Vector3 playerBPartnerSpawnPos = new Vector3( 5f, 0.1f,  24.5f);
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
        matchMode = MatchSessionConfig.SelectedMatchMode;
        maxConnections = RequiredPlayerCount;
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
        matchMode = MatchSessionConfig.SelectedMatchMode;
        maxConnections = RequiredPlayerCount;
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
        if (roomSlots.Count < RequiredPlayerCount) return;
        ServerChangeScene(GameplayScene);
    }

    // Cleanly back out of an active lobby for either host or client. Tears down the
    // Mirror connection and releases the Steam lobby so a friend's invite list updates.
    public void LeaveLobby()
    {
        if (NetworkServer.active && NetworkClient.isConnected)      StopHost();
        else if (NetworkClient.isConnected || NetworkClient.active) StopClient();
        else if (NetworkServer.active)                              StopServer();

        if (SteamManager.Initialized && _currentLobby.IsValid())
            SteamMatchmaking.LeaveLobby(_currentLobby);

        _currentLobby = CSteamID.Nil;
        _joiningLobby = false;
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

            NetworkPlayerController networkPlayer = player.GetComponent<NetworkPlayerController>();
            int playerId = networkPlayer != null ? networkPlayer.PlayerId : GetPlayerIdForRoomIndex(slot.index);
            GetSpawnPoseForPlayer(playerId, out Vector3 pos, out Quaternion rot);
            player.ServerResetForMatch(pos, rot);
        }
    }

    [Server]
    public void ResetGamePlayersForServe()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules == null) return;

        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot == null) continue;

            NetworkIdentity roomIdentity = slot.GetComponent<NetworkIdentity>();
            NetworkConnectionToClient conn = roomIdentity != null ? roomIdentity.connectionToClient : null;
            NetworkPlayerController player = conn?.identity != null
                ? conn.identity.GetComponent<NetworkPlayerController>()
                : null;

            if (player == null) continue;

            Vector3 pos = rules.GetServeZonePositionForPlayer(player.PlayerId);
            Quaternion rot = rules.GetServeRotationForPlayer(player.PlayerId);
            player.ServerResetForServe(pos, rot);
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
        SteamMatchmaking.SetLobbyData(_currentLobby, "mode",     matchMode.ToString());
        StartHost();
    }

    void OnLobbyEntered(LobbyEnter_t cb)
    {
        if (!_joiningLobby) return; // host entering their own lobby — ignore
        _joiningLobby = false;
        _currentLobby = new CSteamID(cb.m_ulSteamIDLobby);
        if (Enum.TryParse(SteamMatchmaking.GetLobbyData(_currentLobby, "mode"), out MatchMode lobbyMode))
        {
            matchMode = lobbyMode;
            MatchSessionConfig.SetMode(lobbyMode);
            maxConnections = RequiredPlayerCount;
        }

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
        int roomIndex = rp != null ? rp.index : 0;
        GetIdentityForRoomIndex(roomIndex, out int playerId, out int teamId, out int teamSlot);
        GetSpawnPoseForPlayer(playerId, out Vector3 pos, out Quaternion rot);

        GameObject player = Instantiate(playerPrefab, pos, rot);
        player.GetComponent<NetworkPlayerController>()?.ServerConfigureIdentity(playerId, teamId, teamSlot);
        return player;
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
                name    = GetLobbyDisplayName(rp.index),
                isReady = rp.readyToBegin
            });
        }
        return list;
    }

    void GetIdentityForRoomIndex(int roomIndex, out int playerId, out int teamId, out int teamSlot)
    {
        if (matchMode == MatchMode.Doubles)
        {
            playerId = roomIndex switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 3,
                _ => roomIndex
            };
            teamId = PlayerIdentity.TeamOfPlayer(playerId);
            teamSlot = PlayerIdentity.SlotOfPlayer(playerId);
            return;
        }

        playerId = roomIndex == 0 ? 0 : 1;
        teamId = playerId;
        teamSlot = 0;
    }

    int GetPlayerIdForRoomIndex(int roomIndex)
    {
        GetIdentityForRoomIndex(roomIndex, out int playerId, out _, out _);
        return playerId;
    }

    void GetSpawnPoseForPlayer(int playerId, out Vector3 position, out Quaternion rotation)
    {
        int team = PlayerIdentity.TeamOfPlayer(playerId);
        int slot = PlayerIdentity.SlotOfPlayer(playerId);

        if (team == 0)
        {
            position = slot == 0 ? playerASpawnPos : playerAPartnerSpawnPos;
            rotation = Quaternion.Euler(playerASpawnEuler);
            return;
        }

        position = slot == 0 ? playerBSpawnPos : playerBPartnerSpawnPos;
        rotation = Quaternion.Euler(playerBSpawnEuler);
    }

    string GetLobbyDisplayName(int roomIndex)
    {
        GetIdentityForRoomIndex(roomIndex, out int playerId, out int teamId, out int teamSlot);
        string teamName = teamId == 0 ? "Team A" : "Team B";
        string slotName = teamSlot == 0 ? "Server Slot" : "Partner Slot";
        return matchMode == MatchMode.Doubles
            ? $"{teamName} {slotName} (P{playerId + 1})"
            : $"Player {playerId + 1}";
    }
}
