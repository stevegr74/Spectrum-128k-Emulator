using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SzxSnapshotLoaderTests
    {
        private const int RamBankSize = 16 * 1024;

        [Fact]
        public void Load48k_RestoresCpuMemoryUlaAndFramePhaseRegardlessOfChunkOrder()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] snapshot = BuildSnapshot(
                    machineId: 1,
                    ("FUTR", new byte[] { 1, 2, 3 }),
                    RamBlock(0, 0x30, compressed: false),
                    ("SPCR", BuildSpectrumBlock(border: 3, port7ffd: 0, portFe: 0x18)),
                    RamBlock(5, 0x10, compressed: false),
                    ("Z80R", BuildCpuBlock(frameCycles: 123, flags: 0x06)),
                    RamBlock(2, 0x20, compressed: false));

                var machine = new Spectrum128Machine(romFolder);
                SzxSnapshotLoader.Load(machine, snapshot);

                Assert.Equal(SpectrumMachineModel.Spectrum48K, machine.MachineModel);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.AF);
                Assert.Equal((ushort)0x5678, machine.Cpu.Regs.BC);
                Assert.Equal((ushort)0x9ABC, machine.Cpu.Regs.DE);
                Assert.Equal((ushort)0xDEF0, machine.Cpu.Regs.HL);
                Assert.Equal((ushort)0x1357, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x2468, machine.Cpu.Regs.SP);
                Assert.True(machine.Cpu.IFF1);
                Assert.False(machine.Cpu.IFF2);
                Assert.Equal(2, machine.Cpu.InterruptMode);
                Assert.True(machine.Cpu.IsHalted);
                Assert.Equal(123UL, machine.Cpu.TStates);
                Assert.Equal(3, machine.BorderColor);
                Assert.True(machine.SpeakerHigh);
                Assert.Equal((byte)0x10, machine.PeekMemory(0x4000));
                Assert.Equal((byte)0x20, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x30, machine.PeekMemory(0xC000));

                machine.ExecuteFrame();
                Assert.Equal(1, machine.FrameCount);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load128k_RestoresCompressedBanksPagingAndAyState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var chunks = new List<(string Id, byte[] Data)>
                {
                    ("Z80R", BuildCpuBlock(frameCycles: 456, flags: 0)),
                    ("SPCR", BuildSpectrumBlock(border: 6, port7ffd: 0x1F, portFe: 0x08)),
                    ("AY\0\0", BuildAyBlock(selectedRegister: 7))
                };
                for (int bank = 7; bank >= 0; bank--)
                    chunks.Add(RamBlock(bank, (byte)(0x40 + bank), compressed: true));

                var machine = new Spectrum128Machine(romFolder);
                SzxSnapshotLoader.Load(machine, BuildSnapshot(2, chunks.ToArray()));

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(7, machine.PagedRamBank);
                Assert.Equal(7, machine.ScreenBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.False(machine.PagingLocked);
                Assert.Equal(6, machine.BorderColor);
                Assert.Equal(456UL, machine.Cpu.TStates);
                for (int bank = 0; bank < 8; bank++)
                    Assert.Equal((byte)(0x40 + bank), machine.GetRamBankCopy(bank)[0]);

                Spectrum128Machine.QuickState state = machine.CaptureQuickState();
                Assert.Equal(7, state.AyState.SelectedRegister);
                byte[] registers = state.AyState.GetRegistersCopy();
                for (int register = 0; register < registers.Length; register++)
                    Assert.Equal((byte)(0x80 + register), registers[register]);

                machine.ExecuteFrame();
                Assert.Equal(1, machine.FrameCount);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RejectsTruncatedChunkBeforeMutatingMachine()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] snapshot = BuildSnapshot(1, ("Z80R", BuildCpuBlock(0, 0)));
                Array.Resize(ref snapshot, snapshot.Length - 1);
                var machine = new Spectrum128Machine(romFolder);
                machine.Cpu.Regs.PC = 0xBEEF;

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                    () => SzxSnapshotLoader.Load(machine, snapshot));

                Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal((ushort)0xBEEF, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(3, "machine id")]
        [InlineData(15, "machine id")]
        public void Load_RejectsUnsupportedMachineModels(byte machineId, string expectedMessage)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                NotSupportedException error = Assert.Throws<NotSupportedException>(
                    () => SzxSnapshotLoader.Load(machine, BuildSnapshot(machineId)));
                Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RejectsKnownUnsupportedPeripheralState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] snapshot = BuildSnapshot(1, ("MFCE", new byte[] { 0, 0 }));
                var machine = new Spectrum128Machine(romFolder);

                NotSupportedException error = Assert.Throws<NotSupportedException>(
                    () => SzxSnapshotLoader.Load(machine, snapshot));

                Assert.Contains("unsupported hardware", error.Message, StringComparison.OrdinalIgnoreCase);

                byte[] mouseSnapshot = BuildSnapshot(1, ("AMXM", new byte[] { 0 }));
                NotSupportedException mouseError = Assert.Throws<NotSupportedException>(
                    () => SzxSnapshotLoader.Load(machine, mouseSnapshot));
                Assert.Contains("unsupported hardware", mouseError.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RejectsMissingRequiredStateAndDuplicateRamPages()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                InvalidOperationException missing = Assert.Throws<InvalidOperationException>(
                    () => SzxSnapshotLoader.Load(machine, BuildSnapshot(1, ("SPCR", BuildSpectrumBlock(0, 0, 0)))));
                Assert.Contains("Z80R", missing.Message, StringComparison.Ordinal);

                byte[] duplicate = BuildSnapshot(
                    1,
                    ("Z80R", BuildCpuBlock(0, 0)),
                    ("SPCR", BuildSpectrumBlock(0, 0, 0)),
                    RamBlock(5, 0x11, compressed: false),
                    RamBlock(5, 0x22, compressed: true));
                InvalidOperationException repeated = Assert.Throws<InvalidOperationException>(
                    () => SzxSnapshotLoader.Load(machine, duplicate));
                Assert.Contains("duplicate RAM page 5", repeated.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RestoresRemainingInterruptRequestPulseDuration()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] snapshot = BuildSnapshot(
                    1,
                    ("Z80R", BuildCpuBlock(frameCycles: 321, flags: 0, holdInterruptCycles: 7)),
                    ("SPCR", BuildSpectrumBlock(0, 0, 0)),
                    RamBlock(5, 0x11, compressed: false),
                    RamBlock(2, 0x22, compressed: false),
                    RamBlock(0, 0x33, compressed: false));
                var machine = new Spectrum128Machine(romFolder);

                SzxSnapshotLoader.Load(machine, snapshot);

                Assert.True(machine.Cpu.InterruptPending);
                Spectrum128Machine.QuickState state = machine.CaptureQuickState();
                Assert.Equal(machine.Cpu.TStates + 7UL, state.InterruptPulseEndTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RestoresFirstInstructionInterruptSuppressionAndQState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] snapshot = BuildSnapshot(
                    1,
                    ("Z80R", BuildCpuBlock(frameCycles: 321, flags: 0x05, holdInterruptCycles: 7)),
                    ("SPCR", BuildSpectrumBlock(0, 0, 0)),
                    RamBlock(5, 0x00, compressed: false),
                    RamBlock(2, 0x00, compressed: false),
                    RamBlock(0, 0x00, compressed: false));
                var machine = new Spectrum128Machine(romFolder);

                SzxSnapshotLoader.Load(machine, snapshot);

                var restored = machine.Cpu.CaptureQuickState();
                Assert.True(restored.SuppressInterruptsForNextInstruction);
                Assert.Equal(machine.Cpu.Regs.F, restored.QFlags);
                Assert.True(machine.Cpu.InterruptPending);

                machine.Cpu.ExecuteInstructionFetches(1);

                var afterFirstInstruction = machine.Cpu.CaptureQuickState();
                Assert.False(afterFirstInstruction.SuppressInterruptsForNextInstruction);
                Assert.True(machine.Cpu.InterruptPending);
                Assert.Equal((ushort)0x1358, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Load_RejectsNonZeroReservedSpectrumState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                byte[] spectrum = BuildSpectrumBlock(0, 0, 0);
                spectrum[6] = 1;
                var machine = new Spectrum128Machine(romFolder);

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                    () => SzxSnapshotLoader.Load(machine, BuildSnapshot(1, ("SPCR", spectrum))));

                Assert.Contains("reserved", error.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[RamBankSize]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[RamBankSize]);
            return folder;
        }

        private static byte[] BuildSnapshot(byte machineId, params (string Id, byte[] Data)[] chunks)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes("ZXST"));
            writer.Write((byte)1);
            writer.Write((byte)5);
            writer.Write(machineId);
            writer.Write((byte)0);
            foreach ((string id, byte[] data) in chunks)
            {
                byte[] idBytes = Encoding.Latin1.GetBytes(id);
                Assert.Equal(4, idBytes.Length);
                writer.Write(idBytes);
                writer.Write((uint)data.Length);
                writer.Write(data);
            }
            return stream.ToArray();
        }

        private static byte[] BuildCpuBlock(uint frameCycles, byte flags, byte holdInterruptCycles = 0)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
            writer.Write((ushort)0x1234);
            writer.Write((ushort)0x5678);
            writer.Write((ushort)0x9ABC);
            writer.Write((ushort)0xDEF0);
            writer.Write((ushort)0x1122);
            writer.Write((ushort)0x3344);
            writer.Write((ushort)0x5566);
            writer.Write((ushort)0x7788);
            writer.Write((ushort)0xABCD);
            writer.Write((ushort)0xBCDE);
            writer.Write((ushort)0x2468);
            writer.Write((ushort)0x1357);
            writer.Write((byte)0x3F);
            writer.Write((byte)0x81);
            writer.Write((byte)1);
            writer.Write((byte)0);
            writer.Write((byte)2);
            writer.Write(frameCycles);
            writer.Write(holdInterruptCycles);
            writer.Write(flags);
            writer.Write((ushort)0xCAFE);
            return stream.ToArray();
        }

        private static byte[] BuildSpectrumBlock(byte border, byte port7ffd, byte portFe) =>
            new byte[] { border, port7ffd, 0, portFe, 0, 0, 0, 0 };

        private static byte[] BuildAyBlock(byte selectedRegister)
        {
            byte[] data = new byte[18];
            data[1] = selectedRegister;
            for (int register = 0; register < 16; register++)
                data[register + 2] = (byte)(0x80 + register);
            return data;
        }

        private static (string Id, byte[] Data) RamBlock(int bank, byte value, bool compressed)
        {
            byte[] page = new byte[RamBankSize];
            Array.Fill(page, value);
            byte[] payload = compressed ? Compress(page) : page;
            byte[] data = new byte[payload.Length + 3];
            data[0] = compressed ? (byte)1 : (byte)0;
            data[2] = (byte)bank;
            Buffer.BlockCopy(payload, 0, data, 3, payload.Length);
            return ("RAMP", data);
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(data);
            return output.ToArray();
        }
    }
}
