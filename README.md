# MHZ Multiplayer

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin that adds co-op multiplayer to **MH-Zombie**, including networked player sync, ghost helicopter rendering for remote players, and an in-game lobby UI.

## Features

- Peer-to-peer networked play with a host/join lobby (`LobbyUI.cs`)
- Position and state sync for remote players (`NetworkManager.cs`, `Packets.cs`, `RemotePlayer.cs`)
- Remote helicopters rendered as ghost copies of the local model (`GhostHeliFactory.cs`, `HeliLocator.cs`)
- Harmony patches to hook the base game without modifying it (`Patches.cs`)
- One-click installer script for Windows (`install_mhz_multiplayer.bat`)

## Requirements

- MH-Zombie (Steam)
- [BepInEx 5.x (x64)](https://github.com/BepInEx/BepInEx/releases)
- .NET Framework 4.6+ SDK to build
- Visual Studio 2022 or VS Code with the C# extension

## Installation & building

Full step-by-step instructions are in [`HOW_TO_INSTALL.txt`](HOW_TO_INSTALL.txt). Short version:

1. Extract BepInEx into your MH-Zombie install folder and run the game once.
2. Edit `<GameDir>` in `MHZombieMultiplayer.csproj` to point at your MH-Zombie folder.
3. Build the project — the compiled DLL goes in `BepInEx/plugins/`.
4. Launch the game; the multiplayer lobby is available from the main menu.

Alternatively, run `install_mhz_multiplayer.bat` to automate setup.

## Disclaimer

This is an unofficial fan-made mod and is not affiliated with the developers of MH-Zombie. Use at your own risk.
