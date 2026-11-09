# Game Shelf — project brief

A lightweight Windows desktop widget that replaces game shortcuts on the desktop: a cover grid of the user's
installed games that launches them, gets completely out of the way while a game runs, and comes back when it closes.
(The website calls it Binder; the code still says GameShelf, don't rename anything.)

**Top priority: zero performance cost while gaming.** While a game runs the widget process must not exist; only the
tiny watcher may remain, blocked/sleeping.

**Scope: a launcher, not a game toolbox.** A feature earns its place only if it helps the user start, find or manage
their games, and it must feel like part of Windows. When an idea doesn't clearly pass, leave it out (or note it under
"Not decided" in `docs/proposals.md`).

## Minimal resource use, always

- Work only in response to a user action or a launch event. No background timers, polling, file watchers or periodic
  refreshes while idle (a timer may exist only while something visibly moves).
- Lazy: read only what the first screen needs, defer the rest, release what's no longer visible.
- Cache expensive results (covers, parsed manifests) on disk; never re-download or re-parse needlessly.
- Cheap UI: hardware rendering, a virtualized grid, no blur/transparency of our own, no continuous or decorative
  animations; short hover/transition effects only if they cost nothing when idle and are skipped with "Animation
  effects" off. The one approved exception is the panel's Acrylic backdrop (`PanelBackdrop`, WinUI's
  `DesktopAcrylicController`, active while unfocused; solid when Transparency is off, in high contrast or unsupported).
- No heavy or reflection-heavy dependencies. Between simpler-but-heavier and a bit-more-code-but-lighter, pick lighter.
- Measure, don't assume: report CPU, memory and startup after a milestone; fix regressions before moving on.

## Budgets (hard)

- Idle widget: 0% CPU. Memory ~130–230 MB open with covers (accepted by the owner); covers decoded with
  `DecodePixelWidth` sized to the tile; a few MB while the flyout is closed (stay ready).
- Cold start under ~1 s to visible: read `library.json` first, rescan after the window shows.
- During a game: no widget process; the watcher under ~3 MB RAM and 0% CPU after detection.

## Tech stack

- C# / WinUI 3 (Windows App SDK, unpackaged, self-contained, Native AOT) on the current LTS .NET (.NET 10). Windows
  10/11 x64. `System.Text.Json` for storage. No third-party UI frameworks.
- AOT rules: no `[ComImport]` (use `[GeneratedComInterface]`), `LibraryImport` not `DllImport`, `x:Bind` or
  code-behind instead of `{Binding}`, `[UnmanagedCallersOnly]` for native callbacks, no reflection in app code.

## Dependencies policy

- Prefer the base library and Windows APIs; writing a little code (e.g. the VDF parser) beats adding a package.
- **Ask the owner before adding any third-party package**: what for, why built-ins don't do, size/impact. Only
  well-known, actively maintained, permissively licensed packages; pin exact versions and list them here.
- Approved: `Microsoft.WindowsAppSDK.WinUI` 2.6.4 (MIT), only this component, not the `Microsoft.WindowsAppSDK`
  umbrella (it bundles AI runtimes we don't use).
- The watcher stays dependency-free and tiny.

## Layout

```
src/GameShelf/            the WinUI widget
  Library/                sources (*Source.cs), InstallFolder (folder safety), Vdf, ShortcutFile/ShortcutImporter,
                          LaunchOptions, GameScan, AntiCheat, PlaytimeStats, CrashAnalysis, SpacePlanner (pure logic)
  Models/                 Game, Session, Settings, ImportedShortcut, CrashRecord
  Services/               Launcher, CoverCache, Storage, Windows integration (DesktopLayer, Monitors, Placement,
                          Autostart, ShellNotify, PcChecks, …), Gamepad (XInput, polled only while the panel is
                          active and a pad is connected), GameModeService, Chime/Sound
  ViewModels/             MVVM: ObservableObject, RelayCommand, one <Page>ViewModel per page (SettingsViewModel so far)
  Views/                  ShelfView, DetailView, SettingsView, SetupView, ImportView, FreeSpaceView, GameModeView,
                          GameModeToast; Format.cs the text formatting
  Themes/Theme.xaml        shared styles and brushes
  Program.cs, MessageWindow.cs   single instance, tray icon, hotkey
src/GameShelf.Watcher/    windowless; Program.cs, GameMode.cs (apply/restore + journal), ElevatedClient.cs,
                          GameModeShared.cs (also compiled into the widget, helper and setup)
src/GameShelf.Elevated/   Game mode's admin helper (Native AOT, windowless, run by scheduled tasks)
src/GameShelf.Setup/      GameShelfSetup.exe: per-user installer/updater/uninstaller (docs/installer.md)
tests/GameShelf.Tests/    plain console unit tests (no test framework packages); smoke.ps1 / setup-test.ps1 are CI-only
scripts/                  publish.ps1 (builds publish\app: widget folder + AOT watcher + helper, and GameShelfSetup.exe),
                          check.ps1 (the hard rules on a diff, then build + tests; -Task for agent cards),
                          make-tray-icons.ps1 / make-watcher-icon.ps1 (redraw icons after a logo change)
site/                     one-page website: plain HTML + CSS, no scripts, nothing from other servers
docs/proposals.md         approved feature specs, progress table (§0), decisions log, "Not decided"
docs/tasks/, AGENTS.md    task cards and rules for cheaper coding agents
```

Data: `%APPDATA%\GameShelf\` (`settings.json`, `library.json`, `sessions.json`, `imported-shortcuts.json`,
`crashes.json`, `Shortcuts\`). `%LOCALAPPDATA%\GameShelf\`: `covers\`, `error.log`, and Game mode's
`gamemode-journal.json` (only while applied), `gamemode.log`, `gamemode-last.txt`, `gamemode-request.txt`. The
helper: `%ProgramData%\GameShelf\` (`journal.json`, `elevated.log`, `allowed-<SID>.txt`, `result-<SID>.txt`).
The version lives in `Directory.Build.props`.

## Core flow: launch → hide → return

1. Play: `Launcher` starts the game via the launcher's URI/command, or the exe directly (GOG, manual, shortcut).
2. The in-panel launch toast: "Starting [Game]", a countdown (default 10 s, per-game override) and **Keep open**.
3. The widget starts `GameShelf.Watcher.exe` (game id, install folder, optional process name, timeout).
4. When the countdown ends the widget **exits** (unless Keep open).
5. The watcher:
   - Detection: any process whose exe is inside the install folder, or the explicit process name. Polling every
     ~2 s is allowed here only, for up to ~3 min. Never found: relaunch the widget with "Couldn't detect the game".
   - Found: **block** on the process handle; no polling while the game runs. After an exit, re-check the folder
     for ~5 s (games that restart themselves).
   - On exit: append the session to `sessions.json`, relaunch the widget if "Come back when the game closes" is on.
   - Game mode (when on for the game): apply right after start (and tell a widget that is still up, never starting
     it for that), Above normal priority once found, restore everything before relaunching the widget, which shows
     the result. Admin items go through the helper's scheduled tasks (`GameShelf Apply/Restore <SID>`, waiting on
     `Local\GameShelf.Elevated.<id>`). Not detected: restore at once.
6. A game the watcher can't follow (no folder, no process name) starts without hiding the widget, with a toast
   pointing to "Process to watch".

Single instance: `Program.Main` holds a mutex before WinUI starts; a second start forwards its arguments to the
hidden `MessageWindow` (WM_COPYDATA). With no arguments that toggles the flyout.

## Code structure (MVVM, being migrated page by page)

- A page's state and logic go in `ViewModels/<Page>ViewModel.cs` (derives from `ObservableObject`; setters use
  `SetProperty` or report only real changes, so filling a page never counts as a user change). The view binds with
  `x:Bind` (`Mode=OneWay`/`TwoWay`, `Command=`); its code-behind keeps only pure view work (focus, animation,
  `Disclosure`, drawing rows).
- What a page asks its owner to do is an interface (`ISettingsActions`), implemented by `MainWindow` in its own
  partial file (`MainWindow.Settings.cs`), so a missing action is a compile error, not a dead button.
- View models don't reference WinUI types, so `tests/GameShelf.Tests` compiles them and tests them with a spy for
  the actions interface (`ViewModelTests.cs`). Add checks there with each view model.

## Library sources

Steam, Epic, GOG, EA app, Ubisoft Connect, Xbox app, Rockstar, Hytale, imported desktop shortcuts and manual adds;
each `*Source.cs` documents where it reads and how it launches. Rescan on startup and on manual refresh only.

- Steam: skip non-games (redistributables, Proton, SDKs, servers). When Steam isn't running, start
  `steam.exe -silent <link>` so its window doesn't open.
- Shortcut import: scan the user's and the Public Desktop; the user ticks which are games; chosen shortcuts are
  **moved** to `%APPDATA%\GameShelf\Shortcuts\` (reversible, "Restore to desktop"); Steam/Epic URIs merge with the
  detected entry.
- Covers: store art where a source has it (Steam: local cache, else the CDN once, cached); otherwise a generated
  cover (the game's icon on its own muted color, or a monogram), no network.

## UI: native Windows 11

Designs: the owner's canvas https://claude.ai/artifact/9tZEP6HSrETxwGwpSbMg1Q (pages "Shelf", "Game detail",
"Window, setup & settings", "Still to build"; board numbers match `docs/proposals.md`). The built UI in `Views/` is
the reference for what exists; these rules apply to every change:

- Fluent controls, Windows 11 radii, spacing and type ramp; Segoe UI Variable; Segoe Fluent Icons (MDL2 fallback).
  No custom chrome that fights Windows, no web-style UI, no non-system fonts, no off-brand colors or emoji icons.
- Colors from WinUI theme resources: the Windows accent (fallback `#4CC2FF`), light/dark and high contrast, live.
  Dark reference: panel `#202024`, border `#34343A`, surface `#2A2A2F`, text `#F3F3F5` / `#A9A9B2`, text on
  accent `#0A1A24`.
- Radii: 8 panel, system-drawn via `DWMWCP_ROUND` (never clip the window to a custom radius); 8 cards and covers
  (`OverlayCornerRadius`); 4 controls (`ControlCornerRadius`). No `AllowsTransparency` (software rendering).
- Respect per-monitor DPI (v2, physical-pixel placement in `Placement.Compute`), text size, Animation effects off,
  high contrast, multiple monitors and auto-hide taskbars. Re-place only on `WM_SETTINGCHANGE` /
  `WM_DISPLAYCHANGE` / DPI changes, never poll.
- Full keyboard (arrows, Enter plays, Space details, Esc back, type-to-search), native context menus, tooltips and
  UI Automation names on all controls.
- Hidden from taskbar and Alt+Tab (`WS_EX_TOOLWINDOW`). Standard file picker, Run key autostart (exe path quoted,
  the real install location), data in AppData.
- Settings and Detail (boards 19, 21): one card per setting under group headings (`GroupLabel`, `SettingCard`,
  `SettingLinkCard`; `Disclosure` for cards that open, not Expander). A card's second line says only state;
  explanations go in tooltips.
- Owner decisions to keep: clicking a cover plays it (details from the (i) button, Space or right-click); the
  greeting is set when the panel gets data, never on a timer; the top slot (Continue playing / This week) is a fixed
  168 px so switching never moves the grid; favorites pinned first; launcher icon badges, none for manual games;
  filter chips All, Favorites, Recent, then launchers that have games (most first, beyond three under "More") and
  "Other", in one row with the sort button right above the grid.

Display modes: **Flyout** (default; tray icon and Win+Shift+G) or **On the desktop** (docked, kept at the bottom of
the z-order with `HWND_BOTTOM`, no WorkerW hack). **Stay ready** (default on): closing the flyout destroys the
window, releases covers, trims memory and enters efficiency mode ("Open faster" only hides it); off exits the
process. Autostart uses `--background`.

Errors: unexpected UI errors go to `%LOCALAPPDATA%\GameShelf\error.log` (1 MB rotation) and a toast; a burst
(more than 5 in 10 s) isn't caught, so a broken state ends instead of spinning.

## Security rules

The widget and watcher run as the normal user and never ask for admin rights. The one exception (owner, spec §11):
Game mode's "Allow…" starts `GameShelf.Elevated.exe` once with a UAC prompt. Untrusted input: every launcher's
manifests, config files and registry entries (library folders can be on drives others can write to), desktop
shortcuts, downloaded images.

- **Validate before use.** Store ids match their shape (Steam appid, GOG id: digits; Epic ids: letters, digits,
  `-`, `_`, `.`). Steam install folders: one folder name under `steamapps\common`. Every other install folder goes
  through `InstallFolder.Validate` (full, existing, not a drive root, not inside Windows, not a shared folder like
  Program Files, the profile or Desktop). A GOG exe must be inside its install folder. Reject otherwise.
- **Bounded parsing.** Parsers cap file size and nesting depth and fail with `FormatException`, never a crash.
  Downloads have a size cap.
- **Launching.** Only what came from a validated library entry or that the user picked. Store URIs from validated
  ids only. Arguments via `ProcessStartInfo.ArgumentList`, or quoted by the Windows argv rules.
- **Native code.** Only Windows system DLLs, from System32 (`DefaultDllImportSearchPaths(System32)` in every
  assembly; the setup and helper also link with `/DEPENDENTLOADFLAG:0x800`, the setup calls
  `SetDefaultDllDirectories`).
- **Shortcuts.** `.lnk` via `IShellLinkW` (load only, never `Resolve`), `.url` as plain INI. Never execute a
  shortcut while scanning. Where the shared desktop can't be changed without admin, copy and leave the original.
- **Files.** Only delete inside our own AppData folders; never follow paths from data files to delete or overwrite
  anything. Never delete the user's files or shortcuts; moves stay reversible. Never delete game files: Free up
  space hands each uninstall to the game's own launcher (or Settings › Apps).
- **Network.** HTTPS to the store CDNs only, for artwork. No telemetry, no other calls.
- **Installer.** `asInvoker`, fixed paths from known folders (never read back from the registry), deletes only
  files it wrote and never through links, hashes each file after writing, ends only processes from its own
  install folder. Details: `docs/installer.md`.
- **Game mode, no-admin part.** Every change is journaled (flushed) before it's made and put back only if it's still
  the value we set. Apps are closed only with WM_CLOSE to visible windows, never ended; only full paths to an
  `.exe` outside Windows, Store apps and our own folder are closed or reopened (checked in the widget, the watcher
  and when reading the journal); only the journaled exe is reopened, without arguments. Priority: Above normal at
  most, never lowered, never High/Realtime.
- **Game mode, admin part (the helper).**
  - Lives only in `%ProgramFiles%\GameShelf\` (a hashed copy made by "Allow…", which holds its own exe open without
    delete sharing so the copy is the approved file); never runs elevated from a user-writable folder except that
    one `--install`. Its tasks run that path as the user's SID, highest privileges, only while signed in; the user
    may run them, not change them.
  - Trusts nothing from the user's side. The request file is size-capped (on bytes actually read), never a link,
    parsed strictly (`ElevatedRequest.Parse`): services only from the compiled-in allowlist (WSearch, SysMain,
    DiagTrack, Spooler); app names only plain exe file names, never system or security processes
    (`ElevatedPart.IsSystemApp`); upload limit only 1, 5 or 20; ids only 32 hex digits. Paths from known folders
    only. One run at a time via a file lock in its admin-only folder (not a named mutex).
  - Its folder `%ProgramData%\GameShelf\` has a protected ACL (SYSTEM and Administrators write, Users read); an
    existing folder counts as ours only if owned by Administrators/SYSTEM and not a link, else "Allow…" moves it
    aside and the helper refuses to work.
  - Services are only stopped (never their start type), only when running, stoppable and without running
    dependents, and restarted only if we stopped them and they weren't disabled meanwhile. QoS policies go into the
    ActiveStore, named `GameShelf …`, and only those are removed. The Delivery Optimization value is restored
    exactly (including "not set" and its key if we created it).
  - Removal ("Remove it", uninstaller) runs the installed copy's `--uninstall`: restores what's still applied,
    deletes this user's tasks, marker and result, and with the last user the ProgramData folder and itself.

## Working rules

- What to build next: `docs/proposals.md` §0 progress table, then the section it names. Items under "Not decided"
  there must not be built. Keep that table and this brief current when something changes.
- After each change run `./scripts/check.ps1` (or `dotnet build` + the unit tests). This repo builds on Windows only;
  on Linux only the unit tests run. Test new behaviour on the real PC's launchers where possible.
- Commit at each milestone with a short message.
- Routine work can go to cheaper agents as task cards (`docs/tasks/README.md`, `AGENTS.md`): one goal per card,
  exact files, insertion points and expected values checked against the code, and a `./scripts/check.ps1 -Task` line under
  "Done when". Review agent branches before merging and set the card to `done`. Keep `AGENTS.md`'s "Don't touch"
  list and `check.ps1`'s protected list in sync.
