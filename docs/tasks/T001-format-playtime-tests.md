# T001 · Tests for `Format.Playtime`
Status: done
Depends on: none
Branch: agent/T001

## Goal

Add a new test method `FormatTests()` to the unit tests, with checks for `Format.Playtime` (the total playtime
text in the detail view). The checks describe what the code does today; don't change `Format.cs`.

## Files

- `tests/GameShelf.Tests/Tests.cs`

## Steps

1. In `Main()`, find this line (it appears once):

   ```csharp
           SpaceTests();
   ```

   and add this line directly after it, with the same indentation (8 spaces):

   ```csharp
           FormatTests();
   ```

2. Find this line (it appears once):

   ```csharp
       static void PayloadTests()
   ```

   and insert this method directly before it, followed by one empty line:

   ```csharp
       /// <summary>Text formatting in Views/Format.cs: playtime, durations, delays, greetings, days.</summary>
       static void FormatTests()
       {
           Check(Format.Playtime(TimeSpan.Zero) == "1 min", "Playtime: 0 shows 1 min");
           Check(Format.Playtime(TimeSpan.FromMinutes(59)) == "59 min", "Playtime: 59 min");
           Check(Format.Playtime(TimeSpan.FromSeconds(3599)) == "59 min", "Playtime: 59 min 59 s rounds down");
           Check(Format.Playtime(TimeSpan.FromHours(1)) == "1 h 0 min", "Playtime: 1 h shows 1 h 0 min");
           Check(Format.Playtime(new TimeSpan(4, 5, 0)) == "4 h 5 min", "Playtime: minutes not padded under 10 h");
           Check(Format.Playtime(new TimeSpan(9, 59, 0)) == "9 h 59 min", "Playtime: 9 h 59 min");
           Check(Format.Playtime(TimeSpan.FromHours(10)) == "10 h", "Playtime: from 10 h only hours");
           Check(Format.Playtime(new TimeSpan(123, 45, 0)) == "123 h", "Playtime: 123 h 45 min shows 123 h");
       }
   ```

## Done when

- `./check.ps1 -Task docs/tasks/T001-format-playtime-tests.md` ends with `CHECK PASSED`.
- The test output has 8 lines starting with `PASS Playtime:`.

## Don't

- Don't change `src/GameShelf/Views/Format.cs` or any existing check.
- Don't move the existing `Format.` checks in `Main()`.
