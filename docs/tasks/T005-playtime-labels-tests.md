# T005 · Tests for the playtime chart labels and `WeekStart`
Status: done (2026-11-17)
Depends on: none
Branch: agent/T005

## Goal

Add checks for the English chart labels and for `PlaytimeStats.WeekStart` across month and year ends, at the end
of the existing `PlaytimeTests()` method.

## Files

- `tests/Binder.Tests/Tests.cs`

## Steps

1. Find the `PlaytimeTests()` method. Its last check ends with this line (it appears once):

   ```csharp
               "chart labels are English whatever the locale");
   ```

   Add the new lines directly after it, before the method's closing `}`.

2. Write one line per row, in this exact form (8 spaces indentation). `<Expected>` is C# code: copy it exactly.

   ```csharp
           Check(<Call> == <Expected>, "<Description>");
   ```

| Call | Expected | Description |
|---|---|---|
| `PlaytimeStats.DayName(new DateTime(2026, 11, 2))` | `"Monday"` | `Labels: day name` |
| `PlaytimeStats.DayLetter(new DateTime(2026, 11, 5))` | `"T"` | `Labels: Thursday letter` |
| `PlaytimeStats.DayLetter(new DateTime(2026, 11, 8))` | `"S"` | `Labels: Sunday letter` |
| `PlaytimeStats.WeekLabel(new DateTime(2026, 1, 5))` | `"Jan 5"` | `Labels: week label without a leading zero` |
| `PlaytimeStats.MonthLabel(new DateTime(2026, 9, 1))` | `"Sep"` | `Labels: short month` |
| `PlaytimeStats.MonthName(new DateTime(2026, 9, 1))` | `"September 2026"` | `Labels: month and year` |
| `PlaytimeStats.WeekStart(new DateTime(2026, 11, 2, 8, 30, 0))` | `new DateTime(2026, 11, 2)` | `WeekStart: a Monday keeps its date, without the time` |
| `PlaytimeStats.WeekStart(new DateTime(2026, 12, 1))` | `new DateTime(2026, 11, 30)` | `WeekStart: across a month end` |
| `PlaytimeStats.WeekStart(new DateTime(2027, 1, 1))` | `new DateTime(2026, 12, 28)` | `WeekStart: across a year end` |

For example, the first row becomes:

```csharp
        Check(PlaytimeStats.DayName(new DateTime(2026, 11, 2)) == "Monday", "Labels: day name");
```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T005-playtime-labels-tests.md` ends with `CHECK PASSED`.
- The test output has 6 lines starting with `PASS Labels:` and 3 starting with `PASS WeekStart:`.

## Don't

- Don't change `src/Binder/Library/PlaytimeStats.cs` or any existing check.
