using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class Z80SnapshotLoaderTests
    {
        private const int HeaderSizeV1 = 30;
        private const int Ram48Size = 48 * 1024;
        private const int RamBankSize = 16 * 1024;

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }

        [Fact]
        public void LoadZ80v1_Uncompressed_Restores_Registers_And_Memory()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "test.z80");

            try
            {
                byte[] ram = new byte[Ram48Size];
                ram[0x0000] = 0x42;
                ram[0x4000] = 0x99;
                ram[0x8000] = 0x55;

                byte[] data = BuildV1Snapshot(ram, compressed: false);
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                Z80SnapshotLoader.Load(machine, snapshotPath);

                Assert.Equal((byte)0x12, machine.Cpu.Regs.A);
                Assert.Equal((byte)0x34, machine.Cpu.Regs.F);
                Assert.Equal((byte)0x56, machine.Cpu.Regs.C);
                Assert.Equal((byte)0x78, machine.Cpu.Regs.B);
                Assert.Equal((byte)0x9A, machine.Cpu.Regs.L);
                Assert.Equal((byte)0xBC, machine.Cpu.Regs.H);
                Assert.Equal((ushort)0x2345, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xCDEF, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0x44, machine.Cpu.Regs.I);
                Assert.Equal((byte)0x85, machine.Cpu.Regs.R);
                Assert.Equal((byte)0x66, machine.Cpu.Regs.E);
                Assert.Equal((byte)0x77, machine.Cpu.Regs.D);
                Assert.Equal((byte)0x88, machine.Cpu.Regs.C_);
                Assert.Equal((byte)0x99, machine.Cpu.Regs.B_);
                Assert.Equal((byte)0xAA, machine.Cpu.Regs.E_);
                Assert.Equal((byte)0xBB, machine.Cpu.Regs.D_);
                Assert.Equal((byte)0xCC, machine.Cpu.Regs.L_);
                Assert.Equal((byte)0xDD, machine.Cpu.Regs.H_);
                Assert.Equal((byte)0xEE, machine.Cpu.Regs.A_);
                Assert.Equal((byte)0xF0, machine.Cpu.Regs.F_);
                Assert.Equal((ushort)0x1357, machine.Cpu.Regs.IY);
                Assert.Equal((ushort)0x2468, machine.Cpu.Regs.IX);
                Assert.True(machine.Cpu.IFF1);
                Assert.True(machine.Cpu.IFF2);
                Assert.Equal(2, machine.BorderColor);
                Assert.Equal((byte)0x42, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0x99, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x55, machine.PeekMemory(0xC000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadZ80v1_Compressed_Restores_48k_Ram()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "compressed.z80");

            try
            {
                byte[] ram = new byte[Ram48Size];
                for (int i = 0; i < 16384; i++)
                    ram[i] = 0x00;
                for (int i = 16384; i < 32768; i++)
                    ram[i] = 0x11;
                for (int i = 32768; i < Ram48Size; i++)
                    ram[i] = 0x22;

                ram[0x0001] = 0xED;
                ram[0x4002] = 0x33;
                ram[0x8003] = 0x44;

                byte[] data = BuildV1Snapshot(ram, compressed: true);
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                Z80SnapshotLoader.Load(machine, snapshotPath);

                Assert.Equal((byte)0x00, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0xED, machine.PeekMemory(0x4001));
                Assert.Equal((byte)0x11, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x33, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x22, machine.PeekMemory(0xC000));
                Assert.Equal((byte)0x44, machine.PeekMemory(0xC003));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadZ80v2_48k_PageBlocks_Restores_48k_Memory()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "48k-v2.z80");

            try
            {
                byte[] page8 = CreateFilledBank(0x11);
                byte[] page4 = CreateFilledBank(0x22);
                byte[] page5 = CreateFilledBank(0x33);

                page8[0x0001] = 0x7A;
                page4[0x0002] = 0x8B;
                page5[0x0003] = 0x9C;

                byte[] data = BuildExtendedSnapshot(
                    additionalHeaderLength: 23,
                    hardwareMode: 0,
                    last7ffd: 0x00,
                    compressedBlocks: false,
                    (8, page8),
                    (4, page4),
                    (5, page5));

                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                Z80SnapshotLoader.Load(machine, snapshotPath);

                Assert.Equal((ushort)0x3456, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x11, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0x7A, machine.PeekMemory(0x4001));
                Assert.Equal((byte)0x22, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x8B, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x33, machine.PeekMemory(0xC000));
                Assert.Equal((byte)0x9C, machine.PeekMemory(0xC003));
                Assert.Equal(5, machine.BorderColor);
                Assert.True(machine.PagingLocked);
                Assert.Equal(1, machine.CurrentRomBank);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadZ80v2_48k_PageBlocks_Applies_Default_Initial_Interrupt_Delay()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "48k-delay.z80");

            try
            {
                byte[] page8 = CreateFilledBank(0x11);
                byte[] page4 = CreateFilledBank(0x22);
                byte[] page5 = CreateFilledBank(0x33);

                byte[] data = BuildExtendedSnapshot(
                    additionalHeaderLength: 23,
                    hardwareMode: 0,
                    last7ffd: 0x00,
                    compressedBlocks: false,
                    (8, page8),
                    (4, page4),
                    (5, page5));

                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                Z80SnapshotLoader.Load(machine, snapshotPath);
                machine.Cpu.ClearRecentTrace();

                machine.ExecuteFrame();

                string[] events = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong firstAcceptTStates = ExtractFirstInterruptAcceptTStates(events);
                Assert.InRange(
                    firstAcceptTStates,
                    (ulong)Spectrum128Machine.Default48kSnapshotInitialInterruptDelay,
                    (ulong)Spectrum128Machine.Default48kSnapshotInitialInterruptDelay + 16UL);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadZ80v3_128k_PageBlocks_Restores_All_Banks_And_Paging_State()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "128k-v3.z80");

            try
            {
                var pages = new List<(int pageNumber, byte[] data)>();
                for (int page = 3; page <= 10; page++)
                {
                    byte[] bank = CreateFilledBank((byte)(0x10 * (page - 2)));
                    bank[page] = (byte)(0xA0 + page);
                    pages.Add((page, bank));
                }

                byte last7ffd = 0x1F;
                byte[] data = BuildExtendedSnapshot(
                    additionalHeaderLength: 54,
                    hardwareMode: 4,
                    last7ffd: last7ffd,
                    compressedBlocks: true,
                    pages.ToArray());

                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                Z80SnapshotLoader.Load(machine, snapshotPath);

                Assert.Equal((ushort)0x3456, machine.Cpu.Regs.PC);
                Assert.Equal(7, machine.PagedRamBank);
                Assert.Equal(7, machine.ScreenBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.False(machine.PagingLocked);
                Assert.Equal(5, machine.BorderColor);

                Assert.Equal((byte)0x60, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0x30, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0xAA, machine.PeekMemory(0xC00A));

                Assert.Equal((byte)0xA3, machine.GetRamBankCopy(0)[3]);
                Assert.Equal((byte)0xA4, machine.GetRamBankCopy(1)[4]);
                Assert.Equal((byte)0xA5, machine.GetRamBankCopy(2)[5]);
                Assert.Equal((byte)0xA6, machine.GetRamBankCopy(3)[6]);
                Assert.Equal((byte)0xA7, machine.GetRamBankCopy(4)[7]);
                Assert.Equal((byte)0xA8, machine.GetRamBankCopy(5)[8]);
                Assert.Equal((byte)0xA9, machine.GetRamBankCopy(6)[9]);
                Assert.Equal((byte)0xAA, machine.GetRamBankCopy(7)[10]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadZ80_Rejects_Unsupported_Hardware_Mode()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "unsupported.z80");

            try
            {
                byte[] page8 = CreateFilledBank(0x11);
                byte[] page4 = CreateFilledBank(0x22);
                byte[] page5 = CreateFilledBank(0x33);

                byte[] data = BuildExtendedSnapshot(
                    additionalHeaderLength: 23,
                    hardwareMode: 2,
                    last7ffd: 0x00,
                    compressedBlocks: false,
                    (8, page8),
                    (4, page4),
                    (5, page5));

                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() =>
                    Z80SnapshotLoader.Load(machine, snapshotPath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        private static byte[] BuildV1Snapshot(byte[] ram, bool compressed)
        {
            if (ram.Length != Ram48Size)
                throw new ArgumentException("RAM must be exactly 48K.", nameof(ram));

            var data = new List<byte>(HeaderSizeV1 + ram.Length + 4);
            AppendCommonHeader(data, pc: 0x2345, flagsByte12: (byte)(0x04 | 0x01 | (compressed ? 0x20 : 0x00)));
            data.Add(0x01);
            data.Add(0x01);
            data.Add(0x02);

            if (compressed)
            {
                data.AddRange(CompressBlock(ram));
                data.Add(0x00);
                data.Add(0xED);
                data.Add(0xED);
                data.Add(0x00);
            }
            else
            {
                data.AddRange(ram);
            }

            return data.ToArray();
        }

        private static byte[] BuildExtendedSnapshot(
            int additionalHeaderLength,
            byte hardwareMode,
            byte last7ffd,
            bool compressedBlocks,
            params (int pageNumber, byte[] data)[] pages)
        {
            var data = new List<byte>();
            AppendCommonHeader(data, pc: 0x0000, flagsByte12: 0x0B);
            data.Add(0x01);
            data.Add(0x01);
            data.Add(0x02);

            data.Add((byte)(additionalHeaderLength & 0xFF));
            data.Add((byte)(additionalHeaderLength >> 8));

            var additional = new byte[additionalHeaderLength];
            additional[0] = 0x56;
            additional[1] = 0x34;
            additional[2] = hardwareMode;
            if (additionalHeaderLength >= 4)
                additional[3] = last7ffd;
            data.AddRange(additional);

            foreach (var (pageNumber, pageData) in pages)
            {
                if (pageData.Length != RamBankSize)
                    throw new ArgumentException("Each page must be exactly 16K.", nameof(pages));

                if (compressedBlocks)
                {
                    byte[] compressed = CompressBlock(pageData).ToArray();
                    data.Add((byte)(compressed.Length & 0xFF));
                    data.Add((byte)(compressed.Length >> 8));
                    data.Add((byte)pageNumber);
                    data.AddRange(compressed);
                }
                else
                {
                    data.Add(0xFF);
                    data.Add(0xFF);
                    data.Add((byte)pageNumber);
                    data.AddRange(pageData);
                }
            }

            return data.ToArray();
        }

        private static void AppendCommonHeader(List<byte> data, ushort pc, byte flagsByte12)
        {
            data.Add(0x12);
            data.Add(0x34);
            data.Add(0x56);
            data.Add(0x78);
            data.Add(0x9A);
            data.Add(0xBC);
            data.Add((byte)(pc & 0xFF));
            data.Add((byte)(pc >> 8));
            data.Add(0xEF);
            data.Add(0xCD);
            data.Add(0x44);
            data.Add(0x05);
            data.Add(flagsByte12);
            data.Add(0x66);
            data.Add(0x77);
            data.Add(0x88);
            data.Add(0x99);
            data.Add(0xAA);
            data.Add(0xBB);
            data.Add(0xCC);
            data.Add(0xDD);
            data.Add(0xEE);
            data.Add(0xF0);
            data.Add(0x57);
            data.Add(0x13);
            data.Add(0x68);
            data.Add(0x24);
        }

        private static byte[] CreateFilledBank(byte value)
        {
            byte[] bank = new byte[RamBankSize];
            for (int i = 0; i < bank.Length; i++)
                bank[i] = value;
            return bank;
        }

        private static ulong ExtractFirstInterruptAcceptTStates(string[] events)
        {
            foreach (string line in events)
            {
                int acceptIndex = line.IndexOf("INT_ACCEPT", StringComparison.Ordinal);
                if (acceptIndex < 0 || line.Contains("return=", StringComparison.Ordinal) == false)
                    continue;

                int tIndex = line.IndexOf("T=", StringComparison.Ordinal);
                if (tIndex < 0)
                    continue;

                int pcIndex = line.IndexOf("PC=", StringComparison.Ordinal);
                if (pcIndex <= tIndex)
                    continue;

                string tToken = line.Substring(tIndex + 2, pcIndex - (tIndex + 2)).Trim();
                if (ulong.TryParse(tToken, out ulong tStates))
                    return tStates;
            }

            throw new InvalidOperationException("No INT_ACCEPT event found in interrupt log.");
        }

        private static IEnumerable<byte> CompressBlock(byte[] data)
        {
            int index = 0;
            while (index < data.Length)
            {
                byte value = data[index];
                int runLength = 1;

                while (index + runLength < data.Length &&
                       data[index + runLength] == value &&
                       runLength < 255)
                {
                    runLength++;
                }

                bool shouldEncodeRun = runLength >= 5 || (value == 0xED && runLength >= 2);
                if (shouldEncodeRun)
                {
                    yield return 0xED;
                    yield return 0xED;
                    yield return (byte)runLength;
                    yield return value;
                    index += runLength;
                    continue;
                }

                yield return value;
                index++;

                if (value == 0xED && index < data.Length)
                {
                    yield return data[index];
                    index++;
                }
            }
        }
    }
}
