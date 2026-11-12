# Binder

A small Windows 10/11 app that replaces the game shortcuts on your desktop. It shows the games you have
installed as a clean cover grid, starts them with one click, gets completely out of the way while you play, and
comes back when the game closes.

> **Vibe-coded, use at your own risk.** See the [disclaimer](#disclaimer) below.

## What it does

- **Finds your games:** Steam, Epic Games, GOG, EA app, Ubisoft Connect, the Xbox app (Game Pass), the
  Rockstar Games Launcher and the Hytale Launcher, plus desktop shortcuts you import and games you add yourself. Covers come from the
  store, or are generated from the game's icon.
- **Opens like part of Windows:** a flyout from the notification-area icon or **Win+Shift+G**. It can also sit
  docked on the desktop, behind your windows.
- **Costs nothing while you play:** a few seconds after a game starts, Binder exits completely. Only a tiny
  watcher (about 3 MB of memory, no CPU) waits for the game to close, records your playtime and brings the shelf
  back.
- **Tidies your desktop:** moves the game shortcuts you pick into its own folder. They're never deleted, and
  can be put back anytime.
- **Shelf:** search, filters per launcher, favorites, "Continue playing" or "This week", sorting, four cover
  sizes.
- **Game details:** playtime with weekly and monthly charts, last played, size on disk, recent sessions, launch
  options (with suggestions for the game's engine), an anti-cheat check, per-game hide delay, process to watch
  and custom cover.
- **Crash detection:** when a game crashes, Binder says so when it comes back, with the likely cause and
  what to try, read from Windows' own event log.
- **Free up space:** type the size of a game you want to install, and it suggests which games to remove on
  which drive (the ones you play least first), shown as a map of what takes up the space. Each game is removed
  through its own launcher, which asks first. Binder never deletes game files itself.
- **Light on your PC:** no background timers or polling. While it waits in the tray, it uses about 12 MB of
  memory and no CPU.

Dark and light mode, your Windows accent color, keyboard navigation, screen-reader names, per-monitor DPI and
"Animation effects off" are all respected.

## Install

You need Windows 10 or 11 (x64). Nothing else: the app carries its own runtimes.

1. Build `GameShelfSetup.exe` (see below), or use a copy someone built for you.
2. Run it and click **Install**. It installs for your account only and never asks for administrator rights.
   (Only Game mode's service and network options do, once, if you choose to allow them in its settings.)
3. Binder opens and walks you through a short first-time setup.

To **update**, run a newer `GameShelfSetup.exe`. It works even while Binder or a game is running. To
**uninstall**, use Settings → Apps → Binder. It removes everything Binder put on your PC; it asks whether to
put your imported shortcuts back on the desktop (the default) and whether to delete your game list too.

All the details, including silent install options and how the installer works, are in
[docs/installer.md](docs/installer.md).

Because the setup isn't code-signed, Windows SmartScreen may warn about a downloaded copy ("Windows protected
your PC" → More info → Run anyway).

## Build it yourself

Requirements: the .NET 10 SDK, and the "Desktop development with C++" workload from Visual Studio or the Build
Tools (for Native AOT). Then, in PowerShell:

```powershell
./scripts/publish.ps1  # publish\GameShelfSetup.exe, plus the portable GameShelf.exe and GameShelf.Watcher.exe
dotnet build           # a normal development build into bin\
dotnet run --project tests/GameShelf.Tests -c Release   # unit tests
```

CI (`.github/workflows/ci.yml`) builds and tests every push on a real Windows machine. That includes a UI smoke
test and an installer test.

Don't run `tests/smoke.ps1` or `tests/setup-test.ps1` on your own PC. They replace your Binder data and
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
| `src/GameShelf.Elevated/` | Game mode's optional admin helper (Native AOT). |
| `scripts/` | Publish, the pre-commit check and the icon generators (PowerShell). |
| `tests/` | Unit tests, the stand-in game and the CI UI tests. |
| `docs/proposals.md` | Planned features. |
| `docs/installer.md` | Building, installing, updating and how the installer works. |
| `docs/manual-testing.md` | The checklist for testing a build on a real PC. |
| `CLAUDE.md` | The project brief and rules the code is written against. |

## Status

Working:
- Steam, Epic, GOG, EA app, Ubisoft Connect, Xbox app, Rockstar Games Launcher and Hytale Launcher; shortcut import and manual
  add.
- Launch → hide → return, with playtime tracking.
- Shelf, details (playtime charts, launch options, anti-cheat check) and settings.
- Crash detection and Free up space.
- Multi-monitor, the flyout with tray icon and shortcut, first-run setup, and the installer.
- Game mode (off by default): Best performance power mode, the game at Above normal priority, Game Bar
  background recording paused and chosen apps closed, all put back when the game closes. With admin rights
  allowed once: Windows Search, SysMain, telemetry and (optionally) Print Spooler paused, the game and voice
  chat marked as urgent on the network, cloud uploads slowed down and Windows Update downloads limited. Not
  yet tested with admin rights on a real PC.

Planned (see `docs/proposals.md`):
- More launchers (Battle.net, Riot, Amazon, itch.io, Humble, Meta).
- Controller support.

## Contributing

Code contributions aren't accepted. Translation fixes, security fixes, bug reports and feature requests that fit
the launcher are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Binder is free software under the GNU General Public License, version 3, with an additional permission for
the libraries it bundles ([LICENSE](LICENSE), [LICENSE-EXCEPTION.txt](LICENSE-EXCEPTION.txt)). It includes parts of
Microsoft's .NET and Windows App SDK, which stay under their own licenses
([THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)); by installing Binder you also agree to the Windows App
SDK's license terms. All the licenses are in the `licenses` folder of the install folder.

## Disclaimer

**Binder is vibe-coded.** Its code was written by an AI (Anthropic's Claude), directed and tested by
Jannis Jost, but not written or reviewed line by line by a human developer.

The software is provided **"as is", without warranty of any kind**, express or implied. That includes, but isn't
limited to, the warranties of merchantability, fitness for a particular purpose and non-infringement. In no
event shall the author or anyone involved be liable for any claim, damages or other liability arising from, out
of, or in connection with the software or its use. You use it at your own risk.

Binder isn't affiliated with or endorsed by Valve, Epic Games, CD PROJEKT (GOG), Electronic Arts, Ubisoft,
Rockstar Games, Hypixel Studios, Microsoft or any other
company whose launchers it works with. Their names and icons belong to their owners.

Binder is licensed under the GNU GPL, version 3, with an additional permission for the libraries it bundles. It
includes parts of Microsoft's .NET and Windows App SDK, which stay under their own licenses; by installing Game
Shelf you also agree to the Windows App SDK's license terms. The licenses are in the "licenses" folder of the
install folder.
