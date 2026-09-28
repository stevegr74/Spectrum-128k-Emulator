namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        public sealed class QuickState
        {
            internal ushort AF;
            internal ushort BC;
            internal ushort DE;
            internal ushort HL;
            internal byte A_;
            internal byte F_;
            internal byte B_;
            internal byte C_;
            internal byte D_;
            internal byte E_;
            internal byte H_;
            internal byte L_;
            internal ushort IX;
            internal ushort IY;
            internal ushort SP;
            internal ushort PC;
            internal byte I;
            internal byte R;
            internal ulong TStates;
            internal ulong InstructionFetchCount;
            internal bool Halted;
            internal bool InterruptPending;
            internal bool IFF1;
            internal bool IFF2;
            internal int InterruptMode;
            internal int EiDelay;
            internal byte QFlags;
            internal ulong LastInterruptProgressTStates;
            internal bool FlagsChangedLastInstruction;
            internal byte LastFlagsBeforeInstruction;
        }

        public QuickState CaptureQuickState()
        {
            return new QuickState
            {
                AF = Regs.AF,
                BC = Regs.BC,
                DE = Regs.DE,
                HL = Regs.HL,
                A_ = Regs.A_,
                F_ = Regs.F_,
                B_ = Regs.B_,
                C_ = Regs.C_,
                D_ = Regs.D_,
                E_ = Regs.E_,
                H_ = Regs.H_,
                L_ = Regs.L_,
                IX = Regs.IX,
                IY = Regs.IY,
                SP = Regs.SP,
                PC = Regs.PC,
                I = Regs.I,
                R = Regs.R,
                TStates = TStates,
                InstructionFetchCount = InstructionFetchCount,
                Halted = halted,
                InterruptPending = interruptPending,
                IFF1 = IFF1,
                IFF2 = IFF2,
                InterruptMode = interruptMode,
                EiDelay = eiDelay,
                QFlags = qFlags,
                LastInterruptProgressTStates = LastInterruptProgressTStates,
                FlagsChangedLastInstruction = flagsChangedLastInstruction,
                LastFlagsBeforeInstruction = lastFlagsBeforeInstruction
            };
        }

        public void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            Regs.AF = state.AF;
            Regs.BC = state.BC;
            Regs.DE = state.DE;
            Regs.HL = state.HL;
            Regs.A_ = state.A_;
            Regs.F_ = state.F_;
            Regs.B_ = state.B_;
            Regs.C_ = state.C_;
            Regs.D_ = state.D_;
            Regs.E_ = state.E_;
            Regs.H_ = state.H_;
            Regs.L_ = state.L_;
            Regs.IX = state.IX;
            Regs.IY = state.IY;
            Regs.SP = state.SP;
            Regs.PC = state.PC;
            Regs.I = state.I;
            Regs.R = state.R;
            TStates = state.TStates;
            InstructionFetchCount = state.InstructionFetchCount;
            halted = state.Halted;
            interruptPending = state.InterruptPending;
            IFF1 = state.IFF1;
            IFF2 = state.IFF2;
            interruptMode = state.InterruptMode;
            eiDelay = state.EiDelay;
            qFlags = state.QFlags;
            LastInterruptProgressTStates = state.LastInterruptProgressTStates;
            flagsChangedLastInstruction = state.FlagsChangedLastInstruction;
            lastFlagsBeforeInstruction = state.LastFlagsBeforeInstruction;
            ExecutionStopped = false;

            reportedHighRamEntry = false;
            reportedDiWindowEntry = false;
            reportedLowStackEntry = false;
            reported17xxStackEntry = false;
            reportedRomStackWindowEntry = false;
            recentTrace.Clear();
            recentInterruptEvents.Clear();
        }
    }
}
