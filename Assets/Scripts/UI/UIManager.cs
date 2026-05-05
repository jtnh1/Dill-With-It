using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using Mirror;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD")]
    public TextMeshProUGUI  playerAScoreText;
    public TextMeshProUGUI  playerBScoreText;
    public TextMeshProUGUI  servingIndicatorText;
    public TextMeshProUGUI  announcementText;
    public TextMeshProUGUI gameOverText;
    public Slider           staminaBar;

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject lobbyPanel;
    public GameObject joinPanel;
    public GameObject hudPanel;
    public GameObject gameOverPanel;

    [Header("Game Over")]
    public Button playAgainButton;

    [Header("Networking (Multiplayer Only)")]
    [Tooltip("Drag the NetworkLobbyManager prefab here. It is NOT placed in the scene — only instantiated when Host or Join is clicked.")]
    public GameObject networkManagerPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        ResolvePanelReferences();

        // GameStateManager only exists in GameScene, not MainMenu
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnStateChanged;

        var stamina = FindAnyObjectByType<StaminaSystem>();
        if (stamina != null) stamina.OnStaminaChanged += UpdateStamina;

        // Always start hidden; shown only when game ends or a point lands
        SetPanelActive(gameOverPanel, false);
        if (announcementText) announcementText.gameObject.SetActive(false);
        if (playAgainButton == null && gameOverPanel != null)
            playAgainButton = gameOverPanel.GetComponentInChildren<Button>(true);
        EnsureUiInputReady();

        // Show starting score & serving state immediately
        UpdateScoreBoard(0, 0, 0);

        if (GameStateManager.Instance != null)
        {
            // In GameScene — hide cursor for gameplay
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // In MainMenu — always ensure cursor is visible (editor stop-play can leave it locked)
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        if (GameStateManager.Instance != null
            && Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            ResetGame();
    }

    void ResetGame()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    }

    // -- Scoreboard

    public void UpdateScoreBoard(int scoreA, int scoreB, int serving)
    {
        // In multiplayer, scoreA = host, scoreB = client. Show the local player as YOU.
        bool isPureClient = NetworkClient.active && !NetworkServer.active;
        int myScore    = isPureClient ? scoreB : scoreA;
        int theirScore = isPureClient ? scoreA : scoreB;
        bool iAmServing = isPureClient ? serving == 1 : serving == 0;

        if (playerAScoreText) playerAScoreText.text = $"YOU: {myScore}";
        if (playerBScoreText) playerBScoreText.text = $"Opponent: {theirScore}";
        if (servingIndicatorText) servingIndicatorText.text = iAmServing ? "Serving: YOU" : "Serving: Opponent";
    }

    public void ShowAnnouncement(string message, float duration = 2.5f)
    {
        if (!announcementText) return;
        announcementText.text = message;
        announcementText.gameObject.SetActive(true);
        CancelInvoke(nameof(HideAnnouncement));
        Invoke(nameof(HideAnnouncement), duration);
    }

    void HideAnnouncement()
    {
        if (announcementText != null)
            announcementText.gameObject.SetActive(false);
    }
    public void UpdateStamina(float current, float max)
    {
        if (staminaBar) staminaBar.value = current / max;
    }

    // State-driven UI

    void OnStateChanged(GameState from, GameState to)
    {
        switch (to)
        {
            case GameState.ResultOfRound:
                ShowAnnouncement("Point Scored!");
                break;
            case GameState.GameOver:
                ShowGameOver();
                break;
        }
    }

    void ShowGameOver()
    {
        var rules = PickleballRulesEngine.Instance;
        string winner = rules.playerAScore > rules.playerBScore ? "Host" : "Opponent";
        if (gameOverText) gameOverText.text = $"{winner} Wins!";
        ResolvePanelReferences();
        SetPanelActive(gameOverPanel, true);
        SetPanelActive(hudPanel, false);
        EnsureUiInputReady();

        // Restore cursor for menu interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (playAgainButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playAgainButton.gameObject);

        // Fire off the win VFX
        VFXManager.Instance?.PlayGameWin();
    }

    // Main Menu Buttons

    public void OnPlayButton()
    {
        if (NetworkClient.active || NetworkServer.active)
        {
            // Multiplayer: ask the server to reload the scene for everyone.
            foreach (var npc in FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None))
            {
                if (npc.isLocalPlayer) { npc.CmdRequestRestart(); return; }
            }
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        }
    }

    public void OnHostButton()
    {
        EnsureNetworkManager();
        SetPanelActive(mainMenuPanel, false);
        SetPanelActive(lobbyPanel, true);
        NetworkLobbyManager.Instance?.HostLobby();
    }

    public void OnJoinButton()
    {
        EnsureNetworkManager();
        SetPanelActive(mainMenuPanel, false);
        SetPanelActive(joinPanel, true);
        // JoinPanel.OnEnable calls SearchFriendLobbies automatically.
    }

    public void ShowMainMenu()
    {
        SetPanelActive(mainMenuPanel, true);
        SetPanelActive(lobbyPanel, false);
        SetPanelActive(joinPanel, false);
    }

    public void ShowLobby()
    {
        SetPanelActive(mainMenuPanel, false);
        SetPanelActive(joinPanel, false);
        SetPanelActive(lobbyPanel, true);
    }

    public void OnQuitButton()
    {
        Application.Quit();
    }

    // Instantiates the NetworkLobbyManager prefab the first time multiplayer is requested.
    // Keeps networking completely out of the single-player flow.
    void EnsureNetworkManager()
    {
        if (NetworkLobbyManager.Instance != null) return;
        if (networkManagerPrefab == null)
        {
            Debug.LogError("[UIManager] networkManagerPrefab is not assigned. Drag the NetworkLobbyManager prefab into UIManager in the Inspector.");
            return;
        }
        Instantiate(networkManagerPrefab);
    }

    void ResolvePanelReferences()
    {
        if (mainMenuPanel == null) mainMenuPanel = FindSceneObjectByName("MainMenuPanel");
        if (lobbyPanel == null) lobbyPanel = FindSceneObjectByName("LobbyPanel");
        if (joinPanel == null) joinPanel = FindSceneObjectByName("JoinPanel");
        if (hudPanel == null) hudPanel = FindSceneObjectByName("HUDPanel");
        if (gameOverPanel == null) gameOverPanel = FindSceneObjectByName("GameOverPanel");
    }

    GameObject FindSceneObjectByName(string objectName)
    {
        foreach (Transform child in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (child.name == objectName)
                return child.gameObject;
        }

        return null;
    }

    void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }

    void EnsureUiInputReady()
    {
        var eventSystem = EventSystem.current ?? FindAnyObjectByType<EventSystem>();
        if (eventSystem == null) return;

        var uiInputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (uiInputModule == null) return;

        if (uiInputModule.actionsAsset == null || uiInputModule.leftClick == null || uiInputModule.point == null)
            uiInputModule.AssignDefaultActions();

        uiInputModule.enabled = true;
        eventSystem.enabled = true;
    }
}
