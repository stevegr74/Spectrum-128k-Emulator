using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Tap
{
    internal static class TapeLoadPlanner
    {
        public static TapeLoadPlan Create(Spectrum128Machine machine, IReadOnlyList<TapeBlock> blocks)
        {
            ArgumentNullException.ThrowIfNull(machine);

            if (!machine.SupportsRomTapeAcceleration)
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.MountedRealtime,
                    $"ROM profile '{machine.RomProfile.Name}' is not compatible with address-based tape acceleration.");
            }

            if (TapLoader.CanLoadAllStandardTapeBlocks(blocks))
            {
                bool use128kMode = TapLoader.Use128kTapeLoadMode(machine);
                if (TapLoader.RequiresRomDrivenBootstrapForStandardTape(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Standard tape chains from an autorun BASIC stage into a second BASIC loader and should resume under ROM control.");
                }

                if (TapLoader.RequiresMountedLoadSemanticsForStandardTape(machine, blocks, use128kMode))
                {
                    if (!TapLoader.CanExecuteLeadingBasicProgramWithBootstrap(machine, blocks, use128kMode))
                    {
                        return new TapeLoadPlan(
                            TapeLoadStrategy.RomBootstrapMounted,
                            "Standard tape requires mounted LOAD semantics, but its autorun BASIC control flow must execute under ROM control.");
                    }

                    return new TapeLoadPlan(
                        TapeLoadStrategy.BootstrapHybrid,
                        "Standard tape contains a protected or chained loader stage that requires mounted LOAD semantics.");
                }

                return new TapeLoadPlan(
                    TapeLoadStrategy.FullFakeLoad,
                    "All blocks are standard header/data pairs and can be fully fake-loaded.");
            }

            if (TapLoader.HasTwoStageBasicPrefixBeforeElectricallyDecodedProtectedStream(blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.RomBootstrapMounted,
                    "Tape has a two-stage BASIC prefix whose protected continuation depends on real ROM loader state.");
            }

            if (TapLoader.RequiresLeadingStandardBasicChainFakeLoad(machine, blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.LeadingStandardChainFakeLoad,
                    "Tape begins with a chain of standard BASIC stages before a protected remainder and can fake-load that safe prefix.");
            }

            if (TapLoader.RequiresRomDrivenBootstrapForMixedTape(machine, blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.RomBootstrapMounted,
                    "Tape begins with an autorun BASIC stage that chains into a mounted BASIC loader before protected continuation.");
            }

            if (TapLoader.RequiresMountedRealtimeForMixedTape(machine, blocks))
            {
                bool use128kMode = TapLoader.Use128kTapeLoadMode(machine);
                if (TapLoader.FindLeadingStandardBasicChainEndIndex(blocks) == 0 &&
                    !TapLoader.CanSatisfyLeadingBasicLoadRequestsBeforeProtectedRemainder(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Tape loader requests more standard LOAD stages than the safe bootstrap can satisfy before protected playback, so autorun must remain under ROM control.");
                }

                if (TapLoader.HasStandardHeaderDataPairAfterLeadingBasic(blocks) &&
                    !TapLoader.CanResolveMountedLoadUsrContinuation(machine, blocks))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Tape requires mounted continuation that cannot be represented safely by the bootstrap executor, so autorun must remain under ROM control.");
                }

                return new TapeLoadPlan(
                    TapeLoadStrategy.BootstrapHybrid,
                    "Tape begins with a BASIC stage followed by raw standard byte streams and should enter the mounted remainder through the bootstrap loader path.");
            }

            if (TapLoader.CanBootstrapBasicProgramAndMountRemaining(blocks))
            {
                bool use128kMode = TapLoader.Use128kTapeLoadMode(machine);
                if (TapLoader.StartsProtectedRemainderImmediatelyAfterLeadingBasic(blocks) &&
                    !TapLoader.CanExecuteLeadingBasicProgramWithBootstrap(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Tape begins with a protected BASIC loader whose interpreter handoff must execute under ROM control.");
                }

                if (TapLoader.FindLeadingStandardBasicChainEndIndex(blocks) == 0 &&
                    !TapLoader.CanSatisfyLeadingBasicLoadRequestsBeforeProtectedRemainder(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Tape loader cannot complete its LOAD sequence through the safe bootstrap path, so autorun must remain under ROM control.");
                }

                return new TapeLoadPlan(
                    TapeLoadStrategy.BootstrapHybrid,
                    "Tape begins with a standard BASIC loader and requires mounted continuation.");
            }

            return new TapeLoadPlan(
                TapeLoadStrategy.MountedRealtime,
                "Tape does not have a safe fake-load bootstrap path.");
        }
    }
}
