using System;
using System.Collections.Generic;
using System.IO;
using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TapLoaderTests
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
        public void LoadTap_CodeBlock_Writes_Bytes_To_Target_Address()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "code.tap");

            try
            {
                byte[] code = new byte[] { 0x3E, 0x2A, 0x32, 0x00, 0x80 };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODEDEMO", dataLength: (ushort)code.Length, parameter1: 0x8000, parameter2: 32768),
                    BuildDataBlock(code));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoadResult result = TapLoader.Load(machine, tapePath);

                Assert.Equal(2, result.TotalBlockCount);
                Assert.Equal(1, result.LoadedBlockCount);
                Assert.Equal((byte)0x3E, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x2A, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x32, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8003));
                Assert.Equal((byte)0x80, machine.PeekMemory(0x8004));
                Assert.Equal((ushort)0x1555, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xFF58, machine.Cpu.Regs.SP);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.True(machine.PagingLocked);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadTap_BasicProgram_Loads_Program_And_Updates_System_Variables()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "basic.tap");

            try
            {
                byte[] basicAndVariables = new byte[]
                {
                    0x0A, 0x00, 0x04, 0x00, 0xF5, 0x0D,
                    0x14, 0x00, 0x04, 0x00, 0xF7, 0x0D,
                    0x80, 0xAA
                };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BASICDEMO", dataLength: (ushort)basicAndVariables.Length, parameter1: 10, parameter2: 12),
                    BuildDataBlock(basicAndVariables));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoadResult result = TapLoader.Load(machine, tapePath);

                Assert.Equal("BASICDEMO", result.AutoStartFileName);
                Assert.Equal((byte)0x0A, machine.PeekMemory(23755));
                Assert.Equal((byte)0xF7, machine.PeekMemory(23765));
                Assert.Equal((byte)0xAA, machine.PeekMemory(23768));
                Assert.Equal((ushort)23755, ReadWord(machine, 23635));
                Assert.Equal((ushort)(23755 + 12), ReadWord(machine, 23627));
                Assert.Equal((ushort)(23755 + basicAndVariables.Length), ReadWord(machine, 23641));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountTap_Exposes_Changing_Ear_Bit_On_Port_Fe()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "mounted.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "EARTEST", dataLength: 4, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x80, 0x00, 0xFF, 0x55 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapMountResult result = TapLoader.Mount(machine, tapePath);

                Assert.Equal(2, result.TotalBlockCount);
                Assert.True(machine.HasMountedTape);

                bool sawHigh = false;
                bool sawLow = false;
                for (int i = 0; i < 64; i++)
                {
                    bool earHigh = (machine.DebugReadPort(0x00FE) & 0x40) != 0;
                    sawHigh |= earHigh;
                    sawLow |= !earHigh;
                    machine.Cpu.AddTStates(2168);
                }

                Assert.True(sawHigh);
                Assert.True(sawLow);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Loads_Header_Block_And_Returns_To_Rom()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "romload.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "ROMTEST", dataLength: 5, parameter1: 0x8000, parameter2: 0x2222),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4, 5 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x9002, machine.Cpu.Regs.SP);
                Assert.Equal((ushort)0, machine.Cpu.Regs.DE);
                Assert.Equal((ushort)(0x8000 + 17), machine.Cpu.Regs.IX);
                Assert.Equal((byte)'R', machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Mismatch_Resets_Carry()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "rommismatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 1, 2, 3, 4 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 99;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, (byte)(machine.Cpu.Regs.F & 0x01));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadTap_Rejects_Data_Block_Without_Header()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bad.tap");

            try
            {
                File.WriteAllBytes(tapePath, BuildTap(BuildDataBlock(new byte[] { 1, 2, 3, 4 })));

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() => TapLoader.Load(machine, tapePath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }


        [Fact]
        public void MountedTap_RomTrap_Loads_Header_Then_Data_Sequentially()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "sequence.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "ONE", dataLength: 3, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3 }),
                    BuildHeaderBlock(type: 3, fileName: "TWO", dataLength: 2, parameter1: 0x8100, parameter2: 0),
                    BuildDataBlock(new byte[] { 4, 5 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8200;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8300;
                machine.Cpu.Regs.DE = 2;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)1, machine.PeekMemory(0x8100));
                Assert.Equal((byte)3, machine.PeekMemory(0x8102));
                Assert.Equal((byte)4, machine.PeekMemory(0x8300));
                Assert.Equal((byte)5, machine.PeekMemory(0x8301));
                Assert.False(machine.HasMountedTape && machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_Reset_Rewinds_Block_Sequence()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "reset.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildDataBlock(new byte[] { 9, 8, 7 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());
                Assert.False(machine.MountedTape!.HasRemainingBlocks);

                machine.MountedTape!.Reset();
                Assert.True(machine.MountedTape!.HasRemainingBlocks);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)9, machine.PeekMemory(0x8100));
                Assert.Equal((byte)7, machine.PeekMemory(0x8102));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_Header_With_Mismatched_Data_Length_Throws()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "badsequence.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "BAD", dataLength: 5, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.Throws<InvalidOperationException>(() => machine.TryServiceTapeTrap());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }



        [Fact]
        public void MountedTap_RomTrap_Verify_Match_Sets_Carry_Without_Writing()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "verifymatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 0x10, 0x20, 0x30, 0x40 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.PokeMemory(0x8000, 0x10);
                machine.PokeMemory(0x8001, 0x20);
                machine.PokeMemory(0x8002, 0x30);
                machine.PokeMemory(0x8003, 0x40);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x00;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
                Assert.Equal((byte)0x10, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x40, machine.PeekMemory(0x8003));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Verify_Mismatch_Resets_Carry()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "verifymismatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 0x10, 0x20, 0x30, 0x40 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.PokeMemory(0x8000, 0x10);
                machine.PokeMemory(0x8001, 0x20);
                machine.PokeMemory(0x8002, 0x31);
                machine.PokeMemory(0x8003, 0x40);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x00;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, (byte)(machine.Cpu.Regs.F & 0x01));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Loads_Leading_Basic_And_Starts_Tape_After_Consumed_Blocks()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bootstrap.tap");

            try
            {
                byte[] basicLoader = new byte[] { 0x0A, 0x00, 0x02, 0x00, 0xF7, 0x0D };
                byte[] codeLoader = new byte[] { 0x3E, 0x42, 0x32, 0x00, 0x90 };
                byte[] trailingData = new byte[] { 0x44, 0x55, 0x66 };
                byte[] header = BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: 12, parameter1: 10, parameter2: 12);
                byte[] tap = BuildTap(
                    header,
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)codeLoader.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(codeLoader),
                    BuildDataBlock(trailingData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(5, result.TotalBlockCount);
                Assert.Equal(4, result.ConsumedBlockCount);
                Assert.Equal("BOOT", result.AutoStartFileName);
                Assert.True(machine.HasMountedTape);
                Assert.True(machine.MountedTape!.HasRemainingBlocks);
                Assert.Equal((byte)0x0A, machine.PeekMemory(23755));
                Assert.Equal((byte)0xF7, machine.PeekMemory(23759));
                Assert.Equal((byte)0x3E, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x42, machine.PeekMemory(0x9001));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)trailingData.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((byte)0x44, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x55, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x66, machine.PeekMemory(0x8002));
                Assert.False(machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Rejects_NonBasic_Leader()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "nonbasicbootstrap.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 4, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() =>
                    TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static byte[] BuildTap(params byte[][] blocks)
        {
            var bytes = new List<byte>();
            foreach (byte[] block in blocks)
            {
                bytes.Add((byte)(block.Length & 0xFF));
                bytes.Add((byte)(block.Length >> 8));
                bytes.AddRange(block);
            }

            return bytes.ToArray();
        }

        private static byte[] BuildHeaderBlock(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
        {
            byte[] payload = new byte[19];
            payload[0] = 0x00;
            payload[1] = type;

            string paddedName = (fileName ?? string.Empty).PadRight(10).Substring(0, 10);
            for (int i = 0; i < 10; i++)
                payload[2 + i] = (byte)paddedName[i];

            payload[12] = (byte)(dataLength & 0xFF);
            payload[13] = (byte)(dataLength >> 8);
            payload[14] = (byte)(parameter1 & 0xFF);
            payload[15] = (byte)(parameter1 >> 8);
            payload[16] = (byte)(parameter2 & 0xFF);
            payload[17] = (byte)(parameter2 >> 8);
            payload[18] = ComputeChecksum(payload, 0, payload.Length - 1);
            return payload;
        }

        private static byte[] BuildDataBlock(byte[] data)
        {
            byte[] block = new byte[data.Length + 2];
            block[0] = 0xFF;
            Buffer.BlockCopy(data, 0, block, 1, data.Length);
            block[block.Length - 1] = ComputeChecksum(block, 0, block.Length - 1);
            return block;
        }

        private static byte ComputeChecksum(byte[] data, int offset, int count)
        {
            byte checksum = 0;
            for (int i = 0; i < count; i++)
                checksum ^= data[offset + i];

            return checksum;
        }
    }
}
