using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Spectrum128kEmulator
{
    public static class RzxLoader
    {
        private const string Signature = "RZX!";
        private const byte SnapshotBlockId = 0x30;
        private const byte InputRecordingBlockId = 0x80;

        public static void Load(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Recording path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            if (fileData.Length < 10 || Encoding.ASCII.GetString(fileData, 0, 4) != Signature)
                throw new InvalidOperationException("Invalid .rzx file signature.");

            byte[]? snapshotData = null;
            string? snapshotExtension = null;
            var frames = new List<RzxFrame>();

            int offset = 10;
            while (offset < fileData.Length)
            {
                EnsureAvailable(fileData, offset, 5);
                byte blockId = fileData[offset];
                int blockLength = checked((int)ReadDWord(fileData, offset + 1));
                if (blockLength < 5)
                    throw new InvalidOperationException("Invalid .rzx block length.");
                EnsureAvailable(fileData, offset, blockLength);

                switch (blockId)
                {
                    case SnapshotBlockId:
                        ParseSnapshotBlock(fileData, offset, blockLength, out snapshotData, out snapshotExtension);
                        break;

                    case InputRecordingBlockId:
                        frames.AddRange(ParseInputRecordingBlock(fileData, offset, blockLength));
                        break;
                }

                offset += blockLength;
            }

            if (snapshotData == null || string.IsNullOrWhiteSpace(snapshotExtension))
                throw new InvalidOperationException("The .rzx file does not contain an embedded snapshot block.");
            if (frames.Count == 0)
                throw new InvalidOperationException("The .rzx file does not contain any input recording frames.");

            LoadEmbeddedSnapshot(machine, snapshotExtension, snapshotData);
            machine.AttachRzxPlayback(new RzxPlaybackSession(Path.GetFileName(path), frames));
        }

        private static void ParseSnapshotBlock(byte[] data, int offset, int blockLength, out byte[] snapshotData, out string snapshotExtension)
        {
            EnsureAvailable(data, offset + 5, 12);
            uint flags = ReadDWord(data, offset + 5);
            bool compressed = (flags & 0x00000002u) != 0;
            bool externalData = (flags & 0x00000001u) != 0;
            snapshotExtension = Encoding.ASCII.GetString(data, offset + 9, 4).TrimEnd('\0', ' ');
            int uncompressedLength = checked((int)ReadDWord(data, offset + 13));
            int payloadOffset = offset + 17;
            int payloadLength = blockLength - 17;
            EnsureAvailable(data, payloadOffset, payloadLength);

            if (externalData)
                throw new NotSupportedException("External-data .rzx snapshot blocks are not supported yet.");

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(data, payloadOffset, payload, 0, payload.Length);
            snapshotData = compressed
                ? Inflate(payload, uncompressedLength)
                : payload;
        }

        private static IReadOnlyList<RzxFrame> ParseInputRecordingBlock(byte[] data, int offset, int blockLength)
        {
            EnsureAvailable(data, offset + 5, 13);
            int frameCount = checked((int)ReadDWord(data, offset + 5));
            uint flags = ReadDWord(data, offset + 14);
            bool compressed = (flags & 0x00000002u) != 0;
            int payloadOffset = offset + 18;
            int payloadLength = blockLength - 18;
            EnsureAvailable(data, payloadOffset, payloadLength);

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(data, payloadOffset, payload, 0, payload.Length);
            if (compressed)
                payload = Inflate(payload, expectedLength: null);

            var frames = new List<RzxFrame>(frameCount);
            int cursor = 0;
            for (int i = 0; i < frameCount; i++)
            {
                EnsureAvailable(payload, cursor, 4);
                ushort instructionFetchCount = ReadWord(payload, cursor);
                ushort inCount = ReadWord(payload, cursor + 2);
                cursor += 4;

                if (inCount == 0xFFFF)
                {
                    frames.Add(new RzxFrame(instructionFetchCount, Array.Empty<byte>(), true));
                    continue;
                }

                EnsureAvailable(payload, cursor, inCount);
                byte[] inputs = new byte[inCount];
                Buffer.BlockCopy(payload, cursor, inputs, 0, inCount);
                cursor += inCount;
                frames.Add(new RzxFrame(instructionFetchCount, inputs, false));
            }

            return frames;
        }

        private static void LoadEmbeddedSnapshot(Spectrum128Machine machine, string extension, byte[] snapshotData)
        {
            if (extension.Equals("SNA", StringComparison.OrdinalIgnoreCase))
            {
                SnapshotLoader.LoadSna48k(machine, snapshotData);
                return;
            }

            if (extension.Equals("Z80", StringComparison.OrdinalIgnoreCase))
            {
                Z80SnapshotLoader.Load(machine, snapshotData);
                return;
            }

            throw new NotSupportedException($"Embedded .rzx snapshot type '{extension}' is not supported yet.");
        }

        private static byte[] Inflate(byte[] data, int? expectedLength)
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            byte[] inflated = output.ToArray();
            if (expectedLength.HasValue && inflated.Length != expectedLength.Value)
            {
                throw new InvalidOperationException(
                    $"Compressed .rzx data expanded to {inflated.Length} bytes instead of the expected {expectedLength.Value}.");
            }

            return inflated;
        }

        private static void EnsureAvailable(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
                throw new InvalidOperationException("The .rzx file is truncated.");
        }

        private static ushort ReadWord(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadDWord(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }
    }
}
