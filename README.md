# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# using only standard libraries.

This project focuses on clean architecture, correctness, and incremental development, supported by automated tests and compliance tooling to enable future accuracy improvements.

---

## Features

- Z80 CPU emulation
- 128K memory paging (port `0x7FFD`)
- ROM loading (128K + 48K modes)
- 48K `.sna` snapshot loading
- `.z80` snapshot support in progress
- Keyboard matrix (8x5, active low)
- Screen rendering (`256x192`)
- Attribute handling (INK, PAPER, BRIGHT, FLASH)
- Frame-based FLASH implementation
- Stopwatch-based frame pacing (50Hz target)
- Per-frame interrupt scheduling
- Headless machine core (testable)
- Renderer separated from emulation
- Manual smoke harness for debugging
- Headless Z80 compliance runner for ZEXDOC

---

## Current Status

**Milestone 4 In Progress**

- Emulator boots into the 128 menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs run correctly
- FLASH rendering behaves correctly
- Rendering optimized using `LockBits`
- Stable frame pacing implemented (~50 FPS baseline)
- Per-frame interrupt cadence in place
- 48K `.sna` loading is implemented and verified
- `.z80` loading is in progress
- ZEXDOC runs to completion in the headless compliance runner
- Most ZEXDOC groups are now passing
- One remaining grouped compliance failure is still under investigation:
  - `<daa,cpl,scf,ccf>`

---

## Architecture

The emulator is structured for clarity and testability:

- `Z80Cpu`  
  Instruction execution, flags, prefix tables, and register state

- `Spectrum128Machine`  
  Memory, paging, keyboard, interrupts, ROM mapping, and frame-level timing

- `SpectrumRenderer`  
  Converts screen RAM and attributes into pixels

- `SnapshotLoader` / `Z80SnapshotLoader`  
  Snapshot loading support

- `MainForm`  
  Thin WinForms UI shell

- Test projects  
  Validate CPU, machine behaviour, rendering correctness, and compliance-focused edge cases

- `Spectrum128kEmulator.Z80Compliance`  
  Headless compliance runner used for ZEXDOC and instruction-behaviour debugging

---

## Running

Run the emulator:

```bash
dotnet run
```

ROM files must be placed in:

```
/ROMs
```

Expected ROMs:

- 128-0.rom
- 128-1.rom

---

## Tests

Run all tests:

```bash
dotnet test
```

Coverage includes:

- CPU instruction behaviour
- Memory paging
- Keyboard matrix
- FLASH timing
- Renderer correctness
- ROM boot smoke test
- Focused opcode tests
- Exhaustive sweep tests for tricky flag behaviour

---

## Snapshot Support

Current snapshot status:

- .sna
  48K loading implemented and verified
- .z80
  Supported in progress and still being expanded/refined

The emulator UI currently supports snapshot loading through keyboard shortcuts in the WinForms shell.

---

## Manual Harness

A simple headless harness is included for debugging:

```bash
dotnet run --project Spectrum128kEmulator.ManualHarness
```

This runs frames without UI and logs CPU/machine state.

> Note: The manual harness includes verbose debug logging and is intended for debugging, not performance measurement.

---

## Z80 Compliance Runner

A separate headless compliance runner is included for CPU verification work:

```bash
dotnet run -c Release --project Spectrum128kEmulator.Z80Compliance -- test-assets/z80/zexdoc.com 7000000000
```

This runner loads ZEXDOC in a minimal CP/M-style environment and executes uncapped in a tight loop.

Notes:

- It is intentionally headless and uncapped
- It is meant for CPU correctness/compliance work, not Spectrum timing validation
- Current status: ZEXDOC runs to completion, with one remaining grouped failure still being investigated

---

## Roadmap

### Milestone 1 — Input & Menu ✅
- Keyboard matrix
- 128 menu navigation
- Enter BASIC

### Milestone 2 — Rendering Fidelity ✅
- FLASH implementation
- Renderer cleanup and optimisation

### Milestone 3 — Baseline complete ✅
- Stable frame pacing (50Hz)
- Per-frame interrupt cadence
- Baseline host timing behaviour in place

### Milestone 4 — Snapshots (In Progress)
- Load `.z80`
- Load `.sna`
- Continue refining .z80 support and compatibility

### Milestone 5 — Tape Loading
- Basic `.tap` support
- ROM loader compatibility

### Milestone 6 — Audio
- AY-3-8912 register emulation
- Basic sound output

---

## Future Improvements (Stretch Goals)

- ULA contention timing
- Scanline-accurate rendering
- Border effects
- Demo compatibility improvements
- Stronger Z80 compliance coverage
- Further snapshot compatibility work

---

## Design Principles

- Standard library only (no external dependencies)
- Incremental development (no large rewrites)
- Behaviour verified with tests
- Clear separation between emulation and UI
- Headless tooling for reproducible debugging and compliance work

---

## Contributing

This is primarily a personal project for learning and development.

Contributions are welcome but limited to:

- Bug fixes
- Fixes must include tests where practical

See `CONTRIBUTING.md` for full details.

---

## License

MIT
