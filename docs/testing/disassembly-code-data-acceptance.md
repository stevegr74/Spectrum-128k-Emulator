# Disassembly Code/Data Marking Acceptance

Use this short regression gate when changing debugger classification, listing,
search, cross-reference, persistence, or export behavior.

## Acceptance Checklist

| Area | Expected result |
| --- | --- |
| Controls | The listing context menu contains `Mark Selected As Code`, `Mark Selected As Data`, and `Clear Selected Marking`. |
| Data | Marked bytes show `DATA`, render as individual `DB` rows, use the data highlight, and are not decoded as control flow. |
| Code | Marked bytes show `CODE`, use the code highlight, decode normally, and can seed cross-reference discovery. |
| Clear | Clearing a marking restores normal decoding and an empty Type value. |
| Address spaces | Mapped, ROM-bank, and RAM-bank classifications remain independent, including mapped wraparound at `FFFFH`. |
| Persistence | Markings survive closing and reopening the debugger for the same machine/media context, but do not leak into another context. |
| Search and export | Mnemonic search skips confirmed data; copied and exported listings use `DB` and include `DATA`/`CODE` annotations. |
| Safety | Classification changes do not alter captured machine memory, execution controls, symbols, navigation, or breakpoints. |

## Automated Gate

Run the focused acceptance tests:

```text
dotnet test Spectrum128kEmulator.Windows.Tests/Spectrum128kEmulator.Windows.Tests.csproj --no-restore --filter "FullyQualifiedName~DisassemblyClassificationTests|FullyQualifiedName~DisassemblerFormInteractionTests"
```

Then run the complete solution in Release configuration:

```text
dotnet test Spectrum128kEmulator.sln -c Release --no-restore
```

The focused gate uses an STA WinForms instance, not just isolated model tests,
and verifies the visible listing state, context menu, navigation, persistence,
and immutable captured bytes.
