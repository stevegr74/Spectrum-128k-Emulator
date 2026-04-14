using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SnapshotLoaderTests
    {
        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }

        [Fact]
        public void LoadSna48k_Restores_Registers_And_Memory()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "test.sna");

            try
            {
                byte[] data = new byte[27 + 49152];

                // Header
                data[0] = 0x3F; // I

                data[1] = 0x34; data[2] = 0x12; // HL'
                data[3] = 0x78; data[4] = 0x56; // DE'
                data[5] = 0xBC; data[6] = 0x9A; // BC'
                data[7] = 0xF0; data[8] = 0xDE; // AF'

                data[9] = 0x11; data[10] = 0x22; // HL
                data[11] = 0x33; data[12] = 0x44; // DE
                data[13] = 0x55; data[14] = 0x66; // BC

                data[15] = 0x88; data[16] = 0x77; // IY
                data[17] = 0xAA; data[18] = 0x99; // IX

                data[19] = 0x04; // IFF2 non-zero
                data[20] = 0x2B; // R

                data[21] = 0xCC; data[22] = 0xBB; // AF
                data[23] = 0x00; data[24] = 0xC0; // SP = 0xC000
                data[25] = 0x01; // IM 1
                data[26] = 0x05; // border

                // RAM dump starts at offset 27
                int ramOffset = 27;

                // Put a visible byte at 0x4000
                data[ramOffset + 0x0000] = 0x42;

                // Put PC on stack at 0xC000 (which is first byte of top 16K block)
                // 0xC000 corresponds to ramOffset + 0x8000
                data[ramOffset + 0x8000] = 0x34;
                data[ramOffset + 0x8001] = 0x12; // PC = 0x1234

                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                SnapshotLoader.LoadSna48k(machine, snapshotPath);

                Assert.Equal((byte)0x3F, machine.Cpu.Regs.I);
                Assert.Equal((byte)0x2B, machine.Cpu.Regs.R);

                Assert.Equal((byte)0x22, machine.Cpu.Regs.H);
                Assert.Equal((byte)0x11, machine.Cpu.Regs.L);

                Assert.Equal((byte)0x44, machine.Cpu.Regs.D);
                Assert.Equal((byte)0x33, machine.Cpu.Regs.E);

                Assert.Equal((byte)0x66, machine.Cpu.Regs.B);
                Assert.Equal((byte)0x55, machine.Cpu.Regs.C);

                Assert.Equal((ushort)0x7788, machine.Cpu.Regs.IY);
                Assert.Equal((ushort)0x99AA, machine.Cpu.Regs.IX);

                Assert.Equal((byte)0xBB, machine.Cpu.Regs.A);
                Assert.Equal((byte)0xCC, machine.Cpu.Regs.F);

                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xC002, machine.Cpu.Regs.SP);

                Assert.Equal((byte)0x42, machine.PeekMemory(0x4000));
                Assert.Equal(5, machine.BorderColor);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_Rejects_Non48k_File_Size()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "bad.sna");

            try
            {
                File.WriteAllBytes(snapshotPath, new byte[123]);

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() =>
                    SnapshotLoader.LoadSna48k(machine, snapshotPath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}