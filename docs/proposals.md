# Planned features (approved by the owner)

Status: **approved specs**; what's built is in the progress table in §0.
- **Designs:** on the design canvas, https://claude.ai/artifact/9tZEP6HSrETxwGwpSbMg1Q. Pages by area ("Shelf",
  "Game detail", "Window, setup & settings"), plus "Still to build" with the open milestones in build order. Board
  numbers (1–17) are given in each section below and stay in the board titles, with "built" or the milestone.
- **Baseline:** first written against the code at `0eacc65` (milestones 1–6 done); the progress table below is
  what's current.

Every item follows the project brief: zero cost while idle or gaming, no third-party packages beyond the ones
`CLAUDE.md` approves (WinUI, and Win2D for the Game mode toast only), nothing that can break the user's system.

---

## 0. Read this first (for the session that implements this)

### Progress (keep this table current)

| Milestone | Sections | Status |
|---|---|---|
| 8 Quick wins | §1 app icon, §2 fallback covers, §3 launcher icons, §7 sort / Favorites chip / cover size | Done |
| 9 Window behavior | §5 flyout, tray, shortcut, stay ready; §6 first-run setup | Done |
| 10 Detail additions | §7 "This week", §9 playtime chart, launch options, anti-cheat | Done. "This week" was later merged with "Continue playing" into one switchable slot (board 16, see `CLAUDE.md`) |
| 11 Crash detection | §10 | Done. With "Come back" off the crash card shows the next time the panel opens; memory is the installed amount (`GetPhysicallyInstalledSystemMemory`) |
| 12 More launchers | §8 | **In progress.** Done: EA app, Ubisoft Connect, Xbox app (tile logo as the cover picture) and Rockstar (tested with synthetic data only, no Rockstar game installed). Not yet: Battle.net, Riot, Amazon, itch, Humble and Meta |
| 13 Controller | §4 | Built (`Services/Gamepad`, XInput; the hint bar, LB/RB pills and bold focus ring of board 3, without its "Controller" header pill). B closes menus and dialogs too; A on other controls goes through their UI Automation pattern; Menu toggles Settings. Tried briefly with a real pad on the owner's PC; still to test more thoroughly. `Windows.Gaming.Input` (PlayStation, Switch) not yet |
| 14 Game mode | §11 | **In progress.** Done, the no-admin part: power mode (overlay, else the High performance plan), Above normal priority, Game Bar background recording, closing chosen apps (WM_CLOSE to visible windows, ~5 s, else left open), the write-ahead journal with recovery on the widget's start and "Restore now", the Game mode toast ("Game mode is on" after applying, while the widget is still up, and "Windows is back to normal" after the restore; since 2026-11-16 the changes melt in, `Views/ToastMelt` with Win2D, board 33) and the Settings page with the per-game Default / On / Off. The watcher applies and restores (the widget is gone while the game runs); journal, log and last result are in `%LOCALAPPDATA%\Binder\`. Tested on the real PC only where it was already at High performance with Game Bar recording off (left as is), so the switch-and-restore path is covered by unit tests, not yet live. Built, the admin part: `Binder.Elevated.exe` (Native AOT), installed to Program Files by Settings › Game mode › Allow… (one UAC prompt; admin accounts only) with per-user tasks `Binder Apply/Restore <SID>`; the four allowlisted services; QoS through WMI over COM in the ActiveStore (DSCP 46 for the game, found voice chat apps and user-added ones; upload throttle 1/5/20 Mbit/s); Windows Update limited to 10% for background downloads (`DOPercentageMaxBackgroundBandwidth`, restored exactly); journal and log in an admin-only `%ProgramData%\Binder\`; "Update…" when the helper's version differs; "Remove it" and removal on uninstall; security rules in `CLAUDE.md`. Game Bar's row moved to the services section. Unit-tested (request and journal parsing). Tested live on the owner's PC on 2026-11-07: the tasks register and run, the Print Spooler is paused and restarted, the QoS policies and the Windows Update limit are applied and restored. Windows keeps ActiveStore policy names lowercased, so ours are matched ignoring case. The shelf shows "Game mode needs an update" with Update… when Game mode uses the helper and it's from another version. Still to check: Delivery Optimization picks up the policy without a restart. Also built: §20 (board 24), the "Windows' own settings" card, Pause OneDrive syncing and Close / Calm per app (efficiency mode) |
| 15 Free up space | §12 | Done (built ahead of 13 and 14, owner's choice). Steam's `LastPlayed` verified on the real PC (Unix seconds, 0 = never). Mid-uninstall the plan survives the flyout closing (memory only; with "Stay ready" off it's lost). Games without a known size (Xbox, GOG, some Ubisoft) are listed under the map, never suggested |
| 16 Look and moments | §13 | **In progress.** Done: a, the logo on banners (`ArtKind.Logo`, `BannerLogo`; Steam keeps `librarycache\{appid}\logo.png`, 188 of 400 games on the owner's PC, the rest downloaded once; shadow from the logo's alpha via `Image.GetAlphaMask`; in Detail it sits right of the cover, which overlaps the banner's bottom left). Built: b–e, to try on the real PC.<br>b (the cover growing into details) was built and then removed on 2026-11-09, owner's choice: it looked bad.<br>c and e are `Library/Moments`, picked each time the panel opens; tested live with a session written into the sessions file. With a crash, the crash card still takes the slot.<br>d is `Views/Sheen`, plain alpha layers tuned below the prototype's "Medium", since blend modes would need Win2D (approved for the Game mode toast only). The rest look was checked on screen; following the pointer couldn't be tested here, because injected mouse input didn't reach the panel |
| 17 Advanced: sources and artwork | §14 | Built: `Library/SourceDefaults` (every default, `Validate` per kind, `Use` at start and after a change; invalid or unknown values fall back), read by the Steam, Epic, GOG, Ubisoft and Rockstar sources and `CoverCache` (cover names tried in order, the first downloaded; `ForgetSteamLookups` after an artwork change drops resolved paths and `.none` markers). Settings › Advanced: one card at the bottom (closed), per launcher a Check (`Services/SourceCheck`, no downloads) and a text box per entry with Reset and its error, Reset all, the host named when the address differs. Values take effect from the next scan. Unit-tested (each kind's validation, the view model); network and validation rules in `CLAUDE.md`. Still to try on the real PC: the page on screen, Check per launcher |
| 18 Performance | §15 | To do |
| 19 Quick actions | §16 | Done (header menu, `Ctrl+G` and `F5`, tray items, Back to the shelf from Game mode and Free up space); tried on the real PC |
| 20 Restart after a crash | §17 | Built: the Detail toggle, `CrashAnalysis.ShouldRestart` (in a row from `crashes.json`, at most 2), the watcher's `--restart` / `--crashed`, the restart pill (Game mode's pill with a 5 s countdown, bar and Cancel, owner's change) and the crash card's "Restarted twice already" line; unit-tested. To try on the real PC with a game that crashes (or a test exe that exits with 0xC0000005 after a minute) |
| 21 Softer sound and hover | §19 Lullaby sound, Media tile hover | Built (boards 25, 26); to try on the real PC (press, keyboard, controller, animations off, high contrast) |
| 22 Open a launcher | §21 | Done; the owner tried it on the real PC on 2026-11-11 and it works. `GameActions.OpenLauncher` / `LauncherTarget` (Free up space's launcher step uses it too), names in `Format.LauncherAppName`, the tray group (`ShelfViewModel.LaunchersByGames`, chip order) and the launcher chips' right-click menu with the launcher's icon; unit-tested. On the owner's PC every launcher with games resolves (Steam, Epic, GOG Galaxy, EA app, Ubisoft Connect, Xbox app, Hytale); Steam, already running, comes to the front without a second copy |
| 23 Faster than Start | §22 | Built 2026-11-17, all five parts; to try on the real PC. a: `Library/GameSearch` (prefix, word start, initials with whole numbers, anywhere; recent first within a rank), Enter in the search box plays the top match, an "Enter" key on its tile or row, the box's help text. b: with nothing typed Enter plays the Continue playing / Welcome back game, and the placeholder says so ("Search 72 games · Enter plays Hades II"). c: `--play <id>` (`App.Play`: plays without opening the panel; a game that's gone, the anti-cheat question or an error open it), "Add my games to Start search" (Settings › Opening and setup step 1, off by default; `Services/StartMenuGames` writes `Programs\Binder games\` after each rescan, only when the stamp in settings changed; off and uninstall delete it). d: setup step 1's "N of them aren't in Start search" (`Library/StartMenuMatch` over the Start menu's shortcuts, read once per run), the import toast "Your desktop has N fewer icons" with Undo for that import, Welcome back's one-time "Binder was closed the whole time you played" (only when the game's end started the process). e: the cover's Play tooltip. Unit-tested (ranking, initials, matching, plan, file names, view models).<br>Tried on the owner's PC 2026-11-17 (published AOT build): search by initials, the Enter key (tile, list row), Enter plays the top match and the Continue playing game, Down into the grid, Esc clears, nothing with This week; the folder written with the games' icons and found by Start search; `--play` from Start with Binder running and closed (no panel, the watcher brings it back), an unknown id opens the panel; off deletes the folder; step 1's count matches the folder; the import toast and Undo of only that import; the closed-time line once (not on the next Welcome back); the Play tooltips. Fixed then: imported shortcuts matched by name (a launcher's own Start entry was duplicated), their own icons, the folder follows imports/Undo/Add/Hide at once, the Return symbol on narrow covers, list rows trim long names, a shorter German closed-time line. Not tried: German/French on screen (no per-app language switch; lengths checked), high contrast and Animation effects off (Windows settings), the uninstaller (CI's setup test checks the folder) |
| 24 Lean speed-ups | §23 | To do |
| 25 Weekly chart details | §24 | Proposed (2026-11-12), waiting for the owner's go; two choices open (§24 "To decide") |
| 26 Updates from GitHub | §25 | **Built and released** (owner's go 2026-11-14). Done: `Library/UpdateManifest` (signature on the raw bytes first, then the strict rules; urgent only with two keys; downgrade, replay and clock checks; GitHub-only addresses; the download's size and hash check), unit-tested with generated P-384 keys; the keys (both YubiKeys' since 2026-11-15, made on the keys with PIN and touch always) and the repository set at build time since 2026-11-15: `build/OfficialRelease.props` for `publish.ps1 -Official` (release.yml), `build/Updates.targets` checks them and generates `Services.UpdateKeys`; other builds and forks without their own keys never check (About says so); `scripts/sign-release.ps1` (`-ExportPublicKey`, sign, `-Urgent`, `-DryRun`), its manifest and signature checked against the app's verifier; notes format in `docs/releases/README.md`. Default decided: Ask before installing (2026-11-14).<br>Built 2026-11-14: `.github/workflows/release.yml` (v* tags only, actions pinned, a draft with the setup and its attestation); `Services/Updater` (check with redirects only through `IsAllowedAddress`, 15 s, a client per check, no cookies; manifest kept in `%LOCALAPPDATA%\Binder\updates\` and verified again when read; the download hashed while it streams; the setup hashed again through a handle held with read sharing only and started with `/S /update`; waits for a watcher or a Game mode journal); `App.Updates` (check after a panel's first frame at most once a day, download at once with "Install automatically" or urgent, closing the panel cancels it, install when the flyout closes or at a `--background` start, the in-panel toast "Updated to …" with What's new after `--updated`, "couldn't be installed" when `--updated` comes back on the old version); Settings › About (version card with Check for updates / Update / Restart now, progress bar, What's new with Skip this version, the Updates combo); the "…" dot and first item, the tray item; three settings fields plus the mode; 37 strings in EN/DE/FR; view model tests. The pill is the panel's toast, not board 23's separate pill window. Not built: the Authenticode same-publisher check (Binder is unsigned today).<br>Built 2026-11-15: the setup's `/update` (the widget comes back with `--updated --background` after a success, a failure or a busy setup) and the all-or-nothing swap (§25 "To decide" 3, owner: yes; `FileSwap`, unit-tested on real files), run in CI by `setup-test.ps1`.<br>Released: Windows sees both YubiKeys' certificates and `sign-release.ps1` asks which one signs; after a dry run, 0.2.0 and 0.2.1 were published on 2026-11-15 through `release.yml` and `sign-release.ps1`. Since 2026-11-16 `virustotal.yml` scans each published setup and adds the result to the release notes. An installed copy updated itself through the app on the owner's PC. Not built: the Authenticode same-publisher check (Binder is unsigned today) |
| 27 Game shortcuts | §26 | Built 2026-11-18 (boards 35 A, 36, 37). Tried on the real PC 2026-11-18 (Debug build, keys and mouse sent with SendInput, one 4K screen with an auto-hide taskbar): Win+Shift, G, a number on the number row shows the flash, the panel stays shut, nothing on the taskbar starts; a quick tap opens the panel at once, holding opens it after 1 s; Esc cancels ("Cancelled · Balatro didn't start", gone after about 1 s) and Esc and letters reach apps normally afterwards; the same number again plays at once (Balatro started, Binder exited, only the watcher stayed, the widget came back when it closed); another number switches; "Nothing on 4"; the switch off lets the number through and opens the panel; dragging a cover or a list row raises the numbers, the line follows the slot under the pointer ("replaces Balatro", "already there"), a drop gives the number and the toast names the game that lost it, 0 refuses, releasing elsewhere removes the row; efficiency mode back on after a flash and a cancel. Fixed: the number pad (Windows lifts Shift around a pad key while Shift is held, which ended the wait, and may hand it over as Insert, End, arrows); a game without store art showed only its name (now its generated cover); the drag's caption repeated the line above the numbers. Added (owner, 2026-11-18): held past 1 s the shortcut lists what each number plays, letting go then opens the panel; and the debug panel's "Game shortcuts" buttons. Not tried: the menu's, Game detail's and Settings' number pickers by hand (their view models are unit-tested), a real keyboard (only sent keys: the Start menu on letting go of Win and the number pad with NumLock need a person), light mode, high contrast, Animation effects off, two screens, Narrator (Windows settings Claude doesn't change), the published AOT build |
| 29 End a stuck game | §28 | Built 2026-11-19 (board 40); to try on the real PC. The watcher's `EndHold` (shortcut, keys, checks, ending) and `RingWindow` (GDI+ layered window), the session's `EndedByUser`/`Hung`/`ErrorReport`, `GameEnding.EndedByUser` and the card's three wordings, Settings › When a game starts › End a stuck game, `WatcherKey` (the widget takes its shortcut back). Unit-tested (arguments, texts, the card's wording, restart, the setting). Not tried: everything on Windows (no Windows here): the ring on 100/150/200 % and two monitors, light/dark/high contrast, Animation effects off, exclusive fullscreen, a game running as administrator (does `GetAsyncKeyState` still see the keys?), a real hang and a real crash with WerFault |
| 28 Native ARM64 build | §27 | Proposed (2026-11-17), waiting for the owner's go; choices open (§27 "To decide") |
| Calmer pages | §18 Settings, Game detail, Game mode pill and sound | Built (boards 19, 21, 23); to try on the real PC |
| Languages | English, German, French (`CLAUDE.md`, "Text and languages") | Built: all text in `Strings/*.resx` and the setup's `Text.cs`, wording shortened throughout; unit-tested on Linux, to try on the real PC in German and French (Windows display language) |
| Setup: shortcuts into the logo | §6 step 2 | Built 2026-11-16 (`Views/LogoMerge`, as on the website): after Move, the moved shortcuts' rows turn into pills and fly into the logo's covers, the rest of the page fades, then step 3. At most seven fly, ~1.5 s on the compositor, no timer; skipped with Animation effects off or in high contrast. A second Move while it runs does nothing (unit test). To try on the real PC |
| Setup: Game mode step | §6 step 4 | Built 2026-11-17: step 4 with Off (the default) / without admin rights / All items (the helper's UAC prompt from setup), the list of changes with shields, "What the admin helper does", "change this later". View model tests.<br>Tried on the owner's PC 2026-11-17: opens on Off with Game mode off; Done with Off leaves `GameModeEnabled` false; All items with the helper installed finishes without a prompt. Fixed then: "Only items that don't need administrator rights" now drops the admin items (an allowed helper ran them), All items puts them back, the step reopens on that choice; card padding and the helper card's width. Not tried: the UAC prompt approved or declined (needs the helper removed and a person at the prompt), a standard account |
| Debug panel | `CLAUDE.md`, "Working rules" | Built 2026-11-16: `Binder.exe --dev` in Debug builds shows every toast, card and pill through the real code with made-up data (`src/Binder/Dev`, left out of Release; `publish.ps1` stops if its marker ships). CI builds Debug too |
| Releases and website | §25 "Making a release" | 0.2.0 and 0.2.1 published (2026-11-15). The website (`site/`, GitHub Pages) follows the latest release through GitHub's API (owner, 2026-11-16, website only) and shows its VirusTotal result |

The build order is at the end of this file. The ideas under "Not decided" at the bottom must **not** be built.

---

## Decisions log

**2026-11-01**
- Logo **B** (cover stack).
- Game mode may use admin rights:
  - One-time UAC prompt.
  - Helper in `%ProgramFiles%\Binder\`.
  - A real installer is therefore needed.
- Power mode via the undocumented overlay API is OK.

**2026-11-02**
- **Features:** sort options, favorites chip, as many launchers as possible, launch arguments with engine-based
  suggestions, summary chart, global shortcut, all polish items (cover size / list view, hide with undo plus
  hidden games, first-run setup), slide-in flyout.
- **Launcher icons:** every launcher shows its logo instead of text on covers. Manually added games and shortcuts
  get **no badge**.
- **Opening:**
  - The notification-area (tray) icon is **on** by default.
  - The taskbar pin is **not** set up by default; setup only mentions it as an option.
  - Flyout is the default mode.
- **Shortcut:** **Win+Shift+G**, falling back to Win+Shift+J if it's taken.
- **Stay ready in the background:** **on**, with every footprint technique in §5.
- **Anti-cheat requirements:** a **built-in** list, no online list.
- **Network defaults** (all on):
  - "Game and voice chat first".
  - "Slow down background uploads" at **5 Mbit/s**.
  - "Limit Windows Update downloads".
- **No** check for whole-PC crashes.

**2026-11-03 – 11-04** (already built and in `CLAUDE.md`)
- Native Windows 11 radii: 8 panel (system-drawn), 8 cards and covers, 4 controls.
- Clicking a cover plays; details open from an (i) button on hover, Space or right-click.
- Shelf header is a time-of-day greeting; "Continue playing" (hero banner) and "This week" share one switchable slot.
- Steam starts with `-silent` when it isn't running.
- Scope rule: a launcher, not a game toolbox. Engine versions and shader-cache tools were dropped under it
  (engine versions came back on 2026-11-11, see there).

**2026-11-04**
- **Free up space** approved (§12):
  - The user enters the new game's total size; the planner picks the drive and the games.
  - Order: least played first, the closest size fit within similar playtime, then games played in the last 14 days,
    favorites last of all.
  - Shown as a treemap with the games' icons; tiles can be added or removed by clicking.
  - Uninstall goes through each launcher. Where there's no uninstall link, opening the launcher is fine.
  - Reached from Settings and from the Detail view's "…" menu.

**2026-11-05**
- **Look and moments** approved (§13): the game's logo on banners, the cover growing into the detail view, a
  "Welcome back" banner after a session, shiny sleeves for favourites, and quiet playtime milestones. Not chosen:
  the panel taking the game's colour.
- **Advanced settings** approved (§14): users can change where Binder looks for games and artwork, in case a
  launcher changes its files and Binder hasn't been updated yet.
- **Performance** approved (§15): update WinUI to the newest patch, keep pre-scaled copies of covers, load hidden
  parts of the UI only when they're needed, try the AOT compiler's optimization settings, and move to CsWinRT 3.0
  once it's stable. Ruled out: ReadyToRun (Native AOT already does more), server GC (more memory), Win2D and
  DirectStorage (extra dependencies for little gain; Win2D came in on 2026-11-16 for the Game mode toast only), and newer CPU instruction sets (would crash on older PCs).

**2026-11-07**
- **Quick actions** approved (§16, board 18): a "…" menu in the shelf header with Game mode on/off, Game mode
  settings, Free up space, Add a game and Refresh, and Game mode and Free up space in the tray menu. Not chosen: a
  row of action chips, a Game mode switch in the header.
- The shelf shows "Game mode needs an update" with Update… when Game mode uses the admin helper and the helper is
  from another helper version (its own number, bumped only when the helper changes, not every app release).
- A successful Game mode restore plays the Windows notification sound (its own setting).

**2026-11-08**
- **Restart after a crash** approved (§17): a per-game option that starts the game again after a crash, with a
  cancellable countdown, at most twice in a row.
- **Calmer Settings and Game detail** approved (§18): boards 19 and 21, one card per setting under group headings,
  with a little more spacing than first drawn. Not chosen: a Settings home page with sub-pages (board 20), tabs on
  the detail page (board 22). Store page and Hide from shelf stay as links at the bottom.
- **Game mode toast:** design B, the one-line pill; the failure case is a larger card with room for the names.
- **Restore sound:** sound 1 "Open fourths", made by Binder in memory, replacing the Windows notification sound.
- **Restore sound, replaced (§19 a, board 25):** "Lullaby": two soft notes falling a major third over a low D, 2 s,
  quiet (−34 LUFS, −40 between 22:00 and 7:00), silent when Windows' notification sound is off. Rejected along the
  way: struck tones ("too much like a notification"), long swells, bright ones ("way too bright"), rising ones
  (didn't fit "back to normal") and falling dark ones ("like horror sounds").
- **Cover hover (§19 b, board 26):** option C "Media tile": eased lift to 1.04 with a shadow, a fine light edge, a
  bottom scrim and a round Play button; no dark veil or accent outline.

**2026-11-09**
- **Open a launcher** approved (§21): from a launcher filter chip's right-click menu and as a group in the tray
  menu. Not chosen: a launcher row on the shelf, deep links into launchers.
- **Faster than Start, and showing why** approved (§22), all five parts: Enter plays the top match with smarter
  matching (initials), Enter plays the last game on an empty search, optional Start search entries that launch
  through Binder (`--play`), the user's own numbers in setup / import / Welcome back, and a Play tooltip that
  says what's different. This replaces "Start menu search integration" under "Not decided". Not chosen: opening
  on controller pick-up, a "you saved" counter.
- **Lean speed-ups** approved (§23), all six: hide when the game appears (the countdown becomes only the limit, and
  the 5 / 10 / 30 s choices go), the last three games in the tray menu (replaces the jump list idea under "Not
  decided"), open on the screen with the mouse, "Needs an update" on Steam covers, "New" on recently installed games,
  and removing the footer hint. Rule: a feature that explains itself removes the help text it replaces.

**2026-11-10**
- The app speaks English, German and French, following the Windows display language. All text moved to resource
  files; the wording was made shorter and plainer (no selling the app inside the app).
- Bug report and AI prompt are in the display language too, since they are pasted into the user's own forum or
  assistant. The setup's disclaimer stays English.

**2026-11-11**
- Engine versions (owner, reverses the 11-03 drop): the engine tag in Detail shows Unity's and Unreal's version
  ("Unreal Engine 5.4", "Unity 6") when it can be read, and every engine tag gets an icon that suits the engine's
  name (not its logo). See "Engine detection" in §9.
- Shelf header (§16 a): Settings moved into the "…" menu as its last item (`Ctrl+,`), no separate gear. Game mode
  shows as a checked toggle while it's on, instead of a dot on "…" that looked like a notification.
- Sessions are stored as one JSON Lines file per year (`sessions-2026.jsonl`), by the year each session ended.
  The watcher appends one line instead of reading and rewriting the whole history, so its cost and memory stay
  the same after years of play; past years never change. JSON stays (readable, no package); a binary format or
  SQLite would add a dependency for little gain at a few hundred KB a year. A session over New Year stays one
  record; a future "year in review" clips it at Jan 1. Not yet: a cache of finished years' totals, only once
  loading all years measures slow.

**2026-11-12**
- **Uninstalled or deleted games leave the shelf**, and nothing is ever deleted from the game's side:
  - Play and Details check the game first (one attribute read): gone means removed at once, with the toast
    "[Game] isn't installed anymore".
  - A rescan drops a store game its launcher no longer lists (as before), a Steam game whose folder was deleted by
    hand (Steam keeps its manifest), and a manual or shortcut game whose program is gone. Never on an unplugged
    drive or an offline share: "gone" needs the drive to be there (`GameFiles.Check`).
  - The panel rescans when it opens more than an hour after the last scan (with Stay ready the process can live for
    days); still never on a timer.
  - A dropped game's settings (favorite, launch options, Game mode, custom cover, …) are kept in
    `removed-games.json` (only games with settings, the newest 200) and come back when it's installed or added
    again. Its cached art is deleted; sessions are kept as always. Not shown anywhere ("Showing uninstalled games"
    stays undecided).
- **Upload limit and security software:** the helper never limits uploads of a Windows service's program, a
  protected process, a program running as another account, or an antivirus Windows Security lists
  (`ProtectedApps`, checked at each apply; logged, not shown as a failure). Third-party antivirus and EDR agents
  are covered without a list of their names. Helper version 3, so "Update…" is offered.
- **Faster return after a game** (owner): the watcher's check for games that restart themselves is 3 s (was 5 s),
  and with "Come back" on the widget starts before Game mode is put back; the "back to normal" toast follows when
  the restore is done. With "Come back" off the old order stays (that widget only starts for a toast or a restart).
- **Robustness round** (owner: "fix them all"), from a review of races and crashes:
  - A library scan works on a copy and is reconciled with what changed meanwhile (`LibraryScanner.Reconcile`):
    games added or removed during a scan stay that way; one scan at a time app-wide; Refresh never stays stuck.
  - Data files are flushed before they're swapped in, the previous save kept as `.bak` and used when a file is
    damaged (kept as `.bad`), so a power loss never turns into an empty library or default settings.
  - Shortcut import saves each record before moving the file; restore also puts back unrecorded shortcuts.
  - The same game's Play within 5 s is ignored (`RepeatGuard`): no double start, no session counted twice.
  - Game mode: another Windows account's admin changes never look pending here (no "0 changes put back" toast);
    an app that closes after the 5 s wait is still reopened after the game.
  - A start while the running copy exits takes over instead of getting lost; one setup at a time (exit code 6);
    a failed update starts the widget again.
  - The watcher hears shutdown, sign-out and sleep (`SystemEvents`, one hidden window on its own thread): the
    session is saved before Windows ends it, and time asleep isn't counted (the session is saved in its awake parts).
  - Covers that couldn't be downloaded (offline) are tried again when the panel next opens, one timeout at most per
    open; a cover lookup can't bring back an old cover after a custom one is set; another game ending doesn't
    cancel the countdown.
- **Uninstall removes everything Binder put on the PC** (owner): the cache (`%LOCALAPPDATA%\Binder`) always,
  setup's log after a completed uninstall, the data with "Also delete…". New choice when there are imported
  shortcuts: **Put my shortcuts back on the desktop** (default) or **Don't put them back** (they stay with the data,
  or go with it); `/keepshortcuts` for a silent uninstall. Shortcuts that couldn't go back are never deleted.

**2026-11-13**
- **Renamed to Binder everywhere** (owner): the name, projects, namespaces and exes (`Binder.exe`,
  `Binder.Watcher.exe`, `Binder.Elevated.exe`, `BinderSetup.exe`), data folders, scheduled tasks, registry entries
  and mutexes. No migration from Game Shelf: it was never released, so an old install is uninstalled first.

**2026-11-14**
- **Updates from GitHub** approved (§25): signed manifest, the owner's two YubiKeys, the app checks at most once a
  day when the panel opens. Default: **Ask before installing**.

**2026-11-15**
- Update keys and repository are set at build time (`build/Updates.targets`, `build/OfficialRelease.props`); only
  official releases check for updates.
- The setup writes and checks every file before swapping any (§25 "To decide" 3).
- First releases: **0.2.0** and **0.2.1**, signed with a YubiKey.

**2026-11-16**
- The website may fetch what is directly about Binder's releases: `site/release.js` reads the latest release from
  GitHub's API (download link, version, VirusTotal result). Website only; the app's rules don't change.
- The Game mode toast's changes melt in (canvas board 33), drawn with Win2D (`Microsoft.Graphics.Win2D` 1.4.0,
  approved; MIT source, also named in the license permission). Chosen over the package-free outline version, whose
  joins had small corners. Its blur runs only while the toast plays.
- Each published setup is scanned by VirusTotal (`virustotal.yml`); the result goes into the release notes and on
  the website.
- A debug panel for Debug builds (`Binder.exe --dev`), never in a release (rules in `CLAUDE.md`).
- Setup step 2: the moved shortcuts fly into Binder's logo, as on the website.

**2026-11-17**
- First-run setup gets a fourth step, Game mode (§6), so the full feature set is there right after setup without
  looking for it in Settings. Off / Only items that don't need administrator rights / All items (the helper, one
  UAC prompt through the same `GameModeService.AllowAsync` as Settings › Game mode › Allow…). It opens on **Off**:
  Game mode stays opt-in. A line says it can be changed later in Settings › Game mode.
- The website's new look, "the desktop story" (design canvas https://claude.ai/artifact/9tRqEzXwkccPqmsjQYqWNc):
  the page opens on a Windows desktop full of game shortcuts that fly into Binder's shelf as you scroll, then a game
  with Binder gone and only the helper left (its measured memory), then "Welcome back". The rest is plain Windows
  (4 px controls, neutral greys, the accent only for actions); the blue download panel stays. The launcher chips'
  merge into the logo and the old "How it works" scroll story went with the old hero.

**2026-11-18**
- A release depends on its VirusTotal scan (§25, "Making a release", step 4): the draft is scanned before it's
  signed, and `sign-release.ps1` refuses a setup the release check blocked: flagged by any of the major engines
  (Microsoft, Kaspersky, ESET-NOD32, BitDefender, Sophos, Symantec, Avast, AVG, Malwarebytes, Trend Micro, McAfee,
  CrowdStrike) or by 3 or more engines of any kind. The owner can override it only with a reason, which goes into the
  published notes. Uploading the draft to VirusTotal before release is accepted (it'll be public anyway).
- The website has a Releases page (every version's notes and setup) and, once a download starts, a dialog in the
  site's own look (not a Windows dialog's): Defender may warn, the VirusTotal result, don't install if that's not
  acceptable.

**2026-11-18** (fixes from a review of the whole launch → hide → return path)
- **One watcher per game** (`Local\Binder.Watcher.<hash>`): a second Play of a game that's starting or running
  (Keep open, its Start entry) starts no second watcher, so its playtime isn't saved twice. Released once the
  session is saved, so a crash restart gets its own while Game mode is still put back.
- A session starts when the game's process did (not when detection or Game mode's changes finished), never before
  the watcher. A game already up is held before Game mode applies, so a quick crash is still seen.
- Shutdown: the second save waits for the first to finish writing; a shutdown that's cancelled carries on as after any
  game (Game mode put back, the widget back). Modern Standby sleep is registered for
  (`RegisterSuspendResumeNotification`). The last part of a session is kept however short; parts after a sleep carry
  `Resumed` (never "closed right after starting", may be restarted).
- A rescan keeps store games whose drive isn't there (unplugged, BitLocker-locked, offline share), as decided on
  2026-11-12. Steam games stay while Steam updates them (bit 4 cleared, installed depots kept).
- Shortcuts that start a launcher (Riot Client, GOG Galaxy, Battle.net, `steam.exe -applaunch`, …) or a Windows
  program (`explorer.exe shell:…`) watch nothing: the game stays open with the "Process to watch" toast instead of
  "running" as long as the launcher. Restore to desktop and Undo keep the games' settings for a later import; a copied
  shortcut whose original was deleted doesn't come back.
- A data file another program holds at start is read from its `.bak` and never saved over in that run.

**2026-11-19**
- **End a stuck game** approved and built (§28, board 40): hold the shortcut 2 s; the default ends a game only while
  Windows says it isn't responding (or its error report holds it); "Always" and "Off" as choices. With "Come back"
  off the shelf doesn't open for it, and its card isn't shown later. A new security rule: the watcher may end the
  game's own processes, only after the hold.

---

## 1. App icon (board 1)

**Logo B · Cover stack:** three fanned 3:4 covers with a play mark on the front one, using the accent-blue gradients.

- Master SVG, then a multi-size `.ico` (16, 20, 24, 32, 40, 48, 64, 256). Hand-tune 16–24 px: fewer shapes, no gradient.
- Tray icon: a monochrome glyph version for light and dark taskbars.
- The same icon is used for:
  - The exe (`ApplicationIcon`).
  - The Settings → Apps entry.
  - The tray.
  - The toasts.

## 2. Fallback covers (board 2)

The cover comes from the first step that has something:

1. **Custom cover** (exists).
2. **Store art on disk:**
   - Steam: as today.
   - Steam's own custom grid art in `userdata\<id>\config\grid\`, for non-Steam games the user added to Steam.
   - GOG Galaxy's local image cache when present (check the real layout on the machine).
3. **Icon on its own color:**
   - Take the exe or shortcut-target icon at 256 px with `IShellItemImageFactory::GetImage` (`SIIGBF_ICONONLY`).
   - Take the main color from a 16×16 downsample: the most frequent saturated hue.
   - Clamp it to a muted dark tone (HSL lightness 22–30 %, saturation at most 45 %) so white text passes 4.5:1.
   - Draw it live in XAML: a solid background, the 64 px icon on a faint plate and the title below.
   - Cache only the icon PNG plus the color (in `library.json`).
   - The Detail banner reuses the tint, with the icon large and faint on the right.
4. **Monogram:**
   - Initials: the first letters of the first two words, skipping "The".
   - Background: one of 8 fixed muted tones, picked from a stable hash of the title.

No network use. SteamGridDB is *not* approved: it's a third-party service with an API key.

## 3. Launcher icons on covers (board 2 and the shelf boards)

- **The badge:** a 24 px dark translucent rounded square (`rgba(0,0,0,.6)`, radius 6) in the top-left of the
  cover, holding a 14–16 px icon. It replaces today's `SourceLabel` text badge.
- **Icon source:**
  - Taken at runtime from the installed launcher's own exe with `IShellItemImageFactory`: `steam.exe`,
    `EpicGamesLauncher.exe`, GOG Galaxy, and so on. For the Xbox app, its packaged logo asset.
  - So no trademarked artwork is shipped in the repo.
  - Read once at 32 px, cached as PNG in the covers folder, one shared frozen `BitmapSource` per launcher.
- **If the icon can't be read:** a standard Segoe Fluent Icons glyph.
- **Manually added games and desktop shortcuts:** no badge.
- **Accessibility:** tooltip and `AutomationProperties.Name` = launcher name, so identity is never by logo alone.

## 4. Controller input (board 3)

**API:**
- XInput (`xinput1_4.dll`) via P/Invoke. It covers Xbox pads and anything Steam Input or DS4Windows exposes as XInput.
- Later: native PlayStation and Switch pads via `Windows.Gaming.Input`. It needs the
  `net10.0-windows10.0.19041.0` target; measure the size and startup cost first.

**Zero idle cost:**
- Find controllers when the panel is activated and on `WM_DEVICECHANGE` (Windows sends `DBT_DEVNODES_CHANGED` to every top-level window, so no registration is needed), checking again 1.5 s later since XInput may see a new pad a moment after Windows reports it.
- Poll only while the widget is the **active window** and a pad is connected:
  - 60 Hz for 5 s after the last input, then 10 Hz.
  - Stop completely when the widget is deactivated.
  - Only poll connected slots.

**Mapping** (WinUI's directional focus navigation, `FocusManager.TryMoveFocus`, and by index inside the grid so it scrolls past the realized covers; A on other controls uses their UI Automation pattern, so it does what Enter or Space would):

| Input | Action |
|---|---|
| D-pad / left stick | Move focus (repeat after 400 ms, then every 80 ms) |
| A | Play |
| X | Open Detail |
| Y | Favorite |
| B | Back |
| LB / RB | Previous / next filter chip |
| Menu | Settings |
| Right stick | Scroll |

- The hint bar and the bold focus ring appear on the first controller input and hide again on mouse or keyboard input.
- Search by controller comes later.

## 5. Flyout, tray icon, shortcut and stay-ready footprint (boards 6 and 9)

**Display modes** (`Settings.DisplayMode`):
- **Flyout** (default): hidden until opened, slides in at the docked edge, hides when you click away.
- **On the desktop:** today's docked behavior.

**Opening:**
- **Tray icon:**
  - On by default.
  - Use `Shell_NotifyIcon` via P/Invoke, *not* WinForms `NotifyIcon`, which loads WinForms.
  - Left-click toggles the panel; right-click opens a small menu (Open, Settings, Quit).
  - Windows 11 tucks new icons behind the overflow arrow and offers no API to change that, so setup explains how
    to drag it out.
- **Taskbar pin** (optional, not set up by default):
  - Windows doesn't let an app pin itself, so it's only mentioned in setup as an option.
  - Clicking a pin starts a second instance. The single-instance check at the very top of `Main` (before WPF
    starts) forwards "toggle" to the running instance and exits.
- **Shortcut:**
  - `RegisterHotKey` for **Win+Shift+G** on the same hidden message window as the tray icon.
  - If registration fails, offer Win+Shift+J.
  - Settings shows whether the key is free and has a "Change" button.

**Flyout behavior:**
- **Size:** full work-area height, 12 px margins, 560 px wide, on the docked side.
- **Open:** the window slides in 40 effective px (scaled to the monitor) over 300 ms with Fluent's entrance curve, one step per display refresh (`DwmFlush`), starting with the first rendered frame, by moving the window (`SetWindowPos` with
  `SWP_NOSIZE|SWP_NOACTIVATE|SWP_NOREDRAW`), so the content doesn't redraw.
  - If another monitor sits on that edge, skip the slide.
  - If "Animation effects" is off, appear instantly.
- **Close:** clicking elsewhere (deactivation), Esc, the toggle again, or the shortcut.
- **Pin:** a pin button in the header keeps it open.

**Stay ready, at the lowest possible footprint.** On close, in this order:
1. **Release the views:** detach the shelf, detail and settings views, clear the cover LRU, keep only the game list.
2. **Close the window itself,** not just hide it, which releases the graphics device and the visual tree. Rebuild it
   on open, which costs about 100–200 ms. Measure this against hiding.
3. **Compact once:**
   - `GCSettings.LargeObjectHeapCompactionMode = CompactOnce`.
   - `GC.Collect(2, Aggressive, blocking: true, compacting: true)`.
4. **Give memory back:** `SetProcessWorkingSetSizeEx(-1, -1)`.
5. **Efficiency mode** while closed: `SetProcessInformation` with EcoQoS (`ProcessPowerThrottling`) and
   `MEMORY_PRIORITY_VERY_LOW`. Undo both on open.
6. **Nothing wakes the process:** no timers. Only tray and shortcut messages arrive.

Build and runtime settings:
- ReadyToRun.
- `ConcurrentGarbageCollection=false`, workstation GC.
- `System.GC.ConserveMemory`.
- `TieredPGO=false`.
- Measure whether `InvariantGlobalization` saves enough. Only use it if name sorting still places umlauts correctly.

Code habits:
- Frozen lookup tables (`FrozenDictionary`) for the engine, anti-cheat and Game mode lists.
- `Span` and `ArrayPool` in parsers.
- Lazy views.
- No WinForms, WMI or `System.Management`.

**Report** the memory while closed and the time to open after building it.

**Fallback, only if the measured hidden footprint is still too high:** a tiny Native AOT resident helper (tray,
shortcut, single instance) that starts WPF on demand. Opening would then be a cold start of about 0.5–1 s. That
switch needs the owner's OK.

## 6. First-run setup (board 10)

Four steps, shown once (Settings has "Run first-time setup again"):

1. **We found N games:** launchers with game counts, each with a checkbox.
2. **Tidy up your desktop?** The existing shortcut picker, embedded; moves are reversible as today.
3. **How should it open?**
   - Flyout / On the desktop.
   - Start with Windows.
   - The shortcut.
   - A tip about the tray icon (and pinning as an option).
4. **Use Game mode?** (added 2026-11-17, §11): what it does in one line, then
   - three choices: **Off** (selected when the step opens; a run with Game mode already on starts from what's set),
     **Only items that don't need administrator rights** (takes the admin items out of the item list, so an allowed
     helper doesn't run them), **All items** ("Windows asks for permission once"; only on an administrator account,
     else `GmHelperNeedsAdmin` says why; "already installed" when the helper is current; puts the default admin items
     back when none are left; `SetupViewModel.ItemsFor`);
   - "What it changes while you play": the default items (`GameModeInfo.DefaultItems`, the services on one line),
     a shield on the ones that need the helper;
   - a closed card "What the admin helper does" (`Disclosure`): the prompt, Program Files, the two scheduled tasks,
     only the shielded changes, put back after the game, how to remove it;
   - "You can change this later in Settings › Game mode."
   - Done turns Game mode on or off. With All items it reads "Allow and finish…" with the shield and shows the UAC
     prompt first: approved, setup ends with Game mode on; declined, the step stays with a notice and nothing turned
     on; failed, an error with the code and `elevated.log`. Skip setup leaves Game mode off.

## 7. Shelf additions (boards 7 and 11)

- **Sort menu** (button above the grid; default sort stored in `Settings.SortBy`):
  - Recently played, Most played, Name (A–Z, culture-aware), Recently added, Size on disk.
  - "Favorites first" checkbox (on by default).
  - "Recently added" needs a new `Game.AddedAt`, set the first time a game is seen.
- **Favorites filter chip** with a star; a star badge on favorite covers (top-right).
- **"This week" card** (optional, `Settings.ShowWeeklySummary`, on by default). Built; it now shares one slot with
  "Continue playing" (board 16, `ShelfView`):
  - Total time, number of games, most played game, and 7 bars Mon–Sun.
  - Single series in the accent color: thin bars with 4 px rounded tops, a recessive baseline, a tooltip per bar,
    and an automation name with all values.
  - Computed from the sessions files when the panel opens. Never on a timer.
- **Cover size:** Small (5 columns), Medium (4), Large (3), List (`Settings.GridDensity`).
  - The list rows show a small cover, the name, the launcher and when it was last played.
  - Decode widths follow the tile size.
- **Hidden games:** already done. Keep the Undo toast and the Settings section as they are.

## 8. More launchers (board 9)

Each launcher is a small source class with bounded parsing and validation, as the security rules require. Settings
lists only the launchers that are installed, plus "Show N that aren't installed".

| Launcher | How games are found | How they're launched | Verify on a real PC |
|---|---|---|---|
| Xbox app / Game Pass | Windows' installed-app records under `HKCU\Software\Classes\Local Settings\…\AppModel\Repository\Packages` plus `AppxManifest.xml`; `XboxGames` folders via the `.GamingRoot` file on each drive | `shell:AppsFolder\{PFN}!{AppId}` | Yes |
| Ubisoft Connect | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{id}\InstallDir` | `uplay://launch/{id}/0` | — |
| EA app | Registry entries per game plus `__Installer\installerdata.xml` | `link2ea://launchgame/{id}` | Yes |
| Battle.net | `C:\ProgramData\Battle.net\Agent\product.db` (protobuf, minimal parser) plus a fixed product-code table | `battlenet://{code}` | Yes |
| Riot Client | `C:\ProgramData\Riot Games\RiotClientInstalls.json` | `RiotClientServices.exe --launch-product=… --launch-patchline=live` | — |
| Rockstar | `HKLM\SOFTWARE\WOW6432Node\Rockstar Games\{title}` | Through the Rockstar launcher | Yes |
| Amazon Games | `%LOCALAPPDATA%\Amazon Games\Data\Games\Sql\GameInstallInfo.sqlite` via `winsqlite3.dll` (ships with Windows, no package) | `amazon-games://play/{id}` | Yes |
| itch.io | `%APPDATA%\itch\db\butler.db` via `winsqlite3.dll` | Run the game file | Yes |
| Humble App | `%APPDATA%\Humble App\config.json` | Run the game file | Later |
| Meta (VR) | `Oculus\Libraries` plus `Software\Manifests\*.json` | Run the game file | Later |

- Add the matching `GameSource` values, filter chips and launcher icons (§3).
- `SourceLabel` stays as the accessible name.

## 9. Detail view additions (boards 8, 12, 13)

### Playtime chart
- Hours per week for 8 weeks, or per month for 12 months (segmented switch).
- Totals above the chart: total, this period, last played.
- Same chart style as §7, from our own sessions only.

### Launch options (`Game.LaunchArgs`, `Game.ExeOverride`)
- **Engine detection:**
  - Runs when Detail opens and is cached with the exe's last-write time.
  - Detection rules:
    - Unity: `UnityPlayer.dll` plus a `*_Data` folder.
    - Unreal: a `*-Win64-Shipping.exe` or an `Engine\` folder.
    - Source: `gameinfo.txt`. Source 2: `gameinfo.gi`.
    - Godot: a `.pck` next to the exe.
  - **Version** (2026-11-11), cached with the rest:
    - Unity: `UnityPlayer.dll`'s file version, "2019.4"; Unity 6 numbers itself 6000.x, shown as "6", "6.1".
    - Unreal: the build string `++UE5+Release-5.4` inside the shipping exe (one read-only pass, up to 512 MB),
      else "UE5"/"UE4" in its file version ("5"). Many games build from a branch of their own and show only
      "Unreal Engine" (on the owner's PC: 6 of 11 found).
    - Other engines carry no version that can be read.
  - **Icon** in the engine tag: a Segoe Fluent glyph that suits the name, not the logo (`Format.EngineGlyph`):
    Unity a cube, Unreal a lightning bolt, CryEngine an eye, id Tech a flame, Frostbite a thermometer, and so on;
    the controller for the rest.
- **Suggestions** (checkboxes with plain labels, the real argument shown beside each):

  | Engine | Suggestions |
  |---|---|
  | Unity | `-popupwindow`, `-screen-fullscreen 0`, `-force-d3d11`, `-force-d3d12`, `-force-vulkan`, `-monitor N` |
  | Unreal | `-dx11`, `-dx12`, `-windowed`, `-nomovie` (labelled "works in many games") |
  | Source / Source 2 | `-novid`, `-windowed`, `-noborder`, `-vulkan` |
  | Godot | `--rendering-driver vulkan` / `d3d12`, `--fullscreen`, `--windowed` |
  | Unknown | Free-text field only |

- **Also on the panel:** an "Extra arguments" field, a "Starts as" preview, **Test launch** and **Reset**.
- **How arguments reach the game:**
  - **Exe launches:** `ProcessStartInfo.ArgumentList`.
  - **Steam:** `steam://run/{appid}//{args}/`, built from validated parts. Steam may ask for confirmation the
    first time; say so in the UI.
  - **Epic:** not supported through its URI. Link to Epic's own per-game setting instead.
- **"Start with":** Automatic, or pick another exe in the install folder (validated to be inside it).

### Anti-cheat
- **Label:** under the title, e.g. "Easy Anti-Cheat ✓" or "BattlEye · 1 issue". Clicking it scrolls to the card.
  Hidden when nothing is found.
- **Detection:** file checks, run when Detail opens and cached until the game updates.

  | Anti-cheat | Found by |
  |---|---|
  | Easy Anti-Cheat | `EasyAntiCheat\` folder, `EasyAntiCheat_EOS_Setup.exe`, `start_protected_game.exe` |
  | BattlEye | `BattlEye\` folder, `BEService_x64.exe`, `*_BE.exe` |
  | Riot Vanguard | Riot games plus `vgk.sys` |
  | EA Javelin | `EAAntiCheat.GameServiceLauncher.exe` |
  | Others | Denuvo Anti-Cheat, nProtect GameGuard, XIGNCODE3, ACE, HoYoProtect |

  VAC isn't detected yet (it would need Steam's own app data).
- **Requirements:**
  - A built-in table: a baseline for each anti-cheat plus exceptions for specific games, e.g. Secure Boot or
    TPM 2.0 required on Windows 11. Updated with app releases.
  - The card states that requirements can change with game updates.
- **PC checks** (no admin, run when Detail opens):

  | Check | How |
  |---|---|
  | Secure Boot | `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled` |
  | TPM 2.0 | `Tbsi_GetDeviceInfo` |
  | Test signing / kernel debugging | `NtQuerySystemInformation` (code-integrity and kernel-debugger information) |
  | Vanguard restart pending | Driver installed but not loaded |

  When something is off, link to Microsoft's help page. Never change these settings.
- **Conflicts:**
  - **Running programs** from a built-in list: Cheat Engine, debuggers, trainers, memory editors, tools that load
    their own driver. Offer **Close**, which sends `WM_CLOSE`.
  - **Known blocked drivers**, e.g. old hardware-monitor or RGB drivers. Only a tip to update the tool. Verify
    whether driver names can be listed without admin.
  - Gray areas (ReShade, AutoHotkey, overlays) only for games known to object.
  - **"Remind me before launching"** checkbox: Play shows "Close X first?" with *Close it and play* or *Play anyway*.
- **Never-touch list:** Game mode must never stop anti-cheat services (EasyAntiCheat, BEService, vgc, …). The
  watcher only opens the game process with the minimal rights it needs. If an anti-cheat refuses the priority
  change, skip it quietly.

## 10. Crash detection (board 13)

**Watcher** (stays tiny):
- Save the exit code with the session: `Session.ExitCode`, via `GetExitCodeProcess` on the handle it already holds.
- Treat engine crash reporters as "not the game":
  - `CrashReportClient.exe` (Unreal).
  - The game's crash handler starting *after* the main exe exited.
  - The restart grace must not mistake them for the game relaunching. Record a crash-reporter flag instead.

**Widget**, when it comes back (or shows the toast):
- Look up Windows events in the session's time window via `wevtapi.dll` (`EvtQuery`, no admin):
  - Application log: event 1000 "Application Error" (exe, exception code, faulting module) and 1002 (hang).
  - System log: event 4101 "Display driver stopped responding and recovered".
- **Classify:**
  - **Crashed:** the exit code is a crash code *and* there's an event or a crash reporter.
  - **Closed unexpectedly:** only the exit code. Use softer wording, since some games exit nonzero normally.
  - **Closed right after starting:** within ~5 s, e.g. `0xC0000135` / `0xC000007B` → missing runtime; link to
    Microsoft's Visual C++ Redistributable page.
  - **Normal.**
- **Explain in plain words** from a built-in table:

  | Faulting module | Shown as | Advice |
  |---|---|---|
  | `nvwgf2umx.dll` / `nvoglv64.dll` | NVIDIA graphics driver | Update the driver, remove the GPU overclock |
  | `amdxx64.dll` / `atiumd*` | AMD graphics driver | Same |
  | `igd*` | Intel graphics driver | Same |
  | `GameOverlayRenderer64.dll`, Discord / RivaTuner hooks, a ReShade `dxgi.dll` in the game folder | Overlay or mod | Turn it off for this game |
  | `vcruntime*`, `ucrtbase` | C++ runtime | Reinstall the Visual C++ Redistributable |
  | Unknown | Exception description plus module name | No made-up advice |

- **The crash card** at the top of the panel:
  - "[Game] crashed after 1 h 12 min", one sentence of explanation.
  - Details: what happened, where, other events at that time, when.
  - Actions:
    - **Play again**.
    - **Open game log**: Unity `Player.log`, Unreal `Saved\Logs`, found via the engine detection.
    - **Copy bug report** (every crash card and the "didn't start" card).
    - **Copy AI prompt**, only when the error isn't in the table above (the "Unknown" row): the card then says
      "Binder doesn't know this error" and offers both copy actions instead of advice (board 13, right column).
- **The two texts** (plain language, no technical background needed; built locally; copied with the Windows
  clipboard and confirmed with "Copied", with the text shown so the user sees what they paste):
  - **AI prompt:** a question in the first person for any AI assistant: the game, how long it ran, the exception code
    with its plain description and the faulting file, the PC (Windows version, graphics card and driver version,
    memory), how many crashes in the last 10 sessions and after how long, ending with "I am not technical. What could
    cause this, and what can I try, step by step, starting with the easiest fix?"
  - **Bug report:** the same facts as a short report for the developer: game and store version (Steam, Epic, …),
    play time before the crash, date and time, error code and file, how often, PC. Ends with "Report from Game
    Shelf. No personal data or file paths included."
  - **Where the PC facts come from (no admin, no network):** Windows version from
    `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` (`ProductName`, `DisplayVersion`); graphics card and driver
    from the display adapter class key (`DriverDesc`, `DriverVersion`); installed memory from `GetPhysicallyInstalledSystemMemory`.
  - **Privacy:** never include the user name, user-profile paths or full install paths (file names only), and no
    other running programs. Nothing is sent anywhere; the user decides where to paste.
- **If "Come back" is off:** show a "Restored"-style toast (§11) with Details instead.
- **Detail view:** a warning icon on crashed sessions, plus a pattern line when there is one ("2 crashes in the
  last 10 sessions, both in the graphics driver").
- **Privacy:** all local. Nothing is uploaded.
- **Out of scope:** detecting whole-PC crashes (owner's decision), and telling a Task Manager kill or an
  anti-cheat kick apart from a normal exit.

## 11. Game mode (boards 4 and 5)

### What it changes (all opt-in)

| Item | How | Admin | Default |
|---|---|---|---|
| Power mode → Best performance | `PowerGetEffectiveOverlayScheme` / `PowerSetActiveOverlayScheme` (undocumented, approved). Falls back to `PowerSetActiveScheme` with High performance. Restore the exact previous GUID. | No | On |
| Game priority Above normal | `SetPriorityClass` on the game process. Never High or Realtime. Nothing to restore. | No | On |
| Windows Search (`WSearch`) | Stop the service | Yes | On |
| SysMain | Stop the service | Yes | On |
| Telemetry (`DiagTrack`) | Stop the service | Yes | On |
| Print Spooler | Stop the service | Yes | Off |
| Game Bar background recording | HKCU `…\GameDVR\HistoricalCaptureEnabled`. Check whether it applies live. | No | Off |
| Close chosen apps | `WM_CLOSE`, reopen afterwards. Leave the app open if it doesn't close within ~5 s. | No | Empty list |
| Game + voice chat first | QoS policy with DSCP 46 for the game exe and the "kept smooth" apps | Yes | On |
| Slow down background uploads | QoS policy throttle for the upload list (OneDrive, Google Drive, Dropbox; editable) | Yes | On, 5 Mbit/s |
| Limit Windows Update downloads | Delivery Optimization bandwidth policy | Yes | On |

### Network details

- **Mechanism:** Windows policy-based QoS (`MSFT_NetQosPolicySettingData`, the same as `New-NetQosPolicy`),
  matched by app path. Prefer the **temporary ActiveStore** policy store, which disappears on reboot so a crash
  can't leave anything behind (verify on the machine). Call it through COM/WMI from the elevated helper, not by
  starting PowerShell.
- **Marking (DSCP 46):** only helps if the router honors it; the UI says so.
- **Throttling:** only limits what an app *sends*, so it targets uploads, the usual cause of lag spikes.
  Downloads can't be throttled locally.
  - Steam pauses its downloads during games by default.
  - For Epic and Battle.net, point to their own download limit.
- **"Always kept smooth, never slowed down" list:** the game, Discord, TeamSpeak, Mumble, Teams, Zoom (detected
  when installed), plus user-added apps. They're never throttled and get the same marking as the game.
- **Delivery Optimization:** set its bandwidth policy for the session, then restore the previous value exactly
  (including "not set").
- **Excluded:** Nagle / `NetworkThrottlingIndex` / adapter tweaks, turning off Wi-Fi scanning, lowering other
  apps' priority.

### Excluded overall, and why

- **Defender:** security, and Tamper Protection blocks it anyway.
- **Windows Update services** (`wuauserv`, `UsoSvc`, `BITS`): stopping them mid-install can leave updates half-applied.
- **Delivery Optimization service:** protected; only its bandwidth policy is used.
- **Xbox / Gaming Services**, **anti-cheat services:** games need them.
- **Audio, networking, DWM, Explorer:** visibly break things.
- **Settings that need a reboot or weaken security:** HAGS, VBS.
- **"Clear standby memory", timer or core-parking hacks:** placebo or harmful.
- **Focus Assist:** no API; link to `ms-settings:notifications`.
- **Services that are already stopped:** left alone.

### Exact restore

1. **Snapshot** every enabled item:
   - Service: running state, start type, delayed-auto flag.
   - Power: GUID.
   - Apps: running yes/no plus path.
   - QoS: which policies we created.
   - Delivery Optimization: previous policy values.
2. **Write-ahead journal**, flushed *before* the first change.
3. **Pause, don't reconfigure:** only stop services, never change their start type. A service that's Automatic
   but stopped is skipped and stays exactly that way.
4. **Before stopping a service,** all of these must hold:
   - It's on the allowlist.
   - It's running.
   - It accepts stop (`SERVICE_ACCEPT_STOP`).
   - No running service depends on it.
5. **Restore** in reverse order:
   - Only change what differs from the snapshot.
   - Restart only what was running.
   - Reopen only what *we* closed.
   - Set power back only if it is still the value *we* set.
6. **Verify:** re-query, compare, log, then delete the journal.
7. **Recovery:** if the journal survives (a crash), restore on the next widget start and from the helper task's
   at-logon trigger. Settings has a "Restore now" button.

### Elevation and security

- **One "Allow…" UAC prompt:**
  - Installs `Binder.Elevated.exe` to `%ProgramFiles%\Binder\`. Never in a user-writable folder.
  - Registers the scheduled tasks *Binder Apply* / *Binder Restore* with "Run with highest privileges".
- **Helper version:** "Allow…" writes `ElevatedPart.HelperVersion` (a whole number, not the app version) into
  `allowed-<SID>.txt`. Only another number there asks for "Update…" (one UAC prompt), so a Binder update that
  doesn't change the helper doesn't ask again. **Bump `HelperVersion` whenever `Binder.Elevated` changes**: what
  it does, the request, journal or result format, or how its tasks and folders are set up. Markers from before it
  hold the app version ("0.1.3") and update once more. Update replaces the copy in Program Files; the same exe
  already there is left alone.
- **Per game:** the watcher runs a task (`schtasks /run`) and waits on a named event with a timeout.
- **The helper trusts nothing from the user side:**
  - It acts only on ids from its compiled-in allowlist.
  - The journal lives in `%ProgramData%\Binder\` with an admin-only ACL.
- **Without the helper:** the no-admin items still work.

### Timing

- **Apply:** when Play is pressed, in parallel with the game starting.
- **Restore:** after the watcher confirms the game closed (after the 3 s restart grace); with "Come back" on, while the widget starts (its toast follows), else before it starts.
- **If the game is never detected:** restore immediately.

### Game mode toast (board 4)

- **Style:** replaced by the one-line pill and the failure card of §18 (board 23). Like the Windows 11 volume
  flyout, centered above the taskbar. The dot pops in and its check draws itself once (no idle cost; skipped with
  "Animation effects" off).
- **After the restore** (`Binder.exe --restored <result>`; with "Come back" off only the toast shows, then it exits):
  - "Windows is back to normal · Game mode put back 5 changes" (closes after 4 s), with Binder's own calm sound
    (§18; "Play a sound with it", on by default; system sounds volume, skipped while
    `SHQueryUserNotificationState` says notifications are held back), or
  - "Restored 4 of 5 · Print Spooler couldn't be put back" plus **Details** (stays until dismissed).
- **After it applied** (`--applied <result>`, forwarded to a widget that is still up: the countdown or Keep open;
  a widget that isn't running never starts for it, so with "Instantly" it doesn't show):
  - "Game mode is on · 3 changes · put back when the game closes", or "Windows was already set up for gaming"
    (closes after 4 s); something that couldn't be changed is named (closes after 6 s).
- **Accessibility:** UI Automation live region; honors "Animation effects" off.

### Settings sub-page (board 5) and model

- **Rows on the page:**
  - Master toggle and the "Allow…" banner.
  - Performance.
  - Pause background services, each row showing its current state.
  - Network.
  - Close apps while playing.
  - When the game closes: confirmation toggle, last result plus View log, Restore now.
  - Per game: Default / On / Off.
- **Model additions:**
  - `GameModeEnabled`
  - `GameModeItems` (allowlist ids)
  - `GameModeCloseApps`
  - `KeepSmoothApps`
  - `ThrottledUploadApps`
  - `UploadLimitMbps` (default 5)
  - `ShowRestoreToast`
  - `Game.GameMode` (null / on / off)

---

## 12. Free up space (board 17)

A new game needs more room than a drive has. The user says how big it is; Binder suggests which installed games
to remove and on which drive, and hands each removal to the game's own launcher. It never deletes game files itself.

### Where
- **Settings:** a "Storage" row, "Free up space…".
- **Detail view:** "Free up space…" in the "…" menu, opening with that game's drive selected.
- A page inside the panel like Settings (back button, Esc). Nothing runs until it's opened.

### Input
- "How much space does the new game need?": a `NumberBox` with the unit "GB" inside the field on its right (you type
  only the number; screen-reader name "gigabytes"), followed by "in total, as on its store page". Focused when the
  page opens; the plan updates as you type.
- Drive free space comes from `DriveInfo.AvailableFreeSpace`, read when the page opens and after "Check again".
  No watchers, no timers.

### Which games count
- Installed games on that drive (grouped by `Path.GetPathRoot(InstallDir)`), hidden ones included (they still use space).
- Need a known size: Steam `SizeOnDisk`, Epic install size, EA / Ubisoft `EstimatedSize`. Games without one are shown
  but never suggested. No folder measuring.
- Manual games and shortcuts are left out: no launcher can uninstall them.
- A game that's running right now is left out.

### The plan, per drive
1. `missing = need − free`. Zero or less: "It fits already".
2. Candidates go in three **tiers**, used in order; a later tier only when the earlier ones aren't enough:
   1. Everything else.
   2. Played in the last 14 days.
   3. Favorites.
3. Within a tier, by **playtime band**: never played, under 1 h, under 5 h, under 20 h, more. Lower bands first.
4. Within a band, the **closest fit**: the smallest game that covers what's still missing; if none does, the largest,
   then repeat with what's left.
5. Finally drop any picked game the plan doesn't need after all, biggest first.

Signals:
- Playtime: our own sessions.
- Last played: the later of our sessions and Steam's `LastPlayed` in `appmanifest_*.acf` (`Game.StoreLastPlayed`), so a
  game played through Steam directly isn't treated as unplayed. Verified on the real PC.

**Best drive:** the plan with the least total playtime, then the fewest games, then the smallest overshoot. It gets a
"Best choice" label; the user can pick another drive.

### Showing it
- Drive letters always appear as a small tag with the Fluent drive glyph (e.g. `[drive D:]`), so "D:" reads as a
  drive, not a typo.
- One card per drive: the drive tag and its label, a used-space bar, "N games to remove" or "fits already".
- Heading above the treemap: "What takes up space on `[drive D:]`" with an (i) button. One line under it: "Each tile
  is one of your games: the bigger the tile, the more space it uses. The highlighted ones are suggested for removal.
  Click a tile to add it or leave it out."
- The (i) opens a `TeachingTip`, "How games are picked", with the three steps (least played first, closest fit
  within similar playtime; then games played in the last 14 days; favorites last) and "Games that aren't needed
  after all are left out again." The rules are **not** in the legend; the legend only explains the tiles
  (suggested, kept, free now).
- A **treemap** of the chosen drive (squarified, built once per change, static):
  - One tile per game, sized by its size on disk, on the game's muted color (`CoverColors`) with its icon
    (`FallbackArt`), or a monogram when there's none.
  - Suggested games: full color, accent outline, a check mark. Kept games: dimmed. Free space: a dashed tile.
  - Marks for "played recently" and favorites.
  - Clicking a tile adds or removes it. Each tile is a focusable toggle with a screen-reader name (name, size,
    playtime, suggested or not).
- A summary: "Frees 80 GB of the 78 GB missing", then the chosen games with size and why (e.g. "Never played ·
  8 months ago").

### Uninstall
- "Uninstall N games…" goes through them one at a time:
  - Steam: `steam://uninstall/{appid}` (Steam asks first), as the existing "Uninstall in Steam…" does.
  - Launchers without an uninstall link (Epic, EA app, Ubisoft Connect, GOG Galaxy, …): open the launcher, with a
    toast naming the game to remove there. Where no launcher is installed, open Settings → Apps.
- "Check again" re-reads free space and the library afterwards.
- Security: only built-in links from validated ids; nothing deleted by Binder.

---

## 13. Look and moments (milestone 16)

Five small things that make the app pleasant to look at without getting in the way. The rules for all of them:
- They happen only when the user does something or a session ends. Nothing runs while the panel is idle.
- Nothing plays on every opening of the panel.
- With "Animation effects" off in Windows, anything that moves is skipped or stays still. In high contrast, the
  decorative layers (logo shadow, sheen) are left out.
- Designs: not on the canvas yet. The shiny sleeve has an interactive prototype:
  https://claude.ai/artifact/9jPBr9Y16UycgVVoWS48ck (board to add to the canvas with the others).

### a. The game's logo on banners

- Where: the "Continue playing" banner and the Detail view's banner.
- Steam keeps a transparent logo per game next to the cover and hero art: `logo.png` in
  `appcache\librarycache\{appid}\` (possibly in a hashed subfolder, like the other files) or the older
  `{appid}_logo.png`. Check the real layout on the PC first.
- If it isn't on disk, download it once from the same CDN as the covers (`…/apps/{appid}/logo.png`, check the name)
  into our cover cache, with the same size cap and "missing" marker as covers. `ArtKind.Logo` in `CoverCache`.
- Shown at the banner's bottom left instead of the title text: at most 56 px high in the slot, 72 px in Detail,
  at most 60 % of the banner's width, aspect kept. A soft static shadow behind it so dark logos stay readable.
- Loaded only while the banner is shown, like the hero art. The image's screen-reader name is the game's title.
- No logo (not Steam, or none found): the title text as today.

### b. The cover grows into the detail view (dropped)

**Removed on 2026-11-09 (owner's choice): it looked bad.** Details open and close without it. Kept here only as a
record; don't build it again.

- Opening details from a cover animates that cover into the Detail view's cover position (WinUI's
  `ConnectedAnimationService`, default duration, about 250 ms). Back plays it in reverse into the grid, if that
  cover is in view; otherwise no animation.
- Only for the cover grid, not the List view. Skipped with "Animation effects" off.
- No cost outside those 250 ms: the service only runs while an animation plays.

### c. "Welcome back" after a session

- The first time the panel shows after a game's session ended (the watcher brought it back, or the next opening
  if "Come back" is off), the top slot shows "Welcome back" instead of "Continue playing":
  - the game's hero art and logo (a), "Welcome back", the session's length ("1 h 42 min") and the week so far
    ("6 h this week", from `PlaytimeStats`), and a Play button;
  - a milestone line from (e) when this session passed one.
- It's shown once: the next opening shows the normal slot. Nothing to dismiss.
- After a crash, the crash card shows as today and "Welcome back" still shows the session, without a milestone line.
- Sessions shorter than 2 minutes don't get a "Welcome back" (a game that didn't start properly).
- Stored: `Settings.WelcomeBackSessionStart` (the start time of the last session it was shown for), so it
  survives the process exiting during the game.

### d. Shiny sleeves for favourites

- Favourite covers get a faint, still rainbow sheen and a thin light edge, like a rare trading card. While the
  pointer moves over a favourite cover, the sheen and a soft highlight follow it. When the pointer leaves, they
  ease back to rest in about 0.5 s.
- Only favourites, only in the cover grid (not List), and only on the tile under the pointer: the pointer is
  followed with `PointerMoved` on that tile, which sets two composition properties (the gradient's offset and the
  highlight's position). No timers.
- Built with Composition layers on top of the cover: a diagonal rainbow `CompositionLinearGradientBrush` at low
  opacity, a radial white highlight, and the edge. Blend modes (the prototype's colour-dodge) would need Win2D,
  which is approved for the Game mode toast only; plain alpha layers come close. Compare with the prototype, and if it isn't good enough,
  ask the owner before adding a package.
- Settings › Appearance: "Shiny favourites" (on by default). No intensity setting: tune it once to the
  prototype's "Medium".
- "Animation effects" off: the sheen stays at rest. High contrast: no sheen. The prototype's tilt is not built.

### e. Quiet milestones

- When a game's playtime (our own sessions, as in the Detail view) passes 10, 50, 100, 250, 500 or 1,000 hours,
  the next "Welcome back" (c) carries one line: "100 hours in [Game]". No toast, no badge, no sound.
- Each milestone is shown once: `Game.MilestoneShownHours` keeps the highest one shown. Crossing two at once
  shows only the higher one.
- The first time this runs on an existing library, it records the milestones already passed without showing
  them, so long-time users aren't greeted with old milestones.

## 14. Advanced: sources and artwork (milestone 17)

Why: Binder reads other launchers' files, folders and registry keys, and downloads Steam artwork from a fixed
address. If a launcher changes any of that and Binder hasn't been updated yet, games or pictures disappear. Users
can then point Binder at the new place themselves.

### What can be changed

All defaults move into one place, `Library/SourceDefaults.cs`, as a fixed list of named entries. Every source and
`CoverCache` read their locations through it, so a user's value replaces the default everywhere it's used.

| Launcher | Entry | Default |
|---|---|---|
| Steam | Steam folder | from `HKCU\Software\Valve\Steam\SteamPath` |
| Steam | Artwork folder | `{Steam folder}\appcache\librarycache` |
| Steam | Cover file names (tried in order) | `library_600x900.jpg, library_capsule.jpg` |
| Steam | Banner file name | `library_hero.jpg` |
| Steam | Logo file name | `logo.png` (§13 a) |
| Steam | Download address | `https://shared.steamstatic.com/store_item_assets/steam/apps/{appid}/{file}` |
| Epic | Manifests folder | `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests` |
| GOG | Games registry key | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games` |
| Ubisoft Connect | Installs registry key | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs` |
| Rockstar | Launcher registry key | `HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Launcher` |

Not included: what Windows itself provides (Settings › Apps entries for EA and Rockstar, Xbox's registered
packages and `.GamingRoot`); those are Windows formats, not a launcher's.

### Where and how it looks

- Settings › Advanced (at the bottom, collapsed by default): "Where Binder finds games and pictures", one group
  per launcher. Each entry is a text box whose placeholder shows the default; empty means "use the default".
  A "Reset" button per entry and "Reset all".
- A short line on top: "Only change these if games or pictures went missing after a launcher update."
- "Check" button per launcher: runs that source once with the current values and shows what it found, for
  example "Found 42 games · covers for 40 · banners for 38 · logos for 31", or which entry failed ("Artwork folder
  not found"). Runs only when clicked.

### Rules (the values are untrusted input like the launchers' own files)

- Stored in `Settings.SourceOverrides` (entry name → value); unknown names are ignored.
- Folders: a full local path that exists. Registry keys: under `HKLM\SOFTWARE\` or `HKCU\Software\` only, read only.
- File names: one name, no folder separators, `.jpg`, `.jpeg` or `.png`.
- Download address: `https://` only, must contain `{appid}`; `{file}` is replaced by the file name. When the host
  differs from the default, the page shows "Binder will download pictures from [host]" next to it. Downloads keep
  the size cap and are only ever decoded as images.
- Everything else the security rules require (id validation, `InstallFolder.Validate` for install folders, bounded
  parsing) still applies to what is read from the new place.
- Changing an artwork entry forgets the "missing" markers and resolved paths for that launcher, so pictures are
  looked for again; pictures already cached stay.
- Unit tests for the validation of each kind of value.

## 15. Performance (milestone 18)

Each item is measured on the real PC before and after, and kept only if it helps: time to the first window, reopen
time with "Use less memory", memory with covers shown (Task Manager › Details, "Memory (private working set)"),
smooth scrolling through the grid, and the published size. Report the numbers in the commit message and here.

### a. The newest WinUI patch

- `Microsoft.WindowsAppSDK.WinUI` 2.6.4 → the newest 2.6.x patch (2.6.5 was released on 2026-11-03). Update once it
  has been out for about two weeks and its release notes show nothing that affects this app.
- Pin the exact version, update the dependency list in `CLAUDE.md`, and let CI's build, smoke test and installer
  test run.

### b. Pre-scaled copies of covers

- Today every cover is read and decoded from the full file (Steam: 600×900 JPEG) and scaled to the tile on decode
  (`DecodePixelWidth`). The tile is about 120 px wide at 100 % scaling.
- Instead, the first time a cover is shown, write a scaled copy into the cover cache (`covers\{id}_{width}.jpg`),
  sized to the widest tile the current settings need in physical pixels (cover size and display scaling), rounded
  up to a multiple of 64. Later loads read the small copy.
- Encode with Windows' own `BitmapEncoder` (Windows.Graphics.Imaging), quality about 90, on a background thread, at
  most two at a time, like the downloads. One write per cover, and only when the source is more than 1.5 times wider
  than needed.
- When the needed width grows (larger covers in Settings, a higher scaling), make a new copy and delete the old one.
  Custom covers chosen by the user are scaled the same way; the original file is never touched.
- Hero art (banners) and logos stay as they are: they're shown large.

### c. Load hidden parts of the UI only when needed

- Nothing in the XAML uses deferred loading yet (`x:Load`). Mark elements that aren't visible when a view first
  shows, so WinUI builds them only when they're needed:
  - the Settings page and its sections (it's only shown when opened);
  - the Detail view's parts that are collapsed at first (crash card, launch options, anti-cheat details, the
    "This game only" settings);
  - the shelf's crash card and empty states.
- Elements whose names are used in code-behind need a null check or a `FindName` call before first use; go through
  each `x:Name` that gets `x:Load`.

### d. AOT compiler settings

- Try `OptimizationPreference` `Speed` and `Size` against the default, for the widget and the watcher, and keep
  whichever is best on the measurements above. Add `IlcGenerateStackTraceData=false` only if the error log stays
  readable without it.
- Watcher, done 2026-11-16, measured on the real PC 2026-11-18: `OptimizationPreference` `Size`, identical method
  bodies folded, no stack trace data (its logs keep `e.Message` only), no EventSource, metrics or debugger support,
  non-concurrent GC (no background GC thread), and its working set emptied (`SetProcessWorkingSetSize(-1, -1)`)
  right before it blocks on the game.
  CI's smoke test (Native AOT, 5 s into the fake game), main → this change: working set 9.2 → 3.1 MB, private
  3.1 → 3.2 MB (unchanged within noise: emptying the working set frees RAM but not committed memory, and the GC
  switch made no measurable difference); installed size 114,622 → 114,115 KB, `BinderSetup.exe` 34,678 → 34,558 KB.
  Real PC, 2026-11-18, Windows 11 25H2 (build 26200.9974): 0.2.1's installed `Binder.Watcher.exe` against this
  branch's `publish.ps1` build, each started with the widget's arguments right after the game, read 10 s into the
  game (counters as Task Manager › Details shows them: Memory = private working set, Commit size = private bytes;
  CPU time over the next 10 s). Exe 2,000 → 1,613 KB.

  | Game (source)                | Private WS (KB) | Working set (KB) | Commit (KB)   | CPU |
  |------------------------------|-----------------|------------------|---------------|-----|
  | Balatro (Steam, Game mode on)| 1,820 → 592     | 12,204 → 3,248   | 3,692 → 4,080 | 0 → 0 |
  | Little Inferno (Epic)        | 1,332 → 540     | 9,012 → 3,244    | 3,096 → 3,316 | 0 → 0 |
  | Magical Drop VI (GOG, exe)   | 1,392 → 540     | 9,096 → 3,312    | 3,200 → 3,304 | 0 → 0 |

  Working set about a third, private working set under half; commit size about the same (up 0.1–0.4 MB, the
  non-concurrent GC's or noise; CI saw the same). Behaviour unchanged, all with the new build: each game detected,
  its session saved, the widget back after it closed; Game mode applied and put back (helper: "Restored 5 of 5");
  Balatro with "Restart if it crashes", ended by force after 70 s: recorded as closed unexpectedly, the restart
  pill played it again. Error logs read the same in both builds (exception messages are resource keys already,
  `UseSystemResourceKeys`), e.g. "IO_SharingViolation_File, …\sessions-2026.jsonl".
- Don't raise the instruction set (`IlcInstructionSet`, e.g. AVX2): Binder would crash on older processors.

### e. CsWinRT 3.0, once it's stable

- CsWinRT connects .NET to WinUI. Version 3.0 rewrites it for Native AOT (smaller and faster) but is only a preview
  so far (3.0.0-preview in 2026-03). Move to it once there's a stable 3.0 that the stable Windows App SDK supports,
  and only through the Windows App SDK's own version (no separate package).

## 16. Quick actions (board 18, milestone 19)

Free up space is three clicks deep (Settings › Storage › Free up space…) and Game mode two plus a page. Both get a
shorter way in, without taking room from the grid.

### a. The "…" button in the shelf header

- An icon button at the right end of the header: Segoe Fluent "More" (`E712`), tooltip and screen-reader name
  "More actions". It also holds Settings (owner, 2026-11-11: no separate gear; Settings is occasional, and the
  header stays calm).
- It opens a `MenuFlyout` (the same native menu as the cover's right-click menu), built in its `Opening` handler,
  not when the shelf loads, so it costs nothing until it's used:
  1. **Game mode**, a `ToggleMenuFlyoutItem` (`Ctrl+G`): turns `Settings.GameModeEnabled` on or off and saves.
     A game set to On or Off in its own settings keeps that. When the admin helper needs an update
     (`GameModeService.HelperNeedsUpdate`), the item reads "Game mode (needs update)" and opens the Game mode page
     instead of switching.
  2. **Game mode settings…**: the Game mode page; Back returns to the shelf.
  3. Separator.
  4. **Free up space…**: the Free up space page, opened as from Settings (it picks the drive itself and follows
     "Best choice" once a size is typed; mid-uninstall it continues there). Back returns to the shelf.
  5. **Add a game…**: the standard file picker, as in Settings.
  6. **Refresh library** (`F5`): the same rescan as Settings' refresh button.
  7. Separator.
  8. **Settings** (`Ctrl+,`, gear `E713`). Game mode settings uses the sliders glyph (`E9E9`) so the two differ.
- While Game mode is on, a checked toggle button with the Game glyph (`E7FC`, accent fill like a Quick Settings
  tile) sits left of "…"; selecting it turns Game mode off. It replaced a 7 px accent dot on "…", which read as an
  unread notification (owner, 2026-11-11). The menu item stays checked too.
- `Ctrl+G`, `F5` and `Ctrl+,` work while the shelf has focus. Neither types a character, so type-to-search is unaffected.

### b. The same items in the tray menu

- Today: Open Binder, Settings, Quit. New: Open Binder, separator, **Game mode** (checked while on;
  `MF_CHECKED`), **Free up space…**, separator, Settings, Quit.
- Game mode switches without opening the panel (the setting is saved straight away). With the helper out of date,
  it opens the panel on the Game mode page, like the header item.
- Free up space opens the panel on that page.

### Not chosen

- A row of action chips under the header: permanent space taken from the grid for occasional actions.
- A Game mode switch always in the header: fastest, but easy to hit by accident, and it doesn't fit the calm
  header. (The toggle shown only while Game mode is on, above, is a status that also turns it off.)

### Touches

`ShelfView` (button, menu, accelerators), `MainWindow` (the actions), `App` and `MessageWindow` (tray items, a
checked item), `FreeSpaceView` (choosing the drive). No new settings.

## 17. Restart after a crash (milestone 20)

For games that crash now and then mid-session (long online sessions, unstable mods): Binder starts the game
again on its own, with a short countdown the user can cancel. Off by default; it's a per-game choice, since a game
that crashes on every start would loop.

### Setting

- Detail › "This game only": **Restart if it crashes**, a toggle (`Game.RestartOnCrash`, default off). Its help
  line: "Starts the game again after a crash, at most twice in a row."
- No global setting: the games that need it are few, and a global "on" would restart games that crash on start.

### When it restarts

- Only for the crash card's **Crashed** and **Closed unexpectedly** endings (§10). Never after a normal exit,
  never after **Closed right after starting** (a missing runtime or a broken install; restarting won't help), and
  never when the session ran under 60 s.
- At most **2 restarts in a row**: the count is the game's consecutive crashed sessions in `crashes.json` within
  the last 30 minutes, so there's no new state and nothing kept in memory (the widget is gone while the game runs).
  After the second, the crash card shows as today, with the line "Restarted twice already, so Binder stopped
  trying".
- It needs the widget to come back. With "Come back when the game closes" off, the watcher still relaunches the
  widget after a crash exit code when the game has `RestartOnCrash` (it passes `--crashed`; the widget shows only
  the restart pill, not the panel, and exits again with the restarted game, or when there's nothing to restart).

### What the user sees

- A pill like Game mode's (§18, `Views/GameModeToast`, owner's change of 2026-11-11, instead of the launch toast):
  centered above the taskbar, a caution dot with the restart glyph, "[Game] crashed · Restarting in 5 s", a fixed
  **5-second** countdown with a thin bar along the bottom edge that drains (one smooth compositor move; a step a
  second with animations off), and **Cancel**. It replaces a "Back to normal" pill; a Game mode message that waits
  for the user (something couldn't be put back) is never covered: then there's no restart, the crash card shows.
- Cancel, Esc or B (when the pill has focus), or opening the panel another way stops it and shows the crash card,
  so a crash is never hidden. Restarted sessions' crashes stay in `crashes.json`, so the pattern line still counts
  them.
- When the countdown ends it's an ordinary Play: the same launch path, watcher, hide and Game mode (Game mode was
  restored when the game exited and is applied again). With the panel up that's Play with its launch toast; without
  it the game starts and the widget exits at once (the anti-cheat reminder's question opens the panel; a start that
  fails opens it with the error).

### Not chosen

- Restarting from the watcher without the widget: the watcher doesn't know the launch paths (store URIs, launch
  options, "Start with"), would have to duplicate `Launcher`, and the user couldn't cancel.
- Restarting after a whole-PC crash: Binder doesn't detect those (owner's decision, §10).

### Touches

`Game` (`RestartOnCrash`), `DetailView` (the toggle), the watcher (`--restart 1`; relaunch after a crash exit code
when the game has it, even with "Come back" off), `RestartViewModel` and `GameModeToast` (the pill), `App.Restart`
(the decision after the crash check, then Play or `Launcher`), `CrashAnalysis` (the in-a-row count), `CrashCard`
(the "Restarted twice already" line).
## 18. Calmer Settings and Game detail, the Game mode pill and its sound (boards 19, 21 and 23)

Settings and Game detail had grown crowded: long cards of rows with an explanation under every setting. Both now
follow Windows 11 Settings: one card per setting, a little apart, under a few group headings, and anything with
more inside opens from its card. **Nothing was removed**; every setting and action is still on the page.

### Shared pieces

- `Themes/Theme.xaml`: `GroupLabel` (BodyStrong, 36 px above, 12 below), `SettingCard` (one card, 52 px high,
  6 px apart), `SettingCardBody` (the opened part, joined under its card, a shade darker, indented to the title),
  `SettingLinkCard` (the whole card is a button to another page), `CardIcon` (20 px Fluent glyph), `DisclosureButton`.
- `Views/Disclosure.cs` instead of WinUI's `Expander`: an Expander's header is one toggle button, so a switch or
  Refresh button inside it would not be its own control for the keyboard and screen readers, and a collapsed
  Expander's content leaves the UI Automation tree. The chevron (name "Show …" / "Hide …") or a click on the card
  outside its controls opens it; closed, the body is collapsed and costs nothing.
- The detail line under a title says only state ("3 of 8 on · 72 games", "Uses less memory while it's closed"), never
  an explanation; explanations moved to tooltips.

### Settings (board 19)

- **Opening Binder:** How it opens (opens to the two mode pictures), Keyboard shortcut (a caution line only
  when the shortcut is taken), Notification-area icon, Hide when I click elsewhere, Stay ready (switch on the card;
  "While it's closed" inside), Start with Windows.
- **When a game starts:** Hide Binder (the delay), Come back when the game closes, Game mode › (its page).
- **Shelf:** Cover size, the "This week" switch.
- **Library:** Game sources (Refresh on the card, the eight checkboxes inside), Desktop shortcuts (Import… on the
  card, Restore inside), Add a game, Hidden games (the list inside), Free up space ›.
- **Window:** Position, Screen (with more than one monitor), Stay on the desktop layer.
- "Run first-time setup again" at the bottom.

### Game detail (board 21)

- Banner, title, Play row and the three stat tiles unchanged.
- **Activity:** one card with the playtime chart and the recent sessions (crash card and pattern line as before).
- **This game:** Hide delay, Game mode, Cover image, **Launch options** (closed: the engine and what's changed,
  e.g. "Unity · -monitor 2" or "Default"; inside: suggestions, extra arguments, "Starts as", Test launch / Reset,
  Start with, Process to watch) and **Anti-cheat** (closed: the names and "All set" or "N issues"; it opens by itself
  when something needs attention, unless the user opened or closed it for this game; the label under the title
  opens it and scrolls to it).
- Store page and Hide from shelf stay as links at the bottom (no decision to move them).

### Game mode toast (board 23, design B)

- A one-line pill centered above the taskbar: a 28 px dot and "**Game mode on** · 5 changes" / "· already set up"
  (accent dot, gauge glyph) or "**Back to normal** · 5 changes put back" (neutral dot, green check that draws itself).
  Closes after 4 s. The screen-reader text keeps the full sentence ("put back when the game closes").
- Something couldn't be changed or put back: a 380 px card instead, with the title ("Restored 4 of 5"), the names
  and "The log says what happened.", **Open the Game mode log** and a Dismiss (X) button. After a restore it stays
  until dismissed; after applying it closes after 6 s.
- Corners: Windows' own rounded window corners (8 px). The board's full capsule would need a transparent or clipped
  window, which loses the acrylic backdrop and draws jagged edges; left as a possible follow-up.

### The "back to normal" sound (board 23, sound 1 "Open fourths")

- Replaces Windows' notification sound. `Services/Chime` makes it in memory each time (~0.1 s on a background
  thread, ~2 MB of short-lived buffers): FM keys on A2, D3, G3, C4 rolled left to right, a low-pass closing from
  1100 to 320 Hz, a dark ping-pong echo and a 6 s hall (the board's noise impulse response, normalized like Web
  Audio's convolver, convolved by FFT). It follows the board's Web Audio graph sample by sample; rendered side by
  side, the levels match within 2–3 dB over the whole tail.
- `Services/Sound` plays it with `PlaySound(SND_MEMORY | SND_ASYNC | SND_SYSTEM)` from native memory at the system
  sounds volume, still skipped while `SHQueryUserNotificationState` holds notifications back. The process waits
  until it has played (8.5 s) before quitting when it was started only for the toast, then frees the buffer.
- Same setting as before ("Play a sound with the Restored confirmation"); turning it on plays it once.

### Touches

`SettingsView`, `DetailView` (+ `.Launch.cs`), new `Disclosure`, `Theme.xaml`, `GameModeToast`, new `Chime`,
`Sound`, `App` (sound lifetime), `GameModeView` (preview), `tests/smoke.ps1` (opens the Hidden games and Desktop
shortcuts cards before using their buttons). No new settings.

## 19. A softer "back to normal" sound and a modern cover hover (boards 25 and 26)

**Approved (owner, 2026-11-08):** the sound **Lullaby** (a) and the hover **C · Media tile** (b). Designs and
prototypes: canvas, "Still to build" page, board 25 (listen to each sound) and board 26 (hover, press, Tab). The
other variants below are kept as the record of how the choice was reached; build only the chosen ones.

### a. The "back to normal" sound: a breath, not a notification (board 25)

Goal: a sound that never gets annoying, even after hundreds of sessions or at 3 am.

**What can grate in today's sound** (§18, "Open fourths"; measured offline from the board's Web Audio graph,
BS.1770 K-weighting, loudest 400 ms, against `Windows Notify System Generic.wav`, the default notification sound):

| | Today | Windows' notification sound |
|---|---|---|
| Loudest moment | −26.4 LUFS (peak −18.6 dBFS) | −17.9 LUFS (peak −9.8 dBFS) |
| Audible (to −40 dB of peak) | 3.8 s, in an 8.5 s file | 1.1 s |
| Energy below ~150 Hz | 54 % | 32 % |
| Energy above ~2 kHz | 0.4 % | 1 % |

- **Length:** the hall tail is still sounding when the user is back at the desktop; in a quiet room it's the part
  that's heard. The widget process also waits 8.5 s for it before quitting.
- **Echo:** the ping-pong repeats every 0.29 s; repeats are rhythm, and rhythm asks for attention every time.
- **Bass:** A2 (110 Hz) carries half the energy. Laptop speakers lose it (so people raise the volume), headphones
  and subwoofers boom, and low notes are what carries through walls and floors at night.
- **Four-note roll:** a small melody, heard as an event each time.
- **The "tine":** the FM keys start with a short metallic shimmer: soft, but an edge, heard again every time.
- **Level:** −26.4 LUFS: fine by day, too loud at night.

**Direction (owner, 2026-11-08):** not modelled on Windows' sounds, and it must not sound like a notification at
all: "the most relaxing sound ever". Two earlier rounds (short struck tones, then pure gentle sines) were rejected
as still too much like a notification. What makes a sound read as a notification is its shape: something strikes,
then decays. So these have no strike: each one rises out of silence over most of a second, breathes slowly while
it's there and sinks back into silence. Longer (5 s) but very quiet, so it's felt more than heard.

**What all six do:**
- **No onset.** Every voice rises with a sin² curve (zero slope at both ends) over 0.7–1.4 s, then falls with
  `setTargetAtTime(0, τ)`. In C#: `peak · sin²(π/2 · t / attack)`, then `peak · e^(−(t − attack)/τ)`.
- **It breathes.** Each tone is two equal sines a fraction of a hertz apart (0.3–0.8 Hz, each at half the level),
  so it swells and softens by itself. The main low-pass opens a little as it rises and closes as it fades.
- **Air:** where used, soft brown noise (white noise integrated with a leak: `y = 0.985·y + 0.12·white`, seeded),
  through its own low-pass, with the same kind of swell.
- **Dark, warm and quiet.** Low mids only: filters peak at 0.6–1.4 kHz and close as it fades, §18's noise room
  (cut at 0.9–1.8 kHz), high-pass 90–120 Hz (Q 0.7 dB), no echo, no FM. **−32 LUFS** at its loudest (about 14 dB under Windows' notification sound), about −38 at night.
  The master fades linearly over the last 0.5 s of the file.
- Main low-pass Q 0.2 dB; frequency points are exponential ramps from the previous point.
- The same sound every time (identical sounds fade into the background; variation keeps them noticeable).

**Warm and friendly (latest round; Lullaby chosen).** The owner said the settling sounds (next round) "sound like
horror sounds". Why: tones fading in from silence like a ghost, a full slow wobble (two equal sines beating), very
muffled, and pitch falling (Resolve even slid down). These fix exactly that, still 2.0 s, −34 LUFS, −40 at night:
- a soft but defined start (sin², 0.10–0.15 s, like a felt key);
- a light shimmer only: `depth` 0.15 (the beating copy is 15 % of the pair instead of 50 %);
- a little warmth: a second harmonic at 0.10–0.15 (`partials [[1, 1], [2, 0.12]]`);
- less muffled: low-pass 1.6–1.8 kHz closing to 1 kHz, room 1.2 s, wet 0.30, pre-delay 20 ms, cut 2500 Hz;
- a clear major third, no pitch slides. High-pass 120 Hz for all.

- **Home.** One warm D major chord blooming almost together, low to high: "everything is in its
  place".
  - Voices (Hz, pan, level, start, attack, τ, beat, 2nd harmonic): D3 146.83, 0, 0.026, 0, 0.15 s, 0.50 s, 0.4 Hz,
    0.15 · A3 220.00, −0.20, 0.020, 30 ms, 0.14 s, 0.48 s, 0.45 Hz, 0.12 · F♯4 369.99, 0.20, 0.018, 60 ms, 0.13 s,
    0.45 s, 0.5 Hz, 0.12 · A4 440.00, 0.05, 0.011, 90 ms, 0.12 s, 0.42 s, 0.6 Hz, 0.10.
  - Low-pass 1600 → 1000 Hz over 2 s. Master 0.99. Measured: −34.0 LUFS, peak −26.2 dBFS, audible 1.9 s; night −40.0.
- **Warm settle.** Settle made warm: A4, F♯4, D4, D3 100 ms apart like soft felt keys.
  - A4 440.00, 0.25, 0.012, 0, 0.12 s, 0.32 s, 0.6 Hz, 0.12 · F♯4 369.99, −0.20, 0.018, 100 ms, 0.12 s, 0.36 s,
    0.5 Hz, 0.12 · D4 293.66, 0.10, 0.020, 200 ms, 0.13 s, 0.40 s, 0.45 Hz, 0.12 · D3 146.83, 0, 0.026, 300 ms,
    0.15 s, 0.48 s, 0.4 Hz, 0.15.
  - Low-pass 1800 → 1000 Hz over 2 s. Master 1.135. Measured: −34.0 LUFS, peak −28.5 dBFS; night −40.1.
- **Lullaby (chosen).** F♯4 then D4 (a falling major third) with a low D joining under the second note.
  - F♯4 369.99, 0.15, 0.022, 0, 0.10 s, 0.30 s, 0.5 Hz, 0.12 · D4 293.66, −0.15, 0.024, 250 ms, 0.10 s, 0.45 s,
    0.45 Hz, 0.12 · D3 146.83, 0, 0.018, 250 ms, 0.15 s, 0.50 s, 0.4 Hz, 0.15.
  - Low-pass 1800 → 1000 Hz over 2 s. Master 1.135. Measured: −34.0 LUFS, peak −25.9 dBFS; night −40.0.

**Made for "back to normal" (the round before; sounded eerie).** The owner found Soft glass didn't fit the message.
Glass light rises into a major seventh, which sounds like something opening or beginning; "Windows is back to
normal" means things coming down, settling, ending at rest. These keep Soft glass's sound (pure sines, slow
shimmer, dark, 2.0 s, −34 LUFS, −40 at night) but move downwards and end on a plain D major chord, with the low D
last or underneath. No major seventh. Engine additions: a voice can have a shimmer `depth` (default 0.5), and can `bend` (an exponential frequency ramp by a
ratio, starting at a time after its start, over a time), applied to both sines of its pair.

- **Settle.** Soft glass upside down: A, F♯, D drift in from the top and the low D arrives last.
  - Voices (Hz, pan, level, beat, start, attack, release τ): A4 440.00, 0.25, 0.010, 0.45 Hz, 0, 0.40 s, 0.28 s ·
    F♯4 369.99, −0.20, 0.014, 0.40 Hz, 120 ms, 0.40 s, 0.30 s · D4 293.66, 0.10, 0.020, 0.35 Hz, 240 ms, 0.45 s,
    0.32 s · D3 146.83, 0, 0.032, 0.25 Hz, 360 ms, 0.50 s, 0.38 s.
  - Low-pass 900 → 500 Hz over 2 s. Room 1.4 s, wet 0.40, pre-delay 30 ms, cut 1100 Hz. High-pass 120 Hz.
    Master 0.90. Measured: −34.0 LUFS, peak −28.9 dBFS, audible 1.9 s; night −40.2.
- **Resolve.** Dsus4 (D3, A3, D4, G4) together; after 0.55 s the G4 sinks to F♯4 over 0.3 s and the chord becomes
  D major: tension released.
  - D3 146.83, 0, 0.030, 0.25 Hz, τ 0.45 s · A3 220.00, −0.20, 0.022, 0.30 Hz, τ 0.42 s · D4 293.66, 0.20,
    0.018, 0.35 Hz, τ 0.40 s (all attack 0.50 s) · G4 392.00, 0.05, 0.013, 0.40 Hz, attack 0.45 s, τ 0.50 s,
    bend ratio 369.99 / 392.00 at 0.55 s over 0.3 s.
  - Low-pass 850 → 500 Hz over 2 s. Room 1.4 s, wet 0.40, pre-delay 30 ms, cut 1100 Hz. High-pass 120 Hz.
    Master 0.785. Measured: −34.0 LUFS, peak −27.1 dBFS, audible 1.8 s; night −40.0.
- **Sigh.** A4 then D4 (a falling fifth) over a soft D3: a relieved "ahh".
  - D3 146.83, 0, 0.030, 0.25 Hz, 0, 0.5 s, 0.40 s · A4 440.00, 0.20, 0.014, 0.45 Hz, 0, 0.3 s, 0.22 s ·
    D4 293.66, −0.10, 0.020, 0.35 Hz, 300 ms, 0.4 s, 0.38 s.
  - Low-pass 900 → 500 Hz over 2 s. Room 1.4 s, wet 0.40, pre-delay 30 ms, cut 1100 Hz. High-pass 120 Hz.
    Master 0.955. Measured: −34.0 LUFS, peak −30.0 dBFS, audible 1.8 s; night −40.3.

**Soft glass (the round before).** The owner liked Glass light (5 below) and asked for it softer, then
for about 2 s. Three versions, all **2.0 s** (audible 1.7–1.9 s) at **−34 LUFS** (2 dB under the others, −40 at
night). All five (or three) tones arrive within 0.4 s; the master fades over the last 0.5 s.

- **Soft glass.** Glass light's five tones, swelling in over about half a second, with a slow quiet
  shimmer and a small dark room.
  - Voices (Hz, pan, level, beat, start, attack, release τ): D3 146.83, 0, 0.030, 0.25 Hz, 0, 0.55 s, 0.35 s ·
    D4 293.66, −0.30, 0.020, 0.35 Hz, 60 ms, 0.45 s, 0.30 s · F♯4 369.99, 0.30, 0.014, 0.40 Hz, 180 ms, 0.45 s,
    0.28 s · A4 440.00, −0.12, 0.010, 0.45 Hz, 300 ms, 0.45 s, 0.26 s · C♯5 554.37, 0.15, 0.006, 0.50 Hz,
    420 ms, 0.45 s, 0.25 s.
  - Low-pass 900 → 550 Hz over 2 s. Room 1.4 s, wet 0.40, pre-delay 30 ms, cut 1200 Hz. High-pass 120 Hz.
    Master 0.87. Measured: −34.0 LUFS, peak −27.9 dBFS, audible 1.7 s; night −40.1.
- **Three glass.** Only D3, F♯4, A4: a plain warm major chord.
  - D3 146.83, 0, 0.030, 0.25 Hz, 0, 0.55 s, 0.32 s · F♯4 369.99, −0.25, 0.016, 0.35 Hz, 120 ms, 0.45 s, 0.28 s ·
    A4 440.00, 0.25, 0.011, 0.40 Hz, 250 ms, 0.45 s, 0.26 s.
  - Low-pass 800 → 500 Hz over 2 s. Room 1.3 s, wet 0.38, pre-delay 30 ms, cut 1100 Hz. High-pass 120 Hz.
    Master 1.035. Measured: −34.0 LUFS, peak −28.1 dBFS, audible 1.7 s; night −40.1.
- **Still glass.** Soft glass with no shimmer (each tone a single sine), attacks D3 0.6 s, the rest 0.5 s, releases
  as Soft glass. Low-pass 800 → 500 Hz over 2 s. Room 1.4 s, wet 0.40, pre-delay 30 ms, cut 1100 Hz. High-pass
  120 Hz. Master 0.767. Measured: −34.0 LUFS, peak −27.4 dBFS, audible 1.9 s; night −40.1.

The earlier rounds below stay for comparison.

**Calm variants**, ranked (measured offline at 48 kHz, BS.1770 K-weighting, loudest 400 ms). After the owner found
the first versions "way too bright", every variant is dark: filters peak at 0.6–1.4 kHz, rooms are cut at
0.9–1.8 kHz, and under 2 % of the energy is above 2 kHz.

1. **Exhale (recommended).** An open D chord that fades up from nothing over about a second, with a breath of soft
   air beneath it, then lets go slowly: a long, contented sigh.
   - Voices (Hz, pan, level, beat, start): D3 146.83, −0.30, 0.050, 0.35 Hz, 0 · A3 220.00, 0.25, 0.040, 0.5 Hz, 0 ·
     E4 329.63, −0.10, 0.026, 0.6 Hz, 80 ms · F♯4 369.99, 0.35, 0.015, 0.45 Hz, 150 ms.
   - Attack 0.9 / 0.95 / 1.0 / 1.05 s, release τ 0.9 / 0.85 / 0.8 / 0.75 s.
   - Air: level 0.05, attack 0.8 s, τ 0.7 s, its low-pass (Q 0.5 dB) 200 → 500 Hz at 0.8 s → 180 Hz at 4.0 s.
   - Low-pass 350 → 750 Hz at 0.9 s → 280 Hz at 4.5 s. Room 2.4 s, wet 0.35, pre-delay 30 ms, cut 900 Hz.
     High-pass 100 Hz. Master 0.76. File 5.0 s. Measured: −31.9 LUFS, peak −24.5 dBFS; night −38.4.
2. **Bowl.** A singing bowl on A that is rubbed, never struck: two partials bloom together and hum, wavering
   slowly. Deeply calm, though some will hear it as "spa" more than "all's well".
   - A3 220.00, centre, level 0.055, partials ×1 (1) and ×2.71 (0.12), each a pair 0.8 Hz apart; attack 0.7 s,
     τ 1.1 s.
   - Low-pass 700 → 400 Hz over 5 s. Room 2.2 s, wet 0.30, pre-delay 25 ms, cut 1000 Hz. High-pass 110 Hz.
     Master 0.92. File 5.5 s. Measured: −32.0 LUFS, peak −29.1 dBFS; night −37.4.
3. **Tide.** One slow wave: soft noise rising and drawing back like water reaching the sand, a faint fifth inside
   it. The most natural, but on small laptop speakers noise can turn into hiss.
   - Air: level 0.16, attack 1.2 s, τ 0.75 s, its low-pass (Q 0.7 dB) 180 → 600 Hz at 1.2 s → 400 at 2.2 s →
     150 at 4.6 s.
   - Voices: A3 220.00, −0.2, 0.022, 0.3 Hz, 200 ms, attack 1.3 s, τ 0.8 s · E4 329.63, 0.2, 0.014, 0.4 Hz,
     250 ms, attack 1.4 s, τ 0.75 s.
   - Low-pass 400 → 700 Hz at 1.2 s → 300 Hz at 4.5 s. Room 1.8 s, wet 0.20, pre-delay 20 ms, cut 900 Hz.
     High-pass 90 Hz. Master 0.38. File 5.0 s. Measured: −32.0 LUFS, peak −22.8 dBFS; night −38.9.

**Heavenly variants** (added at the owner's request, kept beside the three above): the same breath with more space,
a large soft room (3.5–4 s) and tones that arrive one after another. Still no strike, still −32 LUFS, and as dark
as the calm ones. Two engine additions: a voice can be an ensemble of detuned copies (`detunes` in cents) with a
vibrato (a sine LFO into each oscillator's frequency, ±cents), and can pass through parallel band-passes
(formants: frequency, Q in Web Audio's bandpass sense, gain).

4. **Choir.** A low, distant choir humming a dark "oo" on an open D chord, each voice entering after the last.
   - Voices (Hz, pan, vibrato rate, start): D3 146.83, −0.30, 4.6 Hz, 0 · F♯3 185.00, 0.30, 5.0 Hz, 100 ms ·
     A3 220.00, −0.10, 5.2 Hz, 200 ms · E4 329.63, 0.15, 4.4 Hz, 350 ms.
   - Each: three sawtooths at −7 / 0 / +7 cents, vibrato ±6 cents, formants 320 Hz (Q 4, gain 1) and 800 Hz
     (Q 6, 0.25); level 0.06 (E4 0.035); attack 1.2 / 1.25 / 1.3 / 1.4 s, τ 1.0 s.
   - Low-pass 900 → 600 Hz over 5 s. Room 4.0 s, wet 0.50, pre-delay 40 ms, cut 1500 Hz. High-pass 110 Hz.
     Master 1.82. File 5.5 s. Measured: −32.0 LUFS, peak −23.2 dBFS; night −38.1.
5. **Glass light (recommended of these).** A soft D underneath, then four pure tones (D, F♯, A, C♯: a major
   seventh) appearing a quarter second apart, each shimmering, in a wide soft room: light through frosted glass.
   - Voices (Hz, pan, level, beat, start): D3 146.83, 0, 0.030, 0.3 Hz, 0 (attack 1.0 s, τ 1.2 s) · D4 293.66,
     −0.35, 0.022, 0.5 Hz, 100 ms (0.7 s, τ 1.0 s) · F♯4 369.99, 0.35, 0.017, 0.6 Hz, 350 ms · A4 440.00, −0.15,
     0.013, 0.7 Hz, 600 ms · C♯5 554.37, 0.20, 0.009, 0.8 Hz, 850 ms (the last three 0.7 s, τ 0.9 s).
   - Low-pass 1400 → 900 Hz over 6 s. Room 4.0 s, wet 0.55, pre-delay 50 ms, cut 1800 Hz. High-pass 120 Hz.
     Master 1.59. File 6.0 s. Measured: −31.9 LUFS, peak −26.1 dBFS; night −38.2.
6. **Rising light.** A warm G chord that starts muffled and opens up like a door to a lit room; two softer tones
   shimmer in above it with a breath of air, then it settles.
   - Voices: G3 196.00, −0.30, 0.045, 0.3 Hz · D4 293.66, 0.25, 0.040, 0.45 Hz (both 1.4 s, τ 1.1 s) · B4 493.88,
     −0.05, 0.026, 0.5 Hz (1.5 s, τ 1.0 s) · A4 440.00, 0.30, 0.014, 0.6 Hz, 900 ms · D5 587.33, −0.30, 0.009,
     0.7 Hz, 1.1 s (both 1.2 s, τ 1.0 s).
   - Air: level 0.03, attack 1.5 s, τ 1.0 s, low-pass 300 → 1000 Hz at 2.0 s → 400 Hz at 5.0 s.
   - Low-pass 300 → 1300 Hz at 2.2 s → 600 Hz at 5.5 s. Room 3.5 s, wet 0.45, pre-delay 40 ms, cut 1600 Hz.
     High-pass 110 Hz. Master 1.06. File 6.0 s. Measured: −32.0 LUFS, peak −24.5 dBFS; night −37.6.

Earlier picks: Exhale if it should be calm and warm, Glass light if it should feel heavenly; now superseded by
Home above. A 4 s room at
22050 Hz keeps the FFT at 2^18, as today.

**Night**, for whichever variant: between 22:00 and 7:00 by the local clock, read once when the sound plays (no
timer, no setting): master × 0.5 (−6 dB), every filter point × 0.7, room wet × 0.6, high-pass at least 140 Hz.
"At night" buttons on board 25.

**Also silent when** Windows' own notification sound is off: `HKCU\AppEvents\Schemes\Apps\.Default\Notification.Default\.Current`
empty (the "No sounds" scheme or (None) chosen), read once at play time. As today, it's skipped while
`SHQueryUserNotificationState` holds notifications back. Check on the PC whether Windows 11's Do not disturb shows up
there; if it doesn't, leave it: there's no public API for it, and undocumented WNF state isn't worth reading.

**Implementing (when approved):** `Chime` becomes the swell engine above: voices as a small array (frequency,
pan, level, beat, start, attack, release, partials), an optional air layer (seeded brown noise through a
time-varying low-pass), the main low-pass points, the room (the same FFT convolution, shorter), a final high-pass
(`Biquad.SetHighpass`, Web Audio's formula) and `Render(bool night)`; FM, the glide and `Echo` go. `Seconds`
becomes 2.0 for Lullaby, so the process waits 2 s instead of 8.5 s; the WAV is 176 KB instead of 750 KB. Compare the C#
render with board 25's `OfflineAudioContext` render as for §18 (within 2–3 dB). `Sound` reads the clock and the
Notification.Default value. No new setting; the toggle stays.

### b. Cover hover, more modern (board 26)

Today (`Views/GameTile`): the cover jumps to scale 1.03, gets a 2 px accent outline, a 25 % black veil
(`CoverOverlayBrush`) and a centred accent "Play" pill; the (i) button appears. Instant, the same with animations
off. The veil darkens the art and the outline is loud, which modern media tiles avoid: Windows' own apps (Xbox,
Media Player, Photos, the Store) lift or lighten the tile, keep the art bright and put the action at the bottom or
corner, with Fluent's short control timings.

Shared by all three options: Fluent motion values from WinUI's theme resources, `ControlFasterAnimationDuration`
(83 ms) for fades and presses, `ControlFastAnimationDuration` (167 ms) for scale and lift, easing
`ControlFastOutSlowInKeySpline` (0,0,0,1). Implicit composition animations on the tile's own visual
(`ElementCompositionPreview.GetElementVisual`, `ImplicitAnimations` for Scale, Translation and Opacity with a
`CubicBezierEasingFunction`, or `UIElement.ScaleTransition` / `TranslationTransition` / `OpacityTransition` if their
built-in ease is close enough): they run only when a value changes, so idle cost stays zero. The XAML
`ScaleTransform` becomes `UIElement.Scale` with `CenterPoint` at the cover's centre (updated in `SizeChanged`).

- **A · Lift.** Today's look, eased: scale 1.00 → 1.04 in 167 ms, `ThemeShadow` with Translation Z 0 → 24, veil,
  outline and pill fade in over 83 ms. Press → 1.01 in 83 ms, release 167 ms. Least change, still the heaviest look.
- **B · Light edge.** Nothing moves: white 6 % over the art, a 1 px static edge gradient (135°, white 55 % → 8 % →
  4 %), a neutral pill (`SolidBackgroundFillColorBase` at 88 %, `ControlStrokeColorDefault` border), 83 ms fades;
  press: scale 0.98 and black 12 % instead of the light. Calmest, but the weakest "this is clickable". (The old
  pointer-following Reveal is gone from Windows 11, so it isn't copied.)
- **C · Media tile (chosen).** Lift as A (1.04, 167 ms, ThemeShadow Z 24, press 1.01), a 1 px inside edge
  (`CoverHoverEdgeBrush`: white 24 % dark, black 14 % light), no veil and no outline, a scrim on the bottom half
  only (transparent at 50 % → black 65 %, 83 ms), and a 36 px round Play button (Accent fill, glyph `F5B0` 14 px in
  `TextOnAccentFillColorPrimaryBrush`) bottom left, rising 4 px and fading in (167 ms / 83 ms). The (i) button stays
  bottom right, 28–30 px as today, so Play and Details sit together. Screen-reader names unchanged.

How it combines (C; A the same where it applies):
- **Keyboard focus** shows the hover state plus Windows' focus visual just outside the cover
  (`FocusVisualPrimaryBrush` 2 px, `FocusVisualSecondaryBrush` 1 px); returning from details still focuses without a
  ring after a click (programmatic focus).
- **Controller focus** shows the hover state plus today's PadRing (3 px, 4 px out, radius 12) and the bold title.
  1.04 is already the pad's scale, so hover and pad finally match. Pointer and pad on different covers: both show.
- **Shiny favourites (§13 d):** layers from the art up: art, sheen, scrim, edge, badges, Play, (i). The scrim covers
  only the bottom half, so the sheen stays visible where the pointer moves it. A favourite's light edge is the
  sheen's own edge, so C's hover edge is left off there. The sheen and the lift share one PointerEntered/Exited pair.
- **"Animation effects" off:** the implicit animations are removed (instant), the look is the same.
- **High contrast:** no shadow, scrim, sheen or light edge; a 2 px `SystemColorHighlightColor` outline and a Play
  button in Highlight / HighlightText.
- **List view** (`GameRow`) unchanged.
- **Cost:** one ThemeShadow at a time (only the hovered or focused tile is raised); check that the GridView's item
  container doesn't clip the shadow or the 4 % growth (the grid has 12 px gaps; the PadRing already needs 4 px).

### Touches (if approved)

a: `Services/Chime`, `Services/Sound`, `App` (wait time), `GameModeView` (preview); tests compare the render's
length and level. b: `Views/GameTile.xaml(.cs)`, `Themes/Theme.xaml` (`CoverScrimBrush`, `CoverHoverEdgeBrush`;
`CoverOverlayBrush` goes), `ShelfView` (item container clipping, if needed). No new settings.

## 20. Game mode: more items (board 24, milestone 14)

**Approved by the owner (2026-11-08) and built**, all four as recommended:
- Item 1 is the "Windows' own settings" card on the Game mode page (`Services/WindowsGaming`). Each row shows On/Off
  on its button, which opens the Settings page. The values are read when the page opens.
- Item 2 is "Pause OneDrive syncing", a switch in the services card (id `onedrive`, off by default, no admin rights).
  - The watcher journals the exe before it sends `/shutdown`, and waits up to 10 s.
  - The exe is kept in the journal even when OneDrive is still closing, so it's started again after the game. It
    isn't started again if it's already running.
  - OneDrive is left running when Documents is inside a OneDrive folder.
- Item 3 is a Close / Calm choice on each app row ("Apps while playing", `Settings.GameModeCalmApps`, watcher
  `--calm`).
  - Only `EXECUTION_SPEED` is set; the priority isn't changed.
  - Each process is journaled with its pid, start time and previous masks. Restore touches only that same process,
    and only if it's still in efficiency mode.
  - Never the game: the watcher drops paths inside its install folder, and the widget drops the game's own files.
    Windows' own programs, Store apps and Binder are never calmed either (`IsClosableApp`).
  - Tested live on 2026-11-08: one process went into efficiency mode, and the same process was restored.
- Item 4 is the Do not disturb row in that card.

Research for "what else could Game mode safely do" (2026-11-08). Only items that measurably help a game *and* can
be put back exactly passed; everything already in §11 (done or excluded) is not re-proposed unless there's new
evidence. Honest summary: **§11 already covers nearly everything that really helps.** What's left is small, so two
of the four picks don't change anything at all: they only *show* Windows' own gaming switches and link to them.

### Recommended, ranked

| # | Item | Benefit (source) | Admin | Restore | Risk | Suggested default |
|---|---|---|---|---|---|---|
| 1 | **"Windows' own gaming settings" card**: shows Windows Game Mode and "Optimizations for windowed games", each with its state and an "Open" link | Windows Game Mode: no driver installs or restart prompts from Windows Update during play, "more stable frame rate" depending on game ([Xbox Support][xbox-gm]). Windowed optimizations: DX10/11 windowed and borderless games move from blt to flip model, which lowers frame latency and enables VRR and Auto HDR ([Microsoft Support][winopt]) | No | Nothing to restore: read-only, Binder never changes them | None. Reads two undocumented HKCU values behind documented settings; if they can't be read, the row shows only "Open" | On (it's just a card) |
| 2 | **OneDrive: pause sync while playing** (closes OneDrive with its own `/shutdown`, reopens it with `/background`) | Stops sync hashing, disk I/O and downloads; the existing upload limit only slows what it *sends*. Helps only while OneDrive is actually syncing; no benchmark exists, so measure it on the PC | No | Journal "we closed it" + exe path, flushed before; reopen only if we closed it and it isn't running again. Crash: the normal journal recovery reopens it on the next start | `/shutdown` is community-documented, not on Microsoft Learn (flag). Online-only files can't open while it's off: a game whose saves are in a OneDrive-backed Documents folder with online-only saves would fail to load, so skip when Documents is redirected to OneDrive and not all local. Files changed during play sync afterwards | Off |
| 3 | **Calm chosen apps** (efficiency mode, the green leaf in Task Manager) instead of closing them | EcoQoS "always selects most efficient CPU frequency and schedules to efficient cores" ([Microsoft Learn, Quality of Service][qos]): on hybrid Intel CPUs it keeps those apps off the P-cores the game uses. Helps only when the app is busy (encoding, syncing, a heavy browser tab); on non-hybrid CPUs only that app's clocks drop | No (same user's processes) | `SetProcessInformation(ProcessPowerThrottling)`. Journal pid + start time + exe path + the previous state (`GetProcessInformation`, verify it returns what the app set itself) before; restore only that state, only on the same process, only if it's still EcoQoS. Crash: an app that is still running gets it back on the next start; one that closed has nothing to restore | Owner decision: §11 excluded "lowering other apps' priority". This is not priority (Task Manager's mode also sets Idle priority; we wouldn't), but it touches other apps. Never the game, its launcher, the "kept smooth" apps, Windows or security processes (`IsSystemApp`), or our own | Off, empty list |
| 4 | **Do not disturb**: a link row only | Windows turns DND on automatically "when playing a game" by default ([Microsoft Support, Focus][focus]) | No | Nothing | None | Link only |

Notes:
- **1 instead of toggling them per session:** both switches only affect games, so there's no reason to turn them
  off between sessions. Flipping them per game would add journal entries and crash recovery for no gain, and
  windowed optimizations must be set before the game creates its swap chain. Showing them keeps the user in charge
  and feels native (Settings › Gaming › Game Mode, `ms-settings:gaming-gamemode`; Settings › Display › Graphics,
  `ms-settings:display-advancedgraphics`). Read them when the Game mode page opens, never on a timer
  (`HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled`, missing = on; `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`
  `DirectXUserGlobalSettings` containing `SwapEffectUpgradeEnable=1`). On Windows 10, the windowed row hides.
- **4 has no new evidence for building more:** starting a focus session (`FocusSessionManager.TryStartFocusSession`)
  is a Limited Access Feature that needs a token from Microsoft ([Microsoft Learn][fsm]); reading it
  (`IsFocusActive`) is public but tells us nothing useful. So §11's "link to `ms-settings:notifications`" stands.
- **2 and 3 change the close-apps section**: OneDrive would appear as a row in "Close apps while playing" with its own
  method (today it's left open because it has no window), and 3 adds a third choice per app: Close / Calm.

### Rejected, and why

- **Transparency and visual effects:** in fullscreen and independent-flip (borderless with flip model) DWM isn't
  composing the game, so there's nothing to win; the windowed case is covered by item 1. Placebo.
- **Timer resolution:** since Windows 10 2004 `timeBeginPeriod` only affects the calling process
  ([Microsoft Learn][timer]); games set their own. A global setting would only cost battery.
- **Core parking / processor policy:** Best performance power mode is already there. Unparking by hand fights AMD
  X3D parking that keeps games on the cache CCD. Harmful, as §11 already says.
- **HAGS, MPO (`OverlayTestMode`), memory compression (`Disable-MMAgent`):** need a restart or driver reset, and
  are HKLM troubleshooting knobs, not gaming tweaks. MPO off is a flicker workaround that *adds* latency.
- **Disabling fullscreen optimizations** (the per-exe compatibility flag): Microsoft's DirectX team shows FSO
  gives exclusive-fullscreen performance with faster Alt+Tab ([DirectX blog][fso]); turning it off can break VRR
  and HDR. No benefit to measure.
- **Standby list cleaning, working-set trimming of other apps:** the pages fault back in; placebo or harmful.
- **Nagle, interrupt moderation, `NetworkThrottlingIndex`:** most game traffic is UDP and games set
  `TCP_NODELAY` themselves; adapter settings reset the network link. Already excluded in §11; no new evidence.
- **Mouse acceleration ("Enhance pointer precision"):** not a performance setting, and games that use raw input
  never see pointer ballistics ([Microsoft Learn][rawinput]). Changing it would change how the desktop feels.
- **Pausing Windows Update / active hours:** undocumented HKLM values; Windows Game Mode already blocks driver
  installs and restart prompts; the bandwidth limit (§11) handles the network part. Services stay excluded.
- **Delivery Optimization peer upload:** uploading to Internet peers is off by default (local network only); the
  existing limit covers downloads. Nothing to add.
- **Defender scan schedule, Automatic Maintenance:** scheduled scans start only when the PC isn't in use
  (`ScanOnlyIfIdleEnabled`, on by default) and maintenance waits for idle ([Microsoft Learn][maint]). Nothing runs
  during play to pause; touching Defender stays excluded.
- **Steam / Epic / Battle.net downloads:** Steam pauses during games by default; the others have no API, only
  their own settings (already pointed to in §11).
- **Browser background tabs:** browsers already throttle background tabs; Edge even puts tabs to sleep when a PC
  game starts ("PC gaming boost", [Microsoft][edge]). Item 3 covers a browser the user wants calmer anyway.
- **RGB / vendor utilities (iCUE, Armoury Crate…):** a real source of DPC spikes on some PCs, but they're tray apps
  with no window to close politely, and ending them is excluded. Users can add them to "Close apps" where they do
  have a window.

### Not decided (noticed on the way)

- **"Use the high-performance GPU" per game** on laptops with two GPUs (`UserGpuPreferences` `GpuPreference=2;`,
  the per-app Graphics setting). A real, large win when Windows picks the integrated GPU, but it's a permanent
  per-game preference, so it belongs in Game detail, not Game mode.

[xbox-gm]: https://support.xbox.com/help/games-apps/game-setup-and-play/use-game-mode-gaming-on-pc
[winopt]: https://support.microsoft.com/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952
[qos]: https://learn.microsoft.com/windows/win32/procthread/quality-of-service
[focus]: https://support.microsoft.com/topic/81ed1b25-809b-741d-549c-7696474d15d3
[fsm]: https://learn.microsoft.com/uwp/api/windows.ui.shell.focussessionmanager.trystartfocussession
[timer]: https://learn.microsoft.com/windows/win32/api/timeapi/nf-timeapi-timebeginperiod
[fso]: https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/
[rawinput]: https://learn.microsoft.com/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement
[maint]: https://learn.microsoft.com/windows/win32/taskschd/task-maintenence
[edge]: https://www.microsoft.com/edge/features/pc-gaming-performance

## 21. Open a launcher (milestone 22, no board yet)

After the shortcut import, the launchers' own icons (Steam, Epic, …) are often the last game shortcuts left on the
desktop. People still open them for game reasons: updates, installing something new, the store, friends. Binder
can open them, so the desktop can be cleared completely. Two entry points, both menus, nothing new on the shelf.

### a. Filter chip menu
- Right-clicking a launcher's filter chip (or Shift+F10 / the Menu key on it) opens a small `MenuFlyout` with one
  item: "Open Steam" (the launcher's own name, with its icon from `LauncherIcons`). Built when it opens.
- A normal click still only filters the grid.
- All, Favorites, Recent and Other get no menu. Launchers under "More" get no entry here; the tray menu covers them.
- Screen readers: the chip's help text says "Right-click to open Steam".

### b. Tray menu
- The tray menu (§16 b) gets the launchers that have games, in the chip order, as their own group before Settings:

  ```
  Open Binder
  ───────────
  Game mode
  Free up space…
  ───────────
  Open Steam
  Open Epic Games Launcher
  Open EA app
  ───────────
  Settings
  Quit
  ```
- Only launchers that have games on the shelf and whose launcher is installed (`LauncherIcons.LauncherExe` returns a
  path). None: no launcher group and no extra separator.
- Works while the panel is closed: the game list stays in memory in "Stay ready", so the item doesn't open the panel
  or load any view. Plain text items, like the rest of the native menu.

### How a launcher is opened
- One helper, e.g. `GameActions.OpenLauncher(GameSource)`, used by both menus and by Free up space's "open the
  launcher" step (§12) instead of its own copy.
- Target: `LauncherIcons.LauncherExe(source)`, started with `UseShellExecute` and no arguments. Xbox opens its
  `shell:AppsFolder` item.
- **Steam opens with its window,** i.e. `steam.exe` without `-silent`. The user asked for the launcher here, unlike a
  game launch.
- When the launcher is already running, starting its exe again should bring its window to the front. **Verify on the
  real PC** for each installed launcher; if one starts a second copy or does nothing, note it here.
  - Steam (2026-11-11): comes to the front, the second start hands over and exits.
- The panel behaves as after any other outside action (in Flyout mode it closes on click-away as usual). No toast.
- Display names: one table shared with `GameActions.LauncherName` (Steam, Epic Games Launcher, GOG Galaxy, EA app,
  Ubisoft Connect, Xbox app, Rockstar Games Launcher, Hytale Launcher).

### Security
- Only the paths `LauncherExe` already returns: Steam's from `SteamPath`, Rockstar's and Hytale's from their own
  lookups, the others from the program registered for the launcher's link (`HKCR\{scheme}\shell\open\command`),
  which is what Windows runs for those links anyway. The file must exist and end in `.exe`; no arguments.
- Nothing runs until the user picks the item.

### Not chosen
- A launcher row or tiles on the shelf: permanent space taken from the grid for something used far less than games.
- Deep links into launchers (store pages, friends, downloads): that turns Binder into a front-end for them.

### Touches
`ShelfView` (chip menu), `App.ShowTrayMenu`, `GameActions`, `LauncherIcons`, `FreeSpaceView` (reuse the helper).
No new settings. Cost: nothing while idle; the items are built when a menu opens (a few registry reads).

## 22. Faster than Start, and showing why (milestone 23, no board yet)

The question this answers: "why not just press Windows and search for the game?" If you know the game, Start
search is good. Binder wins in three ways it doesn't show today:
- **It finds every game.** Start only finds games that made a Start menu shortcut; Epic, EA, Ubisoft and GOG games
  often don't, and Steam only when that box was ticked.
- **It helps when you don't know what to play:** covers, Continue playing, Recent, Favorites.
- **It handles the session, not only the launch:** Steam started silently, the widget gone while you play and back
  afterwards, Game mode, playtime, crash help.

So: make the launch itself at least as fast as Start (a, b), meet people in Start search (c), and show those
advantages with the user's own numbers, at the moment they matter (d, e).

### a. Type, Enter, playing

- Today Enter in the search box only moves focus to the first cover, so it takes Enter twice. Enter in the search
  box now **plays the top match**. Down still moves into the grid.
- Matching (pure code, run per keystroke over the in-memory list):
  1. The name starts with the query ("bal" → Baldur's Gate 3).
  2. A word in the name starts with it ("gate").
  3. **Initials:** the query matches the first letters of the words, digits included ("bg3" → Baldur's Gate 3,
     "rdr2" → Red Dead Redemption 2; a leading "The" is skipped, as in `CoverColors.Initials`).
  4. Anywhere in the name (today's rule).
  Within a rank, the most recently played first, then the name. Case and accents are ignored (culture-aware, as
  the Name sort).
- The top match gets a quiet "Enter to play" hint (a small `KeyboardAccelerator`-style label on its tile, or the
  row in List), so the shortcut is discoverable. Screen readers: the search box's help text says "Enter plays the
  first result".
- Results are games only: no web results, apps or settings, which is the difference from Start.
- Tests: the ranking and initials rules are unit-tested (`Library/GameSearch`, no UI).

### b. One keystroke to your last game

- When the panel opens and nothing has been typed yet, **Enter plays the "Continue playing" game** (the slot's Play
  button has focus semantics without stealing the search box's focus: typing still searches).
- So the usual case is Win+Shift+G, Enter. The search box's placeholder says so while it's empty: "Search 72
  games · Enter plays Hades II".
- Only when the slot shows Continue playing (or Welcome back). With This week showing, or no game played yet,
  Enter does nothing until something is typed.

### c. Your games in Start search (optional)

Instead of competing with Start, put the games there, launched through Binder.
- A setting, off by default and offered in first-run setup (next to the numbers in d): **"Add my games to Start
  search"**.
- Shortcuts go only into our own folder, `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Binder games\`, one
  `.lnk` per game, written with the Shell API (`IShellLinkW` + `IPersistFile.Save`, the same interfaces
  `ShortcutFile` already loads).
- Each shortcut runs **`Binder.exe --play <gameId>`**, so a launch from Start gets everything Binder adds:
  silent Steam, the watcher, Game mode, playtime, crash help. Its icon is the game's icon (`SteamSource.IconFor`,
  the exe, or the fallback cover's icon saved as `.ico` in our cache).
- **`--play <id>`:** the id must match a game in `library.json`; anything else is ignored. It plays the game
  exactly as the Play button does, without opening the panel (the launch toast is skipped; the hide delay doesn't
  apply since nothing is shown). Through the single-instance forwarding when Binder is already running.
- Only for games **without** a Start entry of their own (checked as in d), so Start search never shows a game
  twice. Existing Start entries are never touched.
- Kept in step on every rescan: added, renamed or removed with the library (hidden games are removed too). Turning
  the setting off deletes the folder; uninstall deletes it too. Only files in that folder are ever written or
  deleted, never through links (the files-we-own rule).
- Cost: nothing while idle; written only on rescan when something changed.

### d. Show the difference with the user's own numbers

- **First-run setup, step 1** ("We found N games"): one line under the count, "23 of them aren't in Start search",
  when that's more than zero. Worked out once, then, by reading the Start menu folders (the user's and the shared
  `Programs` folders, bounded depth and file count, `.lnk` and `.url` read with `ShortcutFile`, never run) and
  matching targets to games by install folder, exe or store link. No result is stored.
- **After the desktop import:** the toast says "Your desktop has 14 fewer icons · Undo" (Undo = Restore to
  desktop, as today).
- **Welcome back, once:** the first Welcome back after setup adds one line, "Binder was closed the whole time
  you played", so the zero-cost promise is seen once. Not repeated.
- Plain statements only; no badges, scores or "you saved" counters.

### e. Say what Play does

- The cover's hover Play mark gets a short tooltip that names the real difference for that game, where there is
  one: Steam "Plays without opening Steam's window", any game with Game mode on "Plays with Game mode", otherwise
  just "Play". One line, from facts the game already has.

### Not chosen

- Opening the panel when a controller is picked up: XInput can't notify, so it would need constant polling, which
  breaks "zero cost while idle".
- "Surprise me" and other discovery features: nice, but they don't answer "why not Start?" (it stays under "Not
  decided").
- A "you saved N clicks" or similar counter: noise.

### Touches

New `Library/GameSearch` (ranking, initials; tested), `ShelfView` (Enter, hint, placeholder), `Program`/`App`
(`--play`), new `Services/StartMenuGames` (read Start entries for d; write our folder for c), `SetupView`,
`MainWindow` (import toast), `Moments` (the one-time line), `GameTile` (tooltip), `Settings` (one setting), the
installer's uninstall step. Security rules in `CLAUDE.md`: add the Start menu folder to the files we own, and
`--play` to "only launch validated library entries".

## 23. Lean speed-ups (milestone 24, no board yet)

Six small changes that remove a step or a surprise, and pass the bloat test: nothing runs while idle, no new page,
and where possible a setting or a line of text goes away instead of one being added.

### a. Hide when the game appears

- Today the panel hides after a fixed countdown (`Settings.HideDelaySeconds`, default 10 s, per game
  `Game.HideDelaySeconds`), whatever the game is doing: too early while Steam is still updating, too late for a game
  that's up in 3 s.
- New: the watcher tells the widget the moment it finds the game's process, the same way it reports Game mode
  today (`StartWidget("--applied", …)`): it starts `Binder.exe --detected <gameId>`, which the single instance
  forwards to the running widget, or which exits at once when none is running. The widget then ends the countdown
  and exits, as when the countdown ends today.
- The countdown stays as the **upper limit** (if detection takes longer, the panel still hides then), and Keep open
  still cancels everything. The toast reads "Starting [Game]… hides when it opens".
- A `--detected` for another game, or after Keep open, is ignored.
- **What it removes:** the hide delay setting becomes "When the game opens (default) / Instantly", with a per-game
  override only for "Instantly". The 5 / 10 / 30 s choices go. Existing values migrate to the new default.

### b. Recent games in the tray menu

- The tray menu gets the last three games played (by our sessions), as its first group under "Open Binder":
  "Play Hades II", "Play Baldur's Gate 3", … Choosing one plays it exactly as the Play button does, without opening
  the panel.
- Built when the menu opens from the in-memory library and sessions, like the Game mode item. None played yet: no
  group and no extra separator.
- This resolves "A taskbar jump list with recent games" from "Not decided": the same reach, with the tray icon
  Binder already has, no taskbar pin needed.

### c. Open on the screen with the mouse

- The flyout opens on the screen chosen in Settings (`Settings.Monitor`). With several screens, the Screen choice
  gets one more option, **"Where the mouse is"** (the new default for new installs, and for anyone who never chose a
  screen): the monitor under the cursor when the shortcut or tray icon opens it (`Monitors.FromPoint`).
- The desktop-docked mode keeps its fixed screen.

### d. "Needs an update" on Steam covers

- Steam's `appmanifest_*.acf` `StateFlags` already says when an update is pending (`SteamSource` reads it today
  only to skip uninstalled games). **Verify the bits on the real PC** (update required, update queued or running)
  before relying on them.
- Kept as `Game.UpdatePending` during the existing scan; no extra reads.
- The cover gets a small badge chip (bottom-left, the `BadgeBrush` style, a Fluent download/sync glyph) and the
  Play tooltip says "Steam will update this first". Its screen-reader name includes "update pending".
- Steam only for now; other launchers only where their local data says the same just as cheaply.

### e. "New" on recently installed games

- A small "New" label on the cover for games first seen in the last 7 days (`Game.AddedAt`, already recorded) and
  not played yet. It goes away when the game is played or the 7 days pass. No filter chip; "Recently added" in the
  sort menu already exists.
- The very first scan (setup) marks nothing as new.

### f. Retire teaching text that's no longer needed

- The footer hint ("Hides 10 s after a game starts · comes back when it closes", shown until three sessions are
  recorded) goes once a lands: the toast's "hides when it opens" teaches the same thing at the moment it matters.
- The empty-search placeholder from §22 b ("Enter plays Hades II") already teaches Enter; no extra hint is added for
  it.
- Rule for later features: when a feature explains itself, remove the help text it replaces in the same milestone.

### Not chosen

- Starting Steam ahead of time when the panel opens: work done just in case.
- Number keys to play favorites: they clash with typing names that contain digits.
- An Explorer right-click "Add to Binder": a shell extension; drag and drop onto the panel already adds games.
- Ratings, tags or notes: a library manager, not a launcher.

### Touches

Watcher `Program.cs` (`--detected`), widget `Program`/`App` (forwarding, ignore rules), `MainWindow.Launch`
(countdown as the limit), `Settings`/`Game` (hide delay migration, `UpdatePending`), `SettingsView`/`DetailView`
(fewer delay choices, Screen option), `App.ShowTrayMenu` (recent group), `MainWindow.Flyout` (mouse screen),
`SteamSource` (flags), `GameTile`/`GameRow` (update chip, "New"), `ShelfView` (footer removed). Unit tests for the
flag parsing, the "New" rule and the delay migration.

---

## 24. Weekly chart details (milestone 25, proposed, no board yet)

The "This week" chart in the shelf's top slot shows only a total per day ("Monday: 2 h 35 min"); which games were
played isn't visible. **Proposed, not approved yet:** build only after the owner's go.

### Bar tooltips

```
Monday · 2 h 35 min
────────────────────────────────
▮ Elden Ring            1 h 50 min
▮ Hades                     35 min
▮ Balatro                   10 min
  2 more · 10 min
```

- Title: the day's full name and its total (as now).
- Up to 3 games, longest first: a small accent mark, the name, the time right-aligned; beyond that one line
  "N more · time" (`Strings.Plural`).
- A day without play keeps the one-line tooltip ("Tuesday · not played").
- The card's line ("This week · 4 games · most: …") gets the week's top 3 in the same layout as its tooltip.
- Today's day letter in primary text, later days stay tertiary and empty: where the week stands, at a glance.
- Screen readers: each bar reads "Monday, 2 h 35 min, mostly Elden Ring" (one name per bar, not one long chart
  name).

### Cost

- `PlaytimeStats.Week` already makes one pass over the sessions and the 7 days; it also keeps each day's time per
  game id (`Dictionary<string, TimeSpan>[7]`), still one pass. Names through a lookup built once (id → game).
- The tooltip content is plain WinUI (a small Grid per bar), built when the panel gets its data; nothing while idle.
- About 3 new strings (EN, DE, FR, with comments).

### Optional, same pattern

Detail's per-game charts (weeks, months): the tooltip adds the number of sessions, "Week of 3 Nov · 4 h 10 min ·
3 sessions".

### Left out

- Stacked bars coloured per game: 16 px wide and 44 px tall bars in a fixed 168 px slot can't carry 3–4 colours
  or a legend, and the chart stays single-accent.
- Clicking a bar to open the day: a second view in the slot for what hovering already answers.

### To decide (owner)

1. Hidden games: proposed to show them in the tooltips (it's the user's own history; leaving them out makes the
   totals look wrong). Today the "most played" line skips them.
2. Games no longer in the library: the name from `removed-games.json` where kept, else "A removed game".

## 25. Updates from GitHub (milestone 26, approved 2026-11-14, boards 28 and 29)

Binder finds, downloads and installs its own updates from the public GitHub repository's releases. **Approved**
(owner's go, 2026-11-14), being built in the order under "Build order" 26. Today the app never contacts anything to update (README,
`docs/installer.md`, the website's FAQ); approving this changes that rule (see "Rules that change").

### Goals

- **Tamper-proof:** an update runs only if it carries a signature from the owner's own key, which never lives on
  GitHub. Taking over the GitHub account, a token, the release workflow or the connection is not enough.
- **Zero cost while idle or gaming:** no timer, no background service, no scheduled task. The check is one small
  request when the panel opens, at most once a day. During a game the widget doesn't exist, so nothing updates.
- **Nothing new to trust on the PC:** no elevation, no new process that stays running, no package. The installer
  that's already there (`BinderSetup.exe /S`) does the install.

### What an attacker would need (threat model)

| Attack | What stops it |
|---|---|
| Takes over the owner's GitHub account or a token, publishes a release | The release has no valid signature: the signing key isn't on GitHub. The app ignores it |
| Changes the release workflow, or an action it uses | The sign script checks the build's attestation (which workflow, which commit) and refuses; actions are pinned to commit hashes |
| Replaces a file in a published release | Immutable releases (repo setting) block it; the setup's hash is in the signed manifest anyway |
| Intercepts the connection (proxy, DNS, public Wi-Fi) | HTTPS, plus the signature: any changed byte fails |
| Serves an older, genuinely signed release (downgrade) | Never installs a version at or below the installed one, or a manifest older than the last one accepted |
| Swaps the downloaded file between the check and the start | The file is held open without write or delete sharing from the hash check until the setup has started |
| Steals one YubiKey and its PIN, marks a release urgent | Urgent needs both YubiKeys; with one signature it's a normal update the user is asked about |
| Steals the owner's signing key | It can't be copied off the YubiKey; a stolen YubiKey needs its PIN (locks after 3 tries) and a touch. The second YubiKey signs a release that drops the lost key |
| Malware already running as the user | Out of scope: it could change the installed files directly (true for every per-user app). The updater never elevates, so it adds no way up |

### The signed manifest

Every release carries three files: `BinderSetup-<version>.exe`, `release.json` and `release.json.sig`.

```json
{
  "format": 1,
  "product": "Binder",
  "channel": "stable",
  "version": "0.2.0",
  "issued": "2026-11-20T18:04:00Z",
  "commit": "<40 hex digits, the tagged commit>",
  "setup": { "size": 31457280, "sha256": "<64 hex digits>" },
  "urgent": false,
  "notes": { "en": ["…", "…"], "de": ["…"], "fr": ["…"] }
}
```

- **Signature:** ECDSA P-384 with SHA-384 (built into .NET and Windows CNG, works on Windows 10, no package),
  over the exact bytes of `release.json` prefixed with the fixed text `Binder release manifest v1` and a zero byte
  (so the key can't be tricked into signing anything else that verifies). `release.json.sig` holds the 96-byte
  signature (IEEE P1363) in base64, one per line: one line normally, two (one per YubiKey) for an urgent update. Ed25519 isn't in .NET 10 or CNG, and writing our own crypto is out.
- **Keys in the app:** two public keys compiled in: the owner's main key and the offline backup key. Either one
  verifies. They and the repository the app checks are set at build time (owner's go, 2026-11-15), never read at
  run time: `build/OfficialRelease.props` holds the official values, which only an official build uses
  (`publish.ps1 -Official`, run by `release.yml`); `build/Updates.targets` checks them (owner/name, one or two P-384
  keys) and writes them into the widget as `Services.UpdateKeys`. Any other build (a local build, CI's test build,
  a fork) has no keys and never checks: Settings › About says "This build doesn't check for updates", without the
  Updates choice or the GitHub link. A fork either builds with its own `-Repository` and `-UpdateKeys`, or puts its
  own values in `OfficialRelease.props` to run `release.yml` and `sign-release.ps1` for itself; `publish.ps1
  -Official` refuses to build in another repository's GitHub Actions with values that aren't its own. The props
  and targets files are on `AGENTS.md`'s "Don't touch" list and `check.ps1`'s protected list.
- **Verify first, then parse:** the signature is checked on the raw bytes before anything reads the JSON.
  `release.json` is capped at 64 KB, the signature file at 1 KB.
- **Then, strictly** (`Library/UpdateManifest`, pure logic, unit-tested): `format` 1, `product` and `channel` exactly
  as above, `version` three whole numbers, `issued` a UTC time not more than a day in the future, `commit` 40 hex
  digits, `size` between 1 MB and 200 MB, `sha256` 64 hex digits; notes at most 6 lines of at most 200 characters per
  language, plain text (shown in a TextBlock, never as markup or links). Anything else is rejected; unknown fields
  are ignored.
- **The download address isn't in the manifest:** the app builds it from the version alone,
  `https://github.com/<repository>/releases/download/v<version>/BinderSetup-<version>.exe` (the build's
  repository, `JannisJostBot/binder` for the official releases).

### Urgent security updates (owner's request, 2026-11-12)

A release can be marked `"urgent": true` for a security fix that shouldn't wait for the user's click.

- **What changes for the user:** with "Ask before installing", an urgent update downloads as soon as the check finds
  it and installs the next time the panel closes, as with "Install automatically". Never while the panel is open,
  never while a watcher tracks a game. Afterwards the pill says "Security update installed · 0.2.1" with **What's
  new**. "Don't check" still means no network at all, so nothing is ever forced on those users.
- **Two keys:** `urgent` counts only if `release.json.sig` has valid signatures from **both** YubiKeys. With one
  signature the release is still a normal update (the user is asked). So a single stolen or misused YubiKey can never
  install anything on its own; it would need both YubiKeys and both PINs. For an urgent release the owner fetches
  YubiKey 2 from where it's kept; `sign-release.ps1 -Urgent` asks for both in turn.
- **Notes must say why:** the script refuses an urgent release whose notes don't start with a line for it (EN, DE,
  FR), e.g. "Fixes a security problem in shortcut import." That line shows in the pill's What's new.
- **Not a lever for anything else:** urgent doesn't skip any check (signature, version, `issued`, size, hash), doesn't
  bring back a skipped version faster than the next check, and doesn't change the check's schedule (once a day, when
  the panel opens).
- **Settings:** the "Ask before installing" choice gets the tooltip "Security fixes still install when you close
  Binder".

### Making a release

1. **Prepare (owner):** raise `<Version>` in `Directory.Build.props` and write the notes in
   `docs/releases/<version>.json` (`en`, `de`, `fr`, same rules as the app's text: short, plain, no selling; an
   urgent release adds `"urgent": { "en": …, "de": …, "fr": … }`, the line the script puts first; see
   `docs/releases/README.md`).
   Merge to `main` through a pull request with CI green.
2. **Tag (owner):** `git tag v0.2.0` on that commit of `main`, push it. A tag ruleset lets only the owner create
   `v*` tags and forbids moving or deleting them.
3. **Build (GitHub Actions, `.github/workflows/release.yml`, on `v*` tags only):**
   - Refuses unless the tag equals `<Version>`, the commit is on `main`, and `docs/releases/<version>.json` exists.
   - `publish.ps1`, the unit tests, the UI smoke test and the setup test, as in CI.
   - `actions/attest-build-provenance` for `BinderSetup.exe` (a Sigstore attestation naming the repository,
     workflow and commit).
   - Creates a **draft** release `v0.2.0` with `BinderSetup-0.2.0.exe` only.
   - Permissions only `contents: write`, `id-token: write`, `attestations: write`; every action pinned to a commit
     hash; no secrets; no `pull_request_target`. The workflow has no signing key and can't publish.
4. **Sign (owner's PC, `scripts/sign-release.ps1 -Version 0.2.0`):**
   - Downloads the draft's setup with `gh`, runs `gh attestation verify` (repository, `release.yml`, the tag's
     commit), computes its SHA-256 and size.
   - Writes `release.json` itself from what it checked plus the notes file at the tagged commit; nothing from CI's
     side goes into the manifest unchecked.
   - Shows version, commit, size, hash and the English notes and asks to confirm.
   - Checks VirusTotal's release check (step 6) in the draft's notes: it must be there, for this very file, and
     "passed". "Blocked" stops it unless `-OverrideScan "<reason>"` (20 to 300 characters, confirmed by typing
     "override"); the reason goes into the published notes above the VirusTotal section.
   - Signs with the main key and checks the signature with the public keys in `build/OfficialRelease.props` at the
     tagged commit before uploading (it reads the repository from there too).
   - Uploads `release.json` and `release.json.sig` to the draft and publishes it as the latest release.
5. **Website numbers (GitHub Actions, `.github/workflows/site-numbers.yml`, on a published release):** builds the
   tag, runs `main`'s UI smoke test on it (so older tags are measured the same way; it also measures the open panel
   with 100 games after scrolling through every cover), writes its memory and processor numbers into the website's "How much it uses"
   (`scripts/site-numbers.ps1`: only the text between the page's `<!-- n:… -->` markers; stops when a number is
   missing or contradicts the page's "None") and opens a pull request `Website: numbers for v…`; the site goes live
   from `main` once it's merged. Memory is Task Manager's value (private working set) on GitHub's Windows test
   machine with the smoke test's 9 games and with 100, and the page says so. Needs "Allow GitHub Actions to create and approve
   pull requests" in the repository's Actions settings; without it the branch is pushed and the run warns. Can be
   run by hand for a tag (Actions › Website numbers › Run workflow).
6. **Published:** with "immutable releases" on, the release's files and tag can't change any more. A bad release is
   fixed by the next version, never by replacing files (the app never downgrades).
6. **VirusTotal (automatic, `.github/workflows/virustotal.yml`), twice:**
   - **Before signing:** when `release.yml` has made the draft (up to about 40 minutes; sign after it). It downloads
     the draft's setup, checks it against GitHub's hash of the upload, uploads it to VirusTotal, waits for the result
     and adds a "VirusTotal" section to the draft's notes: scan date, how many engines flagged it, report link,
     hash, and the **release check**, plus a `<!-- release-check: passed|blocked sha256=… -->` line that step 4 reads.
     Blocked when any major engine flags it (Microsoft, Kaspersky, ESET-NOD32, BitDefender, Sophos, Symantec, Avast,
     AVG, Malwarebytes, Trend Micro, McAfee, CrowdStrike) or 3 or more engines of any kind do; then the run fails,
     naming the engines in its log. A false positive: report it to the vendor and run the workflow again later, or
     override in step 4 with a reason. Below the limits, findings are a warning on the run (0.2.1: Bkav alone).
   - **After publishing:** scanned again, since engines change their verdicts; the section is replaced (also checked
     against `release.json`). Blocked now: the run fails and opens an issue (people may already have it), unless
     it was published with an override for that same file.
   The notes name no engines (one false positive named there reads worse than it is). It runs only with the
   repository secret `VT_API_KEY` (a free VirusTotal account's key); `release.yml` itself stays without secrets.
   Earlier tags or a rescan: Actions › VirusTotal › Run workflow.
7. **Website (after publishing):** `site/index.html`'s "Download for Windows" button links to the published file,
   `https://github.com/JannisJostBot/binder/releases/download/v<version>/BinderSetup-<version>.exe`, and the line
   under it says `Version <version> · <size> MB` (size in MiB, rounded; the sign script prints both at the end).
   Change both in one commit to `main` once the release is out, never before: until then the link would be a 404.
   The live page doesn't wait for this: `site/release.js` asks GitHub's API for the latest release when the page
   opens and shows its link, version, size and the VirusTotal section of its notes ("not scanned yet" until step 6
   has written it). The values in `index.html` are the fallback when GitHub can't be reached, so update them too,
   including the VirusTotal box once step 6 has finished.
   Merging it publishes the site (`.github/workflows/pages.yml`).
   An older link keeps working (releases are immutable) and the app updates itself from there. There's no
   `releases/latest/download/BinderSetup.exe`: the setup's file name carries its version.

### The keys (owner: two YubiKeys, 2026-11-12)

The owner has two YubiKeys. Each holds one signing key in its PIV smart card part, made on the key itself, so the
private key never exists anywhere else and can't be copied off it.

- **Needs:** YubiKey 5 series (PIV with ECC P-384). The blue "Security Key" series has only FIDO, no PIV: check
  with `ykman info` ("PIV: Enabled"). Firmware before 5.7 has the EUCLEAK side channel on ECDSA, which needs the
  YubiKey in the attacker's hands plus lab equipment; acceptable if the keys stay with the owner, worth knowing.
- **Setup, once per YubiKey (owner's PC, Yubico's `ykman`, a tool for the owner only, never part of the app):**
  - change the PIV PIN, PUK and management key from the defaults;
  - generate an ECC P-384 key in slot 9c (Digital Signature) with PIN policy "always" and touch policy "always", and
    a self-signed certificate for it (Windows finds the key through the certificate);
  - `scripts/sign-release.ps1 -ExportPublicKey` writes its public key for `build/OfficialRelease.props`.
- **Main key (YubiKey 1):** kept by the owner, used for every release. Signing goes through Windows' own smart card
  support (the certificate in CurrentUser\My, `ECDsa` from it, no Yubico software needed at signing time): the
  script asks for the PIN and the YubiKey blinks for a touch, for each release.
- **Backup key (YubiKey 2):** set up the same way, then stored apart from the PC and YubiKey 1 (another room or
  place). Used only if YubiKey 1 is lost, broken or stolen. Its PIN and PUK written down and kept apart from it.
- **Lost or stolen YubiKey:** without its PIN it locks after 3 wrong tries; still, the next release is signed with
  the other YubiKey and drops the lost key from `OfficialRelease.props`, then a new key is set up on a replacement. Both lost
  at once: users would have to install a new setup by hand once (the app can't be told to trust a new key).
- **Rotation:** a release signed with the old key ships the new public key; the release after that drops the old
  one.
- **GitHub account:** both YubiKeys registered as passkeys / security keys for the owner's sign-in (their FIDO part,
  separate from PIV), branch protection on `main` (no force push, pull requests with CI), the tag ruleset, immutable
  releases, Dependabot for the workflow's actions.
- Never in GitHub secrets, never in the repository, never in a file on the PC.

### In the app

- **When it checks:** when the panel opens, after the first frame (like the rescan), if the last check was over
  24 hours ago; and from Settings' **Check for updates**. Never on a timer, never in `--background` start-up before
  the panel has been opened, never while a watcher is tracking a game.
- **What it fetches:** `https://github.com/<repository>/releases/latest/download/release.json` and
  `.sig` (about 2 KB). Redirects are followed only over HTTPS and only to `github.com`,
  `objects.githubusercontent.com` and `release-assets.githubusercontent.com`. 15 s timeout. No GitHub API, no
  account, no cookies, a plain `User-Agent: Binder/<version>`.
- **What it keeps** (`settings.json`): the last check's time, the highest `issued` accepted, and a version the user
  skipped. The verified manifest itself goes to `%LOCALAPPDATA%\Binder\updates\release.json` with its signature,
  and is verified again whenever it's read back.
- **Download:** when the user presses **Update**, or at once with "Install automatically". To
  `%LOCALAPPDATA%\Binder\updates\BinderSetup-<version>.exe.part`, hashed while it streams, stopped past the
  manifest's size, then compared with `sha256` and renamed. One download at a time; a progress bar only while it
  runs. Cancelled or failed: the part file is deleted; the next try starts over (no ranges). Closing the panel
  cancels it (nothing downloads while the panel is closed or a game runs).
- **Start the setup:** the verified file stays open with read sharing only (no write, no delete) until
  `CreateProcess` has returned, so nothing can swap it in between. If an Authenticode signature is present on the
  installed `Binder.exe`, the new setup must carry the same publisher (`WinVerifyTrust`); unsigned today, so this
  only starts to matter if the owner gets a certificate.
  - Arguments: `/S /update` (new: restart the widget with `--updated` afterwards, in the tray or on the desktop as
    before). The setup's own payload check (`payload.txt` hashes) still runs as today.
  - The widget exits right after starting it; the setup's `--quit` wait then finds nothing to close.
- **When it installs:** "Ask before installing": when the user presses **Restart now** (the panel closes, comes
  back updated). "Install automatically": the next time the panel closes (Stay ready) or at sign-in, never while it's
  open, never while a watcher tracks a game.
- **After:** the panel shows the pill (board 23's style) once: "Updated to 0.2.0 · What's new", which opens the
  About card with the notes. The updates folder is emptied at that start.
- **Failures:**
  - No connection, timeout, HTTP error: the About card says "Couldn't check · no connection"; no toast; next try
    at the next panel open after 24 hours.
  - Bad signature, unparsable manifest, older `issued`: as above, plus a line in `error.log`. Never retried for the
    same file within the 24 hours.
  - Hash or size mismatch: the file is deleted, the card says "The download couldn't be verified. It was deleted.
    Nothing was changed." with **Try again**. Logged.
  - Setup exit code other than 0: the setup logs to `%TEMP%\BinderSetup.log` as today; the old version keeps
    running (the setup restarts it); the card says "The update couldn't be installed" with the log's place in its
    tooltip.

### Game mode's admin helper after an update (owner's question, 2026-11-12)

The helper in `%ProgramFiles%\Binder\` is only ever replaced through a UAC prompt ("Update…"), never by the
setup or the updater. So after an update that bumps `ElevatedPart.HelperVersion`, the PC runs a **new watcher with
the old helper** until the user clicks Update…. What the code does today (checked 2026-11-12):

- **The old helper keeps working.** `GameMode.ApplyElevated` only checks that a marker exists
  (`AllowedVersion` not null), not its number. The new watcher writes its request and runs the old helper's task.
- **Unknown parts are skipped, nothing breaks.** `ElevatedRequest.Parse` ignores unknown keys and service ids, so an
  old helper applies what it knows and leaves out newer items. A value it doesn't accept (e.g. a new upload limit)
  makes it refuse the whole request: nothing is applied and the pill reports "Services and network" as failed.
- **Restore always matches apply.** The same installed exe applies, journals and restores, so its changes are put
  back by the code that made them. No mixed state.
- **The user is told.** The shelf shows "Game mode needs an update" with Update… (`HelperNeedsUpdate`), and Settings ›
  Game mode offers Update….

Two gaps, fixed in this milestone:

1. **A helper change that is a safety fix keeps the old behaviour until Update….** Helper version 3, for example,
   stopped limiting uploads of services and security software; a version 2 helper would go on doing it. New:
   `ElevatedPart.MinimumHelperVersion` (bumped by hand together with `HelperVersion` when the change is a safety
   fix). A marker below it: the watcher **skips the admin items** (logs it, the pill counts them as "needs an
   update"), the shelf notice says "Game mode needs an update to use its admin items". Restore of a journal that's
   already there still goes through the installed helper, whatever its version, so nothing stays changed.
   - Limit: the old helper and its tasks stay on the PC until Update… or Remove it; a program running as the user
     could still start its tasks. Only a UAC prompt can replace it, and the updater never asks for one. So such a
     release's notes say so, and the shelf notice stays until it's done.
2. **Update… while changes are applied.** `Install.Run` replaces the exe without first putting back a pending
   `journal.json`, so a newer helper could later meet an older journal. New: Update… restores this user's pending
   journal first, as `Remove` already does (the journal format stays readable by later helpers too; `Version` in it
   only ever gains optional fields).

For the updater itself:

- `release.json` gets `"helperVersion": 3`. When it's above the installed marker's number, What's new gets a fixed
  line first: "Game mode will ask for administrator rights once after this update." (only for users who allowed the
  helper). An update is never held back for this: the app updates, the helper waits for the user's click.
- Never installs while a Game mode journal exists (either one): it waits for the restore, like it waits for a game.
- Urgent updates can't fix the helper on their own (that needs a UAC prompt); with `MinimumHelperVersion` raised
  they at least stop the new watcher from using the old helper.

### Settings and UI (board 28)

- **Settings, new group "About" at the bottom:**
  - Card "Binder 0.1.4", second line the state ("Up to date · checked today at 14:02", "0.2.0 is available",
    "Downloading · 12 of 31 MB", "Ready to install"), button **Check for updates**, or **Update** / **Restart now**
    (accent). A `Disclosure` opens "What's new" with the notes when an update is known.
  - Card "Updates" with a combo: **Install automatically**, **Ask before installing** (default),
    **Don't check**. Tooltip: "Checks GitHub once a day when Binder opens".
- **The shelf:** no banner, nothing in the top slot, no pill. While an update is known (with "Ask before
  installing"; not after "Skip this version"):
  - an 8 px accent dot at the top right of the header's "…" button, as browsers mark an update on theirs. Its
    screen-reader name becomes "More actions, update available"; high contrast uses the system highlight color.
    This is the one dot on "…": the owner turned one down for Game mode on 2026-11-11 because that is a state, not
    something waiting; an update is something waiting, so the dot fits here (owner, 2026-11-12);
  - a first item "Update to 0.2.0" ("Restart to update" once downloaded) in that menu and in the tray menu, then a
    separator. Choosing it starts the download and opens Settings › About to show it.
  - With "Install automatically" there's no dot: it installs the next time the panel closes.
- **First-run setup:** no new step (the default is set; About shows it).
- About 20 new strings (EN, DE, FR, with comments).

### Cost

- Idle and gaming: nothing (no timer, no service, no task, no watcher change).
- A check: two HTTPS requests of about 1 KB, once a day at most, only when the panel opens. `HttpClient` is
  created for it and released after (as `CoverCache` does). Covered by the 0% idle rule since it's tied to opening.
- The download: about 30 MB, only after **Update** or with "Install automatically".

### Rules that change (on approval)

- `CLAUDE.md` network rule: add "HTTPS GETs to `github.com` (and its release download hosts) for this repository's
  release manifest and setup, signed with the owner's key; never anything about the user or the PC".
- `CLAUDE.md` security rules, a new "Updates" bullet: signature before parsing, keys compiled in, never downgrade,
  hash before start, file held open, never elevate, delete only inside `%LOCALAPPDATA%\Binder\updates\`.
- `README.md`, `docs/installer.md` ("The app never checks for updates") and the website's FAQ ("It doesn't check
  for updates") are rewritten to say what it checks and how to turn it off.
- `AGENTS.md` / `check.ps1`: `UpdateKeys.cs`, `Updater.cs`, `UpdateManifest.cs`, `scripts/sign-release.ps1` and
  `.github/workflows/release.yml` protected.

### Not chosen

- **Signing the manifest in GitHub Actions** (key in a secret, release environment with approval): simpler, but a
  compromised GitHub account would then hold the key too.
- **Verifying Sigstore attestations in the app:** needs a certificate-transparency and Fulcio client, far too heavy
  for the rule on dependencies; the owner's signature covers it, and the attestation is checked at signing.
- **Patches / deltas, a separate updater process, a Windows service or scheduled task, MSIX/App Installer, the
  GitHub API:** more parts and more to trust, for a 30 MB download a few times a year.
- **Beta channel:** `channel` exists in the manifest so one can be added later; not now.
- **Forcing updates or a minimum version:** the user can always stay on "Don't check".

### Touches

New `Library/UpdateManifest` (parse and rules, tested), `Services/UpdateKeys`, `Services/Updater` (fetch, verify,
download, start), `ViewModels/SettingsViewModel` (About group, states; tested with the spy), `SettingsView` (About
cards), `ShelfView` / `App.ShowTrayMenu` (menu item, dot), `MainWindow` (check after first frame, install on close,
pill after `--updated`), `Models/Settings` (three fields), the setup's `Program`/`Installer` (`/update`), new
`.github/workflows/release.yml`, `scripts/sign-release.ps1` (`-ExportPublicKey`, signing through the YubiKey), `docs/releases/`. Unit tests:
signature (good, changed byte, wrong key, backup key), manifest rules, downgrade and `issued` checks, the
download's size and hash checks against a local file.

### To decide (owner)

1. ~~Default for "Updates"~~: decided 2026-11-14, **Ask before installing**.
2. ~~Main key~~: decided 2026-11-12, the owner's two YubiKeys (main and backup, see "The keys"). To check: both
   are YubiKey 5 series (`ykman info`), and their firmware version.
3. ~~Write and verify every file before swapping any~~: decided 2026-11-15, **yes** (`Binder.Setup/FileSwap.cs`): all
   files are written and checked first, then swapped in; a failed swap puts back what was already swapped.

## 26. Game shortcuts (milestone 27, boards 35–37)

The owner's idea (2026-11-18): the panel's shortcut plus a number starts a game without opening the shelf. Built
with option A of board 35 and a 1.5 s wait (both picked by Claude for a first build; the owner may still change them).

### Keys
- **Hold the shortcut's modifiers, tap its letter, tap a number** (Win+Shift+G, 3). Windows uses Win+number with
  every modifier for the taskbar, and Ctrl+Alt+number is AltGr on German and French keyboards, so the number can't be
  a hotkey of its own.
- The shortcut's `WM_HOTKEY` starts a wait (`MessageWindow.GameKeys.cs`) only while its modifiers are still down: a
  low-level keyboard hook (`WH_KEYBOARD_LL`) that takes only 0–9 (number row and pad; with Shift held Windows hands the
  pad's keys over as Insert, End, arrows, … and lifts Shift around them with a scan code marked 0x200, which isn't a
  release). Letting the modifiers go without a number opens the panel as before. The hook exists only during that wait
  and while the flash waits (for Esc and another number); its callback only records the key and returns, the work runs
  after.
- **Held for 1 s** (owner, 2026-11-18): instead of opening the panel, the list of what each number plays shows where
  the flash does (`GameKeyFlash`'s list: "Press a number · let go to open Binder", 0 and its last game, then the
  numbers given, or how to give one). A number plays as above; letting go closes it and opens the panel; Esc closes
  it and opens nothing. At most 15 s, should the keys' release never reach the hook.
- 0 is always the last game played on the shelf (`GameKeys.LastPlayedGame`); 1–9 are the user's
  (`Settings.GameKeySlots`, number → game id, checked on every use). `Settings.GameKeysOn`, on by default.

### The flash (board 35, A)
- `Views/GameKeyFlash`: the game's banner (Steam's hero and logo via `BannerLogo`, else the cover beside the name on
  the cover's color) over a strip "Starting · Esc to cancel" with the keys pressed, centered above the taskbar of the
  screen with the mouse, without focus. A bar empties over `GameKeys.Wait` (1.5 s).
- Esc: "Cancelled · [Game] didn't start", gone after 1.2 s. The same number again starts at once; another number
  (modifiers still held) switches. The panel opening cancels the wait.
- After the wait the game plays as `--play` does (`App.PlayFromStart`): no panel, the watcher, Game mode.
- A number without a game: the pill "Nothing on 4 · right-click a game in Binder", or "No game played yet" for 0.
- Animations off: no rise and fade; the bar still empties (it shows time). High contrast: no art, system colors.

### Giving a game a number (boards 36, 37)
- The cover's right-click menu: "Shortcut ›" with None and 1–9, each naming the game that has it.
- Game detail: a Shortcut card under "This game" (second line "Win+Shift+G, 3").
- Settings › Opening Binder: "Game shortcuts" with a switch and a list of 0–9 (0 fixed, 1–9 a list of the shelf's
  games).
- Dragging a cover or row on the shelf raises the numbers 0–9 over the bottom of the grid; dropping it on one gives
  the game that number (0 refuses). The drag carries only our own format.
- A number that's taken moves to the new game; the toast says which game has none now. A game that leaves the
  library frees its number.

### Not chosen
- More than 10 slots, letters, or a confirmation: search (Win+Shift+G, type, Enter) covers the rest.
- A badge on covers: the number shows in Game detail and Settings only.

### Touches
`Library/GameKeys` (tested), `MessageWindow.GameKeys.cs`, `App.GameKeys.cs`, `Views/GameKeyFlash`,
`MainWindow.GameKeys.cs`, `ViewModels/GameKeyChoices` and `SettingsViewModel.GameKeys.cs` (tested), `GameMenu`,
`DetailView`, `SettingsView`, `ShelfView.GameKeys.cs`, `site/docs.html` (what the keyboard hook does).

## 27. Native ARM64 build (milestone 28, proposed 2026-11-17, no board)

Binder ships for x64 only. Windows 11 on Arm runs it through emulation (Prism), so it works there today, but every
start pays for the translation and the emulated watcher sits in memory while a game runs. A native `win-arm64`
build removes that. **Proposed, not approved yet:** build only after the owner's go; it changes "Windows 10/11 x64"
in `CLAUDE.md`.

### What carries over unchanged (checked 2026-11-17)

- WinUI (`Microsoft.WindowsAppSDK.WinUI`), Native AOT and .NET 10 all support `win-arm64`; no new package.
- No code depends on x64: the `WOW6432Node` registry paths are the same on Arm64; the helper's VARIANT is 24 bytes
  on every 64-bit Windows; XInput, power plans, QoS, WMI and the scheduled tasks exist on Windows on Arm.
- The watcher matches games by their exe's path, so it follows emulated x64 games as well as native ones.

### Build

- The three `.csproj` files take the runtime from the build instead of fixing `win-x64`; `publish.ps1` gets
  `-Runtime win-x64|win-arm64` and builds into `publish\<runtime>\`.
- Cross-compiled on GitHub's x64 Windows runners (Native AOT for Arm64 with MSVC's Arm64 tools; nothing runs).
- Tests: the unit tests are architecture-free. The smoke and setup tests need an Arm64 machine: GitHub's
  `windows-11-arm` runners (free for public repositories) or the owner's own Arm PC. An Arm64 build isn't released
  until both have passed on Arm64 at least once.

### Two setups, not one

- `BinderSetup-<version>.exe` (x64, as now) and `BinderSetup-arm64-<version>.exe`. One setup carrying both apps would
  double every download and update (~35 → ~70 MB) for everyone; the brief prefers the lighter way.
- An x64 setup started on an Arm64 PC (Windows 11 runs it) installs the x64 app as now; switching is a new install
  from the Arm64 setup, which replaces the files in place (same folder, same data).

### Updates (§25)

- The signed manifest gains an optional `setupArm64` object (`size`, `sha256`), checked like `setup`. The parser
  already ignores unknown fields, so installed x64 copies keep working unchanged.
- An Arm64 copy (`RuntimeInformation.ProcessArchitecture`) reads `setupArm64`, builds its address from the version
  alone (`BinderSetup-arm64-<version>.exe`) and refuses a manifest without it ("no update for this PC yet"), never
  falling back to the x64 setup.
- `release.yml` builds both, attests both; `sign-release.ps1` checks both attestations and writes both entries.
  `UpdateManifest` unit tests for: missing entry on Arm64, a bad entry, x64 ignoring it.

### Website

- The script-free part of the page can't tell the processor (browsers on Windows on Arm usually report x64), so it
  offers both: the main button "Download for Windows" (x64), under it a plain link "Download for Arm-based PCs".
- The line under them: "Most PCs need the first one. To check: Settings › System › About › System type says
  'x64-based processor' or 'ARM-based processor'." (Windows' own words.)
- `release.js` fills both links and sizes from the release's assets, as it does for the one setup now.

### Cost

- No change for x64 users. Release builds take about twice as long; two files to sign per release.

### To decide (owner)

1. Go or no go, and whether the first Arm64 release waits for a `windows-11-arm` CI run (public repository) or a
   test on an Arm PC.
2. Windows 10 on Arm (end of support October 2025): proposed not to support it; it can't run the x64 setup either.

## 28. End a stuck game (milestone 29, board 40, approved 2026-11-19)

A fullscreen game hangs and Task Manager can't get in front of it. Holding Binder's shortcut ends it.

### What the user does
- During a game, hold the shortcut (Win+Shift+G by default). After 0.3 s a ring with an X appears in the middle of
  the game's monitor and fills over **2 s**; full, the game ends. Letting go earlier drains it ("Cancelled · [game] is
  still running"); a tap shows nothing.
- Settings › When a game starts › **End a stuck game**: **When it stops responding** (default) / **Always** / **Off**.
  Second line "Hold Win+Shift+G"; "Needs a keyboard shortcut" (disabled) when the shortcut is off.
- **When it stops responding**: when the ring would appear, the game counts as stuck only if one of its top-level
  windows is hung (`IsHungAppWindow`) or a System32 `WerFault.exe` holds one of its processes (parent, or `-p <pid>`
  on its command line). Not stuck: a pill "[game] is responding · nothing was ended" instead of the ring. Checked
  again when the ring is full ("[game] is responding again · nothing was ended").
- Afterwards, with **Come back when the game closes** on, the shelf opens with a card: "[game] crashed after …" (its
  crash event or error report; the usual diagnosis), "[game] stopped responding after …" (hung), or, only possible with
  Always, a neutral "You ended [game] after … · It was still responding, so it isn't counted as a crash." Play again
  on all. With Come back off nothing opens and the card isn't shown later. Never restarted by "Restart if it crashes".

### How it works (watcher)
- `--end hung|always --end-key <modifiers>,<key> --end-text <seven lines>` from the widget (the texts translated,
  with the game's name: the watcher has no resources). Missing or invalid: off.
- The system events window (SystemEvents.cs) registers the shortcut once the game is found (`RegisterHotKey`, no hook).
  The widget still running (countdown, Keep open) has it: the watcher waits on that widget's process and takes it
  when it exits. A widget that starts meanwhile and can't register asks the watchers to let go (`WatcherKey`,
  `WatcherNames.ReleaseKey`), and the watcher takes it again once that widget has exited. Its loop is
  `MsgWaitForMultipleObjectsEx`: no polling.
- `WM_HOTKEY` with the keys really held starts a 30 ms timer that reads the keys (`GetAsyncKeyState`) and draws the
  ring; it stops when they're let go or the ring is done. With Animation effects off, quarters and no drain.
- The ring (`RingWindow`): a layered, top-most, click-through, non-activating window; GDI+ into a premultiplied DIB;
  per-monitor DPI; dark/light from the app mode, system colors in high contrast; created when shown, destroyed (and
  GDI+ shut down) after. In exclusive fullscreen (`SHQueryUserNotificationState`) the game's windows are minimized
  while it shows and restored on letting go.
- Ending: `TerminateProcess` (exit code 1) of the processes the watcher follows, each checked by id and start time;
  the error report first. The session line gets `EndedByUser`, `Hung`, `ErrorReport`; no restart grace, no crash
  restart.

### Not chosen (board 40)
- The "press again" fallback for a game running as administrator (if Windows hides its key state): to add only if
  testing shows it's needed. Event-based hang detection (shell hook, Event Log subscription): later, if wanted.

## Build order (after milestone 7)

8. **Quick wins:** *(Done.)*
   - App icon (§1).
   - Launcher icons (§3).
   - Fallback covers (§2).
   - Sort, Favorites chip, cover size / list view (§7).
9. **Window behavior:** flyout, tray icon, shortcut, stay-ready footprint (§5) and first-run setup (§6). *(Done.)*
   Measure and report.
10. **Detail additions:** playtime chart and weekly card (§7, §9), launch options, anti-cheat (§9). *(Done.)*
11. **Crash detection** (§10). *(Done.)*
12. **More launchers** (§8). Verify each one on the real PC. *(EA app, Ubisoft Connect, Xbox app and Rockstar done;
    Battle.net, Riot, Amazon, itch, Humble and Meta to do.)*
13. **Controller** (§4). *(Built; tried briefly with a real pad.)*
14. **Game mode:**
    - First the no-admin part: power, priority, apps, Game Bar, journal, toast.
    - Then the elevated helper, the installer step, the services and the network items.
    - Update the security rules in `CLAUDE.md` in the same milestone.
    *(Both parts built; see the table.)*
15. **Free up space** (§12). *(Done.)*
16. **Look and moments** (§13): logo on banners first (the others reuse it), then the detail animation, "Welcome
    back" with milestones, and shiny favourites. Check the sheen against the prototype on the real PC. *(Built; the
    detail animation was removed.)*
17. **Advanced: sources and artwork** (§14): move the defaults into `SourceDefaults` first (no behaviour change,
    covered by the existing tests), then the settings page and the "Check" buttons. Update the network rule in
    `CLAUDE.md`. *(Built.)*
18. **Performance** (§15): the WinUI patch first, then pre-scaled covers, deferred loading and the AOT settings, each
    measured on its own. CsWinRT 3.0 when it's stable.
19. **Quick actions** (§16): the header menu first, then the tray items. *(Done.)*
20. **Restart after a crash** (§17): the in-a-row count and the toggle first, then the toast, then the watcher's
    relaunch with "Come back" off. *(Built.)*
21. **Softer sound and cover hover** (§19): the Lullaby sound (compare the C# render with board 25's), then the
    Media tile hover; check both on the real PC (speakers and headphones; mouse, keyboard and controller). *(Built.)*
22. **Open a launcher** (§21): the shared helper first (Free up space switches to it), then the tray group, then
    the chip menu. Verify each installed launcher on the real PC. *(Done.)*
23. **Faster than Start** (§22): a and b first (search ranking with tests, then Enter), then d and e, then c
    (`--play` first, then the Start folder and the setting). Count the "not in Start search" games on the real PC
    and compare with what Start actually finds.
24. **Lean speed-ups** (§23): a first (with the footer removal in f), then d (check the Steam flags on the real PC
    first), then b, e and c.
25. **Weekly chart details** (§24), once approved: `PlaytimeStats.Week` per day and game with tests, then the bar
    tooltips and their automation names, then the card line's top 3, then (optionally) Detail's session counts.
26. **Updates from GitHub** (§25): the keys and `scripts/sign-release.ps1` first, then
    `UpdateManifest` with tests, then `release.yml` with a dry run (a draft that's signed but never
    published, checked by the unit tests' verifier), then the app's check, About cards and download, then install on close and the
    pill. Update the rules in `CLAUDE.md`, the README, `docs/installer.md` and the site in the same milestone.
    *(Built; 0.2.0 and 0.2.1 released.)*
27. **Game shortcuts** (§26): first check on the real PC that the hook gets the number while Win+Shift is held; then
    the flash, Esc, and the three ways to give a number. *(Built.)*

## Not decided (don't build)

Suggested but neither approved nor rejected yet:
- Collections / tags
- Showing uninstalled games
- Emulator entries
- Merging duplicates
- Run something before or after a game. Refined form for voice chat (suggested instead of a strip of app
  buttons on the shelf, which fails the scope rule: the panel exits ~10 s after Play, Windows already pins apps,
  and a permanent strip would shrink the grid):
  - One setting, "Also open with games": Discord, TeamSpeak, Mumble (shown only when detected, reusing Game
    mode's "kept smooth" detection, §11) plus "Add an app…" (file picker). Optionally per game under "This game
    only".
  - Runs once when Play is pressed, only if the app isn't already running, minimized where the app supports it
    (Discord: `Update.exe --processStart Discord.exe --process-start-args --start-minimized`). No new shelf UI, no
    idle cost.
  - Paths go through the security rules (detected install folders validated, user-picked exes only).
- "Surprise me"
- Pin to Start / desktop shortcut from a cover
- Free up space, faster size entry (§12): fill the size from a pending install the launcher has written to disk
  (Steam/Epic, check what Steam writes first), a "Paste" button that reads "Storage: 150 GB" from copied store
  text, and quick picks (50/100/150/200 GB, last used)
- Importing playtime from Steam
- Session notes
- Refresh rate / HDR per game
- Audio device per game
- A battery check for Best performance
- Backup / restore
- Portable mode (likely dropped, since it conflicts with the installer)
- A Windows 11 Widgets-board card
- "Use the high-performance GPU" per game (noticed in §20; it would belong in Game detail, not Game mode)
