using System;
using System.Collections.Generic;
using System.Text;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        public Z80Registers Regs { get; } = new Z80Registers();
        public ulong TStates { get; private set; } = 0;
        public ulong InstructionFetchCount { get; private set; } = 0;

        public Func<ushort, byte> ReadMemory { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WriteMemory { get; set; } = (_, _) => { };
        public Func<ushort, byte> ReadPort { get; set; } = _ => 0xFF;
        public Func<ushort, int, byte>? ReadPortTimed { get; set; }
        public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };
        public Action<string>? Trace { get; set; }
        public Func<Z80Cpu, bool>? BeforeInstruction { get; set; }
        // Diagnostic-only stop hook.  It is unset during normal emulation.
        public Func<Z80Cpu, bool>? StopBeforeInstruction { get; set; }
        public bool ExecutionStopped { get; private set; }

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
        private bool instructionTraceCaptureEnabled;
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
            const int portCycleTStates = 4;
            TStates += (ulong)(instructionTStates - portCycleTStates);
            WritePort(port, value);
            TStates += portCycleTStates;
        }

        // =========================================================
        // Public control
        // =========================================================

        public void Reset()
        {
            Regs.AF = 0xFFFF;
            Regs.BC = 0x0000;
            Regs.DE = 0x0000;
            Regs.HL = 0x0000;
            Regs.A_ = 0;
            Regs.F_ = 0;
            Regs.B_ = 0;
            Regs.C_ = 0;
            Regs.D_ = 0;
            Regs.E_ = 0;
            Regs.H_ = 0;
            Regs.L_ = 0;
            Regs.IX = 0xFFFF;
            Regs.IY = 0xFFFF;
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
            qFlags = 0;

            recentTrace.Clear();
            recentInterruptEvents.Clear();
            TStates = 0;
            InstructionFetchCount = 0;
            LastInterruptProgressTStates = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
            ExecutionStopped = false;
        }

        public void ResetExecutionStatePreserveTiming()
        {
            Regs.AF = 0xFFFF;
            Regs.BC = 0x0000;
            Regs.DE = 0x0000;
            Regs.HL = 0x0000;
            Regs.A_ = 0;
            Regs.F_ = 0;
            Regs.B_ = 0;
            Regs.C_ = 0;
            Regs.D_ = 0;
            Regs.E_ = 0;
            Regs.H_ = 0;
            Regs.L_ = 0;
            Regs.IX = 0xFFFF;
            Regs.IY = 0xFFFF;
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
            qFlags = 0;
            LastInterruptProgressTStates = TStates;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
            ExecutionStopped = false;
        }

        public void ExecuteCycles(ulong cycles)
        {
            ulong target = TStates + cycles;
            ExecutionStopped = false;

            while (TStates < target)
            {
                if (BeforeInstruction != null && BeforeInstruction(this))
                    continue;

                if (StopBeforeInstruction?.Invoke(this) == true)
                {
                    ExecutionStopped = true;
                    break;
                }

                if (InterruptPending && IFF1)
                {
                    ushort returnPc = Regs.PC;
                    RecordInterruptEvent($"INT_ACCEPT return={returnPc:X4}", true);
                    RecordInterruptEvent("INT_ACCEPT");
                    LastInterruptProgressTStates = TStates;
                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;
                    // Preserve IFF2 on maskable interrupt acknowledge.
                    // RETN/RETI restore IFF1 from IFF2.

                    IncrementRefreshRegister();
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
                    IncrementRefreshRegister();
                    TStates += 4;
                    InstructionFetchCount++;
                    continue;
                }

                Step();
            }
        }

        public void ExecuteInstructionFetches(ulong fetches)
        {
            ulong target = InstructionFetchCount + fetches;
            ExecutionStopped = false;

            while (InstructionFetchCount < target)
            {
                if (BeforeInstruction != null && BeforeInstruction(this))
                    continue;

                if (StopBeforeInstruction?.Invoke(this) == true)
                {
                    ExecutionStopped = true;
                    break;
                }

                if (InterruptPending && IFF1)
                {
                    ushort returnPc = Regs.PC;
                    RecordInterruptEvent($"INT_ACCEPT return={returnPc:X4}", true);
                    RecordInterruptEvent("INT_ACCEPT");
                    LastInterruptProgressTStates = TStates;
                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;

                    IncrementRefreshRegister();
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
                    IncrementRefreshRegister();
                    TStates += 4;
                    InstructionFetchCount++;
                    continue;
                }

                Step();
            }
        }

        public void Step()
        {
            ushort pcBefore = Regs.PC;
            byte fBefore = Regs.F;

            byte op = FetchOpcodeByte();
            RecordTrace(pcBefore, op);

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

        public void SetInstructionTraceCaptureEnabled(bool enabled)
        {
            instructionTraceCaptureEnabled = enabled;
            if (!enabled)
                recentTrace.Clear();
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
            InstructionFetchCount = 0;

            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;
            qFlags = 0;
            LastInterruptProgressTStates = TStates;

        }

        public void AdvanceTStates(uint tStates)
        {
            TStates += tStates;
        }
    }
}
