using System;

namespace Spectrum128kEmulator.Tap
{
    public enum TapeBlockKind
    {
        Data,
        PureTone,
        PulseSequence,
        DirectRecording,
        Pause,
        SetSignalLevel,
        Metadata
    }

    public sealed class TapeBlock
    {
        private TapeBlock(
            TapeBlockKind kind,
            bool isLoadableRomBlock,
            bool canUseRomLoadTrap,
            byte[]? streamData,
            byte[]? payload,
            byte checksum,
            ushort pilotPulseLength,
            ushort pilotPulseCount,
            ushort syncFirstPulseLength,
            ushort syncSecondPulseLength,
            ushort zeroBitPulseLength,
            ushort oneBitPulseLength,
            byte usedBitsInLastByte,
            ushort pauseAfterBlockMs,
            ushort pureTonePulseLength,
            ushort pureTonePulseCount,
            int[]? pulseSequence,
            byte[]? directRecordingSamples,
            ushort directRecordingSampleTStates,
            bool? signalLevel)
        {
            Kind = kind;
            IsLoadableRomBlock = isLoadableRomBlock;
            CanUseRomLoadTrap = canUseRomLoadTrap;
            StreamData = streamData;
            Payload = payload;
            Checksum = checksum;
            PilotPulseLength = pilotPulseLength;
            PilotPulseCount = pilotPulseCount;
            SyncFirstPulseLength = syncFirstPulseLength;
            SyncSecondPulseLength = syncSecondPulseLength;
            ZeroBitPulseLength = zeroBitPulseLength;
            OneBitPulseLength = oneBitPulseLength;
            UsedBitsInLastByte = usedBitsInLastByte;
            PauseAfterBlockMs = pauseAfterBlockMs;
            PureTonePulseLength = pureTonePulseLength;
            PureTonePulseCount = pureTonePulseCount;
            PulseSequence = pulseSequence;
            DirectRecordingSamples = directRecordingSamples;
            DirectRecordingSampleTStates = directRecordingSampleTStates;
            SignalLevel = signalLevel;
        }

        public TapeBlockKind Kind { get; }
        public bool IsLoadableRomBlock { get; }
        public bool CanUseRomLoadTrap { get; }
        public byte[]? StreamData { get; }
        public byte[]? Payload { get; }
        public byte Checksum { get; }
        public ushort PilotPulseLength { get; }
        public ushort PilotPulseCount { get; }
        public ushort SyncFirstPulseLength { get; }
        public ushort SyncSecondPulseLength { get; }
        public ushort ZeroBitPulseLength { get; }
        public ushort OneBitPulseLength { get; }
        public byte UsedBitsInLastByte { get; }
        public ushort PauseAfterBlockMs { get; }
        public ushort PureTonePulseLength { get; }
        public ushort PureTonePulseCount { get; }
        public int[]? PulseSequence { get; }
        public byte[]? DirectRecordingSamples { get; }
        public ushort DirectRecordingSampleTStates { get; }
        public bool? SignalLevel { get; }

        public bool IsDataBlock => Kind == TapeBlockKind.Data;
        public byte Flag => StreamData == null || StreamData.Length == 0 ? (byte)0xFF : StreamData[0];
        public int StreamByteCount => StreamData?.Length ?? 0;

        public byte GetStreamByte(int index)
        {
            if (StreamData == null)
                throw new InvalidOperationException("This tape block does not contain a byte stream.");

            return StreamData[index];
        }

        public byte GetStreamByteBitCount(int index)
        {
            if (StreamData == null)
                throw new InvalidOperationException("This tape block does not contain a byte stream.");

            if (index < StreamData.Length - 1)
                return 8;

            return UsedBitsInLastByte == 0 ? (byte)8 : UsedBitsInLastByte;
        }

        public static TapeBlock CreateData(
            byte[] streamData,
            ushort pilotPulseLength,
            ushort pilotPulseCount,
            ushort syncFirstPulseLength,
            ushort syncSecondPulseLength,
            ushort zeroBitPulseLength,
            ushort oneBitPulseLength,
            byte usedBitsInLastByte,
            ushort pauseAfterBlockMs)
        {
            if (streamData == null)
                throw new ArgumentNullException(nameof(streamData));
            if (streamData.Length < 2)
                throw new ArgumentException("Tape data blocks must contain at least a flag byte and checksum byte.", nameof(streamData));

            byte[] payload = new byte[streamData.Length - 2];
            Buffer.BlockCopy(streamData, 1, payload, 0, payload.Length);

            return new TapeBlock(
                TapeBlockKind.Data,
                isLoadableRomBlock: true,
                canUseRomLoadTrap: true,
                (byte[])streamData.Clone(),
                payload,
                streamData[^1],
                pilotPulseLength,
                pilotPulseCount,
                syncFirstPulseLength,
                syncSecondPulseLength,
                zeroBitPulseLength,
                oneBitPulseLength,
                usedBitsInLastByte == 0 ? (byte)8 : usedBitsInLastByte,
                pauseAfterBlockMs,
                0,
                0,
                null,
                null,
                0,
                null);
        }

        public static TapeBlock CreateByteStreamData(
            byte[] streamData,
            ushort zeroBitPulseLength,
            ushort oneBitPulseLength,
            byte usedBitsInLastByte,
            ushort pauseAfterBlockMs)
        {
            if (streamData == null)
                throw new ArgumentNullException(nameof(streamData));
            if (streamData.Length == 0)
                throw new ArgumentException("Tape byte-stream blocks must contain at least one data byte.", nameof(streamData));

            byte[]? payload = null;
            byte checksum = 0;

            return new TapeBlock(
                TapeBlockKind.Data,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                (byte[])streamData.Clone(),
                payload,
                checksum,
                0,
                0,
                0,
                0,
                zeroBitPulseLength,
                oneBitPulseLength,
                usedBitsInLastByte == 0 ? (byte)8 : usedBitsInLastByte,
                pauseAfterBlockMs,
                0,
                0,
                null,
                null,
                0,
                null);
        }

        public static TapeBlock ReclassifyAsByteStreamData(TapeBlock source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Kind != TapeBlockKind.Data || source.StreamData == null)
                throw new ArgumentException("Only data blocks with a byte stream can be reclassified.", nameof(source));

            return new TapeBlock(
                TapeBlockKind.Data,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                (byte[])source.StreamData.Clone(),
                payload: null,
                checksum: 0,
                source.PilotPulseLength,
                source.PilotPulseCount,
                source.SyncFirstPulseLength,
                source.SyncSecondPulseLength,
                source.ZeroBitPulseLength,
                source.OneBitPulseLength,
                source.UsedBitsInLastByte,
                source.PauseAfterBlockMs,
                source.PureTonePulseLength,
                source.PureTonePulseCount,
                source.PulseSequence == null ? null : (int[])source.PulseSequence.Clone(),
                source.DirectRecordingSamples == null ? null : (byte[])source.DirectRecordingSamples.Clone(),
                source.DirectRecordingSampleTStates,
                source.SignalLevel);
        }

        public static TapeBlock ReclassifyAsRomTrapByteStreamData(TapeBlock source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Kind != TapeBlockKind.Data || source.StreamData == null)
                throw new ArgumentException("Only data blocks with a byte stream can be reclassified.", nameof(source));

            return new TapeBlock(
                TapeBlockKind.Data,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: true,
                (byte[])source.StreamData.Clone(),
                source.Payload == null ? null : (byte[])source.Payload.Clone(),
                source.Checksum,
                source.PilotPulseLength,
                source.PilotPulseCount,
                source.SyncFirstPulseLength,
                source.SyncSecondPulseLength,
                source.ZeroBitPulseLength,
                source.OneBitPulseLength,
                source.UsedBitsInLastByte,
                source.PauseAfterBlockMs,
                source.PureTonePulseLength,
                source.PureTonePulseCount,
                source.PulseSequence == null ? null : (int[])source.PulseSequence.Clone(),
                source.DirectRecordingSamples == null ? null : (byte[])source.DirectRecordingSamples.Clone(),
                source.DirectRecordingSampleTStates,
                source.SignalLevel);
        }

        public static TapeBlock CreatePureTone(ushort pulseLength, ushort pulseCount)
        {
            return new TapeBlock(
                TapeBlockKind.PureTone,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                pulseLength,
                pulseCount,
                null,
                null,
                0,
                null);
        }

        public static TapeBlock CreatePulseSequence(int[] pulseSequence, ushort pauseAfterBlockMs = 0)
        {
            if (pulseSequence == null)
                throw new ArgumentNullException(nameof(pulseSequence));

            return new TapeBlock(
                TapeBlockKind.PulseSequence,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                pauseAfterBlockMs,
                0,
                0,
                (int[])pulseSequence.Clone(),
                null,
                0,
                null);
        }

        public static TapeBlock CreateDirectRecording(
            byte[] sampleData,
            ushort tStatesPerSample,
            byte usedBitsInLastByte,
            ushort pauseAfterBlockMs)
        {
            if (sampleData == null)
                throw new ArgumentNullException(nameof(sampleData));
            if (sampleData.Length == 0)
                throw new ArgumentException("Direct recording blocks must contain at least one sample byte.", nameof(sampleData));

            return new TapeBlock(
                TapeBlockKind.DirectRecording,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                usedBitsInLastByte == 0 ? (byte)8 : usedBitsInLastByte,
                pauseAfterBlockMs,
                0,
                0,
                null,
                (byte[])sampleData.Clone(),
                tStatesPerSample,
                null);
        }

        public static TapeBlock CreatePause(ushort pauseAfterBlockMs)
        {
            return new TapeBlock(
                TapeBlockKind.Pause,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                pauseAfterBlockMs,
                0,
                0,
                null,
                null,
                0,
                null);
        }

        public static TapeBlock CreateSetSignalLevel(bool high)
        {
            return new TapeBlock(
                TapeBlockKind.SetSignalLevel,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                0,
                high);
        }

        public static TapeBlock CreateMetadata()
        {
            return new TapeBlock(
                TapeBlockKind.Metadata,
                isLoadableRomBlock: false,
                canUseRomLoadTrap: false,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                0,
                null);
        }
    }
}
