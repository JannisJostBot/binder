# Release notes

One file per release, `<version>.json` (e.g. `0.2.0.json`), written by the owner before tagging
(`docs/proposals.md` §25, "Making a release"). `scripts/sign-release.ps1` reads it from the tagged commit and
copies it into the signed `release.json`; the app shows it under "What's new".

```json
{
  "en": ["Games from the Xbox app start faster.", "Fixes covers that stayed blank after a Steam update."],
  "de": ["Spiele aus der Xbox-App starten schneller.", "Behebt Cover, die nach einem Steam-Update leer blieben."],
  "fr": ["Les jeux de l'application Xbox démarrent plus vite.", "Corrige les jaquettes restées vides après une mise à jour de Steam."]
}
```

- `en`, `de` and `fr` are required, each 1 to 6 lines of 1 to 200 characters, plain text (no line breaks, no
  markup or links: they're shown as text). The same wording rules as the app's text (`CLAUDE.md`, "Text and
  languages"): short and plain, what changed, no selling, no exclamation marks, "Sie" / "vous".
- An urgent release (`sign-release.ps1 -Urgent`, both YubiKeys) also needs `"urgent": { "en": "…", "de": "…",
  "fr": "…" }`: one line saying what the security fix is, e.g. "Fixes a security problem in shortcut import." The
  script puts it first, so with it the other lines are at most 5.

## Collecting changes (`next.md`)

`next.md` collects what the next release changes, in plain English, while the work is merged. Whoever makes a
change that people using Binder will notice adds its line in the same commit (agents too: it's the one file in
this folder they may change). The owner turns it into `<version>.json` at release time and empties it.

- **Write for the person using Binder, not for developers.** Say what they can now do or what now works, in the
  app's words: "Covers load faster when the panel opens", not "Cache decoded bitmaps in `CoverCache`". No class,
  file, setting-key or issue names, no spec numbers.
- **New and changed:** one line per feature or visible change: a new setting, page, button or launcher, a changed
  default, something that's now noticeably faster or smaller.
- **Fixed:** only big bugs, the ones someone would have noticed and been stuck on: a crash, a game that didn't
  start or wasn't found, lost or wrong data (playtime, favorites, imported shortcuts), Game mode not putting
  something back, an update that failed, a security fix. Say what works now ("Games from the EA app start again
  after its latest update"), not what was wrong in the code.
- **Small fixes:** everything else people might notice but wouldn't miss (a misaligned label, a rare edge case, a
  wrong tooltip): no line of its own; set "Small fixes" to `yes`. It becomes one standard line at the end of the
  notes.
- **No line at all** for work nobody using the app sees: refactoring, tests, CI, the website, docs, task cards,
  build scripts. `scripts/check.ps1` (and CI) fails when a file under `src/` changes without `next.md`: for such a
  change, add a `Release-Notes: none <why>` line to one of the branch's commit messages (or pass
  `-NoReleaseNote` locally).
- One line, one change; edit an existing line instead of adding a second one about the same thing. The same
  wording rules as the app's text: short, plain, no selling, no exclamation marks.

The standard line, word for word (it goes last):

| | |
|---|---|
| `en` | Stability improvements and small fixes. |
| `de` | Verbesserungen der Stabilität und kleinere Fehlerbehebungen. |
| `fr` | Améliorations de la stabilité et corrections mineures. |

At release time, with more than 6 lines (5 for an urgent release, plus the standard line if it's there), the
owner merges related lines or moves the least visible ones into the standard line.
