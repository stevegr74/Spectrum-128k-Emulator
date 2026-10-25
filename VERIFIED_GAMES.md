# Verified Games And Media

This is the emulator's living manual compatibility record. An entry means the
listed media reached the stated checkpoint on this project; it does not imply
that every level, control path, or hardware edge case has been tested.

## Tape Images

| Title / release | Format | Verified checkpoint and notes |
| --- | --- | --- |
| Arkanoid (Imagine) | `.tzx` | Protected loading completes and gameplay runs; Milestone 17 beam-aware rendering eliminates the previously observed ship flicker, while audio timing remains slightly imperfect |
| Arkanoid (Hit Squad) | `.tzx` | LOAD capability gating falls back to the real-ROM protected path; loading reaches gameplay and beam-aware rendering eliminates the previously observed ship flicker |
| Batman | `.tzx` | Chained BASIC and protected loading complete; game runs |
| Booze Up | `.tzx` | Interactive BASIC loading remains under real-ROM control, reaches the loaded program, and detects the terminal tape state correctly |
| Captain America | `.tzx` | Protected loader completes; game runs |
| Donkey Kong (Erbe) | `.tzx` | Custom loader completes without corrupting the loading screen; game runs |
| Exolon | `.tap`, `.tzx` | Both tape formats load and reach the game |
| Ikari Warriors | `.tzx` | Both the original and Encore releases load and run; transport stops after loading |
| Impossible Mission | `.tzx` | Protected loader completes; game runs |
| Impossible Mission II | `.tzx` | Protected loader completes; game runs |
| Indiana Jones and the Last Crusade | `128k.tap` | Initial load auto-stops at the picture; side-two resume loads into gameplay and preserves the next multiload boundary |
| Rambo (Hit Squad) | `.tzx` | Long protected loader completes; title and game screens run |
| Rambo (Ocean) | `.tzx` | Speedlock BASIC handoff uses real-ROM bootstrap; protected pulse loading completes without initial screen corruption |
| Roller Coaster (Elite) | `.tzx` | End-of-load transition starts the game correctly |
| Scuba Dive (Durell) | `.tzx` | Nonstandard leading tape structure mounts and loads correctly |
| Target Renegade | `128k.tzx` | 128K mode loads the complete game; 48K mode follows the level-at-a-time path with resumable tape transport; both modes remain responsive with good audio after the Milestone 16 timing changes |
| The New Zealand Story (Hit Squad) | `.tzx` | 128K protected loading reaches the menu, automatically resumes after the game enters its stable ROM tape-search loop, loads level 1-1, and auto-stops at the next multiload boundary |
| The Oracle's Cave (Doric Computer) | `.tzx` | ROM-driven multi-stage loading completes; maze generation and level selection lead into the playable cave |
| Where Time Stood Still | `.tap` | Fast load completes and terminal tape state is detected |

## Snapshots

| Title / file | Format | Verified checkpoint and notes |
| --- | --- | --- |
| CD Player | `cdplayer.sna` | Snapshot loads and runs responsively with good audio after the Milestone 16 timing changes |
| Cybernoid: The Fighting Machine | `Cybernoid_The_Fighting_Machine_1988_Hewson_Consultants_a_128K.z80` | 128K snapshot loads and runs responsively with good AY audio after the Milestone 16 timing changes |
| Exolon | `.sna`, `.z80` | Both snapshot forms load and run with 128K audio |
| Jet Set Willy April | `JSWAPRIL.Z80` | Snapshot loads and executes |
| Robocop | `robocop128k.z80` | 128K snapshot loads and executes |
| Target Renegade | Converted 128K `.sna` | Exact representable-state round trip and execution smoke test pass |

## Replay Images

| Title / file | Format | Verified checkpoint and notes |
| --- | --- | --- |
| Auf Wiedersehen Monty | `aufmonty.rzx` | Replay loads and runs through the RZX input path |

## Regression Validation

The complete table currently represents 30 media paths. The Milestone 20
structural acceptance pass loaded every path and ran each for 1,200 headless
frames without an emulator exception or automatic BASIC-error dump. The preceding
manual acceptance pass reached each documented checkpoint, visually inspected the
resulting frame, and continued execution for at least 1,000 additional frames
where the checkpoint allowed it. No delayed BASIC errors, blank screens, or
post-load crashes were observed.

The interaction-dependent checks also confirmed that Target Renegade follows
the distinct 48K multiload and 128K full-load paths, New Zealand Story resumes
automatically into level 1-1, Indiana Jones resumes into level 1 and preserves
the next tape boundary, and Oracle's Cave accepts a quest selection and enters
the generated cave. Synthetic tests cover the underlying loader transitions so
these checks do not depend on distributing the game media with the repository.

## Recording New Results

When adding an entry, include the exact release or filename where it matters,
the media format, and the furthest checkpoint actually observed. Record unusual
requirements such as machine mode, manual tape resume, protected loading, or a
known limitation. Keep unconfirmed or intermittent failures out of this verified
list until they can be reproduced and diagnosed.
