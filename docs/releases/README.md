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
