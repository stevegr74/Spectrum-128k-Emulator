# SZX Acceptance Validation

The automated tests construct focused SZX files in memory. This runbook adds an
independent compatibility check using unmodified SZX 1.3 snapshots produced by
Fuse/libspectrum. The binary fixtures are not redistributed in this repository.

## Source

- Repository: `https://github.com/libretro/fuse-libretro`
- Commit: `e997e2bc32c888348f862f69f2c53babfedf7791`
- 48K path: `fuse/lib/compressed/tape_48.szx`
- 128K path: `fuse/lib/compressed/tape_128.szx`

Expected SHA-256 hashes:

```text
tape_48.szx  204EEFCA0E5FC8BD95001AC74235A9AB239E77CF6BC3AC6A9BFF0D3E45006C09
tape_128.szx 16383EB704AD6583BA2F103586AC7004794B1113D7656FD09FA493747283DD5E
```

## Procedure

Place the two files in `tmp/szx-validation`, then build the Core-only manual
harness so `--no-build` cannot use a stale executable:

```powershell
dotnet build Spectrum128kEmulator.ManualHarness/Spectrum128kEmulator.ManualHarness.csproj -c Release --no-restore
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- "tmp/szx-validation/tape_48.szx" frames=2000 quiet=1 audioanalysis=1
dotnet run --project Spectrum128kEmulator.ManualHarness -c Release --no-build -- "tmp/szx-validation/tape_128.szx" frames=2000 quiet=1 audioanalysis=1
```

Both snapshots must load and execute 2,000 frames without an exception or an
automatic diagnostic dump. The 128K fixture should render the stable ROM Tape
Loader screen. The 48K fixture resumes its captured loader/startup state.

This is acceptance evidence for the supported 48K and original 128K subset. It
does not imply support for other SZX machine IDs or peripheral-state blocks.
