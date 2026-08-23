using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Spectrum128kEmulator.Tap
{
    public static class TzxLoader
    {
        private const string Signature = "ZXTape!";
        private const ushort StandardPilotPulseLength = 2168;
        private const ushort StandardSyncFirstPulseLength = 667;
        private const ushort StandardSyncSecondPulseLength = 735;
        private const ushort StandardZeroBitPulseLength = 855;
        private const ushort StandardOneBitPulseLength = 1710;
        private const ushort StandardHeaderPilotPulseCount = 8063;
        private const ushort StandardDataPilotPulseCount = 3223;
        private const byte HeaderFlag = 0x00;
        private const int CpuClockHz48 = 3500000;

        public static TapMountResult Mount(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            var blocks = ParseBlocksForNewTapeLoad(File.ReadAllBytes(path));
            if (blocks.Count == 0)
                throw new InvalidOperationException("The .tzx file does not contain any supported tape blocks.");

            var tape = new MountedTape(
                Path.GetFileName(path),
                blocks,
                skipCustomHeaderForEarPlayback: false,
                initialEarLevelHigh: false);
            machine.MountTape(tape);
            return new TapMountResult(blocks.Count, Path.GetFileName(path));
        }

        public static TapeExecutionResult LoadWithPolicy(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            var blocks = ParseBlocksForNewTapeLoad(File.ReadAllBytes(path));
            string displayName = Path.GetFileName(path);
            TapeLoadPlan plan = TapLoader.CreateExecutionPlan(machine, blocks);
            return TapLoader.ExecutePlan(machine, displayName, blocks, plan, initialEarLevelHigh: false);
        }

        public static TapBootstrapResult BootstrapBasicProgramAndMountRemaining(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            var blocks = ParseBlocksForNewTapeLoad(File.ReadAllBytes(path));
            return TapLoader.BootstrapTapeBlocksAndMountRemaining(
                machine,
                Path.GetFileName(path),
                blocks,
                skipCustomHeaderForEarPlayback: false,
                initialEarLevelHigh: false);
        }

        public static TapBootstrapResult LoadAllStandardBlocksAndAutoStart(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            var blocks = ParseBlocksForNewTapeLoad(File.ReadAllBytes(path));
            return TapLoader.LoadAllStandardTapeBlocksAndAutoStart(
                machine,
                Path.GetFileName(path),
                blocks,
                skipCustomHeaderForEarPlayback: false,
                initialEarLevelHigh: false);
        }

        public static IReadOnlyList<TapeBlock> ParseBlocks(byte[] fileData)
        {
            return ParseBlocks(fileData, stopTapeIf48k: false);
        }

        internal static IReadOnlyList<TapeBlock> ParseBlocks(byte[] fileData, bool stopTapeIf48k)
        {
            if (fileData == null)
                throw new ArgumentNullException(nameof(fileData));
            if (fileData.Length < 10)
                throw new InvalidOperationException("The .tzx file is too short to contain a valid header.");
            if (Encoding.ASCII.GetString(fileData, 0, 7) != Signature || fileData[7] != 0x1A)
                throw new InvalidOperationException("Invalid .tzx file signature.");

            List<RawTzxBlock> rawBlocks = ParseRawBlocks(fileData);
            IReadOnlyList<TapeBlock> resolved = ResolveRawBlocks(rawBlocks, stopTapeIf48k);
            return resolved;
        }

        private static IReadOnlyList<TapeBlock> ParseBlocksForNewTapeLoad(byte[] fileData)
        {
            // A newly requested tape load should be evaluated from the tape image itself,
            // not from whatever 48K/128K mode the previous run happened to leave behind.
            // Public tape-load entry points therefore ignore "Stop if 48K" during the
            // initial parse and let the tape policy select the target load mode later.
            return PrepareBlocksForExecution(ParseBlocks(fileData, stopTapeIf48k: false));
        }

        internal static IReadOnlyList<TapeBlock> PrepareBlocksForExecution(IReadOnlyList<TapeBlock> blocks)
        {
            return NormalizeRomLoadableStandardDataBlocks(blocks);
        }

        private static IReadOnlyList<TapeBlock> NormalizeRomLoadableStandardDataBlocks(IReadOnlyList<TapeBlock> blocks)
        {
            return blocks;
        }

        private static List<RawTzxBlock> ParseRawBlocks(byte[] fileData)
        {
            var blocks = new List<RawTzxBlock>();
            int offset = 10;
            while (offset < fileData.Length)
            {
                byte blockId = fileData[offset++];
                switch (blockId)
                {
                    case 0x10:
                    {
                        EnsureAvailable(fileData, offset, 4);
                        ushort pauseMs = ReadWord(fileData, offset);
                        ushort dataLength = ReadWord(fileData, offset + 2);
                        offset += 4;
                        EnsureAvailable(fileData, offset, dataLength);
                        byte[] streamData = ReadBytes(fileData, offset, dataLength);
                        offset += dataLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateData(
                            streamData,
                            StandardPilotPulseLength,
                            streamData[0] == HeaderFlag ? StandardHeaderPilotPulseCount : StandardDataPilotPulseCount,
                            StandardSyncFirstPulseLength,
                            StandardSyncSecondPulseLength,
                            StandardZeroBitPulseLength,
                            StandardOneBitPulseLength,
                            usedBitsInLastByte: 8,
                            pauseAfterBlockMs: pauseMs)));
                        break;
                    }

                    case 0x11:
                    {
                        EnsureAvailable(fileData, offset, 18);
                        ushort pilotPulseLength = ReadWord(fileData, offset);
                        ushort syncFirst = ReadWord(fileData, offset + 2);
                        ushort syncSecond = ReadWord(fileData, offset + 4);
                        ushort zeroBit = ReadWord(fileData, offset + 6);
                        ushort oneBit = ReadWord(fileData, offset + 8);
                        ushort pilotCount = ReadWord(fileData, offset + 10);
                        byte usedBits = fileData[offset + 12];
                        ushort pauseMs = ReadWord(fileData, offset + 13);
                        int dataLength = Read24(fileData, offset + 15);
                        offset += 18;
                        EnsureAvailable(fileData, offset, dataLength);
                        byte[] streamData = ReadBytes(fileData, offset, dataLength);
                        offset += dataLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateData(
                            streamData,
                            pilotPulseLength,
                            pilotCount,
                            syncFirst,
                            syncSecond,
                            zeroBit,
                            oneBit,
                            usedBits,
                            pauseMs)));
                        break;
                    }

                    case 0x12:
                        EnsureAvailable(fileData, offset, 4);
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreatePureTone(ReadWord(fileData, offset), ReadWord(fileData, offset + 2))));
                        offset += 4;
                        break;

                    case 0x13:
                    {
                        EnsureAvailable(fileData, offset, 1);
                        int pulseCount = fileData[offset];
                        offset++;
                        EnsureAvailable(fileData, offset, pulseCount * 2);
                        int[] pulses = new int[pulseCount];
                        for (int i = 0; i < pulseCount; i++)
                            pulses[i] = ReadWord(fileData, offset + (i * 2));
                        offset += pulseCount * 2;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreatePulseSequence(pulses)));
                        break;
                    }

                    case 0x14:
                    {
                        EnsureAvailable(fileData, offset, 10);
                        ushort zeroBit = ReadWord(fileData, offset);
                        ushort oneBit = ReadWord(fileData, offset + 2);
                        byte usedBits = fileData[offset + 4];
                        ushort pauseMs = ReadWord(fileData, offset + 5);
                        int dataLength = Read24(fileData, offset + 7);
                        offset += 10;
                        EnsureAvailable(fileData, offset, dataLength);
                        byte[] streamData = ReadBytes(fileData, offset, dataLength);
                        offset += dataLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateByteStreamData(
                            streamData,
                            zeroBit,
                            oneBit,
                            usedBits,
                            pauseMs)));
                        break;
                    }

                    case 0x15:
                    {
                        EnsureAvailable(fileData, offset, 8);
                        ushort tStatesPerSample = ReadWord(fileData, offset);
                        ushort pauseMs = ReadWord(fileData, offset + 2);
                        byte usedBits = fileData[offset + 4];
                        int dataLength = Read24(fileData, offset + 5);
                        offset += 8;
                        EnsureAvailable(fileData, offset, dataLength);
                        byte[] sampleData = ReadBytes(fileData, offset, dataLength);
                        offset += dataLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateDirectRecording(sampleData, tStatesPerSample, usedBits, pauseMs)));
                        break;
                    }

                    case 0x18:
                    {
                        EnsureAvailable(fileData, offset, 14);
                        uint blockLength = ReadDWord(fileData, offset);
                        EnsureAvailable(fileData, offset, 4 + (int)blockLength);
                        ushort pauseMs = ReadWord(fileData, offset + 4);
                        int sampleRate = Read24(fileData, offset + 6);
                        byte compressionType = fileData[offset + 9];
                        uint storedPulseCount = ReadDWord(fileData, offset + 10);
                        int dataLength = (int)blockLength - 10;
                        byte[] cswData = ReadBytes(fileData, offset + 14, dataLength);
                        int[] pulseDurations = DecodeCswPulses(cswData, compressionType, sampleRate, storedPulseCount);
                        offset += 4 + (int)blockLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreatePulseSequence(pulseDurations, pauseMs)));
                        break;
                    }

                    case 0x19:
                    {
                        EnsureAvailable(fileData, offset, 4);
                        uint blockLength = ReadDWord(fileData, offset);
                        EnsureAvailable(fileData, offset, 4 + (int)blockLength);
                        byte[] blockData = ReadBytes(fileData, offset + 4, (int)blockLength);
                        offset += 4 + (int)blockLength;
                        blocks.Add(RawTzxBlock.FromTapeBlocks(ParseGeneralizedDataBlock(blockData)));
                        break;
                    }

                    case 0x20:
                        EnsureAvailable(fileData, offset, 2);
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreatePause(ReadWord(fileData, offset))));
                        offset += 2;
                        break;

                    case 0x21:
                        EnsureAvailable(fileData, offset, 1);
                        offset += 1 + fileData[offset];
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x22:
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x23:
                        EnsureAvailable(fileData, offset, 2);
                        blocks.Add(RawTzxBlock.CreateJump(ReadSignedWord(fileData, offset)));
                        offset += 2;
                        break;

                    case 0x24:
                        EnsureAvailable(fileData, offset, 2);
                        blocks.Add(RawTzxBlock.CreateLoopStart(ReadWord(fileData, offset)));
                        offset += 2;
                        break;

                    case 0x25:
                        blocks.Add(RawTzxBlock.CreateLoopEnd());
                        break;

                    case 0x26:
                    {
                        EnsureAvailable(fileData, offset, 2);
                        ushort count = ReadWord(fileData, offset);
                        offset += 2;
                        EnsureAvailable(fileData, offset, count * 2);
                        short[] offsets = new short[count];
                        for (int i = 0; i < count; i++)
                            offsets[i] = ReadSignedWord(fileData, offset + (i * 2));
                        offset += count * 2;
                        blocks.Add(RawTzxBlock.CreateCallSequence(offsets));
                        break;
                    }

                    case 0x27:
                        blocks.Add(RawTzxBlock.CreateReturn());
                        break;

                    case 0x28:
                    {
                        EnsureAvailable(fileData, offset, 2);
                        ushort length = ReadWord(fileData, offset);
                        EnsureAvailable(fileData, offset, 2 + length);
                        int cursor = offset + 2;
                        byte selectionCount = fileData[cursor++];
                        short[] offsets = new short[selectionCount];
                        for (int i = 0; i < selectionCount; i++)
                        {
                            offsets[i] = ReadSignedWord(fileData, cursor);
                            cursor += 2;
                            byte textLength = fileData[cursor++];
                            cursor += textLength;
                        }

                        offset += 2 + length;
                        blocks.Add(RawTzxBlock.CreateSelect(offsets));
                        break;
                    }

                    case 0x2A:
                        EnsureAvailable(fileData, offset, 4);
                        offset += 4;
                        blocks.Add(RawTzxBlock.CreateStopIf48k());
                        break;

                    case 0x2B:
                        EnsureAvailable(fileData, offset, 5);
                        uint signalLength = ReadDWord(fileData, offset);
                        if (signalLength != 1)
                            throw new InvalidOperationException("Invalid .tzx set signal level block length.");
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateSetSignalLevel(fileData[offset + 4] != 0)));
                        offset += 5;
                        break;

                    case 0x30:
                        EnsureAvailable(fileData, offset, 1);
                        offset += 1 + fileData[offset];
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x31:
                        EnsureAvailable(fileData, offset, 2);
                        offset += 2 + fileData[offset + 1];
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x32:
                    {
                        EnsureAvailable(fileData, offset, 2);
                        ushort blockLength = ReadWord(fileData, offset);
                        offset += 2 + blockLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;
                    }

                    case 0x33:
                        EnsureAvailable(fileData, offset, 1);
                        offset += 1 + (fileData[offset] * 3);
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x34:
                        EnsureAvailable(fileData, offset, 8);
                        offset += 8;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    case 0x35:
                    {
                        EnsureAvailable(fileData, offset, 20);
                        uint customLength = ReadDWord(fileData, offset + 16);
                        offset += 20 + (int)customLength;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;
                    }

                    case 0x5A:
                        EnsureAvailable(fileData, offset, 9);
                        offset += 9;
                        blocks.Add(RawTzxBlock.FromTapeBlock(TapeBlock.CreateMetadata()));
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported .tzx block 0x{blockId:X2}.");
                }
            }

            return blocks;
        }

        private static IReadOnlyList<TapeBlock> ResolveRawBlocks(IReadOnlyList<RawTzxBlock> rawBlocks, bool stopTapeIf48k)
        {
            var resolved = new List<TapeBlock>();
            var loopFrames = new Stack<LoopFrame>();
            var callFrames = new Stack<CallFrame>();
            int index = 0;
            int stepsRemaining = Math.Max(1024, rawBlocks.Count * 256);

            while (index >= 0 && index < rawBlocks.Count)
            {
                if (stepsRemaining-- <= 0)
                    throw new InvalidOperationException("TZX control flow could not be resolved safely.");

                RawTzxBlock block = rawBlocks[index];
                switch (block.Kind)
                {
                    case RawTzxBlockKind.TapeBlocks:
                        resolved.AddRange(block.TapeBlocks!);
                        index++;
                        break;

                    case RawTzxBlockKind.Jump:
                        index = ResolveRelativeIndex(index, block.RelativeOffset);
                        break;

                    case RawTzxBlockKind.LoopStart:
                        loopFrames.Push(new LoopFrame(index + 1, block.RepetitionCount));
                        index++;
                        break;

                    case RawTzxBlockKind.LoopEnd:
                        if (loopFrames.Count == 0)
                        {
                            index++;
                            break;
                        }

                        LoopFrame loop = loopFrames.Pop();
                        if (loop.RemainingCount > 1)
                        {
                            loopFrames.Push(new LoopFrame(loop.StartIndex, (ushort)(loop.RemainingCount - 1)));
                            index = loop.StartIndex;
                        }
                        else
                        {
                            index++;
                        }
                        break;

                    case RawTzxBlockKind.CallSequence:
                        if (block.CallOffsets == null || block.CallOffsets.Length == 0)
                        {
                            index++;
                            break;
                        }

                        callFrames.Push(new CallFrame(index + 1, index, block.CallOffsets, 0));
                        index = ResolveRelativeIndex(index, block.CallOffsets[0]);
                        break;

                    case RawTzxBlockKind.Return:
                        if (callFrames.Count == 0)
                        {
                            index++;
                            break;
                        }

                        CallFrame call = callFrames.Pop();
                        int nextCallIndex = call.NextOffsetIndex + 1;
                        if (nextCallIndex < call.RelativeOffsets.Length)
                        {
                            callFrames.Push(new CallFrame(call.ReturnIndex, call.CallBlockIndex, call.RelativeOffsets, nextCallIndex));
                            index = ResolveRelativeIndex(call.CallBlockIndex, call.RelativeOffsets[nextCallIndex]);
                        }
                        else
                        {
                            index = call.ReturnIndex;
                        }
                        break;

                    case RawTzxBlockKind.Select:
                        if (block.CallOffsets == null || block.CallOffsets.Length == 0)
                        {
                            index++;
                            break;
                        }

                        index = ResolveRelativeIndex(index, block.CallOffsets[0]);
                        break;

                    case RawTzxBlockKind.StopIf48k:
                        if (stopTapeIf48k)
                            return resolved;

                        index++;
                        break;

                    default:
                        index++;
                        break;
                }
            }

            return resolved;
        }

        private static int ResolveRelativeIndex(int currentIndex, short relativeOffset)
        {
            return currentIndex + relativeOffset;
        }

        private static List<TapeBlock> ParseGeneralizedDataBlock(byte[] blockData)
        {
            int offset = 0;
            ushort pauseAfterBlockMs = ReadWord(blockData, offset);
            uint totalPilotSymbols = ReadDWord(blockData, offset + 2);
            byte maxPilotPulses = blockData[offset + 6];
            int pilotAlphabetSize = blockData[offset + 7];
            if (pilotAlphabetSize == 0)
                pilotAlphabetSize = 256;

            uint totalDataSymbols = ReadDWord(blockData, offset + 8);
            byte maxDataPulses = blockData[offset + 12];
            int dataAlphabetSize = blockData[offset + 13];
            if (dataAlphabetSize == 0)
                dataAlphabetSize = 256;
            offset += 14;

            SymbolDefinition[] pilotAlphabet = Array.Empty<SymbolDefinition>();
            if (totalPilotSymbols > 0)
            {
                pilotAlphabet = ReadSymbolDefinitions(blockData, ref offset, pilotAlphabetSize, maxPilotPulses);
            }

            var blocks = new List<TapeBlock>();
            if (totalPilotSymbols > 0)
            {
                for (uint i = 0; i < totalPilotSymbols; i++)
                {
                    EnsureAvailable(blockData, offset, 3);
                    byte symbolIndex = blockData[offset++];
                    ushort repetitions = ReadWord(blockData, offset);
                    offset += 2;
                    if (symbolIndex >= pilotAlphabet.Length)
                        throw new InvalidOperationException("Generalized-data pilot symbol index is out of range.");

                    for (int repeat = 0; repeat < repetitions; repeat++)
                        AppendGeneralizedSymbol(blocks, pilotAlphabet[symbolIndex], pauseAfterBlockMs: 0);
                }
            }

            SymbolDefinition[] dataAlphabet = Array.Empty<SymbolDefinition>();
            if (totalDataSymbols > 0)
                dataAlphabet = ReadSymbolDefinitions(blockData, ref offset, dataAlphabetSize, maxDataPulses);

            if (totalDataSymbols > 0)
            {
                int bitsPerSymbol = BitsRequired(dataAlphabetSize - 1);
                int dataBytesLength = (int)((totalDataSymbols * (uint)bitsPerSymbol + 7U) / 8U);
                EnsureAvailable(blockData, offset, dataBytesLength);
                var reader = new BitReader(blockData, offset, dataBytesLength);
                for (uint i = 0; i < totalDataSymbols; i++)
                {
                    int symbolIndex = reader.ReadBits(bitsPerSymbol);
                    if (symbolIndex < 0 || symbolIndex >= dataAlphabet.Length)
                        throw new InvalidOperationException("Generalized-data data symbol index is out of range.");

                    AppendGeneralizedSymbol(blocks, dataAlphabet[symbolIndex], pauseAfterBlockMs: i == totalDataSymbols - 1 ? pauseAfterBlockMs : (ushort)0);
                }

                offset += dataBytesLength;
            }
            else if (blocks.Count > 0 && pauseAfterBlockMs != 0)
            {
                blocks.Add(TapeBlock.CreatePause(pauseAfterBlockMs));
            }

            return blocks;
        }

        private static SymbolDefinition[] ReadSymbolDefinitions(byte[] data, ref int offset, int alphabetSize, int maxPulseCount)
        {
            var definitions = new SymbolDefinition[alphabetSize];
            for (int symbolIndex = 0; symbolIndex < alphabetSize; symbolIndex++)
            {
                EnsureAvailable(data, offset, 1 + (maxPulseCount * 2));
                byte flags = data[offset++];
                int[] pulses = new int[maxPulseCount];
                for (int pulseIndex = 0; pulseIndex < maxPulseCount; pulseIndex++)
                {
                    pulses[pulseIndex] = ReadWord(data, offset);
                    offset += 2;
                }

                definitions[symbolIndex] = new SymbolDefinition(flags, pulses);
            }

            return definitions;
        }

        private static void AppendGeneralizedSymbol(List<TapeBlock> blocks, SymbolDefinition symbol, ushort pauseAfterBlockMs)
        {
            int[] pulses = TrimTrailingZeroPulses(symbol.Pulses);
            if (pulses.Length == 0)
            {
                if (pauseAfterBlockMs != 0)
                    blocks.Add(TapeBlock.CreatePause(pauseAfterBlockMs));
                return;
            }

            int startMode = symbol.Flags & 0x03;
            if (startMode == 0x01)
                throw new NotSupportedException("Generalized-data symbols that prolong the current level without an edge are not supported yet.");

            if (startMode == 0x02 || startMode == 0x03)
                blocks.Add(TapeBlock.CreateSetSignalLevel(startMode == 0x03));

            blocks.Add(TapeBlock.CreatePulseSequence(pulses, pauseAfterBlockMs));
        }

        private static int[] TrimTrailingZeroPulses(int[] pulses)
        {
            int length = pulses.Length;
            while (length > 0 && pulses[length - 1] == 0)
                length--;

            if (length == pulses.Length)
                return (int[])pulses.Clone();

            int[] trimmed = new int[length];
            Array.Copy(pulses, trimmed, length);
            return trimmed;
        }

        private static int[] DecodeCswPulses(byte[] cswData, byte compressionType, int sampleRate, uint expectedPulseCount)
        {
            byte[] decoded = compressionType switch
            {
                0x01 => cswData,
                0x02 => Inflate(cswData),
                _ => throw new NotSupportedException($"Unsupported CSW compression type 0x{compressionType:X2}.")
            };

            var pulses = new List<int>();
            int offset = 0;
            while (offset < decoded.Length)
            {
                uint sampleCount = decoded[offset++];
                if (sampleCount == 0)
                {
                    if (offset + 4 > decoded.Length)
                        throw new InvalidOperationException("The CSW pulse stream is truncated.");

                    sampleCount = ReadDWord(decoded, offset);
                    offset += 4;
                }

                int tStates = ConvertSamplesToTStates(sampleCount, sampleRate);
                if (tStates > 0)
                    pulses.Add(tStates);
            }

            if (expectedPulseCount != 0 && pulses.Count != expectedPulseCount)
                throw new InvalidOperationException("CSW pulse count validation failed.");

            return pulses.ToArray();
        }

        private static byte[] Inflate(byte[] data)
        {
            using var source = new MemoryStream(data);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static int ConvertSamplesToTStates(uint sampleCount, int sampleRate)
        {
            if (sampleRate <= 0)
                throw new InvalidOperationException("Invalid CSW sample rate.");

            return (int)Math.Round(sampleCount * (double)CpuClockHz48 / sampleRate, MidpointRounding.AwayFromZero);
        }

        private static int BitsRequired(int maximumValue)
        {
            if (maximumValue <= 0)
                return 1;

            int bits = 0;
            while (maximumValue > 0)
            {
                bits++;
                maximumValue >>= 1;
            }

            return bits;
        }

        private static byte[] ReadBytes(byte[] data, int offset, int length)
        {
            var bytes = new byte[length];
            Buffer.BlockCopy(data, offset, bytes, 0, length);
            return bytes;
        }

        private static void EnsureAvailable(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
                throw new InvalidOperationException("The .tzx file is truncated.");
        }

        private static ushort ReadWord(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static short ReadSignedWord(byte[] data, int offset)
        {
            return unchecked((short)ReadWord(data, offset));
        }

        private static int Read24(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        }

        private static uint ReadDWord(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        private enum RawTzxBlockKind
        {
            TapeBlocks,
            Jump,
            LoopStart,
            LoopEnd,
            CallSequence,
            Return,
            Select,
            StopIf48k
        }

        private sealed class RawTzxBlock
        {
            private RawTzxBlock(RawTzxBlockKind kind)
            {
                Kind = kind;
            }

            public RawTzxBlockKind Kind { get; }
            public IReadOnlyList<TapeBlock>? TapeBlocks { get; private init; }
            public short RelativeOffset { get; private init; }
            public ushort RepetitionCount { get; private init; }
            public short[]? CallOffsets { get; private init; }

            public static RawTzxBlock FromTapeBlock(TapeBlock block) => new RawTzxBlock(RawTzxBlockKind.TapeBlocks) { TapeBlocks = new[] { block } };
            public static RawTzxBlock FromTapeBlocks(IReadOnlyList<TapeBlock> blocks) => new RawTzxBlock(RawTzxBlockKind.TapeBlocks) { TapeBlocks = blocks };
            public static RawTzxBlock CreateJump(short relativeOffset) => new RawTzxBlock(RawTzxBlockKind.Jump) { RelativeOffset = relativeOffset };
            public static RawTzxBlock CreateLoopStart(ushort repetitionCount) => new RawTzxBlock(RawTzxBlockKind.LoopStart) { RepetitionCount = repetitionCount };
            public static RawTzxBlock CreateLoopEnd() => new RawTzxBlock(RawTzxBlockKind.LoopEnd);
            public static RawTzxBlock CreateCallSequence(short[] offsets) => new RawTzxBlock(RawTzxBlockKind.CallSequence) { CallOffsets = offsets };
            public static RawTzxBlock CreateReturn() => new RawTzxBlock(RawTzxBlockKind.Return);
            public static RawTzxBlock CreateSelect(short[] offsets) => new RawTzxBlock(RawTzxBlockKind.Select) { CallOffsets = offsets };
            public static RawTzxBlock CreateStopIf48k() => new RawTzxBlock(RawTzxBlockKind.StopIf48k);
        }

        private readonly record struct LoopFrame(int StartIndex, ushort RemainingCount);
        private readonly record struct CallFrame(int ReturnIndex, int CallBlockIndex, short[] RelativeOffsets, int NextOffsetIndex);
        private readonly record struct SymbolDefinition(byte Flags, int[] Pulses);

        private sealed class BitReader
        {
            private readonly byte[] data;
            private readonly int start;
            private readonly int length;
            private int bitPosition;

            public BitReader(byte[] data, int start, int length)
            {
                this.data = data;
                this.start = start;
                this.length = length;
            }

            public int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++)
                {
                    int absoluteBit = bitPosition++;
                    int byteIndex = absoluteBit / 8;
                    if (byteIndex >= length)
                        throw new InvalidOperationException("The generalized-data bit stream is truncated.");

                    int bitIndex = 7 - (absoluteBit % 8);
                    int bit = (data[start + byteIndex] >> bitIndex) & 0x01;
                    value = (value << 1) | bit;
                }

                return value;
            }
        }
    }
}
