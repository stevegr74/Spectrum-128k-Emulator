Current checkpoint on 2026-06-12:

- User-verified accepted tape baseline:
  - `Exolon.tzx` works again
  - `exolon.tap` works
  - `Impossible Mission - Bugfix.tzx` works
  - `Batman - Release 1.tzx` now loads to the game path

- User-verified runtime/UI baseline:
  - emulator boots normally
  - FPS is effectively stable around `49/50/51` on the accepted tape paths
  - current menu/composite-key behavior is acceptable enough to keep

- Important generic tape decisions now in place:
  - raw-standard mixed tapes use the bootstrap/hybrid mounted path instead of the older ROM-bootstrap-mounted path
  - mounted `IF ... THEN USR(...)` continuation steps directly evaluate safe numeric-variable expressions using BASIC-style default-zero semantics
  - mounted continuation variable reads decode integer-valued Spectrum floating-point numeric variables generically
  - mounted ROM data loads refresh the preserved BASIC variable snapshot before later continuation steps use it
  - early ROM sync-loop traps can consume unstructured standard ROM-loadable data blocks, not just structured header/data contexts
  - mounted continuations can resume during pauses before custom non-ROM blocks, but not before pending ROM-loadable blocks
  - mounted continuations also avoid resuming during pauses before unstructured standard ROM-loadable data blocks

- Important generic runtime fix from this checkpoint:
  - mounted tape idle/reset EAR polarity must remain high-idle
  - forcing those mounted idle/reset states low was the real cause of the latest `Exolon.tzx` regression
  - Batman still works with the high-idle fix in place

- Performance/runtime baseline now kept:
  - emulation and audio submission run on a background loop
  - the UI presents copied snapshots at fixed 50Hz cadence
  - muted turbo tape loads skip unnecessary per-frame audio-frame construction
  - protected non-ROM live streams use the safer lower turbo ceiling
  - emulated FE/tape pulse timing stays exact during live-tape acceleration; only wall-clock execution is accelerated

- Renderer baseline now kept:
  - normal Spectrum colours use `0xD7`
  - bright Spectrum colours use `0xFF`

- Guard coverage expected to stay green:
  - `TzxLoaderTests`
  - `TapLoaderTests`
  - `MachineCoreTests`
  - `SpectrumRendererTests`
  - `SpectrumKeyInputBridgeTests`

- Best next step from this checkpoint:
  - continue with broader generic `.tzx` compatibility work from the now-restored Batman / Exolon / Impossible baseline
  - do not regress `Exolon.tzx`, `exolon.tap`, or `Impossible Mission - Bugfix.tzx`
  - prefer format- and structure-driven fixes over title-specific hacks
