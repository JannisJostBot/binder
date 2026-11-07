# Planned features (approved by the owner)

Status: **approved specs. Milestones 8–11 and 15 are built; 12 is in progress; 13, 14, 16, 17 and 18 are still to do** (see the progress table in §0).
- **Designs:** on the design canvas, https://claude.ai/artifact/9tZEP6HSrETxwGwpSbMg1Q. Pages by area ("Shelf",
  "Game detail", "Window, setup & settings"), plus "Still to build" with the open milestones in build order. Board
  numbers (1–17) are given in each section below and stay in the board titles, with "built" or the milestone.
- **Baseline:** written against the code at `0eacc65` (branch `milestone-6-sources`: milestones 1–6 done).

Every item follows the project brief: zero cost while idle or gaming, no third-party packages, nothing that can
break the user's system.

---

## 0. Read this first (for the session that implements this)

### Progress (keep this table current)

| Milestone | Sections | Status |
|---|---|---|
| 8 Quick wins | §1 app icon, §2 fallback covers, §3 launcher icons, §7 sort / Favorites chip / cover size | Done |
| 9 Window behavior | §5 flyout, tray, shortcut, stay ready; §6 first-run setup | Done |
| 10 Detail additions | §7 "This week", §9 playtime chart, launch options, anti-cheat | Done. "This week" was later merged with "Continue playing" into one switchable slot (board 16, see `CLAUDE.md`) |
| 11 Crash detection | §10 | Done. With "Come back" off the crash card shows the next time the panel opens instead of a toast (Game mode's "Restored" toast doesn't exist yet); memory is the installed amount (`GetPhysicallyInstalledSystemMemory`) |
| 12 More launchers | §8 | **In progress.** Done: EA app, Ubisoft Connect, Xbox app (tile logo as the cover picture) and Rockstar (tested with synthetic data only, no Rockstar game installed). Not yet: Battle.net, Riot, Amazon, itch, Humble and Meta |
| 13 Controller | §4 | To do |
| 14 Game mode | §11 | **In progress.** Done, the no-admin part: power mode (overlay, else the High performance plan), Above normal priority, Game Bar background recording, closing chosen apps (WM_CLOSE to visible windows, ~5 s, else left open), the write-ahead journal with recovery on the widget's start and "Restore now", the Game mode toast ("Game mode is on" after applying, while the widget is still up, and "Windows is back to normal" after the restore; animated badge) and the Settings page with the per-game Default / On / Off. The watcher applies and restores (the widget is gone while the game runs); journal, log and last result are in `%LOCALAPPDATA%\GameShelf\`. Tested on the real PC only where it was already at High performance with Game Bar recording off (left as is), so the switch-and-restore path is covered by unit tests, not yet live. Built, the admin part: `GameShelf.Elevated.exe` (Native AOT), installed to Program Files by Settings › Game mode › Allow… (one UAC prompt; admin accounts only) with per-user tasks `GameShelf Apply/Restore <SID>`; the four allowlisted services; QoS through WMI over COM in the ActiveStore (DSCP 46 for the game, found voice chat apps and user-added ones; upload throttle 1/5/20 Mbit/s); Windows Update limited to 10% for background downloads (`DOPercentageMaxBackgroundBandwidth`, restored exactly); journal and log in an admin-only `%ProgramData%\GameShelf\`; "Update…" when the helper's version differs; "Remove it" and removal on uninstall; security rules in `CLAUDE.md`. Game Bar's row moved to the services section. Unit-tested (request and journal parsing); **not yet tested live** with admin rights (the install needs the owner's UAC approval): check that the tasks register with their security descriptor, the user can `schtasks /run` them, QoS accepts the `PolicyStore` context value, and Delivery Optimization picks up the policy without a restart |
| 15 Free up space | §12 | Done (built ahead of 13 and 14, owner's choice). Steam's `LastPlayed` verified on the real PC (Unix seconds, 0 = never). Mid-uninstall the plan survives the flyout closing (memory only; with "Stay ready" off it's lost). Games without a known size (Xbox, GOG, some Ubisoft) are listed under the map, never suggested |
| 16 Look and moments | §13 | To do |
| 17 Advanced: sources and artwork | §14 | To do |
| 18 Performance | §15 | To do |

Also built since the baseline, outside these milestones (already in `CLAUDE.md`): native radii, click-to-play covers
with a Details (i) button, greeting header, the switchable top slot, silent Steam start.

### Already in the code: don't rebuild

- Favorites (`Game.Favorite`, star in Detail, pinned first).
- Hidden games:
  - `Game.Hidden`.
  - "Hid … · Undo" toast in `MainWindow`.
  - "Hidden games" section in `SettingsView`.
- Detail view (`DetailView`):
  - Banner, cover, stat tiles, recent sessions.
  - Per-game hide delay, process to watch and custom cover.
- Settings view, autostart, desktop layer, Epic / GOG / shortcut import / manual add.
- Windows CI with a smoke test (`.github/workflows/ci.yml`, `tests/smoke.ps1`).

### What's new

| # | Feature | Kind | Mainly touches |
|---|---|---|---|
| 1 | App icon: logo B | New asset | csproj (`ApplicationIcon`), tray, toasts |
| 2 | Fallback covers (icon-on-color, monogram) | Changes existing | `CoverCache`, `CoverView`, `Game` (cached tint) |
| 3 | Launcher icons instead of text badges | **Changes existing** | `ShelfView.xaml` (badge uses `SourceLabel` today), new `LauncherIcons` service |
| 4 | Controller input (XInput) | New | new `Services/Gamepad`, `MainWindow` |
| 5 | Flyout mode, tray icon, global shortcut, low-footprint "stay ready" | **Changes existing window behavior** | `MainWindow`, `App`, `DesktopLayer`, `Settings` |
| 6 | First-run setup (3 steps) | New | new `Views/SetupView` |
| 7 | Sort menu, Favorites chip, weekly summary, cover size / list view | Changes existing | `ShelfView`, `Settings` |
| 8 | More launchers (Xbox, EA, Ubisoft, Battle.net, Riot, Rockstar, Amazon, itch, Humble, Meta) | New sources | `Library/*Source.cs`, `GameSource` enum, filter chips |
| 9 | Detail: playtime chart, launch options with engine suggestions, anti-cheat label and check | Changes existing | `DetailView`, `Launcher`, `Game` |
| 10 | Crash detection on return | Changes existing | Watcher (save exit code), `Session`, `MainWindow`, `DetailView` |
| 11 | Game mode (power, priority, services, apps, network) with exact restore and a "Restored" toast | New, biggest item | new `GameShelf.Elevated` project, watcher hooks, new Settings sub-page |
| 12 | Free up space: which games to uninstall for a new one, per drive, shown as a treemap | New | new `Library/SpacePlanner`, new `Views/FreeSpaceView`, `SettingsView`, `GameMenu` |
| 13 | Look and moments: game logos on banners, the cover growing into the detail view, "Welcome back" after a session, shiny favourites, quiet playtime milestones | Changes existing | `ShelfView`, `DetailView`, `GameTile`, `CoverCache`, `Game`, `Settings` |
| 14 | Advanced settings: where Binder finds games and artwork, so users can fix it if a launcher changes | Changes existing | new `Library/SourceDefaults`, every `*Source`, `CoverCache`, `SettingsView`, `Settings` |
| 15 | Performance: the newest WinUI patch, pre-scaled cover copies, loading hidden UI only when needed, AOT compiler settings, CsWinRT 3.0 once stable | Changes existing | `GameShelf.csproj`, `CoverCache`, the XAML views |

### Conflicts with `CLAUDE.md` to resolve while implementing

These are owner decisions and override the brief:
- **Security rules say "never ask for admin rights".**
  - The owner approved one exception: Game mode's separate elevated helper, set up with one UAC prompt (§11).
  - The widget and the watcher themselves still never elevate.
  - Update the security rules when Game mode is built.
- **"A source badge in the top-left"** becomes the launcher's icon (§3).
- **The widget window:** "Flyout" becomes the default display mode, plus a notification-area icon (§5). The current
  docked-on-desktop behavior stays as the "On the desktop" option.
- **Filter chips** become: All, Favorites, Recent, then only the launchers that have games, with the rest under "More" (§7, §8).
- **Network rule "HTTPS to the store CDNs only"** gains one case (§14): an HTTPS address the user typed
  themselves in Advanced settings. Update the security rules in `CLAUDE.md` in milestone 17.

### Build order

1. Finish **milestone 7**.
2. Then implement this spec as milestones **8–14**, in the order at the end of this file.

The ideas listed under "Not decided" at the bottom must **not** be built.

---

## Decisions log

**2026-11-01**
- Logo **B** (cover stack).
- Game mode may use admin rights:
  - One-time UAC prompt.
  - Helper in `%ProgramFiles%\GameShelf\`.
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
- Scope rule: a launcher, not a game toolbox. Engine versions and shader-cache tools were dropped under it.

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
  DirectStorage (extra dependencies for little gain), and newer CPU instruction sets (would crash on older PCs).

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
- Find controllers through `WM_DEVICECHANGE` (`RegisterDeviceNotification`, HID class).
- Poll only while the widget is the **active window** and a pad is connected:
  - 60 Hz for 5 s after the last input, then 10 Hz.
  - Stop completely when the widget is deactivated.
  - Only poll connected slots.

**Mapping** (through WPF's directional focus navigation, so the keyboard and controller share one code path):

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

Three steps, shown once (Settings has "Run first-time setup again"):

1. **We found N games:** launchers with game counts, each with a checkbox.
2. **Tidy up your desktop?** The existing shortcut picker, embedded; moves are reversible as today.
3. **How should it open?**
   - Flyout / On the desktop.
   - Start with Windows.
   - The shortcut.
   - A tip about the tray icon (and pinning as an option).

## 7. Shelf additions (boards 7 and 11)

- **Sort menu** (button above the grid; default sort stored in `Settings.SortBy`):
  - Recently played, Most played, Name (A–Z, culture-aware), Recently added, Size on disk.
  - "Favorites first" checkbox (on by default).
  - "Recently added" needs a new `Game.AddedAt`, set the first time a game is seen.
- **Favorites filter chip** with a star; a star badge on favorite covers (top-right).
- **"This week" card** (optional, `Settings.ShowWeeklySummary`, on by default). Built; it now shares one slot with
  "Continue playing" (board 16, `CLAUDE.md` → Shelf view):
  - Total time, number of games, most played game, and 7 bars Mon–Sun.
  - Single series in the accent color: thin bars with 4 px rounded tops, a recessive baseline, a tooltip per bar,
    and an automation name with all values.
  - Computed from `sessions.json` when the panel opens. Never on a timer.
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
      "Game Shelf doesn't know this error" and offers both copy actions instead of advice (board 13, right column).
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
  - Installs `GameShelf.Elevated.exe` to `%ProgramFiles%\GameShelf\`. Never in a user-writable folder.
  - Registers the scheduled tasks *GameShelf Apply* / *GameShelf Restore* with "Run with highest privileges".
- **Per game:** the watcher runs a task (`schtasks /run`) and waits on a named event with a timeout.
- **The helper trusts nothing from the user side:**
  - It acts only on ids from its compiled-in allowlist.
  - The journal lives in `%ProgramData%\GameShelf\` with an admin-only ACL.
- **Without the helper:** the no-admin items still work.

### Timing

- **Apply:** when Play is pressed, in parallel with the game starting.
- **Restore:** after the watcher confirms the game closed (after the 5 s restart grace), before the widget returns.
- **If the game is never detected:** restore immediately.

### Game mode toast (board 4)

- **Style:** like the Windows 11 volume flyout, centered above the taskbar: a two-line card with a 36 px round
  badge (accent; caution with "!" when something failed). The badge pops in and its checkmark draws itself once
  (no idle cost; skipped with "Animation effects" off).
- **After the restore** (`GameShelf.exe --restored <result>`; with "Come back" off only the toast shows, then it exits):
  - "Windows is back to normal · Game mode put back 5 changes" (closes after 4 s), with the user's Windows notification
    sound ("Play a sound with it", on by default; `PlaySound` alias `Notification.Default`, system volume, skipped
    while `SHQueryUserNotificationState` says notifications are held back), or
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

A new game needs more room than a drive has. The user says how big it is; Game Shelf suggests which installed games
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
- Security: only built-in links from validated ids; nothing deleted by Game Shelf.

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

### b. The cover grows into the detail view

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
  which isn't approved; plain alpha layers come close. Compare with the prototype, and if it isn't good enough,
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
- Don't raise the instruction set (`IlcInstructionSet`, e.g. AVX2): Binder would crash on older processors.

### e. CsWinRT 3.0, once it's stable

- CsWinRT connects .NET to WinUI. Version 3.0 rewrites it for Native AOT (smaller and faster) but is only a preview
  so far (3.0.0-preview in 2026-03). Move to it once there's a stable 3.0 that the stable Windows App SDK supports,
  and only through the Windows App SDK's own version (no separate package).

## Build order (after milestone 7)

8. **Quick wins:** *(Done.)*
   - App icon (§1).
   - Launcher icons (§3).
   - Fallback covers (§2).
   - Sort, Favorites chip, cover size / list view (§7).
9. **Window behavior:** flyout, tray icon, shortcut, stay-ready footprint (§5) and first-run setup (§6). *(Done.)*
   Measure and report.
10. **Detail additions:** playtime chart and weekly card (§7, §9), launch options, anti-cheat (§9). *(Done.)*
11. **Crash detection** (§10).
12. **More launchers** (§8). Verify each one on the real PC. *(EA app, Ubisoft Connect, Xbox app and Rockstar done.)*
13. **Controller** (§4).
14. **Game mode:**
    - First the no-admin part: power, priority, apps, Game Bar, journal, toast.
    - Then the elevated helper, the installer step, the services and the network items.
    - Update the security rules in `CLAUDE.md` in the same milestone.
15. **Free up space** (§12). *(Done.)*
16. **Look and moments** (§13): logo on banners first (the others reuse it), then the detail animation, "Welcome
    back" with milestones, and shiny favourites. Check the sheen against the prototype on the real PC.
17. **Advanced: sources and artwork** (§14): move the defaults into `SourceDefaults` first (no behaviour change,
    covered by the existing tests), then the settings page and the "Check" buttons. Update the network rule in
    `CLAUDE.md`.
18. **Performance** (§15): the WinUI patch first, then pre-scaled covers, deferred loading and the AOT settings, each
    measured on its own. CsWinRT 3.0 when it's stable.

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
- A taskbar jump list with recent games
- Free up space, faster size entry (§12): fill the size from a pending install the launcher has written to disk
  (Steam/Epic, check what Steam writes first), a "Paste" button that reads "Storage: 150 GB" from copied store
  text, and quick picks (50/100/150/200 GB, last used)
- Start menu search integration
- Importing playtime from Steam
- Session notes
- Refresh rate / HDR per game
- Audio device per game
- A battery check for Best performance
- Backup / restore
- Portable mode (likely dropped, since it conflicts with the installer)
- A Windows 11 Widgets-board card
