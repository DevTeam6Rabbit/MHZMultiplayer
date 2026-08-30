# MHZ Multiplayer PvP

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin that adds co-op multiplayer to **MH-Zombie**. Play together over Steam lobbies: remote helicopters with floating name tags, player-vs-player combat with an on-screen health bar and K/D scoreboard, an in-game chat, and a shared time-trial leaderboard.

![MHZ Multiplayer in action — three players in a lobby with a remote helicopter visible](screenshot.png)

## Features

- Peer-to-peer co-op over Steam lobbies — host with **F9**, join via lobby ID or Steam overlay invite
- Remote players rendered as ghost copies of your helicopter, each with a name tag
- Player-vs-player combat with a bottom-of-screen health bar (30mm PvP damage tuned for balance)
- Always-visible HUD, no menus to toggle:
  - **Multiplayer** panel (top-left) — host/join, lobby info, player list, leave
  - **Scoreboard** (right edge) — PvP K / D / K-D table and the time-trial leaderboard
  - **Chat** (bottom-left) — lobby chat
- Full dark theme that matches the game and stays readable in-game and in menus

## Requirements

- **MH-Zombie** (Steam), on Windows 10/11

That's it. You do **not** need to install BepInEx, .NET, an IDE, or anything else — the installer handles all of it for you.

## Installation

1. Download the ZIP (Code ▾ → Download ZIP) and extract it anywhere (your Downloads folder is fine).
2. Double-click **`install_mhz_multiplayer.bat`**.
3. The installer will:
   - Find your MH-Zombie install automatically (scans every drive / Steam library). If it can't, it asks you to paste the game folder path (Steam → right-click *MH-Zombie* → Manage → Browse local files).
   - Install BepInEx and a private .NET SDK into `%USERPROFILE%\.dotnet-mhz` (it never touches your system's .NET).
   - Build the mod and copy it into the game's `BepInEx\plugins` folder.
4. When it says **"Installation complete!"**, close the window and launch MH-Zombie from Steam.

**You're done.**

## Playing

Everything is already on screen — there's nothing to open or toggle.

- **F9** — host a lobby (or press **Host Lobby** in the Multiplayer panel)
- **F10** — leave the lobby
- To join a friend: paste their lobby ID into the box in the Multiplayer panel and press **Join**, or accept a Steam overlay invite
- Your PvP health bar sits at the bottom-center; the Scoreboard tracks K / D / K-D and time-trial finish times

## Building from source (developers only)

Requires a .NET SDK (the installer's private SDK works: `%USERPROFILE%\.dotnet-mhz\dotnet.exe`).

```sh
dotnet build --configuration Release
```

The compiled DLL is emitted to `bin\Release\net462\MHZombieMultiplayer.dll`. The project auto-detects the game folder via the `MHZ_GAME_DIR` environment variable (or the standard install path), so it builds standalone too.

## Disclaimer

This is an unofficial fan-made mod and is not affiliated with the developers of MH-Zombie. Use at your own risk.
