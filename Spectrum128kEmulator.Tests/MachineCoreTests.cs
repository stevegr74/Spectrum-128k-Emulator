using System;
using System.IO;
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
        public void ConfigureFor48kSnapshot_UsesBaselineFrameTiming()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
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

                Assert.InRange(after - before, (ulong)Spectrum128Machine.FrameTStates128, (ulong)Spectrum128Machine.FrameTStates128 + 32UL);
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

        [Fact(Skip = "Experimental 48K floating-bus model is currently rolled back to the pushed baseline.")]
        public void Unknown_Odd_Port_Read_Uses_Floating_Bus_During_48k_Display_Window()
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

                SetCpuTStates(machine, 14347);

                Assert.Equal((byte)0xA5, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K floating-bus model is currently rolled back to the pushed baseline.")]
        public void Unknown_Odd_Port_Read_Is_High_During_48k_Display_Idle_Phase()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.Set48kFloatingBusTimingAdjustments(displayStartAdjustTStates: 0, sampleAdjustTStates: 0);

                byte[] ram48 = new byte[48 * 1024];
                ram48[0] = 0xA5;
                ram48[0x1800] = 0x3C;
                ram48[1] = 0x5A;
                ram48[0x1801] = 0xC3;
                machine.Load48kSnapshotRam(ram48);

                SetCpuTStates(machine, 14348);
                Assert.Equal((byte)0x3C, machine.DebugReadPort(0xFFFF));

                SetCpuTStates(machine, 14349);
                Assert.Equal((byte)0x5A, machine.DebugReadPort(0xFFFF));

                SetCpuTStates(machine, 14350);
                Assert.Equal((byte)0xC3, machine.DebugReadPort(0xFFFF));

                SetCpuTStates(machine, 14351);
                Assert.Equal((byte)0xFF, machine.DebugReadPort(0xFFFF));
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

        [Fact(Skip = "Experimental 48K floating-bus model is currently rolled back to the pushed baseline.")]
        public void Floating_Bus_Display_Start_Adjust_Shifts_First_Byte_Window()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                byte[] ram48 = new byte[48 * 1024];
                ram48[0] = 0xA5;
                machine.Load48kSnapshotRam(ram48);
                machine.Set48kFloatingBusTimingAdjustments(displayStartAdjustTStates: -1, sampleAdjustTStates: 0);

                SetCpuTStates(machine, 14346);

                Assert.Equal((byte)0xA5, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K floating-bus model is currently rolled back to the pushed baseline.")]
        public void Floating_Bus_Sample_Adjust_Shifts_Timed_Odd_Port_Read()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                byte[] ram48 = new byte[48 * 1024];
                ram48[0] = 0xA5;
                ram48[0x1800] = 0x3C;
                machine.Load48kSnapshotRam(ram48);
                machine.Set48kFloatingBusTimingAdjustments(displayStartAdjustTStates: 0, sampleAdjustTStates: 1);

                SetCpuTStates(machine, 14347);

                Assert.Equal((byte)0x3C, machine.Cpu.ReadPortTimed!(0xFFFF, 0));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K contention model is currently rolled back to the pushed baseline.")]
        public void ClearDebugHistory_Does_Not_Reset_48k_Floating_Bus_Timing()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);

                byte[] ram48 = new byte[48 * 1024];
                ram48[0] = 0xA5;
                ram48[0x1800] = 0x3C;
                machine.Load48kSnapshotRam(ram48);

                machine.ClearDebugHistory();
                SetCpuTStates(machine, 14347);

                Assert.Equal((byte)0x3C, machine.Cpu.ReadPortTimed!(0xFFFF, 0));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K contention model is currently rolled back to the pushed baseline.")]
        public void Contended_48k_Memory_Read_Adds_Wait_States()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.Load48kSnapshotRam(new byte[48 * 1024]);
                SetCpuTStates(machine, 14335);

                ulong before = machine.Cpu.TStates;
                _ = machine.Cpu.ReadMemory!(0x4000);

                Assert.Equal(before + 6UL, machine.Cpu.TStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K contention model is currently rolled back to the pushed baseline.")]
        public void Even_Port_Read_With_Contended_High_Byte_Uses_C1_C3_Pattern()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                SetCpuTStates(machine, 14335);

                ulong before = machine.Cpu.TStates;
                _ = machine.Cpu.ReadPortTimed!(0x40FE, 0);

                Assert.Equal(before + 6UL, machine.Cpu.TStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Experimental 48K contention model is currently rolled back to the pushed baseline.")]
        public void Even_Port_Read_With_Uncontended_High_Byte_Uses_N1_C3_Pattern()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                SetCpuTStates(machine, 14335);

                ulong before = machine.Cpu.TStates;
                _ = machine.Cpu.ReadPortTimed!(0xFEFE, 0);

                Assert.Equal(before + 5UL, machine.Cpu.TStates);
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
    }
}
