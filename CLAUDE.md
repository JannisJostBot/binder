# Binder — project brief

A lightweight Windows desktop widget that replaces game shortcuts on the desktop: a cover grid of the user's
installed games that launches them, gets completely out of the way while a game runs, and comes back when it closes.

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
- Data structures by the operation: lookups by id or path go through a `Dictionary`/`HashSet` built once, never a
  list search inside a loop over games or sessions (O(n·m)); one pass over the sessions per screen, not one per game.
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
- Approved: `Microsoft.WindowsAppSDK.WinUI` 2.6.4 (its binaries are under the Microsoft Software License Terms for
  the Windows App SDK, not MIT; redistributable with the app), only this component, not the `Microsoft.WindowsAppSDK`
  umbrella (it bundles AI runtimes we don't use).
- **License:** Binder is GPL 3.0 with a section 7 permission to combine it with the Windows App SDK, .NET and
  WebView2 (`LICENSE`, `LICENSE-EXCEPTION.txt`). Every bundled component and its license is listed in
  `THIRD-PARTY-NOTICES.txt`; `publish.ps1` copies the upstream license and notice files into `app\licenses` and
  stops if one is missing. A new dependency must have a GPL-compatible license or be added to that permission,
  and gets its line in `THIRD-PARTY-NOTICES.txt`.
- The watcher stays dependency-free and tiny.

## Layout

```
src/Binder/            the WinUI widget
  Library/                sources (*Source.cs), SourceDefaults (where each source reads, user overrides),
                          InstallFolder (folder safety), Vdf, ShortcutFile/ShortcutImporter,
                          LaunchOptions, GameScan, GameFiles (installed or gone, removed games' settings), AntiCheat, PlaytimeStats, CrashAnalysis, SpacePlanner,
                          Moments ("Welcome back"), UpdateManifest (release signature and rules) (pure logic)
  Models/                 Game, Session, Settings, ImportedShortcut, CrashRecord
  Services/               Launcher, CoverCache, Storage, UpdateKeys (release signing keys), Updater (check, download,
                          start the setup), Windows integration (DesktopLayer, Monitors, Placement,
                          Autostart, ShellNotify, PcChecks, …), Gamepad (XInput, polled only while the panel is
                          active and a pad is connected), GameModeService, WindowsGaming (Windows' Game Mode,
                          OneDrive; read when the Game mode page opens), Chime/Sound
  ViewModels/             MVVM: ObservableObject, RelayCommand, one <Page>ViewModel + I<Page>Actions per page (Shelf,
                          Detail, Settings, Setup, FreeSpace, GameMode)
  Views/                  ShelfView, DetailView, SettingsView, SetupView, ImportView, FreeSpaceView, GameModeView,
                          GameModeToast, BannerLogo, Sheen (shiny favorites); Format.cs the text formatting,
                          GameAction.cs (menu actions, chart bars)
  Themes/Theme.xaml        shared styles and brushes
  Strings/                the app's text: Strings.resx (English), Strings.de.resx, Strings.fr.resx, one property per
                          entry in Strings.Entries.cs; Strings.cs has Format, Plural, List, LowerFirst
  MainWindow.xaml.cs      the panel: start, first frame, page navigation, rescan; partial files by area:
                          MainWindow.<Page>.cs (that page's actions), .Flyout (slide, placement), .Games (import,
                          add, game actions), .Input (Esc, controller, type-to-search), .Launch (play, countdown,
                          return), .Notices (toast, errors, crash card), .Updates (About's buttons, the "…" item,
                          the pill after an update)
  App.xaml.cs             the app's lifetime (panel, tray menu, countdown); App.Restart.cs, App.Updates.cs (when to
                          check, download and install an update; spec §25)
  Program.cs, MessageWindow.cs   single instance, tray icon, hotkey
src/Binder.Watcher/    windowless (one hidden window on its own thread, SystemEvents.cs: shutdown, sign-out and
                          sleep); Program.cs, GameMode.cs (apply/restore + journal), ElevatedClient.cs,
                          GameModeShared.cs (also compiled into the widget, helper and setup)
src/Binder.Elevated/   Game mode's admin helper (Native AOT, windowless, run by scheduled tasks)
src/Binder.Setup/      BinderSetup.exe: per-user installer/updater/uninstaller (docs/installer.md)
tests/Binder.Tests/    plain console unit tests (no test framework packages); smoke.ps1 / setup-test.ps1 are CI-only
scripts/                  publish.ps1 (builds publish\app: widget folder + AOT watcher + helper, and BinderSetup.exe),
                          check.ps1 (the hard rules on a diff, then build + tests; -Task for agent cards),
                          sign-release.ps1 (owner only: sign a release with the YubiKeys, spec §25),
                          make-tray-icons.ps1 / make-watcher-icon.ps1 (redraw icons after a logo change)
site/                     the website: HTML + CSS, nothing loaded from other servers; one script, release.js, asks
                          GitHub's API for the latest release (download link, version, VirusTotal box; the website
                          only: the app's network rules below don't change); index.html,
                          docs.html (what Binder changes on the PC: every Game mode item, autostart, updates, …)
docs/proposals.md         approved feature specs, progress table (§0), decisions log, "Not decided"
docs/tasks/, AGENTS.md    task cards and rules for cheaper coding agents
```

Data: `%APPDATA%\Binder\` (`settings.json`, `library.json`, `sessions-<year>.jsonl`, `imported-shortcuts.json`,
`crashes.json`, `removed-games.json` (settings of games that left the library), `Shortcuts\`; each JSON file with the
previous save as `.bak`, a damaged one kept as `.bad`: `Storage.Save` flushes, swaps and keeps the backup, `Load` falls
back to it). `%LOCALAPPDATA%\Binder\`: `covers\`, `error.log`, and Game mode's
`gamemode-journal.json` (only while applied), `gamemode.log`, `gamemode-last.txt`, `gamemode-request.txt`. The
helper: `%ProgramData%\Binder\` (`journal.json`, `elevated.log`, `allowed-<SID>.txt`, `result-<SID>.txt`).
The version lives in `Directory.Build.props`.

Sessions (`Binder.Watcher/SessionLog.cs`, shared with the widget): JSON Lines, one compact object per line, one
file per year by the session's end. A session over New Year stays one record in the year it ended; per-year
totals clip it at Jan 1. Readers load every year file and go by the timestamps, skip lines that don't parse, and
ignore unknown fields: new fields are optional, never renamed; a breaking change gets a new file name. The widget
moves an old `sessions.json` into the year files at start (kept as `sessions.json.bak`).

## Core flow: launch → hide → return

1. Play: `Launcher` starts the game via the launcher's URI/command, or the exe directly (GOG, manual, shortcut).
2. The in-panel launch toast: "Starting [Game]", a countdown (default 10 s, per-game override) and **Keep open**.
3. The widget starts `Binder.Watcher.exe` (game id, install folder, optional process name, timeout).
4. When the countdown ends the widget **exits** (unless Keep open).
5. The watcher:
   - Detection: any process whose exe is inside the install folder, or the explicit process name. Polling every
     ~2 s is allowed here only, for up to ~3 min. Never found: relaunch the widget with "Couldn't detect the game".
   - Found: **block** on the process handle; no polling while the game runs. After an exit, re-check the folder
     for ~3 s (games that restart themselves).
   - On exit: append the session as one line to `sessions-<year>.jsonl` (the year it ended, `SessionLog`; never
     reads the file), relaunch the widget if "Come back when the game closes" is on, or with `--crashed` after a crash exit code of a game with "Restart if it crashes" (spec §17: the widget's
     restart pill counts 5 s down, then plays it again; at most twice in a row).
   - Time the PC slept isn't playtime: the session is saved as its awake parts. Shutdown or sign-out during the
     game: the session is saved at once (`SystemEvents`), nothing is relaunched, Game mode's journal is put back
     at the next start.
   - Game mode (when on for the game): apply right after start (and tell a widget that is still up, never starting
     it for that), Above normal priority once found, restore after the game: with "Come back" on, the widget is
     started first and the result follows as a forwarded `--restored` once it's up (`WaitForInputIdle`); with it off,
     the restore comes first. A new Game mode session waits up to 30 s for a previous watcher still restoring. Admin items go through the helper's scheduled tasks (`Binder Apply/Restore <SID>`, waiting on
     `Local\Binder.Elevated.<id>`). Not detected: restore at once.
6. A game the watcher can't follow (no folder, no process name) starts without hiding the widget, with a toast
   pointing to "Process to watch".

Single instance: `Program.Main` holds a mutex before WinUI starts; a second start forwards its arguments to the
hidden `MessageWindow` (WM_COPYDATA). With no arguments that toggles the flyout.

## Code structure (MVVM)

- Each page (the shelf, Detail, Settings, Setup, Free up space, Game mode) has a view model: its state and logic go
  in `ViewModels/<Page>ViewModel.cs` (derives from `ObservableObject`; setters use `SetProperty` or report only real
  changes, so filling a page never counts as a user change). The view creates it in `Initialize(actions)` and binds
  with `x:Bind` (`Mode=OneWay`/`TwoWay`, `Command=`); its code-behind keeps only pure view work (focus, animation,
  `Disclosure`, menus, drawing rows, and on the shelf the cover grid's sizing, covers and keyboard/controller
  focus), redrawing code-built parts from the view model's `PropertyChanged`. Classes handed to WinUI (view models, commands) must be `partial`: under Native AOT
  CsWinRT only generates their WinRT interfaces then, and the panel crashes at the first binding otherwise. An
  `ItemsSource` from a view model is a plain array (`string[]`), never a collection expression typed as an interface
  (`IReadOnlyList<T> x = [..]` makes a hidden compiler type AOT can't pass to WinUI); bind a ComboBox's selection by
  item, not index, when its list changes with it.
- What a page asks its owner to do is an interface (`I<Page>Actions`), implemented by `MainWindow` with explicit
  interface members in the page's partial file (`MainWindow.<Page>.cs`), so a missing action is a compile error,
  not a dead button. What the page needs from Windows (drives, running apps, the helper) goes through it too.
- View models don't reference WinUI types, so `tests/Binder.Tests` compiles them and tests them with a spy for
  the actions interface (`ViewModelTests.cs`). Add checks there with each view model.
- `MainWindow` itself is split by area into partial files (see Layout); a new area gets its own file rather than
  growing `MainWindow.xaml.cs`.

## Library sources

Steam, Epic, GOG, EA app, Ubisoft Connect, Xbox app, Rockstar, Hytale, imported desktop shortcuts and manual adds;
each `*Source.cs` documents where it reads and how it launches. Rescan on startup, on manual refresh and when the
panel opens over an hour after the last scan, never on a timer. Uninstalled or deleted games leave the shelf (rescan,
or at once on Play/Details via `GameFiles`, never for an unplugged drive); their settings wait in `removed-games.json`.

- Steam: skip non-games (redistributables, Proton, SDKs, servers). When Steam isn't running, start
  `steam.exe -silent <link>` so its window doesn't open.
- Shortcut import: scan the user's and the Public Desktop; the user ticks which are games; chosen shortcuts are
  **moved** to `%APPDATA%\Binder\Shortcuts\` (reversible, "Restore to desktop"); Steam/Epic URIs merge with the
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
- Look and moments (`docs/proposals.md` §13): "Welcome back" takes the top slot once after a session of 2 min or
  longer, with a quiet playtime milestone line; shiny favorites (`Sheen`, a Settings toggle; still with animations
  off, none in high contrast or List). Details open and close without a cover animation (§13 b was removed).

Display modes: **Flyout** (default; tray icon and Win+Shift+G) or **On the desktop** (docked, kept at the bottom of
the z-order with `HWND_BOTTOM`, no WorkerW hack). **Stay ready** (default on): closing the flyout destroys the
window, releases covers, trims memory and enters efficiency mode ("Open faster" only hides it); off exits the
process. Autostart uses `--background`.

## Text and languages

English, German and French, picked by the Windows display language (English for any other). Numbers, dates and
times follow the user's regional format.

- **No user-visible text in code or XAML.** Every label, tooltip, UI Automation name, toast, menu item and file
  picker filter is an entry in `Strings/Strings.resx` with its German and French in `Strings.de.resx` and
  `Strings.fr.resx`, plus its line in `Strings.Entries.cs`. Code reads `Strings.Name`; XAML binds
  `{x:Bind s:Strings.Name}` (`xmlns:s="using:Binder"`), except on a page's root element and on per-item
  controls of the grid (GameTile, GameRow), which set theirs in code. The unit tests check that the three tables
  have the same entries and `{0}` placeholders and that every `Strings.X` in `src/` exists.
- **Every English entry has a `<comment>` for translators** (only in `Strings.resx`): where the text appears
  (button, card title, tooltip, toast, screen-reader name), what each `{0}` holds, and, where a word could be read
  two ways, its part of speech or a length limit. Name terms to copy exactly (Windows' own setting names, brand
  names). The unit tests fail on an entry without a comment or with a placeholder its comment doesn't name.
- Values go in through `{0}`, `{1}` (`Strings.Format`), never by joining pieces: word order differs. Counts use
  `Strings.Plural(n, one, other)` (French counts 0 as one); lists `Strings.List` ("A, B and C"). Capitalize in the
  entry itself; `Strings.LowerFirst` only for text that starts a sentence in one place and sits inside one in
  another (never `ToLower`: German nouns stay capitalized).
- Not translated: brand and product names (Steam, Epic Games, EA app, Binder), log files, the data files'
  contents, and the setup's disclaimer (English only, as in the README). The watcher and the helper record Game
  mode items by English name (journal, result, log); the widget translates them (`Views/GameModeText.cs`).
- `BinderSetup.exe` keeps its few strings in `Text.cs` (invariant globalization, no resource files).
- The CI tests (`tests/smoke.ps1`, `setup-test.ps1`) find controls by their English names: renaming an English
  label means updating them.
- **Wording:** short and plain, in the words Windows uses. Say what something does or what happened, not why it's
  good; no selling, no "seamless", no exclamation marks, no "we". A card's second line is state ("3 imported"),
  a tooltip is one short line. German uses "Sie", like Windows; French "vous". Keep the three languages saying the
  same thing.

Errors: unexpected UI errors go to `%LOCALAPPDATA%\Binder\error.log` (1 MB rotation) and a toast; a burst
(more than 5 in 10 s) isn't caught, so a broken state ends instead of spinning.

## Security rules

The widget and watcher run as the normal user and never ask for admin rights. The one exception (owner, spec §11):
Game mode's "Allow…" starts `Binder.Elevated.exe` once with a UAC prompt. Untrusted input: every launcher's
manifests, config files and registry entries (library folders can be on drives others can write to), desktop
shortcuts, downloaded images, and the places typed in Settings › Advanced.

- **Validate before use.** Store ids match their shape (Steam appid, GOG id: digits; Epic ids: letters, digits,
  `-`, `_`, `.`). Steam install folders: one folder name under `steamapps\common`. Every other install folder goes
  through `InstallFolder.Validate` (full, existing, not a drive root, not inside Windows, not a shared folder like
  Program Files, the profile or Desktop). A GOG exe must be inside its install folder. Reject otherwise.
- **Source locations (Settings › Advanced, spec §14).** Every source and `CoverCache` read their folders, registry
  keys, Steam art file names and download address through `SourceDefaults`, which checks the user's values again on
  every load (`SourceDefaults.Validate`): folders full, local and existing; registry keys only under
  `HKLM\SOFTWARE\` or `HKCU\Software\`, read-only; file names one `.jpg`/`.jpeg`/`.png` name; the address
  `https://` with `{appid}`. Invalid or unknown values fall back to the defaults; what's read from the new place
  goes through the same checks as from the default one.
- **Bounded parsing.** Parsers cap file size and nesting depth and fail with `FormatException`, never a crash.
  Downloads have a size cap.
- **Launching.** Only what came from a validated library entry or that the user picked. Store URIs from validated
  ids only. Arguments via `ProcessStartInfo.ArgumentList`, or quoted by the Windows argv rules.
- **Native code.** Only Windows system DLLs, from System32 (`DefaultDllImportSearchPaths(System32)` in every
  assembly; the setup and helper also link with `/DEPENDENTLOADFLAG:0x800`, the setup calls
  `SetDefaultDllDirectories`).
- **Shortcuts.** `.lnk` via `IShellLinkW` (load only, never `Resolve`), `.url` as plain INI. Never execute a
  shortcut while scanning. Where the shared desktop can't be changed without admin, copy and leave the original.
  "Restore to desktop" moves only a `.lnk`/`.url` directly in our `Shortcuts\` folder, and only onto the user's or
  the Public desktop (`ShortcutImporter.StoredShortcut` / `RestoreTarget`), whatever `imported-shortcuts.json` says.
  An import saves each shortcut's record before moving it (`ShortcutImporter.Store`), and a restore also puts back
  shortcuts in our folder that no record names (`Unrecorded`), except a manually added game's own copy.
- **Files.** Only delete inside our own AppData folders; never follow paths from data files to delete or overwrite
  anything. Never delete the user's files or shortcuts; moves stay reversible. The one exception is the
  uninstaller, when the user chooses both "Don't put them back" and "Also delete my game list": then the imported
  shortcuts go with the data, as its dialog says. Shortcuts that couldn't go back are never deleted. Never delete game files: Free up
  space hands each uninstall to the game's own launcher (or Settings › Apps).
- **Network.** HTTPS to the store CDNs only, for artwork, or to an HTTPS address the user typed themselves in
  Settings › Advanced (the page names its host). Downloads are size-capped and only ever decoded as images. And
  HTTPS GETs to `github.com` (and its release download hosts) for this repository's release manifest and setup,
  signed with the owner's key; never anything about the user or the PC. No telemetry, no other calls.
- **Updates** (spec §25, `Services/Updater`, `Library/UpdateManifest`). The signature is checked on the raw bytes
  before anything parses them; the keys are compiled in (`Services/UpdateKeys.cs`, owner only); never a version at
  or below the installed one, never a manifest issued before the last one accepted; redirects only through
  `UpdateManifest.IsAllowedAddress`; the setup's size and SHA-256 checked while it streams and again through a handle
  held without write or delete sharing until it has started; never elevates; never installs while a watcher runs or
  a Game mode journal exists; "Don't check" means no request at all. Deletes only inside
  `%LOCALAPPDATA%\Binder\updates\`, never through links.
- **Installer.** `asInvoker`, fixed paths from known folders (never read back from the registry), deletes only
  files it wrote and never through links, hashes each file after writing, ends only processes from its own
  install folder. Uninstall puts back Game mode's changes first (`--restore --quiet`). Details: `docs/installer.md`.
- **Game mode, no-admin part.** Every change is journaled (flushed) before it's made and put back only if it's still
  the value we set. Apps are closed only with WM_CLOSE to visible windows, never ended; only full paths to an
  `.exe` outside Windows, Store apps and our own folder are closed or reopened (checked in the widget, the watcher
  and when reading the journal); only the journaled exe is reopened, without arguments. Priority: Above normal at
  most, never lowered, never High/Realtime.
- **Game mode, admin part (the helper).**
  - Lives only in `%ProgramFiles%\Binder\` (a hashed copy made by "Allow…", which holds its own exe open without
    delete sharing so the copy is the approved file); never runs elevated from a user-writable folder except that
    one `--install`. Its tasks run that path as the user's SID, highest privileges, only while signed in; the user
    may run them, not change them.
  - Trusts nothing from the user's side. The request file is size-capped (on bytes actually read), never a link,
    parsed strictly (`ElevatedRequest.Parse`): services only from the compiled-in allowlist (WSearch, SysMain,
    DiagTrack, Spooler); app names only plain exe file names, never system or security processes
    (`ElevatedPart.IsSystemApp`); uploads are never limited for a Windows service's program, a protected process,
    another account's process or an antivirus Windows Security lists (`ProtectedApps`, checked at each apply); upload limit only 1, 5 or 20; ids only 32 hex digits. Paths from known folders
    only. One run at a time via a file lock in its admin-only folder (not a named mutex).
  - Its folder `%ProgramData%\Binder\` has a protected ACL (SYSTEM and Administrators write, Users read); an
    existing folder counts as ours only if owned by Administrators/SYSTEM and not a link, else "Allow…" moves it
    aside and the helper refuses to work.
  - Services are only stopped (never their start type), only when running, stoppable and without running
    dependents, and restarted only if we stopped them and they weren't disabled meanwhile. QoS policies go into the
    ActiveStore, named `Binder …`, and only those are removed. The Delivery Optimization value is restored
    exactly (including "not set" and its key if we created it).
  - Removal ("Remove it", uninstaller) runs the installed copy's `--uninstall`: restores what's still applied,
    deletes this user's tasks, marker and result, and with the last user the ProgramData folder and itself.

## Working rules

- What to build next: `docs/proposals.md` §0 progress table, then the section it names. Items under "Not decided"
  there must not be built. Keep that table and this brief current when something changes.
- `site/docs.html` must stay true. A change to what it describes (Game mode's items, the helper, Start with
  Windows, updates, shortcut import, uninstall) updates it in the same commit. The unit tests (`DocsTests.cs`)
  compare its Game mode cards with `GameModeInfo`/`ElevatedPart` (items, order, defaults, admin, services, apps,
  limits); `check.ps1` fails when a documented file changes without `docs.html`. When every sentence still
  holds, pass `-DocsUnchanged`, or (for CI) add a `Docs-Unchanged: <why>` line to one of the branch's commits.
- After each change run `./scripts/check.ps1` (or `dotnet build` + the unit tests). This repo builds on Windows only;
  on Linux only the unit tests run. Test new behaviour on the real PC's launchers where possible.
- Commit at each milestone with a short message.
- Routine work can go to cheaper agents as task cards (`docs/tasks/README.md`, `AGENTS.md`): one goal per card,
  exact files, insertion points and expected values checked against the code, and a `./scripts/check.ps1 -Task` line under
  "Done when". Review agent branches before merging and set the card to `done`. Keep `AGENTS.md`'s "Don't touch"
  list and `check.ps1`'s protected list in sync.
