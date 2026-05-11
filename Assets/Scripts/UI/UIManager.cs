using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using Mirror;
using System.Collections.Generic;

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
    public GameObject pausePanel;

    [Header("Game Over")]
    public Button playAgainButton;

    [Header("Pause")]
    public Button resumeButton;
    public Button pauseQuitButton;

    [Header("Main Menu")]
    [SerializeField] private Button mainMenuMusicToggleButton;
    [SerializeField] private TextMeshProUGUI mainMenuMusicToggleText;
    [SerializeField] private Button mainMenuMatchModeToggleButton;
    [SerializeField] private TextMeshProUGUI mainMenuMatchModeToggleText;
    private int latestScoreA;
    private int latestScoreB;
    private bool isPaused;
    private float timeScaleBeforePause = 1f;
    private readonly List<PlayerController> pausedPlayerControllers = new();

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
        EnsurePausePanel();
        SetPanelActive(pausePanel, false);
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
            EnsureMainMenuMatchModeToggle();
            EnsureMainMenuAudioToggle();
            ApplyMainMenuSceneAudioMute(IsMainMenuMusicMuted());
        }
    }

    private void Update()
    {
        if (GameStateManager.Instance == null || Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();

        if (Keyboard.current.f12Key.wasPressedThisFrame)
            ResetGame();
    }

    void ResetGame()
    {
        if (isPaused)
            SetPaused(false);

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnStateChanged;

        if (isPaused)
            Time.timeScale = 1f;

        if (Instance == this)
            Instance = null;
    }

    // -- Scoreboard

    public void UpdateScoreBoard(int scoreA, int scoreB, int serving)
    {
        latestScoreA = scoreA;
        latestScoreB = scoreB;

        int localTeam = GetLocalTeamId();
        int localPlayer = GetLocalPlayerId();
        int servingTeam = GetTeamForPlayer(serving);
        int myScore = localTeam == 1 ? scoreB : scoreA;
        int theirScore = localTeam == 1 ? scoreA : scoreB;

        if (playerAScoreText) playerAScoreText.text = $"YOU: {myScore}";
        if (playerBScoreText) playerBScoreText.text = $"Opponent: {theirScore}";
        if (servingIndicatorText)
            servingIndicatorText.text = BuildServingIndicator(serving, servingTeam, localPlayer, localTeam);
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
                if (isPaused)
                    SetPaused(false);
                ShowGameOver();
                break;
        }
    }

    void ShowGameOver()
    {
        int localTeam = GetLocalTeamId();
        int winningTeam = latestScoreA > latestScoreB ? 0 : 1;
        if (gameOverText) gameOverText.text = winningTeam == localTeam ? "You Win!" : "You Lose!";
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
            // Multiplayer: ask the server to reset the active match for everyone.
            foreach (var npc in FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None))
            {
                if (npc.isLocalPlayer) { npc.CmdRequestRestart(); return; }
            }

            if (NetworkServer.active)
            {
                NetworkGameManager.Instance?.RestartMatch();
                return;
            }

            Debug.LogWarning("[UIManager] Could not find the local network player to request a restart.");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        }
    }

    public void ShowGameplayForRestart(int scoreA, int scoreB, int serving)
    {
        ResolvePanelReferences();
        if (isPaused)
            SetPaused(false);

        SetPanelActive(gameOverPanel, false);
        SetPanelActive(pausePanel, false);
        SetPanelActive(hudPanel, true);
        if (announcementText != null) announcementText.gameObject.SetActive(false);
        UpdateScoreBoard(scoreA, scoreB, serving);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        if (GameStateManager.Instance != null)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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

    public void OnResumeButton()
    {
        SetPaused(false);
    }

    public void OnPauseQuitButton()
    {
        SetPaused(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    public void OnMainMenuMusicToggle()
    {
        bool muted;
        if (SoundManager.Instance != null)
        {
            muted = SoundManager.Instance.ToggleMainMenuMusic();
        }
        else
        {
            muted = !IsMainMenuMusicMuted();
            PlayerPrefs.SetInt(SoundManager.MainMenuMusicMutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        ApplyMainMenuSceneAudioMute(muted);
        RefreshMainMenuMusicToggleText();
    }

    public void OnMainMenuMatchModeToggle()
    {
        MatchSessionConfig.ToggleMode();
        RefreshMainMenuMatchModeToggleText();
        ApplySelectedMatchModeToNetworkManager();
    }

    // Instantiates the NetworkLobbyManager prefab the first time multiplayer is requested.
    // Keeps networking completely out of the single-player flow.
    void EnsureNetworkManager()
    {
        if (NetworkLobbyManager.Instance != null)
        {
            ApplySelectedMatchModeToNetworkManager();
            return;
        }

        if (networkManagerPrefab == null)
        {
            Debug.LogError("[UIManager] networkManagerPrefab is not assigned. Drag the NetworkLobbyManager prefab into UIManager in the Inspector.");
            return;
        }
        Instantiate(networkManagerPrefab);
        ApplySelectedMatchModeToNetworkManager();
    }

    void ResolvePanelReferences()
    {
        if (mainMenuPanel == null) mainMenuPanel = FindSceneObjectByName("MainMenuPanel");
        if (lobbyPanel == null) lobbyPanel = FindSceneObjectByName("LobbyPanel");
        if (joinPanel == null) joinPanel = FindSceneObjectByName("JoinPanel");
        if (hudPanel == null) hudPanel = FindSceneObjectByName("HUDPanel");
        if (gameOverPanel == null) gameOverPanel = FindSceneObjectByName("GameOverPanel");
        if (pausePanel == null) pausePanel = FindSceneObjectByName("PausePanel");
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

    void EnsureMainMenuAudioToggle()
    {
        if (mainMenuPanel == null) ResolvePanelReferences();
        if (mainMenuPanel == null) return;

        ResolveMainMenuToggle(
            "MusicToggleButton",
            ref mainMenuMusicToggleButton,
            ref mainMenuMusicToggleText);

        ConfigureMainMenuMusicToggle(mainMenuMusicToggleButton);
        RefreshMainMenuMusicToggleText();
    }

    void EnsureMainMenuMatchModeToggle()
    {
        if (mainMenuPanel == null) ResolvePanelReferences();
        if (mainMenuPanel == null) return;

        ResolveMainMenuToggle(
            "MatchModeToggleButton",
            ref mainMenuMatchModeToggleButton,
            ref mainMenuMatchModeToggleText);

        ConfigureMainMenuMatchModeToggle(mainMenuMatchModeToggleButton);
        RefreshMainMenuMatchModeToggleText();
    }

    void ResolveMainMenuToggle(string objectName, ref Button button, ref TextMeshProUGUI label)
    {
        if (button == null)
        {
            Transform existing = mainMenuPanel.transform.Find(objectName);
            if (existing != null)
                button = existing.GetComponent<Button>();
        }

        if (label == null && button != null)
            label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (button == null)
            Debug.LogWarning($"[UIManager] Main menu toggle '{objectName}' is missing from the MainMenuPanel hierarchy.");
    }

    void TogglePause()
    {
        if (!CanUseSinglePlayerPause()) return;

        if (isPaused)
        {
            SetPaused(false);
            return;
        }

        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.GameOver || state == GameState.ResultOfRound)
            return;

        SetPaused(true);
    }

    bool CanUseSinglePlayerPause()
    {
        return GameStateManager.Instance != null
            && !NetworkClient.active
            && !NetworkServer.active;
    }

    void SetPaused(bool paused)
    {
        if (paused == isPaused) return;

        EnsurePausePanel();
        isPaused = paused;

        if (paused)
        {
            timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            SetGameplayInputPaused(true);
            SetPanelActive(pausePanel, true);
            EnsureUiInputReady();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (resumeButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }
        else
        {
            Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;
            SetPanelActive(pausePanel, false);
            SetGameplayInputPaused(false);

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            if (GameStateManager.Instance != null
                && GameStateManager.Instance.CurrentState != GameState.GameOver)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void SetGameplayInputPaused(bool paused)
    {
        if (paused)
        {
            pausedPlayerControllers.Clear();
            foreach (PlayerController controller in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!controller.enabled) continue;
                pausedPlayerControllers.Add(controller);
                controller.enabled = false;
            }

            return;
        }

        foreach (PlayerController controller in pausedPlayerControllers)
        {
            if (controller != null)
                controller.enabled = true;
        }

        pausedPlayerControllers.Clear();
    }

    void EnsurePausePanel()
    {
        if (pausePanel != null)
        {
            ConfigurePauseButtons();
            return;
        }

        if (gameOverPanel == null)
            ResolvePanelReferences();

        if (gameOverPanel == null)
            return;

        pausePanel = Instantiate(gameOverPanel, gameOverPanel.transform.parent);
        pausePanel.name = "PausePanel";

        Transform gameOverText = pausePanel.transform.Find("GameOverText");
        if (gameOverText != null)
            gameOverText.gameObject.SetActive(false);

        Button templateButton = pausePanel.GetComponentInChildren<Button>(true);
        if (templateButton == null)
            return;

        resumeButton = templateButton;
        resumeButton.name = "ResumeButton";
        ConfigurePauseButton(resumeButton, "Resume", OnResumeButton, new Vector2(0f, 40f));

        pauseQuitButton = Instantiate(resumeButton, resumeButton.transform.parent);
        pauseQuitButton.name = "QuitButton";
        ConfigurePauseButton(pauseQuitButton, "Quit", OnPauseQuitButton, new Vector2(0f, -40f));
    }

    void ConfigurePauseButtons()
    {
        if (resumeButton == null && pausePanel != null)
        {
            Transform existingResume = pausePanel.transform.Find("ResumeButton");
            if (existingResume != null)
                resumeButton = existingResume.GetComponent<Button>();
        }

        if (pauseQuitButton == null && pausePanel != null)
        {
            Transform existingQuit = pausePanel.transform.Find("QuitButton");
            if (existingQuit != null)
                pauseQuitButton = existingQuit.GetComponent<Button>();
        }

        ConfigurePauseButton(resumeButton, "Resume", OnResumeButton, new Vector2(0f, 40f));
        ConfigurePauseButton(pauseQuitButton, "Quit", OnPauseQuitButton, new Vector2(0f, -40f));
    }

    void ConfigurePauseButton(Button button, string label, UnityEngine.Events.UnityAction action, Vector2 position)
    {
        if (button == null) return;

        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect != null)
            rect.anchoredPosition = position;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = label;

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        button.interactable = true;
    }

    void ConfigureMainMenuMusicToggle(Button button)
    {
        if (button == null) return;

        // The toggle is cloned from a scene button, so replace the whole UnityEvent
        // to drop any persistent Inspector listener copied from the template.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(OnMainMenuMusicToggle);
        button.interactable = true;
    }

    void ConfigureMainMenuMatchModeToggle(Button button)
    {
        if (button == null) return;

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(OnMainMenuMatchModeToggle);
        button.interactable = true;
    }

    void RefreshMainMenuMusicToggleText()
    {
        if (mainMenuMusicToggleText == null) return;
        bool muted = IsMainMenuMusicMuted();
        mainMenuMusicToggleText.text = muted ? "Music: Off" : "Music: On";
    }

    void RefreshMainMenuMatchModeToggleText()
    {
        if (mainMenuMatchModeToggleText == null) return;
        mainMenuMatchModeToggleText.text = MatchSessionConfig.SelectedMatchMode == MatchMode.Doubles
            ? "Mode: Doubles"
            : "Mode: Singles";
    }

    void ApplySelectedMatchModeToNetworkManager()
    {
        if (NetworkLobbyManager.Instance == null) return;
        NetworkLobbyManager.Instance.matchMode = MatchSessionConfig.SelectedMatchMode;
        NetworkLobbyManager.Instance.maxConnections = NetworkLobbyManager.Instance.RequiredPlayerCount;
    }

    bool IsMainMenuMusicMuted()
    {
        if (SoundManager.Instance != null)
            return SoundManager.Instance.IsMainMenuMusicMuted();

        return PlayerPrefs.GetInt(SoundManager.MainMenuMusicMutedKey, 0) == 1;
    }

    void ApplyMainMenuSceneAudioMute(bool muted)
    {
        if (GameStateManager.Instance != null) return;

        GameObject sceneSoundManager = FindSceneObjectByName("SoundManager");
        if (sceneSoundManager != null)
        {
            foreach (AudioSource source in sceneSoundManager.GetComponents<AudioSource>())
                source.mute = muted;
        }

        if (SoundManager.Instance != null && SoundManager.Instance.musicSource != null)
            SoundManager.Instance.musicSource.mute = muted;
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

    int GetLocalPlayerId()
    {
        foreach (NetworkPlayerController player in FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None))
        {
            if (player.isLocalPlayer)
                return player.PlayerId;
        }

        if (TryGetLocalHumanIdentity(out PlayerIdentity humanIdentity))
            return humanIdentity.PlayerId;

        foreach (PlayerIdentity identity in FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None))
        {
            if (identity.IsAssigned && identity.GetComponent<AIBot>() == null)
                return identity.PlayerId;
        }

        return NetworkClient.active && !NetworkServer.active ? 1 : 0;
    }

    int GetLocalTeamId()
    {
        foreach (NetworkPlayerController player in FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None))
        {
            if (player.isLocalPlayer)
                return player.TeamId;
        }

        if (TryGetLocalHumanIdentity(out PlayerIdentity humanIdentity))
            return humanIdentity.TeamId;

        foreach (PlayerIdentity identity in FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None))
        {
            if (identity.IsAssigned && identity.GetComponent<AIBot>() == null)
                return identity.TeamId;
        }

        return GetTeamForPlayer(GetLocalPlayerId());
    }

    bool TryGetLocalHumanIdentity(out PlayerIdentity identity)
    {
        foreach (PlayerController controller in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (controller.GetComponent<AIBot>() != null) continue;
            if (controller.TryGetComponent(out identity) && identity.IsAssigned)
                return true;
        }

        identity = null;
        return false;
    }

    string BuildServingIndicator(int serving, int servingTeam, int localPlayer, int localTeam)
    {
        string serverLabel = serving == localPlayer
            ? "YOU"
            : servingTeam == localTeam ? "Partner" : "Opponent";

        if (!IsDoublesHudActive())
            return $"Serving: {serverLabel}";

        string text = $"Serve: {serverLabel} S{GetServerNumber()}";
        int receiver = GetCurrentReceiverPlayer();
        int receiverTeam = GetTeamForPlayer(receiver);

        if (receiverTeam == localTeam)
        {
            string receiverLabel = receiver == localPlayer ? "YOU" : "Partner";
            text += $" | Receive: {receiverLabel}";
        }

        return text;
    }

    int GetTeamForPlayer(int playerId)
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules != null && rules.ActiveMatchMode == MatchMode.Singles)
            return Mathf.Clamp(playerId, 0, 1);

        return PlayerIdentity.TeamOfPlayer(playerId);
    }

    bool IsDoublesHudActive()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        if (rules != null)
            return rules.ActiveMatchMode == MatchMode.Doubles;

        if (NetworkLobbyManager.Instance != null)
            return NetworkLobbyManager.Instance.matchMode == MatchMode.Doubles;

        return MatchSessionConfig.SelectedMatchMode == MatchMode.Doubles;
    }

    int GetServerNumber()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null ? rules.ServerNumber : 1;
    }

    int GetCurrentReceiverPlayer()
    {
        PickleballRulesEngine rules = PickleballRulesEngine.Instance;
        return rules != null ? rules.CurrentReceiverPlayer : 1;
    }
}
