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

Verified media includes:

- Snapshots: `robocop128k.z80`, `JSWAPRIL.Z80`, Exolon in `.sna`/`.z80` form, and a converted 128K Renegade `.sna`
- Tapes: Exolon, Where Time Stood Still, Impossible Mission I/II, Batman, Target Renegade (128K all-at-once and 48K level-at-a-time), Scuba Dive, Roller Coaster, Captain America, Donkey Kong, and Indiana Jones and the Last Crusade
- Replay: `aufmonty.rzx`

Current limitations and active work:

- original 128K/+2 floating-bus reads and AY selected-register reads are planned for Milestone 16
- active-screen rendering is frame-snapshot based rather than beam-aware; Milestone 17 addresses this
- protected live-tape audio handoff still needs polish in some titles
- broader protected/custom TZX compatibility remains ongoing

### Implementation Notes

- Fast tape bootstrap is capability-gated. Unsupported initial or chained BASIC loader control flow falls back to a clean real-ROM autorun path with the remaining tape mounted.
- Address-based ROM tape services are enabled only for the fingerprinted bundled ROM pair; an unknown ROM pair uses real-time mounted pulse/EAR playback instead of assuming compatible internal addresses.
- ROM-driven tape handoffs preserve consumed-block pauses, exact TZX pause signal transitions, and zero-duration stop markers.
- Mounted playback detects stable keyboard/HALT handoffs, pauses at multiload boundaries, and preserves custom ROM flag and LOAD/VERIFY state across accelerated blocks.
- Protected playback and Quick State preserve precise tape pulse, EAR, loader-continuation and replay positions.
- Mounted-loader continuation state, BASIC workspace preservation, resume gating, and Quick State capture are isolated from the machine in a dedicated controller.
- The background emulation loop owns mutable machine state; the UI consumes copied snapshots and uses nested pause leases for safe inspection.
- The undocumented Z80 Q latch is tracked by flag-writing semantics, including prefix and interrupt boundaries, with focused regressions alongside ZEXDOC/ZEXALL.

## Development Priorities

Each feature is developed on its own branch. The next ordered work is ULA/port
conformance (Milestone 16), beam-aware video (17), debugger execution controls
(18), and bank-aware symbolic disassembly (19). Tape compatibility and the
turbo-to-realtime audio transition continue in parallel, with the verified media
above plus ZEXDOC/ZEXALL used as regression gates.

---

## Architecture

The solution separates the platform-neutral emulator from the Windows frontend
and diagnostic tools:

```text
Spectrum128kEmulator/
|-- Spectrum128kEmulator.Core/             net8.0 platform-neutral emulator
|   |-- Audio/                             AY/beeper synthesis and sample clock
|   |-- Tape/                              TAP/TZX parsing, transport, and bootstrap policy
|   |   `-- MountedLoadContinuationController.cs  ROM-profile-aware loader resume policy
|   |-- Z80/                               CPU execution plus side-effect-free instruction decoding
|   |-- Spectrum128Machine.cs              machine orchestration, memory, paging, ULA timing, and input
|   |-- SpectrumRomProfile.cs              fingerprinted ROM capabilities and internal entry points
|   |-- SpectrumFrameBuffer.cs             platform-neutral ARGB frame buffer
|   |-- BorderFrame.cs                     timestamped border events
|   |-- SnapshotLoader.cs                  SNA snapshot loading
|   |-- Z80SnapshotLoader.cs               Z80 snapshot loading
|   |-- RzxLoader.cs                       RZX replay loading
|   `-- RzxPlaybackSession.cs              RZX playback orchestration
|
|-- Audio/                                 Windows frontend audio pipeline and output adapters
|-- DisassemblerForm.cs                    read-only Z80 disassembly window
|-- DisassemblySnapshot.cs                 immutable mapped-memory capture and address parser
|-- DisassemblyNavigationHistory.cs        deterministic back/forward address history
|-- DisassemblySearch.cs                   address, byte-sequence, and mnemonic capture search
|-- DisassemblyListingFormatter.cs         metadata-rich copy and complete-listing export
|-- DisassemblerWindowSettings.cs          resilient per-user disassembler layout persistence
|-- EmulatorHelpForm.cs                    WinForms shortcut-reference dialog
|-- EmulationPauseLeaseManager.cs          nested-safe UI pause ownership
|-- MainForm.cs                            WinForms menus, host input, and presentation scheduling
|-- SpectrumDisplayMode.cs                 fixed display-mode dimensions and cycling policy
|-- SpectrumDisplayScaler.cs               deterministic Scale2x/Scale3x pixel-art scaler
|-- SpectrumKeyInputBridge.cs              WinForms-to-Spectrum keyboard bridge
|-- SpectrumRenderer.cs                    System.Drawing presentation adapter
|-- Program.cs                             Windows application entry point
|-- Spectrum128kEmulator.csproj            net10.0-windows frontend project
|
|-- Spectrum128kEmulator.Tests/            xUnit unit and regression tests
|-- Spectrum128kEmulator.ManualHarness/    repeatable diagnostic and capture tool
|-- Spectrum128kEmulator.Z80Compliance/    net8.0 ZEXDOC/ZEXALL compliance runner
|-- ROMs/                                  required 128K ROM images
|-- test-assets/                           checked-in test programs and fixtures
`-- tmp/                                   ignored local probes, captures, and scratch artifacts
```

`Spectrum128kEmulator.Core` has no WinForms, `System.Drawing`, or Windows
audio-device dependency. The `net10.0-windows` frontend consumes Core for
emulation state and produces Windows-specific video and audio output. The
compliance runner depends only on Core, while the test and manual-harness
projects may reference the frontend when they need to validate its adapters.

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
shows the current media name and active tape state, while its ended status clears
with the ended badge. The temporary Quick State slot restores the associated
media name as well as CPU, RAM banks, paging, ULA/audio frame state, tape, and
RZX position; it survives media loads during the session and is discarded on exit.

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
- AY register behaviour
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

Its generic diagnostics accept `frames=`, `quiet=1`, `dumpblocks=1`, `strategy=`,
scheduled key/register/memory events, memory-write watches, instruction traces, and
execution breakpoints. It writes a machine dump and frame image only for the run being
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

### Milestone 16 - ULA Timing Conformance And Port Accuracy Planned
- Introduce one model-specific timing profile for frame length, scanline length, contention start, display fetches, visible raster mapping, and the documented timing convention
- Add table-driven contention coverage across complete 48K and original 128K display phases, line/frame boundaries, contended memory banks, and all four I/O contention cases
- Timestamp CPU memory and I/O bus accesses at their actual machine-cycle positions where required, rather than relying on instruction-total timing
- Generalize the existing floating-bus model for the original Spectrum 128K and grey +2, including normal bank 5 and shadow bank 7 screen selection
- Decode attached input devices before the floating-bus fallback, including `IN (0xFFFD)` reading the selected AY register
- Correct and test border-latch timestamps and visible pixel/T-state mapping without claiming +2A/+3 behavior
- Validate with published contention and floating-bus diagnostics plus the existing tape, snapshot, RZX, ZEXDOC, and ZEXALL regression matrix

### Milestone 17 - Beam-Aware Video Pipeline Planned
- Add an event-driven ULA raster that advances to timed machine events; a literal per-T-state host loop is not required when the observable result is equivalent
- Latch bitmap and attribute bytes when the ULA fetches them instead of rendering the active display from final end-of-frame RAM
- Apply border changes and 128K normal/shadow screen switches at their raster positions through the same timing model
- Preserve FLASH, palette, scaling, and the platform-neutral frame-buffer boundary while replacing only the frame's source data
- Implement and validate the 48K fetch path first, then original 128K/+2 timing and shadow-screen behavior
- Add deterministic mid-frame bitmap, attribute, border, and paging tests, followed by representative multicolour and racing-beam demo validation

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
- Remaining menu/input responsiveness polish for games like Jet Set Willy
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
