using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class UlaRasterCaptureTests
    {
        [Fact]
        public void ScreenBytes_AreLatchedAtTheirIndividualFetchTimes()
        {
            byte[][] banks = CreateBanks();
            int screenBank = 5;
            banks[5][0x0000] = 0x81;
            banks[5][0x1800] = 0x47;
            var capture = CreateCapture(UlaTimingProfile.Spectrum48K, banks, () => screenBank);

            capture.AdvanceTo(14338);
            banks[5][0x0000] = 0x00;
            capture.AdvanceTo(14339);
            banks[5][0x1800] = 0x00;
            capture.AdvanceTo(Spectrum128Machine.FrameTStates48);

            UlaFrame frame = Assert.IsType<UlaFrame>(capture.LastCompletedFrame);
            Assert.Equal((byte)0x81, frame.GetBitmapByte(0, 0));
            Assert.Equal((byte)0x47, frame.GetAttributeByte(0, 0));
        }

        [Fact]
        public void AttributeBytes_AreFetchedAgainForEachPixelLine()
        {
            byte[][] banks = CreateBanks();
            int screenBank = 5;
            banks[5][0x1800] = 0x01;
            var capture = CreateCapture(UlaTimingProfile.Spectrum48K, banks, () => screenBank);

            capture.AdvanceTo(14339);
            banks[5][0x1800] = 0x02;
            capture.AdvanceTo((ulong)14339 + 224);
            capture.AdvanceTo(Spectrum128Machine.FrameTStates48);

            UlaFrame frame = Assert.IsType<UlaFrame>(capture.LastCompletedFrame);
            Assert.Equal((byte)0x01, frame.GetAttributeByte(0, 0));
            Assert.Equal((byte)0x02, frame.GetAttributeByte(1, 0));
        }

        [Fact]
        public void ShadowScreenSwitch_AffectsOnlyFetchesAfterThePagingEvent()
        {
            byte[][] banks = CreateBanks();
            int screenBank = 5;
            banks[5][0x0000] = 0x80;
            banks[5][0x1800] = 0x01;
            banks[7][0x0001] = 0x40;
            banks[7][0x1801] = 0x02;
            var capture = CreateCapture(UlaTimingProfile.Spectrum128K, banks, () => screenBank);

            capture.AdvanceTo(14365);
            screenBank = 7;
            capture.AdvanceTo(14367);
            capture.AdvanceTo(Spectrum128Machine.FrameTStates128);

            UlaFrame frame = Assert.IsType<UlaFrame>(capture.LastCompletedFrame);
            Assert.Equal((byte)0x80, frame.GetBitmapByte(0, 0));
            Assert.Equal((byte)0x01, frame.GetAttributeByte(0, 0));
            Assert.Equal((byte)0x40, frame.GetBitmapByte(0, 1));
            Assert.Equal((byte)0x02, frame.GetAttributeByte(0, 1));
        }

        [Fact]
        public void CompletedFrame_OwnsItsMatchingBorderTimeline()
        {
            byte[][] banks = CreateBanks();
            int screenBank = 5;
            int borderColor = 1;
            var capture = new UlaRasterCapture(
                UlaTimingProfile.Spectrum48K,
                () => screenBank,
                () => borderColor,
                (bank, offset) => banks[bank][offset]);

            capture.AdvanceTo(0);
            capture.RecordBorderChange(12000, 4);
            borderColor = 4;
            capture.AdvanceTo(Spectrum128Machine.FrameTStates48);

            UlaFrame frame = Assert.IsType<UlaFrame>(capture.LastCompletedFrame);
            Assert.Equal(1, frame.BorderFrame.InitialColor);
            BorderEvent borderEvent = Assert.Single(frame.BorderFrame.Events);
            Assert.Equal(12000, borderEvent.TStateOffset);
            Assert.Equal(4, borderEvent.Color);
        }

        [Fact]
        public void FrameBuffer_RendersCapturedBytesInsteadOfFinalScreenRam()
        {
            byte[] bitmap = new byte[UlaFrame.CellCount];
            byte[] attributes = new byte[UlaFrame.CellCount];
            bitmap[0] = 0x80;
            attributes[0] = 0x02;
            var borderFrame = new BorderFrame(
                Spectrum128Machine.FrameTStates48,
                initialColor: 0,
                Array.Empty<BorderEvent>());
            var ulaFrame = new UlaFrame(
                Spectrum128Machine.FrameTStates48,
                bitmap,
                attributes,
                borderFrame);
            var frameBuffer = new SpectrumFrameBuffer();

            frameBuffer.Render(ulaFrame, flashPhase: false);

            int firstDisplayPixel =
                (Spectrum128Machine.BorderTopHeight * SpectrumFrameBuffer.Width) +
                Spectrum128Machine.BorderLeftWidth;
            Assert.Equal(
                SpectrumFrameBuffer.GetSpectrumColorArgb(2, bright: false),
                frameBuffer.Pixels.Span[firstDisplayPixel]);
            Assert.Equal(
                SpectrumFrameBuffer.GetSpectrumColorArgb(0, bright: false),
                frameBuffer.Pixels.Span[firstDisplayPixel + 1]);
        }

        [Fact]
        public void Machine_PublishesACompletedRasterFrameWithoutScreenWrites()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(SpectrumMachineModel.Spectrum48K);
                machine.PokeMemory(0x4000, 0x80);
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();

                UlaFrame frame = Assert.IsType<UlaFrame>(machine.LastCompletedUlaFrame);
                Assert.Equal((byte)0x80, frame.GetBitmapByte(0, 0));
                Assert.Equal((byte)0x38, frame.GetAttributeByte(0, 0));
            }
            finally
            {
                Directory.Delete(romFolder, recursive: true);
            }
        }

        [Fact]
        public void QuickState_RestoresTheCompletedAndInProgressRasterState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(SpectrumMachineModel.Spectrum48K);
                machine.PokeMemory(0x4000, 0x80);
                machine.Cpu.Regs.PC = 0x8000;
                machine.ExecuteFrame();
                Spectrum128Machine.QuickState saved = machine.CaptureQuickState();

                machine.PokeMemory(0x4000, 0x00);
                machine.ExecuteFrame();
                Assert.Equal((byte)0x00, machine.LastCompletedUlaFrame!.GetBitmapByte(0, 0));

                machine.RestoreQuickState(saved);

                Assert.Equal((byte)0x80, machine.LastCompletedUlaFrame!.GetBitmapByte(0, 0));
                machine.ExecuteFrame();
                Assert.Equal((byte)0x80, machine.LastCompletedUlaFrame!.GetBitmapByte(0, 0));
            }
            finally
            {
                Directory.Delete(romFolder, recursive: true);
            }
        }

        private static UlaRasterCapture CreateCapture(
            UlaTimingProfile timing,
            byte[][] banks,
            Func<int> getScreenBank) =>
            new UlaRasterCapture(
                timing,
                getScreenBank,
                () => 0,
                (bank, offset) => banks[bank][offset]);

        private static byte[][] CreateBanks()
        {
            var banks = new byte[8][];
            for (int bank = 0; bank < banks.Length; bank++)
                banks[bank] = new byte[0x4000];
            return banks;
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SpectrumUlaRaster-" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[0x4000]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[0x4000]);
            return folder;
        }
    }
}
