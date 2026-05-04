# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# using only standard libraries.

This project focuses on correctness, clean architecture, and incremental development, with strong validation through automated tests and Z80 compliance tooling.

---

## Features

- Z80 CPU emulation
- Full ZEXDOC and ZEXALL CPU compliance (all instruction groups passing)
- 128K memory paging (port `0x7FFD`)
- ROM loading (128K + 48K modes)
- 48K `.sna` snapshot loading (verified)
- `.z80` snapshot support
  - v1 loading implemented
  - v2/v3 page-block support implemented
- Keyboard matrix (8x5, active low)
- Screen rendering (`256x192`)
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
- Shared audio output pipeline
- 48K beeper audio output
- AY-3-8912 audio support
  - register model implemented
  - port wiring implemented
  - tone generation implemented
  - envelope support implemented
  - noise support implemented
  - basic mixing implemented
- Headless machine core (testable)
- Renderer separated from emulation
- Headless Z80 compliance runner (ZEXDOC / ZEXALL)

---

## Current Status

Milestone 7 In Progress - Audio Output Working, Snapshot Compatibility Stabilized

- Emulator boots into 128K menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs execute correctly
- Rendering pipeline stable and optimized
- FLASH behaviour implemented correctly
- Frame pacing stable (~50 FPS baseline)
- Interrupt cadence implemented
- 48K `.sna` snapshots load correctly
- `.z80` snapshots load with v1 and v2/v3 support
- `robocop128k.z80` has been tested successfully and is playable
- `.tap` loading works through the ROM-driven path
- AY register model implemented and wired to ports
- 48K beeper implemented via port `0xFE` (speaker state + edge detection)
- AY tone, envelope, and noise output implemented
- Basic audio mixing implemented
- CPU/frame timing and interrupt handling improved through real-game testing
- Snapshot restore semantics now follow generic `.sna` and `.z80` format paths without snapshot-name hacks
- 48K `.z80` snapshots now use a dedicated format-based restore path that restores correct `JSWAPRIL.Z80` audio behaviour
- Jet Set Willy menu and in-game music now play with correct pitch and sequencing again
- Exolon now works correctly from both `exolon.sna` and `Exolon.z80`
- Remaining JSW polish is limited to occasional `ENTER` responsiveness on the menu after waiting a few seconds
- App-side keyboard bridging now defers host key release until the relevant Spectrum keyboard row has been scanned, with a bounded fallback to avoid sticky keys
- Z80 core refactored into focused partial files without intended behaviour changes

CPU Compliance
- ZEXDOC runs to completion in a headless runner
- ZEXALL runs to completion in a headless runner
- All instruction groups pass
- DAA implementation fixed and validated

ZEXDOC and ZEXALL are used as the authoritative validation sources for CPU correctness.

Snapshot Support Progress (Milestone 5)
- 48K `.sna` loading implemented and verified (real game runs)
- `.z80` snapshot support implemented (v1 + v2/v3)
- 128K paging and memory restoration working
- `robocop128k.z80` verified working and playable
- Snapshot restore now uses format-based generic paths
  - `.sna` restores interrupt state from header semantics
  - 48K `.z80` uses the dedicated generic 48K `.z80` machine path

Tape Loading Progress (Milestone 6)
- `.tap` parsing implemented
- fake loader path implemented
- ROM-driven `LD-BYTES` path implemented
- VERIFY path implemented
- deterministic header/data sequencing implemented
- mounted tape rewind and multi-block progression implemented

Timing is still deliberately simplified at this stage. Pulse-level and higher-fidelity tape behaviour remain future work.

Audio Progress (Milestone 7)
- AY register model implemented
- AY port wiring implemented (`0xFFFD` / `0xBFFD`)
- 48K beeper signal implemented via port `0xFE`
- Shared audio output pipeline implemented
- PCM audio output implemented using Windows APIs only
- AY tone generation implemented
- AY envelope support implemented
- AY noise support implemented
- Basic beeper + AY mixing implemented
- 48K beeper frame-boundary regression coverage added
- 48K audio clock handling aligned with snapshot mode
- Output buffering tuned to reduce low-level crackle
- `JSWAPRIL.Z80` regression testing restored correct music pitch and sequencing
- Timing/performance polish still in progress
- Remaining polish is mostly app-side input responsiveness rather than core audio generation

---

## Architecture

The emulator is structured for clarity and testability:


- `Z80/` / `Z80Cpu.cs`  
  Main CPU execution/orchestration layer, including the execution loop, interrupt handling, dispatch entry points, and core CPU state

- `Z80/` / `Z80Registers.cs`  
  Z80 register model, including main and shadow registers plus byte/word access helpers

- `Z80/` / `Z80Flags.cs`  
  Flag definitions and flag-related helpers, including parity and undocumented flag handling

- `Z80/` / `Z80AluHelpers.cs`  
  8-bit and 16-bit ALU helpers, overflow handling, NEG, and DAA support

- `Z80/` / `Z80BaseOperations.cs`  
  Non-prefixed opcode table setup and base instruction flow helpers

- `Z80/` / `Z80BitOperations.cs`  
  CB-prefixed rotate, shift, BIT, SET, and RES operations

- `Z80/` / `Z80ExtendedOperations.cs`  
  ED-prefixed instructions, including block operations and extended I/O behaviour

- `Z80/` / `Z80IndexedOperations.cs`  
  DD/FD-prefixed IX/IY operations and indexed opcode handling

- `Z80/` / `Z80CoreHelpers.cs`  
  Shared CPU helpers such as fetch, stack, register, and other core internal utilities

- `Z80/` / `Z80Disassembler.cs`  
  Trace/disassembly scaffolding, separated to allow future expansion into a fuller disassembler

- `Spectrum128Machine`  
  Memory, paging, keyboard, ROM mapping, interrupts, frame timing, machine-level tape integration, and audio state capture

- `SpectrumRenderer`  
  Converts screen memory into pixel output

- `SnapshotLoader` / `Z80SnapshotLoader`  
  Snapshot loading support

- `Tape/TapLoader`  
  `.tap` parsing, fake loading support, mounted tape state, and ROM-driven tape integration

- `MainForm`  
  Thin WinForms UI layer

- Test projects  
  CPU correctness, machine behaviour, rendering, audio behaviour, and regression tests

- `Spectrum128kEmulator.Z80Compliance`  
  Headless CPU validation using ZEXDOC and ZEXALL

---

## Running

Run the emulator:

```text
dotnet run
```

ROM files must be placed in:

```text
/ROMs
```

Expected ROMs:

- `128-0.rom`
- `128-1.rom`

---

## Tests

Run all tests:

```text
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
- AY register behaviour
- Audio sample generation
- Audio pipeline behaviour
- ZEXDOC and ZEXALL compliance validation via the dedicated runner

ZEXDOC and ZEXALL are used separately for full CPU validation.

---

## Snapshot Support

Current snapshot status:

- `.sna`
  - 48K loading implemented and verified
  - interrupt state restored from snapshot header semantics
  - generic format-based load path in use

- `.z80`
  - v1 loading implemented
  - v2/v3 page-block support implemented
  - interrupt state and interrupt mode restored from snapshot metadata
  - 48K `.z80` uses a dedicated generic restore path

Snapshots can be loaded via keyboard shortcuts in the UI.

---

## Manual Harness

A simple headless harness is included for debugging:

```text
dotnet run --project Spectrum128kEmulator.ManualHarness
```

This runs the emulator without UI and logs state.

> Intended for debugging, not performance measurement.

---

## Z80 Compliance Runner

A dedicated headless runner is included for CPU validation:

```text
dotnet run -c Release --project Spectrum128kEmulator.Z80Compliance -- test-assets/z80/zexdoc.com 7000000000
```

Notes:

- Runs ZEXDOC and ZEXALL in a minimal CP/M-style environment
- Fully uncapped execution
- Used for correctness validation, not timing accuracy
- All instruction groups currently pass

---

## Roadmap

### Milestone 1 - Keyboard & Menu Complete
- Keyboard matrix implemented
- 128K menu navigation working
- BASIC entry functional

### Milestone 2 - Rendering & FLASH Complete
- Attribute rendering (INK, PAPER, BRIGHT)
- FLASH behaviour implemented correctly
- Renderer optimisation

### Milestone 3 - Timing Baseline Complete
- Stable frame pacing (~50Hz)
- Frame-based execution loop
- Interrupt cadence established

### Milestone 4 - Z80 Compliance Complete
- ZEXDOC runs to completion
- ZEXALL runs to completion
- All instruction groups passing
- CPU behaviour validated against hardware-derived tests

### Milestone 5 - Snapshots Complete
- 48K `.sna` loading complete and verified
- `.z80` support implemented (v1 + v2/v3)
- Real snapshot validated (`robocop128k.z80` playable)
- Snapshot-format-specific restore paths now stabilised for current `.sna` and `.z80` support

### Milestone 6 - Tape Loading Complete
- `.tap` parsing implemented
- fake loader path available
- ROM-driven tape loading path implemented
- VERIFY path implemented
- deterministic sequencing and rewind implemented

### Milestone 7 - Audio (In Progress)
- AY-3-8912 register emulation
- AY port wiring implemented
- 48K beeper implemented
- Shared audio output pipeline implemented
- Basic audio output working
- AY tone generation implemented
- AY envelope support implemented
- AY noise support implemented
- Basic mixing implemented
- 48K snapshot audio path improved through regression testing
- `JSWAPRIL.Z80` music pitch and sequencing restored
- Timing/performance polish still in progress
- Remaining input responsiveness polish is outside the core audio path

---

## Future Improvements

- ULA contention timing
- Scanline-accurate rendering
- Border effects
- Demo compatibility improvements
- Higher-fidelity tape timing
- Extended tape compatibility
- Remaining menu/input responsiveness polish for games like Jet Set Willy
- Broader real-game validation

---

## Design Principles

- Standard library only (no external dependencies)
- Incremental development (no large rewrites)
- Behaviour verified with tests, ZEXDOC, and ZEXALL
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
