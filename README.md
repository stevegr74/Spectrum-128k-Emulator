# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# with no third-party runtime dependencies.

This project focuses on correctness, clean architecture, and incremental development, with strong validation through automated tests and Z80 compliance tooling.

---

## Features

- Z80 CPU with passing ZEXDOC and ZEXALL instruction groups
- Explicit 48K and 128K models, 128K paging, keyboard matrix, interrupts, and a model-specific timing baseline
- Beam-latched Spectrum display with INK, PAPER, BRIGHT, FLASH, timestamped borders, and fixed 1x/Scale2x/Scale3x modes
- 48K beeper and AY-3-8912 tone, noise, envelope, and mixing through a clock-driven audio pipeline
- 48K/128K `.sna`, `.z80` v1/v2/v3, and `.rzx` loading/replay
- `.tap` and `.tzx` parsing, fast bootstrap, ROM-driven loading, protected/mounted playback, VERIFY, and multi-block sequencing
- Manual/resumable tape transport with 48K stop-marker support and persistent status feedback
- Platform-neutral headless core with Windows presentation/audio adapters
- Paused `F6` debugger with immutable mapped/physical-bank captures, a visual paging map, grouped CPU/stack context, execution controls, breakpoints, bank-qualified labels, control/data cross-references, hexadecimal memory inspection, persistent code/data markings, search, navigation, copy, and export
- In-memory `F7`/`F8` Quick State save and restore
- Headless manual diagnostics and Z80 compliance runners

---

## Screenshots

### Spectrum 128K Startup

![Spectrum 128K emulator showing the 128K ROM menu](docs/images/emulator-128k-basic.png)

### Bank-Aware Debugger

![Paused Z80 debugger showing the paging map, grouped CPU state, memory inspector, and execution controls](docs/images/debugger-bank-aware.png)

---

## Current Status

`master` is a stable, playable baseline with a headless core, clock-driven audio,
explicit machine models, resumable tape transport, a paused execution debugger, and a
temporary Quick State slot. ZEXDOC and ZEXALL pass in the headless compliance
runner, although they are not proof that every undocumented hardware interaction
is complete.

Manually tested titles, formats, and reached checkpoints are tracked in
[Verified Games And Media](VERIFIED_GAMES.md).

Current limitations and active work:

- opcode/immediate fetches, ordinary/indexed/block/stack memory accesses, port I/O, border, and floating-bus timing use model-specific machine-cycle positions validated against published 48K and 128K diagnostics
- beam-aware 48K and original 128K/+2 video is implemented and validated; broader timing-sensitive software coverage remains ongoing
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
- Windows key messages are captured before control/menu preprocessing, then applied by the emulation loop through a scan-aware matrix bridge; shared Spectrum chords retain independent host-key ownership and use emulated T-states rather than UI scheduler ticks.
- The undocumented Z80 Q latch is tracked by flag-writing semantics, including prefix and interrupt boundaries, with focused regressions alongside ZEXDOC/ZEXALL.
- Maskable interrupt acceptance resets both Z80 interrupt flip-flops; `LD A,I`/`LD A,R`, `RETN`/`RETI`, and delayed `EI` behavior have focused regressions across both execution schedulers.
- Model-specific ULA profiles now drive contention, floating-bus fetches, frame timing, and raster mapping; original 128K/+2 floating-bus reads follow the selected bank 5 or bank 7 screen, and `IN (0xFFFD)` reads the selected AY register.
- Border, beeper, and AY writes are timestamped at the completed I/O cycle, including contended and repeating block-I/O paths; border changes are reconstructed in hardware-sized eight-pixel latch groups without stretching visible pixels across horizontal flyback.
- Contended opcode, prefix, and immediate-byte fetches use their individual machine-cycle offsets; corrected indexed-instruction totals cover ignored `DD`/`FD` prefixes, absolute IX/IY loads and stores, stack exchange, and stack-pointer transfer.
- Interrupt scheduling carries instruction-boundary overshoot into the next frame, preserving the 69,888-T-state 48K and 70,908-T-state 128K frame cadence without truncating instructions.
- An event-driven ULA raster latches bitmap and per-scanline attribute bytes at their fetch times, including mid-frame 128K bank 5/bank 7 screen switches; each immutable completed frame owns its matching border timeline across the background-loop/UI boundary.

## Development Priorities

Each feature is developed on its own branch. Milestone 19 is complete, delivering
the bank-aware debugger foundation, symbolic analysis, conservative control/data
cross-references, persistent code/data markings, hexadecimal memory inspection,
and a compact paging/CPU-state workspace. Milestone 20 is hardening the tape
architecture through explicit state ownership, stronger transition tests, and
behavior-preserving decomposition. Milestone 21 will
add a bank-aware Poke Manager with a maintainable cheat library and safe one-off
memory writes. Tape compatibility and the turbo-to-realtime audio transition
continue in parallel, with the verified media matrix plus ZEXDOC/ZEXALL used as
regression gates.

---

## Architecture

The solution separates the platform-neutral emulator from the Windows frontend
and diagnostic tools:

```text
Spectrum128kEmulator/
|-- Spectrum128kEmulator.Core/             net8.0 platform-neutral emulator
|   |-- Audio/                             AY/beeper synthesis and sample clock
|   |-- Debugging/                         instruction-boundary execution controller
|   |-- Tape/                              TAP/TZX parsing, transport, and bootstrap policy
|   |   |-- MountedTape.cs                 pulse/EAR playback and ROM-load traps
|   |   |-- TapLoader.cs                   TAP orchestration and load-strategy planning
|   |   |-- TapLoader.BasicBootstrap.cs    BASIC bootstrap analysis and execution
|   |   |-- TapeLoadContracts.cs           load plans, results, and transport state
|   |   `-- MountedLoadContinuationController.cs  ROM-profile-aware loader resume policy
|   |-- Z80/                               CPU execution plus side-effect-free instruction decoding
|   |-- Ula/                               timing profiles plus event-driven raster capture and immutable frames
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
|-- Disassembly/                           debugger UI, bank captures, symbolic analysis, memory inspection, and export
|-- Host/                                  background loop, pause ownership, and UI snapshots
|-- Forms/                                 main window partials and shortcut-reference dialog
|-- Input/                                 WinForms-to-Spectrum keyboard bridge
|-- Video/                                 display modes, pixel-art scaling, and presentation adapter
|-- docs/images/                           README screenshots of the emulator and debugger
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
|   |-- ula/                               external ULA diagnostic runbook (media not redistributed)
|   `-- z80/                               ZEXDOC/ZEXALL compliance programs
`-- tmp/                                   ignored local probes, captures, and scratch artifacts
```

`Spectrum128kEmulator.Core` has no WinForms, `System.Drawing`, or Windows
audio-device dependency. The `net10.0-windows` frontend consumes Core for
emulation state and produces Windows-specific video and audio output. The
compliance runner, `Spectrum128kEmulator.Tests`, and manual harness depend only
on Core. Windows presentation, debugger UI, and host behavior are covered by
`Spectrum128kEmulator.Windows.Tests`.

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
| `F6` | Open the execution debugger, paused on an immutable machine capture |
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

The debugger captures the mapped logical 64K, both ROM banks, all eight RAM
banks, registers, flags, interrupt state, stack context, model, and paging state.
Its grouped CPU panel highlights values changed since the previous stopped
capture, shows the instruction at `PC`, and retains the main and alternate
registers, individual flags, interrupt state, timing, and four stack words.
Register addresses open in Memory, while the PC and stack values navigate the
disassembly listing. The visual paging map identifies the current ROM, all three
mapped RAM slots, and the active display bank; its compact fallback preserves the
same context at narrow window widths. The memory-view selector keeps mapped
addresses distinct from bank-qualified physical offsets; search, history, copy,
and export follow the selected view.
Bank-qualified labels persist between sessions, can be imported or exported as
plain-text symbol files, and are created automatically when an unlabeled direct
call or branch target is followed.
Direct calls and jumps plus absolute Z80 data reads/writes produce incoming and
outgoing cross-references without interpreting arbitrary data as executable code.
The independent Memory tab displays mapped or physical-bank bytes in hexadecimal
and ASCII, and can navigate from registers, stack values, listing addresses, and
direct control/data targets without changing the disassembly view.
Media- and model-scoped bank-qualified code/data ranges also persist as debugger
metadata without modifying captured machine memory. Confirmed data is displayed
and exported as `DB`, excluded from mnemonic decoding and recursive control-flow
analysis, while confirmed code supplies additional analysis entry points.
It opens paused and supports Run/Pause, Step Into, Step Over, Step Out, run to
cursor, and persistent enabled/disabled mapped-address breakpoints. The listing
remains frozen while execution runs and refreshes only after the machine stops
safely at an instruction boundary. Help and file choosers use nested-safe pause
ownership.

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
- Z80 instruction decoding, bank-aware disassembly navigation/search, paging-map presentation, grouped CPU-state changes and address links, labels, classifications, control/data cross-references, hexadecimal memory inspection, listing export, settings, snapshot behavior, and STA WinForms interaction
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

The [ULA timing validation runbook](test-assets/ula/README.md) records how the
48K/128K Timing Test and floating-bus diagnostics are run without redistributing
their GPL-licensed media.

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
| 1 | Complete | Active-low keyboard matrix, scan-aware Windows input, shared chord ownership, 128K menu navigation, and BASIC entry |
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
| 16 | Complete | Central ULA profiles, contention conformance, 128K floating bus, exact port events, border-latch mapping, timed CPU bus cycles, and published diagnostic validation |
| 17 | Complete | Event-driven beam capture for bitmap, per-line attributes, border timing, and 128K shadow-screen changes, validated against Arkanoid and published diagnostics |
| 18 | Complete | Instruction-boundary Run/Pause, Step Into/Over/Out, run to cursor, CPU context, and persistent breakpoints |
| 19 | Complete | Bank-aware inspection, labels, conservative control/data cross-references, persistent code/data marking, and hexadecimal memory navigation |
| 20 | In progress | Tape state ownership, transition coverage, diagnostics, and behavior-preserving subsystem decomposition |
| 21 | Planned | Bank-aware Poke Manager, maintainable cheat library, media matching, and safe one-off memory writes |

### Milestone 16 - ULA Timing Conformance And Port Accuracy Complete
- Implemented one model-specific timing profile for frame length, scanline length, contention start, display fetches, visible raster mapping, and the documented timing convention
- Added table-driven contention coverage across complete 48K and original 128K display phases, line/frame boundaries, contended memory banks, and all four I/O contention cases
- Timestamped opcode/immediate fetches, ordinary/indexed/block/stack memory accesses, interrupt-vector reads, and port reads/writes at their machine-cycle positions, including contended I/O and the distinct block-I/O repeat tail
- Generalized the floating-bus model for the original Spectrum 128K and grey +2, including normal bank 5 and shadow bank 7 screen selection
- Decoded attached input devices before the floating-bus fallback, including `IN (0xFFFD)` reading the selected AY register
- Corrected and tested border, beeper, and AY write timestamps plus eight-pixel border-latch and visible pixel/T-state mapping without claiming +2A/+3 behavior
- Preserved interrupt phase across instruction-boundary overshoot, producing exact published frame lengths of 69,888 T-states for 48K and 70,908 T-states for 128K
- Matched the Patrik Rak Timing Test 0.3 reference captures pixel-for-pixel for 48K/128K contended NOP, snow-effect NOP, and all six even/odd I/O contention cases
- Matched the Mark Woodmass Float48K and Float128K reference address/value sequences, with the repeatable external-media procedure recorded under `test-assets/ula`

### Milestone 17 - Beam-Aware Video Pipeline Complete
- Implemented an event-driven ULA raster that advances to timed machine events without requiring a literal per-T-state host loop
- Bitmap bytes and attributes are latched at their individual ULA fetches; attributes are fetched independently on every pixel line rather than read from final frame RAM
- Original 128K/+2 bank 5 and shadow bank 7 screen changes take effect at their timed paging event, so one frame can contain output from both banks
- Completed raster frames own their matching timestamped border timeline, remain immutable across the background-loop/UI boundary, and survive Quick State capture and restore
- Preserved FLASH, palette, display scaling, and the platform-neutral frame-buffer boundary
- Added deterministic fetch-boundary, per-line attribute, shadow-screen, border ownership, rendering, and no-screen-write frame tests; published 48K/128K contention captures remain pixel-identical
- Eliminated the previously observed Arkanoid ship flicker while preserving CD Player and Cybernoid snapshot rendering and protected tape execution

### Milestone 18 - Debug Execution Controls Complete
- Opens paused by default, with explicit Run/Pause controls and a visibly frozen capture while execution is active
- Implements Step Into, call-aware Step Over, stack-aware Step Out, and Run to Cursor at instruction boundaries
- Provides persistent enabled/disabled address breakpoints with a compact list and listing highlights
- Shows a compact grouped CPU panel with main and alternate registers, individual flags, interrupt state, stack context, timing, the instruction at `PC`, changed-value highlighting, and direct register/stack navigation
- Keeps execution and breakpoint policy in a platform-neutral debugger service, with deterministic controller, interrupt-boundary, CPU, and scheduler tests
- Refreshes the immutable capture only after a safe stop, avoiding mutable-state inspection while execution is active

### Milestone 19 - Bank-Aware Disassembly And Symbolic Analysis Complete
- Completed immutable inspection of both ROM banks and all eight RAM banks independently of the currently mapped 64K address space
- Completed explicit mapped versus bank-qualified offset context across navigation history, search, copy, and 64K/16K export
- Completed persistent user labels, automatic followed-target labels, symbol-aware navigation, and bank-qualified symbol import/export
- Completed refreshed paging context without conflating physical offsets with logical CPU addresses
- Completed a responsive four-slot paging map for the current ROM, mapped RAM banks, active display bank, listing address, captured PC, and machine model
- Completed stable bank-qualified listings suitable for diagnostics and comparison between emulator runs
- Completed incoming and outgoing direct control-flow cross-references for calls and jumps, with recursive analysis limited to explicit code roots and conservative bank qualification
- Completed and acceptance-tested persistent bank-qualified code/data/unknown range marking; confirmed data renders as `DB` and is excluded from mnemonic search and recursive control-flow decoding
- Completed conservative absolute-address data read/write cross-references, including byte/word widths and bank qualification, without treating indirect accesses or arbitrary data as executable code
- Completed an independent hexadecimal/ASCII memory inspector with mapped and physical-bank views plus navigation from registers, stack entries, listing addresses, and direct control/data targets

### Milestone 20 - Tape Architecture Hardening In Progress
- Documented current tape-state ownership and invariants across transport, load-strategy selection, continuation handling, threading, and Quick State restoration in [Tape State Ownership](docs/tape-state-ownership.md)
- Added focused machine-level lifecycle coverage for no-tape behavior, media replacement, eject, model reset, and stopped/ended Quick State restoration
- Isolated tape read, loader-activity, handoff, and automatic-resume counters plus their Quick State in a dedicated activity tracker without changing transition policy
- Centralized mounted-media identity, transport state, lifecycle transitions, and exact transport Quick State restoration in a dedicated controller
- Moved automatic handoff stop/resume decisions behind the activity tracker and added direct coverage for all safety gates plus exact EAR-phase preservation
- Add table-driven transition coverage for fast loading, real-time fallback, pause/resume, stop markers, tape end, multiloads, media replacement, reset, and Quick State restoration
- Split the large tape regression suite into responsibility-based test classes without reducing existing coverage
- Keep pulse/EAR transport, fast/ROM/protected load strategy, and BASIC/machine-code continuation as separate cooperating responsibilities rather than one combined state machine
- Reduce `TapLoader.BasicBootstrap.cs`, `TapLoader.cs`, and `MountedTape.cs` incrementally, starting with behavior-preserving extractions and leaving `MountedLoadContinuationController` focused on resume state
- Add structured transition diagnostics that distinguish pauses, tape end, ROM handoff, unsupported acceleration, and invalid continuation state
- Use one branch per extraction and retain the full automated suite plus the verified media matrix as regression gates; do not mix compatibility changes into structural branches

### Milestone 21 - Poke Manager And Cheat Library Planned
- Add a discoverable Poke Manager through the normal menus and right-click context menu without consuming another function key
- Store curated game entries, aliases, model requirements, and multi-address pokes in a validated human-maintainable data file rather than hardcoded UI logic
- Suggest compatible entries from loaded-media checksums, with filename aliases used only for discovery rather than compatibility guarantees
- Support one-off mapped-64K and physical-bank writes with explicit address context, value validation, and a preview of every affected byte
- Apply each poke atomically on the emulation thread through the existing pause-lease ownership model, and retain safe session-local original-byte restoration where possible
- Keep the first version to deliberate one-time writes; defer frozen values, conditional trainers, and cheat discovery until their continuous-mutation semantics are designed and tested
- Add schema, duplicate-entry, media-matching, bank-selection, atomic-write, restoration, and frontend workflow tests

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
