# Tape State Ownership

This document records the tape-state boundaries at the start of Milestone 20.
It describes existing behavior. Structural changes must preserve these
invariants unless a compatibility fix explicitly changes one.

## Owners

| Component | Owns | Does not own |
| --- | --- | --- |
| `Spectrum128Machine` | Machine-level coordination and aggregate Quick State capture/restore | Tape parsing, pulse decoding, mounted-media identity, or loader-strategy selection |
| `TapeTransportController` | Mounted-media identity, public transport state, mount/eject/manual-control transitions, completion, and transport Quick State | Pulse generation, automatic handoff policy, or loader continuation |
| `TapeActivityTracker` | EAR/keyboard read counters, loader-activity and handoff observations, automatic stop/resume safety policy, and their Quick State | Pulse generation, public transport state, or continuation policy |
| `MountedTape` | Block position, exact pulse/EAR cursor, pause and stop-marker state, ROM-trap stream position, header/data sequencing, and transport Quick State | Machine-wide reset policy or BASIC/machine-code continuation |
| `MountedLoadContinuationController` | Pending BASIC/USR continuation, workspace restoration, resume gates, and continuation Quick State | Physical tape position or the visible transport state |
| `TapeLoadPlanner` | Capability-gated selection of full fake load, safe-prefix loading, ROM bootstrap, hybrid bootstrap, or real-time mounting, with a diagnostic reason | Parsing, plan execution, transport state, or continuation state |
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
8. Mounting media replaces the previous tape, configures it for the current
   model clock, resets its cursor, and resets machine-level activity tracking.
9. Physical tape ownership and loader continuation are separate. Loader policy
   decides when a continuation is created or cleared; a full machine reset
   clears both.
10. Quick State captures the mounted media, exact tape cursor, public transport
    state, automatic-handoff counters, and loader continuation as one aggregate.
    Restore must reattach media before restoring tape and continuation state.

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

## Hardening Order

1. Lock the lifecycle and restoration invariants with focused tests.
2. Extract machine-level activity tracking and transport transition policy without changing behavior. Activity tracking and automatic handoff decisions are now isolated in `TapeActivityTracker`; media identity and lifecycle transitions are isolated in `TapeTransportController`.
3. Separate loader-strategy and continuation orchestration behind explicit
   boundaries. Strategy selection is now isolated in `TapeLoadPlanner`; execution
   and continuation remain separate existing responsibilities.
4. Improve transition diagnostics and split the large regression suite by
   responsibility.
5. Re-run the complete automated suite and representative difficult media after
   every structural step.
