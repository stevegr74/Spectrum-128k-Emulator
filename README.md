# ZX Spectrum 128K Emulator (C#)

A from-scratch ZX Spectrum 128K emulator written in C# with no third-party runtime dependencies.

This project focuses on correctness, clean architecture, and incremental development, with strong validation through automated tests and Z80 compliance tooling.

---

## Features

- Z80 CPU emulation
- Full ZEXDOC and ZEXALL CPU compliance (all instruction groups passing)
- 128K memory paging (port `0x7FFD`)
- ROM loading (128K + 48K modes)
- 48K-format `.sna` snapshot loading (verified)
- `.z80` snapshot support
  - v1 loading implemented
  - v2/v3 page-block support implemented
- Keyboard matrix (8x5, active low)
- Explicit 48K/128K machine selection, with 128K as the startup default
- Screen rendering (`256x192`) with fixed 1x, Scale2x, and Scale3x display modes
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
- Manual tape stop/resume and resumable TZX 48K stop markers
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
- Side-effect-free Z80 instruction decoder
- Read-only `F6` disassembler with history, capture search, export, and paging context
- In-memory `F7`/`F8` Quick State save and restore

---

## Current Status

The established baseline on `master` includes CPU compliance, a headless core,
clock-driven audio, the first ULA contention/border model, explicit 48K/128K
operation, resumable tape transport, a paused read-only disassembler, and a temporary
in-memory Quick State slot. Broader tape compatibility, live-tape audio handoff
polish, disassembler expansion, and video-timing accuracy remain active work.

- Emulator boots into 128K menu
- Menu navigation works
- Can enter 48 BASIC / 128 BASIC
- BASIC programs execute correctly
- Rendering pipeline stable and optimized
- FLASH behaviour implemented correctly
- Frame pacing stable (~50 FPS baseline)
- Fixed display modes preserve exact Spectrum pixel geometry: 1x native, Scale2x-enhanced, and Scale3x-enhanced
- The emulator starts in the 2x enhanced mode; the window cannot be manually resized
- An in-app F1 control reference and F2 status-overlay toggle are available
- `F7` saves and `F8` restores one temporary in-memory Quick State, with restore disabled until a state exists
- Interrupt cadence implemented
- 48K `.sna` snapshots load correctly
- `.z80` snapshots load with v1 and v2/v3 support
- `robocop128k.z80` has been tested successfully and is playable
- `.tap` loading works through the ROM-driven path
- `.tap` loading now works for real game cases including `exolon.tap` and `Where Time Stood Still.tap`
- `.tzx` support is implemented and `Exolon.tzx` is verified working
- `Impossible Mission - Bugfix.tzx` now loads successfully, including its protected loader stage
- `Batman - Release 1.tzx` now loads through to the game path
- `Target Renegade (Imagine, OR) 128k.tzx` completes its protected 128K multi-load, stops/ejects the tape cleanly, and reaches the game menu
- `.rzx` replay support is implemented and `aufmonty.rzx` plays back successfully
- emulation and audio submission now run on a background loop while the UI presents frames at a fixed 50Hz cadence
- loader-only turbo tape phases skip unnecessary per-frame audio-frame construction; live playback returns to real-time audio submission when the machine becomes audible
- protected non-ROM live tape streams use a lower turbo ceiling than ordinary streaming tape
- the Spectrum palette now uses standard `0xD7` normal and `0xFF` bright intensity levels
- AY register model implemented and wired to the register-select and write ports
- 48K beeper implemented via port `0xFE` (speaker state + edge detection)
- AY tone, envelope, and noise output implemented
- Basic audio mixing implemented
- CPU/frame timing and interrupt handling improved through real-game testing
- 48K floating-bus reads are implemented; original 128K/+2 unattached-port reads still return `0xFF` and are explicitly planned for Milestone 16
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

### CPU Compliance Baseline
- ZEXDOC runs to completion in a headless runner
- ZEXALL runs to completion in a headless runner
- All ZEXDOC/ZEXALL instruction groups pass in the current headless harness
- DAA implementation fixed and validated
- Hardware-derived flags for block-I/O instructions (`INI`, `IND`, `INIR`, `INDR`, `OUTI`, `OUTD`, `OTIR`, `OTDR`) are implemented and covered by targeted regression tests

ZEXDOC and ZEXALL are major CPU regression gates, but they are not treated as proof that every undocumented or I/O-data-dependent behaviour is complete.

### Snapshot Support Progress (Milestone 5)
- 48K `.sna` loading implemented and verified (real game runs)
- `.z80` snapshot support implemented (v1 + v2/v3)
- 128K paging and memory restoration working
- `robocop128k.z80` verified working and playable
- Snapshot restore now uses format-based generic paths
  - `.sna` restores interrupt state from header semantics
  - 48K `.z80` uses the dedicated generic 48K `.z80` machine path

### Recording Replay Progress
- `.rzx` parsing and replay are implemented
- embedded SNA/Z80 snapshot restoration is supported for the implemented replay path
- `aufmonty.rzx` has been verified playing successfully

### Tape Loading Progress (Milestone 6)
- `.tap` parsing implemented
- `.tzx` parsing implemented
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
  - `Target Renegade (Imagine, OR) 128k.tzx` (protected 128K continuation and final tape stop)
- tape execution is selected from parsed tape structure rather than title-specific rules:
  - standard BASIC chains use the fast bootstrap path where their ROM side effects can be reproduced
  - mixed and protected tapes retain mounted signal playback for the live/protected stage
  - the ROM `LD-BYTES` trap remains the shared path for standard header/data loads and VERIFY
- protected BASIC bootstrap now honours Spectrum `CLEAR -1` semantics, preserving all RAM before a `USR` handoff; this is required by the Impossible Mission loader
- generic hybrid-tape support includes:
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
- Target Renegade's protected 128K continuation now reaches the game menu and auto-ejects without synthetic end-of-stream pulses
- explicit 48K/128K selection is implemented, and the selected model controls TZX stop-marker behavior
- `F5` stops and resumes the mounted tape without advancing its pulse position or changing its EAR level
- 48K TZX stop markers now preserve the remaining blocks as resumable transport stops instead of truncating the tape
- tape transitions show a full top-left status for three seconds; playing then keeps a compact icon, while paused/stopped icons disappear after five seconds
- the window title persistently distinguishes playing, manual pause, automatic stop, and ended states
- broader `.tzx` compatibility remains active work beyond the verified Batman, Exolon, Impossible Mission, and Target Renegade baseline

### Disassembly Progress
- the side-effect-free instruction decoder foundation is integrated
- `Z80TraceDiagnostics.cs` remains separate diagnostic CPU trace scaffolding
- `F6` opens a read-only disassembly window over an immutable capture of the currently mapped logical 64K address space
- address history, keyboard navigation, address/byte/mnemonic search, capture age, persisted layout, metadata-rich copy, complete 64K export, and branch-target navigation are implemented

### Audio Progress (Milestone 7)
- AY register model implemented
- AY register-select and write-port wiring implemented (`0xFFFD` / `0xBFFD`); selected-register input reads are planned in Milestone 16
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
- Protected live-tape loads now resume audio when playable code begins before the
  tape stream ends. The transition is improved but not yet seamless in every
  title; refine the turbo-to-realtime audio handoff without distorting tape timing.
- Outside the live-tape handoff, remaining polish is mostly app-side input responsiveness rather than core audio generation

## Current Development Plan

Completed milestones remain regression baselines rather than being repeated as
new work. Each planned milestone will be developed on its own feature branch.
The next core and debugger milestones are intentionally ordered dependencies,
while tape/audio compatibility remains a parallel work stream:

1. **Finish validating the current disassembler milestone.**
   - Complete Milestone 15 manual usability testing and merge it as the new baseline before starting another feature branch.

2. **Establish ULA timing accuracy in two stages.**
   - Milestone 16 centralizes model timing, strengthens contention and border conformance, and adds original 128K/+2 floating-bus and input-port accuracy.
   - Milestone 17 introduces an event-driven beam-aware video pipeline so active-screen memory and paging changes are represented at their actual raster times.
   - Preserve the tested tape, snapshot, audio, RZX, and CPU baseline throughout the timing work.

3. **Expand the debugger on the stabilized core.**
   - Milestone 18 adds explicit Run/Pause, stepping, Run to Cursor, breakpoints, and register/stack context through deterministic platform-neutral debugger services.
   - Milestone 19 adds physical bank selection, bank-qualified addresses, labels, cross-references, code/data marking, and stable exports without treating arbitrary data as code.

4. **Refine live-tape audio handoff.**
   - Keep tape timing exact while moving from loader-only turbo operation to audible real-time playback.
   - Remove the remaining non-seamless transitions in protected titles without regressing normal playback.

5. **Use the compatibility matrix as the merge gate.**
   - `exolon.tap` and `Exolon.tzx`
   - `Where Time Stood Still.tap`
   - `Impossible Mission - Bugfix.tzx`
   - `Batman - Release 1.tzx`
   - `Target Renegade (Imagine, OR) 128k.tzx`
   - representative `.sna` / `.z80` snapshots, `aufmonty.rzx`, ZEXDOC, and ZEXALL

---

## Architecture

The solution separates the platform-neutral emulator from the Windows frontend
and diagnostic tools:

```text
Spectrum128kEmulator/
|-- Spectrum128kEmulator.Core/             net8.0 platform-neutral emulator
|   |-- Audio/                             AY/beeper synthesis and sample clock
|   |-- Tape/                              TAP/TZX parsing, transport, and bootstrap policy
|   |-- Z80/                               CPU execution plus side-effect-free instruction decoding
|   |-- Spectrum128Machine.cs              machine, memory, paging, ULA timing, and input matrix
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

The emulator starts in `2x Enhanced` and its window cannot be manually resized.
Press plain `F4` to cycle `1x Native` (`320x240`), `2x Enhanced` (`640x480`),
and `3x Enhanced` (`960x720`). Enhanced modes use deterministic Scale2x/Scale3x
pixel-art scaling; the same modes are available from the display context menu.

Press `F1` for the in-app control reference. `F2` toggles the FPS and display
mode overlay, which is hidden by default. `F3` resets and toggles between
explicit 128K and 48K machine modes, and `F4` cycles the display mode. `F5`
stops or resumes a mounted tape while CPU emulation continues. Manual
transport changes show a full top-left status for three seconds. Playing then
keeps a compact icon; paused and automatically stopped states keep the icon for
five seconds in total before hiding it. The window title continues to show the
current tape state, including an explicit `Tape: Auto-stopped` marker status.
`F6` opens or closes the read-only Z80 disassembler. Opening it pauses emulation
and captures the currently mapped logical 64K address space, current `PC`, model,
ROM, and paged-RAM context. It does not capture every physical 128K RAM/ROM bank
or the complete machine state; closing it resumes emulation when no other pause
owner remains. Inside the window, Alt+Left/Right navigate address history,
Ctrl+G focuses address entry, Ctrl+P returns to the captured PC, Ctrl+F and F3
search addresses, byte sequences, or mnemonics, and Enter follows a selected
direct branch. F5 refreshes the immutable capture, copy includes capture
metadata, and export writes a complete mapped 64K listing. Window size, column
widths, and the latest listing address are remembered per user.
`F7` atomically saves the complete emulated machine to one temporary Quick State
slot, and `F8` restores it. This includes CPU, all RAM banks, paging, ULA/audio
frame state, and tape/RZX positions. Restore is unavailable until a state has
been saved. The slot remains available across resets and media loads in the same
app session, but is deliberately in-memory only and is discarded when the app
closes. Saving or restoring briefly shows an on-screen confirmation.
`F9` opens the 48K-format `.sna` loader; that file format can run on the selected
48K or 128K hardware model, but 128K-format `.sna` files are not supported.
`F10`, `F11`, and `F12` open the Z80/RZX loader, tape loader, and machine-dump
action respectively.
The emulator pauses while Help or a file chooser is open and resumes only when
the final UI pause owner closes.

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
  - 128K-format `.sna` files are not currently supported
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

Milestone numbers record delivery order. Milestones 6 and 7 remain open for
broader compatibility and polish while later, independently scoped foundations
and tools are completed incrementally.

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
- Hardware-derived block-I/O flags, including data-dependent and undocumented bits, are implemented and covered by targeted regressions

### Milestone 5 - Supported Snapshot Formats Complete
- 48K `.sna` loading complete and verified
- 128K-format `.sna` files remain outside the completed milestone scope
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
- Verified protected-loader examples include Exolon, Impossible Mission, Batman, and Target Renegade's protected 128K multi-load
- Target Renegade's 128K path now reaches its game menu after a clean final tape stop/eject
- Milestone 11 supplies explicit 48K/128K selection, and Milestone 12 supplies manual/resumable transport; together they enable the manually validated dual-mode Target Renegade paths
- Broader protected/custom TZX compatibility remains ongoing

### Milestone 7 - Audio (In Progress)
- AY-3-8912 register emulation
- AY register-select and write-port wiring implemented; selected-register input reads remain planned in Milestone 16
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
- Protected live-tape loads resume audio when playable code begins before the tape stream ends; the remaining turbo-to-realtime transition needs further refinement in some titles
- Remaining input responsiveness polish is outside the core audio path

### Milestone 8 - Core/UI Decoupling And Clock-Driven Audio Complete
- A platform-neutral ARGB frame buffer now sits below the Windows `Bitmap` adapter, with direct pixel regression coverage
- A headless `net8.0` core library now contains the machine, CPU, tape, snapshot/RZX, audio synthesis, and frame-buffer model; the WinForms frontend and ZEX runner consume it
- AY and beeper PCM sample counts now use a deterministic master-T-state accumulator; the frontend only queues produced PCM
- Regression tests prove that split execution slices produce the same sample count as a combined interval and preserve fractional 48K samples across frames

### Milestone 9 - ULA Timing And Border Effects Baseline Complete
- Static visible borders and timestamped `OUT (FE)` raster border changes are rendered through the platform-neutral frame buffer
- 48K and 128K ULA contention coverage includes display phase, contended memory, contended I/O, and every paged 128K RAM bank
- This baseline does not include original 128K/+2 floating-bus reads or beam-aware active-screen rendering; those limitations are planned for Milestones 16 and 17 respectively
- The core/UI boundary and clock-driven audio contract are merged on `master`; focused CPU, renderer, audio, machine, snapshot/RZX, and tape regression suites cover the baseline

### Milestone 10 - Disassembler Foundation Complete
- Side-effect-free Z80 instruction decoder added for base, `CB`, `ED`, `DD`, `FD`, `DD CB`, and `FD CB` forms
- Decoder returns instruction bytes, length, mnemonic, and optional branch target without mutating machine state
- Focused opcode and prefix regression tests cover immediates, relative branches, indexed displacements, prefix lengths, unsupported fallback, and address wraparound

### Milestone 11 - Explicit 48K/128K Machine Modes Complete
- 128K remains the startup default and the selected model is authoritative over tape heuristics
- `F3` and right-click Machine Model actions reset cleanly into either model
- Status overlay and in-app help show the selected model behavior
- TZX `stop if 48K` blocks are honored by the selected model and feed the resumable Milestone 12 transport path

### Milestone 12 - Resumable Tape Transport Complete
- `F5` stops/resumes tape transport without ejecting or advancing the waveform
- TZX `stop if 48K` markers remain in the mounted tape as resumable stops with later blocks intact
- A translucent badge shows full transport text for three seconds, then an icon only; the playing icon persists and paused/stopped icons hide after five seconds
- The window title persistently distinguishes playing, manual pause, automatic stop, and ended states
- Core regressions cover exact pulse/EAR preservation, marker resumption, machine transport state, and selected-model parsing
- Target Renegade's 128K all-at-once and 48K level-at-a-time paths and the final transport presentation have been manually validated

### Milestone 13 - Disassembler Window Complete
- `F6` and the right-click quick menu open a read-only disassembly view from the current `PC` while emulation is paused
- Hexadecimal navigation, Go to PC, Refresh, Copy, branch-target navigation, and current-PC highlighting are implemented
- The window captures an immutable copy of the currently mapped logical 64K address space and shows machine-model, ROM, paged-RAM, and screen-bank context
- The snapshot-backed UI and result model remain ready for later breakpoints, stepping, labels, and execution history

### Milestone 14 - In-Memory Quick State Complete
- `F7` atomically captures one temporary Quick State and `F8` restores it; restore remains disabled until a state exists
- The state deep-copies the CPU, all eight RAM banks, paging/model state, ULA border and partial-frame audio state, tape transport/cursor, RZX cursor, and mounted-loader continuation context
- Restore clears host key state, resets presentation scheduling, and recreates the host audio pipeline to prevent stuck input and stale buffered sound
- The right-click menu and in-app help expose both controls in F-key order, with short on-screen saved/restored confirmations
- The slot survives machine resets and media loads within the running app but is intentionally discarded on exit; it is not a replacement for portable `.sna` or `.z80` files
- Deterministic core regressions cover machine replay plus exact tape and RZX cursor restoration, and the save/restore UI flow was manually validated before merge

### Milestone 15 - Disassembler Navigation And Usability In Validation
- Back and Forward address history is available through buttons and Alt+Left/Right, with forward history discarded after a new branch
- Arrow-key row navigation and Enter-to-follow complement retained double-click branch navigation
- Typed search finds hexadecimal addresses, byte sequences, and mnemonic text throughout the immutable mapped 64K capture; F3 repeats a search
- The header continuously distinguishes paused immutable inspection and shows capture time/age, captured `PC`, current listing address, model, and paging context
- Window size, column widths, and the most recent listing address persist under the current user's local application data
- Selected or displayed rows copy with capture metadata, while export produces a complete mapped 64K listing from the chosen start address
- Deterministic tests cover history branching, search parsing/wrapping, complete export, metadata formatting, and settings recovery; manual UI validation is pending

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
