# T002 · Tests for `Format.Duration`
Status: ready
Depends on: T001
Branch: agent/T002

## Goal

Add checks for `Format.Duration` (the length of one session) to the `FormatTests()` method that T001 added.

## Files

- `tests/GameShelf.Tests/Tests.cs`

## Steps

1. Find the `FormatTests()` method. Add the checks below at the end of the method, after its last existing
   `Check(...)` line and before its closing `}`.
2. Write one line per row of the table, in this exact form (8 spaces indentation):

   ```csharp
           Check(Format.Duration(<Input>) == "<Expected>", "Duration: <Description>");
   ```

| Input | Expected | Description |
|---|---|---|
| `TimeSpan.Zero` | `< 1 min` | `0 shows < 1 min` |
| `TimeSpan.FromSeconds(59)` | `< 1 min` | `59 s shows < 1 min` |
| `TimeSpan.FromSeconds(60)` | `1 min` | `60 s shows 1 min` |
| `TimeSpan.FromSeconds(3599)` | `59 min` | `59 min 59 s rounds down` |
| `TimeSpan.FromHours(1)` | `1 h 00 min` | `1 h pads the minutes` |
| `new TimeSpan(2, 5, 0)` | `2 h 05 min` | `2 h 5 min pads the minutes` |
| `new TimeSpan(25, 30, 0)` | `25 h 30 min` | `over a day stays in hours` |

For example, the first row becomes:

```csharp
        Check(Format.Duration(TimeSpan.Zero) == "< 1 min", "Duration: 0 shows < 1 min");
```

## Done when

- `./check.ps1 -Task docs/tasks/T002-format-duration-tests.md` ends with `CHECK PASSED`.
- The test output has 7 lines starting with `PASS Duration:`.

## Don't

- Don't change `src/GameShelf/Views/Format.cs` or any existing check.
