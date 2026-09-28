using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class QuickStateTests
    {
        [Fact]
        public void MachineQuickState_RestoresCpuPagingRamAyAndModel()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                for (byte bank = 0; bank < 8; bank++)
                {
                    machine.ForceApply7ffdValue(bank);
                    machine.PokeMemory(0xC000, (byte)(0x40 + bank));
                }

                machine.ForceApply7ffdValue(0x1E);
                machine.DebugWritePort(0xFFFD, 8);
                machine.DebugWritePort(0xBFFD, 0x0F);
                machine.Cpu.Regs.AF = 0x1234;
                machine.Cpu.Regs.BC = 0x5678;
                machine.Cpu.Regs.PC = 0x8123;
                machine.Cpu.Regs.SP = 0x9ABC;

                Spectrum128Machine.QuickState state = machine.CaptureQuickState();
                machine.Reset(SpectrumMachineModel.Spectrum48K);
                machine.RestoreQuickState(state);

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(6, machine.PagedRamBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.Equal(7, machine.ScreenBank);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.AF);
                Assert.Equal((ushort)0x5678, machine.Cpu.Regs.BC);
                Assert.Equal((ushort)0x8123, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x9ABC, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0x0F, machine.Ay.ReadRegister(8));
                for (int bank = 0; bank < 8; bank++)
                    Assert.Equal((byte)(0x40 + bank), machine.GetRamBankCopy(bank)[0]);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void MachineQuickState_ReplaysDeterministicallyFromCapturedInstructionBoundary()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.PokeMemory(0x8000, 0x3C); // INC A
                machine.PokeMemory(0x8001, 0x34); // INC (HL)
                machine.PokeMemory(0x8002, 0xC3); // JP 8000
                machine.PokeMemory(0x8003, 0x00);
                machine.PokeMemory(0x8004, 0x80);
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.HL = 0x9000;
                machine.Cpu.Regs.A = 7;
                machine.PokeMemory(0x9000, 11);

                Spectrum128Machine.QuickState state = machine.CaptureQuickState();
                machine.ExecuteTimeSlice(4000);
                (byte A, byte Memory, ushort PC, ulong TStates, int Frames) first =
                    (machine.Cpu.Regs.A, machine.PeekMemory(0x9000), machine.Cpu.Regs.PC,
                        machine.Cpu.TStates, machine.FrameCount);

                machine.RestoreQuickState(state);
                machine.ExecuteTimeSlice(4000);
                var second = (machine.Cpu.Regs.A, machine.PeekMemory(0x9000), machine.Cpu.Regs.PC,
                    machine.Cpu.TStates, machine.FrameCount);

                Assert.Equal(first, second);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void MountedTapeQuickState_RestoresExactPulseCursor()
        {
            var tape = new MountedTape(
                "quick-state.tap",
                new[] { TapeBlock.CreatePulseSequence(new[] { 100, 150, 200, 250 }) });

            Assert.True(tape.ReadEarBit(0));
            Assert.True(tape.ReadEarBit(75));
            MountedTape.QuickState state = tape.CaptureQuickState();
            bool[] first = { tape.ReadEarBit(100), tape.ReadEarBit(249), tape.ReadEarBit(250), tape.ReadEarBit(449) };

            tape.RestoreQuickState(state);
            bool[] second = { tape.ReadEarBit(100), tape.ReadEarBit(249), tape.ReadEarBit(250), tape.ReadEarBit(449) };

            Assert.Equal(first, second);
        }

        [Fact]
        public void RzxQuickState_RestoresFrameAndInputCursor()
        {
            var session = new RzxPlaybackSession(
                "quick-state.rzx",
                new[]
                {
                    new RzxFrame(10, new byte[] { 0x12, 0x34 }, false),
                    new RzxFrame(20, Array.Empty<byte>(), true)
                });

            Assert.True(session.TryBeginNextFrame(out _));
            Assert.True(session.TryReadPortValue(out byte firstByte));
            Assert.Equal((byte)0x12, firstByte);
            RzxPlaybackSession.QuickState state = session.CaptureQuickState();

            Assert.True(session.TryReadPortValue(out byte firstRunValue));
            Assert.True(session.TryBeginNextFrame(out ushort firstRunFetches));
            session.RestoreQuickState(state);
            Assert.True(session.TryReadPortValue(out byte secondRunValue));
            Assert.True(session.TryBeginNextFrame(out ushort secondRunFetches));

            Assert.Equal(firstRunValue, secondRunValue);
            Assert.Equal(firstRunFetches, secondRunFetches);
            Assert.Equal(2, session.FrameIndex);
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }
    }
}
