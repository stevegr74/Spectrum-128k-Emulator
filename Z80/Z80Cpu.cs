using System;
using System.Collections.Generic;
using System.Text;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        public Z80Registers Regs { get; } = new Z80Registers();
        public ulong TStates { get; private set; } = 0;

        public Func<ushort, byte> ReadMemory { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WriteMemory { get; set; } = (_, _) => { };
        public Func<ushort, byte> ReadPort { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };
        public Action<string>? Trace { get; set; }
        public Func<Z80Cpu, bool>? BeforeInstruction { get; set; }

        private bool halted = false;
        public bool IsHalted => halted;
        public bool InterruptPending { get; set; } = false;
        public bool IFF1 { get; private set; } = false;
        public bool IFF2 { get; private set; } = false;
        public int InterruptMode => interruptMode;

        private int eiDelay = 0;
        private int interruptMode = 1;
        private byte qFlags = 0;
        private readonly Action[] opcodeTable = new Action[256];
        private readonly Action[] cbOpcodeTable = new Action[256];
        private readonly Action[] edOpcodeTable = new Action[256];
        private readonly Action[] ddOpcodeTable = new Action[256];
        private readonly Action[] fdOpcodeTable = new Action[256];

        private readonly Queue<string> recentTrace = new Queue<string>();
        private const int RecentTraceCapacity = 512;
        private bool reportedHighRamEntry = false;
        private bool flagsChangedLastInstruction = false;
        private byte lastFlagsBeforeInstruction = 0;

        private byte IXH
        {
            get => (byte)(Regs.IX >> 8);
            set => Regs.IX = (ushort)((value << 8) | (Regs.IX & 0x00FF));
        }

        private byte IXL
        {
            get => (byte)(Regs.IX & 0x00FF);
            set => Regs.IX = (ushort)((Regs.IX & 0xFF00) | value);
        }

        private byte IYH
        {
            get => (byte)(Regs.IY >> 8);
            set => Regs.IY = (ushort)((value << 8) | (Regs.IY & 0x00FF));
        }

        private byte IYL
        {
            get => (byte)(Regs.IY & 0x00FF);
            set => Regs.IY = (ushort)((Regs.IY & 0xFF00) | value);
        }

        public Z80Cpu()
        {
            InitializeOpcodeTable();
            InitializeCBTable();
            InitializeEDTable();
            InitializeDDTable();
            InitializeFDTable();
        }

        private void WritePortTimed(ushort port, byte value, int instructionTStates)
        {
            TStates += (ulong)instructionTStates;
            WritePort(port, value);
        }

        // =========================================================
        // Public control
        // =========================================================

        public void Reset()
        {
            Regs.PC = 0;
            Regs.SP = 0xFFFF;
            Regs.I = 0;
            Regs.R = 0;

            halted = false;
            InterruptPending = false;
            IFF1 = false;
            IFF2 = false;

            eiDelay = 0;
            interruptMode = 1;

            reportedHighRamEntry = false;
            recentTrace.Clear();
            TStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
        }

        public void ExecuteCycles(ulong cycles)
        {
            ulong target = TStates + cycles;

            while (TStates < target)
            {
                if (BeforeInstruction != null && BeforeInstruction(this))
                    continue;

                if (InterruptPending && IFF1)
                {
                    if (Regs.SP < 0x4000)
                    {
                        Trace?.Invoke($"INT with BAD SP: PC={Regs.PC:X4} SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
                    }

                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;
                    IFF2 = false;

                    Push(Regs.PC);

                    switch (interruptMode)
                    {
                        case 0:
                        case 1:
                            Regs.PC = 0x0038;
                            break;

                        case 2:
                            ushort vector = (ushort)((Regs.I << 8) | 0xFF);
                            byte low = ReadMemory(vector);
                            byte high = ReadMemory((ushort)(vector + 1));
                            Regs.PC = (ushort)(low | (high << 8));
                            break;
                    }

                    TStates += 13;
                    continue;
                }

                if (halted)
                {
                    TStates += 4;
                    continue;
                }

                Step();
            }
        }

        public void Step()
        {
            ushort pcBefore = Regs.PC;
            ushort spBefore = Regs.SP;
            ushort ixBefore = Regs.IX;
            ushort iyBefore = Regs.IY;
            byte fBefore = Regs.F;

            byte op = FetchByte();

            RecordTrace(pcBefore, op);

            if (!reportedHighRamEntry && pcBefore >= 0xC000)
            {
                reportedHighRamEntry = true;
                Trace?.Invoke("=== ENTERED HIGH RAM ===");
                foreach (var line in recentTrace)
                    Trace?.Invoke(line);
            }

            if (op == 0xCB)
            {
                byte cbOp = FetchByte();
                cbOpcodeTable[cbOp]();
            }
            else if (op == 0xED)
            {
                byte edOp = FetchByte();
                edOpcodeTable[edOp]();
            }
            else if (op == 0xDD)
            {
                byte ddOp = FetchByte();
                if (ddOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IX, disp, cbOp);
                }
                else
                {
                    ddOpcodeTable[ddOp]();
                }
            }
            else if (op == 0xFD)
            {
                byte fdOp = FetchByte();
                if (fdOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IY, disp, cbOp);
                }
                else
                {
                    fdOpcodeTable[fdOp]();
                }
            }
            else
            {
                opcodeTable[op]();
            }

            if (spBefore != Regs.SP || ixBefore != Regs.IX || iyBefore != Regs.IY)
            {
                Trace?.Invoke(
                    $"STATE PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} IX {ixBefore:X4}->{Regs.IX:X4} IY {iyBefore:X4}->{Regs.IY:X4}");
            }

            if (Regs.SP < 0x4000)
            {
                Trace?.Invoke(
                    $"BAD SP after PC={pcBefore:X4} OP={op:X2}: SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
            }

            if (eiDelay > 0)
            {
                eiDelay--;
                if (eiDelay == 0)
                {
                    IFF1 = true;
                    IFF2 = true;
                }
            }

            lastFlagsBeforeInstruction = fBefore;
            flagsChangedLastInstruction = Regs.F != fBefore;
            qFlags = (Regs.F != fBefore) ? Regs.F : (byte)0;
        }

        public string[] GetRecentTraceSnapshot() => recentTrace.ToArray();

        public void ClearRecentTrace()
        {
            recentTrace.Clear();
        }

        public void RestoreInterruptState(bool iff1, bool iff2, int interruptMode)
        {
            IFF1 = iff1;
            IFF2 = iff2;
            this.interruptMode = interruptMode & 0x03;
            eiDelay = 0;
        }

        public void ClearSnapshotExecutionState()
        {
            halted = false;
            InterruptPending = false;
            TStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;

        }

        public void AdvanceTStates(uint tStates)
        {
            TStates += tStates;
        }
    }
}
