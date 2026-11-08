# Game Shelf — project brief

A lightweight Windows desktop widget that replaces game shortcuts on the desktop. It shows the user's installed games as a clean cover grid docked on the desktop, launches them, gets completely out of the way while a game runs, and comes back when the game closes.

**Top priority: zero performance cost while gaming.** When a game is running, the widget process must not exist. Only a tiny watcher process may remain, blocked/sleeping.

**Scope: a launcher, not a game toolbox.** A feature earns its place only if it helps the user start, find or manage their games, and it must feel like part of Windows. Think each one through and refine it before building; when an idea doesn't clearly pass, leave it out (or note it under "Not decided" in `docs/proposals.md`) instead of adding it.

## Guiding principle: minimal resource use everywhere

This app must perform very well and consume as little CPU, RAM, disk I/O and GPU as possible for what it does, at all times, not just during gaming. Treat efficiency as a feature on equal footing with correctness:

- Do work only in response to a user action or a launch event, never "just in case". No background timers, polling, file watchers or periodic refreshes while idle.
- Prefer lazy loading: read only what's needed to show the first screen, defer everything else, and release resources (images, views, parsed data) that are no longer visible.
- Cache expensive results (covers, parsed manifests) on disk, and never re-download or re-parse unnecessarily.
- Keep the UI cheap to render: hardware rendering, no blur or transparency effects of our own, no continuous or decorative animations, and a virtualized grid. The one approved exception is the panel's Acrylic backdrop (WinUI's `DesktopAcrylicController`, kept active while unfocused), which falls back to solid when Transparency effects are off, in high contrast and where it isn't supported. Short hover/transition effects only if they cost nothing when idle.
- Avoid heavy dependencies and reflection-heavy libraries. Use trimming/ReadyToRun where it helps startup and size.
- When choosing between a simpler-but-heavier and a slightly-more-code-but-lighter approach, pick the lighter one.
- Measure, don't assume: check CPU, memory and startup time after each milestone, and report the numbers. If a change regresses them, fix it before moving on.

Design reference (owner's private canvas, open it in a browser): https://claude.ai/artifact/9tZEP6HSrETxwGwpSbMg1Q
Pages: "Shelf", "Game detail", "Window, setup & settings" (built designs) and "Still to build" (open milestones, in build order). Board titles carry the board number used in `docs/proposals.md` and "built" or the milestone.

---

## Tech stack

- C# / WinUI 3 (Windows App SDK, unpackaged, self-contained, Native AOT) on the current LTS .NET (check `dotnet --list-sdks`; .NET 10 at time of writing). Windows 10/11 x64. The owner moved the widget from WPF to WinUI 3 so it feels native; desktop memory matters less than before, since the widget is gone while a game runs.
  - AOT rules: no `[ComImport]` (use `[GeneratedComInterface]`), `LibraryImport` for P/Invoke, `x:Bind` or code-behind instead of `{Binding}`, `[UnmanagedCallersOnly]` for native callbacks.
- Two projects in one solution:
  - `GameShelf` — the WinUI widget (UI, library, settings).
  - `GameShelf.Watcher` — a tiny windowless app (OutputType `WinExe`, no UI). Try Native AOT publish to keep it ~1–3 MB RAM; fall back to a trimmed self-contained build if AOT causes trouble.
- No third-party UI frameworks. `System.Text.Json` for storage.

## Dependencies policy

- Default to the .NET base library and Windows APIs (P/Invoke). Writing a small amount of code yourself (e.g. a VDF parser) is preferred over adding a package.
- **Ask the owner before adding any third-party package** (NuGet or otherwise). Explain what it's for, why the built-in options aren't enough, and its size/impact. Don't add it until they approve.
- Only propose well-known, actively maintained packages: widely used, with recent releases, an active repository and a permissive license. No abandoned, obscure or single-maintainer hobby packages.
- Pin exact versions, and keep the dependency list in this file up to date.

Approved third-party dependencies:
- `Microsoft.WindowsAppSDK.WinUI` 2.6.4 (the WinUI component of Microsoft's Windows App SDK 2.6.4, MIT). Only this component, not the `Microsoft.WindowsAppSDK` umbrella package, which bundles AI runtimes (ONNX, DirectML) the app doesn't use.

- Publish the widget as a self-contained Native AOT folder and the watcher as a Native AOT exe, into the same folder (`publish\app`).

## Architecture

```
GameShelf.sln
  src/GameShelf/            WinUI 3 app
    Library/                SteamSource, EpicSource, GogSource, EaSource, UbisoftSource, XboxSource, RockstarSource, HytaleSource, InstallFolder (folder safety),
                            ShortcutFile (.lnk/.url reading), ShortcutImporter (import, restore, manual add),
                            LaunchOptions (arguments, Steam run links, "Start with"), GameScan (engine and anti-cheat
                            from file names, bounded, cached per game), AntiCheat (requirements and conflict tables),
                            PlaytimeStats (this week, weekly and monthly playtime from our sessions),
                            CrashAnalysis (milestone 11: exit code + Windows events -> ending, culprit, advice, copied texts),
                            SpacePlanner (milestone 15: which games to remove on which drive, squarified treemap)
    Models/                 Game, Session, Settings, ImportedShortcut, CrashRecord
    Services/               Launcher, CoverCache, Storage, DesktopLayer, Autostart, GameActions,
                            Placement (pure pixel math), Monitors (displays, auto-hide taskbars), ShellNotify, ErrorLog,
                            PcChecks (Secure Boot, TPM, test signing, drivers, conflicting programs; read-only, no admin),
                            CrashLog (event log 1000/1002/4101 for one session), CrashStore (crashes.json), PcInfo
                            (Windows, graphics, memory; read only when copying), GameLogs (Unity/Unreal log files),
                            RunningGames (games running right now, read once when Free up space opens),
                            Gamepad (milestone 13: XInput, read only while the panel is active and a pad is connected),
                            GameModeService (milestone 14: the watcher's Game mode arguments, recovery, page state),
                            Chime (the calm "back to normal" sound, made in memory; board 23), Sound (plays it)
    Views/                  ShelfView, DetailView, SettingsView, ImportView, SetupView (the launch toast lives in MainWindow),
                            Disclosure (a settings card that opens, boards 19 and 21), BarChart (the playtime charts), ModePreview (display mode pictures), CrashCard (shelf and detail),
                            FreeSpaceView (Free up space, milestone 15), GameModeView (Settings › Game mode),
                            GameModeToast (Game mode's "Game mode is on" / "Windows is back to normal" toast, its own small window)
  src/GameShelf.Watcher/    Program.cs, GameMode.cs (applies and restores Game mode, with its journal),
                            ElevatedClient.cs (runs the helper's tasks and waits on its event),
                            GameModeShared.cs (ids, files, restore result, power mode, the helper's request and paths;
                            also compiled into the widget, the helper and the setup)
  src/GameShelf.Elevated/   GameShelf.Elevated.exe: Game mode's admin part (Native AOT, windowless). Installed to
                            %ProgramFiles%\GameShelf by "Allow…", run by two scheduled tasks: Helper (apply, restore,
                            journal), Services (allowlisted pause/restart), Qos (WMI over COM), Install (tasks, ACLs)
  src/GameShelf.Setup/      GameShelfSetup.exe: per-user installer, updater and uninstaller (Native AOT, task dialogs)
  tests/GameShelf.Tests/    plain console unit tests for the non-UI code (no test framework packages)
  tests/smoke.ps1           Windows UI smoke test run by CI (see .github/workflows/ci.yml)
  tests/setup-test.ps1      CI test of the installer: install, update while running, uninstall
  tools/make-tray-icons.ps1  draws the tray icon frames (run after changing the logo)
  tools/make-watcher-icon.ps1  draws the watcher's icon: the logo with an eye instead of the play mark
  publish.ps1               builds what ships into publish/: app\ (the WinUI widget folder, the Native AOT
                            watcher and helper) and GameShelfSetup.exe with app\ inside (optional -CertificateThumbprint signing)
  check.ps1                 the hard rules on a diff (packages, AOT, and with -Task the agent rules), then build + tests
  AGENTS.md                 short rules for other coding agents doing task cards
  docs/tasks/               small, exactly specified task cards for other agents (README has the workflow and template)
  site/                     the one-page website (plain HTML + CSS, no scripts, nothing loaded from other servers;
                            published from this repo once it is public). Design: https://claude.ai/artifact/BzoYw1zPYHLeafpbdfZdQY
  README.md                 overview, install, build, privacy, disclaimer
  docs/installer.md         how to build, install, update and uninstall, and how the installer works
```

Data lives in `%APPDATA%\GameShelf\` (`settings.json`, `library.json`, `sessions.json`, `imported-shortcuts.json`, `crashes.json`, and the `Shortcuts\` folder imported shortcuts are moved into). Covers are cached in `%LOCALAPPDATA%\GameShelf\covers\`. Game mode keeps `gamemode-journal.json` (only while changes are applied), `gamemode.log`, `gamemode-last.txt` and `gamemode-request.txt` (the watcher's request to the helper) in `%LOCALAPPDATA%\GameShelf\`. Its admin part keeps `journal.json`, `elevated.log`, `allowed-<SID>.txt` and `result-<SID>.txt` in `%ProgramData%\GameShelf\` (admins and SYSTEM write, users read).

## Core flow: launch → hide → return

1. The user clicks Play. `Launcher` starts the game: via the launcher's URI or command (Steam, Epic, EA, Ubisoft, Xbox, Rockstar, Hytale) or by running the exe directly (GOG, manual, shortcut).
2. The widget shows the in-panel **launch toast**: "Starting [Game]", a countdown (default 10 s, per-game override), and a **Keep open** button.
3. The widget starts `GameShelf.Watcher.exe` with arguments: game id, install folder, optional explicit process name, and the timeout.
4. When the countdown ends, the widget **exits completely** (process ends, not just hidden). If the user pressed Keep open, it stays and nothing else changes.
5. The watcher:
   - Looks for the game process. Store launchers start the store app first, so match any running process whose executable path is inside the game's install folder, or the explicit process name if set. During this detection phase only, polling every ~2 s is fine, for up to ~3 minutes.
   - If it never finds the game, it relaunches the widget showing "Couldn't detect the game" and exits.
   - Once found, it **blocks** on the process handle (`Process.WaitForExitAsync` / `WaitForSingleObject`). No polling while the game runs.
   - Some games restart themselves (launcher → game exe). After an exit, re-check the install folder for ~5 s before deciding the game is really closed.
   - On exit it appends a session (start, end, game id) to `sessions.json`, relaunches `GameShelf.exe` if "Come back when the game closes" is on, and exits.
   - Game mode (milestone 14, when on for the game): the watcher applies it right after it starts (`--mode`, `--close`, `--toast`) and tells a widget that is still up what it changed (`--applied 3/3`; the widget never starts just for that), raises the game to Above normal once found, and puts everything back before relaunching the widget, which shows the result (`--restored 4/5|Name`; with "Come back" off only the toast shows, then it exits). If the game is never detected, it restores at once. If "Allow…" was done, the admin items go through the helper: the watcher writes the request, runs the `GameShelf Apply <SID>` task with `schtasks /run` and waits on the event `Local\GameShelf.Elevated.<id>`; once the game is found it runs Apply again to add the game's exe to "Game and voice chat first"; at the end it runs `GameShelf Restore <SID>` and adds the helper's result to its own.
6. Single instance: a named mutex in the widget. A second launch hands its arguments to the running one (see Display modes).

## Library sources

- **Steam**
  - Find the Steam path in `HKCU\Software\Valve\Steam\SteamPath`.
  - Parse `steamapps\libraryfolders.vdf` for all library folders, then each `appmanifest_*.acf` (appid, name, installdir, SizeOnDisk). You'll need a small VDF parser.
  - Skip non-games: redistributables (e.g. appid 228980), Proton, SDKs, dedicated servers.
  - `LastPlayed` (Unix seconds, 0 = never) is kept as `Game.StoreLastPlayed`, so Free up space counts games played through Steam directly.
  - Launch with `steam://rungameid/{appid}`. When Steam isn't running (`HKCU\Software\Valve\Steam\ActiveProcess\pid`, checked against a live `steam` process), start `{SteamPath}\steam.exe -silent <link>` instead, so Steam starts in the notification area without opening its window.
  - Covers: the local Steam library cache first, else download `library_600x900` from the Steam CDN once and cache it.
  - Icon (`SteamSource.IconFor`, for generated covers and the Free up space map): the `.ico` named by the game's `Steam App {appid}` uninstall entry (`DisplayIcon`, only inside `{SteamPath}\steam\games`, up to 256 px), else the small `.jpg` in `appcache\librarycache\{appid}\` (or the older `{appid}_icon.jpg`).
- **Epic**
  - Read `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: DisplayName, InstallLocation, LaunchExecutable, AppName, CatalogNamespace, CatalogItemId).
  - Launch with `com.epicgames.launcher://apps/{Namespace}%3A{CatalogItemId}%3A{AppName}?action=launch&silent=true`.
  - Covers: use a placeholder unless the user sets one.
- **GOG**: Read `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\*` (gameName, path, exe) and launch the exe directly.
- **EA app**: Settings → Apps entries (`HKLM\...\Uninstall\*`) whose uninstaller is EA's `EAInstaller\…\Cleanup.exe uninstall_game` (DisplayName, InstallLocation, DisplayIcon = the exe, EstimatedSize). The content id is the first `<contentID>` in `{InstallLocation}\__Installer\installerdata.xml`. Launch with `origin2://game/launch?offerIds={contentId}&autoDownload=1`, or the exe if there's no content id.
- **Ubisoft Connect**: `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}` (InstallDir), named by the `Uplay Install {id}` uninstall entry (DisplayName, EstimatedSize, DisplayIcon .ico). Launch with `uplay://launch/{id}/0`. Entries whose folder no longer exists are skipped.
- **Xbox app (Game Pass, Microsoft Store PC games)**: each fixed drive's `.GamingRoot` file ("RGBX", a count, UTF-16 folder names) names its `XboxGames` folder; each game folder has `Content\appxmanifest.xml` (Identity Name, Application Id) and `Content\MicrosoftGame.config` (ShellVisuals DefaultDisplayName and Square150x150Logo, Executable Name). Only packages registered for this user count (key names under `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages` give the publisher id). Launch with `shell:AppsFolder\{Name_PublisherId}!{AppId}`. The generated cover uses the tile logo PNG (Xbox game exes often have no icon); the badge is the Xbox app's own icon.
- **Rockstar Games Launcher**: uninstall entries whose UninstallString runs `Rockstar Games\Launcher\uninstall.exe -uninstall=<title>` (not `launcher`); launch with `{InstallFolder}\Launcher.exe -launchTitle=<title>` (`HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Launcher`). Copies bought on Epic or Steam come from those sources.
- **Hytale Launcher**: `HKLM\SOFTWARE\Hypixel Studios\Hytale Launcher` (InstallDir with `hytale-launcher.exe`) and `HKLM\SOFTWARE\Hypixel Studios\Hytale` (GameInstallPath, usually `%APPDATA%\Hytale`). The game counts as installed when `{GameInstallPath}\install\{patchline}\package\game\latest\Client\HytaleClient.exe` exists; the install folder is `{GameInstallPath}\install` (client, Java runtime, local server), its size counted from the files (bounded). The client needs the launcher's sign-in, so Play opens the launcher and the watcher follows what runs from the install folder.
- **Desktop shortcuts (import)**
  - Scan the user's Desktop and the Public Desktop for `.lnk` and `.url` files. Show a picker so the user ticks which ones are games.
  - Move the chosen shortcuts into `%APPDATA%\GameShelf\Shortcuts\`. Never delete them, so the import is reversible, and offer "Restore to desktop".
  - Shortcuts that point at Steam/Epic URIs should merge with the detected entry instead of duplicating it.
- **Manual add**: let the user pick an .exe or a shortcut.

Rescan the library on startup and with a manual refresh button. No background file watchers.

A game the watcher can't follow (no install folder and no process name, e.g. a web-link shortcut) starts without hiding the widget, with a toast pointing to the game's "Process to watch" setting.

## UI spec (match the design canvas)

Dark, Windows 11 style. Font: Segoe UI Variable (fallback Segoe UI).

### It must feel native and integrated into Windows

The widget should feel like a part of Windows 11, not a foreign app sitting on top of it. Follow Microsoft's Fluent Design / Windows 11 app guidelines in look and behavior:

- **Look:** Use Fluent-style controls (buttons, toggles, segmented controls, text box with an accent underline on focus), Windows 11 corner radii, spacing and type ramp, and standard Fluent icons (Segoe Fluent Icons font, which ships with Windows 11; fall back to Segoe MDL2 Assets on Windows 10).
- **Theme:** Use the user's Windows accent color by default (fall back to `#4CC2FF`), and follow the system light/dark mode. Dark is the design reference; derive a matching light palette. Update live when the user changes either in Windows settings.
- **System settings respected:** per-monitor DPI scaling, the Windows text size setting, "Animation effects" off (disable transitions), high contrast themes, and multiple monitors.
- **Native behavior:**
  - Native right-click context menus on covers (Play, Open folder, Favorite, Hide).
  - Full keyboard navigation (arrow keys in the grid, Enter to play, Space for details, Esc to go back, type-to-search).
  - Proper tooltips, and screen-reader names (UI Automation) on all controls.
- **Windows integration:**
  - Start with Windows via the standard Run key.
  - Optional notification-area (tray) icon to show/hide the widget; it must cost nothing when idle.
  - Use the standard Windows file picker for "Add a game".
  - Store data in the standard AppData locations.
  - The widget appears in Settings → Apps with a proper name, icon and version.
- **No foreign look:** no custom window chrome that fights Windows, no web-style UI, no non-system fonts, no off-brand colors or emoji icons.

| Token | Value |
|---|---|
| Panel background | `#202024` |
| Panel border | `#34343A` |
| Surface (cards, inputs) | `#2A2A2F` |
| Primary text | `#F3F3F5` |
| Secondary text | `#A9A9B2` |
| Accent | `#4CC2FF` (text on accent: `#0A1A24`) |
| Radii | 8 panel (system-drawn window corners via `DWMWCP_ROUND`, scaled with DPI like Start and the other shell flyouts; never clip the window to a custom radius), 8 cards and covers (`OverlayCornerRadius`), 4 buttons and other controls (`ControlCornerRadius`) |

**Display modes (milestone 9)**
- **Flyout** (default): a notification-area icon and a global shortcut (Win+Shift+G; Win+Shift+J, Ctrl+Alt+G or Off in Settings) open the panel next to the screen edge with a short slide-in (skipped when animations are off). It closes on Esc, click-away (unless pinned or a dialog is open), the icon or the shortcut again.
- **On the desktop**: the docked, always-visible panel on the desktop layer described below. The shortcut brings it in front for a moment.
- `Program.Main` holds the single-instance mutex before WinUI starts; a second start forwards its arguments to the running instance's hidden `MessageWindow` (WM_COPYDATA) and exits. With no arguments that toggles the flyout. The message window also owns the tray icon (re-added on `TaskbarCreated`) and the hotkey. The icon is one monochrome card while closed; on open the back card fans out into the coloured logo (`Assets\tray-open-0..6.ico`, drawn by `tools/make-tray-icons.ps1`), played backwards on close, with a timer that exists only while it moves (skipped when animations are off).
- **Stay ready** (on by default): closing the flyout destroys the window and releases covers, then compacts the GC heap, trims the working set and puts the process in efficiency mode (EcoQoS, very low memory priority) until it opens again. "While it's closed" (Settings, owner's choice): **Use less memory** (default) does the above; **Open faster** only hides the window, keeping it loaded (trimmed working set, efficiency mode), so it reopens in a few ms instead of ~180 ms. Neither affects games, since the process exits while one runs. Off: closing the flyout exits the process. Autostart uses `--background`, which starts straight into this state.
- **First-run setup** (`SetupView`, shown until `SetupDone`): which launchers, tidy the desktop (the shortcut picker, embedded), how it opens and Start with Windows. Settings can run it again.

**Widget window**
- Borderless and 560 px wide.
- Docked to the right edge of the work area (Left / Right / Free in settings). Respect the taskbar and multiple monitors.
  - Placement is computed in physical pixels per monitor (`Placement.Compute`), with per-monitor DPI v2 (`app.manifest`).
  - Auto-hide taskbars reserve no work-area space but slide over everything, so their thickness on each edge is kept free too.
  - Re-place on `WM_SETTINGCHANGE` / `WM_DISPLAYCHANGE` / DPI changes (taskbar setting, resolution, monitors). Never poll.
  - With more than one monitor, Settings shows a Screen choice (stored as the monitor's device name; unknown = primary).
- Hide it from the taskbar and Alt+Tab (`ShowInTaskbar=false` plus the `WS_EX_TOOLWINDOW` style).
- "Stay on the desktop layer": keep the window at the bottom of the z-order (`SetWindowPos` with `HWND_BOTTOM`, re-applied when it gets activated). Test how this behaves with Win+D (Show desktop) and fix it if the widget disappears. Avoid the WorkerW wallpaper-parenting hack unless the simple approach fails.
- Use Windows 11 rounded corners via `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)`. Avoid `AllowsTransparency=true`, because it forces software rendering.
- Acrylic backdrop (owner's choice): WinUI's `DesktopAcrylicController` in a custom `SystemBackdrop` (`PanelBackdrop`), kept active while the panel isn't focused. Colors come from WinUI's theme resources (light, dark, high contrast and the accent color follow Windows live); the token table below is the dark design reference.

**Shelf view (main)**
- Header: a greeting for the time of day ("Good morning" / "Good afternoon" / "Good evening", set when the panel gets its data, never on a timer), a "…" quick actions menu (Game mode on/off with `Ctrl+G`, Game mode settings, Free up space, Add a game, Refresh with `F5`; an accent dot while Game mode is on; `docs/proposals.md` §16) and a settings button. The tray menu has Game mode and Free up space too. The game count is in the search box's placeholder ("Search 72 games").
- Search box: focused on open, filters as you type; its placeholder carries the game count.
- Filter chips (below the slot, in one row with the sort button, right above the grid they filter; owner's choice): All, Favorites, Recent, then the launchers that have games (most games first) and "Other" for manual games and shortcuts; beyond three launcher chips the rest go under "More".
- Sort menu: an icon button at the right end of the filter row (one toolbar row above the grid; its tooltip and screen-reader name say the current order) (Recently played, Most played, Name, Recently added, Size on disk, plus "Favorites first"), saved in settings. Cover size in Settings: Small (5 columns), Medium (4), Large (3), List.
- One fixed-height slot (168 px) under the search box, switched with a small `SelectorBar` in its top-right corner and remembered (`SpotlightShowsWeek`), so switching never moves the grid:
  - **Continue playing:** a hero banner of the last played game (its hero art, loaded only while shown, with a bottom fade), the game's logo (Steam's `logo.png`, local cache or downloaded once, max 56 px high and 60 % wide, soft shadow) or else its title, last played and launcher, and a Play button.
  - **This week:** the total, game count, most played game and a bar per day.
  - The switch shows only when both have something to show; the slot hides while searching.
- Cover grid (3:4 covers, the title below). Top-left: the launcher's own icon as a 28 px badge (read from the installed launcher's exe at runtime; manual games and shortcuts get none). Top-right: a star on favorites. Badges, the star and the 30 px Details (i) button are near-solid dark chips with a light edge (`BadgeBrush`, `BadgeStrokeBrush`), so they read on any cover. Hovering lifts the cover slightly with an accent outline and a "Play" label, and shows the Details button in its bottom-right corner (instant, no animation). Coming back from details focuses that cover programmatically, so no focus ring shows after a mouse click (keyboard keeps it).
- Games without store art get a generated cover: the game's icon on its own muted color, or a monogram (`FallbackArt`, `CoverColors`). No network.
- Clicking a cover (or a row in List) plays the game, like Enter and like the desktop shortcuts it replaces. Details open from the (i) button, Space, or right-click › Details. The (i) button stays in the UI Automation tree when not hovered, so screen readers can reach it.
- Favorites are pinned first.
- Footer hint: "Hides 10 s after a game starts · comes back when it closes", shown until three sessions are recorded.
- "Game mode needs an update" (an `InfoBar` above the search box, with Update…): shown when Game mode is on with items
  that need the admin helper and the helper is from another version; checked when the shelf gets its data.
- Empty states: a large Fluent icon, one plain sentence and the way forward (no games: Import desktop shortcuts / Add a game; no search match: Clear search / Add a game).

**Settings and Detail layout (boards 19 and 21, `docs/proposals.md` §18):** like Windows 11 Settings, one card per setting under a few group headings, a little apart (`GroupLabel`, `SettingCard`, `SettingLinkCard` in `Theme.xaml`); anything with more inside opens from its card (`Disclosure`, not WinUI's Expander). A card's second line says only state; explanations are tooltips.

**Detail view** (replaces the grid inside the same panel; a back button returns)
- Banner (Steam hero art) with the game's logo right of the cover (max 72 px high; the title text then hides), cover, title, store, and a favorite star. The developer isn't in the local store manifests, so it's left out unless a source provides it.
- A big Play button, an open-folder button and a "…" menu (it also has "Free up space…", opening with this game's drive).
- Stat tiles: Playtime (from our own sessions), Last played, Size on disk.
- Activity: one card with the playtime chart and the recent sessions (last 3–5).
- "This game": hide delay (Default / custom), Game mode, a custom cover, Launch options (opens: arguments, Start with, process to watch) and Anti-cheat (opens by itself when something needs attention).
- Links: Store page, Hide from shelf.

**Settings view** (groups: Opening Game Shelf, When a game starts, Shelf, Library, Window)
- Hide delay: Instantly / 5 s / 10 s / 30 s.
- Come back when the game closes (toggle).
- Library sources: Steam, Epic, GOG, EA app, Ubisoft Connect, Xbox app, Rockstar Games Launcher, Hytale Launcher and Desktop shortcuts, each with a checkbox, plus "Add a game".
- Position: Left / Right / Free.
- Stay on the desktop layer (toggle).
- Start with Windows: an `HKCU\...\Run` entry.
- Storage: "Free up space…" (milestone 15, `docs/proposals.md` §12).
  - The user types the new game's size in GB; each fixed drive gets a plan, and the best one is labelled "Best choice".
  - A treemap of the chosen drive shows the games by size; clicking a tile adds or leaves out that game. Tiles show
    the game's icon (the generated cover's icon, loaded a few at a time and kept for the page's life), with the
    name beside or below it where the tile has room; the picked-games list shows the same icons.
  - "Uninstall N games…" hands each game to its own launcher, one at a time: Steam's uninstall link, GOG Galaxy's
    game page, the launcher itself, or Settings › Apps for Xbox games and missing launchers. Game Shelf never deletes
    game files.
  - Mid-uninstall the page's state lives in `App.State.FreeSpace` (memory only), so reopening the flyout continues there.

## Performance budgets (hard requirements)

- Widget idle: 0% CPU. No timers, animations or polling running while it's idle on the desktop.
- Widget memory: WinUI needs more than the old ~80 MB WPF budget (about 130–230 MB open with covers); the owner accepted that. Still load covers with `DecodePixelWidth` sized to the tile, virtualize the grid, and shrink to a few MB while the flyout is closed (stay ready).
- Widget cold start: under ~1 s to visible, reading library.json first and rescanning after the window shows.
- During a game: the widget process does not exist. The watcher uses under ~3 MB RAM and 0% CPU after detection.

## Planned additions

Approved features after milestone 7 are specced in **`docs/proposals.md`** (baseline: commit `0eacc65`). Designs are on the canvas; the open ones are on its "Still to build" page. **Read section 0 of that file before starting the next milestone** (its progress table says which one: milestones 8–11, 15 and 19 are done, 12 More launchers and 14 Game mode are in progress, 13 Controller is built; 13, 14, 16 Look and moments, 17 Advanced sources and 18 Performance are to do). It lists what is new, what changes existing code, and which owner decisions override this brief:

- Game mode's separate elevated helper is an approved exception to "never ask for admin rights". The widget and the watcher still never elevate.
- Cover badges show the launcher's icon instead of text, and manually added games get no badge.
- Flyout plus a notification-area icon becomes the default display mode. The desktop-docked mode stays as an option.

Items under "Not decided" in that file must not be built.

## Build order

1. Solution skeleton, models, JSON storage, and a Steam source with a plain list UI. Verify Steam detection on the real machine.
2. The launcher and the watcher with the full hide → return loop. Test it with a Steam game and a plain exe game.
3. The shelf UI to spec: grid, covers, search, filters, continue card.
4. The detail view and session tracking.
5. The settings view, autostart and the desktop-layer behavior. Include a way to show hidden games again (hiding currently only has an Undo in the toast).
6. Epic, GOG, desktop-shortcut import and manual add.
7. Polish: multi-monitor handling, empty states, the error toast, and a single-file publish script.
8.–15. The approved additions, in the order given at the end of `docs/proposals.md`.

## Errors

Unexpected UI errors are logged to `%LOCALAPPDATA%\GameShelf\error.log` (rotated at 1 MB) and shown as a toast; the widget keeps running. A burst of errors (more than 5 in 10 s) is not caught, so a broken state ends instead of spinning the CPU.

## Installer

`GameShelfSetup.exe` installs per user to `%LOCALAPPDATA%\Programs\GameShelf\` (no admin), adds the Start menu
shortcut and the Settings → Apps entry. Its install/update dialog shows the README's disclaimer and enables its
button only once "I've read and understood the disclaimer" is ticked (`/S` implies it). It updates in place: it quits a running widget (`--quit`) and restarts it,
and moves an in-use watcher aside (`.old`) so a game in progress keeps being tracked. Uninstall restores imported
shortcuts (`GameShelf.exe --restore-shortcuts`) and deletes data only when asked. Details and its own security
rules: `docs/installer.md`. The version lives in `Directory.Build.props`.

## Security rules

The widget runs as the normal user with no elevation, and must never ask for admin rights. The one exception (owner's decision, spec §11) is Game mode's "Allow…", which starts `GameShelf.Elevated.exe` once with a UAC prompt; the widget and the watcher themselves never elevate. Treat these as untrusted input: every launcher's manifests, config files and registry entries (library folders can be on drives other users or devices can write to), desktop shortcuts, and downloaded images.

- **Validate before use.** Store ids must match their expected shape (Steam appid and GOG id: digits only; Epic ids: letters, digits, `-`, `_`, `.`). Steam install folders must be a single folder name under `steamapps\common`. Every other install folder goes through `InstallFolder.Validate`: a full, existing path that isn't a drive root, isn't inside Windows, and isn't a shared folder itself (Program Files, the user profile, Desktop, …). A GOG exe must be inside its install folder. Reject the entry otherwise.
- **Bounded parsing.** Parsers cap file size and nesting depth and fail with `FormatException`, never a crash. Downloads have a size cap.
- **Launching.** Only launch what came from a validated library entry or that the user picked themselves. Build store URIs from validated ids only. Pass arguments with `ProcessStartInfo.ArgumentList`, or quote them by the Windows argv rules when building a command line by hand.
- **Native code.** P/Invoke only Windows system DLLs, loaded from System32 only (`DefaultDllImportSearchPaths(System32)` in every assembly; the setup also links with `/DEPENDENTLOADFLAG:0x800` and calls `SetDefaultDllDirectories`, since it runs from Downloads).
- **Installer.** `asInvoker`, fixed paths computed from known folders (never read back from the registry), deletes only the files it wrote and never through links, hashes each file after writing it, ends only processes running from its own install folder. See `docs/installer.md`.
- **Autostart (milestone 5).** Write the Run key value with the exe path in quotes, and point it at the actual install location.
- **Shortcut import.** Read `.lnk` targets with the Shell API (`IShellLinkW`, load only, never `Resolve`) and `.url` files as plain INI. Never execute a shortcut while scanning. Only launch what the user ticked in the picker. Moves stay reversible; where the shared desktop can't be changed without admin rights, copy instead and leave the original.
- **Game mode, no-admin part (milestone 14).** Every change is journaled (flushed) before it's made, and put back only if it's still the value we set. Apps are closed only with WM_CLOSE to their visible windows, never ended; only full paths to an `.exe` outside Windows, Store apps and our own folder are closed or reopened (checked in the widget, the watcher and when reading the journal), and only the exe recorded in the journal is reopened, without arguments. Priority goes to Above normal at most, never lowered or set High/Realtime.
- **Game mode, admin part (the helper).**
  - Where it lives: only in `%ProgramFiles%\GameShelf\` (a hashed copy of itself, made by "Allow…"); it never runs elevated from a user-writable folder except for that one approved `--install`. Its scheduled tasks run that path as the user's SID with highest privileges, only while they're signed in; their security descriptor lets the user run them (read and execute), not change them. It's linked with `/DEPENDENTLOADFLAG:0x800`.
  - What it trusts: nothing from the user's side. The request file is size-capped, never a link, and parsed strictly (`ElevatedRequest.Parse`): service ids only from the compiled-in allowlist (WSearch, SysMain, DiagTrack, Spooler), app names only as plain exe file names (no paths, quotes or slashes) and never Windows' own or security processes (`ElevatedPart.IsSystemApp`: svchost, Defender, …), the upload limit only 1, 5 or 20, ids only 32 hex digits; the size cap applies to the bytes actually read. Paths come from known folders only. One run at a time through a file lock in its admin-only folder (not a named mutex, which a user's program could take first). "Allow…" holds its own exe open without delete sharing from the start, so the copy put into Program Files is the file the user approved.
  - Its files: `%ProgramData%\GameShelf\` with a protected ACL (SYSTEM and Administrators write, Users read). A folder that is already there counts as ours only if its owner is Administrators or SYSTEM and it isn't a link; otherwise "Allow…" moves it aside and the helper refuses to work.
  - What it changes: services are only stopped (never their start type), only when running, accepting a stop and with no running dependents, and restarted only if we stopped them and they weren't disabled meanwhile. QoS policies go into the ActiveStore (gone at restart), are named `GameShelf …`, and only those are removed. The Delivery Optimization value is restored exactly (including "not set", and its key if we created it). Journal before each change; restore only what is still the value we set.
  - Removal: Settings › Game mode › "Remove it" and the uninstaller run the installed copy's `--uninstall` (a UAC prompt; a silent uninstall skips it). It puts back what's still applied, deletes this user's tasks, marker and result, and with the last user the ProgramData folder and itself.
- **Files we own vs. the user's.** Only delete files inside our own AppData folders. Never follow paths from data files to delete or overwrite anything outside them.
- **Network.** HTTPS to the store CDNs only, for artwork. No telemetry, no other outbound calls.

## Working rules for Claude Code

- After each change, run `dotnet build`. After each milestone, run the app and test it on this machine's real Steam/Epic installs.
- Check the performance budgets with Task Manager or `Get-Process` after milestones 2, 3 and 7.
- Never delete the user's files or shortcuts. Moves must be reversible.
- Keep the watcher dependency-free and tiny.
- Never add a third-party package without asking first (see Dependencies policy).
- Commit at each milestone with a short message.
- Routine work can go to other (cheaper) coding agents as task cards in `docs/tasks/` (see `AGENTS.md`). When a
  session turns a plan into cards: one goal per card, exact files, exact insertion points and expected values
  checked against the code, and a `./check.ps1 -Task` line under "Done when". Review agent branches before merging
  and set the card to `done`. Keep `AGENTS.md`'s "Don't touch" list and `check.ps1`'s protected list in sync.
