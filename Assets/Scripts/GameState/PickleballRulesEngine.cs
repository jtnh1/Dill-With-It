using System.Collections;
using Mirror;
using UnityEngine;

/*
Enforces pickleball rules via a state machine:
  PreServe → FirstBounce → SecondBounce → Rally → ResultOfRound → PreServe
Two-bounce rule is enforced structurally by the state sequence.
*/

public class PickleballRulesEngine : MonoBehaviour
{
    public static PickleballRulesEngine Instance { get; private set; }

    [Header("Score")]
    public int playerAScore = 0;
    public int playerBScore = 0;
    public int winningScore = 11;
    public int mustWinBy = 2;
    public MatchMode matchMode = MatchMode.Singles;

    [Header("Ball")]
    public BallController ball;
    [Tooltip("Seconds to wait after a point before resetting the ball and transitioning to PreServe.")]
    public float ballResetDelay = 2f;
    [Tooltip("Y height and Z depth for Player A's serve position. X is computed from score.")]
    public Vector3 servePositionA = new Vector3(0f, 1.2f, -24.5f);
    [Tooltip("Y height and Z depth for Player B's serve position. X is computed from score.")]
    public Vector3 servePositionB = new Vector3(0f, 1.2f,  24.5f);
    [Tooltip("How far left/right of centre the ball is placed for each serve side.")]
    public float serveXOffset = 5f;
    [Tooltip("Y height used when teleporting players into their side's serve zone.")]
    public float playerServeY = 0.1f;
    [Tooltip("Inset applied when clamping players inside a serve zone.")]
    public float serveZoneClampMargin = 0.45f;
    [Tooltip("Guards the state machine from duplicate bounce callbacks from stacked floor colliders.")]
    public float duplicateBounceIgnoreSeconds = 0.1f;

    // Companion states
    public LastHitState LastHit        { get; private set; } = LastHitState.None;
    public BallBounceCountState BounceCount { get; private set; } = BallBounceCountState.Zero;

    // Who is currently serving: player id in singles/doubles.
    public int ServingPlayer { get; private set; } = 0;
    public int ServingTeam { get; private set; } = 0;
    public int CurrentReceiverPlayer { get; private set; } = 1;
    public int ServerNumber { get; private set; } = 1;
    public bool HasServeBeenHit { get; private set; } = false;
    public bool IsServeSetupActive =>
        GameStateManager.Instance != null
        && GameStateManager.Instance.CurrentState == GameState.PreServe
        && !HasServeBeenHit;

    // Side that last absorbed a bounce in Rally (0 = A's side, 1 = B's side, -1 = none yet)
    private int lastBounceSide = -1;
    private int lastHitPlayerId = -1;
    private int lastHitTeamId = -1;
    private int servingTeamSlot = 0;
    private bool firstServerExceptionActive = true;
    private int pendingNetFaultTeam = -1;
    private int pendingNetFaultPlayer = -1;
    private float lastAcceptedBounceTime = -999f;
    private int lastAcceptedBounceFrame = -1;

    public MatchMode ActiveMatchMode =>
        NetworkLobbyManager.Instance != null ? NetworkLobbyManager.Instance.matchMode : MatchSessionConfig.SelectedMatchMode;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        matchMode = ActiveMatchMode;
        if (ball == null) ball = FindAnyObjectByType<BallController>();
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;

        if (ball != null)
        {
            InitializeServeState();
            UpdateScoreUi();
            PlaceBallAtServePosition();
            ResetPlayersToServeZones();
        }
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
    }

    // ── Entry points ──────────────────────────────────────────────────────────

    // Fired whenever a paddle makes contact. Subscribers receive the player index (0 or 1).
    public event System.Action<int> OnHit;

    // Call after every successful paddle contact (player index: 0 = Player A, 1 = Player B).
    public void OnBallHit(int playerIndex, Vector3 hitterPosition)
    {
        if (NetworkClient.active && !NetworkServer.active) return; // clients don't run rules
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;

        Debug.Log($"[OnBallHit] ----- Player = {playerIndex}");
        int playerTeam = TeamOfPlayer(playerIndex);
        ResetBounceDeduplication();

        if (state == GameState.PreServe)
        {
            if (playerIndex != ServingPlayer)
            {
                if (HasServeBeenHit)
                {
                    bool servingTeamTouchedServe = playerTeam == ServingTeam;
                    AwardPoint(
                        servingTeamTouchedServe ? OpponentTeam(ServingTeam) : ServingTeam,
                        servingTeamTouchedServe
                            ? "Serving team touched the serve before it bounced."
                            : "Receiver volleyed the serve before it bounced.");
                }
                return;
            }

            HasServeBeenHit = true;
            if (NetworkServer.active)
                NetworkGameManager.Instance?.BroadcastServeHit();
        }

        if (TryHandleNetReplayFault(playerIndex, playerTeam))
            return;

        if (TryHandleTwoBounceRuleFault(playerIndex))
            return;

        if (TryHandleKitchenVolleyFault(playerIndex, hitterPosition))
            return;

        lastBounceSide = -1;
        lastHitPlayerId = playerIndex;
        lastHitTeamId = playerTeam;
        LastHit = playerTeam == 0 ? LastHitState.PlayerA : LastHitState.PlayerB;
        OnHit?.Invoke(playerIndex);

        // Serving player hitting in SecondBounce transitions to free Rally.
        if (GameStateManager.Instance.CurrentState == GameState.SecondBounce
            && playerTeam == ServingTeam)
        {
            lastBounceSide = -1;
            GameStateManager.Instance.TransitionTo(GameState.Rally);
        }
    }

    public void OnBallBounced(ZoneID zone, int lastHitByPlayer)
    {
        if (NetworkClient.active && !NetworkServer.active) return;
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;
        if (ShouldIgnoreDuplicateBounce(zone, lastHitByPlayer, state)) return;

        switch (state)
        {
            case GameState.PreServe:
                Debug.Log($"[OnBallBounced] State ----> PreServe ||||||||| Zone ----> {zone}");
                HandleServeBounce(zone);
                break;

            case GameState.FirstBounce:
                Debug.Log($"[OnBallBounced] State ----> FirstBounce ||||||||| Zone ----> {zone}");
                HandleFirstBounceReturn(zone, lastHitByPlayer);
                break;

            case GameState.SecondBounce:
                Debug.Log($"[OnBallBounced] State ----> SecondBounce ||||||||| Zone ----> {zone} ||||||||| lastHit ----> {lastHitByPlayer}");
                // Server let the ball bounce a second time without hitting it.
                AwardPoint(OpponentTeam(ServingTeam), "Server failed to return the ball.");
                break;

            case GameState.Rally:
                HandleRallyBounce(zone, lastHitByPlayer);
                break;
        }
    }

    public void OnBallHitNet(int lastHitByPlayer)
    {
        if (NetworkClient.active && !NetworkServer.active) return;
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.ResultOfRound || state == GameState.GameOver) return;

        pendingNetFaultPlayer = lastHitByPlayer;
        pendingNetFaultTeam = TeamOfPlayer(lastHitByPlayer);
        Debug.Log($"[Rules] Ball hit net. Waiting for landing result. LastHit={lastHitByPlayer}");
    }

    // ── State-specific bounce handlers ─────────────────────────────────────────

    void HandleServeBounce(ZoneID zone)
    {
        if (zone == ZoneID.OutOfBounds)
        {
            Debug.Log($"[HandleServeBounce] ||||||| zone = {zone}");
            AwardPoint(OpponentTeam(ServingTeam), "Serve landed out of bounds.");
            return;
        }
        if (!IsValidServeZone(zone, ServingPlayer))
        {
            Debug.Log($"[HandleServeBounce] ||||||| zone = {zone}");
            AwardPoint(OpponentTeam(ServingTeam), "Serve landed in wrong service box.");
            return;
        }
        BounceCount    = BallBounceCountState.One;
        lastBounceSide = SideOf(zone);
        ClearPendingNetFault();
        GameStateManager.Instance.TransitionTo(GameState.FirstBounce);
    }

    void HandleFirstBounceReturn(ZoneID zone, int lastHitByPlayer)
    {
        Debug.Log($"[HandleFirstBounceReturn] ||||||| zone = {zone} ||||||| lastHitByPlayer = {lastHitByPlayer}");
        if (zone == ZoneID.OutOfBounds)
        {
            // Receiver failed to keep the ball in — always faults the receiver regardless of who last touched it.
            AwardPoint(ServingTeam, "Receiver did not return the serve.");
            return;
        }

        int side = SideOf(zone);
        int receivingTeam = OpponentTeam(ServingTeam);
        int actualLastHitTeam = TeamOfPlayer(lastHitByPlayer);

        if (side == lastBounceSide)
        {
            // Ball double-bounced on the receiver's side before they returned it.
            AwardPoint(ServingTeam, "Receiver let ball bounce twice.");
            return;
        }

        if (side == receivingTeam)
        {
            AwardPoint(ServingTeam, "Receiver returned the ball into their own side.");
            return;
        }

        // Ball is on the server's side — only valid if the non-serving player actually hit it.
        // Without this check, physics bounciness can carry the ball back across without a hit.
        if (actualLastHitTeam != receivingTeam)
        {
            AwardPoint(ServingTeam, "Receiver did not return the serve.");
            return;
        }

        if (IsDoublesMode() && lastHitByPlayer != CurrentReceiverPlayer)
        {
            AwardPoint(ServingTeam, "Wrong receiver returned the serve.");
            return;
        }

        // Two-bounce rule satisfied.
        lastHitPlayerId = lastHitByPlayer;
        lastHitTeamId = actualLastHitTeam;
        LastHit = actualLastHitTeam == 0 ? LastHitState.PlayerA : LastHitState.PlayerB;
        BounceCount    = BallBounceCountState.Two;
        lastBounceSide = side;
        ClearPendingNetFault();
        GameStateManager.Instance.TransitionTo(GameState.SecondBounce);
    }

    void HandleRallyBounce(ZoneID zone, int lastHitByPlayer)
    {
        if (zone == ZoneID.OutOfBounds)
        {
            AwardPoint(OpponentTeam(TeamOfPlayer(lastHitByPlayer)), "Ball landed out of bounds.");
            return;
        }

        int side = SideOf(zone);
        int lastHitTeam = TeamOfPlayer(lastHitByPlayer);

        if (side == lastHitTeam)
        {
            AwardPoint(OpponentTeam(lastHitTeam), "Ball landed on hitter's own side.");
            return;
        }

        if (side == lastBounceSide)
        {
            // Double bounce on the same side — the owning player faulted.
            AwardPoint(OpponentTeam(side), "Ball bounced twice on the same side.");
            return;
        }

        ClearPendingNetFault();
        lastBounceSide = side;
    }

    // ── Scoring ────────────────────────────────────────────────────────────────

    public void AwardPoint(int team, string reason)
    {
        ClearPendingNetFault();

        if (team == ServingTeam)
        {
            if (team == 0) playerAScore++;
            else           playerBScore++;
        }
        else
        {
            HandleServingTeamLostRally(team);
        }

        RefreshServeParticipants();

        Debug.Log($"[Rules] {reason} | Score: A={playerAScore} B={playerBScore} | ServingPlayer: {ServingPlayer} | ServingTeam: {ServingTeam}");
        UIManager.Instance?.UpdateScoreBoard(playerAScore, playerBScore, ServingPlayer);
        UIManager.Instance?.ShowAnnouncement(reason);

        // Broadcast score and announcement to clients in multiplayer.
        if (NetworkServer.active)
            NetworkGameManager.Instance?.BroadcastScore(
                playerAScore,
                playerBScore,
                ServingPlayer,
                ServingTeam,
                CurrentReceiverPlayer,
                ServerNumber,
                reason);

        GameStateManager.Instance.TransitionTo(GameState.ResultOfRound);

        if (IsGameOver())
            GameStateManager.Instance.TransitionTo(GameState.GameOver);
        else
            StartCoroutine(DelayedReset());
    }

    IEnumerator DelayedReset()
    {
        yield return new WaitForSeconds(ballResetDelay);
        ResetRally();
    }

    public void ResetRally()
    {
        LastHit        = LastHitState.None;
        BounceCount    = BallBounceCountState.Zero;
        lastBounceSide = -1;
        lastHitPlayerId = -1;
        lastHitTeamId = -1;
        ClearPendingNetFault();
        ResetBounceDeduplication();
        HasServeBeenHit = false;
        RefreshServeParticipants();
        PlaceBallAtServePosition();
        ResetPlayersToServeZones();
        GameStateManager.Instance.TransitionTo(GameState.PreServe);
    }

    public void ResetMatch()
    {
        StopAllCoroutines();

        playerAScore = 0;
        playerBScore = 0;
        LastHit = LastHitState.None;
        BounceCount = BallBounceCountState.Zero;
        lastBounceSide = -1;
        lastHitPlayerId = -1;
        lastHitTeamId = -1;
        ClearPendingNetFault();
        ResetBounceDeduplication();
        HasServeBeenHit = false;
        InitializeServeState();

        UIManager.Instance?.UpdateScoreBoard(playerAScore, playerBScore, ServingPlayer);

        if (NetworkServer.active)
            NetworkGameManager.Instance?.BroadcastScore(
                playerAScore,
                playerBScore,
                ServingPlayer,
                ServingTeam,
                CurrentReceiverPlayer,
                ServerNumber,
                string.Empty);

        PlaceBallAtServePosition();
        ResetPlayersToServeZones();
        GameStateManager.Instance?.TransitionTo(GameState.PreServe);
    }

    public void RegisterBall(BallController newBall, bool placeAtServePosition = true)
    {
        if (newBall == null) return;

        ball = newBall;
        if (placeAtServePosition)
        {
            InitializeServeState();
            UpdateScoreUi();
            PlaceBallAtServePosition();
            ResetPlayersToServeZones();
        }
    }

    public void ApplyRemoteScore(int scoreA, int scoreB, int serving)
    {
        ApplyRemoteScore(
            scoreA,
            scoreB,
            serving,
            TeamOfPlayer(serving),
            CurrentReceiverPlayer,
            ServerNumber);
    }

    public void ApplyRemoteScore(int scoreA, int scoreB, int serving, int servingTeam, int receiver, int serverNumber)
    {
        playerAScore = scoreA;
        playerBScore = scoreB;
        ServingPlayer = serving;
        ServingTeam = Mathf.Clamp(servingTeam, 0, 1);
        CurrentReceiverPlayer = receiver;
        ServerNumber = Mathf.Clamp(serverNumber, 1, 2);
        servingTeamSlot = PlayerIdentity.SlotOfPlayer(serving);
    }

    public void ApplyRemoteServeHit()
    {
        HasServeBeenHit = true;
    }

    public bool HasBallBouncedOnPlayerSideSinceOpponentHit(int playerIndex)
    {
        return lastBounceSide == TeamOfPlayer(playerIndex);
    }

    public Vector3 GetServeZonePositionForPlayer(int playerIndex)
    {
        float x = GetServeZoneXForPlayer(playerIndex);
        ZoneID zoneID = GetServeZoneIdForPlayer(playerIndex);
        if (TryFindServeZoneBounds(zoneID, out Bounds bounds))
            return new Vector3(bounds.center.x, playerServeY, bounds.center.z);

        float z = TeamOfPlayer(playerIndex) == 0 ? servePositionA.z : servePositionB.z;
        return new Vector3(x, playerServeY, z);
    }

    public Quaternion GetServeRotationForPlayer(int playerIndex)
    {
        return Quaternion.Euler(0f, TeamOfPlayer(playerIndex) == 0 ? 0f : 180f, 0f);
    }

    public Vector3 ClampPositionToServeZone(int playerIndex, Vector3 position)
    {
        if (!IsServeSetupActive) return position;
        if (!TryGetServeZoneBounds(playerIndex, out Bounds bounds)) return position;

        position.x = Mathf.Clamp(position.x, bounds.min.x + serveZoneClampMargin, bounds.max.x - serveZoneClampMargin);
        position.z = Mathf.Clamp(position.z, bounds.min.z + serveZoneClampMargin, bounds.max.z - serveZoneClampMargin);
        return position;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    void PlaceBallAtServePosition()
    {
        if (ball == null) return;

        Vector3 serveBase = ServingTeam == 0 ? servePositionA : servePositionB;
        Vector3 pos = new Vector3(GetServerServeX(), serveBase.y, serveBase.z);

        Debug.Log($"[PlaceBall] Placing at {pos} | server={NetworkServer.active} | ball={ball != null}");
        ball.ResetToPosition(pos);
    }

    float GetServerServeX()
    {
        if (ServingTeam == 0)
            return (playerAScore % 2 == 0) ? serveXOffset : -serveXOffset;

        return (playerBScore % 2 == 0) ? -serveXOffset : serveXOffset;
    }

    void ResetPlayersToServeZones()
    {
        if (NetworkServer.active)
        {
            NetworkLobbyManager.Instance?.ResetGamePlayersForServe();
            return;
        }

        foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.GetComponent<AIBot>() != null) continue;

            PlayerIdentity identity = EnsureIdentity(player.gameObject);
            if (!identity.IsAssigned)
                identity.Apply(0, 0, 0);

            int playerId = IsDoublesMode() ? identity.PlayerId : 0;
            player.ResetForServePosition(
                GetServeZonePositionForPlayer(playerId),
                GetServeRotationForPlayer(playerId),
                true);
        }

        bool assignedFirstBot = false;
        foreach (AIBot ai in FindObjectsByType<AIBot>(FindObjectsSortMode.None))
        {
            PlayerIdentity identity = EnsureIdentity(ai.gameObject);
            if (!identity.IsAssigned)
            {
                int fallbackId = assignedFirstBot ? 3 : 1;
                ai.ConfigureIdentity(fallbackId);
                identity = EnsureIdentity(ai.gameObject);
                assignedFirstBot = true;
            }

            int playerId = IsDoublesMode() ? identity.PlayerId : 1;
            ai.ResetForServePosition(
                GetServeZonePositionForPlayer(playerId),
                GetServeRotationForPlayer(playerId));
        }
    }

    bool TryGetServeZoneBounds(int playerIndex, out Bounds bounds)
    {
        ZoneID zoneID = GetServeZoneIdForPlayer(playerIndex);
        if (TryFindServeZoneBounds(zoneID, out bounds))
            return true;

        Vector3 center = GetServeZonePositionForPlayer(playerIndex);
        bounds = new Bounds(center, new Vector3(10f, 1f, 5f));
        return true;
    }

    bool TryFindServeZoneBounds(ZoneID zoneID, out Bounds bounds)
    {
        foreach (CourtZone zone in FindObjectsByType<CourtZone>(FindObjectsSortMode.None))
        {
            if (zone.zoneID != zoneID) continue;
            Collider col = zone.GetComponent<Collider>();
            if (col == null) break;
            bounds = col.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    ZoneID GetServeZoneIdForPlayer(int playerIndex)
    {
        float x = GetServeZoneXForPlayer(playerIndex);
        int team = TeamOfPlayer(playerIndex);
        if (team == 0)
            return x >= 0f ? ZoneID.Zone_A_Serve_Right : ZoneID.Zone_A_Serve_Left;

        return x >= 0f ? ZoneID.Zone_B_Serve_Left : ZoneID.Zone_B_Serve_Right;
    }

    float GetServeZoneXForPlayer(int playerIndex)
    {
        float serverX = GetServerServeX();
        int playerTeam = TeamOfPlayer(playerIndex);

        if (playerIndex == ServingPlayer)
            return serverX;

        if (playerTeam == ServingTeam)
            return -serverX;

        float receiverX = -serverX;
        return playerIndex == CurrentReceiverPlayer ? receiverX : -receiverX;
    }

    void OnGameStateChanged(GameState from, GameState to)
    {
        if (to == GameState.PreServe)
            HasServeBeenHit = false;
    }

    bool IsValidServeZone(ZoneID zone, int server)
    {
        // Diagonal serve rule: even score → serve from right, must land in opponent's right box.
        int servingTeam = TeamOfPlayer(server);
        if (servingTeam == 0)
        {
            ZoneID target = (playerAScore % 2 == 0) ? ZoneID.Zone_B_Right : ZoneID.Zone_B_Left;
            return zone == target;
        }
        else
        {
            ZoneID target = (playerBScore % 2 == 0) ? ZoneID.Zone_A_Right : ZoneID.Zone_A_Left;
            return zone == target;
        }
    }

    bool TryHandleKitchenVolleyFault(int playerIndex, Vector3 hitterPosition)
    {
        GameState state = GameStateManager.Instance.CurrentState;
        if (state == GameState.PreServe || state == GameState.ResultOfRound || state == GameState.GameOver)
            return false;

        if (!IsPlayerInKitchen(playerIndex, hitterPosition))
            return false;

        if (HasBallBouncedOnPlayerSideSinceOpponentHit(playerIndex))
            return false;

        AwardPoint(OpponentTeam(TeamOfPlayer(playerIndex)), "Kitchen volley fault.");
        return true;
    }

    bool TryHandleNetReplayFault(int playerIndex, int playerTeam)
    {
        if (pendingNetFaultTeam < 0)
            return false;

        if (playerTeam != pendingNetFaultTeam)
        {
            ClearPendingNetFault();
            return false;
        }

        string reason = playerIndex == pendingNetFaultPlayer
            ? "Player replayed their own net rebound."
            : "Team replayed its own net rebound.";
        AwardPoint(OpponentTeam(playerTeam), reason);
        return true;
    }

    bool TryHandleTwoBounceRuleFault(int playerIndex)
    {
        GameState state = GameStateManager.Instance.CurrentState;
        int playerTeam = TeamOfPlayer(playerIndex);
        int receivingTeam = OpponentTeam(ServingTeam);

        if (state == GameState.PreServe)
        {
            if (playerIndex == ServingPlayer) return false;
            if (!HasServeBeenHit) return false;

            bool servingTeamTouchedServe = playerTeam == ServingTeam;
            AwardPoint(
                servingTeamTouchedServe ? OpponentTeam(ServingTeam) : ServingTeam,
                servingTeamTouchedServe
                    ? "Serving team touched the serve before it bounced."
                    : "Receiver volleyed the serve before it bounced.");
            return true;
        }

        if (state == GameState.FirstBounce
            && IsDoublesMode()
            && playerTeam == receivingTeam
            && playerIndex != CurrentReceiverPlayer)
        {
            AwardPoint(ServingTeam, "Wrong receiver returned the serve.");
            return true;
        }

        if (state == GameState.FirstBounce
            && playerTeam == ServingTeam
            && lastHitTeamId == receivingTeam)
        {
            AwardPoint(OpponentTeam(ServingTeam), "Server volleyed the return before it bounced.");
            return true;
        }

        return false;
    }

    bool IsPlayerInKitchen(int playerIndex, Vector3 position)
    {
        return TeamOfPlayer(playerIndex) == 0
            ? position.z >= -7f && position.z <= 0f
            : position.z >= 0f && position.z <= 7f;
    }

    // Returns which player's side a zone belongs to: 0 = Player A, 1 = Player B
    int SideOf(ZoneID zone)
    {
        switch (zone)
        {
            case ZoneID.Zone_A_Kitchen:
            case ZoneID.Zone_A_Serve_Left:
            case ZoneID.Zone_A_Serve_Right:
            case ZoneID.Zone_A_Left:
            case ZoneID.Zone_A_Right:
                return 0;
            default:
                return 1;
        }
    }

    int TeamOfPlayer(int player)
    {
        return IsDoublesMode() ? PlayerIdentity.TeamOfPlayer(player) : Mathf.Clamp(player, 0, 1);
    }

    int OpponentTeam(int team) => team == 0 ? 1 : 0;

    bool IsDoublesMode() => ActiveMatchMode == MatchMode.Doubles;

    void InitializeServeState()
    {
        ClearPendingNetFault();
        ServingTeam = 0;
        servingTeamSlot = 0;
        firstServerExceptionActive = IsDoublesMode();
        ServerNumber = IsDoublesMode() ? 2 : 1;
        RefreshServeParticipants();
    }

    void RefreshServeParticipants()
    {
        if (!IsDoublesMode())
        {
            servingTeamSlot = 0;
            ServerNumber = 1;
            ServingPlayer = ServingTeam == 0 ? 0 : 1;
            CurrentReceiverPlayer = OpponentTeam(ServingTeam) == 0 ? 0 : 1;
            return;
        }

        servingTeamSlot = Mathf.Clamp(servingTeamSlot, 0, 1);
        ServingPlayer = PlayerIdentity.PlayerForTeamSlot(ServingTeam, servingTeamSlot);

        int receivingTeam = OpponentTeam(ServingTeam);
        int receiverSlot = GetTeamSlotForLaneX(receivingTeam, -GetServerServeX());
        CurrentReceiverPlayer = PlayerIdentity.PlayerForTeamSlot(receivingTeam, receiverSlot);
    }

    void HandleServingTeamLostRally(int winningTeam)
    {
        if (IsDoublesMode() && !firstServerExceptionActive && ServerNumber == 1)
        {
            ServerNumber = 2;
            servingTeamSlot = 1 - servingTeamSlot;
            return;
        }

        ServingTeam = winningTeam;
        firstServerExceptionActive = false;
        ServerNumber = IsDoublesMode() ? 1 : 1;
        servingTeamSlot = IsDoublesMode() ? GetTeamSlotForCurrentScore(ServingTeam) : 0;
    }

    int GetTeamSlotForCurrentScore(int team)
    {
        return GetTeamScore(team) % 2 == 0 ? 0 : 1;
    }

    int GetTeamSlotForLaneX(int team, float laneX)
    {
        return team == 0
            ? (laneX >= 0f ? 0 : 1)
            : (laneX < 0f ? 0 : 1);
    }

    int GetTeamScore(int team) => team == 0 ? playerAScore : playerBScore;

    void ClearPendingNetFault()
    {
        pendingNetFaultTeam = -1;
        pendingNetFaultPlayer = -1;
    }

    bool ShouldIgnoreDuplicateBounce(ZoneID zone, int lastHitByPlayer, GameState state)
    {
        bool sameFrame = Time.frameCount == lastAcceptedBounceFrame;
        bool tooSoon = Time.time - lastAcceptedBounceTime < duplicateBounceIgnoreSeconds;
        if (!sameFrame && !tooSoon)
        {
            lastAcceptedBounceFrame = Time.frameCount;
            lastAcceptedBounceTime = Time.time;
            return false;
        }

        Debug.Log($"[Rules] Ignored duplicate bounce | state={state} | zone={zone} | lastHit={lastHitByPlayer}");
        return true;
    }

    void ResetBounceDeduplication()
    {
        lastAcceptedBounceFrame = -1;
        lastAcceptedBounceTime = -999f;
    }

    void UpdateScoreUi()
    {
        UIManager.Instance?.UpdateScoreBoard(playerAScore, playerBScore, ServingPlayer);
    }

    PlayerIdentity EnsureIdentity(GameObject target)
    {
        if (!target.TryGetComponent(out PlayerIdentity identity))
            identity = target.AddComponent<PlayerIdentity>();
        return identity;
    }

    bool IsGameOver()
    {
        int max = Mathf.Max(playerAScore, playerBScore);
        int min = Mathf.Min(playerAScore, playerBScore);
        return max >= winningScore && (max - min) >= mustWinBy;
    }
}
