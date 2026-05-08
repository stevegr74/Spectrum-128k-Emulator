using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class RzxLoaderTests
    {
        [Fact]
        public void Load_With_Embedded_Sna_And_Input_Block_Attaches_Playback()
        {
            string romFolder = CreateTempRoms();
            string recordingPath = Path.Combine(romFolder, "test.rzx");

            try
            {
                byte[] sna = new byte[27 + (48 * 1024)];
                byte[] rzx = BuildRzxWithEmbeddedSna(sna, new ushort[] { 1 }, new byte[][] { new byte[] { 0xFE } });
                File.WriteAllBytes(recordingPath, rzx);

                var machine = new Spectrum128Machine(romFolder);
                RzxLoader.Load(machine, recordingPath);

                Assert.True(machine.HasRzxPlayback);
                Assert.Equal("test.rzx", machine.RzxPlaybackName);

                machine.ExecuteFrame();

                Assert.Equal(1, machine.FrameCount);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void PlaybackSession_Reuses_Last_Frame_Inputs_For_Repeated_Frames()
        {
            var session = new RzxPlaybackSession(
                "test.rzx",
                new[]
                {
                    new RzxFrame(10, new byte[] { 0x12, 0x34 }, false),
                    new RzxFrame(10, Array.Empty<byte>(), true)
                });

            Assert.True(session.TryBeginNextFrame(out ushort firstFetch));
            Assert.Equal((ushort)10, firstFetch);
            Assert.True(session.TryReadPortValue(out byte first0));
            Assert.True(session.TryReadPortValue(out byte first1));
            Assert.Equal((byte)0x12, first0);
            Assert.Equal((byte)0x34, first1);

            Assert.True(session.TryBeginNextFrame(out ushort secondFetch));
            Assert.Equal((ushort)10, secondFetch);
            Assert.True(session.TryReadPortValue(out byte second0));
            Assert.True(session.TryReadPortValue(out byte second1));
            Assert.Equal((byte)0x12, second0);
            Assert.Equal((byte)0x34, second1);
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }

        private static byte[] BuildRzxWithEmbeddedSna(byte[] sna, ushort[] frameFetchCounts, byte[][] frameInputs)
        {
            using var ms = new MemoryStream();
            ms.Write(System.Text.Encoding.ASCII.GetBytes("RZX!"));
            ms.WriteByte(0x00);
            ms.WriteByte(0x0D);
            ms.WriteByte(0x00);
            ms.WriteByte(0x00);
            ms.WriteByte(0x00);
            ms.WriteByte(0x00);

            byte[] snapshotBlock = BuildSnapshotBlock("SNA", sna);
            ms.Write(snapshotBlock, 0, snapshotBlock.Length);

            byte[] inputBlock = BuildInputBlock(frameFetchCounts, frameInputs);
            ms.Write(inputBlock, 0, inputBlock.Length);

            return ms.ToArray();
        }

        private static byte[] BuildSnapshotBlock(string extension, byte[] snapshot)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x30);
            ms.Write(new byte[4], 0, 4);
            ms.Write(new byte[4], 0, 4); // flags
            byte[] ext = new byte[4];
            System.Text.Encoding.ASCII.GetBytes(extension, 0, extension.Length, ext, 0);
            ms.Write(ext, 0, ext.Length);
            WriteDWord(ms, (uint)snapshot.Length);
            ms.Write(snapshot, 0, snapshot.Length);
            PatchBlockLength(ms);
            return ms.ToArray();
        }

        private static byte[] BuildInputBlock(ushort[] frameFetchCounts, byte[][] frameInputs)
        {
            using var payload = new MemoryStream();
            for (int i = 0; i < frameFetchCounts.Length; i++)
            {
                WriteWord(payload, frameFetchCounts[i]);
                WriteWord(payload, (ushort)frameInputs[i].Length);
                payload.Write(frameInputs[i], 0, frameInputs[i].Length);
            }

            using var ms = new MemoryStream();
            ms.WriteByte(0x80);
            ms.Write(new byte[4], 0, 4);
            WriteDWord(ms, (uint)frameFetchCounts.Length);
            ms.WriteByte(0x00);
            WriteDWord(ms, 0);
            WriteDWord(ms, 0);
            ms.Write(payload.ToArray(), 0, (int)payload.Length);
            PatchBlockLength(ms);
            return ms.ToArray();
        }

        private static void PatchBlockLength(MemoryStream stream)
        {
            long length = stream.Length;
            stream.Position = 1;
            WriteDWord(stream, (uint)length);
            stream.Position = length;
        }

        private static void WriteWord(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteDWord(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }
    }
}
