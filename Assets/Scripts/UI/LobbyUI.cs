using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Mirror;

public class LobbyUI : MonoBehaviour
{
    public Transform playerListParent;
    public GameObject playerEntryPrefab;
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

    public void RefreshPlayerList(System.Collections.Generic.List<LobbyPlayerData> players)
    {
        foreach (Transform t in playerListParent) Destroy(t.gameObject);
        foreach (var p in players)
        {
            var entry = Instantiate(playerEntryPrefab, playerListParent);
            entry.GetComponentInChildren<TextMeshProUGUI>().text = $"{p.name}  {(p.isReady ? "R" : "...")}";
        }

        bool canStart = players.Count >= 2
            && players.TrueForAll(p => p.isReady)
            && NetworkLobbyManager.Instance != null
            && NetworkLobbyManager.Instance.IsHost;
        startButton.gameObject.SetActive(canStart);
    }
}