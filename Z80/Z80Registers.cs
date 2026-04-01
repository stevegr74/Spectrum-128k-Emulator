using System;

namespace Spectrum128kEmulator.Z80
{
    public class Z80Registers
    {
        // Main registers
        public byte A { get; set; }
        public byte F { get; set; }   // Flags
        public byte B { get; set; }
        public byte C { get; set; }
        public byte D { get; set; }
        public byte E { get; set; }
        public byte H { get; set; }
        public byte L { get; set; }

        // Alternate registers (EX AF,AF' and EXX)
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

        // Stack pointer, program counter, interrupt & refresh
        public ushort SP { get; set; }
        public ushort PC { get; set; }
        public byte I { get; set; }
        public byte R { get; set; }

        // 16-bit combined registers (most important ones)
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

        // Optional but useful: AF' (alternate)
        public ushort AF_ 
        { 
            get => (ushort)((A_ << 8) | F_); 
            set { A_ = (byte)(value >> 8); F_ = (byte)value; } 
        }

        public Z80Registers()
        {
            // Realistic power-on / reset state for ZX Spectrum
            AF = 0xFFFF;        // Common default
            BC = 0x0000;
            DE = 0x0000;
            HL = 0x0000;
            IX = 0xFFFF;
            IY = 0xFFFF;
            SP = 0xFFFF;
            PC = 0x0000;        // Starts at address 0 (ROM)
            I = 0x00;
            R = 0x00;

            // Alternate set also zeroed by default
            A_ = 0;
            F_ = 0;
            B_ = C_ = D_ = E_ = H_ = L_ = 0;
        }
    }
}