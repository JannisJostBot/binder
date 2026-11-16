# Task cards

Small, exactly specified tasks for coding agents (see `AGENTS.md`). One card = one branch = one reviewed merge.

## Workflow

1. An agent takes a `ready` card whose dependencies are `done`, works on the card's branch, runs
   `./scripts/check.ps1 -Task <card>` until it prints `CHECK PASSED`, commits and pushes.
2. CI runs the same rule check on every `agent/*` branch (with the card found by the branch name), plus the
   normal build and tests.
3. The owner reviews the diff, merges it, and sets the card's `Status:` to `done`.

## Writing a card

- One goal, at most a few files, all listed under `## Files` as `` - `path` `` (the check reads this list).
- Say exactly where to change things: the method, and the line to insert after (copied verbatim).
- Give the expected values; don't leave them to be worked out. Check them against the code first.
- Say how to know it's done: the check command and what the output must contain.
- List what's out of scope under `## Don't`.

Template:

```markdown
# T000 · Title
Status: ready | done
Depends on: none | T000
Branch: agent/T000

## Goal
## Files
- `path/to/file`
## Steps
## Done when
## Don't
```

## Cards

| Card | Title | Depends on | Status |
|---|---|---|---|
| T001 | Tests for `Format.Playtime` | none | ready |
| T002 | Tests for `Format.Duration` | T001 | ready |
| T003 | Tests for `Format.Delay` and `Format.Greeting` edges | T001 | ready |
| T004 | Tests for `Format.RelativeDay` | T001 | ready |
| T005 | Tests for the playtime chart labels and `WeekStart` | none | ready |
| T006 | Tests for `CoverColors.Initials` edge cases | none | ready |
| T007 | Site: fill in the placeholders whose values are known | none | done |
| T008 | Site: description for search engines and link previews | none | done |
| T009 | Site: "Skip to content" link for keyboard users | none | done |
| T010 | Site: FAQ answer "Where does Binder keep my data?" | none | done |
| T011 | Site: dark mode that follows the visitor's system setting | none | done |

Do the cards one at a time, in this order: T002–T004 add to the method T001 creates, and T007–T011 all change the
website, so parallel branches would conflict.
