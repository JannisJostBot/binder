# T007 · Site: fill in the placeholders whose values are known
Status: ready
Depends on: none
Branch: agent/T007

## Goal

Replace four placeholders on the website with their real values. The others (`[DOWNLOAD LINK]`,
`[PRICE]`, `[TEST PC]`) stay as they are.

## Files

- `site/index.html`

## Steps

Make exactly these four replacements. Each old text appears once in the file.

| Old text | New text |
|---|---|
| `© [YEAR] Jannis Jost` | `© 2026 Jannis Jost` |
| `Version [VERSION] ·` | `Version 0.1.0 ·` |
| `· [SIZE] MB ·` | `· 31 MB ·` |
| `href="[SOURCE LINK]"` | `href="https://github.com/JannisJostBot/game_shelf"` |

(0.1.0 is the version in `Directory.Build.props`; 31 MB is the size of `GameShelfSetup.exe` from CI on
2026-11-05, 32,558,080 bytes.)

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T007-site-known-placeholders.md` ends with `CHECK PASSED`.
- `Select-String -Path site/index.html -Pattern '\[(YEAR|VERSION|SIZE|SOURCE LINK)\]'` prints nothing.
- `Select-String -Path site/index.html -Pattern '\[(DOWNLOAD LINK|PRICE|TEST PC)\]'` prints 3 lines.

## Don't

- Don't fill in any other placeholder, and don't change any other text.
