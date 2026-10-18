# ULA Timing Validation

Milestone 16 is checked against the published diagnostic programs in the
[ZX-M8XXX test collection](https://github.com/Bedazzle/ZX-M8XXX):

- Patrik Rak's Timing Test 0.3, based on Jan Bobrowski's zxtests
- Mark Woodmass's Float48K and Float128K floating-bus tests

Those programs and their reference images are GPL-licensed and are not
redistributed by this MIT-licensed repository. Obtain them from the upstream
project and keep the extracted files outside this working tree.

## Timing Test

Run `timing.tap` through the real-ROM path in each machine model. The harness key
script selects a numbered test after BASIC has loaded the program; replace `N`
with `0` through `7`.

```text
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- <timing.tap> frames=6000 strategy=rom model=48 quiet=1 "140:N:down,142:N:up,150:enter:down,152:enter:up"
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- <timing.tap> frames=6000 strategy=rom model=128 quiet=1 "140:N:down,142:N:up,150:enter:down,152:enter:up"
```

The opening screen must report frame durations of `69888` and `70908`. Tests 0
through 7 must match the corresponding upstream 48K/128K reference captures for
contended NOP, snow-effect NOP, and ports `00FE`, `00FF`, `7FFE`, `7FFF`, `FFFE`,
and `FFFF`. Test 8 additionally exercises 128K paged-memory return timing.

## Floating Bus

Run each floating-bus TAP in its matching model:

```text
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- <float48k.tap> frames=600 strategy=rom model=48 quiet=1
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- <float128k.tap> frames=600 strategy=rom model=128 quiet=1
```

Compare the generated BMP with the upstream reference image. The tested address
and value sequences must match exactly; differences in an outer capture margin
are not emulator output.

## Recorded Baseline

The 2026-10-18 Milestone 16 run matched both floating-bus references and all
published Timing Test 0.3 reference captures pixel-for-pixel after excluding
only their outer capture margins. Test 8 completed with a zero-delay matrix on
the default uncontended 128K page.
