# T006 · Tests for `CoverColors.Initials` edge cases
Status: ready
Depends on: none
Branch: agent/T006

## Goal

Add checks for the initials on generated covers (games without cover art), for titles that are unusual.

## Files

- `tests/GameShelf.Tests/Tests.cs`

## Steps

1. In the `ShelfTests()` method, find this line (it appears once):

   ```csharp
           Check(CoverColors.Initials("half-life: alyx") == "HL", "initials are upper case, split on punctuation");
   ```

   Add the new lines directly after it.

2. Write one line per row, in this exact form (8 spaces indentation):

   ```csharp
           Check(CoverColors.Initials("<Title>") == "<Expected>", "Initials: <Description>");
   ```

| Title | Expected | Description |
|---|---|---|
| `The` | `T` | `The alone is kept` |
| `the witcher` | `W` | `lower-case the is skipped` |
| `Age of Empires II` | `AO` | `only the first two words` |
| `2077 Chronicles` | `2C` | `digits count as initials` |
| `Star_Wars` | `SW` | `underscore splits words` |
| `(Untitled)` | `?` | `no letter or digit first gives ?` |
| *(empty: `""`)* | `?` | `empty title gives ?` |

For example, the first row becomes:

```csharp
        Check(CoverColors.Initials("The") == "T", "Initials: The alone is kept");
```

and the last row becomes:

```csharp
        Check(CoverColors.Initials("") == "?", "Initials: empty title gives ?");
```

## Done when

- `./check.ps1 -Task docs/tasks/T006-initials-tests.md` ends with `CHECK PASSED`.
- The test output has 7 lines starting with `PASS Initials:`.

## Don't

- Don't change `src/GameShelf/Library/CoverColors.cs` or any existing check.
