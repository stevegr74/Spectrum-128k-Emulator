using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class ExtendedZ80ModelSelectionTests
    {
        private const int RamBankSize = 16 * 1024;

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K, false)]
        [InlineData(SpectrumMachineModel.Spectrum128K, true)]
        public void Extended48kSnapshot_UsesExplicitlySelectedHardwareModel(
            SpectrumMachineModel selectedModel,
            bool expectsAyAudio)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                byte[] snapshot = BuildExtended48kSnapshot();

                Z80SnapshotLoader.Load(machine, snapshot, selectedModel);
                machine.DebugWritePort(0xFFFD, 0x07);
                machine.DebugWritePort(0xBFFD, 0b0011_1110);
                machine.DebugWritePort(0xFFFD, 0x08);
                machine.DebugWritePort(0xBFFD, 0x0F);
                machine.ExecuteTimeSlice(machine.FrameTStates);

                Assert.Equal(selectedModel, machine.MachineModel);
                Assert.Equal(expectsAyAudio, machine.HasAudibleOutput);
                Assert.True(machine.TryDequeueCompletedAudioFrame(out var frame));
                Assert.Equal(expectsAyAudio, frame.AyState != null);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static byte[] BuildExtended48kSnapshot()
        {
            var data = new List<byte>(32 + (3 * (RamBankSize + 3)));
            data.AddRange(new byte[30]);
            data[8] = 0x00;
            data[9] = 0xFF;
            data[27] = 1;
            data[28] = 1;
            data[29] = 1;

            const int additionalHeaderLength = 23;
            data.Add((byte)additionalHeaderLength);
            data.Add(0);
            var additional = new byte[additionalHeaderLength];
            additional[0] = 0x00;
            additional[1] = 0x80;
            additional[2] = 0;
            data.AddRange(additional);

            AddUncompressedPage(data, 8, 0x11);
            AddUncompressedPage(data, 4, 0x22);
            AddUncompressedPage(data, 5, 0x33);
            return data.ToArray();
        }

        private static void AddUncompressedPage(List<byte> data, byte page, byte fill)
        {
            data.Add(0xFF);
            data.Add(0xFF);
            data.Add(page);
            for (int i = 0; i < RamBankSize; i++)
                data.Add(fill);
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
