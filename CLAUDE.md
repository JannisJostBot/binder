# Game Shelf — project brief

A lightweight Windows desktop widget that replaces game shortcuts on the desktop. It shows the user's installed games as a clean cover grid docked on the desktop, launches them, gets completely out of the way while a game runs, and comes back when the game closes.

**Top priority: zero performance cost while gaming.** When a game is running, the widget process must not exist. Only a tiny watcher process may remain, blocked/sleeping.

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
Boards: "Desktop with widget", "Game detail", "Settings", "Launch → hide → return".

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
    Library/                SteamSource, EpicSource, GogSource, InstallFolder (folder safety),
                            ShortcutFile (.lnk/.url reading), ShortcutImporter (import, restore, manual add)
    Models/                 Game, Session, Settings, ImportedShortcut
    Services/               Launcher, CoverCache, Storage, DesktopLayer, Autostart, GameActions,
                            Placement (pure pixel math), Monitors (displays, auto-hide taskbars), ShellNotify, ErrorLog
    Views/                  ShelfView, DetailView, SettingsView, ImportView (the launch toast lives in MainWindow)
  src/GameShelf.Watcher/    Program.cs only
  src/GameShelf.Setup/      GameShelfSetup.exe: per-user installer, updater and uninstaller (Native AOT, task dialogs)
  tests/GameShelf.Tests/    plain console unit tests for the non-UI code (no test framework packages)
  tests/smoke.ps1           Windows UI smoke test run by CI (see .github/workflows/ci.yml)
  tests/setup-test.ps1      CI test of the installer: install, update while running, uninstall
  publish.ps1               builds what ships into publish/: app\ (the WinUI widget folder and the Native AOT
                            watcher) and GameShelfSetup.exe with app\ inside (optional -CertificateThumbprint signing)
  README.md                 overview, install, build, privacy, disclaimer
  docs/installer.md         how to build, install, update and uninstall, and how the installer works
```

Data lives in `%APPDATA%\GameShelf\` (`settings.json`, `library.json`, `sessions.json`, `imported-shortcuts.json`, and the `Shortcuts\` folder imported shortcuts are moved into). Covers are cached in `%LOCALAPPDATA%\GameShelf\covers\`.

## Core flow: launch → hide → return

1. The user clicks Play. `Launcher` starts the game: via the store URI (Steam/Epic) or by running the exe directly (GOG/manual/shortcut).
2. The widget shows the in-panel **launch toast**: "Starting [Game]", a countdown (default 10 s, per-game override), and a **Keep open** button.
3. The widget starts `GameShelf.Watcher.exe` with arguments: game id, install folder, optional explicit process name, and the timeout.
4. When the countdown ends, the widget **exits completely** (process ends, not just hidden). If the user pressed Keep open, it stays and nothing else changes.
5. The watcher:
   - Looks for the game process. Store launchers start the store app first, so match any running process whose executable path is inside the game's install folder, or the explicit process name if set. During this detection phase only, polling every ~2 s is fine, for up to ~3 minutes.
   - If it never finds the game, it relaunches the widget showing "Couldn't detect the game" and exits.
   - Once found, it **blocks** on the process handle (`Process.WaitForExitAsync` / `WaitForSingleObject`). No polling while the game runs.
   - Some games restart themselves (launcher → game exe). After an exit, re-check the install folder for ~5 s before deciding the game is really closed.
   - On exit it appends a session (start, end, game id) to `sessions.json`, relaunches `GameShelf.exe` if "Come back when the game closes" is on, and exits.
6. Single instance: use a named mutex in the widget. A second launch focuses the existing one.

## Library sources

- **Steam**
  - Find the Steam path in `HKCU\Software\Valve\Steam\SteamPath`.
  - Parse `steamapps\libraryfolders.vdf` for all library folders, then each `appmanifest_*.acf` (appid, name, installdir, SizeOnDisk). You'll need a small VDF parser.
  - Skip non-games: redistributables (e.g. appid 228980), Proton, SDKs, dedicated servers.
  - Launch with `steam://rungameid/{appid}`.
  - Covers: the local Steam library cache first, else download `library_600x900` from the Steam CDN once and cache it.
- **Epic**
  - Read `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON: DisplayName, InstallLocation, LaunchExecutable, AppName, CatalogNamespace, CatalogItemId).
  - Launch with `com.epicgames.launcher://apps/{Namespace}%3A{CatalogItemId}%3A{AppName}?action=launch&silent=true`.
  - Covers: use a placeholder unless the user sets one.
- **GOG**: Read `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\*` (gameName, path, exe) and launch the exe directly.
- **EA app**: Settings → Apps entries (`HKLM\...\Uninstall\*`) whose uninstaller is EA's `EAInstaller\…\Cleanup.exe uninstall_game` (DisplayName, InstallLocation, DisplayIcon = the exe, EstimatedSize). The content id is the first `<contentID>` in `{InstallLocation}\__Installer\installerdata.xml`. Launch with `origin2://game/launch?offerIds={contentId}&autoDownload=1`, or the exe if there's no content id.
- **Ubisoft Connect**: `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}` (InstallDir), named by the `Uplay Install {id}` uninstall entry (DisplayName, EstimatedSize, DisplayIcon .ico). Launch with `uplay://launch/{id}/0`. Entries whose folder no longer exists are skipped.
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
  - Full keyboard navigation (arrow keys in the grid, Enter to play, Esc to go back, type-to-search).
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
| Radii | 12 panel, 8 cards and covers, 6 buttons |

**Display modes (milestone 9)**
- **Flyout** (default): a notification-area icon and a global shortcut (Win+Shift+G; Win+Shift+J, Ctrl+Alt+G or Off in Settings) open the panel next to the screen edge with a short slide-in (skipped when animations are off). It closes on Esc, click-away (unless pinned or a dialog is open), the icon or the shortcut again.
- **On the desktop**: the docked, always-visible panel on the desktop layer described below. The shortcut brings it in front for a moment.
- `Program.Main` holds the single-instance mutex before WinUI starts; a second start forwards its arguments to the running instance's hidden `MessageWindow` (WM_COPYDATA) and exits. With no arguments that toggles the flyout. The message window also owns the tray icon (re-added on `TaskbarCreated`) and the hotkey.
- **Stay ready** (on by default): closing the flyout destroys the window and releases covers, then compacts the GC heap, trims the working set and puts the process in efficiency mode (EcoQoS, very low memory priority) until it opens again. Off: closing the flyout exits the process. Autostart uses `--background`, which starts straight into this state.
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
- Header: "Games", the installed count and a settings button.
- Search box: focused on open, filters as you type.
- Filter chips: All, Favorites, Recent, then the launchers that have games (most games first) and "Other" for manual games and shortcuts; beyond three launcher chips the rest go under "More".
- Sort menu above the grid (Recently played, Most played, Name, Recently added, Size on disk, plus "Favorites first"), saved in settings. Cover size in Settings: Small (5 columns), Medium (4), Large (3), List.
- "Continue playing" card: the last played game with a Play button.
- Cover grid (3:4 covers, the title below). Top-left: the launcher's own icon as a 24 px badge (read from the installed launcher's exe at runtime; manual games and shortcuts get none). Top-right: a star on favorites. Hovering shows a play overlay.
- Games without store art get a generated cover: the game's icon on its own muted color, or a monogram (`FallbackArt`, `CoverColors`). No network.
- Clicking a cover opens the Detail view. Clicking the hover Play button launches directly.
- Favorites are pinned first.
- Footer hint: "Hides 10 s after a game starts · comes back when it closes".

**Detail view** (replaces the grid inside the same panel; a back button returns)
- Banner (Steam hero art), cover, title, store, and a favorite star. The developer isn't in the local store manifests, so it's left out unless a source provides it.
- A big Play button, an open-folder button and a "…" menu.
- Stat tiles: Playtime (from our own sessions), Last played, Size on disk.
- Recent sessions list (last 3–5).
- "This game only" settings: hide delay (Default / custom), process to watch (auto-detected or picked), and a custom cover.
- Links: Store page, Hide from shelf.

**Settings view**
- Hide delay: Instantly / 5 s / 10 s / 30 s.
- Come back when the game closes (toggle).
- Library sources: Steam, Epic, GOG, EA app, Ubisoft Connect and Desktop shortcuts, each with a checkbox, plus "Add a game".
- Position: Left / Right / Free.
- Stay on the desktop layer (toggle).
- Start with Windows: an `HKCU\...\Run` entry.

## Performance budgets (hard requirements)

- Widget idle: 0% CPU. No timers, animations or polling running while it's idle on the desktop.
- Widget memory: WinUI needs more than the old ~80 MB WPF budget (about 130–230 MB open with covers); the owner accepted that. Still load covers with `DecodePixelWidth` sized to the tile, virtualize the grid, and shrink to a few MB while the flyout is closed (stay ready).
- Widget cold start: under ~1 s to visible, reading library.json first and rescanning after the window shows.
- During a game: the widget process does not exist. The watcher uses under ~3 MB RAM and 0% CPU after detection.

## Planned additions

Approved features after milestone 7 are specced in **`docs/proposals.md`** (baseline: commit `0eacc65`). Designs are on the canvas's "Proposals" and "More features" pages. **Read section 0 of that file before starting milestone 8.** It lists what is new, what changes existing code, and which owner decisions override this brief:

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
8.–14. The approved additions, in the order given at the end of `docs/proposals.md`.

## Errors

Unexpected UI errors are logged to `%LOCALAPPDATA%\GameShelf\error.log` (rotated at 1 MB) and shown as a toast; the widget keeps running. A burst of errors (more than 5 in 10 s) is not caught, so a broken state ends instead of spinning the CPU.

## Installer

`GameShelfSetup.exe` installs per user to `%LOCALAPPDATA%\Programs\GameShelf\` (no admin), adds the Start menu
shortcut and the Settings → Apps entry, and updates in place: it quits a running widget (`--quit`) and restarts it,
and moves an in-use watcher aside (`.old`) so a game in progress keeps being tracked. Uninstall restores imported
shortcuts (`GameShelf.exe --restore-shortcuts`) and deletes data only when asked. Details and its own security
rules: `docs/installer.md`. The version lives in `Directory.Build.props`.

## Security rules

The widget runs as the normal user with no elevation, and must never ask for admin rights. Treat these as untrusted input: Steam/Epic/GOG manifests and registry entries (library folders can be on drives other users or devices can write to), desktop shortcuts, and downloaded images.

- **Validate before use.** Store ids must match their expected shape (Steam appid and GOG id: digits only; Epic ids: letters, digits, `-`, `_`, `.`). Steam install folders must be a single folder name under `steamapps\common`. Every other install folder goes through `InstallFolder.Validate`: a full, existing path that isn't a drive root, isn't inside Windows, and isn't a shared folder itself (Program Files, the user profile, Desktop, …). A GOG exe must be inside its install folder. Reject the entry otherwise.
- **Bounded parsing.** Parsers cap file size and nesting depth and fail with `FormatException`, never a crash. Downloads have a size cap.
- **Launching.** Only launch what came from a validated library entry or that the user picked themselves. Build store URIs from validated ids only. Pass arguments with `ProcessStartInfo.ArgumentList`, or quote them by the Windows argv rules when building a command line by hand.
- **Native code.** P/Invoke only Windows system DLLs, loaded from System32 only (`DefaultDllImportSearchPaths(System32)` in every assembly; the setup also links with `/DEPENDENTLOADFLAG:0x800` and calls `SetDefaultDllDirectories`, since it runs from Downloads).
- **Installer.** `asInvoker`, fixed paths computed from known folders (never read back from the registry), deletes only the files it wrote and never through links, hashes each file after writing it, ends only processes running from its own install folder. See `docs/installer.md`.
- **Autostart (milestone 5).** Write the Run key value with the exe path in quotes, and point it at the actual install location.
- **Shortcut import.** Read `.lnk` targets with the Shell API (`IShellLinkW`, load only, never `Resolve`) and `.url` files as plain INI. Never execute a shortcut while scanning. Only launch what the user ticked in the picker. Moves stay reversible; where the shared desktop can't be changed without admin rights, copy instead and leave the original.
- **Files we own vs. the user's.** Only delete files inside our own AppData folders. Never follow paths from data files to delete or overwrite anything outside them.
- **Network.** HTTPS to the store CDNs only, for artwork. No telemetry, no other outbound calls.

## Working rules for Claude Code

- After each change, run `dotnet build`. After each milestone, run the app and test it on this machine's real Steam/Epic installs.
- Check the performance budgets with Task Manager or `Get-Process` after milestones 2, 3 and 7.
- Never delete the user's files or shortcuts. Moves must be reversible.
- Keep the watcher dependency-free and tiny.
- Never add a third-party package without asking first (see Dependencies policy).
- Commit at each milestone with a short message.
