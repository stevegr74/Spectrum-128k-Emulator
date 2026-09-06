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
- `.tzx` tape loading
- `.rzx` replay loading
- tape bootstrap and mounted-tape playback paths
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

Milestones 1-5 are complete. Tape compatibility and audio polish remain active work. Core/UI decoupling, clock-driven audio, and ULA/video timing are planned as the next coordinated architecture milestones.

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
- `.tap` loading now works for real game cases including `exolon.tap` and `Where Time Stood Still.tap`
- `.tzx` support is implemented and `Exolon.tzx` is verified working
- `Impossible Mission - Bugfix.tzx` now loads successfully, including its protected loader stage
- `Batman - Release 1.tzx` now loads through to the game path
- `.rzx` replay support is implemented and `aufmonty.rzx` plays back successfully
- emulation and audio submission now run on a background loop while the UI presents frames at a fixed 50Hz cadence
- muted turbo tape loads skip unnecessary per-frame audio-frame construction
- protected non-ROM live tape streams use a lower turbo ceiling than ordinary streaming tape
- the Spectrum palette now uses standard `0xD7` normal and `0xFF` bright intensity levels
- AY register model implemented and wired to ports
- 48K beeper implemented via port `0xFE` (speaker state + edge detection)
- AY tone, envelope, and noise output implemented
- Basic audio mixing implemented
- CPU/frame timing and interrupt handling improved through real-game testing
- Snapshot restore semantics now follow generic `.sna` and `.z80` format paths without snapshot-name hacks
- 48K `.z80` snapshots now use a dedicated format-based restore path that restores correct `JSWAPRIL.Z80` audio behaviour
- Jet Set Willy menu and in-game music now play with correct pitch and sequencing again
- Exolon now works correctly from both `exolon.sna` and `Exolon.z80`
- Exolon now also works from both `exolon.tap` and `Exolon.tzx`
- `Where Time Stood Still.tap` now loads and starts gameplay correctly
- App-side keyboard handling now uses a split model:
  - ordinary mapped Spectrum keys are applied directly from WinForms key events
  - composite cursor-style Spectrum chords use a small pulse/continuation bridge to keep menu input responsive
- Runtime ownership is now cleaner:
  - the background emulation loop owns live mutable machine state during normal execution
  - the UI presents copied snapshots instead of competing for long-held machine state
  - tape/snapshot loads pause emulation and start from a clean machine/input boundary
- Z80 core refactored into focused partial files without intended behaviour changes

CPU Compliance Baseline
- ZEXDOC runs to completion in a headless runner
- ZEXALL runs to completion in a headless runner
- All ZEXDOC/ZEXALL instruction groups pass in the current headless harness
- DAA implementation fixed and validated
- Exact flags for block-I/O instructions (`INI`, `IND`, `INIR`, `INDR`, `OUTI`, `OUTD`, `OTIR`, `OTDR`) remain a known gap and are tracked in the current development plan

ZEXDOC and ZEXALL are major CPU regression gates, but they are not treated as proof that every undocumented or I/O-data-dependent behaviour is complete.

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
- `.tzx` parsing implemented
- `.rzx` replay loading implemented
- fake loader path implemented
- ROM-driven `LD-BYTES` path implemented
- VERIFY path implemented
- deterministic header/data sequencing implemented
- mounted tape rewind and multi-block progression implemented
- format-based tape bootstrap paths implemented
- generic 128K tape-loader detection implemented for BASIC loaders that bank-switch via `POKE 23388,...`
- working verified examples now include:
  - `exolon.tap`
  - `Exolon.tzx`
  - `Impossible Mission - Bugfix.tzx`
  - `Where Time Stood Still.tap`
  - `Batman - Release 1.tzx`
  - `aufmonty.rzx`
- tape execution is selected from parsed tape structure rather than title-specific rules:
  - standard BASIC chains use the fast bootstrap path where their ROM side effects can be reproduced
  - mixed and protected tapes retain mounted signal playback for the live/protected stage
  - the ROM `LD-BYTES` trap remains the shared path for standard header/data loads and VERIFY
- protected BASIC bootstrap now honours Spectrum `CLEAR -1` semantics, preserving all RAM before a `USR` handoff; this is required by the Impossible Mission loader
- current generic Batman progress includes:
  - repeated loads in the same app session now behave consistently
  - raw-standard mixed tapes now use the bootstrap/hybrid mounted path instead of the older ROM-bootstrap-mounted path
  - mounted `IF ... THEN USR(...)` continuation steps directly evaluate safe numeric-variable expressions using BASIC-style default-zero semantics
  - mounted continuation variable reads now also decode integer-valued Spectrum floating-point numeric variables generically
  - mounted ROM data loads refresh the preserved BASIC variable snapshot before later continuation steps use it
  - early ROM sync-loop traps can now consume unstructured standard ROM-loadable data blocks, not just structured header/data contexts
- mounted continuations can resume during pauses before custom non-ROM blocks, but not before pending ROM-loadable blocks
- mounted continuations now also avoid resuming during pauses before unstructured standard ROM-loadable data blocks
- mounted `USR 0` handoff now resets CPU execution state more completely before entering ROM48, including stale interrupt/audio bookkeeping
- mounted tape idle/reset EAR polarity is now restored to the correct high-idle state
  - this was the real cause of the Exolon regression while Batman was being brought up
  - Batman now completes its mounted standard-data load deterministically and reaches the later game path instead of failing on the old black-screen route
- mounted live-tape playback now uses a generic wall-clock turbo path in the app while the tape is actively driving the EAR line
- emulated FE/tape pulse timing is kept exact during those live phases; the speed-up happens in the UI scheduler rather than by distorting tape data

Broader `.tzx` compatibility work still remains for additional protected/custom titles. The current active structural goal is expanding the same format, transport, ROM/trap, bootstrap-policy, and regression layers beyond the working Batman / Exolon / Impossible baseline.

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

## Current Development Plan

The active ULA work is isolated on `feature/ula-contention-border`; `master` remains the accepted tape and runtime baseline.

1. **Complete Z80 block-I/O flags before extending video timing.**
   - Replace the provisional `INI`/`IND`/`OUTI`/`OUTD` flag handling with the hardware-derived formulas for `S`, `Z`, `F5`, `H`, `F3`, `P/V`, `N`, and `C`.
   - Add table-driven tests across transfer data and register boundary cases.
   - Rerun ZEXDOC/ZEXALL as a regression gate, while recognising they do not fully validate port-data-dependent behaviour.

2. **Decouple the emulator core from WinForms before extending timing-sensitive output.**
   - Extract a headless `net8.0` engine library that owns machine execution, T-state progression, memory, tape, keyboard matrix state, and platform-neutral video/audio output.
   - Keep WinForms, `System.Drawing`, Windows audio APIs, menus, and frame scheduling in a frontend adapter rather than in the emulation core.
   - Expose the frame buffer as platform-neutral pixel data and make the existing Windows renderer an adapter, preserving a later cross-platform or WebAssembly frontend path.
   - Start with `net8.0` rather than forcing `netstandard2.0`; the latter would constrain the current modern runtime and does not itself make a WebAssembly frontend possible.

3. **Make audio production clock-driven from master T-states.**
   - Timestamp AY and beeper state changes against the machine T-state clock, then generate PCM through a deterministic sample-phase accumulator.
   - The frontend may queue already-produced PCM, but must not determine emulated audio pitch from wall-clock frame delivery.
   - Add tests that prove exact sample counts for a T-state interval and stable pitch when frontend scheduling is delayed or catches up.

4. **Build ULA contention as a timing model, not a title-specific compatibility tweak.**
   - Keep separate documented 48K and 128K timing profiles.
   - Cover contended memory, contended I/O, frame phase, and paged 128K banks with focused tests.
   - Do not merge a timing change that regresses the verified tape or snapshot matrix.

5. **Add border rendering and timestamped border effects.**
   - Render the visible Spectrum border around the active `256x192` display.
   - Record port `FE` colour changes at their emulated T-state and render them on the corresponding raster region.
   - Validate static borders first, then raster effects and their interaction with contention.

6. **Use the compatibility matrix as the merge gate.**
   - `exolon.tap` and `Exolon.tzx`
   - `Where Time Stood Still.tap`
   - `Impossible Mission - Bugfix.tzx`
   - `Batman - Release 1.tzx`
   - representative `.sna` / `.z80` snapshots, `aufmonty.rzx`, ZEXDOC, and ZEXALL

---

## Architecture

The emulator is structured for clarity and testability:

- `Spectrum128kEmulator.Core` (`net8.0`)
  Headless engine library containing machine execution, CPU, tape, snapshots/RZX, audio synthesis, and the platform-neutral ARGB frame buffer. It has no WinForms, `System.Drawing`, or Windows audio-device dependency.

- `Spectrum128kEmulator` (`net10.0-windows`)
  WinForms frontend that owns host input, menus, presentation scheduling, `Bitmap` blitting, and Windows audio output while consuming the core library.


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
  Current Windows pixel-output adapter; planned to consume a platform-neutral engine frame buffer

- `MainForm` / Windows audio pipeline
  Current WinForms frontend and platform audio output; planned to remain outside the headless emulator engine

- `SnapshotLoader` / `Z80SnapshotLoader`  
  Snapshot loading support

- `Tape/TapLoader` / `Tape/TzxLoader` / `Tape/TapeBlock`  
  `.tap` / `.tzx` parsing, fake loading support, mounted tape state, ROM-driven tape integration, and tape bootstrap handling

- `RzxLoader` / `RzxPlaybackSession`  
  `.rzx` replay loading and playback orchestration

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
- TZX parsing
- RZX replay parsing
- VERIFY handling
- Tape sequencing and reset behaviour
- banked tape-loader regression coverage
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

### Milestone 4 - Z80 Compliance Baseline Complete
- ZEXDOC runs to completion
- ZEXALL runs to completion
- All instruction groups passing
- Core CPU behaviour validated by compliance tests and targeted regressions
- Block-I/O flag completion remains active work because those flags are I/O-data-dependent and partly undocumented

### Milestone 5 - Snapshots Complete
- 48K `.sna` loading complete and verified
- `.z80` support implemented (v1 + v2/v3)
- Real snapshot validated (`robocop128k.z80` playable)
- Snapshot-format-specific restore paths now stabilised for current `.sna` and `.z80` support

### Milestone 6 - Tape Loading And Compatibility In Progress
- `.tap` parsing implemented
- `.tzx` parsing and structure-driven playback implemented
- fake loader path available
- ROM-driven tape loading path implemented
- VERIFY path implemented
- deterministic sequencing and rewind implemented
- Verified protected-loader examples include Exolon, Impossible Mission, and Batman
- Broader protected/custom TZX compatibility remains ongoing

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

### Milestone 8 - Core/UI Decoupling And Clock-Driven Audio (In Progress On Feature Branch)
- A platform-neutral ARGB frame buffer now sits below the Windows `Bitmap` adapter, with direct pixel regression coverage
- A headless `net8.0` core library now contains the machine, CPU, tape, snapshot/RZX, audio synthesis, and frame-buffer model; the WinForms frontend and ZEX runner consume it
- AY and beeper PCM sample counts now use a deterministic master-T-state accumulator; the frontend only queues produced PCM
- Regression tests prove that split execution slices produce the same sample count as a combined interval and preserve fractional 48K samples across frames

### Milestone 9 - ULA Timing And Border Effects (Ready To Merge From Feature Branch)
- Static visible borders and timestamped `OUT (FE)` raster border changes are rendered through the platform-neutral frame buffer
- 48K and 128K ULA contention coverage includes display phase, contended memory, contended I/O, and every paged 128K RAM bank
- The core/UI boundary and clock-driven audio contract are in place; the focused CPU, renderer, audio, machine, snapshot/RZX, and full tape regression suites are green

---

## Future Improvements

- Scanline-accurate rendering
- Demo compatibility improvements
- Higher-fidelity tape timing
- Extended tape compatibility
- Remaining menu/input responsiveness polish for games like Jet Set Willy
- Broader real-game validation
- Additional platform frontends, including a browser/WebAssembly adapter once the headless engine boundary is established

---

## Design Principles

- Standard library only (no external dependencies)
- Incremental development (no large rewrites)
- Behaviour verified with tests, ZEXDOC, and ZEXALL
- Clear separation between emulation and UI, with platform-neutral video and audio contracts
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
