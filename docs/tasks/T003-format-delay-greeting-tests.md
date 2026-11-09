# T003 · Tests for `Format.Delay` and `Format.Greeting` edges
Status: ready
Depends on: T001
Branch: agent/T003

## Goal

Add checks for the hide-delay label and the exact hours where the shelf's greeting changes, to the
`FormatTests()` method that T001 added.

## Files

- `tests/GameShelf.Tests/Tests.cs`

## Steps

1. Find the `FormatTests()` method. Add the checks below at the end of the method, after its last existing
   `Check(...)` line and before its closing `}`.
2. Write one line per row, in this exact form (8 spaces indentation):

   ```csharp
           Check(<Call> == "<Expected>", "<Description>");
   ```

| Call | Expected | Description |
|---|---|---|
| `Format.Delay(-5)` | `Instantly` | `Delay: negative shows Instantly` |
| `Format.Delay(0)` | `Instantly` | `Delay: 0 shows Instantly` |
| `Format.Delay(5)` | `5 s` | `Delay: 5 s` |
| `Format.Delay(30)` | `30 s` | `Delay: 30 s` |
| `Format.Greeting(new DateTime(2026, 1, 1, 0, 0, 0))` | `Good evening` | `Greeting: midnight is evening` |
| `Format.Greeting(new DateTime(2026, 1, 1, 11, 59, 0))` | `Good morning` | `Greeting: 11:59 is morning` |
| `Format.Greeting(new DateTime(2026, 1, 1, 17, 59, 0))` | `Good afternoon` | `Greeting: 17:59 is afternoon` |
| `Format.Greeting(new DateTime(2026, 1, 1, 23, 59, 0))` | `Good evening` | `Greeting: 23:59 is evening` |

For example, the first row becomes:

```csharp
        Check(Format.Delay(-5) == "Instantly", "Delay: negative shows Instantly");
```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T003-format-delay-greeting-tests.md` ends with `CHECK PASSED`.
- The test output has 4 lines starting with `PASS Delay:` and 4 starting with `PASS Greeting:`.

## Don't

- Don't change `src/GameShelf/Views/Format.cs` or any existing check (there are greeting checks in `Main()`
  already; leave them).
