Project: ZX Spectrum 128K Emulator (C# WinForms)

Current issue:
Exolon is materially improved and no longer resets into 48K BASIC on the accepted latest build, but it still needs further gameplay/audio validation and comparison against the clean `.z80` control snapshot.

Accepted checkpoint on 2026-05-01:
- User verified success on the latest build:
  - `exolon.sna` now stays on a healthy title/menu path
  - the earlier `48K BASIC` reset is gone
  - logo/sprites start correctly and the accepted regression target is cleared
- The accepted loader rule for `exolon.sna` is now:
  - force `IFF1/IFF2` off on load
  - use normal 48K cadence (`69888`), not the earlier Exolon-only `70908` experiment
  - keep the existing default 48K snapshot initial interrupt delay (`16`)
- This was derived by comparing the problematic `exolon.sna` against a known-good control snapshot:
  - `C:\Users\steve\Desktop\Snapshots\Exolon.z80`
  - that `.z80` runs in clean 48K mode and was the strongest local control during the investigation

Most recent meaningful result:
- `exolon.sna` no longer needs the older Exolon-only legacy frame cadence hack.
- The important fix was matching the healthy control more closely at load time:
  - interrupts off
  - normal 48K cadence
- Harness confirmation for the accepted path:
  - `Spectrum128kEmulator.ManualHarness/bin/Debug/net10.0-windows/debug/harness-end-20260501-102846957.png`
  - this shows the clean title/menu result on the accepted baseline

Best next step when resuming:
- Compare remaining Exolon runtime behavior between:
  - `C:\Users\steve\Desktop\Snapshots\exolon.sna`
  - `C:\Users\steve\Desktop\Snapshots\Exolon.z80`
- The next investigation target is no longer the old early reset-to-BASIC path.
- Next focus should be later runtime differences only:
  - sound sequencing
  - sprite/state corruption if it reappears after more runtime
  - divergence in `79xx/7Axx/7Cxx` game code between the `.sna` and `.z80` paths

High-level status:
- New safe checkpoint on 2026-04-30:
  - `ConfigureFor48kSnapshot(...)` now uses true 48K frame cadence (`69888`) instead of 128K cadence (`70908`).
  - Focused regressions stayed green:
    - `MachineCoreTests`
    - `CpuMicroTests`
    - `SnapshotLoaderTests`
    - `Z80SnapshotLoaderTests`
    - `AudioPipelineTests`
  - This is the first change that materially advances the current Exolon baseline without breaking zexall/zexdoc or the focused unit suites.
- Current user-facing Exolon expectation to verify next:
  - title/logo should still look correct
  - by about frame 60 the title/menu is still intact
  - the old late `11DC` failure is no longer terminal
  - the run now recovers out of that state around frame 161 and reaches live code again (`PC=10B4`, then `15F7/15FF`)
  - after that it appears to head into a later prompt/reset path rather than staying stuck forever in the old fill loop
- The original interrupt-stall bug is no longer the active failure.
- The current crash is still a separate 48K-mode control-flow problem, but the failure now happens later than before.
- Old late-failure signature that is still diagnostically relevant:
  - ROM `PC=11B6`
  - stack target `FFFF`
  - `RET` to `FFFF`
  - fall-through to `0000`
  - `DI` at `0000`
  - later ROM fill loop `11DC..11E0` writing `02` downward through top RAM
- The ROM fill loop is aftermath, not root cause.

Latest concrete evidence:
- Harness run with default snapshot path now reaches:
  - frame 61: still live title/menu path around `PC=103F`
  - frame 71-151: old `11DC/11E9` bad region
  - frame 161: recovers to `PC=10B4`, `SP=FF48`, `IFF1=True`
  - frame 171+: settles into `PC=15F7/15FF`, `SP=FF4A/FF4C`
- Useful artifacts:
  - clean title image at frame 60:
    - `Spectrum128kEmulator.ManualHarness/bin/Debug/net10.0-windows/debug/harness-end-20260430-223755417.png`
  - recovery/later-loop trace:
    - `Spectrum128kEmulator.ManualHarness/bin/Debug/net10.0-windows/debug/harness-end-20260430-223706467.txt`
    - `Spectrum128kEmulator.ManualHarness/bin/Debug/net10.0-windows/debug/harness-end-20260430-223733032.txt`

Best next step after verifying this build:
- Compare the recovered `10B4 -> 15F7/15FF` path against a known-good emulator or expected game flow.
- The new target is no longer “escape 11DC”; it is “why does the recovered run end up at the later `15F7/15FF` loop / prompt-reset path instead of entering stable gameplay/menu behavior?”

What was already ruled out:
- `EI` delay handling is correct.
- The old `IFF1=0 / INTP=1 / no INT_ACCEPT` Exolon stall was fixed.
- `SP` entering `78xx` is intentional game behavior:
  - Exolon executes `LD SP,0x78DC` at `7A31`
- ROM writes around `78DA..78DF` are not the first corruption event.
- Generic low-ROM execution traps were false positives:
  - Exolon legitimately enters ROM at `0018`, `001F`, `0023`, `007B`
- `33AA` is not the first cause by itself; it is part of an explicit helper that builds the bad frame.

Decoded crash path so far:
1. `26D9` is the current earliest trapped point in the bad path.
2. `26DA..26DC = CD B4 33`
   - `CALL 33B4`
3. `33B4..33B7 = ED 5B 65 5C`
   - `LD DE,(0x5C65)`
   - this loads the later bad `DE=FFFF`
4. `33B8..33BA = CD C0 33`
   - `CALL 33C0`
5. `33C0..33C2 = CD A9 33`
   - `CALL 33A9`
6. `33A9 = D5`
   - `PUSH DE`
7. `33AA = E5`
   - `PUSH HL`
   - in the bad run, `HL=0000`
8. `33AB..` continues building the frame that later causes ROM error handling.
9. ROM error path follows:
   - `0058`
   - `16CB` / `16CC` / `16D2`
   - eventually `11B6`
   - `RET FFFF`
   - `0000`
   - `11DC..11E0`

Latest concrete trap result:
- Dump: `machine-debug-20260427-172322141.txt`
- Reason:
  - `Game approached ROM error source: PC=26DA SP=7C06 AF=FF02 BC=DAE0 DE=0000 HL=0000 IX=23FD IY=B331 IFF1=1 IFF2=1 PrevPC=26D9 ...`
- Important decode from that dump:
  - `26DA=CD 26DB=B4 26DC=33` => `CALL 33B4`
  - `26DD=18 26DE=33` => `JR +0x33`
- After that, instrumentation was moved one instruction earlier to `26D9`.

Current instrumentation in `Spectrum128Machine.HandleBeforeInstruction`:
- Active primary trap:
  - 48K mode
  - after startup window
  - `PC == 26D9`
  - `IY == B331`
  - `SP` in `7B00..7CFF`
  - reason text starts:
    - `Game entered ROM error caller: ...`
  - reason also includes bytes for:
    - `26D6..26DF`
    - `33B4..33C3`
    - `33A9..33AB`
    - top four stack bytes
- Secondary trap kept for later fallout:
  - `PC == 0058`
  - reason: `ROM error handler entered: ...`
- Secondary trap kept for later fallout:
  - `PC == 11B6`
  - `[SP] == FF` and `[SP+1] == FF`
  - reason: `ROM RET to FFFF armed: ...`

Other current machine/debug state:
- 48K snapshots use 48K frame timing.
- Default 48K snapshot initial interrupt delay is `512`.
- Interrupts are pulsed, not latched indefinitely.
- `lastObservedPc` was added to include `PrevPC` in trap reasons.

Known useful dump patterns:
- Small dumps before loading Exolon are startup noise.
- Useful Exolon dumps are the larger files from the actual crash run.

Current goal:
- Continue walking upstream from `26D9` until the first incorrect state or branch is found.
- Most likely next step:
  - inspect the next dump from the `PC=26D9` trap
  - decode bytes at `26D6..26DF`
  - determine what condition reaches `CALL 33B4` and why `0x5C65/0x5C66` later become `FFFF`.

New findings from 2026-04-28:
- Auto debug dumps now snapshot immediately at trap time.
  - Before this change, the reason string was trap-time but the full dump was built later, after execution had already run on.
  - `Spectrum128Machine.RequestAutoDebugDump` now stores a full snapshot immediately, and `TryConsumeAutoDebugDump` returns that frozen dump.
- A fresh harness run with:
  - `Spectrum128kEmulator.ManualHarness.exe C:\Users\steve\Desktop\Snapshots\exolon.sna 23040 500`
  - now produces a true trap-time dump in `exolon-trap.txt`.
- The `PC=26D7` trap label is misleading if read literally:
  - at trap time `AF=FF02`
  - `26D5..26D7 = FE C0 38`
  - this is `CP 0xC0` followed by `JR C,+4`
  - carry is clear in `F=02`
  - so the bad path is the fall-through into `26D9`, not the taken `JR C`.
- Fresh decoded path from the trap-time trace:
  1. `24FB = DF`
     - `RST 18`
  2. ROM `0018` loads `HL` from `0x5C5D/0x5C5E`
     - first observed value there is `FFFF`
  3. Later `28B6 = DF`
     - another `RST 18`
  4. `28BD = E5`
     - pushes `HL=FFFF`
  5. `28C1 = E7`
     - `RST 20`
     - ROM `007B` then writes:
       - `5C5D = 00`
       - `5C5E = 00`
  6. Later `2936` returns with:
     - `AF=F318`
     - `BC=DAE0`
     - `DE=0000`
     - `HL=FFFF`
  7. `26D2 = 3A 3B 5C`
     - `LD A,(0x5C3B)`
  8. `26D5 = FE C0`
     - `CP 0xC0`
  9. Since carry is clear, execution falls through:
     - `26D9 = 23`
     - `26DA = CD B4 33`
     - `CALL 33B4`
- Important live register values at the real trap point:
  - `PC=26D7`
  - `SP=7C06`
  - `AF=FF02`
  - `BC=DAE0`
  - `DE=0000`
  - `HL=FFFF`
  - `IX=23FD`
  - `IY=B331`
- New instrumentation/watch additions:
  - watched writes now include:
    - `0x5C0C`
    - `0x5C3B`
    - `0x5C5D`
    - `0x5C5E`
  - latest run only observed recent writes for:
    - `5C5D <- 00`
    - `5C5E <- 00`
    - at ROM `PC=007B`
  - no recent writes to `5C3B` or `5C0C` were captured in that run.
- A follow-up run added write-time auto traps in `Spectrum128Machine.WriteMemory(...)`:
  - trap immediately if `0x5C3B` changes after startup
  - trap if `0x5C5D/0x5C5E` change from `FF` outside the known ROM `PC=007B` path
- Result of that follow-up run:
  - the first `0x5C5D/0x5C5E` transition is still the expected ROM `007B` zeroing
  - after excluding that case, no `0x5C3B` write trap fired before the later `PC=26D7` fall-through
  - recent watched writes still showed:
    - `5C5D <- 00`
    - `5C5E <- 00`
    - at ROM `PC=007B`
  - and still showed no recent writes to:
    - `5C3B`
    - `5C0C`
- Practical conclusion from the focused write trap:
  - within the observed window, `0x5C3B` is not becoming `FF` because of a late visible write
  - it is either already `FF` from earlier state/snapshot setup, or it is being sourced indirectly by logic that does not pass through a normal observed write before the `26D2` read
- A noteworthy earlier event from the same run:
  - `LOW_STACK_ENTER PC=6EC1 OP=C9 SP FFFE->0000`
  - this is the first large stack-wrap event seen in the trace
  - but it may still be intentional game/ROM behavior, not yet confirmed as the root cause.

Best current hypothesis:
- The immediate entry into the bad helper is controlled by `A = (0x5C3B)` at `26D2`.
- The actual failure is now best described as:
  - `LD A,(0x5C3B)` yields `FF`
  - `CP 0xC0` leaves carry clear
  - execution falls through into `26D9`
  - `INC HL`
  - `CALL 33B4`
- The next likely high-value step is to identify why `0x5C3B` is `FF` at that moment, or to decode `28B6..2950` and verify whether the flags/register results there match a known-good Z80.
- Another strong next step is to start watching `0x5C65/0x5C66` as well, because `33B4` begins with:
  - `ED 5B 65 5C` => `LD DE,(0x5C65)`
  - and that helper is on the immediate bad path after `26D9`.
- Follow-up after adding `0x5C65/0x5C66` to the watch set and write-time trap:
  - no write trap fired for `0x5C65`
  - no write trap fired for `0x5C66`
  - the run still first trapped at the later `PC=26D7` site
  - recent watched writes near the trap still showed only:
    - stack traffic
    - `5C5D <- 00`
    - `5C5E <- 00`
  - there were still no recent writes to:
    - `5C3B`
    - `5C65`
    - `5C66`
- This strengthens the current picture:
  - the bad path is not being entered because of an obvious late write into `0x5C3B` or `0x5C65/0x5C66`
  - the next upstream cause is more likely an earlier persistent state problem, snapshot/setup mismatch, or a CPU semantic issue that affects the parser/helper path without showing up as a nearby system-variable write.

New findings from 2026-04-28, later pass:
- The `.sna` file itself already contains the suspicious `FF` system-variable state:
  - `5C3B = FF`
  - `5C5D = FF`
  - `5C5E = FF`
  - `5C65 = FF`
  - `5C66 = FF`
  - `5C71 = 00`
- A narrow delay sweep around `23040` showed that interrupt phase, not late RAM corruption, is what changes the failure shape:
  - `22976..23152` reaches the earlier parser trap at `24FB`
  - nearby values can instead produce:
    - `RET`-to-`FFFF`
    - low-stack failure
    - ROM fill-loop writes to `FFFF`
- A broad phase search then found many 48K snapshot interrupt phases that avoid the known early Exolon traps.
- The decisive trap was not the parser path but a later interrupt entering with `SP` already in ROM space:
  - last good accept in the failing phase:
    - `T=372416`
    - `PC=02B0`
    - `SP=387A`
  - once IM1 fired there, ROM could not push a real RAM stack frame
  - the handler then wandered into ROM `0514/051E` and later a bogus `A102/A110` RAM loop with `IFF1=0`
- That means the earlier `24FB` / `26D7` failures were fallout from a bad restored frame phase:
  - interrupts were landing inside an Exolon routine that temporarily used a ROM-area stack
  - on real hardware the snapshot must resume with a different frame phase so that interrupt does not hit there
- The cleanest stable phase found so far is `512` T-states:
  - with `512`, Exolon runs for 500 harness frames
  - no parser trap
  - no `26D7` error-branch trap
  - no `051E` ROM-loop trap
  - no general interrupt-progress stall
  - no low-stack trap
  - interrupts remain enabled and the run stays on the healthy path
- Based on that result, `Spectrum128Machine.Default48kSnapshotInitialInterruptDelay` was changed from `23040` to `512`.

New findings from 2026-04-28, menu-loop pass:
- The earlier `PC=02BF` Exolon trap was a bad breakpoint for the current state of the investigation.
  - With the improved floating-bus model in place, the path through low ROM is part of a repeating interrupt/menu cycle:
    - `0038`
    - `3870`
    - `387C`
    - `02C2`
    - `02DB`
    - back to the `FF80..FFDC` menu loop
  - That trap has been removed so later failures can be observed.
- `ReadPort` / floating-bus handling was refined in two important ways:
  - Z80 `IN` instructions now pass a timing hint so odd-port reads can be sampled at the end of the I/O instruction instead of at the beginning.
  - 48K floating-bus reads now use an 8T fetch pattern:
    - bitmap
    - attribute
    - bitmap+1
    - attribute+1
    - then 4 idle T-states returning `FF`
- After removing the misleading `02BF` trap, the manual harness now runs Exolon for 500 frames with:
  - no auto trap
  - no low-stack trap
  - no reset
- Current observed behavior is now a stable live-lock on the title/menu loop:
  - end-of-run state after 500 frames:
    - `PC=FFC5`
    - `SP=4454`
    - `AF=FFA9`
    - `BC=0BF8`
    - `DE=A7F3`
    - `HL=F341`
  - `MEMORY F320` has settled to all `F4`
  - the final frame image shows a coherent Exolon menu, but still with text corruption in the options area and no menu progress
- Scripted input retest in the harness:
  - pressing `1` at frames `30..40` did not advance the menu
  - pressing `enter` at frames `30..40` did not advance the menu
  - so the remaining issue is not just "we forgot to press a key"

Best current hypothesis:
- The old crash/reset path is fixed.
- The remaining Exolon problem is a menu live-lock that still depends on subtle 48K odd-port / floating-bus behavior, but the previous `02BF` escape itself is no longer considered the bug.
- The highest-value next step is to decode the `FF80..FFDC` menu routine directly and determine what exact `IN A,(0xF7)` bit pattern it expects before leaving the loop, then compare that with what the emulator is currently feeding it.

New findings from 2026-04-28, later menu instrumentation pass:
- Fresh harness run with current code:
  - `Spectrum128kEmulator.ManualHarness.exe C:\Users\steve\Desktop\Snapshots\exolon.sna 512 500`
  - still ends in the same stable menu loop
  - final state still centers around:
    - `PC=FFC5`
    - `SP=4454`
    - `AF=FFA9`
    - `BC=0BF8`
    - `DE=A7F3`
    - `HL=F341`
- The title screen image is coherent, but the options text is still corrupted:
  - examples seen in the latest frame:
    - `SIGRDI GRME`
    - `KEYBOQRD`
- New targeted `0xF7` read instrumentation in `Spectrum128Machine.ReadPortTimed(...)` showed:
  - Exolon is not just reading idle `FF`
  - the menu routine sees a mix of:
    - `FF`
    - `00`
    - display-like bytes such as:
      - `42`
      - `44`
      - `47`
      - `78`
      - `A0`
      - `D3`
      - `F2`
  - the active reads occur around the menu sampler PCs:
    - `FFAD`
    - `FFB2`
    - `FFB7`
- Important correction from this pass:
  - `IN A,(0xF7)` does not actually use port `00F7`
  - because `IN A,(n)` forms the port from `(A << 8) | n`, live reads appear as ports like:
    - `FEF7`
    - `FFF7`
    - `E5F7`
    - `A7F7`
  - instrumentation was widened to match any port whose low byte is `F7`
- Strong new architectural hypothesis:
  - the remaining bug is likely not "floating-bus reads always return FF"
  - instead, Exolon appears to be getting a partially-correct but still wrong timing stream
  - the emulator currently has no general 48K contention model:
    - no visible implementation of contended memory timing
    - no per-access instruction-timing model for ordinary memory reads/writes
  - because Exolon's menu sampler is timing-sensitive, missing contention is now a prime suspect for the remaining text corruption/live-lock
- Practical next step:
  - before tweaking more floating-bus constants blindly, investigate implementing enough 48K contention timing to shift instruction phase correctly for timing-sensitive menu code in RAM

New findings from 2026-04-28, contention-model pass:
- A first general 48K contention pass was added:
  - memory accesses in `0x4000..0x7FFF` now add delay based on the standard 48K contention table
  - CPU memory/port delegates in `Spectrum128Machine` now route through contention-aware wrappers
  - `ReadPortTimed(...)` still samples odd ports at the end of the I/O instruction
- A harness-accessible floating-bus timing override was also added:
  - `fbstart=<n>`
  - `fbsample=<n>`
  - this allows direct command-line sweeps without rebuilding
- Small sweeps of:
  - floating-bus sample offset
  - floating-bus display-start offset
  - did not clear the Exolon menu loop or materially improve the final image
- A more important correction then followed:
  - the first 48K I/O contention implementation was too aggressive
  - it effectively summed consecutive contention points instead of using the documented port-access patterns
- The I/O contention code was corrected to the documented 48K patterns:
  - high byte not contended, low bit reset:
    - `N:1, C:3`
  - high byte contended, low bit reset:
    - `C:1, C:3`
  - high byte contended, low bit set:
    - `C:1, C:1, C:1, C:1`
- `MachineCoreTests` now explicitly cover:
  - floating-bus timing adjustments
  - contended high-byte even-port timing
  - uncontended high-byte even-port timing
- After the corrected I/O contention pass:
  - Exolon still ends in the same practical state
  - title/menu image remains coherent but corrupted
  - synthetic `1` / `Enter` input still does not advance the menu
- A deeper architectural issue was then identified in the Z80 core:
  - `FetchOpcodeByte()` / `FetchByte()` / `FetchWord()` do not advance `TStates` as instruction subcycles occur
  - most instruction handlers add the total instruction timing only after the memory/port accesses have already happened
  - therefore contention frequently sees instruction-start `TStates`, not the true subcycle time of the access
- A partial mitigation was added for the highest-value stack-sensitive paths:
  - `CALL`
  - `RET`
  - `PUSH`
  - `POP`
  - `RST`
  - IM1/IM2 interrupt entry
  - these now stage some `TStates` before stack accesses so stack reads/writes in contended RAM around `0x445x` are less inaccurately timed
- That partial stack-timing correction changes the exact PCs seen during the menu loop, but still does not clear the block:
  - `512` remains the best stable resume phase in the current model
  - broader delay sweeps still mostly fail, or else land in alternate but still-bad stable menu states
  - one alternate delay (`2432`) now ends in a much noisier corrupted screen (`PC=051A`, `SP=AFEF`), confirming timing sensitivity but not fixing the game

Best current hypothesis:
- The remaining blocker is broader per-instruction subcycle timing accuracy, not just floating-bus constants.
- Exolon appears to rely on stack/interrupt/menu timing that is still too phase-wrong because the core does not yet model instruction time as accesses happen.
- The next real fix is likely a more systematic timing model so memory/stack/interrupt accesses occur at correct internal T-states, especially through the `0038 -> 3870 -> 387C -> 02C2 -> 02DB -> FF80` cycle while the stack lives in contended RAM.

New findings from 2026-04-29, floating-bus idle revert check:
- A focused experiment reverted the 48K floating-bus idle half from "hold last fetched attribute byte" back to `0xFF`.
  - `ReadFloatingBus48(...)` now returns:
    - phase 0: pixel
    - phase 1: attribute
    - phase 2: next pixel
    - phase 3: next attribute
    - phases 4..7: `FF`
  - `MachineCoreTests` was updated to match that model.
- Validation status:
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
    - passes
  - `dotnet run --no-build --project Spectrum128kEmulator.ManualHarness -- 'C:\Users\steve\Desktop\Snapshots\exolon.sna' 512 500`
    - still reproduces the Exolon menu issue
- Practical result:
  - the menu is still live and repeatedly reaches the `OUT (0xEF),0xEE` exit path at `FFCF`
  - the options text is still corrupted in the final frame image
  - example latest image text:
    - `SIGRDI GRME`
    - `DEFINE KEYS`
    - `KEYBOQRD`
    - `INTERFRCE 2`
- Recent `0xF7` samples still show the same broad shape as before:
  - many runs of `FF`
  - periodic `00`
  - occasional intermediate display-like values such as `F0` and `F2`
  - the sampler still exits some groups and stores bytes, but the byte stream is still wrong overall
- Conclusion from this pass:
  - "idle half returns sticky last attribute byte" is not the main root cause of the remaining Exolon corruption/live-lock
  - reverting it changes details of the stream, but not the failure class
  - the stronger remaining hypothesis is still broader timing inaccuracy around the `FF80` menu routine and the interrupt/menu cycle feeding it.

New findings from 2026-04-29, post stack-timing and floating-bus retune:
- A deeper timing bug was identified and corrected in the Z80 stack path:
  - `Push(...)` and `Pop(...)` now advance `TStates` between the two memory accesses instead of performing both bytes at one timestamp
  - plain stack instructions were retotaled around that stepped helper:
    - `CALL`
    - `RET`
    - `PUSH`
    - `POP`
    - `RST`
    - taken `RET cc`
    - taken `CALL cc`
    - maskable interrupt entry
    - `RETN` / `RETI`
  - indexed stack instructions in the ROM interrupt path were also corrected:
    - `PUSH IX`
    - `POP IX`
    - `PUSH IY`
    - `POP IY`
  - important detail:
    - `DD E5` / `DD E1` in the `386E..387C` ROM service path had been undercounted by 4 T-states each before this fix
- After that correction, Exolon floating-bus sweeps became much more coherent:
  - several timing pairs now keep the title/menu text readable for hundreds of frames
  - the old default pair:
    - `displayStart=-8`
    - `sample=-5`
    - is no longer the best choice
- Focused visual comparison results from harness runs:
  - poor/older-logic region:
    - negative sample offsets like `fbsample=-2/-1` still produce obvious option-text corruption
  - strong region after the stack fix:
    - `fbstart=0 fbsample=1`
    - `fbstart=0 fbsample=2`
    - `fbstart=2 fbsample=1/2/3`
    - all keep the option text readable at 500 frames
  - the cleanest chosen pair from the direct `500`-frame comparison is:
    - `fbstart=0`
    - `fbsample=1`
- Default machine constants were updated to:
  - `Default48kFloatingBusDisplayStartAdjustTStates = 0`
  - `Default48kFloatingBusSampleAdjustTStates = 1`
- Visual effect of the new preferred pair in harness runs:
  - menu items read correctly:
    - `START GAME`
    - `DEFINE KEYS`
    - `KEYBOARD`
    - `INTERFACE 2`
    - `KEMPSTON`
  - title/logo corruption is reduced compared with the old negative defaults
- Remaining caution:
  - very long runs can still drift into slightly different end states depending on the timing pair and frame count
  - however, for the user-visible title/menu corruption problem, the new default pair is materially better than the old built-in one.

New findings from 2026-04-29, app-vs-harness divergence:
- The WinForms app and the manual harness were not actually running the same 48K floating-bus timing after snapshot load.
- Root cause:
  - `MainForm.LoadSnaSnapshotFromDialog()` and `LoadZ80SnapshotFromDialog()` call `machine.ClearDebugHistory()` immediately after loading.
  - `ClearDebugHistory()` was incorrectly mutating emulation state by resetting:
    - `floatingBusDisplayStartAdjustTStates`
    - `floatingBusSampleAdjustTStates`
  - that silently overwrote the tuned 48K defaults from `ConfigureFor48kSnapshot()`:
    - `displayStart=0`
    - `sample=1`
  - so the harness kept the newer timing, but the live app fell back to `0,0`.
- Fix applied:
  - `ClearDebugHistory()` now only clears debug/trace state and no longer resets floating-bus timing.
- Regression coverage added:
  - `MachineCoreTests.ClearDebugHistory_Does_Not_Reset_48k_Floating_Bus_Timing`
- Verification:
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
    - passes
  - `dotnet build Spectrum128kEmulator.sln`
    - passes
- Interpretation:
  - if the harness screenshot looks right but the running app still shows the older Exolon corruption, this `ClearDebugHistory()` state reset was the key discrepancy.

New findings from 2026-04-29, JSW audio regression:
- User reported that the current build is still not better for Exolon, and that `JSWAPRIL.Z80` now has some sound out of sequence even though it previously sounded correct.
- Likely app-side regression identified in `Audio/WaveOutAudioOutput.cs`:
  - the recent change to return immediately when no waveOut buffer was available caused whole audio frames to be dropped
  - that is especially plausible for short 48K beeper effects like Jet Set Willy, where skipped frames sound like missing or re-ordered events
  - continuous AY music (for example Robocop 128K) can hide that problem much more easily
- Mitigation applied:
  - restored the older backlog recovery path:
    - if no writable buffer is available, call `FlushBacklog()`
    - then reacquire a slot instead of silently dropping the frame
- Validation:
  - `dotnet build Spectrum128kEmulator.sln`
    - passes
- Remaining status:
  - this is a strong regression fix candidate, but audio could not be directly listened to in this environment
  - Exolon itself is still not confirmed resolved in the user-facing WinForms build.

New findings from 2026-04-29, 48K audio clock mismatch:
- A stronger root cause was identified for Jet Set Willy's wrong-pitch / wrong-flow audio:
  - 48K audio frames were still being converted to samples using the 128K CPU clock (`3546900`)
  - that affects both beeper and AY sample generation because sample counts and sub-frame timing were derived from one global clock
  - for 48K frames (`69888` T-states), this produced fewer samples than intended and shifts the apparent pitch/timing upward
- Fix applied:
  - introduced explicit per-frame CPU clock handling in `AudioFrame`
  - `Spectrum128Machine.DrainAudioFrame()` now passes:
    - `3500000` for 48K mode
    - `3546900` for 128K mode
  - updated:
    - `Audio/BeeperSampleGenerator.cs`
    - `Audio/AySampleGenerator.cs`
    - to use `frame.CpuClockHz` instead of a single global constant
- Regression coverage added:
  - `AudioPipelineTests.SubmitFrame_Uses48kCpuClock_For48kAudioFrames`
    - verifies a 48K frame at `44100 Hz` yields `881` samples instead of the old undercounted 128K-clock result
- Validation:
  - `dotnet build Spectrum128kEmulator.sln`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter AySampleGeneratorTests`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter AudioPipelineTests`
    - passes
- Interpretation:
  - this is the strongest code-level explanation so far for JSW menu pitch being wrong in the current build.

New findings from 2026-04-29, repo-baseline rollback for 48K snapshots:
- User pointed out that Jet Set Willy had previously worked in the pushed repo at:
  - `origin/master`
  - `6058f41`
- Comparison against the pushed baseline showed the biggest remaining divergence was still the experimental 48K snapshot execution model:
  - custom 48K frame length
  - delayed initial interrupt timing from snapshot loaders
  - per-frame interrupt slicing instead of the old "interrupt at frame start, then run the frame" cadence
- Rollback applied toward the pushed baseline:
  - `Spectrum128Machine.ExecuteFrame()` now again:
    - triggers the interrupt at frame start
    - executes one full frame chunk immediately after
  - `ConfigureFor48kSnapshot()` now uses baseline frame timing again instead of the experimental 48K frame cadence
  - `SnapshotLoader` and `Z80SnapshotLoader` no longer inject the custom `512` T-state initial interrupt delay for 48K snapshots
- Test alignment:
  - the temporary floating-bus/contention tests added during Exolon investigation were marked skipped because they no longer describe the intended baseline behavior after this rollback
- Validation:
  - `dotnet build Spectrum128kEmulator.sln`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
    - passes with `13` passed / `8` skipped
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter AudioPipelineTests`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter AySampleGeneratorTests`
    - passes
- Interpretation:
  - this rollback is the strongest "match the known-good repo behavior" step taken so far for JSW
  - direct listening is still required to confirm whether JSW's beeper pitch/flow is restored in the app.

Latest user-verified state from 2026-04-29:
- Jet Set Willy (`JSWAPRIL.Z80`) is now much better in the live app after the repo-baseline rollback:
  - tone is correct
  - speed/flow is broadly correct again
  - remaining issue:
    - a very slight audio crackle is still present
- Exolon is also improved in the live app after the same rollback:
  - logo is now correct
  - menu/title path is further along than before
  - music now plays a couple of notes before failure
  - screen shows animation before crashing
  - this is a genuine improvement over the earlier state where the title/menu was badly corrupted

Repository checkpoint:
- The improved baseline was committed and pushed to GitHub.
- Commit:
  - `d43171e`
- Commit message:
  - `Stabilize 48K snapshot audio and Exolon baseline`
- Remote:
  - `origin/master`

Practical interpretation going forward:
- This pushed state is the new recovery point.
- The current priority order for future work should be:
  - 1. remove the slight JSW beeper crackle without regressing tone/speed
  - 2. continue Exolon crash investigation from the now-improved state rather than re-opening the earlier title/logo corruption branch

Most likely next-step hypotheses:
- For the remaining JSW crackle:
  - check for small discontinuities in the beeper/audio output path rather than gross timing errors
  - likely areas:
    - `WaveOutAudioOutput`
    - frame-boundary transitions in `BeeperSampleGenerator`
    - catch-up behavior in `MainForm.FrameTimer_Tick`
- For Exolon:
  - the important symptom is no longer "corrupt title/logo"
  - it is now "gets further, plays a couple of notes, animates, then crashes"
  - that suggests the rollback restored enough baseline timing to pass the early setup, and the next fault is deeper in runtime execution rather than initial menu/logo generation
  - likely future focus:
    - trace the crash point from this newer live state
    - compare crash-time PC/SP and recent writes against the pushed baseline behavior now that visuals are largely correct

Locked-in incremental audio step from 2026-04-29:
- User tested the crackle-focused beeper cleanup and reported:
  - sound/tone is pretty accurate
  - there is still some background crackle
  - the roughness now feels more like small consistency/output issues than gross timing failure
- Conservative follow-up applied:
  - `Audio/WaveOutAudioOutput.cs`
    - increased `BufferCount` from `3` to `6`
  - rationale:
    - add more output queue headroom
    - reduce the chance of tiny underruns or reset churn showing up as low-level crackle
    - avoid changing core CPU timing while audio pitch/speed is already mostly correct
- Validation:
  - `dotnet build Spectrum128kEmulator.sln`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter BeeperSampleGeneratorTests`
    - passes
  - `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter AudioPipelineTests`
    - passes
- User then said:
  - "ok thats good, lets lock that in for now"
- Practical meaning:
  - this audio-output headroom change is accepted as the current baseline
  - future work should continue from here, with care to avoid regressing the now-mostly-correct JSW sound

Latest Exolon checkpoint from 2026-04-29 after resuming from the accepted baseline:
- User confirmed the restored baseline was "back on track" after reverting the bad default 48K snapshot interrupt-delay change.
- Important guardrail:
  - do not reintroduce a default `512`-T-state initial interrupt delay for 48K snapshots
  - the user explicitly reported that as a regression:
    - logo corrupt again
    - music no longer played the few notes seen in the better baseline

Safe core timing fixes kept during the latest Exolon investigation:
- `Spectrum128Machine`
  - interrupt phase scheduling support remains implemented and test-covered
  - normal 48K snapshot behavior still defaults to immediate interrupt phase
  - `SetInitialInterruptDelay(...)` now genuinely works for harness-only experiments
- `Z80/Z80CoreHelpers.cs`
  - 16-bit word read/write helpers added so low/high byte accesses can be spaced in time while preserving total instruction length
- `Z80/Z80ExtendedOperations.cs`
  - `LD (nn),BC/DE/HL/SP` and `LD BC/DE/HL/SP,(nn)` now use spaced byte accesses instead of back-to-back same-instant accesses
- `Z80/Z80BaseOperations.cs`
  - `DJNZ` now delays before fetching its displacement instead of fetching it immediately
- `Z80/Z80Cpu.cs`
  - interrupt acknowledge now increments refresh register `R`
- These were deliberately kept because they appear hardware-correct and did not regress the user-accepted baseline

Regression coverage added during this pass:
- `Spectrum128kEmulator.Tests/CpuMicroTests.cs`
  - `LdSpPtr_Uses20TStates_And_Separates_Byte_Reads`
  - `LdPtrSp_Uses20TStates_And_Separates_Byte_Writes`
  - `Djnz_Uses13TStates_And_Fetches_Displacement_After_Initial_Delay`
  - `Interrupt_Acknowledge_Increments_R_Register`
- Existing scheduler tests in `MachineCoreTests` still cover:
  - immediate interrupt by default
  - custom initial interrupt delay preserving phase

Validation status for the safe fixes:
- `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter CpuMicroTests`
  - passes with `23` passed
- `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
  - passes with `15` passed / `8` skipped
- A few runs hit transient file-lock/copy warnings in `obj`/`.pdb`, but reruns passed; no known logic-test failures remain from these changes

Current Exolon investigative state:
- The default harness path for `C:\Users\steve\Desktop\Snapshots\exolon.sna` is still bad:
  - by about frame 11 it lands in `FCxx`
  - representative output:
    - `Frame 11: PC=0xFC3D SP=0xFD74`
  - latest default-run artifacts:
    - text dump:
      - `C:\Users\steve\Spectrum128kEmulator\Spectrum128kEmulator.ManualHarness\bin\Debug\net10.0-windows\debug\harness-end-20260429-152713165.txt`
    - image:
      - `C:\Users\steve\Spectrum128kEmulator\Spectrum128kEmulator.ManualHarness\bin\Debug\net10.0-windows\debug\harness-end-20260429-152713165.png`
- The phase sensitivity is still very sharp:
  - `delay=0`
    - bad `FCxx` path
  - `delay=32`
    - reaches coherent live game code around `103E..1056`
    - frame-60 image looks clean
    - artifact image:
      - `C:\Users\steve\Spectrum128kEmulator\Spectrum128kEmulator.ManualHarness\bin\Debug\net10.0-windows\debug\harness-end-20260429-151740492.png`
  - `delay=480`
    - reaches stable `FFxx` menu loop
    - artifact image:
      - `C:\Users\steve\Spectrum128kEmulator\Spectrum128kEmulator.ManualHarness\bin\Debug\net10.0-windows\debug\harness-end-20260429-151740342.png`
- Interpretation:
  - the issue still looks like a first-frame / early-runtime 48K timing problem
  - it is no longer well-modeled as one bad opcode
  - the remaining bug is more likely tied to broader contended-RAM fetch/access timing on the startup path

Most important comparative findings from the latest pass:
- The default `0`-delay path and the healthier delayed paths share a lot of the same early setup:
  - repeated `ST_SP_PTR` / `LD_SP_PTR` around `78DA`
  - repeated execution in the `7A31..7A7D` producer path
- The crucial divergence is still early:
  - default path:
    - first interrupt accepted at `T=0`, returning to `79ED`
    - eventually falls into synthetic-looking `FCxx` worker code
  - `32`-delay path:
    - first interrupt accepted at `T=38`, returning to `79F5`
    - later runs coherent code around `103E..1056`
  - `480`-delay path:
    - flips into the stable `FFxx` menu loop
- The `32`-delay run is now especially valuable as a comparison target because it appears more "alive" than the dead `FCxx` path without collapsing into the over-stable `FFxx` menu branch

Practical conclusion at this checkpoint:
- The recent CPU fixes are worth keeping.
- They did not solve Exolon outright, but they improved hardware fidelity without breaking the current accepted baseline.
- The next pass should compare the first-frame fetch/execution stream for:
  - default `0` delay
  - healthy-looking `32` delay
- Likely target area:
  - operand/opcode fetch timing in contended RAM on the `79ED..7A8D` startup path
  - not another global/default snapshot interrupt-phase change

Separate open issue still pending:
- `Renegade (Hit Squad) 128K.z80` fails to load with:
  - `This .z80 hardware mode is not supported yet (headerLength=31, hardwareMode=4).`
- This has not been fixed yet.
- `Z80SnapshotLoader` currently supports:
  - 48K:
    - header length `23` with hardware modes `0` or `1`
    - header length `54` or `55` with hardware modes `0`, `1`, or `3`
  - 128K:
    - header length `23` with hardware modes `3` or `4`
    - header length `54` or `55` with hardware modes `4`, `5`, or `12`
- The unsupported `31`-byte extended header remains a real loader gap to address later.

Checkpoint after Z80 compliance repair and test cleanup on 2026-04-30:
- User confirmed:
  - `zexall` now passes fully
  - `zexdoc` passes fully
  - manual smoke test harness passes
  - the fixes do not worsen the current Exolon baseline
- Root cause fixed for the `CPI/CPD/LDI/LDD` family:
  - in `Z80/Z80ExtendedOperations.cs`, undocumented flag `F5` for block transfer/compare instructions was taken from the wrong bit
  - corrected behavior:
    - `F3` comes from bit 3 of the documented intermediate value
    - `F5` comes from bit 1 of that same intermediate value
  - this was the concrete cause of the prior `zexall` failures in:
    - `cpd<r>`
    - `cpi<r>`
    - `ldd<r> (2)`
    - `ldi<r> (1)`
    - `ldi<r> (2)`
- The broader experimental Exolon timing changes were rolled back before landing this:
  - no late-read timing change remains for base `(HL)` ALU ops
  - no late-read / read-modify-write timing change remains for indexed `FD/DD CB` ops
  - the kept safe CPU fixes are still:
    - interrupt acknowledge increments `R`
    - `DJNZ` fetch timing cleanup
    - spaced 16-bit `LD rr,(nn)` / `LD (nn),rr` memory accesses

Test-suite cleanup completed:
- Removed the 8 permanently skipped `MachineCoreTests` that only covered the abandoned experimental 48K floating-bus/contention model
- Current automated test status:
  - `dotnet test --no-restore Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj`
    - passes with `78 passed, 0 skipped`
  - `MachineCoreTests`
    - `15 passed, 0 skipped`
  - `CpuMicroTests`
    - `23 passed`

Current stable baseline before resuming Exolon:
- Z80 compliance is back to trusted:
  - user-verified `zexall` pass
  - user-verified `zexdoc` pass
- Unit tests are clean:
  - no skipped tests left in the enabled suite
- Exolon status is unchanged from the accepted baseline:
  - do not treat Exolon as fixed
  - default snapshot path still falls into the bad `FCxx` branch
  - safer CPU fixes and the block-instruction flag fix did not regress the current Exolon state

Recommended next work:
- Resume Exolon from this stronger checkpoint
- Prefer targeted startup-path investigation over broad CPU timing changes
- Keep `zexall`/`zexdoc`/full test suite as the primary anti-regression gate for any further CPU-core changes

Checkpoint after Exolon trace narrowing on 2026-04-30:
- User-confirmed current live-app Exolon state:
  - logo now looks good
  - sound is still wrong
  - sprites are still corrupted
- Important safety note:
  - do not describe Exolon as fixed
  - the current state is visually improved at the title/logo level, but still has gameplay/audio corruption

What was tried and what was kept:
- A wider CPU timing experiment was attempted for simple 7T memory-access instructions:
  - `LD A,(DE)` / `LD A,(BC)`
  - `LD (DE),A` / `LD (BC),A`
  - `LD (HL),r`
  - ALU `(HL)` forms like `AND (HL)`
- That experiment moved Exolon into a different failure mode and made the `32`-T-state comparison path worse.
- Those risky timing edits were fully reverted.
- The only retained changes from this pass are debug-only tracing improvements:
  - dormant focused instruction tracing in `Spectrum128Machine`
  - harness CLI support for:
    - `tracepc=`
    - `traceframes=`
    - `tracefromframe=`
    - `tracemax=`
- These tracing changes do not alter normal emulator behavior.

Current validated baseline after reverting the risky timing edits:
- `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter CpuMicroTests`
  - passes
- `dotnet test Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
  - passes
- `dotnet build Spectrum128kEmulator.ManualHarness\Spectrum128kEmulator.ManualHarness.csproj`
  - passes
- Harness baseline is restored:
  - `delay=0` path:
    - frame 11 reaches `PC=FC3D`
  - `delay=32` path:
    - frame 11 reaches `PC=1040`

Most useful new Exolon findings from the focused trace work:
- The first `79ED..7A2D` startup worker is not where the lasting divergence happens.
  - `delay=0` and `delay=32` are effectively identical there apart from the known first-interrupt placement around `79F5`.
- The real split is already established much earlier than the later `FCxx` symptom suggests.
- By frame 2:
  - both runs are still in the same general `79C2..79C8` worker loop and ROM interrupt service path
  - but the interrupt is landing at slightly different instruction boundaries inside that loop
- By frame 8:
  - the two runs are already in very different machine states before the later ROM/helper fallout:
    - bad/default path:
      - `PC=B7C6`
      - `SP=FFDE`
      - `IY=BDDD`
    - healthier `32`-delay path:
      - `PC=1051`
      - `SP=804C`
      - `IY=B211`
- By frame 9:
  - bad/default path:
    - returns through `004D..0052`
    - re-enters game code at `7A76`
    - then reaches `7AB6`
    - returns into `FC7F`
  - healthier `32`-delay path:
    - returns through `004D..0052`
    - re-enters the coherent `103E..1056` loop
- Practical conclusion:
  - the later `3870` / `3874` ROM helper branch is probably a symptom, not the root cause
  - by the time both runs reach that area in the later frames, stack and `IY` state are already different

Best current hypothesis:
- The remaining Exolon bug is a control-flow/state divergence that is already established by frames 2 through 8.
- The highest-value target is now:
  - the first frame where stack and `IY` stop matching between the default and `32`-delay runs
  - not another broad timing tweak across unrelated instructions

Exact next steps to resume later:
1. Use the focused trace harness on the restored baseline to pinpoint the earliest frame where:
   - `SP`
   - `IY`
   - or the return chain after `0038 -> 386E/3870 -> 004D..0052`
   first diverges between `delay=0` and `delay=32`
2. Once that first divergence frame is isolated, compare:
   - the exact interrupt entry point
   - the stack contents written during the ROM service
   - the return address chain
   - any `IX`/`IY` saves/restores around that boundary
3. Only after identifying a concrete mismatch, consider a surgical CPU fix.
   - Avoid broad per-instruction timing changes unless they are strongly supported by the trace.
4. Keep anti-regression gates in place for every core change:
   - `zexall`
   - `zexdoc`
   - full enabled unit suite
   - Exolon harness baseline checks for both `delay=0` and `delay=32`

Checkpoint after deeper frame-6 analysis on 2026-04-30:
- Latest user-confirmed live-app Exolon behavior:
  - logo still looks good
  - sound is still wrong
  - sprites are still corrupted
  - on a first run, a few frames of animation play before the crash
- Important interpretation:
  - this is not an instant frame-0 failure
  - the default path survives long enough to build some valid state and animate briefly before tipping over
  - any future fix must preserve the current improved title/logo and early animation while removing the later crash/corruption

What was added in this pass:
- One debug-only dump enhancement in `Spectrum128Machine`:
  - `BuildDebugDump()` now includes a 32-byte memory window around `IY-8`
- This does not alter normal emulation behavior

Most useful new low-level findings:
- The ROM helper at `386E` was decoded directly from `128-1.rom`:
  - `386E: DD E5`
  - `3870: FD CB 01 66`
  - `3874: 28 03`
- So the branch at `3874` is:
  - `BIT 4,(IY+1)`
  - `JR Z,+3`
- The branch difference between the bad/default and healthier `delay=32` paths is therefore real data, not a fake flag bug:
  - bad/default frame-6 path:
    - `IY=B301`
    - `B302=78`
    - bit 4 is set
    - `JR Z` is not taken
  - healthier `delay=32` frame-6 path:
    - `IY=B301`
    - `B302=0D`
    - bit 4 is clear
    - `JR Z` is taken

Where that byte comes from:
- The startup/data-driven worker around `7981..7992` was decoded from the snapshot:
  - `7981: ADD HL,DE`
  - `7982: LD E,(HL)`
  - `7984: LD D,(HL)`
  - `7985: LD IX,0000`
  - `7989: ADD IX,DE`
  - `798B: LD HL,78DC`
  - `798E: LD B,10`
  - `7990: POP DE`
  - `7991: XOR A`
  - `7992: JP (IX)`
  - `79C2: LD (HL),E / INC HL / LD (HL),D / INC HL / LD (HL),A ...`
- That means the `B301/B302` bytes later tested by the ROM helper are staged earlier by the `79C2` writer loop, which itself is driven by:
  - the stack word consumed by `POP DE` at `7990`
  - the jump target selected by the `DE` fetched from the `7AD9` table at `7981..7984`

Earliest concrete fork now identified:
- The tight pre-interrupt window is:
  - `7976: LD SP,HL`
  - `7977: ADD A,A`
  - `7978: AND 07`
  - `797A: ADD A,A`
  - `797B: LD L,A`
  - `797C: LD H,00`
  - `797E: LD DE,7AD9`
  - `7981: ADD HL,DE`
- In the healthier comparison path, the interrupt is accepted at `PC=7981` and returns to `7981`.
- In the bad/default path, the interrupt is accepted earlier around `PC=7976`.
- That earlier interrupt entry is enough to change the later stack payload consumed by `POP DE` at `7990`.

Concrete proof from the focused trace:
- At frame 6, the default/bad path later reaches:
  - `7990: POP DE -> DE=78E4`
- At frame 6, the healthier `delay=32` path later reaches:
  - `7990: POP DE -> DE=0D78`
- Those different `DE` values are then written through the `79C2` loop into the `78DC..78DF` staging area.
- The staged bytes are later copied into the `B301/B302` area, causing the `BIT 4,(IY+1)` difference in ROM.

Best current hypothesis:
- The remaining Exolon bug is now most likely an interrupt scheduling / interrupt boundary accuracy issue around frame 6, not generic random corruption and not a simple flag bug.
- The highest-value target is:
  - why the default path accepts the interrupt one instruction earlier than the healthier path
  - whether the emulator is presenting the frame interrupt edge slightly too early on that path

One key explanatory insight from the latest pass:
- The bad/default and healthier paths are not diverging because `79C2` writes differently by itself.
- They diverge because the interrupt is landing on opposite sides of `7976: LD SP,HL`.
  - bad/default path:
    - interrupt is accepted while `PC=7976`
    - `SP` is still the old value (`FFDE`)
    - the ROM interrupt stack frame is pushed to the old stack
    - after returning, `LD SP,HL` then switches `SP` to `FAA0`
  - healthier comparison path:
    - `LD SP,HL` executes first
    - `SP` becomes `FAA0`
    - interrupt is accepted later around `PC=7981`
    - the ROM interrupt stack frame is pushed to the new stack
- That directly explains the later `POP DE` difference at `7990`:
  - bad/default path reads stack words from the old-stack layout
  - healthier path reads stack words from the new-stack layout
- So the wrong `DE` at `7990` is a downstream consequence of interrupt placement relative to `LD SP,HL`, not a separate stack instruction bug.

Exact next steps from here:
1. Inspect the frame interrupt scheduling path in `Spectrum128Machine.ExecuteFrame()` and `Z80Cpu.ExecuteCycles()` with frame-6 behavior in mind.
2. Determine whether the emulator should be allowing the interrupt to be seen at a slightly later instruction boundary in this case.
3. Compare the current interrupt delivery model against the observed good comparison path without changing the default snapshot phase globally.
4. If a change is attempted, keep it surgical and re-run:
   - `zexall`
   - `zexdoc`
   - full enabled unit suite
   - Exolon harness checks for `delay=0` and `delay=32`
   - live Exolon smoke test to confirm logo + early animation are preserved

Checkpoint after plain-loader phase fix on 2026-04-30:
- Meaningful progression achieved in the normal 48K snapshot load path.
- The key fix was not a CPU-core timing rewrite; it was snapshot-load phase plumbing:
  - `Spectrum128Machine.Default48kSnapshotInitialInterruptDelay` is now `39`
  - `SnapshotLoader.LoadSna48k(...)` reapplies that delay at the end of load
  - `Z80SnapshotLoader.Load(...)` now does the same for 48K `.z80` loads
- This matters because `ConfigureFor48kSnapshot(...)` alone was not enough to move the plain loader path; the effective delay was being lost somewhere later in the load sequence.

What the plain Exolon harness does now:
- Default load of `exolon.sna` no longer goes straight to the old early `FCxx` trap.
- It now follows the healthier comparison branch automatically:
  - frame 11: `PC=103E SP=804C IFF1=1`
  - frame 31: `PC=1051 SP=804C IFF1=1`
  - frame 61: `PC=103F SP=804C IFF1=1`
- The next failure is later:
  - around frame 71 it reaches `PC=11DC SP=8054 IFF1=0 IFF2=0`
  - later it recovers to a different loop around `15F7/15FF`
- This is a real step forward because the default app/harness path is now on the later, longer-lived Exolon route rather than the original early-corruption path.

Validation completed:
- `dotnet test --no-restore Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter SnapshotLoaderTests`
  - passed
- `dotnet test --no-restore Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter MachineCoreTests`
  - passed
- `dotnet test --no-restore Spectrum128kEmulator.Tests\Spectrum128kEmulator.Tests.csproj --filter CpuMicroTests`
  - passed
- default harness run:
  - `dotnet run --no-build --project Spectrum128kEmulator.ManualHarness -- 'C:\Users\steve\Desktop\Snapshots\exolon.sna'`
  - now lands on the later `10xx -> 11DC` path automatically

Most important new diagnosis from the later path:
- The later Exolon failure is now clearly tied to a ROM/parser escape rather than the old `FCxx` worker path.
- On the healthier/default-39 path:
  - interrupts are accepted in the `10xx` loop for many frames
  - the run later escapes through ROM service and eventually reaches:
    - `PC=0018`
    - reads through `HL=FFFF`
    - then `PC=FFFF -> 0000`
    - then `DI`
    - leaving `IFF1=0 IFF2=0`
- So the next best target is no longer the frame-6 `LD SP,HL` fork alone.
- The current highest-value next investigation is:
  - why the later healthy path reaches the ROM/parser route at `0018`
  - why it is still using a bad `HL/CH_ADD`-style source that leads to `FFFF`
  - and whether that is due to wrong system-variable state, wrong staged menu data, or a bad return target just before the ROM call

Recommended next work:
1. Keep the new loader-phase fix in place; it is the first normal-load change that produced meaningful Exolon progress.
2. Trace the later failure window on the healthy path:
   - ROM `0038..0052`
   - `02C2..02DB`
   - the `0018` path
   - the transition to `FFFF -> 0000 -> DI`
3. Compare system-variable state just before the ROM/parser escape, especially any `CH_ADD`-style pointer state and surrounding ROM workspace.
4. Do not reintroduce the reverted scheduler experiment that subtracted actual elapsed T-states; that caused a worse `B6xx/IFF1=0` regression.

Tape-path breakthrough on 2026-04-30:
- Added a new hybrid `.tap` bootstrap path for investigation:
  - `Spectrum128kEmulator.ManualHarness/Program.cs` now supports `.tap`
  - `Tape/TapLoader.cs` now has `BootstrapBasicProgramAndMountRemaining(...)`
  - `MountedTape` now supports starting at a non-zero initial block index
- New regression coverage in `Spectrum128kEmulator.Tests/TapLoaderTests.cs`:
  - leading BASIC + CODE bootstrap works
  - mounted tape resumes after consumed bootstrap blocks
  - non-BASIC leader still rejects cleanly
- Validation passed after this work:
  - `dotnet test --no-restore ... --filter TapLoaderTests`
  - `dotnet test --no-restore ... --filter CpuMicroTests`
  - `dotnet test --no-restore ... --filter MachineCoreTests`

What `EXOLON.TAP` actually contains:
- The tape is not purely “weird custom blocks”; it has a standard front end:
  - blocks `0/1`: `BASIC 'exolon'`, length `457`
  - blocks `2/3`: `CODE 'exolon'`, length `768`, load address `0xFC00`
  - blocks `4/5`: `TYPE42 'exolon'`, length `4096`, params `0x8000/0x4000`
  - blocks `6/7`: `TYPE42 'exolon'`, length `37536`, params `26000/26500`
- So the most useful clean repro path is now:
  1. bootstrap/load the leading BASIC + CODE blocks
  2. mount the remaining tape starting at block `4`
  3. investigate the custom-loader phase from there

Most important new Exolon diagnosis from the real tape path:
- Forcing execution to the loaded code block at `PC=0xFC00` is now possible in the harness:
  - `dotnet run --no-build --project Spectrum128kEmulator.ManualHarness -- 'C:\Users\steve\Desktop\Snapshots\EXOLON.TAP' 39 500 reg=0:PC:FC00`
- This is important because it gives a second, cleaner Exolon repro independent of the suspect snapshot state.
- That real tape/code path shows:
  - one undocumented ED-prefixed opcode hit:
    - `UNIMPL ED 0xDD at PC=0xFD1C`
  - then a long live loader phase around `11DE/11E9` with `IFF1=0`
  - unlike the old snapshot interpretation, this `11DC..11E9` region is not instantly terminal on the tape path
  - by about frame `91` the tape path recovers with interrupts restored and reaches later ROM/live code again
- So the `11DC..11E9` loop can be a legitimate long-running loader/memory routine, not automatically the crash by itself.

Useful tape-path observations:
- The code path around `11EA/11ED` repeatedly writes `1` then `0` across large RAM windows (`AAxx`, `BAxx`, `C5DB..`, `FED8..`), consistent with a loader/clear/setup phase rather than random corruption.
- After that phase, the run settles into an input-sensitive live loop around `15F7/15FF`.
- Injected keys (`space`, `enter`, `fire`) perturb that loop temporarily:
  - `space` produced a temporary `15E8/10AC` variation
  - `enter` briefly hit `0E5C`
  - `fire` briefly hit `15ED`
- That strongly suggests the tape/code path is waiting on some ROM/input/tape-start condition rather than simply dead-crashing at that stage.

Why this matters for the remaining Exolon bug:
- The real tape/code repro independently reaches the same broad “later” region we were already chasing from the improved snapshot path.
- That is strong evidence the remaining Exolon issue is a genuine emulator-core compatibility problem, not just poisoned snapshot state.
- The next best target is now the custom loader path after the standard CODE bootstrap, not the old early `FCxx` snapshot trap.

Recommended next work from this new checkpoint:
1. Keep the `.tap` hybrid bootstrap support and use it as a second Exolon repro path beside `exolon.sna`.
2. Investigate the `0xFC00`-started code path around:
   - `0xFD1C` (`ED DD`)
   - the long `11DE..11E9` loader phase
   - the later input-sensitive `15F7/15FF` loop
3. Determine what condition the `15F7/15FF` loop is waiting for:
   - key state
   - tape ROM state
   - custom EAR/tape-read condition
   - some system variable that the snapshot path previously bypassed
4. If a CPU-core change is attempted, verify against both:
   - improved snapshot path (`exolon.sna`, delay `39`)
   - real tape/code path (`EXOLON.TAP`, `PC=0xFC00`)
