# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# with no third-party runtime dependencies.

This project focuses on correctness, clean architecture, and incremental development, with strong validation through automated tests and Z80 compliance tooling.

---

## Features

- Z80 CPU with passing ZEXDOC and ZEXALL instruction groups
- Explicit 48K and 128K models, 128K paging, keyboard matrix, interrupts, and a model-specific timing baseline
- Spectrum display with INK, PAPER, BRIGHT, FLASH, borders, and fixed 1x/Scale2x/Scale3x modes
- 48K beeper and AY-3-8912 tone, noise, envelope, and mixing through a clock-driven audio pipeline
- 48K/128K `.sna`, `.z80` v1/v2/v3, and `.rzx` loading/replay
- `.tap` and `.tzx` parsing, fast bootstrap, ROM-driven loading, protected/mounted playback, VERIFY, and multi-block sequencing
- Manual/resumable tape transport with 48K stop-marker support and persistent status feedback
- Platform-neutral headless core with Windows presentation/audio adapters
- Read-only `F6` disassembler with immutable captures, search, history, branch navigation, copy, and export
- In-memory `F7`/`F8` Quick State save and restore
- Headless manual diagnostics and Z80 compliance runners

---

## Current Status

`master` is a stable, playable baseline with a headless core, clock-driven audio,
explicit machine models, resumable tape transport, a paused disassembler, and a
temporary Quick State slot. ZEXDOC and ZEXALL pass in the headless compliance
runner, although they are not proof that every undocumented hardware interaction
is complete.

Manually tested titles, formats, and reached checkpoints are tracked in
[Verified Games And Media](VERIFIED_GAMES.md).

Current limitations and active work:

- opcode/immediate fetches, ordinary/indexed/block/stack memory accesses, port I/O, border, and floating-bus timing now use model-specific machine-cycle positions; published external diagnostic validation remains active Milestone 16 work
- active-screen rendering is frame-snapshot based rather than beam-aware; Milestone 17 addresses this
- exact audio timing and turbo-to-real-time tape handoff still need polish in some titles
- broader protected/custom TZX compatibility remains ongoing

### Implementation Notes

- Fast tape bootstrap is capability-gated. Unsupported initial or chained BASIC control flow, including protected handoff directly into raw pulses, falls back to a clean real-ROM autorun path with the remaining tape mounted.
- Address-based ROM tape services are enabled only for the fingerprinted bundled ROM pair; an unknown ROM pair uses real-time mounted pulse/EAR playback instead of assuming compatible internal addresses.
- ROM-driven tape handoffs preserve consumed-block pauses, exact TZX pause signal transitions, and zero-duration stop markers.
- TZX pulse and pause timings use their 3.5 MHz tape-time basis and are scaled to the selected machine clock; unstructured ROM loads return to their actual caller so banked multiload stages can continue safely.
- Mounted playback detects stable keyboard/HALT handoffs, pauses at multiload boundaries, and preserves custom ROM flag and LOAD/VERIFY state across accelerated blocks.
- Protected playback and Quick State preserve precise tape pulse, EAR, loader-continuation and replay positions.
- Mounted-loader continuation state, BASIC workspace preservation, resume gating, and Quick State capture are isolated from the machine in a dedicated controller.
- The background emulation loop owns mutable machine state; the UI consumes copied snapshots and uses nested pause leases for safe inspection.
- The undocumented Z80 Q latch is tracked by flag-writing semantics, including prefix and interrupt boundaries, with focused regressions alongside ZEXDOC/ZEXALL.
- Model-specific ULA profiles now drive contention, floating-bus fetches, frame timing, and raster mapping; original 128K/+2 floating-bus reads follow the selected bank 5 or bank 7 screen, and `IN (0xFFFD)` reads the selected AY register.
- Border, beeper, and AY writes are timestamped at the completed I/O cycle, including contended and repeating block-I/O paths; border changes are reconstructed in hardware-sized eight-pixel latch groups without stretching visible pixels across horizontal flyback.
- Contended opcode, prefix, and immediate-byte fetches use their individual machine-cycle offsets; corrected indexed-instruction totals cover ignored `DD`/`FD` prefixes, absolute IX/IY loads and stores, stack exchange, and stack-pointer transfer.

## Development Priorities

Each feature is developed on its own branch. Current work is ULA/port conformance
(Milestone 16), followed by beam-aware video (17), debugger execution controls
(18), and bank-aware symbolic disassembly (19). Tape compatibility and the
turbo-to-realtime audio transition continue in parallel, with the verified media
matrix plus ZEXDOC/ZEXALL used as regression gates.

---

## Architecture

The solution separates the platform-neutral emulator from the Windows frontend
and diagnostic tools:

```text
Spectrum128kEmulator/
|-- Spectrum128kEmulator.Core/             net8.0 platform-neutral emulator
|   |-- Audio/                             AY/beeper synthesis and sample clock
|   |-- Tape/                              TAP/TZX parsing, transport, and bootstrap policy
|   |   |-- MountedTape.cs                 pulse/EAR playback and ROM-load traps
|   |   |-- TapLoader.cs                   TAP orchestration and load-strategy planning
|   |   |-- TapLoader.BasicBootstrap.cs    BASIC bootstrap analysis and execution
|   |   |-- TapeLoadContracts.cs           load plans, results, and transport state
|   |   `-- MountedLoadContinuationController.cs  ROM-profile-aware loader resume policy
|   |-- Z80/                               CPU execution plus side-effect-free instruction decoding
|   |-- Ula/                               model timing profiles and ULA timing policy
|   |-- Spectrum128Machine.cs              machine orchestration, memory, paging, ULA timing, and input
|   |-- Spectrum128Machine.Diagnostics.cs  trace capture, watches, and diagnostic dumps
|   |-- SpectrumRomProfile.cs              fingerprinted ROM capabilities and internal entry points
|   |-- SpectrumFrameBuffer.cs             platform-neutral ARGB frame buffer
|   |-- BorderFrame.cs                     timestamped border events
|   |-- SnapshotLoader.cs                  SNA snapshot loading
|   |-- Z80SnapshotLoader.cs               Z80 snapshot loading
|   |-- RzxLoader.cs                       RZX replay loading
|   `-- RzxPlaybackSession.cs              RZX playback orchestration
|
|-- Audio/                                 Windows audio pipeline and output adapters
|-- Disassembly/                           read-only debugger capture, navigation, search, and export
|-- Host/                                  background loop, pause ownership, and UI snapshots
|-- Forms/                                 main window partials and shortcut-reference dialog
|-- Input/                                 WinForms-to-Spectrum keyboard bridge
|-- Video/                                 display modes, pixel-art scaling, and presentation adapter
|-- Program.cs                             Windows application entry point
|-- Spectrum128kEmulator.csproj            net10.0-windows frontend project
|-- VERIFIED_GAMES.md                      manual media compatibility record
|
|-- Spectrum128kEmulator.Tests/            net8.0 Core unit and regression tests
|-- Spectrum128kEmulator.Windows.Tests/    Windows presentation and host tests
|-- Spectrum128kEmulator.ManualHarness/    net8.0 Core-only diagnostic and capture tool
|-- Spectrum128kEmulator.Z80Compliance/    net8.0 ZEXDOC/ZEXALL compliance runner
|-- ROMs/                                  required 128K ROM images
|-- test-assets/                           checked-in test programs and fixtures
`-- tmp/                                   ignored local probes, captures, and scratch artifacts
```

`Spectrum128kEmulator.Core` has no WinForms, `System.Drawing`, or Windows
audio-device dependency. The `net10.0-windows` frontend consumes Core for
emulation state and produces Windows-specific video and audio output. The
compliance runner depends only on Core, while the test and manual-harness
projects also depend only on Core. Windows presentation and host behavior is
isolated in `Spectrum128kEmulator.Windows.Tests`.

---

## Running

Run the emulator:

```text
dotnet run
```

ROM files must be present in the `ROMs` directory copied beside the executable
(the project does this automatically for files under its local `ROMs` folder):

```text
ROMs/
```

Expected ROMs:

- `128-0.rom`
- `128-1.rom`

The emulator starts in fixed-size `2x Enhanced` mode. `F4` cycles 1x native
(`320x240`), Scale2x (`640x480`), and Scale3x (`960x720`). Right-clicking the
display exposes the same controls in function-key order.

| Key | Action |
| --- | --- |
| `F1` | Open the control reference; emulation pauses while it is open |
| `F2` | Toggle bottom-left FPS, frame, model, tape, and display diagnostics |
| `F3` | Reset into explicit 128K or 48K mode |
| `F4` | Cycle fixed display size |
| `F5` | Stop or resume the mounted tape without ejecting it |
| `F6` | Open the paused, read-only disassembler capture |
| `F7` / `F8` | Save or restore the temporary in-memory Quick State |
| `F9` | Load a 48K or 128K `.sna` snapshot |
| `F10` | Load a `.z80` snapshot or `.rzx` recording |
| `F11` | Mount a `.tap` or `.tzx` tape image |
| `F12` | Write a machine diagnostic dump |

Tape changes show a top-left badge: full text for three seconds, then a compact
playing icon; paused/auto-stopped icons hide after five seconds. The title bar
shows the current media name, while transport state remains in the temporary
overlay and optional `F2` diagnostics. The temporary Quick State slot restores
the associated media name as well as CPU, RAM banks, paging, ULA/audio frame
state, tape, and RZX position; it survives media loads during the session and is
discarded on exit.

The disassembler captures the mapped logical 64K plus `PC`, model, ROM, and
paging context; it does not yet expose every physical bank. Its own Help text
documents history, search, branch following, refresh, copy, and export controls.
Help and file choosers use nested-safe pause ownership.

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
- Tape transport pause/resume and 48K stop-marker behaviour
- 48K/128K machine-model behavior
- complete 48K/128K contention phases, boundaries, I/O cases, and contended-bank behavior
- 48K/128K floating-bus fetches, including 128K shadow-screen bank selection
- exact port event timestamps, block-I/O timing, and border latch/raster mapping
- timed opcode, prefix, immediate, ordinary, indexed, block, stack, and interrupt-vector memory cycles
- AY register write and selected-register port-read behaviour
- Audio sample generation
- Audio pipeline behaviour
- Z80 instruction decoding, disassembly navigation/search, listing export, settings, and snapshot behavior
- Quick State deterministic machine, tape-cursor, and RZX-cursor restoration
- ZEXDOC and ZEXALL compliance validation via the dedicated runner

ZEXDOC and ZEXALL are used separately for full CPU validation.

---

## Snapshot Support

Current snapshot status:

- `.sna`
  - 48K file-format loading implemented and verified
  - 48K-format snapshots can run on the selected 48K or 128K hardware model
  - 128K file-format loading restores all RAM banks, paging state, and the explicit program counter
  - standard and duplicate-current-page 128K layouts are supported; TR-DOS snapshots are rejected because no TR-DOS ROM is emulated
  - a converted 128K Renegade snapshot has passed exact representable-state round-trip comparison and an execution smoke test
  - interrupt state restored from snapshot header semantics
  - generic format-based load path in use

- `.z80`
  - v1 loading implemented
  - v2/v3 page-block support implemented
  - 128K paging and RAM-bank restoration supported for applicable v2/v3 snapshots
  - interrupt state and interrupt mode restored from snapshot metadata
  - 48K `.z80` uses a dedicated generic restore path

Snapshots can be loaded with `F9` (`.sna`) and `F10` (`.z80`) in the UI.

---

## Manual Harness

A simple headless harness is included for debugging:

```text
dotnet run --project Spectrum128kEmulator.ManualHarness
```

This runs the emulator without UI and logs state.

Its generic diagnostics accept `frames=`, `model=48|128`, `quiet=1`, `dumpblocks=1`, `strategy=`,
scheduled key/register/memory events, memory-write watches, instruction traces, and
execution breakpoints. It writes a machine dump and BMP frame image only for the run being
investigated; game-specific probes are deliberately kept out of the harness.

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

Milestone numbers record delivery order. Milestones 6 and 7 stay open for wider
compatibility and polish; completed milestones remain regression baselines.

| Milestone | Status | Outcome |
| --- | --- | --- |
| 1 | Complete | Keyboard matrix, 128K menu navigation, and BASIC entry |
| 2 | Complete | Attribute/FLASH rendering and renderer optimization |
| 3 | Complete | 50Hz pacing, frame execution, and interrupt cadence |
| 4 | Complete | ZEXDOC/ZEXALL CPU baseline and hardware-derived block-I/O flag regressions |
| 5 | Complete | 48K/128K `.sna` plus `.z80` v1/v2/v3 support |
| 6 | In progress | Structure-driven TAP/TZX bootstrap, ROM, protected, mounted, VERIFY, and sequencing paths |
| 7 | In progress | Beeper/AY synthesis and mixing; live-tape transition polish remains |
| 8 | Complete | Headless core/UI split and clock-driven audio buffering |
| 9 | Complete | Initial ULA contention, floating-bus, and timestamped border baseline |
| 10 | Complete | Side-effect-free Z80 decoder foundation |
| 11 | Complete | Explicit resettable 48K/128K machine modes |
| 12 | Complete | Resumable tape transport and TZX 48K stop markers |
| 13 | Complete | Paused read-only mapped-64K disassembler window |
| 14 | Complete | Deterministic temporary in-memory Quick State |
| 15 | Complete | Disassembler history, search, navigation, persistence, copy, and export |
| 16 | In progress | Central ULA profiles, contention conformance, 128K floating bus, exact port events, border-latch mapping, and timed CPU bus cycles implemented; published diagnostic validation remains |

### Milestone 16 - ULA Timing Conformance And Port Accuracy In Progress
- Implemented one model-specific timing profile for frame length, scanline length, contention start, display fetches, visible raster mapping, and the documented timing convention
- Added table-driven contention coverage across complete 48K and original 128K display phases, line/frame boundaries, contended memory banks, and all four I/O contention cases
- Timestamped opcode/immediate fetches, ordinary/indexed/block/stack memory accesses, interrupt-vector reads, and port reads/writes at their machine-cycle positions, including contended I/O and the distinct block-I/O repeat tail
- Generalized the floating-bus model for the original Spectrum 128K and grey +2, including normal bank 5 and shadow bank 7 screen selection
- Decoded attached input devices before the floating-bus fallback, including `IN (0xFFFD)` reading the selected AY register
- Corrected and tested border, beeper, and AY write timestamps plus eight-pixel border-latch and visible pixel/T-state mapping without claiming +2A/+3 behavior
- Continue validation with published contention and floating-bus diagnostics plus the existing tape, snapshot, RZX, ZEXDOC, and ZEXALL regression matrix

### Milestone 17 - Beam-Aware Video Pipeline Planned
- Add an event-driven ULA raster that advances to timed machine events; a literal per-T-state host loop is not required when the observable result is equivalent
- Latch bitmap and attribute bytes when the ULA fetches them instead of rendering the active display from final end-of-frame RAM
- Apply border changes and 128K normal/shadow screen switches at their raster positions through the same timing model
- Preserve FLASH, palette, scaling, and the platform-neutral frame-buffer boundary while replacing only the frame's source data
- Implement and validate the 48K fetch path first, then original 128K/+2 timing and shadow-screen behavior
- Add deterministic mid-frame bitmap, attribute, border, and paging tests, followed by representative multicolour and racing-beam validation; Arkanoid's erase/redraw flicker is a primary acceptance case

### Milestone 18 - Debug Execution Controls Planned
- Keep opening the window paused by default, with an explicit Run/Pause control if live execution is enabled
- Add Step Into first, then Step Over, Step Out, and Run to Cursor using temporary execution stops where appropriate
- Add persistent address breakpoints, enable/disable controls, and a compact breakpoint list
- Show registers, flags, interrupt state, stack context, and the instruction at the current `PC`
- Clearly distinguish frozen snapshots from optional throttled live refresh while the machine is running
- Keep execution control and breakpoint state in platform-neutral core/debugger services, with deterministic instruction-boundary and interrupt tests

### Milestone 19 - Bank-Aware Disassembly And Symbolic Analysis Planned
- Allow inspection of physical ROM and RAM banks independently of the currently mapped 64K address space
- Annotate logical addresses with ROM/RAM bank identity and make paging changes visible in refreshed snapshots
- Add user labels, symbol import/export, and automatic labels for followed branch and call targets
- Add cross-references for jumps, calls, and data references without treating arbitrary data as executable code
- Add code/data marking, hexadecimal memory inspection, and navigation from registers, stack entries, and branch targets
- Export stable bank-qualified listings suitable for diagnostics and comparison between emulator runs

---

## Longer-Term Improvements

- Demo compatibility beyond the current validation matrix
- Broader real-game validation
- Optional game library window with searchable artwork tiles, recent items, and an explicit file picker; prefer a scalable grid over a carousel-only interface
- Additional platform frontends, including a browser/WebAssembly adapter using the established headless engine boundary

---

## Design Principles

- No third-party runtime dependencies; xUnit and its runner are test-only packages
- Incremental development (no large rewrites)
- Behaviour verified with tests, ZEXDOC, and ZEXALL
- Clear separation between emulation and UI, with platform-neutral video and audio contracts
- Headless tooling for reproducible debugging

---

## Contributing

This is primarily a personal project for learning and development.

Contributions are welcome for bug fixes with tests. Feature changes should
align with the roadmap and be discussed before implementation.

See `CONTRIBUTING.md` for details.

---

## License

MIT
