# T004 · Tests for `Format.RelativeDay`
Status: ready
Depends on: T001
Branch: agent/T004

## Goal

Add checks for `Format.RelativeDay` ("Today", "Yesterday", "3 days ago", or a short date) to the
`FormatTests()` method that T001 added. The function compares with today's date, so the inputs are built from
`DateTime.Today`.

## Files

- `tests/Binder.Tests/Tests.cs`

## Steps

1. Find the `FormatTests()` method. At the end of the method, after its last existing `Check(...)` line and
   before its closing `}`, add this helper line first (8 spaces indentation):

   ```csharp
           DateTimeOffset DaysAgo(int days) => new(DateTime.Today.AddDays(-days).AddHours(10));
   ```

2. Then add one line per row, in this exact form:

   ```csharp
           Check(Format.RelativeDay(<Input>) == <Expected>, "RelativeDay: <Description>");
   ```

   Note: `<Expected>` is C# code here, not plain text. Copy it exactly, quotes included.

| Input | Expected | Description |
|---|---|---|
| `DaysAgo(0)` | `"Today"` | `today` |
| `DaysAgo(-1)` | `"Today"` | `tomorrow also shows Today` |
| `DaysAgo(1)` | `"Yesterday"` | `yesterday` |
| `DaysAgo(3)` | `"3 days ago"` | `3 days ago` |
| `DaysAgo(6)` | `"6 days ago"` | `6 days ago` |
| `DaysAgo(7)` | `DaysAgo(7).LocalDateTime.ToString("M", System.Globalization.CultureInfo.CurrentCulture)` | `from 7 days a short date` |

For example, the first row becomes:

```csharp
        Check(Format.RelativeDay(DaysAgo(0)) == "Today", "RelativeDay: today");
```

## Done when

- `./scripts/check.ps1 -Task docs/tasks/T004-format-relativeday-tests.md` ends with `CHECK PASSED`.
- The test output has 6 lines starting with `PASS RelativeDay:`.

## Don't

- Don't add a `using` line at the top of the file; the full name `System.Globalization.CultureInfo` is used on
  purpose.
- Don't change `src/Binder/Views/Format.cs` or any existing check.
