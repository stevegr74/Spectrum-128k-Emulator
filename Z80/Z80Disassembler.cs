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
            string line = $"PC={pcBefore:X4} OP={op:X2} SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4}";
            recentTrace.Enqueue(line);
            if (recentTrace.Count > 40)
                recentTrace.Dequeue();
        }
    }
}
