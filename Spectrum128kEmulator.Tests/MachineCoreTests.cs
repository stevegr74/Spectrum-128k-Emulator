using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class MachineCoreTests
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
        public void Keyboard_Is_Active_Low()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.SetKey(0, 0, true);

                byte portValue = machine.DebugReadPort(0xFEFE);
                Assert.Equal(0xFE, portValue & 0xFF);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ReadingKeyboardPort_Increments_Only_Selected_Row_Scan_Count()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                ulong row0Before = machine.GetKeyboardRowScanCount(0);
                ulong row1Before = machine.GetKeyboardRowScanCount(1);

                machine.DebugReadPort(0xFEFE);

                Assert.Equal(row0Before + 1, machine.GetKeyboardRowScanCount(0));
                Assert.Equal(row1Before, machine.GetKeyboardRowScanCount(1));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void CpuReset_Clears_QFlags_Latch()
        {
            var cpu = new Z80.Z80Cpu();
            FieldInfo qFlagsField = typeof(Z80.Z80Cpu).GetField("qFlags", BindingFlags.Instance | BindingFlags.NonPublic)!;

            qFlagsField.SetValue(cpu, (byte)0x28);
            cpu.Reset();
            Assert.Equal((byte)0x00, (byte)qFlagsField.GetValue(cpu)!);

            qFlagsField.SetValue(cpu, (byte)0x28);
            cpu.ClearSnapshotExecutionState();
            Assert.Equal((byte)0x00, (byte)qFlagsField.GetValue(cpu)!);
        }

        [Fact]
        public void CpuReset_Restores_General_Register_Defaults()
        {
            var cpu = new Z80.Z80Cpu();

            cpu.Regs.AF = 0x1234;
            cpu.Regs.BC = 0x5678;
            cpu.Regs.DE = 0x9ABC;
            cpu.Regs.HL = 0xDEF0;
            cpu.Regs.A_ = 1;
            cpu.Regs.F_ = 2;
            cpu.Regs.B_ = 3;
            cpu.Regs.C_ = 4;
            cpu.Regs.D_ = 5;
            cpu.Regs.E_ = 6;
            cpu.Regs.H_ = 7;
            cpu.Regs.L_ = 8;
            cpu.Regs.IX = 0x1111;
            cpu.Regs.IY = 0x2222;

            cpu.Reset();

            Assert.Equal((ushort)0xFFFF, cpu.Regs.AF);
            Assert.Equal((ushort)0x0000, cpu.Regs.BC);
            Assert.Equal((ushort)0x0000, cpu.Regs.DE);
            Assert.Equal((ushort)0x0000, cpu.Regs.HL);
            Assert.Equal((ushort)0xFFFF, cpu.Regs.IX);
            Assert.Equal((ushort)0xFFFF, cpu.Regs.IY);
            Assert.Equal((byte)0, cpu.Regs.A_);
            Assert.Equal((byte)0, cpu.Regs.F_);
            Assert.Equal((byte)0, cpu.Regs.B_);
            Assert.Equal((byte)0, cpu.Regs.C_);
            Assert.Equal((byte)0, cpu.Regs.D_);
            Assert.Equal((byte)0, cpu.Regs.E_);
            Assert.Equal((byte)0, cpu.Regs.H_);
            Assert.Equal((byte)0, cpu.Regs.L_);
        }

        [Fact]
        public void PendingMountedLoadInterpreterRefresh_Preserves_Original_BasicVariableArea()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                const ushort varsAddress = 0x5FAA;
                const ushort editLineAddress = varsAddress + 6;
                const ushort shrunkenEditLineAddress = 0x5CCC;
                const ushort shrunkenVarsAddress = 0x5CCB;

                WriteWord(machine, 23627, varsAddress);
                WriteWord(machine, 23641, editLineAddress);
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);

                machine.SetPendingMountedLoadUsrContinuationResolver(_ => null);

                WriteWord(machine, 23627, shrunkenVarsAddress);
                WriteWord(machine, 23641, shrunkenEditLineAddress);
                machine.RefreshPendingMountedLoadInterpreterContext();

                MethodInfo readVariableMethod = typeof(Tap.TapLoader).GetMethod(
                    "TryReadLiveBasicNumericVariable",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] args = new object?[] { machine, "a", 0 };

                bool found = (bool)readVariableMethod.Invoke(null, args)!;

                Assert.True(found);
                Assert.Equal(91, (int)args[2]!);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadInterpreterRefresh_Preserves_BasicVariableBytes_When_Live_Area_Is_Overwritten()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                const ushort varsAddress = 0x5FC0;
                const ushort editLineAddress = varsAddress + 6;

                WriteWord(machine, 23627, varsAddress);
                WriteWord(machine, 23641, editLineAddress);
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);

                machine.SetPendingMountedLoadUsrContinuationResolver(_ => null);

                for (ushort address = varsAddress; address < editLineAddress; address++)
                    machine.PokeMemory(address, 0x00);

                MethodInfo readVariableMethod = typeof(Tap.TapLoader).GetMethod(
                    "TryReadLiveBasicNumericVariable",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] args = new object?[] { machine, "a", 0 };

                bool found = (bool)readVariableMethod.Invoke(null, args)!;

                Assert.True(found);
                Assert.Equal(91, (int)args[2]!);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ReadingKeyboardPort_With_Multiple_Selected_Rows_Increments_Each_Selected_Row()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                ulong row0Before = machine.GetKeyboardRowScanCount(0);
                ulong row1Before = machine.GetKeyboardRowScanCount(1);
                ulong row2Before = machine.GetKeyboardRowScanCount(2);

                machine.DebugReadPort(0xFCFE);

                Assert.Equal(row0Before + 1, machine.GetKeyboardRowScanCount(0));
                Assert.Equal(row1Before + 1, machine.GetKeyboardRowScanCount(1));
                Assert.Equal(row2Before, machine.GetKeyboardRowScanCount(2));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static void WriteWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        [Fact]
        public void Paging_Port_Changes_Rom_And_Screen_Bank()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.DebugWritePort(0x7FFD, 0x18);

                Assert.Equal(0, machine.PagedRamBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.Equal(7, machine.ScreenBank);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Paging_Lock_Prevents_Further_Changes()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.DebugWritePort(0x7FFD, 0x20 | 0x03);
                machine.DebugWritePort(0x7FFD, 0x10 | 0x08 | 0x07);

                Assert.True(machine.PagingLocked);
                Assert.Equal(3, machine.PagedRamBank);
                Assert.Equal(0, machine.CurrentRomBank);
                Assert.Equal(5, machine.ScreenBank);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void FlashPhase_Toggles_Every_16_Frames()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                Assert.False(machine.FlashPhase);

                for (int i = 0; i < 15; i++)
                    machine.ExecuteFrame();

                Assert.False(machine.FlashPhase);

                machine.ExecuteFrame();
                Assert.True(machine.FlashPhase);

                for (int i = 0; i < 16; i++)
                    machine.ExecuteFrame();

                Assert.False(machine.FlashPhase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteFrame_Advances_At_Least_One_Frame_Of_TStates()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                ulong before = machine.Cpu.TStates;

                machine.ExecuteFrame();

                ulong after = machine.Cpu.TStates;

                Assert.InRange(after - before, (ulong)Spectrum128Machine.FrameTStates128, (ulong)Spectrum128Machine.FrameTStates128 + 32UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_Completes_Frame_Across_Multiple_Slices()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                int firstSlice = Spectrum128Machine.FrameTStates128 / 3;
                int secondSlice = Spectrum128Machine.FrameTStates128 - firstSlice;

                Assert.Equal(0, machine.ExecuteTimeSlice(firstSlice));
                Assert.Equal(0, machine.FrameCount);

                Assert.Equal(1, machine.ExecuteTimeSlice(secondSlice));
                Assert.Equal(1, machine.FrameCount);
                Assert.True(machine.TryDequeueCompletedAudioFrame(out _));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_FullFrameBudget_Completes_Frame_When_Instruction_Overshoots_FrameBoundary()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                int completedFrames = machine.ExecuteTimeSlice(Spectrum128Machine.FrameTStates128, out int executedTStates);

                Assert.Equal(1, completedFrames);
                Assert.Equal(1, machine.FrameCount);
                Assert.True(executedTStates >= Spectrum128Machine.FrameTStates128);
                Assert.True(machine.TryDequeueCompletedAudioFrame(out _));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_DoesNot_Queue_AudioFrames_When_AudioCapture_Is_Disabled()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.SetAudioFrameCaptureEnabled(false);

                Assert.Equal(1, machine.ExecuteTimeSlice(Spectrum128Machine.FrameTStates128));
                Assert.Equal(1, machine.FrameCount);
                Assert.False(machine.TryDequeueCompletedAudioFrame(out _));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_Queues_AudioFrames_Again_After_AudioCapture_Is_Reenabled()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.SetAudioFrameCaptureEnabled(false);
                machine.ExecuteTimeSlice(Spectrum128Machine.FrameTStates128);
                Assert.False(machine.TryDequeueCompletedAudioFrame(out _));

                machine.SetAudioFrameCaptureEnabled(true);
                Assert.Equal(1, machine.ExecuteTimeSlice(Spectrum128Machine.FrameTStates128));
                Assert.True(machine.TryDequeueCompletedAudioFrame(out _));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_Reports_ActualExecutedTStates()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                ulong before = machine.Cpu.TStates;

                int completedFrames = machine.ExecuteTimeSlice(1, out int executedTStates);

                Assert.InRange(completedFrames, 0, 1);
                Assert.True(executedTStates > 0);
                Assert.Equal((int)(machine.Cpu.TStates - before), executedTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_TinyBudgets_Accumulate_Without_Losing_ExecutedTStates()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                ulong before = machine.Cpu.TStates;
                int totalExecutedTStates = 0;
                int safety = 0;

                while (machine.FrameCount < 1 && safety++ < Spectrum128Machine.FrameTStates128)
                {
                    machine.ExecuteTimeSlice(1, out int executedTStates);
                    totalExecutedTStates += executedTStates;
                }

                Assert.Equal(1, machine.FrameCount);
                Assert.True(totalExecutedTStates >= Spectrum128Machine.FrameTStates128);
                Assert.Equal((int)(machine.Cpu.TStates - before), totalExecutedTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ConfigureFor48kSnapshot_UsesBaselineFrameTiming()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                Assert.Equal(Spectrum128Machine.FrameTStates48, machine.FrameTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ConfigureFor48kZ80Snapshot_UsesLegacyZ80FrameTiming()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kZ80Snapshot(borderColor: 0);

                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ConfigureFor48kSnapshot_DoesNot_Arm_A_Snapshot_Resume_Phase_By_Itself()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                Assert.Equal(0UL, machine.Cpu.TStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteFrame_UsesBaselineFrameTiming_WhenConfiguredFor48kSnapshot()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                ulong before = machine.Cpu.TStates;

                machine.ExecuteFrame();

                ulong after = machine.Cpu.TStates;

                Assert.InRange(after - before, (ulong)Spectrum128Machine.FrameTStates48, (ulong)Spectrum128Machine.FrameTStates48 + 32UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void DrainAudioFrame_Uses_Actual_Elapsed_TStates_For_Frame_Audio()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                machine.ExecuteFrame();
                ulong elapsed = machine.Cpu.TStates;
                var frame = machine.DrainAudioFrame();

                Assert.InRange(elapsed, (ulong)Spectrum128Machine.FrameTStates48, (ulong)Spectrum128Machine.FrameTStates48 + 32UL);
                Assert.Equal((int)elapsed, frame.FrameTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteFrame_Triggers_Interrupt_Immediately_By_Default()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.ClearRecentTrace();

                machine.ExecuteFrame();

                string[] events = machine.Cpu.GetRecentInterruptEventsSnapshot();
                Assert.Contains(events, line => line.Contains("T=         0") && line.Contains("INT_ACCEPT"));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void SetInitialInterruptDelay_Delays_First_Interrupt_And_Preserves_Phase()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.SetInitialInterruptDelay(512);
                machine.Cpu.ClearRecentTrace();

                machine.ExecuteFrame();
                string[] firstFrameEvents = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong firstAcceptTStates = ExtractFirstInterruptAcceptTStates(firstFrameEvents);
                Assert.InRange(firstAcceptTStates, 512UL, 516UL);

                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.ClearRecentTrace();
                machine.ExecuteFrame();
                string[] secondFrameEvents = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong secondAcceptTStates = ExtractFirstInterruptAcceptTStates(secondFrameEvents);
                Assert.InRange(
                    secondAcceptTStates - firstAcceptTStates,
                    (ulong)Spectrum128Machine.FrameTStates128,
                    (ulong)Spectrum128Machine.FrameTStates128 + 4UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void SetSnapshotInitialInterruptDelay_Delays_First_Interrupt_Then_Realigns_To_Frame_Boundary()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.SetSnapshotInitialInterruptDelay(32);
                machine.Cpu.ClearRecentTrace();

                machine.ExecuteFrame();
                string[] firstFrameEvents = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong firstAcceptTStates = ExtractFirstInterruptAcceptTStates(firstFrameEvents);
                Assert.InRange(firstAcceptTStates, 32UL, 36UL);

                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.ClearRecentTrace();
                machine.ExecuteFrame();
                string[] secondFrameEvents = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong secondAcceptTStates = ExtractFirstInterruptAcceptTStates(secondFrameEvents);
                Assert.InRange(
                    secondAcceptTStates - firstAcceptTStates,
                    (ulong)(Spectrum128Machine.FrameTStates48 - 32),
                    (ulong)(Spectrum128Machine.FrameTStates48 - 28));

                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.ClearRecentTrace();
                machine.ExecuteFrame();
                string[] thirdFrameEvents = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong thirdAcceptTStates = ExtractFirstInterruptAcceptTStates(thirdFrameEvents);
                Assert.InRange(
                    thirdAcceptTStates - secondAcceptTStates,
                    (ulong)Spectrum128Machine.FrameTStates48,
                    (ulong)Spectrum128Machine.FrameTStates48 + 4UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void SetSnapshotResumeFramePhase_Advances_TStates_And_Schedules_Next_Interrupt_From_Phase()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.SetSnapshotResumeFramePhase(Spectrum128Machine.Default48kSnapshotResumeFramePhase);

                Assert.Equal(
                    (ulong)Spectrum128Machine.Default48kSnapshotResumeFramePhase,
                    machine.Cpu.TStates);

                ulong before = machine.Cpu.TStates;
                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.ClearRecentTrace();
                machine.ExecuteFrame();

                string[] events = machine.Cpu.GetRecentInterruptEventsSnapshot();
                ulong firstAcceptTStates = ExtractFirstInterruptAcceptTStates(events);
                ulong expectedDelay = (ulong)(Spectrum128Machine.FrameTStates48 - Spectrum128Machine.Default48kSnapshotResumeFramePhase);
                Assert.InRange(firstAcceptTStates - before, expectedDelay, expectedDelay + 4UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Ay_Register_Select_And_Write_Via_Ports_Works()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                machine.DebugWritePort(0xFFFD, 0x07);
                machine.DebugWritePort(0xBFFD, 0xAB);

                Assert.Equal((byte)0x07, machine.Ay.CurrentRegister);
                Assert.Equal((byte)0xAB, machine.Ay.ReadRegister(7));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Ay_Register_Select_Is_Masked_To_Low_4_Bits_Via_Ports()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                machine.DebugWritePort(0xFFFD, 0x1F);
                machine.DebugWritePort(0xBFFD, 0x66);

                Assert.Equal((byte)0x0F, machine.Ay.CurrentRegister);
                Assert.Equal((byte)0x66, machine.Ay.ReadRegister(0x0F));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Unknown_Odd_Port_Read_Is_High_Outside_48k_Display_Window()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.Set48kFloatingBusTimingAdjustments(displayStartAdjustTStates: 0, sampleAdjustTStates: 0);

                byte[] ram48 = new byte[48 * 1024];
                ram48[0] = 0xA5;
                machine.Load48kSnapshotRam(ram48);

                SetCpuTStates(machine, 0);

                Assert.Equal((byte)0xFF, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteFrame_Advances_FrameCount_Predictably()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                for (int i = 0; i < 10; i++)
                    machine.ExecuteFrame();

                Assert.Equal(10, machine.FrameCount);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Speaker_High_Follows_Port_0xFE_Bit_4()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                machine.Cpu.WritePort!(0x00FE, 0x00);
                Assert.False(machine.SpeakerHigh);
                Assert.False(machine.SpeakerEdge);

                machine.Cpu.WritePort!(0x00FE, 0x10);
                Assert.True(machine.SpeakerHigh);
                Assert.True(machine.SpeakerEdge);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Speaker_Edge_Only_Sets_When_Bit_4_Changes()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                machine.Cpu.WritePort!(0x00FE, 0x10);
                Assert.True(machine.SpeakerHigh);
                Assert.True(machine.SpeakerEdge);

                machine.Cpu.WritePort!(0x00FE, 0x10);
                Assert.True(machine.SpeakerHigh);
                Assert.False(machine.SpeakerEdge);

                machine.Cpu.WritePort!(0x00FE, 0x00);
                Assert.False(machine.SpeakerHigh);
                Assert.True(machine.SpeakerEdge);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void KeyboardEarPort_Does_Not_Read_Mic_Bit_Back_As_High_By_Itself()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "low-ear",
                    new[]
                    {
                        Tap.TapeBlock.CreateSetSignalLevel(false),
                        Tap.TapeBlock.CreatePause(1)
                    }));

                machine.Cpu.WritePort!(0x00FE, 0x08);
                byte micOnly = machine.DebugReadPort(0x00FE);

                machine.Cpu.WritePort!(0x00FE, 0x10);
                byte speakerOnly = machine.DebugReadPort(0x00FE);

                Assert.Equal(0, micOnly & 0x40);
                Assert.Equal(0x40, speakerOnly & 0x40);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ExecuteTimeSlice_Advances_MountedTape_Pauses_Without_Ear_Reads()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "pause-progress",
                    new[]
                    {
                        Tap.TapeBlock.CreatePause(1000),
                        Tap.TapeBlock.CreatePureTone(2168, 32)
                    }));

                string before = machine.GetMountedTapeDebugState();
                Assert.Contains("EarBlock=0/2", before);
                Assert.Contains("EarState=Pause", before);

                machine.ExecuteTimeSlice(3_600_000);

                string after = machine.GetMountedTapeDebugState();
                Assert.Contains("EarBlock=1/2", after);
                Assert.Contains("EarState=PureTone", after);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Resumes_On_Usr_Return_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.BC);
                Assert.Equal((ushort)0xFEFE, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0x2B, machine.PeekMemory(0xFEFE));
                Assert.Equal((byte)0x2D, machine.PeekMemory(0xFEFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Does_Not_Resume_On_Rom_Load_Return_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "active-mounted",
                    new[]
                    {
                        Tap.TapeBlock.CreateData(new byte[] { 0xFF, 0xAA, 0x00 }, 2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    }));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x15FE;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.False(resumed);
                Assert.Equal((ushort)0x15FE, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Does_Not_Resume_During_Pause_Before_Remaining_RomLoadable_Block()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "paused-mounted",
                    new[]
                    {
                        Tap.TapeBlock.CreatePause(1000),
                        Tap.TapeBlock.CreateData(new byte[] { 0xFF, 0xAA, 0x00 }, 2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    }));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x15FE;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.False(resumed);
                Assert.Equal((ushort)0x15FE, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Does_Not_Resume_During_Pause_Before_Unstructured_Loadable_Standard_Block()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "paused-before-unstructured-standard",
                    new[]
                    {
                        Tap.TapeBlock.CreatePause(1000),
                        CreateUnstructuredLoadableStandardDataBlock(new byte[] { 0xFF, 0xAA, 0x00 }, pauseAfterBlockMs: 1000)
                    }));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x15FE;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.False(resumed);
                Assert.Equal((ushort)0x15FE, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Can_Resume_During_Pause_Before_Custom_NonRom_Block()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "paused-before-custom",
                    new[]
                    {
                        Tap.TapeBlock.CreatePause(1000),
                        Tap.TapeBlock.CreatePureTone(2168, 32)
                    }));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x15FE;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Can_Resume_On_Late_Rom_Callback_Return_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape(
                    "idle-mounted",
                    Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);
                machine.Cpu.Regs.PC = 0x1600;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Can_Resume_Rom_Basic_Execution()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadBasicResume(40, 3);
                machine.SetPendingMountedLoadUsrContinuation(0xFFFF);
                machine.Cpu.Regs.PC = 0x2D2B;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x1555, machine.Cpu.Regs.PC);
                Assert.Equal((byte)40, machine.PeekMemory(23618));
                Assert.Equal((byte)0, machine.PeekMemory(23619));
                Assert.Equal((byte)3, machine.PeekMemory(23620));
                Assert.Equal((byte)40, machine.PeekMemory(23621));
                Assert.Equal((byte)0, machine.PeekMemory(23622));
                Assert.Equal((byte)3, machine.PeekMemory(23623));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Resumes_From_Workspace_Callback_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuation(0x5CBB);
                machine.Cpu.Regs.PC = 0x5E87;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x5CBB, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x5CBB, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Resumes_From_Keyboard_Input_Callback_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuation(0x5CBB);
                machine.Cpu.Regs.PC = 0x10A8;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x5CBB, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x5CBB, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Requiring_UsrReturn_Does_Not_Resume_From_Rom_Service_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0x8000, requireUsrReturnAddress: true);
                machine.Cpu.Regs.PC = 0x1600;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.False(resumed);
                Assert.Equal((ushort)0x1600, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Requiring_UsrReturn_Resumes_On_Usr_Return_Path()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0x8000, requireUsrReturnAddress: true);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_RomBasicResume_Restores_Workspace_But_Preserves_Live_Channel_State()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                SetWord(machine, 23633, 0x5CB6);
                SetWord(machine, 23643, 0x5CCC);
                SetWord(machine, 23645, 0x5CCC);
                SetWord(machine, 23647, 0x00CC);
                SetWord(machine, 23641, 0x5CCC);
                SetWord(machine, 23649, 0x5CCE);
                SetWord(machine, 23651, 0x5CCE);
                SetWord(machine, 23653, 0x5CCE);

                machine.SetPendingMountedLoadBasicResume(40, 1);
                machine.SetPendingMountedLoadUsrContinuation(0xFFFF);

                SetWord(machine, 23633, 0x5CBB);
                SetWord(machine, 23643, 0x5FC3);
                SetWord(machine, 23645, 0x5FC3);
                SetWord(machine, 23647, 0x00F4);
                SetWord(machine, 23641, 0x5FC3);
                SetWord(machine, 23649, 0x5FC5);
                SetWord(machine, 23651, 0x5FC5);
                SetWord(machine, 23653, 0x5FC5);

                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.Cpu.Regs.PC = 0x2D2B;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x1555, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x5CBB, ReadWord(machine, 23633));
                Assert.Equal((ushort)0x5FC3, ReadWord(machine, 23643));
                Assert.Equal((ushort)0x5FC3, ReadWord(machine, 23645));
                Assert.Equal((ushort)0x00F4, ReadWord(machine, 23647));
                Assert.Equal((ushort)0x5FC3, ReadWord(machine, 23641));
                Assert.Equal((ushort)0x5FC5, ReadWord(machine, 23649));
                Assert.Equal((ushort)0x5FC5, ReadWord(machine, 23651));
                Assert.Equal((ushort)0x5FC5, ReadWord(machine, 23653));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_RomBasicResume_Preserves_Original_Basic_Area_Pointers()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                SetWord(machine, 23627, 0x5FAA);
                SetWord(machine, 23635, 0x5CCB);
                SetWord(machine, 23637, 0x5CCB);
                SetWord(machine, 23639, 0x5CCB);
                SetWord(machine, 23633, 0x5CB6);
                SetWord(machine, 23641, 0x5FC3);
                SetWord(machine, 23643, 0x5FC3);
                SetWord(machine, 23645, 0x5FC3);
                SetWord(machine, 23647, 0x00F4);
                SetWord(machine, 23649, 0x5FC5);
                SetWord(machine, 23651, 0x5FC5);
                SetWord(machine, 23653, 0x5FC5);

                machine.SetPendingMountedLoadBasicResume(40, 1);
                machine.SetPendingMountedLoadUsrContinuation(0xFFFF);

                SetWord(machine, 23627, 0x5CCB);
                SetWord(machine, 23635, 0x5CCB);
                SetWord(machine, 23637, 0x5CCB);
                SetWord(machine, 23639, 0x5CCB);
                SetWord(machine, 23633, 0x5CB6);
                SetWord(machine, 23641, 0x5CCC);
                SetWord(machine, 23643, 0x5CCC);
                SetWord(machine, 23645, 0x0000);
                SetWord(machine, 23647, 0x0000);
                SetWord(machine, 23649, 0x5CCE);
                SetWord(machine, 23651, 0x5CCE);
                SetWord(machine, 23653, 0x5CCE);

                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.Cpu.Regs.PC = 0x2D2B;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x1555, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x5FAA, ReadWord(machine, 23627));
                Assert.Equal((ushort)0x5CCB, ReadWord(machine, 23635));
                Assert.Equal((ushort)0x5CCB, ReadWord(machine, 23637));
                Assert.Equal((ushort)0x5CCB, ReadWord(machine, 23639));
                Assert.Equal((ushort)0x5CB6, ReadWord(machine, 23633));
                Assert.Equal((ushort)0x5CCC, ReadWord(machine, 23641));
                Assert.Equal((ushort)0x5CCC, ReadWord(machine, 23643));
                Assert.Equal((ushort)0x0000, ReadWord(machine, 23645));
                Assert.Equal((ushort)0x0000, ReadWord(machine, 23647));
                Assert.Equal((ushort)0x5CCE, ReadWord(machine, 23649));
                Assert.Equal((ushort)0x5CCE, ReadWord(machine, 23651));
                Assert.Equal((ushort)0x5CCE, ReadWord(machine, 23653));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_DirectUsr_Can_Preserve_Live_Interpreter_State()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                SetWord(machine, 23627, 0x5FAA);
                SetWord(machine, 23635, 0x5CCB);
                SetWord(machine, 23637, 0x5CCB);
                SetWord(machine, 23639, 0x5CCB);
                SetWord(machine, 23633, 0x5CB6);
                SetWord(machine, 23641, 0x5FC3);
                SetWord(machine, 23643, 0x5FC3);
                SetWord(machine, 23645, 0x5FC3);
                SetWord(machine, 23647, 0x00F4);
                SetWord(machine, 23649, 0x5FC5);
                SetWord(machine, 23651, 0x5FC5);
                SetWord(machine, 23653, 0x5FC5);

                machine.SetPendingMountedLoadUsrContinuation(0x8000);

                SetWord(machine, 23627, 0x0000);
                SetWord(machine, 23635, 0x0000);
                SetWord(machine, 23637, 0x0000);
                SetWord(machine, 23639, 0x0000);
                SetWord(machine, 23633, 0x1234);
                SetWord(machine, 23641, 0x9ABC);
                SetWord(machine, 23643, 0x5678);
                SetWord(machine, 23645, 0x2468);
                SetWord(machine, 23647, 0x1357);
                SetWord(machine, 23649, 0x1111);
                SetWord(machine, 23651, 0x2222);
                SetWord(machine, 23653, 0x3333);

                machine.SetPendingMountedLoadResumeCursorOverride(kCur: 0x5FC3, chAdd: 0x5FD0, xPtr: 0x5FD0);
                machine.SetPendingMountedLoadDirectUsrContextPolicy(preserveLiveInterpreterState: true);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.Cpu.Regs.PC = 0x2D2B;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.BC);
                Assert.Equal((ushort)0x1234, ReadWord(machine, 23633));
                Assert.Equal((ushort)0x9ABC, ReadWord(machine, 23641));
                Assert.Equal((ushort)0x5678, ReadWord(machine, 23643));
                Assert.Equal((ushort)0x2468, ReadWord(machine, 23645));
                Assert.Equal((ushort)0x1357, ReadWord(machine, 23647));
                Assert.Equal((ushort)0x1111, ReadWord(machine, 23649));
                Assert.Equal((ushort)0x2222, ReadWord(machine, 23651));
                Assert.Equal((ushort)0x3333, ReadWord(machine, 23653));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_UsrZero_Enters_48k_Mode()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor128kTapeLoad(borderColor: 3);
                machine.MountTape(new Tap.MountedTape("idle-mounted", Array.Empty<Tap.TapeBlock>()));
                machine.SetPendingMountedLoadUsrContinuation(0x0000);
                machine.Cpu.Regs.AF = 0x1234;
                machine.Cpu.Regs.BC = 0x5678;
                machine.Cpu.Regs.DE = 0x9ABC;
                machine.Cpu.Regs.HL = 0xDEF0;
                machine.Cpu.Regs.IX = 0x1111;
                machine.Cpu.Regs.IY = 0x2222;
                machine.Cpu.Regs.SP = 0x3333;
                machine.Cpu.Regs.I = 0x44;
                machine.Cpu.Regs.R = 0x55;
                machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
                machine.Cpu.InterruptPending = true;
                machine.Cpu.AdvanceTStates(1234);
                machine.Cpu.Regs.PC = 0x2D2B;

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;

                Assert.True(resumed);
                Assert.Equal((ushort)0x0000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xFFFF, machine.Cpu.Regs.AF);
                Assert.Equal((ushort)0x0000, machine.Cpu.Regs.DE);
                Assert.Equal((ushort)0x0000, machine.Cpu.Regs.HL);
                Assert.Equal((ushort)0xFFFF, machine.Cpu.Regs.IX);
                Assert.Equal((ushort)0xFFFF, machine.Cpu.Regs.IY);
                Assert.Equal((ushort)0xFFFF, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0, machine.Cpu.Regs.I);
                Assert.Equal((byte)0, machine.Cpu.Regs.R);
                Assert.False(machine.Cpu.IFF1);
                Assert.False(machine.Cpu.IFF2);
                Assert.False(machine.Cpu.InterruptPending);
                Assert.Equal((ulong)1234, machine.Cpu.TStates);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.False(machine.PagingLocked);
                Assert.Equal(0, machine.PagedRamBank);
                Assert.Equal(5, machine.ScreenBank);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
                Assert.Equal((ushort)0x5CBB, ReadWord(machine, 23633));
                Assert.Equal((ushort)0, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
                Assert.Equal((ushort)0, ReadWord(machine, 23621));
                Assert.Equal((byte)0, machine.PeekMemory(23623));
                machine.DebugWritePort(0x7FFD, 0x03);
                Assert.Equal(3, machine.PagedRamBank);
                Assert.False(machine.HasPendingMountedLoadUsrContinuation);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static void SetCpuTStates(Spectrum128Machine machine, ulong value)
        {
            PropertyInfo? property = typeof(Z80.Z80Cpu).GetProperty(
                nameof(Z80.Z80Cpu.TStates),
                BindingFlags.Instance | BindingFlags.Public);

            MethodInfo? setter = property?.GetSetMethod(nonPublic: true);
            if (setter == null)
                throw new InvalidOperationException("Unable to set CPU TStates for test.");

            setter.Invoke(machine.Cpu, new object[] { value });
        }

        private static void SetWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static Tap.TapeBlock CreateUnstructuredLoadableStandardDataBlock(byte[] streamData, ushort pauseAfterBlockMs)
        {
            Type tapeBlockType = typeof(Tap.TapeBlock);
            ConstructorInfo constructor = tapeBlockType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();

            return (Tap.TapeBlock)constructor.Invoke(new object?[]
            {
                Tap.TapeBlockKind.Data,
                true,
                false,
                (byte[])streamData.Clone(),
                null,
                (byte)0,
                (ushort)2168,
                (ushort)3223,
                (ushort)667,
                (ushort)735,
                (ushort)855,
                (ushort)1710,
                (byte)8,
                pauseAfterBlockMs,
                (ushort)0,
                (ushort)0,
                null,
                null,
                (ushort)0,
                null
            });
        }

        private static ulong ExtractFirstInterruptAcceptTStates(string[] events)
        {
            string acceptLine = events.First(line => line.Contains("INT_ACCEPT return="));
            int start = acceptLine.IndexOf("T=", StringComparison.Ordinal);
            int end = acceptLine.IndexOf(" PC=", StringComparison.Ordinal);
            if (start < 0 || end <= start + 2)
                throw new FormatException($"Unable to parse interrupt event line: {acceptLine}");

            return ulong.Parse(acceptLine[(start + 2)..end].Trim());
        }
    }
}
