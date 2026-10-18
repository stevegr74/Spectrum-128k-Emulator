namespace Spectrum128kEmulator
{
    internal sealed class UlaTimingProfile
    {
        public static readonly UlaTimingProfile Spectrum48K = new UlaTimingProfile(
            SpectrumMachineModel.Spectrum48K,
            frameTStates: Spectrum128Machine.FrameTStates48,
            cpuClockHz: Spectrum128Machine.CpuClockHz48,
            lineTStates: 224,
            contentionStartTState: 14335,
            displayFetchStartTState: 14338,
            visibleDisplayStartTState: 14347);

        public static readonly UlaTimingProfile Spectrum128K = new UlaTimingProfile(
            SpectrumMachineModel.Spectrum128K,
            frameTStates: Spectrum128Machine.FrameTStates128,
            cpuClockHz: Spectrum128Machine.CpuClockHz128,
            lineTStates: 228,
            contentionStartTState: 14361,
            displayFetchStartTState: 14364,
            visibleDisplayStartTState: 14361);

        private UlaTimingProfile(
            SpectrumMachineModel model,
            int frameTStates,
            int cpuClockHz,
            int lineTStates,
            int contentionStartTState,
            int displayFetchStartTState,
            int visibleDisplayStartTState)
        {
            Model = model;
            FrameTStates = frameTStates;
            CpuClockHz = cpuClockHz;
            LineTStates = lineTStates;
            ContentionStartTState = contentionStartTState;
            DisplayFetchStartTState = displayFetchStartTState;
            VisibleDisplayStartTState = visibleDisplayStartTState;
        }

        public SpectrumMachineModel Model { get; }
        public int FrameTStates { get; }
        public int CpuClockHz { get; }
        public int LineTStates { get; }
        public int ContentionStartTState { get; }
        public int DisplayFetchStartTState { get; }
        public int VisibleDisplayStartTState { get; }
        public int DisplayLineCount => Spectrum128Machine.ActiveDisplayHeight;
        public int DisplayFetchTStatesPerLine => Spectrum128Machine.ActiveDisplayWidth / 2;

        public static UlaTimingProfile ForModel(SpectrumMachineModel model) =>
            model == SpectrumMachineModel.Spectrum48K ? Spectrum48K : Spectrum128K;

        public static UlaTimingProfile ForFrameLength(int frameTStates) =>
            frameTStates == Spectrum48K.FrameTStates ? Spectrum48K : Spectrum128K;
    }
}
