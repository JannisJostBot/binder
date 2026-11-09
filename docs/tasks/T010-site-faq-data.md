# T010 · Site: FAQ answer "Where does Binder keep my data?"
Status: ready
Depends on: none
Branch: agent/T010

## Goal

Add one question to the website's FAQ, before "What does it cost?".

## Files

- `site/index.html`

## Steps

1. Find the line that contains `Battle.net, Riot, Amazon Games` (it appears once). The line after it is:

   ```html
           </details>
   ```

2. Insert this block directly after that `</details>` line. Copy it exactly; the indentation is 8 spaces for
   `<details>` and 10 for the lines inside:

   ```html
           <details style="background: var(--bg); border: 1px solid var(--border); border-radius: 4px">
             <summary style="min-height: 64px; padding: 0 20px; display: flex; align-items: center; justify-content: space-between; gap: 16px; font-size: 16px; font-weight: 600">Where does Binder keep my data?<svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9l6 6 6-6"></path></svg></summary>
             <p style="margin: 0; padding: 16px 20px 20px; border-top: 1px solid var(--divider); color: var(--text-2); font-size: 15px">On your PC, in your user folder: your library, playtime, settings and imported shortcuts in %APPDATA%\GameShelf, and the cover pictures in %LOCALAPPDATA%\GameShelf. When you uninstall Binder, it asks whether to delete them.</p>
           </details>
   ```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T010-site-faq-data.md` ends with `CHECK PASSED`.
- In a browser, the FAQ has 8 questions, and "Where does Binder keep my data?" is the one directly above "What
  does it cost?". Clicking it opens the answer.

## Don't

- Don't write "Binder" in the folder names: the folders are still called `GameShelf`.
- Don't change the other questions.
