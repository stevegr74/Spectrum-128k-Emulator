# Tape State Ownership

This document records the tape-state boundaries established by Milestone 20.
It describes the hardened behavior. Future structural changes must preserve these
invariants unless a compatibility fix explicitly changes one.

## Owners

| Component | Owns | Does not own |
| --- | --- | --- |
| `Spectrum128Machine` | Machine-level coordination and aggregate Quick State capture/restore | Tape parsing, pulse decoding, mounted-media identity, or loader-strategy selection |
| `TapeTransportController` | Mounted-media identity, public transport state, mount/eject/manual-control transitions, completion, and transport Quick State | Pulse generation, automatic handoff policy, or loader continuation |
| `TapeActivityTracker` | EAR/keyboard read counters, loader-activity and handoff observations, automatic stop/resume safety policy, and their Quick State | Pulse generation, public transport state, or continuation policy |
| `MountedTape` | Block position, exact pulse/EAR cursor, pause and stop-marker state, ROM-trap stream position, header/data sequencing, and transport Quick State | Machine-wide reset policy or BASIC/machine-code continuation |
| `MountedLoadContinuationController` | Pending BASIC/USR continuation, workspace restoration, resume gates, and continuation Quick State | Physical tape position or the visible transport state |
| `TapeLoadPlanner` | Capability-gated selection of full fake load, safe-prefix loading, ROM bootstrap, hybrid bootstrap, or real-time mounting, with a stable reason code and readable explanation | Parsing, plan execution, transport state, or continuation state |
| `TapLoader` and `TzxLoader` | File parsing, plan execution, bootstrap execution, and construction of the remaining mounted tape | Long-lived transport ownership after loading hands control to the machine |
| Windows frontend | User commands and presentation of copied state | Mutation of tape internals or loader continuation state |

The background emulation loop owns mutable machine and tape state. UI code may
request operations through the runner and display copied state, but must not
retain mutable tape objects for direct cross-thread control.

## Invariants

1. `NoTape` means no tape is mounted.
2. `Playing` means a tape is mounted and its playback cursor is not paused.
3. `Stopped` retains the mounted tape and its exact cursor. The stop may be
   manual, automatic, or caused by a tape stop marker.
4. `Ended` means the machine observed completion and released the mounted tape.
5. A stop marker pauses before the following block and remains distinguishable
   from an automatic loader handoff.
6. A manual stop resets automatic-handoff tracking and must not be automatically
   resumed.
7. Automatic resume is restricted to an automatic handoff followed by a
   recognised ROM tape-read state; it must not override a stop marker.
   Keyboard-wait evidence may span frames with no port reads, but any
   EAR-sensitive read cancels the pending automatic stop.
8. Mounting media replaces the previous tape, configures it for the current
   model clock, resets its cursor, and resets machine-level activity tracking.
9. Physical tape ownership and loader continuation are separate. Loader policy
   decides when a continuation is created or cleared; a full machine reset
   clears both.
10. Quick State captures the mounted media, exact tape cursor, public transport
    state, automatic-handoff counters, and loader continuation as one aggregate.
    Restore must reattach media before restoring tape and continuation state.
11. Load strategy and continuation decisions are independent of media names and
    game hashes. A preloaded custom-loader handoff is accepted only when BASIC
    execution changed one unique loader wait loop in live RAM; unchanged or
    ambiguous byte patterns cannot redirect execution.

## Public Transitions

| Event | Result |
| --- | --- |
| New machine or reset | Tape released, continuation cleared, `NoTape` |
| Mount tape | Existing media replaced, incoming tape reset, `Playing` unless the tape itself starts stopped |
| Manual stop | Cursor preserved, automatic tracking reset, `Stopped` |
| Manual resume | Cursor preserved and resumed, `Playing` unless already complete |
| Stop marker | Cursor held before the next block, `Stopped` |
| Recognised loader handoff | Cursor preserved, automatic stop may enter `Stopped` |
| Recognised ROM tape read after automatic handoff | Playback may resume automatically |
| Playback completion | Tape released, `Ended` |
| Eject | Tape released, `NoTape` |
| Quick State restore | All captured transport and continuation state restored consistently |

## Completed Hardening

1. Locked lifecycle and restoration invariants with focused and table-driven tests.
2. Isolated activity tracking and automatic handoff decisions in `TapeActivityTracker`, and media identity and lifecycle transitions in `TapeTransportController`.
3. Isolated strategy selection in `TapeLoadPlanner`, leaving plan execution and continuation as separate responsibilities.
4. Split parsing/timing, ROM loading, BASIC expression handling, and mounted continuation into focused partial-class files without changing their contracts.
5. Added structured transport transitions plus stable load-plan reason codes, while preserving readable diagnostic explanations.
6. Split the tape regression suite by responsibility and retained the complete automated suite, ZEXDOC/ZEXALL, and representative difficult media as acceptance gates.

## Regression Guards

The automated suite uses synthetic tape blocks and BASIC programs rather than
copyrighted game media. It locks the transport and continuation behavior behind
focused checks for:

- automatic handoff stops only after three qualifying keyboard-wait frames;
- idle frames may separate positive evidence but cannot create it, while an
  EAR-sensitive read cancels accumulated evidence;
- keyboard loops that discard EAR with either `AND` or `OR` are recognized;
- automatic ROM resume requires a genuine automatic handoff and preserves the
  exact pulse/EAR cursor;
- manual stops, stop markers, completion, replacement, reset, and Quick State
  restoration remain distinct transitions;
- accelerated BASIC tokenization and execution preserve `INT`, division, and
  byte-extraction expressions used by protected loaders;
- mixed raw-stream structure permits hybrid loading only when the leading BASIC
  program independently passes the bootstrap executor's capability check;
- 128K hybrid bootstrap keeps the ROM `BANK_M` mirror aligned with the active
  ROM-1 paging latch before paging-aware loaded code begins execution;
- unsupported mixed BASIC that depends on native 128K workspace initialization
  starts through ROM 0 and the 128 Tape Loader instead of receiving fabricated
  ROM-1-only state;
- application RAM is not classified as a ROM return path, and unchanged loader
  wait-loop lookalikes cannot redirect bootstrap execution;
- unsupported ROM acceleration, unsafe bootstrap control flow, and
  unrepresentable mounted continuation expose stable reason codes without
  requiring callers to parse diagnostic prose.

The separate verified-media matrix remains the end-to-end acceptance gate for
real loader behavior, including the distinct Target Renegade machine modes and
the New Zealand Story and Indiana Jones multiload resumes.
