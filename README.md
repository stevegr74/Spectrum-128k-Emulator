# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# using only standard libraries.

This project focuses on clean architecture, correctness, and incremental development, supported by automated tests to enable future accuracy improvements.

---

## Features

- Z80 CPU emulation
- 128K memory paging (port 0x7FFD)
- ROM loading (128K + 48K modes)
- Keyboard matrix (8x5, active low)
- Screen rendering (256x192)
- Attribute handling (INK, PAPER, BRIGHT, FLASH)
- Frame-based FLASH implementation
- Headless machine core (testable)
- Renderer separated from emulation
- Manual smoke harness for debugging

---

## Current Status

**Milestone 2 Complete**

- Emulator boots into 128 menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs run correctly
- FLASH rendering behaves correctly
- Rendering optimized using `LockBits`
- Regression tests in place (CPU, machine, renderer, ROM boot)

---

## Architecture

The emulator is structured for clarity and testability:

- `Z80Cpu`  
  Instruction execution and register state

- `Spectrum128Machine`  
  Memory, paging, keyboard, interrupts, and frame-level timing

- `SpectrumRenderer`  
  Converts screen RAM and attributes into pixels

- `MainForm`  
  Thin WinForms UI shell

- Test projects  
  Validate CPU, machine behaviour, and rendering correctness

---

## Running

```bash
dotnet run
```

ROM files must be placed in:

/ROMs

Expected:

128K ROM
48K ROM
Tests

Run all tests:

```bash
dotnet test
```

Coverage includes:

CPU instruction behaviour
Memory paging
Keyboard matrix
FLASH timing
Renderer correctness
ROM boot smoke test
Manual Harness

A simple headless harness is included for debugging:

```bash
dotnet run --project Spectrum128kEmulator.ManualHarness
```

This runs frames without UI and logs CPU/machine state.

---

## Roadmap
Milestone 1 — Input & Menu ✅
Keyboard matrix
128 menu navigation
Enter BASIC

Milestone 2 — Rendering Fidelity ✅
FLASH implementation
Renderer cleanup and optimisation

Milestone 3 — Timing (Next)
Stable frame pacing (50Hz)
Accurate interrupt cadence
Reduced host timing drift

Milestone 4 — Snapshots
Load .z80 and .sna

Milestone 5 — Tape Loading
Basic .tap support
ROM loader compatibility

Milestone 6 — Audio
AY-3-8912 register emulation
Basic sound output

Future Improvements (Stretch Goals)
ULA contention timing
Scanline-accurate rendering
Border effects
Demo compatibility improvements
Design Principles
Standard library only (no external dependencies)
Incremental development (no large rewrites)
Behaviour verified with tests
Clear separation between emulation and UI
Contributing

---

This is primarily a personal project for learning and development.

Contributions are welcome but limited to:

Bug fixes
Fixes must include tests

See CONTRIBUTING.md for full details.

License

MIT
