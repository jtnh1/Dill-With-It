using System.Collections.Generic;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinPanel : MonoBehaviour
{
    [Header("References")]
    public Transform        lobbyListParent;
    public GameObject       lobbyEntryPrefab;
    public TextMeshProUGUI  statusText;

    private void OnEnable()
    {
        if (NetworkLobbyManager.Instance == null) return;
        NetworkLobbyManager.Instance.OnLobbyListReceived += PopulateList;
        RefreshList();
    }

    private void OnDisable()
    {
        if (NetworkLobbyManager.Instance != null)
            NetworkLobbyManager.Instance.OnLobbyListReceived -= PopulateList;
    }

    public void RefreshList()
    {
        SetStatus("Searching...");
        NetworkLobbyManager.Instance?.SearchFriendLobbies();
    }

    public void OnBackButton()
    {
        // Cancel any in-flight Steam join so a late OnRoomClientConnect doesn't yank
        // the player into the LobbyPanel after they've already chosen to leave.
        NetworkLobbyManager.Instance?.LeaveLobby();
        gameObject.SetActive(false);
        UIManager.Instance?.ShowMainMenu();
    }

    void PopulateList(List<(CSteamID id, string hostName, int current, int max)> lobbies)
    {
        foreach (Transform t in lobbyListParent) Destroy(t.gameObject);

        if (lobbies.Count == 0) { SetStatus("No open lobbies found."); return; }
        SetStatus(string.Empty);

        foreach (var lobby in lobbies)
        {
            var entry = Instantiate(lobbyEntryPrefab, lobbyListParent);
            entry.GetComponentInChildren<TextMeshProUGUI>().text =
                $"{lobby.hostName}  ({lobby.current}/{lobby.max})";

            var id = lobby.id; // capture for lambda
            entry.GetComponentInChildren<Button>()
                 .onClick.AddListener(() => NetworkLobbyManager.Instance?.JoinLobby(id));
        }
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
    }
}
