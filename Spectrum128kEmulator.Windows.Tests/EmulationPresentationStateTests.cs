using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class EmulationPresentationStateTests
    {
        [Fact]
        public void Capture_ReturnsIndependentScreenCopy()
        {
            string romFolder = CreateRomFolder();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.PokeMemory(0x4000, 0x5A);
                var state = new EmulationPresentationState();

                state.Publish(machine);
                Assert.True(state.TryCapture(out EmulationPresentationSnapshot first));
                first.ScreenBankData[0] = 0x00;

                Assert.True(state.TryCapture(out EmulationPresentationSnapshot second));
                Assert.Equal((byte)0x5A, second.ScreenBankData[0]);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void EmptyState_HasNoCapture()
        {
            var state = new EmulationPresentationState();

            Assert.False(state.HasValue);
            Assert.False(state.TryCapture(out _));
        }

        private static string CreateRomFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SpectrumPresentation-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[0x4000]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[0x4000]);
            return folder;
        }
    }
}
