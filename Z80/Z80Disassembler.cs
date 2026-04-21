using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        // =========================================================
        // Trace / disassembly scaffolding
        // =========================================================

        private void RecordTrace(ushort pcBefore, byte op)
        {
            byte next0 = ReadMemory((ushort)(pcBefore + 1));
            byte next1 = ReadMemory((ushort)(pcBefore + 2));
            ushort stackTop = (ushort)(ReadMemory(Regs.SP) | (ReadMemory((ushort)(Regs.SP + 1)) << 8));

            string line =
                $"T={TStates,10} PC={pcBefore:X4} OP={op:X2} N={next0:X2} {next1:X2} " +
                $"SP={Regs.SP:X4} [SP]={stackTop:X4} " +
                $"AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} IX={Regs.IX:X4} IY={Regs.IY:X4} " +
                $"I={Regs.I:X2} R={Regs.R:X2} IM={InterruptMode} IFF1={(IFF1 ? 1 : 0)} IFF2={(IFF2 ? 1 : 0)} " +
                $"HALT={(IsHalted ? 1 : 0)} INTP={(InterruptPending ? 1 : 0)}";

            recentTrace.Enqueue(line);
            while (recentTrace.Count > RecentTraceCapacity)
                recentTrace.Dequeue();
        }
    }
}
