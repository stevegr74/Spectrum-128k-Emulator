# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# using only standard libraries.

This project focuses on correctness, clean architecture, and incremental development, with strong validation through automated tests and Z80 compliance tooling.

---

## Features

- Z80 CPU emulation
- Full ZEXDOC CPU compliance (all instruction groups passing)
- 128K memory paging (port `0x7FFD`)
- ROM loading (128K + 48K modes)
- 48K `.sna` snapshot loading (verified)
- `.z80` snapshot support (in progress)
- Keyboard matrix (8×5, active low)
- Screen rendering (`256×192`)
- Attribute handling (INK, PAPER, BRIGHT, FLASH)
- Frame-based FLASH implementation
- Frame pacing (~50Hz)
- Per-frame interrupt scheduling
- Headless machine core (testable)
- Renderer separated from emulation
- Headless Z80 compliance runner (ZEXDOC)

---

## Current Status

**Milestone 4 Complete — Z80 Compliance Achieved**

- Emulator boots into 128K menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs execute correctly
- Rendering pipeline stable and optimized
- FLASH behaviour implemented correctly
- Frame pacing stable (~50 FPS baseline)
- Interrupt cadence implemented

### CPU Compliance

- ZEXDOC runs to completion in a headless runner
- All instruction groups pass
- DAA implementation fixed and validated

ZEXDOC is used as the authoritative validation source for CPU correctness.

### Snapshot Support Progress (Milestone 5)

- 48K `.sna` loading implemented and verified (real game runs)
- `.z80` snapshot support started (v1 loader in progress)

---

## Architecture

The emulator is structured for clarity and testability:

- `Z80Cpu`  
  Instruction decoding, execution, and flag handling

- `Spectrum128Machine`  
  Memory, paging, keyboard, ROM mapping, interrupts, and frame timing

- `SpectrumRenderer`  
  Converts screen memory into pixel output

- `SnapshotLoader` / `Z80SnapshotLoader`  
  Snapshot loading support

- `MainForm`  
  Thin WinForms UI layer

- Test projects  
  CPU correctness, machine behaviour, rendering, and regression tests

- `Spectrum128kEmulator.Z80Compliance`  
  Headless CPU validation using ZEXDOC

---

## Running

Run the emulator:

```
dotnet run
```

ROM files must be placed in:

```
/ROMs
```

Expected ROMs:

- `128-0.rom`
- `128-1.rom`

---

## Tests

Run all tests:

```
dotnet test
```

Test coverage includes:

- CPU instruction behaviour
- Memory paging
- Keyboard matrix
- FLASH timing
- Renderer correctness
- ROM boot smoke tests
- Focused opcode regression tests

ZEXDOC is used separately for full CPU validation.

---

## Snapshot Support

Current snapshot status:

- `.sna`
  - 48K loading implemented and verified

- `.z80`
  - Support in progress (v1 currently being expanded)

Snapshots can be loaded via keyboard shortcuts in the UI.

---

## Manual Harness

A simple headless harness is included for debugging:

```
dotnet run --project Spectrum128kEmulator.ManualHarness
```

This runs the emulator without UI and logs state.

> Intended for debugging, not performance measurement.

---

## Z80 Compliance Runner

A dedicated headless runner is included for CPU validation:

```
dotnet run -c Release --project Spectrum128kEmulator.Z80Compliance -- test-assets/z80/zexdoc.com 7000000000
```

Notes:

- Runs ZEXDOC in a minimal CP/M-style environment
- Fully uncapped execution
- Used for correctness validation, not timing accuracy
- All instruction groups currently pass

---

## Roadmap

### Milestone 1 — Keyboard & Menu ✅
- Keyboard matrix implemented
- 128K menu navigation working
- BASIC entry functional

### Milestone 2 — Rendering & FLASH ✅
- Attribute rendering (INK, PAPER, BRIGHT)
- FLASH behaviour implemented correctly
- Renderer optimisation

### Milestone 3 — Timing Baseline ✅
- Stable frame pacing (~50Hz)
- Frame-based execution loop
- Interrupt cadence established

### Milestone 4 — Z80 Compliance ✅
- ZEXDOC runs to completion
- All instruction groups passing
- CPU behaviour validated against hardware-derived tests

### Milestone 5 — Snapshots (In Progress)
- 48K `.sna` loading complete and verified
- `.z80` snapshot support started (v1 loader in progress)
- Further compatibility and compression support to be added

### Milestone 6 — Tape Loading
- Basic `.tap` support
- Initial implementation via ROM loader path

### Milestone 7 — Audio
- AY-3-8912 register emulation
- Basic audio output

---

## Future Improvements

- ULA contention timing
- Scanline-accurate rendering
- Border effects
- Demo compatibility improvements
- Extended snapshot compatibility
- ZEXALL / deeper compliance validation

---

## Design Principles

- Standard library only (no external dependencies)
- Incremental development (no large rewrites)
- Behaviour verified with tests and ZEXDOC
- Clear separation between emulation and UI
- Headless tooling for reproducible debugging

---

## Contributing

This is primarily a personal project for learning and development.

Contributions are welcome for:

- Bug fixes
- Improvements with accompanying tests

See `CONTRIBUTING.md` for details.

---

## License

MIT
