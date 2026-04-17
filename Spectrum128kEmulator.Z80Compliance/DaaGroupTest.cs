using System;
using Spectrum128kEmulator.Z80;

public static class DaaGroupTest
{
    public static void Run(Z80Cpu cpu)
    {
        Console.WriteLine("=== Running fast DAA/CPL/SCF/CCF sweep ===");

        byte[] ram = new byte[65536];
        cpu.ReadMemory = address => ram[address];
        cpu.WriteMemory = (address, value) => ram[address] = value;
        cpu.ReadPort = _ => 0xFF;
        cpu.WritePort = (_, _) => { };

        ram[0x0000] = 0x27; // DAA
        ram[0x0001] = 0x2F; // CPL
        ram[0x0002] = 0x37; // SCF
        ram[0x0003] = 0x3F; // CCF

        uint crc = 0xFFFFFFFF;

        for (int a = 0; a < 256; a++)
        {
            for (int f = 0; f < 256; f++)
            {
                cpu.Reset();
                cpu.Regs.A = (byte)a;
                cpu.Regs.F = (byte)f;
                cpu.Regs.PC = 0x0000;

                cpu.Step();
                cpu.Step();
                cpu.Step();
                cpu.Step();

                crc = UpdateCrc(crc, cpu.Regs.A);
                crc = UpdateCrc(crc, cpu.Regs.F);
            }
        }

        crc ^= 0xFFFFFFFF;

        Console.WriteLine($"Local CRC: {crc:X8}");
    }

    private static uint UpdateCrc(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0
                ? (crc >> 1) ^ 0xEDB88320u
                : (crc >> 1);
        }
        return crc;
    }
}
