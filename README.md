# Game Shelf

A small Windows 10/11 app that replaces the game shortcuts on your desktop. It shows the games you have
installed as a clean cover grid, starts them with one click, gets completely out of the way while you play, and
comes back when the game closes.

> **Vibe-coded, use at your own risk.** See the [disclaimer](#disclaimer) below.

## What it does

- **Finds your games:** Steam, Epic Games, GOG, EA app, Ubisoft Connect, the Xbox app (Game Pass) and the
  Rockstar Games Launcher, plus desktop shortcuts you import and games you add yourself. Covers come from the
  store, or are generated from the game's icon.
- **Opens like part of Windows:** a flyout from the notification-area icon or **Win+Shift+G**. It can also sit
  docked on the desktop, behind your windows.
- **Costs nothing while you play:** a few seconds after a game starts, Game Shelf exits completely. Only a tiny
  watcher (about 3 MB of memory, no CPU) waits for the game to close, records your playtime and brings the shelf
  back.
- **Tidies your desktop:** moves the game shortcuts you pick into its own folder. They're never deleted, and
  can be put back anytime.
- **Shelf:** search, filters per launcher, favorites, "Continue playing" or "This week", sorting, four cover
  sizes.
- **Game details:** playtime with weekly and monthly charts, last played, size on disk, recent sessions, launch
  options (with suggestions for the game's engine), an anti-cheat check, per-game hide delay, process to watch
  and custom cover.
- **Crash detection:** when a game crashes, Game Shelf says so when it comes back, with the likely cause and
  what to try, read from Windows' own event log.
- **Free up space:** type the size of a game you want to install, and it suggests which games to remove on
  which drive (the ones you play least first), shown as a map of what takes up the space. Each game is removed
  through its own launcher, which asks first. Game Shelf never deletes game files itself.
- **Light on your PC:** no background timers or polling. While it waits in the tray, it uses about 12 MB of
  memory and no CPU.

Dark and light mode, your Windows accent color, keyboard navigation, screen-reader names, per-monitor DPI and
"Animation effects off" are all respected.

## Install

You need Windows 10 or 11 (x64). Nothing else: the app carries its own runtimes.

1. Build `GameShelfSetup.exe` (see below), or use a copy someone built for you.
2. Run it and click **Install**. It installs for your account only and never asks for administrator rights.
3. Game Shelf opens and walks you through a short first-time setup.

To **update**, run a newer `GameShelfSetup.exe`. It works even while Game Shelf or a game is running. To
**uninstall**, use Settings → Apps → Game Shelf. Your imported shortcuts go back on the desktop.

All the details, including silent install options and how the installer works, are in
[docs/installer.md](docs/installer.md).

Because the setup isn't code-signed, Windows SmartScreen may warn about a downloaded copy ("Windows protected
your PC" → More info → Run anyway).

## Build it yourself

Requirements: the .NET 10 SDK, and the "Desktop development with C++" workload from Visual Studio or the Build
Tools (for Native AOT). Then, in PowerShell:

```powershell
./publish.ps1          # publish\GameShelfSetup.exe, plus the portable GameShelf.exe and GameShelf.Watcher.exe
dotnet build           # a normal development build into bin\
dotnet run --project tests/GameShelf.Tests -c Release   # unit tests
```

CI (`.github/workflows/ci.yml`) builds and tests every push on a real Windows machine. That includes a UI smoke
test and an installer test.

Don't run `tests/smoke.ps1` or `tests/setup-test.ps1` on your own PC. They replace your Game Shelf data and
Steam settings with test data, and are meant for CI only.

## Privacy

- No account, no telemetry, no analytics.
- The only network traffic is downloading cover art over HTTPS from the store CDNs (Steam). The app never
  checks for updates or contacts anything else.
- Everything stays on your PC:
  - Settings, library, play history, crash notes and imported shortcuts: `%APPDATA%\GameShelf\`
  - Cover cache and error log: `%LOCALAPPDATA%\GameShelf\`
  - The app itself: `%LOCALAPPDATA%\Programs\GameShelf\`

## Project layout

| Path | What's there |
|---|---|
| `src/GameShelf/` | The WinUI 3 app: shelf, details, settings, library sources, launcher. |
| `src/GameShelf.Watcher/` | The tiny game watcher (Native AOT). |
| `src/GameShelf.Setup/` | The installer, updater and uninstaller (Native AOT). |
| `tests/` | Unit tests, the stand-in game and the CI UI tests. |
| `docs/proposals.md` | Planned features. |
| `docs/installer.md` | Building, installing, updating and how the installer works. |
| `docs/manual-testing.md` | The checklist for testing a build on a real PC. |
| `CLAUDE.md` | The project brief and rules the code is written against. |

## Status

Working:
- Steam, Epic, GOG, EA app, Ubisoft Connect, Xbox app and Rockstar Games Launcher; shortcut import and manual
  add.
- Launch → hide → return, with playtime tracking.
- Shelf, details (playtime charts, launch options, anti-cheat check) and settings.
- Crash detection and Free up space.
- Multi-monitor, the flyout with tray icon and shortcut, first-run setup, and the installer.
- Game mode, the part without admin rights (off by default): Best performance power mode, the game at Above
  normal priority, Game Bar background recording paused and chosen apps closed, all put back when the game
  closes.

Planned (see `docs/proposals.md`):
- More launchers (Battle.net, Riot, Amazon, itch.io, Humble, Meta).
- Controller support.
- The rest of Game mode: pausing background services and the network options (needs admin rights once).

## Disclaimer

**Game Shelf is vibe-coded.** Its code was written by an AI (Anthropic's Claude), directed and tested by
Jannis Jost, but not written or reviewed line by line by a human developer.

The software is provided **"as is", without warranty of any kind**, express or implied. That includes, but isn't
limited to, the warranties of merchantability, fitness for a particular purpose and non-infringement. In no
event shall the author or anyone involved be liable for any claim, damages or other liability arising from, out
of, or in connection with the software or its use. You use it at your own risk.

Game Shelf isn't affiliated with or endorsed by Valve, Epic Games, CD PROJEKT (GOG), Electronic Arts, Ubisoft,
Rockstar Games, Microsoft or any other
company whose launchers it works with. Their names and icons belong to their owners.
