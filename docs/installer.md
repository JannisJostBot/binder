# Building, installing and updating Game Shelf

Game Shelf ships as one file, `GameShelfSetup.exe`. It installs the app for your Windows account (no
administrator rights), updates an existing install, and is what Settings → Apps runs to uninstall it.

> Game Shelf is vibe-coded: the code was written by an AI (Claude) and is provided as is. Settings → Apps lists
> its publisher as "Vibe-coded with Claude (AI-generated) · Jannis Jost". That text comes from `<Company>` in
> `Directory.Build.props`.

## Build it

You need, on a Windows 10/11 x64 PC:

- The **.NET 10 SDK** (`dotnet --list-sdks` should list a 10.x version).
- The **"Desktop development with C++"** workload from Visual Studio or the Visual Studio Build Tools. Native
  AOT (the watcher and the setup) needs its MSVC linker.
- Optional, for signing: the Windows SDK (`signtool.exe`) and a code-signing certificate in your certificate
  store.

Then, in PowerShell, from the repository folder:

```powershell
./publish.ps1
```

This builds everything into `publish\`:

| File | What it is |
|---|---|
| `GameShelfSetup.exe` | The installer and updater. This is the only file you need to share or keep. |
| `app\` | What gets installed, laid out as in the install folder: the widget (`GameShelf.exe`, WinUI 3, Native AOT, with the .NET and Windows App SDK runtimes it needs, about 70 MB in about 220 files) and the tiny game watcher (`GameShelf.Watcher.exe`, Native AOT). Nothing else needs to be installed on the PC. |

The last lines of the output show the sizes and the setup's SHA-256 hash.

Options:

- `./publish.ps1 -CertificateThumbprint <SHA-1 thumbprint>` Authenticode-signs the three exes, with a
  timestamp. Signing the widget and the watcher happens before they're packed into the setup, so the installed
  files are signed too.

`dotnet build` alone builds the setup project as well, but without the app inside it. That setup only tells you
to use `publish.ps1`.

## Install

Double-click `GameShelfSetup.exe`, read the disclaimer, tick **I've read and understood the disclaimer** and click
**Install**. The button stays disabled until the box is ticked; the dialog shows the short version and **Show the
full disclaimer** the full text from the README. Updates and reinstalls ask the same way.

- It installs to `%LOCALAPPDATA%\Programs\GameShelf\` (your account only, no UAC prompt).
- It adds **Game Shelf** to the Start menu and to **Settings → Apps → Installed apps**, with its icon, version
  and size.
- At the end, "Open Game Shelf now" starts it. The first time, it runs its own first-run setup (launchers,
  tidying the desktop, how it opens, Start with Windows).

The app carries its own runtimes, so there's nothing else to install. Setup downloads nothing.

Because the setup isn't signed (unless you sign it), Windows SmartScreen may say "Windows protected your PC"
the first time you run a downloaded copy. Click **More info → Run anyway** if you built it yourself or trust
where it came from. A file you built on the same PC doesn't get that warning.

### Moving from the portable copy

If you've been running `GameShelf.exe` straight from `publish\` (or `publish\app\`), just run the setup. It closes the running
copy and starts the installed one. Your games, play history and settings are in `%APPDATA%\GameShelf` and stay
as they are. If Start with Windows was on, the installed app points it at its new location the first time it
starts. You can then delete the old `publish\` copy.

## Update

Build the new version (raise `<Version>` in `Directory.Build.props` first) and run its `GameShelfSetup.exe`.
It shows **Update Game Shelf to x.y.z**. Updating:

- keeps your games, play history, settings and imported shortcuts (they're not in the install folder);
- closes Game Shelf if it's running and starts it again afterwards, back in the tray (or on the desktop, in
  desktop mode);
- works while you're playing: the watcher that's tracking your game keeps running on its old file, records the
  session as usual, and brings back the *new* widget when the game closes.

Running the same version again offers **Reinstall**, which repairs a damaged install. An older setup warns that a
newer version is installed before replacing it.

The app never checks for updates or contacts anything to do so (see the network rule in `CLAUDE.md`). You
update by running a newer setup.

## Uninstall

Settings → Apps → Game Shelf → **Uninstall** (or Start menu → right-click → Uninstall).

- **Removed:** the app's files, the Start menu entry, the Settings → Apps entry, and Start with Windows.
- **Put back:** every desktop shortcut you moved onto the shelf goes back on the desktop.
- **Your choice:** "Also delete my game list, play history and settings" (off by default). Leave it off to
  keep them for a later reinstall.
- If a game is still being tracked, it asks first, because that session's playtime would be lost.

## Command line

| Command | What it does |
|---|---|
| `GameShelfSetup.exe` | Install or update, with dialogs. |
| `GameShelfSetup.exe /S` | Silent install or update. Doesn't open the app, except to restart one that was running. Running it silently counts as having read the disclaimer (README). |
| `GameShelfSetup.exe /S /launch` | Silent, then start Game Shelf. |
| `GameShelfSetup.exe /S /allowdowngrade` | Silent, even over a newer version (refused otherwise). |
| `Uninstall.exe /uninstall` | Uninstall, with dialogs. |
| `Uninstall.exe /uninstall /S [/removedata]` | Silent uninstall, optionally deleting the data too. |

Exit codes: 0 done, 1 cancelled, 2 error, 4 silent downgrade refused, 5 unknown option (3 was "the .NET
Desktop Runtime is missing", which the self-contained app no longer needs). Every run writes a few lines to `%TEMP%\GameShelfSetup.log`.

---

## How it works inside

The project is `src/GameShelf.Setup`: a Native AOT exe of about 30 MB (almost all of it the compressed app it
carries), with no third-party code. Its UI is Windows' own task dialog (`TaskDialogIndirect` from Common Controls 6), so it
looks like Windows' built-in prompts.

### What's in the exe

`publish.ps1` builds the widget and the watcher into `publish\app\`, then zips that folder to
`obj/setup-payload/app.zip` and writes `payload.txt`, which lists every file with its SHA-256, its size and
its path inside the folder. Both are embedded as resources when the setup is published (`-p:PayloadDir=…`).
Setup only accepts a list of well-formed hashes and plain relative paths (no root, drive, `..`, empty or
dotted names, or stream syntax; at most 8 levels and 2000 files) that includes the widget and the watcher, and
it extracts only the files that list names.

### Install and update steps (`Installer.cs`)

1. Read the payload.
2. Read the installed version from the Settings → Apps entry and choose Install, Update, Reinstall or the
   downgrade warning. The dialog carries the disclaimer, and its button is enabled only while "I've read and
   understood the disclaimer" is ticked (a task dialog callback, `TDM_ENABLE_BUTTON`). In silent mode, refuse a
   downgrade unless `/allowdowngrade`.
3. Refuse if `%LOCALAPPDATA%\Programs` or the install folder is a link (junction or symlink) to somewhere else.
4. If the widget is running, send it `--quit` (the same WM_COPYDATA message a second start uses) and wait up to
   10 s. If a copy of *our* installed exe still hangs after that, end it.
5. For each file (every file of the app, and setup itself copied as `Uninstall.exe`):
   1. Make sure no folder on its way inside the install folder is a link, create its folder, and write it as
      `name.new` next to its final place, with write-through.
   2. Re-read it and compare its SHA-256 with `payload.txt`.
   3. Swap it in with a rename. If the old file is in use (a watcher tracking a game), rename the old one to
      `name.old` first, which Windows allows for a running exe, then move the new one in.
   4. Leftover `.old`/`.new` files of *our* file names are deleted at the start and end of every run, and on
      uninstall.
   5. Then `installed-files.txt` in the install folder is rewritten with the new list, and the files the
      previous list had but the new version doesn't are deleted, along with folders they leave empty. (Setups
      before the list existed installed only `GameShelf.exe`, `GameShelf.Watcher.exe` and `Uninstall.exe`.)
6. Write the Start menu shortcut with the Shell's own `ShellLink` object (source-generated COM, which works with
   AOT).
7. Write `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\GameShelf` (name, version, publisher, icon,
   folder, quoted uninstall command, size, no modify or repair).
8. Restart the widget with `--background` if it was running, or offer to open it.

### Uninstall steps (`Uninstaller.cs`)

An exe can't delete itself while it runs. So `Uninstall.exe /uninstall` in the install folder only asks the
questions, copies itself to a new `%TEMP%\GameShelf-uninstall-<random>\` folder, starts that copy with
`/continue` and the answers, and exits. The temp copy then:

1. Ends the watcher if a game is being tracked (after asking), and quits the widget.
2. Runs the installed `GameShelf.exe --restore-shortcuts`. With no UI, this moves every imported shortcut back
   to where it came from and removes the games made from them.
3. Removes the Start with Windows entry, but only if it starts the installed exe.
4. Removes the Start menu shortcut and the Settings → Apps entry.
5. Deletes the files `installed-files.txt` lists, plus the ones its own payload names (in case the list is
   gone), retrying while the first `Uninstall.exe` is still exiting, and their `.old`/`.new` leftovers. Every
   listed path is checked again, and nothing is deleted through a link. Then the folders they leave empty and
   the install folder, but only if it's empty. Anything you put there yourself stays.
6. If you ticked "delete my data", deletes `%APPDATA%\GameShelf` and `%LOCALAPPDATA%\GameShelf`. It doesn't
   follow links inside them (a link is removed, not entered). If some imported shortcuts couldn't go back to
   the desktop, they and their list are kept, and the final dialog says where.
7. After it exits, removes its own temp folder through `cmd.exe` from System32 a few seconds later. This only
   happens for a folder it made, with a path of plain characters. Otherwise Windows' temp clean-up gets it.

### Security

- **No elevation, ever.** The manifest says `asInvoker`. This also stops Windows' installer detection, which
  would otherwise ask for admin rights for any exe with "setup" in its name.
- **DLL planting.** Installers usually run from Downloads, next to other downloaded files. The exe is linked
  with `/DEPENDENTLOADFLAG:0x800`, and `Main` calls `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`
  first. So every DLL comes from System32, and P/Invokes are pinned to System32 too.
- **Fixed paths only.** Every location is computed from your known folders. None is read back from the registry
  or a data file, so a tampered uninstall entry can't point the uninstaller at other files.
- **Deletes only what it wrote.** The files it listed in its folder, its own Start menu shortcut, registry
  entry and Run value. Your data only when you ask. Never through links.
- **Checked files.** Each installed file is hashed after it's written to disk, and swapped in only if it
  matches.
- **Processes.** It only ends processes whose exe is *our installed copy*, never a same-named program elsewhere.
- **Arguments.** Processes are started with `ArgumentList`. The one `cmd.exe` call only gets a path that's
  been checked to be our own temp folder with plain characters.
- **Network.** None.
- **Signing.** Not signed by default, so SmartScreen warns about downloaded copies. `-CertificateThumbprint`
  signs everything with SHA-256 and a timestamp.

The install folder is in your profile, so other programs running as you could change its files. That's the
same for every per-user install (VS Code, Discord, …), and it's why the Game mode helper (milestone 14), which
runs elevated, will be installed to Program Files instead.

### Later: the Game mode helper (milestone 14)

Game mode's service and network items need admin rights. That's the one approved exception to "never
elevate" (`docs/proposals.md` §11). The app, the watcher and this setup stay per-user and prompt-free. The
helper is added on top:

1. **Install:** one UAC prompt, only when Game mode is turned on in Settings, never during setup. It copies
   `GameShelf.Elevated.exe` to `%ProgramFiles%\GameShelf\` (writable by admins only, so a program running as you
   can't swap it) and registers the *GameShelf Apply* / *GameShelf Restore* scheduled tasks.
2. **Update:** setup carries the helper too, but asks for elevation only when the helper's file actually
   changed. Updates that don't touch it stay prompt-free.
3. **Uninstall:** the per-user uninstaller can't remove Program Files or the tasks by itself. When the helper is
   installed, the temp copy asks for one UAC prompt to remove it. If you decline, the final dialog says it's
   still there and how to remove it later.
4. **Restore first:** if a Game mode journal is still pending (services paused, a power plan changed),
   uninstall restores everything before removing anything.
5. **Several Windows accounts:** the helper is per PC, the app per account. Uninstalling for one account
   doesn't remove the helper while another account still uses Game mode.

### Tested in CI

`tests/setup-test.ps1` runs on every push after the UI smoke test:

1. Install through the dialogs (Install must be disabled until the disclaimer box is ticked), then check the files against the published ones, the Start menu shortcut and
   the Settings → Apps entry.
2. Update silently while the widget and a watcher are running. The widget restarts on the new files, the
   watcher keeps running, and its file was moved aside and replaced.
3. Uninstall through the dialogs with "delete my data" ticked and a game still tracked. Check that everything
   is gone, an imported shortcut is back on the desktop, and the temp copy removed itself.

Screenshots of each dialog and `setup-metrics.txt` are in the CI results.
