using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class CplScfCcfSweepTests
    {
        private const byte FlagC  = 0x01;
        private const byte FlagN  = 0x02;
        private const byte FlagPV = 0x04;
        private const byte FlagF3 = 0x08;
        private const byte FlagH  = 0x10;
        private const byte FlagF5 = 0x20;
        private const byte FlagZ  = 0x40;
        private const byte FlagS  = 0x80;

        private static Z80Cpu CreateCpu(byte opcode, byte initialA, byte initialF)
        {
            byte[] memory = new byte[65536];
            memory[0x0000] = opcode;

            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value,
                ReadPort = _ => 0xFF,
                WritePort = (_, _) => { }
            };

            cpu.Reset();
            cpu.Regs.PC = 0x0000;
            cpu.Regs.A = initialA;
            cpu.Regs.F = initialF;
            return cpu;
        }

        private static bool Has(byte value, byte mask) => (value & mask) != 0;

        private static byte ComposeFlags(
            bool s = false,
            bool z = false,
            bool f5 = false,
            bool h = false,
            bool f3 = false,
            bool pv = false,
            bool n = false,
            bool c = false)
        {
            byte f = 0;
            if (s)  f |= FlagS;
            if (z)  f |= FlagZ;
            if (f5) f |= FlagF5;
            if (h)  f |= FlagH;
            if (f3) f |= FlagF3;
            if (pv) f |= FlagPV;
            if (n)  f |= FlagN;
            if (c)  f |= FlagC;
            return f;
        }

        [Fact]
        public void Cpl_Exhaustive_Sweep()
        {
            var lines = new List<string>
            {
                "A_in\tF_in\tS_in\tZ_in\tPV_in\tC_in\tA_out\tF_out\tS\tZ\tF5\tH\tF3\tPV\tN\tC"
            };

            int cases = 0;

            for (int a = 0; a <= 0xFF; a++)
            {
                for (int mask = 0; mask < 16; mask++)
                {
                    bool s  = (mask & 0x1) != 0;
                    bool z  = (mask & 0x2) != 0;
                    bool pv = (mask & 0x4) != 0;
                    bool c  = (mask & 0x8) != 0;

                    byte initialA = (byte)a;
                    byte initialF = ComposeFlags(s: s, z: z, pv: pv, c: c);

                    var cpu = CreateCpu(0x2F, initialA, initialF); // CPL
                    cpu.Step();

                    byte expectedA = (byte)~initialA;

                    Assert.Equal(expectedA, cpu.Regs.A);

                    Assert.True(Has(cpu.Regs.F, FlagN));
                    Assert.True(Has(cpu.Regs.F, FlagH));

                    Assert.Equal(s,  Has(cpu.Regs.F, FlagS));
                    Assert.Equal(z,  Has(cpu.Regs.F, FlagZ));
                    Assert.Equal(pv, Has(cpu.Regs.F, FlagPV));
                    Assert.Equal(c,  Has(cpu.Regs.F, FlagC));

                    Assert.Equal((expectedA & FlagF3) != 0, Has(cpu.Regs.F, FlagF3));
                    Assert.Equal((expectedA & FlagF5) != 0, Has(cpu.Regs.F, FlagF5));

                    lines.Add(Row(
                        initialA, initialF,
                        s, z, pv, c,
                        cpu.Regs.A, cpu.Regs.F));

                    cases++;
                }
            }

            Assert.Equal(4096, cases);
            WriteSweepFile("cpl_sweep.tsv", lines);
        }

        [Fact]
        public void Scf_Exhaustive_Sweep()
        {
            var lines = new List<string>
            {
                "A_in\tF_in\tS_in\tZ_in\tPV_in\tH_in\tN_in\tC_in\tA_out\tF_out\tS\tZ\tF5\tH\tF3\tPV\tN\tC"
            };

            int cases = 0;

            for (int a = 0; a <= 0xFF; a++)
            {
                for (int mask = 0; mask < 64; mask++)
                {
                    bool s  = (mask & 0x01) != 0;
                    bool z  = (mask & 0x02) != 0;
                    bool pv = (mask & 0x04) != 0;
                    bool h  = (mask & 0x08) != 0;
                    bool n  = (mask & 0x10) != 0;
                    bool c  = (mask & 0x20) != 0;

                    byte initialA = (byte)a;
                    byte initialF = ComposeFlags(s: s, z: z, pv: pv, h: h, n: n, c: c);

                    var cpu = CreateCpu(0x37, initialA, initialF); // SCF
                    cpu.Step();

                    Assert.Equal(initialA, cpu.Regs.A);

                    Assert.Equal(s,  Has(cpu.Regs.F, FlagS));
                    Assert.Equal(z,  Has(cpu.Regs.F, FlagZ));
                    Assert.Equal(pv, Has(cpu.Regs.F, FlagPV));

                    Assert.False(Has(cpu.Regs.F, FlagH));
                    Assert.False(Has(cpu.Regs.F, FlagN));
                    Assert.True(Has(cpu.Regs.F, FlagC));

                    Assert.Equal((initialA & FlagF3) != 0, Has(cpu.Regs.F, FlagF3));
                    Assert.Equal((initialA & FlagF5) != 0, Has(cpu.Regs.F, FlagF5));

                    lines.Add(Row(
                        initialA, initialF,
                        s, z, pv, h, n, c,
                        cpu.Regs.A, cpu.Regs.F));

                    cases++;
                }
            }

            Assert.Equal(16384, cases);
            WriteSweepFile("scf_sweep.tsv", lines);
        }

        [Fact]
        public void Ccf_Exhaustive_Sweep()
        {
            var lines = new List<string>
            {
                "A_in\tF_in\tS_in\tZ_in\tPV_in\tC_in\tA_out\tF_out\tS\tZ\tF5\tH\tF3\tPV\tN\tC"
            };

            int cases = 0;

            for (int a = 0; a <= 0xFF; a++)
            {
                for (int mask = 0; mask < 16; mask++)
                {
                    bool s  = (mask & 0x1) != 0;
                    bool z  = (mask & 0x2) != 0;
                    bool pv = (mask & 0x4) != 0;
                    bool c  = (mask & 0x8) != 0;

                    byte initialA = (byte)a;
                    byte initialF = ComposeFlags(s: s, z: z, pv: pv, c: c);

                    var cpu = CreateCpu(0x3F, initialA, initialF); // CCF
                    cpu.Step();

                    Assert.Equal(initialA, cpu.Regs.A);

                    Assert.Equal(s,  Has(cpu.Regs.F, FlagS));
                    Assert.Equal(z,  Has(cpu.Regs.F, FlagZ));
                    Assert.Equal(pv, Has(cpu.Regs.F, FlagPV));

                    Assert.Equal(c, Has(cpu.Regs.F, FlagH));      // H = old C
                    Assert.False(Has(cpu.Regs.F, FlagN));
                    Assert.Equal(!c, Has(cpu.Regs.F, FlagC));     // C toggled

                    Assert.Equal((initialA & FlagF3) != 0, Has(cpu.Regs.F, FlagF3));
                    Assert.Equal((initialA & FlagF5) != 0, Has(cpu.Regs.F, FlagF5));

                    lines.Add(Row(
                        initialA, initialF,
                        s, z, pv, c,
                        cpu.Regs.A, cpu.Regs.F));

                    cases++;
                }
            }

            Assert.Equal(4096, cases);
            WriteSweepFile("ccf_sweep.tsv", lines);
        }

        private static string Row(
            byte initialA,
            byte initialF,
            bool sIn,
            bool zIn,
            bool pvIn,
            bool cIn,
            byte resultA,
            byte resultF)
        {
            return string.Join('\t', new[]
            {
                Hex(initialA),
                Hex(initialF),
                Bit(sIn),
                Bit(zIn),
                Bit(pvIn),
                Bit(cIn),
                Hex(resultA),
                Hex(resultF),
                Bit(Has(resultF, FlagS)),
                Bit(Has(resultF, FlagZ)),
                Bit(Has(resultF, FlagF5)),
                Bit(Has(resultF, FlagH)),
                Bit(Has(resultF, FlagF3)),
                Bit(Has(resultF, FlagPV)),
                Bit(Has(resultF, FlagN)),
                Bit(Has(resultF, FlagC)),
            });
        }

        private static string Row(
            byte initialA,
            byte initialF,
            bool sIn,
            bool zIn,
            bool pvIn,
            bool hIn,
            bool nIn,
            bool cIn,
            byte resultA,
            byte resultF)
        {
            return string.Join('\t', new[]
            {
                Hex(initialA),
                Hex(initialF),
                Bit(sIn),
                Bit(zIn),
                Bit(pvIn),
                Bit(hIn),
                Bit(nIn),
                Bit(cIn),
                Hex(resultA),
                Hex(resultF),
                Bit(Has(resultF, FlagS)),
                Bit(Has(resultF, FlagZ)),
                Bit(Has(resultF, FlagF5)),
                Bit(Has(resultF, FlagH)),
                Bit(Has(resultF, FlagF3)),
                Bit(Has(resultF, FlagPV)),
                Bit(Has(resultF, FlagN)),
                Bit(Has(resultF, FlagC)),
            });
        }

        private static void WriteSweepFile(string fileName, IReadOnlyList<string> lines)
        {
            string outputDir = Path.Combine(Path.GetTempPath(), "Spectrum128kEmulator", "FlagSweeps");
            Directory.CreateDirectory(outputDir);

            string filePath = Path.Combine(outputDir, fileName);
            File.WriteAllLines(filePath, lines);

            Assert.True(File.Exists(filePath), $"Expected sweep file to exist: {filePath}");
        }

        private static string Hex(byte value) => value.ToString("X2", CultureInfo.InvariantCulture);
        private static string Bit(bool value) => value ? "1" : "0";
    }
}
