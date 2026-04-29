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
        public Func<ushort, int, byte>? ReadPortTimed { get; set; }
        public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };
        public Action<string>? Trace { get; set; }
        public Func<Z80Cpu, bool>? BeforeInstruction { get; set; }

        private bool halted = false;
        public bool IsHalted => halted;
        private bool interruptPending = false;
        public bool InterruptPending
        {
            get => interruptPending;
            set
            {
                if (interruptPending == value)
                    return;

                interruptPending = value;
                RecordInterruptEvent(value ? "INTP_SET" : "INTP_CLEAR");
            }
        }
        public bool IFF1 { get; private set; } = false;
        public bool IFF2 { get; private set; } = false;
        public int InterruptMode => interruptMode;
        public ulong LastInterruptProgressTStates { get; private set; } = 0;

        private int eiDelay = 0;
        private int interruptMode = 1;
        private byte qFlags = 0;
        private readonly Action[] opcodeTable = new Action[256];
        private readonly Action[] cbOpcodeTable = new Action[256];
        private readonly Action[] edOpcodeTable = new Action[256];
        private readonly Action[] ddOpcodeTable = new Action[256];
        private readonly Action[] fdOpcodeTable = new Action[256];

        private readonly Queue<string> recentTrace = new Queue<string>();
        private readonly Queue<string> recentInterruptEvents = new Queue<string>();
        private const int RecentTraceCapacity = 256;
        private const int RecentInterruptEventCapacity = 8192;
        private bool reportedHighRamEntry = false;
        private bool reportedDiWindowEntry = false;
        private bool reportedLowStackEntry = false;
        private bool reported17xxStackEntry = false;
        private bool reportedRomStackWindowEntry = false;
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

        public void AddTStates(ulong delta)
        {
            TStates += delta;
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
            interruptPending = false;
            IFF1 = false;
            IFF2 = false;

            eiDelay = 0;
            interruptMode = 1;

            reportedHighRamEntry = false;
            reportedDiWindowEntry = false;
            reportedLowStackEntry = false;
            reported17xxStackEntry = false;
            reportedRomStackWindowEntry = false;
            recentTrace.Clear();
            recentInterruptEvents.Clear();
            TStates = 0;
            LastInterruptProgressTStates = 0;

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

                    ushort returnPc = Regs.PC;
                    RecordInterruptEvent($"INT_ACCEPT return={returnPc:X4}", true);
                    RecordInterruptEvent("INT_ACCEPT");
                    LastInterruptProgressTStates = TStates;
                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;
                    // Preserve IFF2 on maskable interrupt acknowledge.
                    // RETN/RETI restore IFF1 from IFF2.

                    TStates += 7;
                    Push(Regs.PC);

                    switch (interruptMode)
                    {
                        case 0:
                        case 1:
                            Regs.PC = 0x0038;
                            RecordInterruptEvent($"INT_VECTOR target={Regs.PC:X4}");
                            break;

                        case 2:
                            ushort vector = (ushort)((Regs.I << 8) | 0xFF);
                            byte low = ReadMemory(vector);
                            byte high = ReadMemory((ushort)(vector + 1));
                            Regs.PC = (ushort)(low | (high << 8));
                            RecordInterruptEvent($"INT_VECTOR target={Regs.PC:X4}");
                            break;
                    }

                    RecordInterruptEvent($"INT_VECTOR {Regs.PC:X4}");
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

        private bool reportedFirstPermanentOffCandidate;
        private ushort lastPcBeforeStep;

        public void Step()
        {
            ushort pcBefore = Regs.PC;
            ushort spBefore = Regs.SP;
            ushort ixBefore = Regs.IX;
            ushort iyBefore = Regs.IY;
            byte fBefore = Regs.F;
            bool iff1Before = IFF1;
            bool iff2Before = IFF2;

            byte op = FetchOpcodeByte();
            lastPcBeforeStep = pcBefore;
            RecordTrace(pcBefore, op);
            if (pcBefore == 0x6D21)
            {
                RecordInterruptEvent("ENTERING_6D21_BEFORE_DI", true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_6D21 " + line, true);
            }

            if (pcBefore >= 0x6C00 && pcBefore <= 0x6E00)
            {
                if (!reportedDiWindowEntry)
                {
                    reportedDiWindowEntry = true;
                    RecordInterruptEvent("ENTERED_6C00_WINDOW", true);

                    foreach (var line in recentTrace)
                        RecordInterruptEvent("TRACE_BEFORE_6C00 " + line, true);
                }

                Trace?.Invoke(
                    $"DI-WINDOW T={TStates} PC={pcBefore:X4} OP={op:X2} " +
                    $"N={ReadMemory((ushort)(pcBefore + 1)):X2} {ReadMemory((ushort)(pcBefore + 2)):X2} " +
                    $"SP={Regs.SP:X4} [SP]={ReadMemory(Regs.SP):X2}{ReadMemory((ushort)(Regs.SP + 1)):X2} " +
                    $"AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4} " +
                    $"IFF1={(IFF1 ? 1 : 0)} IFF2={(IFF2 ? 1 : 0)}");
            }

            if (!reportedHighRamEntry && pcBefore >= 0xC000)
            {
                reportedHighRamEntry = true;
                Trace?.Invoke("=== ENTERED HIGH RAM ===");
                foreach (var line in recentTrace)
                    Trace?.Invoke(line);
            }

            if (op == 0xCB)
            {
                byte cbOp = FetchOpcodeByte();
                cbOpcodeTable[cbOp]();
            }
            else if (op == 0xED)
            {
                byte edOp = FetchOpcodeByte();
                edOpcodeTable[edOp]();
            }
            else if (op == 0xDD)
            {
                byte ddOp = FetchOpcodeByte();
                if (ddOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchOpcodeByte();
                    ExecuteIndexedCB(Regs.IX, disp, cbOp);
                }
                else
                {
                    ddOpcodeTable[ddOp]();
                }
            }
            else if (op == 0xFD)
            {
                byte fdOp = FetchOpcodeByte();
                if (fdOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchOpcodeByte();
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

            if (!reportedLowStackEntry && spBefore >= 0x4000 && Regs.SP < 0x4000)
            {
                reportedLowStackEntry = true;
                RecordInterruptEvent(
                    $"LOW_STACK_ENTER PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} " +
                    $"BYTES={FormatOpcodeWindow(pcBefore, 4)} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4}",
                    true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_LOW_STACK " + line, true);
            }

            if (!reported17xxStackEntry &&
                (spBefore < 0x1700 || spBefore > 0x17FF) &&
                Regs.SP >= 0x1700 &&
                Regs.SP <= 0x17FF)
            {
                reported17xxStackEntry = true;
                RecordInterruptEvent(
                    $"STACK_17XX_ENTER PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} " +
                    $"BYTES={FormatOpcodeWindow(pcBefore, 4)} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4}",
                    true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_17XX_STACK " + line, true);
            }

            if (!reportedRomStackWindowEntry &&
                (spBefore < 0x1000 || spBefore > 0x3FFF) &&
                Regs.SP >= 0x1000 &&
                Regs.SP <= 0x3FFF)
            {
                reportedRomStackWindowEntry = true;
                RecordInterruptEvent(
                    $"ROM_STACK_ENTER PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} " +
                    $"BYTES={FormatOpcodeWindow(pcBefore, 4)} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4}",
                    true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_ROM_STACK " + line, true);
            }

            if (Regs.SP < 0x4000)
            {
                Trace?.Invoke(
                    $"BAD SP after PC={pcBefore:X4} OP={op:X2}: SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
            }

            if ((iff1Before || iff2Before) && !IFF1 && !IFF2)
            {
                RecordInterruptEvent(
                    $"IFF_DISABLED PC={pcBefore:X4} OP={op:X2} BYTES={FormatOpcodeWindow(pcBefore, 4)} " +
                    $"SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4}",
                    true);
            }

            if (eiDelay > 0)
            {
                eiDelay--;

                if (eiDelay == 0)
                {
                    IFF1 = true;
                    IFF2 = true;
                    RecordInterruptEvent("EI_EFFECT", true);
                }
            }

            if (!reportedFirstPermanentOffCandidate &&
                !IFF1 &&
                !IFF2 &&
                TStates > 780018) // after known good interrupt in latest log
            {
                reportedFirstPermanentOffCandidate = true;

                Trace?.Invoke("=== FIRST IFF1=0 IFF2=0 AFTER GOOD INTERRUPTS ===");
                Trace?.Invoke(
                    $"T={TStates} PC_BEFORE={pcBefore:X4} PC_AFTER={Regs.PC:X4} " +
                    $"SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4} OP={op:X2}");

                foreach (var line in recentTrace)
                    Trace?.Invoke(line);
            }

            lastFlagsBeforeInstruction = fBefore;
            flagsChangedLastInstruction = Regs.F != fBefore;
            qFlags = (Regs.F != fBefore) ? Regs.F : (byte)0;
        }

        public string[] GetRecentTraceSnapshot() => recentTrace.ToArray();

        public string[] GetRecentInterruptEventsSnapshot() => recentInterruptEvents.ToArray();

        public void ClearRecentTrace()
        {
            recentTrace.Clear();
            recentInterruptEvents.Clear();
            LastInterruptProgressTStates = TStates;
        }

        private void RecordInterruptEvent(string eventText, bool countsAsProgress = false)
        {
            string line =
                $"T={TStates,10} PC={Regs.PC:X4} SP={Regs.SP:X4} IM={interruptMode} " +
                $"IFF1={(IFF1 ? 1 : 0)} IFF2={(IFF2 ? 1 : 0)} INTP={(InterruptPending ? 1 : 0)} {eventText}";

            recentInterruptEvents.Enqueue(line);
            while (recentInterruptEvents.Count > RecentInterruptEventCapacity)
                recentInterruptEvents.Dequeue();

            if (countsAsProgress)
                LastInterruptProgressTStates = TStates;
        }

        private bool IsWatchedStackAddress(ushort addr)
        {
            return addr >= 0x17DC && addr <= 0x17DF;
        }

        private void RecordStackEvent(string eventText)
        {
            RecordInterruptEvent(
                $"{eventText} PC={lastPcBeforeStep:X4} SP={Regs.SP:X4} " +
                $"AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                $"IX={Regs.IX:X4} IY={Regs.IY:X4}",
                true);
        }

        public void RestoreInterruptState(bool iff1, bool iff2, int interruptMode)
        {
            IFF1 = iff1;
            IFF2 = iff2;
            this.interruptMode = interruptMode & 0x03;
            eiDelay = 0;
            RecordInterruptEvent($"RESTORE_STATE iff1={(iff1 ? 1 : 0)} iff2={(iff2 ? 1 : 0)} im={this.interruptMode}", iff1);
        }

        public void ClearSnapshotExecutionState()
        {
            halted = false;
            InterruptPending = false;
            TStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
            LastInterruptProgressTStates = TStates;

        }

        public void AdvanceTStates(uint tStates)
        {
            TStates += tStates;
        }
    }
}
