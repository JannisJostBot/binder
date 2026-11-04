# Planned features (approved by the owner)

Status: **approved specs. Milestones 8–11 are built; 12–15 are still to do** (see the progress table in §0).
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
| 12 More launchers | §8 | **Next.** Partly: EA app and Ubisoft Connect are done; Xbox, Battle.net, Riot, Rockstar, Amazon, itch, Humble and Meta are not |
| 13 Controller | §4 | To do |
| 14 Game mode | §11 | To do |
| 15 Free up space | §12 | To do |

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

### Restored toast (board 4)

- **Style:** like the Windows 11 volume flyout, centered above the taskbar.
- **Content:**
  - "Restored successfully" (fades after 3 s), or
  - "Restored 4 of 5 · Print Spooler didn't start" plus **Details** (stays until dismissed).
- **Shown by:** `GameShelf.exe --restored <result>`. If "Come back" is off, it shows only the toast and exits.
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
- Last played: the later of our sessions and Steam's `LastPlayed` in `appmanifest_*.acf` (already parsed), so a game
  played through Steam directly isn't treated as unplayed. Verify the key on the real PC.

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
12. **More launchers** (§8). Verify each one on the real PC. *(EA app and Ubisoft Connect done.)*
13. **Controller** (§4).
14. **Game mode:**
    - First the no-admin part: power, priority, apps, Game Bar, journal, toast.
    - Then the elevated helper, the installer step, the services and the network items.
    - Update the security rules in `CLAUDE.md` in the same milestone.
15. **Free up space** (§12).

## Not decided (don't build)

Suggested but neither approved nor rejected yet:
- Collections / tags
- Showing uninstalled games
- Emulator entries
- Merging duplicates
- Run something before or after a game
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
