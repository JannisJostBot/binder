# Contributing

Thanks for wanting to help. Binder is a small project run by one person, so please read this before you open
a pull request.

## Code contributions aren't accepted

Pull requests that change the app's code will be closed without review. There isn't enough time to review them
properly, and an unreviewed change is a risk to everyone who installs the app.

Binder's own code is written by an AI (see the disclaimer in the [README](README.md)) and isn't often reviewed
line by line either. It still carries less risk than code from outside: it's written within this project's rules
(`CLAUDE.md`), directed and tested by the maintainer, and comes from one known source. Code from someone else
would need a careful review that there's no time for.

There are two exceptions.

### 1. Translations and wording

Better wording and translation fixes are welcome. The app's text is in `src/GameShelf/Strings/`:

- `Strings.resx`: English. Each entry has a comment that says where the text appears and what `{0}`, `{1}` hold.
- `Strings.de.resx`, `Strings.fr.resx`: German and French.
- The installer's text is in `src/GameShelf.Setup/Text.cs`.

When you change text:

- Change only the `<value>` of existing entries. Keep every `{0}`, `{1}` placeholder, and keep brand names
  (Steam, Epic Games, Binder).
- Use the words Windows itself uses in that language for its own settings and buttons. German uses "Sie",
  French "vous", like Windows.
- Keep it short and plain: say what something does, not why it's good.
- In the pull request, say which language you're a native or fluent speaker of.

For a new language, open an issue first and ask.

### 2. Security fixes

Fixes for security problems are welcome. Please **don't report a vulnerability in a public issue**. Use
"Report a vulnerability" on the repository's Security tab, so it can be fixed before it's public. A fix can go in
the same report, or in a pull request once that's agreed.

## Licensing of contributions

Binder is licensed under the GNU GPL, version 3, with an additional permission for the libraries it bundles
(`LICENSE`, `LICENSE-EXCEPTION.txt`). By sending a contribution (a translation, wording or a security fix), you
agree that it's licensed under the same terms, and that the maintainer may also license it under other terms in
later versions, so Binder can change its license without asking every contributor. Versions already released
keep their license.

## Feature requests

You can suggest features in an issue. Binder is a launcher, not a game toolbox, and stays small on purpose.
A request has a chance if it:

- helps you start, find or manage your installed games,
- looks and works like part of Windows 11 (Fluent controls, no custom look),
- costs nothing while you play and next to nothing while idle (no background polling, services or timers),
- doesn't need new third-party packages, accounts or network access.

Requests that add social features, overlays, stats dashboards, mod managers, store browsing or other extras
will be declined. Bug reports are always welcome: include your Windows version, the launcher involved and what
you expected to happen.
