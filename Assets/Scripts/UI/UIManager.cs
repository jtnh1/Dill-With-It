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
    public Button pauseOptionsButton;

    [Header("Options")]
    public GameObject optionsPanel;
    [Tooltip("Drag the InputManager.asset here so the Keybinds tab can read & rebind player actions.")]
    [SerializeField] private InputManager inputManager;
    private Slider heightOffsetSlider;
    private Slider depthOffsetSlider;
    private TextMeshProUGUI heightOffsetValueText;
    private TextMeshProUGUI depthOffsetValueText;
    private Button optionsBackButton;
    private Button mainMenuOptionsButton;
    private GameObject optionsReturnPanel;
    private GameObject cameraTabContent;
    private GameObject keybindsTabContent;
    private Button cameraTabButton;
    private Button keybindsTabButton;
    private readonly List<KeybindRow> keybindRows = new();
    private bool isRebinding;

    class KeybindRow
    {
        public string displayName;
        public System.Func<UnityEngine.InputSystem.InputAction> getAction;
        public TextMeshProUGUI bindingDisplayText;
        public Button rebindButton;
        public TextMeshProUGUI rebindButtonLabel;
    }

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
        EnsureOptionsPanel();
        SetPanelActive(optionsPanel, false);
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
            EnsureMainMenuOptionsButton();
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

    public void OnOptionsButton()
    {
        EnsureOptionsPanel();
        if (optionsPanel == null) return;

        // Remember which panel asked for Options so Back returns there.
        if (pausePanel != null && pausePanel.activeSelf)
            optionsReturnPanel = pausePanel;
        else if (mainMenuPanel != null && mainMenuPanel.activeSelf)
            optionsReturnPanel = mainMenuPanel;
        else
            optionsReturnPanel = mainMenuPanel != null ? mainMenuPanel : pausePanel;

        SetPanelActive(optionsReturnPanel, false);
        SetPanelActive(optionsPanel, true);

        if (heightOffsetSlider != null) heightOffsetSlider.SetValueWithoutNotify(CameraSettings.LoadHeightOffset());
        if (depthOffsetSlider != null)  depthOffsetSlider.SetValueWithoutNotify(CameraSettings.LoadDepthOffset());
        RefreshHeightOffsetLabel();
        RefreshDepthOffsetLabel();

        EnsureUiInputReady();
        if (optionsBackButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(optionsBackButton.gameObject);
    }

    public void OnCloseOptionsButton()
    {
        SetPanelActive(optionsPanel, false);
        if (optionsReturnPanel != null)
            SetPanelActive(optionsReturnPanel, true);

        if (optionsReturnPanel == pausePanel && resumeButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);

        optionsReturnPanel = null;
    }

    void OnHeightOffsetChanged(float value)
    {
        CameraSettings.SaveHeightOffset(value);
        ApplyLiveCameraSettings();
        RefreshHeightOffsetLabel();
    }

    void OnDepthOffsetChanged(float value)
    {
        CameraSettings.SaveDepthOffset(value);
        ApplyLiveCameraSettings();
        RefreshDepthOffsetLabel();
    }

    void ApplyLiveCameraSettings()
    {
        CameraController cam = FindAnyObjectByType<CameraController>();
        if (cam == null) return;
        cam.heightOffset = CameraSettings.LoadHeightOffset();
        cam.depthOffset  = CameraSettings.LoadDepthOffset();
    }

    void RefreshHeightOffsetLabel()
    {
        if (heightOffsetValueText == null) return;
        float value = heightOffsetSlider != null ? heightOffsetSlider.value : CameraSettings.LoadHeightOffset();
        heightOffsetValueText.text = value.ToString("F1");
    }

    void RefreshDepthOffsetLabel()
    {
        if (depthOffsetValueText == null) return;
        float value = depthOffsetSlider != null ? depthOffsetSlider.value : CameraSettings.LoadDepthOffset();
        depthOffsetValueText.text = value.ToString("F1");
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
        if (optionsPanel == null) optionsPanel = FindSceneObjectByName("OptionsPanel");
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

        // While rebinding, let the InputAction consume ESC for cancel — don't unpause or close panels.
        if (isRebinding) return;

        // If Options is up over the pause panel, ESC closes it back to pause instead of unpausing.
        if (optionsPanel != null && optionsPanel.activeSelf && isPaused)
        {
            OnCloseOptionsButton();
            return;
        }

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
            SetPanelActive(optionsPanel, false);
            optionsReturnPanel = null;
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
        ConfigurePauseButton(resumeButton, "Resume", OnResumeButton, new Vector2(0f, 80f));

        pauseOptionsButton = Instantiate(resumeButton, resumeButton.transform.parent);
        pauseOptionsButton.name = "OptionsButton";
        ConfigurePauseButton(pauseOptionsButton, "Options", OnOptionsButton, new Vector2(0f, 0f));

        pauseQuitButton = Instantiate(resumeButton, resumeButton.transform.parent);
        pauseQuitButton.name = "QuitButton";
        ConfigurePauseButton(pauseQuitButton, "Quit", OnPauseQuitButton, new Vector2(0f, -80f));
    }

    void ConfigurePauseButtons()
    {
        if (resumeButton == null && pausePanel != null)
        {
            Transform existingResume = pausePanel.transform.Find("ResumeButton");
            if (existingResume != null)
                resumeButton = existingResume.GetComponent<Button>();
        }

        if (pauseOptionsButton == null && pausePanel != null)
        {
            Transform existingOptions = pausePanel.transform.Find("OptionsButton");
            if (existingOptions != null)
                pauseOptionsButton = existingOptions.GetComponent<Button>();
        }

        if (pauseQuitButton == null && pausePanel != null)
        {
            Transform existingQuit = pausePanel.transform.Find("QuitButton");
            if (existingQuit != null)
                pauseQuitButton = existingQuit.GetComponent<Button>();
        }

        // PausePanel saved before the Options feature only has Resume + Quit. Create the
        // missing Options button by cloning Resume so the pause menu is always complete.
        if (pauseOptionsButton == null && resumeButton != null)
        {
            pauseOptionsButton = Instantiate(resumeButton, resumeButton.transform.parent);
            pauseOptionsButton.name = "OptionsButton";
        }

        ConfigurePauseButton(resumeButton, "Resume", OnResumeButton, new Vector2(0f, 80f));
        ConfigurePauseButton(pauseOptionsButton, "Options", OnOptionsButton, new Vector2(0f, 0f));
        ConfigurePauseButton(pauseQuitButton, "Quit", OnPauseQuitButton, new Vector2(0f, -80f));
    }

    void EnsureMainMenuOptionsButton()
    {
        if (mainMenuPanel == null) return;

        Transform existing = mainMenuPanel.transform.Find("OptionsButton");
        if (existing == null)
        {
            Debug.LogWarning("[UIManager] OptionsButton missing from MainMenuPanel hierarchy.");
            return;
        }

        mainMenuOptionsButton = existing.GetComponent<Button>();
        if (mainMenuOptionsButton == null) return;

        // Re-bind onClick at runtime so a Wire-Menu-Buttons run isn't strictly required
        // (matches how the music + match-mode toggles bind themselves).
        mainMenuOptionsButton.onClick = new Button.ButtonClickedEvent();
        mainMenuOptionsButton.onClick.AddListener(OnOptionsButton);
        mainMenuOptionsButton.interactable = true;
    }

    void EnsureOptionsPanel()
    {
        if (optionsPanel != null)
        {
            ConfigureOptionsPanel();
            return;
        }

        Transform canvasParent = null;
        if (gameOverPanel != null) canvasParent = gameOverPanel.transform.parent;
        else if (mainMenuPanel != null) canvasParent = mainMenuPanel.transform.parent;
        if (canvasParent == null)
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null) canvasParent = canvas.transform;
        }
        if (canvasParent == null) return;

        optionsPanel = new GameObject("OptionsPanel", typeof(RectTransform), typeof(Image));
        optionsPanel.transform.SetParent(canvasParent, false);

        RectTransform rootRect = optionsPanel.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image bg = optionsPanel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        BuildOptionsPanelContent();
    }

    void BuildOptionsPanelContent()
    {
        CreateLabel(optionsPanel.transform, "TitleText", "Options", 56,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -60f), new Vector2(800f, 100f),
            TMPro.TextAlignmentOptions.Center);

        cameraTabButton = BuildTabButton("CameraTabButton", "Camera",
            new Vector2(-110f, -160f), () => ShowOptionsTab(true));
        keybindsTabButton = BuildTabButton("KeybindsTabButton", "Keybinds",
            new Vector2(110f, -160f), () => ShowOptionsTab(false));

        cameraTabContent   = BuildTabContent("CameraContent");
        keybindsTabContent = BuildTabContent("KeybindsContent");

        BuildCameraTabContent(cameraTabContent.transform);
        BuildKeybindsTabContent(keybindsTabContent.transform);

        optionsBackButton = BuildOptionsBackButton("BackButton", "Back",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 80f), new Vector2(220f, 60f),
            OnCloseOptionsButton);

        ShowOptionsTab(true);
    }

    void BuildCameraTabContent(Transform parent)
    {
        BuildSliderRow(parent, "HeightOffset", "Height Offset",
            CameraSettings.MinHeightOffset, CameraSettings.MaxHeightOffset,
            CameraSettings.LoadHeightOffset(),
            new Vector2(0f, -40f),
            OnHeightOffsetChanged,
            out heightOffsetSlider, out heightOffsetValueText);

        BuildSliderRow(parent, "DepthOffset", "Depth Offset",
            CameraSettings.MinDepthOffset, CameraSettings.MaxDepthOffset,
            CameraSettings.LoadDepthOffset(),
            new Vector2(0f, -120f),
            OnDepthOffsetChanged,
            out depthOffsetSlider, out depthOffsetValueText);
    }

    void BuildKeybindsTabContent(Transform parent)
    {
        keybindRows.Clear();

        // Defer action lookups via lambdas so we can build rows even before InputManager.Initialize runs.
        AddKeybindRow(parent, "Forehand", () => inputManager?.actions?.Player.SwingForehand,  -10f);
        AddKeybindRow(parent, "Backhand", () => inputManager?.actions?.Player.SwingBackhand, -65f);
        AddKeybindRow(parent, "Dink",     () => inputManager?.actions?.Player.Dink,          -120f);
        AddKeybindRow(parent, "Lob",      () => inputManager?.actions?.Player.Lob,           -175f);
        AddKeybindRow(parent, "Smash",    () => inputManager?.actions?.Player.Smash,         -230f);
        AddKeybindRow(parent, "Block",    () => inputManager?.actions?.Player.Block,         -285f);
    }

    void AddKeybindRow(Transform parent, string displayName,
        System.Func<UnityEngine.InputSystem.InputAction> getAction, float yOffset)
    {
        KeybindRow row = new KeybindRow { displayName = displayName, getAction = getAction };

        GameObject rowGO = new GameObject(displayName + "Row", typeof(RectTransform));
        rowGO.transform.SetParent(parent, false);
        RectTransform rowRect = rowGO.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, yOffset);
        rowRect.sizeDelta = new Vector2(720f, 50f);

        // Action label (left)
        CreateLabel(rowGO.transform, displayName + "Label", displayName, 26,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0f), new Vector2(220f, 50f),
            TMPro.TextAlignmentOptions.MidlineLeft);

        // Current binding text (middle)
        GameObject bindingGO = CreateLabel(rowGO.transform, displayName + "Binding", "—", 24,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(240f, 0f), new Vector2(280f, 50f),
            TMPro.TextAlignmentOptions.Midline);
        row.bindingDisplayText = bindingGO.GetComponent<TextMeshProUGUI>();

        // Rebind button (right)
        row.rebindButton = BuildOptionsBackButton(displayName + "RebindButton", "Rebind",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(540f, 0f), new Vector2(160f, 40f),
            () => StartRebindRow(row));
        // Reparent the button onto this row (BuildOptionsBackButton parented it to optionsPanel).
        row.rebindButton.transform.SetParent(rowGO.transform, false);
        RectTransform btnRect = row.rebindButton.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0f, 0.5f);
        btnRect.anchorMax = new Vector2(0f, 0.5f);
        btnRect.pivot = new Vector2(0f, 0.5f);
        btnRect.anchoredPosition = new Vector2(540f, -20f);
        btnRect.sizeDelta = new Vector2(160f, 40f);
        row.rebindButtonLabel = row.rebindButton.GetComponentInChildren<TextMeshProUGUI>(true);

        keybindRows.Add(row);
    }

    Button BuildTabButton(string objName, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        Button btn = BuildOptionsBackButton(objName, label,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            anchoredPos, new Vector2(180f, 50f),
            onClick);
        return btn;
    }

    GameObject BuildTabContent(string objName)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(optionsPanel.transform, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -210f);
        rect.sizeDelta = new Vector2(800f, 420f);
        return go;
    }

    void ShowOptionsTab(bool cameraActive)
    {
        if (cameraTabContent != null)   cameraTabContent.SetActive(cameraActive);
        if (keybindsTabContent != null) keybindsTabContent.SetActive(!cameraActive);

        Color active   = Color.white;
        Color inactive = new Color(0.55f, 0.55f, 0.55f, 1f);
        if (cameraTabButton != null)
        {
            Image img = cameraTabButton.GetComponent<Image>();
            if (img != null) img.color = cameraActive ? active : inactive;
        }
        if (keybindsTabButton != null)
        {
            Image img = keybindsTabButton.GetComponent<Image>();
            if (img != null) img.color = cameraActive ? inactive : active;
        }

        if (!cameraActive)
        {
            EnsureInputManagerInitialized();
            RefreshKeybindDisplays();
        }
    }

    void EnsureInputManagerInitialized()
    {
        if (inputManager == null) return;
        inputManager.Initialize();
    }

    void RefreshKeybindDisplays()
    {
        foreach (KeybindRow row in keybindRows)
        {
            UnityEngine.InputSystem.InputAction action = row.getAction?.Invoke();
            if (row.bindingDisplayText == null) continue;
            row.bindingDisplayText.text = action != null
                ? inputManager.GetBindingDisplayString(action, 0)
                : "—";
        }
    }

    void StartRebindRow(KeybindRow row)
    {
        if (isRebinding) return;
        if (inputManager == null) { Debug.LogWarning("[UIManager] InputManager reference missing — cannot rebind."); return; }

        UnityEngine.InputSystem.InputAction action = row.getAction?.Invoke();
        if (action == null) return;

        isRebinding = true;
        if (row.bindingDisplayText != null) row.bindingDisplayText.text = "Press a key…";
        SetAllRebindButtonsInteractable(false);

        inputManager.StartRebind(action, 0, () =>
        {
            isRebinding = false;
            SetAllRebindButtonsInteractable(true);
            RefreshKeybindDisplays();
        });
    }

    void SetAllRebindButtonsInteractable(bool interactable)
    {
        foreach (KeybindRow row in keybindRows)
            if (row.rebindButton != null) row.rebindButton.interactable = interactable;
    }

    void BuildSliderRow(Transform parent, string id, string label, float min, float max, float initial,
        Vector2 anchoredPos,
        UnityEngine.Events.UnityAction<float> onChanged,
        out Slider slider, out TextMeshProUGUI valueText)
    {
        GameObject row = new GameObject(id + "Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = anchoredPos;
        rowRect.sizeDelta = new Vector2(720f, 60f);

        CreateLabel(row.transform, id + "Label", label, 28,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0f), new Vector2(220f, 50f),
            TMPro.TextAlignmentOptions.MidlineLeft);

        GameObject sliderGO = DefaultControls.CreateSlider(default(DefaultControls.Resources));
        sliderGO.name = id + "Slider";
        sliderGO.transform.SetParent(row.transform, false);
        slider = sliderGO.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(initial);

        RectTransform sliderRect = sliderGO.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(0f, 0.5f);
        sliderRect.pivot = new Vector2(0f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(240f, -10f);
        sliderRect.sizeDelta = new Vector2(380f, 30f);

        GameObject valueGO = CreateLabel(row.transform, id + "Value", initial.ToString("F1"), 28,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, 0f), new Vector2(80f, 50f),
            TMPro.TextAlignmentOptions.MidlineRight);
        valueText = valueGO.GetComponent<TextMeshProUGUI>();

        slider.onValueChanged.AddListener(onChanged);
    }

    GameObject CreateLabel(Transform parent, string objName, string text, float fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size,
        TMPro.TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.enableWordWrapping = false;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return go;
    }

    Button BuildOptionsBackButton(string objName, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        // Prefer cloning an existing TMP-styled button so visuals match the rest of the project.
        Button template = resumeButton;
        if (template == null && pausePanel != null)
            template = pausePanel.GetComponentInChildren<Button>(true);
        if (template == null && mainMenuPanel != null)
            template = mainMenuPanel.GetComponentInChildren<Button>(true);

        Button btn;
        if (template != null)
        {
            btn = Instantiate(template, optionsPanel.transform);
            btn.name = objName;
            TextMeshProUGUI text = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = label;
        }
        else
        {
            GameObject btnGO = DefaultControls.CreateButton(default(DefaultControls.Resources));
            btnGO.name = objName;
            btnGO.transform.SetParent(optionsPanel.transform, false);
            Text legacyText = btnGO.GetComponentInChildren<Text>();
            if (legacyText != null) legacyText.text = label;
            btn = btnGO.GetComponent<Button>();
        }

        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(onClick);
        btn.interactable = true;

        RectTransform btnRect = btn.GetComponent<RectTransform>();
        btnRect.anchorMin = anchorMin;
        btnRect.anchorMax = anchorMax;
        btnRect.pivot = pivot;
        btnRect.anchoredPosition = anchoredPos;
        btnRect.sizeDelta = size;
        return btn;
    }

    void ConfigureOptionsPanel()
    {
        // Re-bind references for a scene-authored OptionsPanel (rare, used if someone hand-builds it later).
        if (optionsPanel == null) return;

        if (heightOffsetSlider == null)
            heightOffsetSlider = FindChildComponent<Slider>(optionsPanel.transform, "HeightOffsetSlider");
        if (depthOffsetSlider == null)
            depthOffsetSlider = FindChildComponent<Slider>(optionsPanel.transform, "DepthOffsetSlider");
        if (heightOffsetValueText == null)
            heightOffsetValueText = FindChildComponent<TextMeshProUGUI>(optionsPanel.transform, "HeightOffsetValue");
        if (depthOffsetValueText == null)
            depthOffsetValueText = FindChildComponent<TextMeshProUGUI>(optionsPanel.transform, "DepthOffsetValue");
        if (optionsBackButton == null)
            optionsBackButton = FindChildComponent<Button>(optionsPanel.transform, "BackButton");

        if (heightOffsetSlider != null)
        {
            heightOffsetSlider.minValue = CameraSettings.MinHeightOffset;
            heightOffsetSlider.maxValue = CameraSettings.MaxHeightOffset;
            heightOffsetSlider.SetValueWithoutNotify(CameraSettings.LoadHeightOffset());
            heightOffsetSlider.onValueChanged.RemoveListener(OnHeightOffsetChanged);
            heightOffsetSlider.onValueChanged.AddListener(OnHeightOffsetChanged);
        }
        if (depthOffsetSlider != null)
        {
            depthOffsetSlider.minValue = CameraSettings.MinDepthOffset;
            depthOffsetSlider.maxValue = CameraSettings.MaxDepthOffset;
            depthOffsetSlider.SetValueWithoutNotify(CameraSettings.LoadDepthOffset());
            depthOffsetSlider.onValueChanged.RemoveListener(OnDepthOffsetChanged);
            depthOffsetSlider.onValueChanged.AddListener(OnDepthOffsetChanged);
        }
        if (optionsBackButton != null)
        {
            optionsBackButton.onClick = new Button.ButtonClickedEvent();
            optionsBackButton.onClick.AddListener(OnCloseOptionsButton);
        }
    }

    static T FindChildComponent<T>(Transform root, string objName) where T : Component
    {
        if (root.name == objName)
        {
            T self = root.GetComponent<T>();
            if (self != null) return self;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            T result = FindChildComponent<T>(root.GetChild(i), objName);
            if (result != null) return result;
        }
        return null;
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
