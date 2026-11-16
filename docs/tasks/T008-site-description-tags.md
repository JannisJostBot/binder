# T008 · Site: description for search engines and link previews
Status: done
Depends on: none
Branch: agent/T008

## Goal

Add the page description that search engines and chat apps show for a link to the site.

## Files

- `site/index.html`

## Steps

1. Find this line (it appears once):

   ```html
     <title>Binder for Windows</title>
   ```

2. Insert these four lines directly after it, with the same indentation (2 spaces):

   ```html
     <meta name="description" content="Binder lists the games you've installed with Steam, Epic, GOG and other launchers, and starts them with one click. For Windows 10 and 11.">
     <meta property="og:title" content="Binder for Windows">
     <meta property="og:description" content="Binder lists the games you've installed with Steam, Epic, GOG and other launchers, and starts them with one click. For Windows 10 and 11.">
     <meta property="og:type" content="website">
   ```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T008-site-description-tags.md` ends with `CHECK PASSED`.
- `Select-String -Path site/index.html -Pattern '<meta (name|property)='` prints 4 lines.

## Don't

- Don't add an `og:image` or `og:url` tag; they need the site's final address, which doesn't exist yet.
