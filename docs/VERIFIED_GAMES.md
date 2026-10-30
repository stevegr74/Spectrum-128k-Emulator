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
| 🟢 | Cauldron (original and Silverbird/Cyberlode 1.1 releases) | `.tzx` | Both releases reach their complete title/control screens; standard-speed custom flags below `0x80` retain the required long pilot tone |
| 🟢 | Cauldron II (Erbe, original, and Silverbird/Cyberlode releases) | `.tzx` | All three releases reach the control-selection menu and remain stable; malformed accelerated expressions in the Cyberlode loader fall back before execution |
| 🟢 | Chase HQ (Portuguese, side 1) | `.tzx` | Its noncanonical protected BASIC layout starts through the native ROM in both machine modes; 48K reaches the stop/key prompt and 128K reaches the complete title screen, with transport stopped at the retained level boundary |
| 🟢 | Cliff Hanger | `.tzx` | Loading reaches the control-selection menu and remains stable |
| 🟢 | Cobra (Hit Squad and Ocean original releases) | `.tzx` | Both releases complete loading and reach the stable opening credit sequence |
| 🟢 | Donkey Kong (Erbe) | `.tzx` | Custom loader completes without corrupting the loading screen; game runs |
| 🟢 | Exolon | `.tap`, `.tzx` | Both tape formats load and reach the game |
| 🟢 | Fairlight: A Prelude | `.tap` | Loading reaches the illustrated title screen and remains stable |
| 🟢 | Feud (Bulldog original release) | `.tzx` | Loading reaches the control-selection menu and remains stable |
| 🟢 | Frankie (original Speedlock 1 release) | `.tzx` | Protected Speedlock pulse playback completes in both machine modes, ends the tape cleanly, reaches the joystick-selection menu, and accepts input into keyboard definition |
| 🟢 | Head Over Heels | `.tap` | Loading reaches the complete illustrated title screen and remains stable |
| 🟢 | Glider Rider (Bug-Byte and Quicksilva original) | `.tzx` | The Bug-Byte release reaches its complete title screen in 48K mode; the Quicksilva 128K release completes native ROM pulse loading, reaches its control menu, accepts Kempston selection, and enters gameplay |
| 🟢 | Ikari Warriors | `.tzx` | Both the original and Encore releases load and run; transport stops after loading |
| 🟢 | Impossible Mission | `.tap`, `.tzx` | The TAP plus primary, Bugfix, and Ricochet TZX releases complete protected loading and reach the control menu; protected chained BASIC stages retain exact ROM interpreter-cursor handling |
| 🟢 | Impossible Mission II | `.tzx` | US Gold original and both Kixx sides complete protected loading and reach stable game/control screens |
| 🟢 | Impossamole (Gremlin original, side 1) | `.tzx` | In 48K mode, side 1 reaches the control menu with level 0 retained after its stop marker; later-level continuation with side 2 remains unverified below |
| 🟢 | Indiana Jones and the Last Crusade | `128k.tap` | Initial load auto-stops at the picture; side-two resume loads into gameplay and preserves the next multiload boundary |
| 🟢 | Indiana Jones and the Temple of Doom (Kixx and US Gold original) | `.tzx` | Both releases reach the stop-tape prompt and advance to the complete quest-start screen; the US Gold release requires 48K mode |
| 🟢 | Kick Off 2 | `.tzx` | Loading reaches its complete title/menu prompt and remains stable |
| 🟢 | Kung-Fu Master (Americana and US Gold/Ocean original) | `.tzx` | Both releases reach their complete illustrated title screens; the original accepts input into its start/demonstration menu after its padded two-stage BASIC prefix and protected loader complete |
| 🟢 | Kwah! (Melbourne House original) | `.tzx` | In 48K mode, the ROM-driven continuation completes, ends the tape cleanly, accepts SPACE at the start prompt, and enters the game |
| 🟢 | Magician | `.tap` | Loading reaches the illustrated adventure opening and remains stable |
| 🟢 | Pac-Land | `.tzx` | In 48K mode, ROM-driven loading reaches the game/high-score screen and remains stable |
| 🟢 | Paperboy (Elite original and Encore) | `.tzx` | Both releases reach the high-score/start screen and remain stable; Encore completes protected playback and ends the tape cleanly from both 48K and 128K startup selections |
| 🟢 | Paperboy 2 (Mindscape) | `.tzx` | Loading reaches the character-selection menu and remains stable |
| 🟢 | Rambo (Hit Squad) | `.tzx` | Long protected loader completes; title and game screens run |
| 🟢 | Rambo (Ocean) | `.tzx` | Speedlock BASIC handoff uses real-ROM bootstrap; protected pulse loading completes without initial screen corruption |
| 🟢 | Red Hawk (parts 1 and 2) | `.tap` | Both headered multi-stage CODE chains preserve their post-decompressor continuation, end the tape cleanly, and reach their distinct playable start screens |
| 🟢 | Rick Dangerous (Kixx) | `.tzx` | Protected loading reaches the control-selection menu and remains stable |
| 🟢 | Robocop 2 (Ocean original) | `.tzx` | Protected loading reaches the title screen and remains stable |
| 🟢 | Robocop 3 (Ocean original, SpeedLock 7) | `.tzx` | Protected loading reaches active gameplay and remains stable |
| 🟢 | Roller Coaster (Elite) | `.tzx` | End-of-load transition starts the game correctly |
| 🟢 | Rotozlow | `.tap` | Loading reaches the demo-generation sequence and continues executing without an error |
| 🟢 | Scuba Dive (Durell) | `.tzx` | Nonstandard leading tape structure mounts and loads correctly |
| 🟢 | Space Harrier (Elite original and Encore) | `.tzx` | Both releases reach the start/control menu and remain stable |
| 🟢 | Star Wars (Hit Squad) | `.tzx` | Loading reaches the complete X-Wing control menu and remains stable |
| 🟢 | Star Wars (Domark original, sides 1 and 2) | `.tzx` | The explicit stop/key/resume sequence reaches the complete title screen from both sides; side 2 requires 48K mode |
| 🟢 | Starglider (48K original) | `.tzx` | Loading reaches the in-game control menu and automatically stops at the handoff boundary |
| 🟢 | Starglider (Rainbird original releases 1 and 2, 128K) | `.tzx` | Both releases reach the complete Starglider title screen; release 1 automatically stops at its final handoff with one trailing signal block retained, while release 2 loads through native 128 BASIC and ends cleanly |
| 🟢 | Starglider 2 (48K original) | `.tzx` | Loading reaches the complete title screen and ends cleanly |
| 🟢 | Starglider 2 (Rainbird original, 128K) | `.tzx` | Hybrid protected loading preserves the 128K paging mirror, reaches the complete illustrated title screen, and ends cleanly |
| 🟢 | Starquake (Bubblebus original and Ricochet) | `.sna`, `.tzx` | The repaired standard-size 48K SNA and both TZX releases reach the control menu and remain stable |
| 🟢 | Target Renegade | `128k.tzx` | 128K mode loads the complete game; 48K mode loads the first level and automatically pauses at the level-at-a-time boundary with resumable transport; both modes remain responsive with good audio |
| 🟢 | The Goonies | `.tzx` | Loading reaches active gameplay and remains stable |
| 🟢 | The Great Escape (Ocean original) | `.tap`, `.tzx` | Both tape formats reach the control-selection menu and remain stable |
| 🟢 | The Growing Pains of Adrian Mole (Ricochet) | `.tzx` | Loading reaches the illustrated opening story screen and remains stable |
| 🟢 | The New Zealand Story (Hit Squad) | `.tzx` | 128K protected loading reaches the menu, automatically resumes after the game enters its stable ROM tape-search loop, loads level 1-1, and auto-stops at the next multiload boundary |
| 🟢 | The Oracle's Cave (Doric Computer) | `.tzx` | ROM-driven multi-stage loading completes; maze generation and level selection lead into the playable cave |
| 🟢 | The Way of the Exploding Fist (Melbourne House original and Ricochet) | `.tzx` | Both releases complete their multi-file load and reach the animated title screen; explicit BASIC CODE destinations override conflicting header addresses |
| 🟢 | Turbo Esprit (Durell original, side 2) | `.tzx` | ROM-driven loading and the explicit copyright prompts advance to the city-selection menu |
| 🟢 | Uridium and Uridium Plus (Hewson original) | `.tzx` | Both releases preserve their intentionally malformed BASIC headers under native ROM loading and reach the animated title sequence from 48K and 128K startup; Uridium auto-stops at its completed handoff and Uridium Plus ends cleanly |
| 🟢 | Wanted! Monty Mole (Gremlin original) | `.tzx` | In 48K mode, the ROM-driven continuation completes, ends the tape cleanly, reaches the options menu, and enters gameplay |
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
| 🟢 | Target Renegade (Hit Squad filenames) | `.sna`, `.z80` | Both 128K snapshots reach the title/high-score screen and remain stable; the converted SNA also passes exact representable-state round-trip validation; menu-level checks confirm Sinclair 1 responds to keys 1-5 and Sinclair 2 to keys 6-0 |
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
| 🟠 | Chase HQ Portuguese side 2 `.tzx` | Side 2 begins with raw continuation data and is not independently bootable; side 1 is verified above, but a cross-side continuation has not been proven |
| 🟠 | Impossamole side 2 `.tzx` | Side 2 contains continuation blocks for levels 1-4 and is not independently bootable; replacing side 1 after level 0 has not yet been gameplay-proven |
| 🟠 | Indiana Jones and the Last Crusade Kixx/US Gold TZX side sets | Side 1 reaches its loading/title handoff, but mounting and completing side 2 was not proven; the 128K TAP path is verified |
| 🟠 | WEC Le Mans side 2 `.tzx` | Standalone side reaches a stop marker with a black frame; side 1 reaches its menu, but the cross-side continuation was not proven |

## Recording New Results

When adding an entry, include the exact release or filename where it matters,
the media format, and the furthest checkpoint actually observed. Record unusual
requirements such as machine mode, manual tape resume, protected loading, or a
known limitation. Keep unconfirmed or intermittent failures out of this verified
list until they can be reproduced and diagnosed.
