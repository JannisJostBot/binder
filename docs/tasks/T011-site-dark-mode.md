# T011 · Site: dark mode that follows the visitor's system setting
Status: ready
Depends on: none
Branch: agent/T011

## Goal

When the visitor's system is set to dark mode, the website uses dark colours, like the app does. Only the colour
variables change; nothing in `index.html` changes.

## Files

- `site/styles.css`

## Steps

1. Find the end of the `:root { … }` block at the top of `site/styles.css` (the first line that is only `}`).
2. Insert this block directly after it, after one empty line:

   ```css
   @media (prefers-color-scheme: dark) {
     :root {
       --accent: #4CC2FF;
       --accent-hover: #99EBFF;
       --accent-edge: rgba(255, 255, 255, 0.08);
       --on-accent: #0A1A24;
       --accent-tint: #1C2B3A;
       --accent-tint-mid: #24384C;
       --accent-tint-strong: #2E4A63;

       --bg: #202020;
       --bg-alt: #272727;
       --surface-2: #2D2D2D;
       --text: #FFFFFF;
       --text-2: #C5C5C5;
       --border: #3A3A3A;
       --border-strong: #4A4A4A;
       --divider: #333333;
       --bar: #5A5A5A;
       --track: #4A4A4A;

       --caution-bg: #433519;
       --caution-border: #5C4A22;
       --caution-icon: #B98A00;
     }
   }
   ```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T011-site-dark-mode.md` ends with `CHECK PASSED`.
- With the system (or the browser's developer tools: "Emulate prefers-color-scheme: dark") in dark mode, the
  page has a dark background, white headings and light blue buttons with dark text. In light mode it looks
  exactly as before.

## Don't

- Don't change `index.html` or the light colours in `:root`.
- Don't change the colours of the product shot in the hero; it's a picture of the app.
