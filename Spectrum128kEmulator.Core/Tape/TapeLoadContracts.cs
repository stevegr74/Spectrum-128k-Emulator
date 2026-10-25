using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    public sealed class TapLoadResult
    {
        public TapLoadResult(int totalBlockCount, int loadedBlockCount, string? autoStartFileName)
        {
            TotalBlockCount = totalBlockCount;
            LoadedBlockCount = loadedBlockCount;
            AutoStartFileName = autoStartFileName;
        }

        public int TotalBlockCount { get; }
        public int LoadedBlockCount { get; }
        public string? AutoStartFileName { get; }
    }

    public sealed class TapMountResult
    {
        public TapMountResult(int totalBlockCount, string displayName)
        {
            TotalBlockCount = totalBlockCount;
            DisplayName = displayName;
        }

        public int TotalBlockCount { get; }
        public string DisplayName { get; }
    }

    public sealed class TapBootstrapResult
    {
        public TapBootstrapResult(int totalBlockCount, int consumedBlockCount, string displayName, string? autoStartFileName)
        {
            TotalBlockCount = totalBlockCount;
            ConsumedBlockCount = consumedBlockCount;
            DisplayName = displayName;
            AutoStartFileName = autoStartFileName;
        }

        public int TotalBlockCount { get; }
        public int ConsumedBlockCount { get; }
        public string DisplayName { get; }
        public string? AutoStartFileName { get; }
    }

    public enum TapeLoadStrategy
    {
        FullFakeLoad,
        LeadingStandardChainFakeLoad,
        RomBootstrapMounted,
        BootstrapHybrid,
        MountedRealtime
    }

    public enum TapeLoadPlanReason
    {
        Unspecified,
        StandardTapeFullyAccelerated,
        IncompatibleRomProfile,
        StandardBasicChainRequiresRom,
        UnsupportedStandardBootstrapControlFlow,
        StandardTapeRequiresMountedSemantics,
        ProtectedContinuationRequiresRom,
        SafeLeadingBasicChain,
        MixedBasicChainRequiresRom,
        UnsatisfiedLeadingLoadRequests,
        UnrepresentableMountedContinuation,
        MixedTapeBootstrap,
        ProtectedBasicRequiresRom,
        BasicBootstrapMounted,
        NoSafeAccelerationPath
    }

    public sealed class TapeLoadPlan
    {
        public TapeLoadPlan(TapeLoadStrategy strategy, string reason)
            : this(strategy, TapeLoadPlanReason.Unspecified, reason)
        {
        }

        public TapeLoadPlan(TapeLoadStrategy strategy, TapeLoadPlanReason reasonCode, string reason)
        {
            Strategy = strategy;
            ReasonCode = reasonCode;
            Reason = reason;
        }

        public TapeLoadStrategy Strategy { get; }
        public TapeLoadPlanReason ReasonCode { get; }
        public string Reason { get; }
    }

    public enum TapeTransportState
    {
        NoTape,
        Playing,
        Stopped,
        Ended
    }

    public enum TapeTransportTransition
    {
        None,
        Reset,
        Mounted,
        ManualStop,
        ManualResume,
        AutomaticHandoffStop,
        AutomaticRomResume,
        StopMarker,
        Ended,
        Ejected
    }

    public sealed class TapeExecutionResult
    {
        public TapeExecutionResult(
            TapeLoadStrategy strategy,
            TapeLoadPlanReason reasonCode,
            string reason,
            int totalBlockCount,
            int consumedBlockCount,
            string displayName,
            string? autoStartFileName)
        {
            Strategy = strategy;
            ReasonCode = reasonCode;
            Reason = reason;
            TotalBlockCount = totalBlockCount;
            ConsumedBlockCount = consumedBlockCount;
            DisplayName = displayName;
            AutoStartFileName = autoStartFileName;
        }

        public TapeLoadStrategy Strategy { get; }
        public TapeLoadPlanReason ReasonCode { get; }
        public string Reason { get; }
        public int TotalBlockCount { get; }
        public int ConsumedBlockCount { get; }
        public string DisplayName { get; }
        public string? AutoStartFileName { get; }
    }

    public sealed class BootstrapTapeLoadResult
    {
        public static readonly BootstrapTapeLoadResult None = new(false, false, 0, 0, 0, 0xFFFF);

        public BootstrapTapeLoadResult(
            bool success,
            bool loadedBasicProgram,
            byte loadedHeaderType,
            ushort loadedProgramLength,
            ushort loadedDataLength,
            ushort loadedAutoStartLine)
        {
            Success = success;
            LoadedBasicProgram = loadedBasicProgram;
            LoadedHeaderType = loadedHeaderType;
            LoadedProgramLength = loadedProgramLength;
            LoadedDataLength = loadedDataLength;
            LoadedAutoStartLine = loadedAutoStartLine;
        }

        public bool Success { get; }
        public bool LoadedBasicProgram { get; }
        public byte LoadedHeaderType { get; }
        public ushort LoadedProgramLength { get; }
        public ushort LoadedDataLength { get; }
        public ushort LoadedAutoStartLine { get; }
    }
}
