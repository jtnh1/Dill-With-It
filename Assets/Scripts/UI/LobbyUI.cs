using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public Button readyButton;
    public Button startButton;
    public TextMeshProUGUI statusText;

    private bool isReady = false;

    private void OnEnable()
    {
        startButton.gameObject.SetActive(false);
    }

    public void OnReadyClicked()
    {
        isReady = !isReady;
        readyButton.GetComponentInChildren<TextMeshProUGUI>().text = isReady ? "Unready" : "Ready";
        NetworkLobbyManager.Instance?.SetReady(isReady);
    }

    public void OnStartClicked()
    {
        NetworkLobbyManager.Instance?.StartMatch();
    }

    public void OnBackButton()
    {
        NetworkLobbyManager.Instance?.LeaveLobby();

        // Reset local ready state so the button label matches a fresh visit next time.
        isReady = false;
        if (readyButton != null)
        {
            TextMeshProUGUI label = readyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Ready";
        }

        UIManager.Instance?.ShowMainMenu();
    }

    // Lobby player list rendering moved to Mirror's NetworkRoomPlayer OnGUI overlay; this
    // refresh is only used to gate the host's Start button on player count + ready state.
    public void RefreshPlayerList(System.Collections.Generic.List<LobbyPlayerData> players)
    {
        int requiredPlayers = NetworkLobbyManager.Instance != null
            ? NetworkLobbyManager.Instance.RequiredPlayerCount
            : 2;

        bool canStart = players.Count >= requiredPlayers
            && players.TrueForAll(p => p.isReady)
            && NetworkLobbyManager.Instance != null
            && NetworkLobbyManager.Instance.IsHost;
        startButton.gameObject.SetActive(canStart);
    }
}
