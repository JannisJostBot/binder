# Building, installing and updating Binder

Binder ships as one file, `BinderSetup.exe`. It installs the app for your Windows account (no
administrator rights), updates an existing install, and is what Settings → Apps runs to uninstall it.

> Binder is vibe-coded: the code was written by an AI (Claude) and is provided as is. Settings → Apps lists
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
./scripts/publish.ps1
```

This builds everything into `publish\`:

| File | What it is |
|---|---|
| `BinderSetup.exe` | The installer and updater. This is the only file you need to share or keep. |
| `app\` | What gets installed, laid out as in the install folder: the widget (`Binder.exe`, WinUI 3, Native AOT, with the .NET and Windows App SDK runtimes it needs, about 70 MB in about 220 files), the tiny game watcher (`Binder.Watcher.exe`, Native AOT) and Game mode's helper (`Binder.Elevated.exe`, Native AOT, only ever run elevated after Settings › Game mode › Allow…). Nothing else needs to be installed on the PC. |

The last lines of the output show the sizes and the setup's SHA-256 hash.

Options:

- `./scripts/publish.ps1 -CertificateThumbprint <SHA-1 thumbprint>` Authenticode-signs the three exes, with a
  timestamp. Signing the widget and the watcher happens before they're packed into the setup, so the installed
  files are signed too.

`dotnet build` alone builds the setup project as well, but without the app inside it. That setup only tells you
to use `publish.ps1`.

## Install

Double-click `BinderSetup.exe`, read the disclaimer, tick **I've read and understood the disclaimer** and click
**Install**. The window shows the README's full disclaimer in a scrollable box with the checkbox right under it; the
button stays disabled until the box is ticked. Updates and reinstalls ask the same way.

- It installs to `%LOCALAPPDATA%\Programs\Binder\` (your account only, no UAC prompt).
- It adds **Binder** to the Start menu and to **Settings → Apps → Installed apps**, with its icon, version
  and size.
- At the end, "Open Binder now" starts it. The first time, it runs its own first-run setup (launchers,
  tidying the desktop, how it opens, Start with Windows).

The app carries its own runtimes, so there's nothing else to install. Setup downloads nothing.

Because the setup isn't signed (unless you sign it), Windows SmartScreen may say "Windows protected your PC"
the first time you run a downloaded copy. Click **More info → Run anyway** if you built it yourself or trust
where it came from. A file you built on the same PC doesn't get that warning.

### Moving from the portable copy

If you've been running `Binder.exe` straight from `publish\` (or `publish\app\`), just run the setup. It closes the running
copy and starts the installed one. Your games, play history and settings are in `%APPDATA%\Binder` and stay
as they are. If Start with Windows was on, the installed app points it at its new location the first time it
starts. You can then delete the old `publish\` copy.

## Update

Build the new version (raise `<Version>` in `Directory.Build.props` first) and run its `BinderSetup.exe`.
It shows **Update Binder to x.y.z**. Updating:

- keeps your games, play history, settings and imported shortcuts (they're not in the install folder);
- closes Binder if it's running and starts it again afterwards, back in the tray (or on the desktop, in
  desktop mode);
- works while you're playing: the watcher that's tracking your game keeps running on its old file, records the
  session as usual, and brings back the *new* widget when the game closes.

Running the same version again offers **Reinstall**, which repairs a damaged install. An older setup warns that a
newer version is installed before replacing it.

The app never checks for updates or contacts anything to do so (see the network rule in `CLAUDE.md`). You
update by running a newer setup.

## Uninstall

Settings → Apps → Binder → **Uninstall** (or Start menu → right-click → Uninstall).

- **Removed:** everything Binder put on your PC: the app's files, the Start menu entry, the Settings → Apps entry,
  Start with Windows, the cache in `%LOCALAPPDATA%\Binder` (covers, icons, logs, Game mode's files) and, after
  it's done, setup's own log in `%TEMP%`.
- **Your choice, shortcuts:** when you have imported shortcuts, **Put my shortcuts back on the desktop** (the
  default) or **Don't put them back**. Shortcuts not put back stay with your game list, or are deleted with it.
  Shortcuts that couldn't go back (no desktop to put them on) are always kept, and the final dialog says where.
- **Your choice, data:** "Also delete my game list, play history and settings" (off by default). Leave it off to
  keep them (in `%APPDATA%\Binder`) for a later reinstall.
- **Only with your OK:** Game mode's admin helper in Program Files needs a UAC prompt to remove. If you decline, it
  stays (it does nothing without Binder), and the final dialog says where.
- If a game is still being tracked, it asks first, because that session's playtime would be lost.
- **Game mode:** whatever it still has applied (a game was running, or a session didn't end normally) is put back
  first, by the installed watcher (`Binder.Watcher.exe --restore --quiet`), then its admin part is removed.

## Command line

| Command | What it does |
|---|---|
| `BinderSetup.exe` | Install or update, with dialogs. |
| `BinderSetup.exe /S` | Silent install or update. Doesn't open the app, except to restart one that was running. Running it silently counts as having read the disclaimer (README). |
| `BinderSetup.exe /S /launch` | Silent, then start Binder. |
| `BinderSetup.exe /S /allowdowngrade` | Silent, even over a newer version (refused otherwise). |
| `Uninstall.exe /uninstall` | Uninstall, with dialogs. |
| `Uninstall.exe /uninstall /S [/removedata] [/keepshortcuts]` | Silent uninstall, optionally deleting the data too; imported shortcuts go back on the desktop unless `/keepshortcuts`. |

Exit codes: 0 done, 1 cancelled, 2 error, 4 silent downgrade refused, 5 unknown option, 6 another setup is still
running (3 was "the .NET
Desktop Runtime is missing", which the self-contained app no longer needs). Every run writes a few lines to `%TEMP%\BinderSetup.log`.

---

## How it works inside

The project is `src/Binder.Setup`: a Native AOT exe of about 30 MB (almost all of it the compressed app it
carries), with no third-party code. The install / update screen is a classic setup window (`ConfirmWindow`: a Win32 dialog
built from an in-memory template, standard controls only); the other prompts are Windows' own task dialogs
(`TaskDialogIndirect` from Common Controls 6). If the window can't be created, the install screen falls back to a task dialog.

### What's in the exe

`publish.ps1` builds the widget, the watcher and the Game mode helper into `publish\app\`, then zips that folder to
`obj/setup-payload/app.zip` and writes `payload.txt`, which lists every file with its SHA-256, its size and
its path inside the folder. Both are embedded as resources when the setup is published (`-p:PayloadDir=…`).
Setup only accepts a list of well-formed hashes and plain relative paths (no root, drive, `..`, empty or
dotted names, or stream syntax; at most 8 levels and 2000 files) that includes the widget and the watcher, and
it extracts only the files that list names.

The app folder includes `licenses\`: Binder's license (GPL 3.0 and its additional permission),
`THIRD-PARTY-NOTICES.txt`, and the license and notice files of the .NET runtime, the Windows App SDK and WebView2,
copied from the exact package versions the build used. `publish.ps1` stops if one is missing.

### Install and update steps (`Installer.cs`)

1. Read the payload.
2. Read the installed version from the Settings → Apps entry and choose Install, Update, Reinstall or the
   downgrade warning. The dialog carries the disclaimer, and its button is enabled only while "I've read and
   understood the disclaimer" is ticked. In silent mode, refuse a
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
      before the list existed installed only `Binder.exe`, `Binder.Watcher.exe` and `Uninstall.exe`.)
6. Write the Start menu shortcut with the Shell's own `ShellLink` object (source-generated COM, which works with
   AOT).
7. Write `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Binder` (name, version, publisher, icon,
   folder, quoted uninstall command, size, no modify or repair).
8. Restart the widget with `--background` if it was running, or offer to open it.

### Uninstall steps (`Uninstaller.cs`)

An exe can't delete itself while it runs. So `Uninstall.exe /uninstall` in the install folder only asks the
questions, copies itself to a new `%TEMP%\Binder-uninstall-<random>\` folder, starts that copy with
`/continue` and the answers, and exits. The temp copy then:

1. Ends the watcher if a game is being tracked (after asking), and quits the widget.
2. Unless you chose not to, runs the installed `Binder.exe --restore-shortcuts`. With no UI, this moves every
   imported shortcut back to where it came from and removes the games made from them. Shortcuts in its folder that
   no record names (an import an older version didn't finish) go back to your desktop too.
3. Removes the Start with Windows entry, but only if it starts the installed exe.
4. Removes the Start menu shortcut and the Settings → Apps entry.
5. Deletes the files `installed-files.txt` lists, plus the ones its own payload names (in case the list is
   gone), retrying while the first `Uninstall.exe` is still exiting, and their `.old`/`.new` leftovers. Every
   listed path is checked again, and nothing is deleted through a link. Then the folders they leave empty and
   the install folder, but only if it's empty. Anything you put there yourself stays.
6. Deletes the cache, `%LOCALAPPDATA%\Binder`, always. If you ticked "delete my data", also
   `%APPDATA%\Binder`. It doesn't follow links inside them (a link is removed, not entered). If some imported
   shortcuts couldn't go back to the desktop, they and their list are kept, and the final dialog says where.
7. After it exits, removes its own temp folder through `cmd.exe` from System32 a few seconds later, and after a
   completed uninstall `%TEMP%\BinderSetup.log` too (kept after a failure or a cancel). This only happens for
   paths of plain characters in the temp folder. Otherwise Windows' temp clean-up gets them.

Only one setup or uninstaller runs at a time (the named mutex `Local\Binder.Setup`): a second one started by
hand says so at once (exit code 6); a silent one (an updater) and the uninstaller's temp copy wait up to a minute.
If an update fails part-way, the widget it closed is started again.

### Security

- **No elevation, ever.** The manifest says `asInvoker`. This also stops Windows' installer detection, which
  would otherwise ask for admin rights for any exe with "setup" in its name.
- **DLL planting.** Installers usually run from Downloads, next to other downloaded files. The exe is linked
  with `/DEPENDENTLOADFLAG:0x800`, and `Main` calls `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`
  first. So every DLL comes from System32, and P/Invokes are pinned to System32 too.
- **Fixed paths only.** Every location is computed from your known folders. None is read back from the registry
  or a data file, so a tampered uninstall entry can't point the uninstaller at other files.
- **Deletes only what it wrote.** The files it listed in its folder, its own Start menu shortcut, registry
  entry and Run value, Binder's cache and its own log. Your data only when you ask. Never through links.
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
runs elevated, is installed to Program Files instead.

### The Game mode helper (milestone 14)

Game mode's service and network items need admin rights. That's the one approved exception to "never
elevate" (`docs/proposals.md` §11). The app, the watcher and this setup stay per-user and prompt-free. Setup
only ships `Binder.Elevated.exe` in the app folder; nothing runs elevated until you allow it:

1. **Allow (Settings › Game mode › Allow…):** one UAC prompt. The helper copies itself to
   `%ProgramFiles%\Binder\` (writable by admins only, so a program running as you can't swap it), checks the
   copy's SHA-256, creates `%ProgramData%\Binder\` with its own ACL (SYSTEM and Administrators write, Users
   read) and registers two scheduled tasks for your account: `Binder Apply <SID>` and `Binder Restore <SID>`
   (the second also at logon, to put back what a crash left). They run with highest privileges, only while
   you're signed in, and you may run them but not change them. Only accounts in the Administrators group can
   allow it; for a standard account the page says so.
2. **Update:** setup never elevates. When the installed helper is from another version, Settings › Game mode
   shows **Update…**, which is the same one prompt. Until then the old helper keeps working.
3. **Remove (Settings › Game mode › Remove it, or uninstall):** a UAC prompt for the installed copy's
   `--uninstall`. It first puts back whatever is still applied, then deletes your tasks, marker and result; when
   no other account uses it, also `%ProgramData%\Binder\` and itself (a running exe is removed at the next
   restart). Uninstall does this before deleting the app's files. If you decline, or uninstall silently (`/S`),
   the helper stays, and the final dialog says where and how to remove it later.
4. **Several Windows accounts:** the helper is per PC, the tasks per account. Removing it for one account
   leaves it for the others.

Its own security rules are in `CLAUDE.md` ("Game mode, admin part").

### Tested in CI

`tests/setup-test.ps1` runs on every push after the UI smoke test:

1. Install through the dialogs (Install must be disabled until the disclaimer box is ticked), then check the files against the published ones, the Start menu shortcut and
   the Settings → Apps entry.
2. Update silently while the widget and a watcher are running. The widget restarts on the new files, the
   watcher keeps running, and its file was moved aside and replaced.
3. Uninstall through the dialogs with "delete my data" ticked and a game still tracked. Check that everything
   is gone, an imported shortcut is back on the desktop, and the temp copy removed itself.

Screenshots of each dialog and `setup-metrics.txt` are in the CI results.
