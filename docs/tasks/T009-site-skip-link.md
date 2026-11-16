# T009 · Site: "Skip to content" link for keyboard users
Status: done
Depends on: none
Branch: agent/T009

## Goal

Keyboard and screen-reader users can jump past the navigation. The link is invisible until it gets focus with
the Tab key, then it appears in the top-left corner.

## Files

- `site/index.html`
- `site/styles.css`

## Steps

1. In `site/index.html`, find this line (it appears once):

   ```html
   <body>
   ```

   and insert this line directly after it (no indentation):

   ```html
   <a class="skip-link" href="#top">Skip to content</a>
   ```

2. In `site/styles.css`, add this block at the very end of the file, after one empty line:

   ```css
   .skip-link {
     position: absolute;
     left: 16px;
     top: -48px;
     z-index: 10;
     padding: 8px 16px;
     border-radius: 4px;
     background: var(--accent);
     color: var(--on-accent);
     font-family: 'Segoe UI Variable Text', 'Segoe UI', system-ui, -apple-system, sans-serif;
     font-weight: 600;
     text-decoration: none;
   }
   .skip-link:focus { top: 16px; }
   ```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T009-site-skip-link.md` ends with `CHECK PASSED`.
- Opening `site/index.html` in a browser and pressing Tab once shows a blue "Skip to content" button in the
  top-left corner; pressing Enter scrolls to the top section. Without pressing Tab, it isn't visible.

## Don't

- Don't change any other style or element.
