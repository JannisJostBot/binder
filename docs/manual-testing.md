# Manual test checklist (real PC)

CI already runs most of this on a clean Windows Server machine with fake Steam, Epic and GOG installs. This
list is for what CI can't check: your real launchers and games, your hardware and monitors, and how it feels.

## How to test everything at once

All milestone branches build on each other in one line (`main` → milestone 4 → … → 9 → `installer`), so the
**newest branch contains everything**. Test that one build only. There's nothing to merge or check out per
milestone, and you're testing exactly the combination that will end up on `main`.

1. **Get the build.** No build tools needed: on GitHub, switch to the branch **`ci-results/installer`** (or
   whichever branch is newest) and download **`GameShelfSetup.exe`**. CI builds it from that branch on every
   push. Building it yourself works too: check out the branch and run `./scripts/publish.ps1` (see
   [installer.md](installer.md)).
2. **Back up your data** (optional, but cheap): copy `%APPDATA%\GameShelf` somewhere. Imported desktop
   shortcuts are only ever moved, never deleted.
3. **Quit any old copy** you're running from the tray (right-click → Quit), then run the setup.
4. Go through the list below, in order (later sections reuse earlier ones). Tick what works.
5. **Report back** the numbers of what failed (e.g. "F4, L7"), with a sentence or a screenshot each, plus
   these two files if something broke:
   - `%LOCALAPPDATA%\GameShelf\error.log` (the app's errors)
   - `%TEMP%\GameShelfSetup.log` (setup)

   I fix those on the newest branch, CI builds a new setup, and you re-test only the failed items.
6. When everything passes, `main` can simply be fast-forwarded to the tested branch.

Tick boxes here on GitHub (edit the file), in a copy, or on paper. Whatever is easiest.

---

## I · Install (installer)

- [x] **I1** Run `GameShelfSetup.exe`. If SmartScreen warns, More info → Run anyway works. **No UAC prompt**.
- [x] **I2** *(Dropped: the WinUI app is self-contained, so there's no runtime to be missing.)*
- [x] **I3** Install → "Game Shelf is installed" → Close with "Open Game Shelf now" ticked opens the app.
- [x] **I4** Start menu has **Game Shelf** with its icon. Typing "Game Shelf" in Start finds it.
- [x] **I5** Settings → Apps → Installed apps lists **Game Shelf 0.1.0**, publisher "Vibe-coded with Claude
  (AI-generated) · Jannis Jost", with icon and size.

## S · First-time setup (milestone 9)

- [ ] **S1** Step 1 lists your installed launchers with the right game counts.
- [ ] **S2** Untick one launcher → Next. Its games don't appear on the shelf afterwards (Settings can turn it
  back on).
- [ ] **S3** Step 2 lists your desktop shortcuts, with game shortcuts pre-ticked and others (browsers, tools)
  not. "Not now" skips; "Move N" moves the ticked ones off the desktop.
- [ ] **S4** Step 3: pick **Flyout**, turn on Start with Windows → Done → the shelf shows.
- [ ] **S5** Settings → "Run first-time setup again" shows it again.

## F · Flyout, tray icon, shortcut (milestone 9)

- [ ] **F1** The Game Shelf icon is in the notification area (maybe behind the **^** arrow; dragging it onto
  the taskbar keeps it visible). It matches the taskbar (light/dark).
- [ ] **F2** Clicking the icon opens the flyout at the screen edge with a short slide-in. Clicking it again
  closes it. The slide stays smooth on a high-refresh screen; covers appear as it ends.
- [ ] **F3** **Win+Shift+G** opens and closes it from anywhere. If another app owns that shortcut, Settings says
  so and lets you pick another (Win+Shift+J, Ctrl+Alt+G).
- [ ] **F4** Esc closes it. Clicking anywhere else closes it. The **pin** button keeps it open while you click
  elsewhere.
- [ ] **F5** Right-click on the tray icon: Open, Settings, Quit all do what they say.
- [ ] **F6** Opening the Start menu entry while it's running opens/closes the same flyout (no second copy in
  Task Manager).
- [ ] **F7** Restart Windows: Game Shelf starts **without showing** anything, then the icon/shortcut opens it.
- [ ] **F8** The tray icon is in colour while the flyout is open, and white/grey again once it closes.
- [ ] **F9** Pinned, the pin button shows a filled pin that still has its needle.
- [ ] **F10** Click the tray icon to close the flyout and click again straight away: it opens again (the second click isn't lost). A double-click on the icon leaves it open.

## D · Desktop mode (milestones 5 and 7)

Settings → How Game Shelf appears → **On the desktop**.

- [ ] **D1** The panel sits on the desktop, docked right, below other windows. It doesn't come in front when
  clicked.
- [ ] **D2** Win+D (Show desktop) keeps it visible. Win+Shift+G brings it in front for a moment.
- [ ] **D3** Position Left / Right / Free works. Free: drag it, restart, and it's still there.
- [ ] **D4** Taskbar on auto-hide: the panel leaves room for it, and the taskbar never covers the panel.
- [ ] **D5** *(Several monitors)* Settings → Screen moves it to the chosen monitor. Unplugging that monitor
  moves it to the main one. Different scaling per monitor stays sharp.
- [ ] **D6** Switch back to **Flyout** for the rest (or test both, if you like).

## L · Library (milestone 6)

- [ ] **L1** **Steam:** all installed games are there (including other library drives). Tools, Proton,
  redistributables and servers aren't. Covers are the Steam art.
- [ ] **L2** **Epic:** installed games are there. Covers are generated (icon on a color), not blank.
- [ ] **L3** **GOG:** installed games are there (if you use GOG).
- [ ] **L9** **EA app:** installed games are there, named without ™, with their icon. Play starts them through the EA app.
- [ ] **L10** **Ubisoft Connect:** installed games are there (not ones whose folder is gone), with their icon. Play starts them through Ubisoft Connect.
- [ ] **L4** **Import desktop shortcuts** (Settings): the picker, moving, and a Steam link merging with the
  Steam entry instead of a duplicate.
- [ ] **L5** **Restore shortcuts to desktop** puts every one back where it was.
- [ ] **L6** **Add a game** opens the standard Windows file picker. An .exe or a shortcut is added.
- [ ] **L7** Dragging an .exe or shortcut onto the panel adds it.
- [ ] **L8** Refresh library picks up a game you installed or removed meanwhile.

## G · Shelf (milestones 3 and 8)

- [ ] **G1** Each cover has its launcher's real icon (Steam, Epic, GOG) top-left. Manually added games and
  shortcuts have none. Favorites have a star top-right.
- [ ] **G2** Games without store art get a generated cover: their icon (the game's real icon, e.g. Battlefield V's "V") or initials, on a soft gradient of their own color. None is blank, broken or a generic program icon.
- [ ] **G3** Search filters as you type. Typing anywhere on the shelf goes to the search box.
- [ ] **G4** Chips: All, Favorites, Recent, your launchers (most games first), Other, "More" if needed.
- [ ] **G5** Sort menu: each order works, "Favorites first" works, and it's remembered after a restart.
- [ ] **G6** Settings → Cover size: Small / Medium / Large / List all look right.
- [ ] **G7** Keyboard: arrow keys move through the grid, Enter plays, Esc goes back.
- [ ] **G8** Right-click on a cover opens a native Windows menu (dark, like Explorer's): Play (bold), Details, Open folder,
  Favorite, Hide, and for Steam Verify game files and Uninstall in Steam…. The Menu key or Shift+F10 opens it under the
  cover; access keys work (e.g. P plays). The detail view's "…" button opens the same menu.
- [ ] **G9** Continue playing shows the last game you played.
- [ ] **G10** With text in the search box, a ✕ appears at its right end. Clicking it clears the search and keeps the cursor in the box.

## P · Play → hide → come back (milestone 2)

- [ ] **P1** **A Steam game:** Play → "Starting …" countdown → Game Shelf disappears (not in Task Manager, only
  `GameShelf.Watcher`). Close the game → it comes back.
- [ ] **P2** **An Epic game:** same as P1 (the Epic launcher starts first; detection still works).
- [ ] **P3** **A GOG or manually added game:** same.
- [ ] **P4** A game that restarts itself, or starts through its own launcher: it's still tracked until the real
  game closes.
- [ ] **P5** **Keep open** during the countdown keeps the shelf open, and the session still counts.
- [ ] **P6** A game it can't follow shows "Couldn't detect the game" after a few minutes and points to the
  "Process to watch" setting. Setting it fixes it.
- [ ] **P7** Settings → "Come back when the game closes" off: it stays gone after the game.

## T · Game details (milestone 4)

- [ ] **T1** Opening a game shows its banner, cover, store, playtime, last played, size on disk and recent
  sessions. They match what you played in P.
- [ ] **T2** Per game: hide delay, process to watch (Change → Choose program…, pick the game's exe), custom cover. All are
  kept after a restart.
- [ ] **T3** Open folder, Store page and Hide from shelf work. Settings → Hidden games brings it back.

## W · Feels like Windows

- [ ] **W1** Switching Windows between light and dark changes the app live, and the tray icon too.
- [ ] **W2** Changing the accent color changes the app's accent live.
- [ ] **W3** Settings → Accessibility → Visual effects → **Animation effects off**: no slide-in, no
  transitions.
- [ ] **W4** Text size (Accessibility → Text size) at 150 %: nothing cut off.
- [ ] **W5** Display scaling 125/150 %: sharp, right size, docked correctly.
- [ ] **W6** A high-contrast theme: everything readable.
- [ ] **W7** Narrator (Win+Ctrl+Enter): buttons, covers and badges are read with sensible names.
- [ ] **W8** The panel has Windows 11 Acrylic (frosted glass showing what's behind it), also while it isn't focused (pinned, or in desktop mode). Settings → Personalization → Colors → Transparency effects off makes it solid right away, and on again brings it back. No dark strip or box anywhere along its right edge or below the scrollbar.

## R · Speed and resources (Task Manager → Details, "Memory (private working set)")

Note the numbers. CI's numbers are in brackets.

- [ ] **R1** Shelf open and idle: CPU 0 % (CI ~0). Memory: WinUI uses more than the old WPF panel did (about 130–230 MB with covers on this PC); the owner accepted that, since only the game's performance matters and the widget is gone while a game runs.
- [ ] **R2** Flyout closed, waiting in the tray: CPU 0 %. Task Manager's Memory column after Windows start (CI
  ~12 MB) and after opening and closing once (CI ~12–15 MB in RAM). "Commit size" stays higher after the first
  open: that's WinUI's graphics stack, paged out rather than in RAM.
- [ ] **R3** Opening with Win+Shift+G feels instant (CI 0.3–0.6 s).
- [ ] **R4** During a game: no `GameShelf.exe` process, `GameShelf.Watcher.exe` at ~3 MB and 0 % CPU.

## U · Update and uninstall (installer)

- [ ] **U1** Run `GameShelfSetup.exe` again → **Reinstall** → Game Shelf comes back as it was (tray), with your
  games and settings.
- [ ] **U2** Same, *while a game is running*: the update finishes, and when you close the game, Game Shelf
  comes back and the session is recorded.
- [ ] **U3** Settings → Apps → Game Shelf → **Uninstall**, *without* "delete my data": the app, Start menu entry
  and Start with Windows are gone. Imported shortcuts are back on the desktop.
- [ ] **U4** Install again: your games, favorites and play history are still there.
- [ ] **U5** *(Only if you're done testing)* Uninstall *with* "delete my data": `%APPDATA%\GameShelf` and
  `%LOCALAPPDATA%\GameShelf` are gone too.

---

*Update this file with each milestone: add its items, and remove nothing until it has passed on a real PC.*
