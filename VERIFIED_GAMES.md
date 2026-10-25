# Verified Games And Media

This is the emulator's living manual compatibility record. An entry means the
listed media reached the stated checkpoint on this project; it does not imply
that every level, control path, or hardware edge case has been tested.

**Status:** ⚪ Grey / Untested · 🔴 Red / Not working · 🟠 Orange / Partial · 🟢 Green / Verified

## Tape Images

| Status | Title / release | Format | Verified checkpoint and notes |
| :---: | --- | --- | --- |
| 🟢 | APB (Hit Squad) | `.tzx` | ROM-driven protected loading reaches the ownership prompt and advances to the complete APB title screen |
| 🟢 | Arkanoid (Imagine) | `.tzx` | Protected loading completes and gameplay runs; Milestone 17 beam-aware rendering eliminates the previously observed ship flicker; sound behavior and timing compare closely with JSspeccy, although JSspeccy has a slightly warmer timbre |
| 🟢 | Arkanoid (Hit Squad) | `.tzx` | LOAD capability gating falls back to the real-ROM protected path; loading reaches gameplay and beam-aware rendering eliminates the previously observed ship flicker |
| 🟢 | Batman | `.tzx` | Chained BASIC and protected loading complete; game runs |
| 🟢 | Bear Grud | `.tap` | Loading reaches the control-selection menu and remains stable through an extended execution soak |
| 🟢 | Booze Up | `.tzx` | Interactive BASIC loading remains under real-ROM control, reaches the loaded program, and detects the terminal tape state correctly |
| 🟢 | Cabal | `48k.tzx`, `128k.tzx` | Both editions reach the player-selection menu through real-ROM bootstrap; 48K stops at its first multiload marker, while 128K completes and ends the tape cleanly |
| 🟢 | Captain America | `.tzx` | Protected loader completes; game runs |
| 🟢 | Cauldron (original release) | `.tzx` | ROM-driven loading reaches the tape prompt and advances to the complete title screen |
| 🟢 | Cauldron II (Erbe and original releases) | `.tzx` | Both releases reach the control-selection menu and remain stable |
| 🟢 | Cliff Hanger | `.tzx` | Loading reaches the control-selection menu and remains stable |
| 🟢 | Cobra (Hit Squad and Ocean original releases) | `.tzx` | Both releases complete loading and reach the stable opening credit sequence |
| 🟢 | Donkey Kong (Erbe) | `.tzx` | Custom loader completes without corrupting the loading screen; game runs |
| 🟢 | Exolon | `.tap`, `.tzx` | Both tape formats load and reach the game |
| 🟢 | Fairlight: A Prelude | `.tap` | Loading reaches the illustrated title screen and remains stable |
| 🟢 | Feud (Bulldog original release) | `.tzx` | Loading reaches the control-selection menu and remains stable |
| 🟢 | Head Over Heels | `.tap` | Loading reaches the complete illustrated title screen and remains stable |
| 🟢 | Ikari Warriors | `.tzx` | Both the original and Encore releases load and run; transport stops after loading |
| 🟢 | Impossible Mission | `.tap`, `.tzx` | The TAP and primary/Bugfix TZX releases complete protected loading and reach the title or control menu; the Ricochet release is excluded below |
| 🟢 | Impossible Mission II | `.tzx` | US Gold original and both Kixx sides complete protected loading and reach stable game/control screens |
| 🟢 | Impossamole (Gremlin original, side 1) | `.tzx` | Side 1 reaches the control menu; continuation with side 2 remains unverified below |
| 🟢 | Indiana Jones and the Last Crusade | `128k.tap` | Initial load auto-stops at the picture; side-two resume loads into gameplay and preserves the next multiload boundary |
| 🟢 | Indiana Jones and the Temple of Doom (Kixx) | `.tzx` | Loading reaches the stop-tape prompt and advances to the complete quest-start screen |
| 🟢 | Kick Off 2 | `.tzx` | Loading reaches its complete title/menu prompt and remains stable |
| 🟢 | Kung-Fu Master (Americana) | `.tzx` | Loading reaches the illustrated title screen and remains stable |
| 🟢 | Magician | `.tap` | Loading reaches the illustrated adventure opening and remains stable |
| 🟢 | Paperboy (Elite original) | `.tzx` | Loading reaches the high-score/start screen and remains stable |
| 🟢 | Paperboy 2 (Mindscape) | `.tzx` | Loading reaches the character-selection menu and remains stable |
| 🟢 | Rambo (Hit Squad) | `.tzx` | Long protected loader completes; title and game screens run |
| 🟢 | Rambo (Ocean) | `.tzx` | Speedlock BASIC handoff uses real-ROM bootstrap; protected pulse loading completes without initial screen corruption |
| 🟢 | Rick Dangerous (Kixx) | `.tzx` | Protected loading reaches the control-selection menu and remains stable |
| 🟢 | Robocop 2 (Ocean original) | `.tzx` | Protected loading reaches the title screen and remains stable |
| 🟢 | Robocop 3 (Ocean original, SpeedLock 7) | `.tzx` | Protected loading reaches active gameplay and remains stable |
| 🟢 | Roller Coaster (Elite) | `.tzx` | End-of-load transition starts the game correctly |
| 🟢 | Rotozlow | `.tap` | Loading reaches the demo-generation sequence and continues executing without an error |
| 🟢 | Scuba Dive (Durell) | `.tzx` | Nonstandard leading tape structure mounts and loads correctly |
| 🟢 | Space Harrier (Elite original and Encore) | `.tzx` | Both releases reach the start/control menu and remain stable |
| 🟢 | Star Wars (Hit Squad) | `.tzx` | Loading reaches the complete X-Wing control menu and remains stable |
| 🟢 | Star Wars (Domark original, side 1) | `.tzx` | The explicit stop/key/resume sequence reaches the complete title screen; side 2 remains unverified below |
| 🟢 | Starglider (48K original) | `.tzx` | Loading reaches the in-game control menu and automatically stops at the handoff boundary |
| 🟢 | Starglider 2 (48K original) | `.tzx` | Loading reaches the complete title screen and ends cleanly |
| 🟢 | Starquake (Bubblebus original and Ricochet) | `.tzx` | Both releases reach the control menu and remain stable |
| 🟢 | Target Renegade | `128k.tzx` | 128K mode loads the complete game; 48K mode loads the first level and automatically pauses at the level-at-a-time boundary with resumable transport; both modes remain responsive with good audio |
| 🟢 | The Goonies | `.tzx` | Loading reaches active gameplay and remains stable |
| 🟢 | The Great Escape (Ocean original) | `.tap`, `.tzx` | Both tape formats reach the control-selection menu and remain stable |
| 🟢 | The Growing Pains of Adrian Mole (Ricochet) | `.tzx` | Loading reaches the illustrated opening story screen and remains stable |
| 🟢 | The New Zealand Story (Hit Squad) | `.tzx` | 128K protected loading reaches the menu, automatically resumes after the game enters its stable ROM tape-search loop, loads level 1-1, and auto-stops at the next multiload boundary |
| 🟢 | The Oracle's Cave (Doric Computer) | `.tzx` | ROM-driven multi-stage loading completes; maze generation and level selection lead into the playable cave |
| 🟢 | Turbo Esprit (Durell original, side 2) | `.tzx` | ROM-driven loading and the explicit copyright prompts advance to the city-selection menu |
| 🟢 | WEC Le Mans (Imagine original, side 1) | `.tzx` | Loading reaches the control-selection menu; side 2 remains unverified below |
| 🟢 | Where Time Stood Still | `.tap` | Fast load completes and terminal tape state is detected |
| 🟢 | Willy's New Mansion: Special Edition | `.tap`, `.tzx` | Both formats reach the difficulty-selection menu and remain stable |

## Snapshots

| Status | Title / file | Format | Verified checkpoint and notes |
| :---: | --- | --- | --- |
| 🟢 | Auf Wiedersehen Monty | `.sna`, `.z80` | Both snapshots reach the control menu and remain stable for 2,000 frames |
| 🟢 | Candy | `CANDY.Z80` | Snapshot reaches the illustrated title screen and remains stable |
| 🟢 | Captain America Defies the Doom Tube | `.z80` | Snapshot reaches the complete title screen and remains stable |
| 🟢 | Cauldron / Cauldron II | `.sna`, `.z80` | Both snapshots reach their control menus and remain stable |
| 🟢 | CD Player | `cdplayer.sna` | Snapshot loads and runs responsively with good audio after the Milestone 16 timing changes |
| 🟢 | Chase HQ | `chasehq.z80` | Snapshot reaches the options prompt and remains stable |
| 🟢 | Cliff Hanger | `.sna` | Snapshot reaches the control-selection menu and remains stable |
| 🟢 | Contact Sam Cruise | `.sna` | Snapshot reaches active gameplay and remains stable |
| 🟢 | Cybernoid: The Fighting Machine | `.sna`, `Cybernoid_The_Fighting_Machine_1988_Hewson_Consultants_a_128K.z80` | Both snapshots reach the title/control screen; the 128K Z80 remains responsive with good AY audio after the Milestone 16 timing changes |
| 🟢 | Donkey Kong | `DONKKONG.Z80` | Snapshot reaches the control menu and remains stable |
| 🟢 | Elite | `Elite.sna`, `Elite128.z80` | Both snapshots reach the commander prompt and remain stable |
| 🟢 | Exolon | `.sna`, `.z80` | Both snapshot forms load and run with 128K audio |
| 🟢 | Feud | `.z80` | Snapshot reaches the control menu and remains stable |
| 🟢 | Flying Shark | `.sna` | Snapshot reaches the player-selection screen and remains stable |
| 🟢 | Frankie Goes to Hollywood | `.sna` | Snapshot reaches the control menu and remains stable |
| 🟢 | Glider Rider | `.sna` | Snapshot reaches the control-selection screen and remains stable |
| 🟢 | Gods | `.sna`, `.z80` | Both snapshots reach the complete title screen and remain stable |
| 🟢 | Green Beret | `.sna`, `.z80` | Both snapshots reach the control-selection screen and remain stable |
| 🟢 | Ikari Warriors | `.sna`, `.z80` | Both snapshots reach the control/title screen and remain stable |
| 🟢 | Jet Set Willy | `JETSETW1.Z80`, `jsw013 (2016 edition, bugfixed).sna`, `jswx.sna` | All three variants reach active title/game screens and remain stable |
| 🟢 | Jet Set Willy April | `JSWAPRIL.Z80` | Snapshot loads and executes |
| 🟢 | WEC Le Mans | `lemans.sna` | Snapshot reaches the control-selection menu and remains stable |
| 🟢 | Mayhem | `MAYHEM.SNA` | Snapshot reaches the title screen and remains stable |
| 🟢 | Oracle's Cave | `oracle.sna` | Snapshot reaches quest selection and remains stable |
| 🟢 | Pac-Land | `Pacland.sna` | Snapshot reaches the high-score screen and remains stable |
| 🟢 | Paperboy | `Paperboy.sna` | Snapshot reaches the high-score/start screen and remains stable |
| 🟢 | Rainbow Islands | `.z80` | Snapshot reaches the complete title screen and remains stable |
| 🟢 | Rambo | `rambo.sna` | Snapshot reaches the high-score/title screen and remains stable |
| 🟢 | Red Hawk | `redhawk.sna` | Snapshot reaches active gameplay and remains stable |
| 🟢 | Target Renegade (Hit Squad filenames) | `.sna`, `.z80` | Both 128K snapshots reach the title/high-score screen and remain stable; the converted SNA also passes exact representable-state round-trip validation |
| 🟢 | Robocop | `Robocop_1988_Ocean_a_128K.z80` | 128K snapshot loads and executes |
| 🟢 | Robocop 3 | `.z80` | Snapshot reaches active gameplay and remains stable |
| 🟢 | Rockford | `.sna` | Snapshot reaches the control menu and remains stable |
| 🟢 | Roller Coaster | `.sna` | Snapshot reaches the title/start screen and remains stable |
| 🟢 | Sabrina | `.sna` | Snapshot reaches the illustrated title screen and remains stable |
| 🟢 | Scorias | `.z80` | Snapshot reaches the preview/title screen and remains stable |
| 🟢 | Sex Simulator 1 | `.sna` | Snapshot reaches its menu and remains stable |
| 🟢 | Space Harrier | `.sna`, `.z80` | Both snapshots reach the title screen and remain stable |
| 🟢 | Starglider | `.sna` | Snapshot reaches the control menu and remains stable |
| 🟢 | Star Wars | `.sna` | Snapshot reaches the complete title screen and remains stable |
| 🟢 | The Great Escape | `.sna`, `.z80` | Both snapshots reach the control-selection screen and remain stable |
| 🟢 | The Thompson Twins | `ttwins.sna` | Snapshot reaches active gameplay and remains stable |
| 🟢 | The Way of the Exploding Fist | `fist.sna` | Snapshot reaches active gameplay and remains stable |
| 🟢 | Uridium / Uridium Plus | `Uridium.sna`, `uridplus.z80` | Both snapshots reach stable title/game screens |
| 🟢 | Wanted! Monty Mole | `.sna` | Snapshot reaches the complete title screen and remains stable |
| 🟢 | Who Dares Wins | `who_dare.sna` | Snapshot reaches active gameplay and remains stable |
| 🟢 | Xenon | `.z80` | Snapshot reaches the control-selection screen and remains stable |
| 🟢 | Xevious | `.sna` | Snapshot reaches the high-score/title screen and remains stable |

## Replay Images

| Status | Title / file | Format | Verified checkpoint and notes |
| :---: | --- | --- | --- |
| 🟢 | Auf Wiedersehen Monty | `aufmonty.rzx` | Replay loads and runs through the RZX input path |

## Tested, Not Yet Verified

These files were exercised during the full-folder audit but did not reach a
checkpoint strong enough for the verified tables. They are recorded here so the
same failures and multipart limitations are not rediscovered. No emulator fixes
were made as part of this audit.

| Status | File / release | Result |
| :---: | --- | --- |
| 🔴 | `starquake.sna` | Rejected because its 49,230-byte size is not a supported 48K or 128K SNA layout; both Starquake TZX releases work |
| 🔴 | Cauldron and Cauldron II (Silverbird, Cyberlode 1.1) `.tzx` | Both abort during bootstrap expression parsing with `Missing closing parenthesis` |
| 🟠 | Chase HQ Portuguese sides 1 and 2 `.tzx` | Side 1 reaches a stop marker with a black frame; side 2 alone also stops without a meaningful screen |
| 🔴 | Frankie (original Speedlock 1) `.tzx` | Tape ends on a solid cyan screen; the Frankie SNA works |
| 🔴 | Glider Rider (Bug-Byte and Quicksilva original) `.tzx` | Both tapes end on a black screen; the Glider Rider SNA works |
| 🔴 | Impossible Mission (Ricochet) `.tzx` | Tape ends at the BASIC error `N Statement lost, 0:2` |
| 🟠 | Impossamole side 2 `.tzx` | Standalone side reaches a stop marker with a black frame; side 1 reaches its menu, but the cross-side continuation was not proven |
| 🟠 | Indiana Jones and the Last Crusade Kixx/US Gold TZX side sets | Side 1 reaches its loading/title handoff, but mounting and completing side 2 was not proven; the 128K TAP path is verified |
| 🔴 | Indiana Jones and the Temple of Doom (US Gold original) `.tzx` | Reaches the stop/key prompt, but the prompted key leaves an incomplete/blank game frame; the Kixx release works |
| 🔴 | Kung-Fu Master `.tzx` | Unlabelled release ends on a blank grey frame; the Americana release works |
| 🔴 | Kwah! (Melbourne House original) `.tzx` | Tape ends on a black frame |
| 🔴 | Pac-Land `.tzx` | Tape ends at the Sinclair copyright screen; the Pac-Land SNA works |
| 🔴 | Paperboy (Encore) `.tzx` | Tape ends on a black frame; the Elite original TZX and Paperboy SNA work |
| 🔴 | Red Hawk parts 1 and 2 `.tap` | Both parts end at the Sinclair copyright/BASIC screen; the Red Hawk SNA works |
| 🟠 | Starglider (Rainbird original release 1) 128K `.tzx` | Loads to the Starglider picture and auto-stops near tape end, but does not advance beyond that screen |
| 🔴 | Starglider (Rainbird original release 2) 128K `.tzx` | Incorrectly reports that it will only run on a 128K Spectrum despite running in 128K mode |
| 🔴 | Starglider 2 128K `.tzx` | Reaches an internal tape-loader menu, but selecting the loader does not reach the game; the 48K tape works |
| 🔴 | Star Wars (Domark original) side 2 `.tzx` | Explicit key and tape resume produce an incomplete title frame; side 1 and the Hit Squad release reach complete title/menu screens |
| 🔴 | The Way of the Exploding Fist original/Ricochet `.tzx` | Both tapes end without reaching a meaningful title/game screen; the SNA works |
| 🔴 | Uridium and Uridium Plus (Hewson original) `.tzx` | Both are rejected because the BASIC header declares 42,020 bytes while the data block contains 637 bytes; their snapshots work |
| 🔴 | Wanted! Monty Mole (Gremlin original) `.tzx` | Tape ends at the Sinclair copyright screen; the SNA works |
| 🟠 | WEC Le Mans side 2 `.tzx` | Standalone side reaches a stop marker with a black frame; side 1 reaches its menu, but the cross-side continuation was not proven |

## Regression Validation

The 25 October 2026 full-folder audit covered all 159 supported-extension files
present in `C:\Users\steve\Desktop\Snapshots`: 95 tape images and 64
snapshot/replay images. Every snapshot/replay received a 2,000-frame execution
soak, and every tape received a 35,000-frame strategy-aware soak in its labelled
machine mode where specified. All final frames and transport states were
inspected; explicit on-screen key/tape instructions received targeted follow-up
runs. In total, 154 files completed the harness without an exception. Completion
alone was not treated as verification: blank screens, BASIC errors, incomplete
multipart paths, and ambiguous loader states remain in the unverified table.

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
