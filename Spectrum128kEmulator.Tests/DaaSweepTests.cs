using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class DaaSweepTests
    {
        private const byte FlagC  = 0x01;
        private const byte FlagN  = 0x02;
        private const byte FlagPV = 0x04;
        private const byte FlagF3 = 0x08;
        private const byte FlagH  = 0x10;
        private const byte FlagF5 = 0x20;
        private const byte FlagZ  = 0x40;
        private const byte FlagS  = 0x80;

        private sealed record DaaSweepRow(
            byte InitialA,
            byte InitialF,
            byte ResultA,
            byte ResultF)
        {
            public string ToTsv()
            {
                return string.Join('\t', new[]
                {
                    InitialA.ToString("X2", CultureInfo.InvariantCulture),
                    InitialF.ToString("X2", CultureInfo.InvariantCulture),
                    Bit(InitialF, FlagN),
                    Bit(InitialF, FlagH),
                    Bit(InitialF, FlagC),
                    ResultA.ToString("X2", CultureInfo.InvariantCulture),
                    ResultF.ToString("X2", CultureInfo.InvariantCulture),
                    Bit(ResultF, FlagS),
                    Bit(ResultF, FlagZ),
                    Bit(ResultF, FlagF5),
                    Bit(ResultF, FlagH),
                    Bit(ResultF, FlagF3),
                    Bit(ResultF, FlagPV),
                    Bit(ResultF, FlagN),
                    Bit(ResultF, FlagC)
                });
            }

            private static string Bit(byte value, byte mask) => ((value & mask) != 0) ? "1" : "0";
        }

        [Fact]
        public void Daa_Enumerate_All_2048_States_And_Write_Stable_Table()
        {
            List<DaaSweepRow> rows = new(capacity: 2048);

            for (int a = 0; a <= 0xFF; a++)
            {
                for (int n = 0; n <= 1; n++)
                {
                    for (int h = 0; h <= 1; h++)
                    {
                        for (int c = 0; c <= 1; c++)
                        {
                            byte initialA = (byte)a;
                            byte initialF = ComposeInputFlags(
                                n: n != 0,
                                h: h != 0,
                                c: c != 0);

                            var cpu = CreateCpuForSingleOpcode(
                                opcode: 0x27, // DAA
                                initialA: initialA,
                                initialF: initialF);

                            cpu.Step();

                            rows.Add(new DaaSweepRow(
                                InitialA: initialA,
                                InitialF: initialF,
                                ResultA: cpu.Regs.A,
                                ResultF: cpu.Regs.F));
                        }
                    }
                }
            }

            Assert.Equal(2048, rows.Count);

            string[] lines = new string[rows.Count + 1];
            lines[0] =
                "InitialA\tInitialF\tInN\tInH\tInC\tResultA\tResultF\tS\tZ\tF5\tH\tF3\tPV\tN\tC";

            for (int i = 0; i < rows.Count; i++)
            {
                lines[i + 1] = rows[i].ToTsv();
            }

            string outputDir = Path.Combine(Path.GetTempPath(), "Spectrum128kEmulator", "DaaSweep");
            Directory.CreateDirectory(outputDir);

            string filePath = Path.Combine(outputDir, "daa_sweep_current.tsv");
            File.WriteAllLines(filePath, lines);

            // Also emit a small summary file grouped by input flag triple, which is handy for eyeballing patterns.
            string summaryPath = Path.Combine(outputDir, "daa_sweep_summary.txt");
            File.WriteAllLines(summaryPath, BuildSummary(rows));

            // Surface paths in test output
            Assert.True(File.Exists(filePath), $"Expected sweep file to exist: {filePath}");
            Assert.True(File.Exists(summaryPath), $"Expected summary file to exist: {summaryPath}");

            // Optional sanity checks so the test still gives useful signal even without opening the files.
            Assert.Contains(rows, r => r.InitialA == 0x9A && (r.InitialF & FlagN) == 0 && (r.InitialF & FlagH) == 0 && (r.InitialF & FlagC) == 0);
            Assert.Contains(rows, r => r.InitialA == 0xFF && (r.InitialF & FlagN) != 0 && (r.InitialF & FlagH) != 0 && (r.InitialF & FlagC) != 0);
        }

        private static IEnumerable<string> BuildSummary(IReadOnlyList<DaaSweepRow> rows)
        {
            yield return "DAA sweep summary";
            yield return $"Total rows: {rows.Count}";
            yield return "";

            foreach (var group in rows.GroupBy(r => (InN: Has(r.InitialF, FlagN), InH: Has(r.InitialF, FlagH), InC: Has(r.InitialF, FlagC)))
                                     .OrderBy(g => g.Key.InN ? 1 : 0)
                                     .ThenBy(g => g.Key.InH ? 1 : 0)
                                     .ThenBy(g => g.Key.InC ? 1 : 0))
            {
                yield return $"Input flags N={Bool01(group.Key.InN)} H={Bool01(group.Key.InH)} C={Bool01(group.Key.InC)}";

                int carrySet = group.Count(r => Has(r.ResultF, FlagC));
                int halfSet = group.Count(r => Has(r.ResultF, FlagH));
                int zeroSet = group.Count(r => Has(r.ResultF, FlagZ));
                int signSet = group.Count(r => Has(r.ResultF, FlagS));
                int paritySet = group.Count(r => Has(r.ResultF, FlagPV));

                yield return $"  Count rows : {group.Count()}";
                yield return $"  Result C=1 : {carrySet}";
                yield return $"  Result H=1 : {halfSet}";
                yield return $"  Result Z=1 : {zeroSet}";
                yield return $"  Result S=1 : {signSet}";
                yield return $"  Result PV=1: {paritySet}";

                foreach (var sample in group.Take(8))
                {
                    yield return $"    A={sample.InitialA:X2} F={sample.InitialF:X2} -> A'={sample.ResultA:X2} F'={sample.ResultF:X2}";
                }

                yield return "";
            }
        }

        private static Z80Cpu CreateCpuForSingleOpcode(byte opcode, byte initialA, byte initialF)
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

        private static byte ComposeInputFlags(bool n, bool h, bool c)
        {
            byte f = 0;
            if (n) f |= FlagN;
            if (h) f |= FlagH;
            if (c) f |= FlagC;
            return f;
        }

        private static bool Has(byte value, byte mask) => (value & mask) != 0;

        private static string Bool01(bool value) => value ? "1" : "0";
    }
}
