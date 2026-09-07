using System;

namespace Spectrum128kEmulator.Z80
{
    public class Z80Registers
    {
        // Main registers
        public byte A { get; set; }
        public byte F { get; set; }
        public byte B { get; set; }
        public byte C { get; set; }
        public byte D { get; set; }
        public byte E { get; set; }
        public byte H { get; set; }
        public byte L { get; set; }

        // Alternate registers
        public byte A_ { get; set; }
        public byte F_ { get; set; }
        public byte B_ { get; set; }
        public byte C_ { get; set; }
        public byte D_ { get; set; }
        public byte E_ { get; set; }
        public byte H_ { get; set; }
        public byte L_ { get; set; }

        // Index registers
        public ushort IX { get; set; }
        public ushort IY { get; set; }

        // System registers
        public ushort SP { get; set; }
        public ushort PC { get; set; }
        public byte I { get; set; }
        public byte R { get; set; }

        // 16-bit helpers
        public ushort AF
        {
            get => (ushort)((A << 8) | F);
            set { A = (byte)(value >> 8); F = (byte)value; }
        }
        public ushort BC
        {
            get => (ushort)((B << 8) | C);
            set { B = (byte)(value >> 8); C = (byte)value; }
        }
        public ushort DE
        {
            get => (ushort)((D << 8) | E);
            set { D = (byte)(value >> 8); E = (byte)value; }
        }
        public ushort HL
        {
            get => (ushort)((H << 8) | L);
            set { H = (byte)(value >> 8); L = (byte)value; }
        }

        public Z80Registers()
        {
            AF = 0xFFFF;
            BC = DE = HL = 0x0000;
            IX = IY = 0xFFFF;
            SP = 0xFFFF;
            PC = 0x0000;
            I = 0;
            R = 0;

            A_ = F_ = B_ = C_ = D_ = E_ = H_ = L_ = 0;
        }
    }
}