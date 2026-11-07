# Instructions for coding agents

This file is for AI coding agents doing small, defined tasks in this repo. `CLAUDE.md` is the full project brief;
you don't need all of it for a task card, but the rules below come from it and always apply.

**The app:** Game Shelf (to be renamed Binder; the code still says GameShelf, don't rename anything) is a small
Windows desktop app in C# / WinUI 3 / .NET 10 that shows the user's installed games and launches them. It must use
as little CPU and memory as possible, and it is published with Native AOT.

## How to do a task

1. Take a task card from `docs/tasks/` whose `Status:` is `ready` and whose `Depends on:` cards are all `done`.
   Do one card at a time.
2. Create the branch named on the card (`agent/T001`, …) from the latest `main`.
3. Do exactly what the card says, in the files listed under `## Files`. Nothing else: no refactoring, no
   reformatting, no "while I'm here" fixes, no new files unless the card lists them.
4. Run the check named on the card, for example:
   `./check.ps1 -Task docs/tasks/T001-format-playtime-tests.md`
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
- Follow the style of the code around you: the same naming, spelling, comment style and indentation (4 spaces).

## Don't touch

These need a reviewed change by the owner; `check.ps1 -Task` fails if you change them:

- `src/GameShelf.Watcher/` (the game watcher) and `src/GameShelf.Setup/` (the installer)
- `Program.cs`, `MessageWindow.cs`, `app.manifest`
- `Services/Launcher.cs` and the services that call into Windows directly (`Native*`, `DesktopLayer`,
  `Monitors`, `Footprint`, `ShellImage*`, `ShellNotify`, `PcChecks`, `PcInfo`, `CrashLog`, `Autostart`,
  `CoverCache`, `Storage`, `RunningGames`, `GameLogs`, `GameModeService`) and `Views/PanelBackdrop.cs`
- The library sources and security checks: `Library/*Source.cs`, `InstallFolder.cs`, `ShortcutFile.cs`,
  `ShortcutImporter.cs`, `Vdf.cs`, `LibraryScanner.cs`
- Project and build files (`*.csproj`, `*.sln`, `*.props`), `publish.ps1`, `check.ps1`, `.github/`, `tools/`,
  `tests/smoke.ps1`, `tests/setup-test.ps1`, `tests/FakeGame/`
- `CLAUDE.md`, `AGENTS.md`, `docs/proposals.md`, `docs/installer.md`, `docs/tasks/`

## Where things are

| Path | What |
|---|---|
| `src/GameShelf/Library/` | Finding games, sorting, playtime, crash and disk-space logic (mostly no UI) |
| `src/GameShelf/Models/` | Data classes saved as JSON |
| `src/GameShelf/Services/` | Launching, storage, Windows integration |
| `src/GameShelf/Views/` | The UI (XAML + code-behind); `Format.cs` has the text formatting |
| `tests/GameShelf.Tests/Tests.cs` | All unit tests: one console program, `Check(condition, "description")` per check |
| `site/` | The website: `index.html`, `styles.css`, `logo.svg` (see "The website" below) |

## The website (`site/`)

The project's one-page website: `site/index.html`, `site/styles.css` and `site/logo.svg`. It will be published
from this repo (for example with GitHub Pages) once the repo is public.

- Plain HTML and CSS only. No JavaScript, no frameworks, no build step.
- Nothing loaded from other servers: no web fonts, CDNs, analytics, embeds or tracking pixels. The page promises
  privacy, so it must keep it. Links to other sites (`<a href="https://…">`) are fine. `check.ps1` enforces this.
- Colours only through the variables at the top of `styles.css` (`var(--text)`, `var(--accent)`, …). The
  product shot in the hero (`<figure>`) is a picture of the app and keeps its own fixed colours.
- Layout and spacing are inline `style="…"` attributes, section by section. Copy the style of a neighbouring
  element instead of inventing new values.
- The text is factual and plain: only what the app really does, numbers only from the card, no marketing words
  ("amazing", "seamless", "blazing fast"). Placeholders in square brackets (`[DOWNLOAD LINK]`) stay until a card
  gives the real value.
- Keep the "Before you download" section (the AI disclaimer and "Not affiliated") as it is.
- Check your change by opening `site/index.html` in a browser, at full width and at about 390 px wide (phone):
  nothing may overflow sideways. Say in your report that you did.

## Commands

- Unit tests (Windows or Linux, .NET 10 SDK): `dotnet run --project tests/GameShelf.Tests -c Release`
  The last line is `ALL PASS` or `N FAILED`; each check prints `PASS …` or `FAIL …`.
- Build (Windows only): `dotnet build GameShelf.sln -c Release`
- Both, plus the rules: `./check.ps1 -Task docs/tasks/<card>.md` (PowerShell 5.1 or 7)

Don't run `tests/smoke.ps1` or `tests/setup-test.ps1` on a real PC: they replace the user's data. They're for CI.
