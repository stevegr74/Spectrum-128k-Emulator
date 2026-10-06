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

                data[19] = 0x04; // IFF2 bit set
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
        public void LoadSna48k_Applies_Default_Resume_Frame_Phase_For_General_48k_Snapshots()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "delay.sna");

            try
            {
                byte[] data = new byte[27 + 49152];
                data[19] = 0x04; // IFF2 bit set -> interrupts enabled after load
                data[23] = 0x00; data[24] = 0xC0; // SP = 0xC000
                data[25] = 0x01; // IM 1
                data[27 + 0x8000] = 0x34;
                data[27 + 0x8001] = 0x12; // PC = 0x1234
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                SnapshotLoader.LoadSna48k(machine, snapshotPath);

                Assert.Equal(
                    (ulong)Spectrum128Machine.Default48kSnapshotResumeFramePhase,
                    machine.Cpu.TStates);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_Ejects_Previously_Mounted_Tape()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new Tap.MountedTape(
                    "playing.tap",
                    new[] { Tap.TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) }));

                SnapshotLoader.LoadSna48k(machine, CreateMinimalSna48());

                Assert.False(machine.HasMountedTape);
                Assert.Equal(Tap.TapeTransportState.NoTape, machine.TapeTransportState);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_Restores_Iff1_And_Iff2_From_Iff2_Bit()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "exolon.sna");

            try
            {
                byte[] data = new byte[27 + 49152];
                data[19] = 0x04; // Only bit 2 should control IFF restore.
                data[23] = 0x00; data[24] = 0xC0;
                data[27 + 0x8000] = 0x34;
                data[27 + 0x8001] = 0x12;
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                SnapshotLoader.LoadSna48k(machine, snapshotPath);

                Assert.True(machine.Cpu.IFF1);
                Assert.True(machine.Cpu.IFF2);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_Ignores_NonControl_Bits_In_Iff2_Byte()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "flags.sna");

            try
            {
                byte[] data = new byte[27 + 49152];
                data[19] = 0x01; // Nonzero, but bit 2 is clear.
                data[23] = 0x00; data[24] = 0xC0;
                data[25] = 0x01;
                data[27 + 0x8000] = 0x34;
                data[27 + 0x8001] = 0x12;
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                SnapshotLoader.LoadSna48k(machine, snapshotPath);

                Assert.False(machine.Cpu.IFF1);
                Assert.False(machine.Cpu.IFF2);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_Uses_Default_Frame_Cadence_For_Other_Games()
        {
            string tempFolder = CreateTempRoms();
            string snapshotPath = Path.Combine(tempFolder, "other.sna");

            try
            {
                byte[] data = new byte[27 + 49152];
                data[23] = 0x00; data[24] = 0xC0;
                data[27 + 0x8000] = 0x34;
                data[27 + 0x8001] = 0x12;
                File.WriteAllBytes(snapshotPath, data);

                var machine = new Spectrum128Machine(tempFolder);
                SnapshotLoader.LoadSna48k(machine, snapshotPath);

                Assert.Equal(Spectrum128Machine.FrameTStates48, machine.FrameTStates);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna48k_CanUse128kHardwareFor48kFormatSnapshot()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);

                SnapshotLoader.LoadSna48k(
                    machine,
                    CreateMinimalSna48(),
                    SpectrumMachineModel.Spectrum128K);

                machine.DebugWritePort(0xFFFD, 0x07);
                machine.DebugWritePort(0xBFFD, 0b0011_1110);
                machine.DebugWritePort(0xFFFD, 0x08);
                machine.DebugWritePort(0xBFFD, 0x0F);

                machine.ExecuteTimeSlice(machine.FrameTStates);

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
                Assert.Equal((byte)0b0011_1110, machine.Ay.ReadRegister(7));
                Assert.True(machine.HasAudibleOutput);
                Assert.True(machine.TryDequeueCompletedAudioFrame(out var frame));
                Assert.NotNull(frame.AyState);
                Assert.NotNull(frame.InitialAyState);
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

        [Fact]
        public void Load_AutoDetects_Standard128kSna_And_Restores_All_Banks_And_Paging()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                const byte last7ffd = 0x3B; // bank 3, shadow screen, ROM 1, paging locked
                byte[] data = CreateSna128(last7ffd);
                data[0] = 0x3F;
                data[19] = 0x04;
                data[20] = 0x2B;
                data[21] = 0xCC;
                data[22] = 0xBB;
                data[23] = 0x34;
                data[24] = 0x12;
                data[25] = 0x02;
                data[26] = 0x06;

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new Tap.MountedTape(
                    "playing.tap",
                    new[] { Tap.TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) }));

                SnapshotLoader.Load(machine, data, SpectrumMachineModel.Spectrum48K);

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
                Assert.Equal((ushort)0x5678, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0x3F, machine.Cpu.Regs.I);
                Assert.Equal((byte)0x2B, machine.Cpu.Regs.R);
                Assert.Equal((byte)0xBB, machine.Cpu.Regs.A);
                Assert.Equal((byte)0xCC, machine.Cpu.Regs.F);
                Assert.True(machine.Cpu.IFF1);
                Assert.True(machine.Cpu.IFF2);
                Assert.Equal(2, machine.Cpu.InterruptMode);
                Assert.Equal(3, machine.PagedRamBank);
                Assert.Equal(7, machine.ScreenBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.True(machine.PagingLocked);
                Assert.Equal(6, machine.BorderColor);
                Assert.False(machine.HasMountedTape);
                Assert.Equal(Tap.TapeTransportState.NoTape, machine.TapeTransportState);

                for (int bank = 0; bank < 8; bank++)
                    Assert.Equal((byte)(0xA0 + bank), machine.GetRamBankCopy(bank)[0]);

                Assert.Equal((byte)0xA5, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0xA2, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0xA3, machine.PeekMemory(0xC000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        public void Load_Accepts_Larger128kSna_When_Current_Page_Duplicates_A_Fixed_Bank(int pagedBank)
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] data = CreateSna128((byte)pagedBank);
                var machine = new Spectrum128Machine(tempFolder);

                SnapshotLoader.Load(machine, data);

                Assert.Equal(147487, data.Length);
                Assert.Equal(pagedBank, machine.PagedRamBank);
                for (int bank = 0; bank < 8; bank++)
                    Assert.Equal((byte)(0xA0 + bank), machine.GetRamBankCopy(bank)[0]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna128k_Rejects_Layout_That_Does_Not_Match_Current_Page()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] data = CreateSna128(last7ffd: 3);
                Array.Resize(ref data, 147487);
                var machine = new Spectrum128Machine(tempFolder);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    SnapshotLoader.LoadSna128k(machine, data));

                Assert.Contains("paging metadata", exception.Message);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadSna128k_Rejects_Unsupported_TrDosRom_State()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] data = CreateSna128(last7ffd: 3);
                data[49182] = 1;
                var machine = new Spectrum128Machine(tempFolder);

                NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
                    SnapshotLoader.LoadSna128k(machine, data));

                Assert.Contains("TR-DOS", exception.Message);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        private static byte[] CreateMinimalSna48()
        {
            byte[] data = new byte[27 + 49152];
            data[19] = 0x04;
            data[23] = 0x00;
            data[24] = 0xC0;
            data[25] = 0x01;
            data[27 + 0x8000] = 0x34;
            data[27 + 0x8001] = 0x12;
            return data;
        }

        private static byte[] CreateSna128(byte last7ffd)
        {
            const int headerSize = 27;
            const int bankSize = 16384;
            const int extensionOffset = headerSize + (3 * bankSize);
            int pagedBank = last7ffd & 0x07;
            bool duplicateCurrentPage = pagedBank == 2 || pagedBank == 5;
            byte[] data = new byte[extensionOffset + 4 + ((duplicateCurrentPage ? 6 : 5) * bankSize)];

            WriteSnaBank(data, headerSize, 5);
            WriteSnaBank(data, headerSize + bankSize, 2);
            WriteSnaBank(data, headerSize + (2 * bankSize), pagedBank);

            data[extensionOffset] = 0x78;
            data[extensionOffset + 1] = 0x56;
            data[extensionOffset + 2] = last7ffd;
            data[extensionOffset + 3] = 0;

            bool[] written = new bool[8];
            written[5] = true;
            written[2] = true;
            written[pagedBank] = true;
            int target = extensionOffset + 4;
            for (int bank = 0; bank < written.Length; bank++)
            {
                if (written[bank])
                    continue;

                WriteSnaBank(data, target, bank);
                target += bankSize;
            }

            return data;
        }

        private static void WriteSnaBank(byte[] data, int offset, int bank)
        {
            Array.Fill(data, (byte)(0xA0 + bank), offset, 16384);
        }

    }
}
