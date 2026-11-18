# Instructions for coding agents

This file is for AI coding agents doing small, defined tasks in this repo. `CLAUDE.md` is the full project brief;
you don't need all of it for a task card, but the rules below come from it and always apply.

**The app:** Binder is a small
Windows desktop app in C# / WinUI 3 / .NET 10 that shows the user's installed games and launches them. It must use
as little CPU and memory as possible, and it is published with Native AOT.

## How to do a task

1. Take a task card from `docs/tasks/` whose `Status:` is `ready` and whose `Depends on:` cards are all `done`.
   Do one card at a time.
2. Create the branch named on the card (`agent/T001`, …) from the latest `main`.
3. Do exactly what the card says, in the files listed under `## Files`. Nothing else: no refactoring, no
   reformatting, no "while I'm here" fixes, no new files unless the card lists them.
4. Run the check named on the card, for example:
   `./scripts/check.ps1 -Task docs/tasks/T001-format-playtime-tests.md`
   It must end with `CHECK PASSED`.
5. Commit with the message `T001: <the card's title>` and push the branch. Don't merge it and don't open a pull
   request unless you're asked to.
6. Don't edit the task card. The owner marks it `done` after reviewing.

**Stop and report instead of guessing** when the card is unclear, when it would need a file that isn't listed,
when the check fails and you can't fix it inside the listed files, or when an expected value in the card turns out
to be wrong. Say what you found; don't change the expected value to make a test pass.

## Hard rules

- No new NuGet or other packages.
- No timers, polling, sleeps, delays, file watchers or background work. (The app does work only when the user
  does something.)
- Native AOT: no `[DllImport]` (`[LibraryImport]` exists), no `[ComImport]`, no `{Binding}` in XAML (`x:Bind`
  exists), no reflection in app code. (The unit tests may use reflection.)
- Never remove, weaken or skip an existing test. Only add new checks.
- Never delete or overwrite the user's files from code.
- No user-visible text in code or XAML: add it to `src/Binder/Strings/Strings.resx`, `Strings.de.resx` and
  `Strings.fr.resx` (same name, same `{0}` placeholders) and a line in `Strings.Entries.cs`, then use
  `Strings.Name` (XAML: `{x:Bind s:Strings.Name}`). Keep it short and plain: say what happens, don't sell.
  Give the English entry a `<comment>`: where it appears and what each `{0}` holds (the tests check this).
  `CLAUDE.md`, "Text and languages", has the rules.
- Follow the style of the code around you: the same naming, spelling, comment style and indentation (4 spaces).

## Don't touch

These need a reviewed change by the owner; `check.ps1 -Task` fails if you change them:

- `src/Binder.Watcher/` (the game watcher), `src/Binder.Setup/` (the installer) and
  `src/Binder.Elevated/` (Game mode's admin helper)
- `Program.cs`, `MessageWindow.cs`, `app.manifest`
- `Services/Launcher.cs` and the services that call into Windows directly (`Native*`, `DesktopLayer`,
  `Monitors`, `Footprint`, `ShellImage*`, `ShellNotify`, `PcChecks`, `PcInfo`, `CrashLog`, `Autostart`,
  `CoverCache`, `Storage`, `RunningGames`, `GameLogs`, `GameModeService`) and `Views/PanelBackdrop.cs`
- The library sources and security checks: `Library/*Source.cs`, `SourceDefaults.cs`, `InstallFolder.cs`, `ShortcutFile.cs`,
  `ShortcutImporter.cs`, `Vdf.cs`, `LibraryScanner.cs`
- Updates: `Library/UpdateManifest.cs`, `Services/UpdateKeys.cs`, `Services/Updater.cs`, `docs/releases/`,
  `build/` (`Updates.targets`, `OfficialRelease.props`: the update source and keys)
- Project and build files (`*.csproj`, `*.sln`, `*.props`), `scripts/`, `.github/`,
  `tests/smoke.ps1`, `tests/setup-test.ps1`, `tests/FakeGame/`
- `CLAUDE.md`, `AGENTS.md`, `docs/proposals.md`, `docs/installer.md`, `docs/tasks/`

## Where things are

| Path | What |
|---|---|
| `src/Binder/Library/` | Finding games, sorting, playtime, crash and disk-space logic (mostly no UI) |
| `src/Binder/Models/` | Data classes saved as JSON |
| `src/Binder/Services/` | Launching, storage, Windows integration |
| `src/Binder/Views/` | The UI (XAML + code-behind); `Format.cs` has the text formatting |
| `tests/Binder.Tests/Tests.cs` | All unit tests: one console program, `Check(condition, "description")` per check |
| `site/` | The website: `index.html`, `docs.html`, `releases.html`, `styles.css`, `logo.svg`, `release.js`, `clock.js`, `story.js`, `game.js`, `nav.js`, `usage.js`, `download.js` (see "The website" below) |

## The website (`site/`)

The project's website: `site/index.html` (the home page), `site/docs.html` (the documentation: what Binder
changes on the PC), `site/releases.html` (every version: notes, date, setup download), `site/styles.css`, `site/logo.svg` and its scripts (`release.js`, `clock.js`, `story.js`, `game.js`, `nav.js`, `usage.js`, `download.js`). `.github/workflows/pages.yml` publishes it to GitHub
Pages whenever a change to `site/` reaches `main`, so what's merged is live within minutes.

- Plain HTML and CSS, no frameworks, no build step. Seven small scripts: `site/download.js` opens a dialog once a
  setup download has started (it isn't code-signed yet, so Defender may warn; that version's VirusTotal result,
  read from the link's data attributes; don't install it if that isn't acceptable; styled as the website's own, never like a Windows dialog), `site/nav.js` marks the header link of the
  part being read and closes the phone Menu flyout after a tap, `site/usage.js` draws the "How much it uses" bars to
  scale from the figures on the page, `site/clock.js` sets the desktop story's
  taskbar clock and the shelf's greeting from the visitor's time, `site/story.js` runs the desktop story (below),
  `site/game.js` is the hidden game behind the shelf's Play button (the launch toast, 20 seconds of catching the
  made-up games' covers, then "Welcome back" with the score; nothing runs until Play is pressed; reduced motion:
  covers appear in place) (all six without network), and `site/release.js` asks GitHub's API
  (`api.github.com`, this repository's latest release) for the download link, the version line and the VirusTotal
  box, which `index.html` also holds as fallback values. That fetch, for things directly about Binder's releases,
  is the only one allowed (owner, 2026-11-16); it applies to the website only, not to the app. No inline scripts
  or event handlers, no `innerHTML`; `index.html`'s Content-Security-Policy allows only that address.
- Nothing loaded from other servers: no web fonts, CDNs, analytics, embeds or tracking pixels. The page promises
  privacy, so it must keep it. Links to other sites (`<a href="https://…">`) are fine. `check.ps1` enforces this.
- Colours only through the variables at the top of `styles.css` (`var(--text)`, `var(--accent)`, …), which
  have light and dark values. The desktop story (`.stage`, `.ico`, `.mock-*`, `.procs`, `.game-scene`) and the
  feature pictures are pictures of a PC and of the app and keep their own fixed colours.
- Layout is in classes in `styles.css`, grouped by section, with each section's phone values right after it.
  Reuse the existing pieces (`.section`, `.split`, `.h2`, `.h3`, `.lead`, `.button`, `.feature`, `.usage`,
  `.faq-item`, `.note`) instead of new styles; no inline `style="…"` except a `--c`/`--h` value on a picture's
  parts.
- The look (owner, 2026-11-17, "the desktop story"): plain Windows. Segoe UI Variable, neutral greys, the Windows
  accent only for actions and links, 4 px corners on controls and 8 px on cards, no blooms, pill buttons or
  decorative shapes. The header is Windows' acrylic (translucent and blurred, the story's desktop runs under it)
  with selector-bar links (an accent bar under the current one) and a Menu flyout on narrow windows; the story's
  captions and hero window are Windows 11 windows in Mica (translucent, tinted by the desktop behind them); the colour comes from the pictures. The one exception is the download panel in Binder's blue
  with the logo's fanned cover stack, kept by the owner. No small labels above headings, and don't add stock
  illustrations, emoji or other fonts.
- The page opens with the desktop story: one Windows desktop (`.stage`) pinned while five captions
  (`.story-step`, `data-step` 0–4) scroll over it: a desktop full of game shortcuts with the headline in a window,
  the shortcuts flying into Binder's shelf, the shelf, a game with Binder gone (the process list), and "Welcome
  back". `story.js` sets the step from the caption in the middle of the window (on narrow windows, just under the
  picture) and measures where each shortcut flies; `styles.css` moves the scene. The game in step 3 (Starfall Drift: stars,
  the ringed planet, asteroids, the ship, a HUD) animates only while step 3 is on screen (`animation-play-state`). Without scripts it shows the open
  shelf; with reduced motion the steps change at once. After changing it, check every step at full width, at
  390 px, in dark mode and with reduced motion.
- The header and footer are the same on both pages (`docs.html` links back with `index.html#…`). The "Binder is
  an app for Windows PCs" note (`touch-hint`) shows only on touch screens.
- The text is factual and plain: only what the app really does, numbers only from the card, no marketing words
  ("amazing", "seamless", "blazing fast"). Placeholders in square brackets (`[DOWNLOAD LINK]`) stay until a card
  gives the real value.
  The desktop story shows made-up games (Starfall Drift, Ember Keep, …) with covers drawn in CSS (`.g1`–`.g12`,
  on the shelf, the desktop icons and the hidden game), and the feature pictures use the same names; they stay
  made up: never a real game's name or look.
- The numbers in "How much it uses" (and the helper's memory in the story's process list) sit between `<!-- n:key -->` and
  `<!-- /n -->` markers. A workflow writes them for each release (`scripts/site-numbers.ps1`); don't edit the text
  between the markers or remove them.
- `releases.html`'s list of versions (the cards between `<!-- releases -->` and `<!-- /releases -->`, the side
  navigation between `<!-- releases-nav -->` and `<!-- /releases-nav -->`) is written by `scripts/site-releases.ps1`
  from the published releases and `docs/releases/*.json`, by `pages.yml` each time it publishes the site (also when
  a release is published). Change the cards' look in the script and in the committed list together; the notes'
  wording in `docs/releases/`.
- Keep the "Before you download" section (the AI disclaimer and "Not affiliated") as it is.
- `docs.html` is long text, so its parts are `docs-*` / `doc-*` classes in `styles.css`, not inline styles. Every
  sentence must be true of the code. Each Game mode item is one `doc-item` card with `data-item`, `data-default`
  and `data-admin` in that order; `tests/Binder.Tests/DocsTests.cs` checks them against the code.
- Check your change by opening the page you changed (`site/index.html` or `site/docs.html`) in a browser, at
  full width and at about 390 px wide (phone): nothing may overflow sideways. Say in your report that you did.

## Commands

- Unit tests (Windows or Linux, .NET 10 SDK): `dotnet run --project tests/Binder.Tests -c Release`
  The last line is `ALL PASS` or `N FAILED`; each check prints `PASS …` or `FAIL …`.
- Build (Windows only): `dotnet build Binder.sln -c Release`
- Both, plus the rules: `./scripts/check.ps1 -Task docs/tasks/<card>.md` (PowerShell 5.1 or 7)

Don't run `tests/smoke.ps1` or `tests/setup-test.ps1` on a real PC: they replace the user's data. They're for CI.
