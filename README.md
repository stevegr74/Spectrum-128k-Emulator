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
- `.z80` snapshot support
  - v1 loading implemented
  - v2/v3 page-block support implemented
- Keyboard matrix (8×5, active low)
- Screen rendering (`256×192`)
- Attribute handling (INK, PAPER, BRIGHT, FLASH)
- Frame-based FLASH implementation
- Frame pacing (~50Hz)
- Per-frame interrupt scheduling
- `.tap` tape loading
  - block parsing implemented
  - fake loader path available for direct testing/debugging
  - ROM-driven `LD-BYTES` loading path implemented
  - VERIFY path implemented
  - deterministic multi-block sequencing implemented
- Headless machine core (testable)
- Renderer separated from emulation
- Headless Z80 compliance runner (ZEXDOC)

---

## Current Status

Milestone 7 In Progress — Audio Pipeline Started

- Emulator boots into 128K menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs execute correctly
- Rendering pipeline stable and optimized
- FLASH behaviour implemented correctly
- Frame pacing stable (~50 FPS baseline)
- Interrupt cadence implemented
- 48K .sna snapshots load correctly
- .z80 snapshots load with v1 and v2/v3 support
- robocop128k.z80 has been tested successfully and is playable
- .tap loading works through the ROM-driven path
- AY register model implemented and wired to ports
- 48K beeper implemented via port 0xFE (speaker state + edge detection)

CPU Compliance
- ZEXDOC runs to completion in a headless runner
- All instruction groups pass
- DAA implementation fixed and validated

ZEXDOC is used as the authoritative validation source for CPU correctness.

Snapshot Support Progress (Milestone 5)
- 4-8K .sna loading implemented and verified (real game runs)
- .z80 snapshot support implemented (v1 + v2/v3)
- 128K paging and memory restoration working
- robocop128k.z80 verified working and playable

Tape Loading Progress (Milestone 6)
- .tap parsing implemented
- fake loader path implemented
- ROM-driven LD-BYTES path implemented
- VERIFY path implemented
- deterministic header/data sequencing implemented
- mounted tape rewind and multi-block progression implemented

Timing is still deliberately simplified at this stage. Pulse-level and higher-fidelity tape behaviour remain future work.

Audio Progress (Milestone 7)
- AY register model implemented
- AY port wiring implemented (0xFFFD / 0xBFFD)
- 48K beeper signal implemented via port 0xFE
- Audio output pipeline not yet implemented

---

## Architecture

The emulator is structured for clarity and testability:

- `Z80Cpu`  
  Instruction decoding, execution, and flag handling

- `Spectrum128Machine`  
  Memory, paging, keyboard, ROM mapping, interrupts, frame timing, and machine-level tape integration

- `SpectrumRenderer`  
  Converts screen memory into pixel output

- `SnapshotLoader` / `Z80SnapshotLoader`  
  Snapshot loading support

- `Tape/TapLoader`  
  `.tap` parsing, fake loading support, mounted tape state, and ROM-driven tape integration

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
- Snapshot loading
- Tape parsing
- VERIFY handling
- Tape sequencing and reset behaviour

ZEXDOC is used separately for full CPU validation.

---

## Snapshot Support

Current snapshot status:

- `.sna`
  - 48K loading implemented and verified

- `.z80`
  - v1 loading implemented
  - v2/v3 page-block support implemented

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

### Milestone 5 — Snapshots ✅
- 48K `.sna` loading complete and verified
- `.z80` support implemented (v1 + v2/v3)
- Real snapshot validated (robocop128k.z80 playable)

### Milestone 6 — Tape Loading ✅
- `.tap` parsing implemented
- fake loader path available
- ROM-driven tape loading path implemented
- VERIFY path implemented
- deterministic sequencing and rewind implemented

### Milestone 7 — Audio (In Progress)
- AY-3-8912 register emulation
- AY port wiring implemented
- 48K beeper implemented
- Shared audio output pipeline implemented
- Basic audio output working
- AY tone generation implemented
- AY envelope support implemented
- AY noise support implemented
- Basic mixing implemented
- Timing/performance polish still in progress

---

## Future Improvements

- ULA contention timing
- Scanline-accurate rendering
- Border effects
- Demo compatibility improvements
- Higher-fidelity tape timing
- Extended tape compatibility
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
