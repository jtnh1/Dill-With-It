# Dill With It

A pickleball game built in Unity 6 with single-player vs AI and online multiplayer (P2P) over Steam.

## Gameplay

Step onto the court and play out points using a full pickleball swing kit:

- **Forehand / Backhand** — your bread-and-butter groundstrokes
- **Dink** — soft drop shot near the kitchen
- **Lob** — high arcing shot to push opponents off the net
- **Smash** — overhead putaway
- **Block** — defensive volley

Sprint to chase down balls, position with the aim indicator, and play by official pickleball rules (kitchen rules, two-bounce rule, side-out scoring) handled by the rules engine.

## Modes

- **Single-player** — practice/play against an AI bot
- **Multiplayer** — host or join a Steam lobby with friends (1v1 for now)

## Controls

| Action | Keyboard / Mouse | Gamepad |
|---|---|---|
| Move | WASD | Left stick |
| Sprint | Left Shift | LB |
| Forehand | Left Mouse | A (south) |
| Backhand | Q | B (east) |
| Dink | E | X (west) |
| Lob | F | Y (north) |
| Smash | R | RB |
| Block | Right Mouse | Left Trigger |

## Tech Stack

- **Engine** — Unity `6000.4.2f1` with URP
- **Networking** — [Mirror](https://mirror-networking.com/) with FizzySteamworks transport
- **Steam integration** — [Steamworks.NET](https://steamworks.github.io/) for matchmaking, lobbies, and friend invites
- **Input** — Unity Input System (keyboard, mouse, gamepad)
- **AI navigation** — Unity NavMesh

## Building

Currently shipping as a **Windows standalone** build. Multiplayer requires Steam to be running and signed in.

1. Open the project in Unity `6000.4.2f1`.
2. Open `Assets/Scenes/MainMenu.unity`.
3. **File → Build Profiles → Windows → Build**.
4. Make sure `steam_appid.txt` sits next to the executable in your build folder.

## Project Layout

```
Assets/
├── Scripts/         Game code
│   ├── AI/          AIBot
│   ├── Ball/        BallController, spawn manager
│   ├── Camera/      CameraController
│   ├── Court/       Court geometry and zones
│   ├── GameState/   PickleballRulesEngine, GameStateManager
│   ├── Network/     Mirror lobby + networked controllers
│   ├── Player/      PlayerController, animation, paddle, stamina
│   ├── Swing/       SwingType / SwingResolver / SwingExecutor
│   └── UI/          Menu and lobby UI
├── Scenes/          MainMenu, GameScene
├── Animations/      Character animation clips (Ty)
├── Mirror/          Mirror networking package (Components, Core, Transports)
└── com.rlabrecque.steamworks.net/  Steamworks.NET wrapper
```

## Status
In development
 - More characters
 - More animation types
 - Serve system
 - Power scaling based on button holding
 - Doubles (2v2)