using System;
using System.Runtime.InteropServices;

namespace Spectrum128kEmulator.Audio
{
    public sealed class WaveOutAudioOutput : IAudioOutput
    {
        private const int CallbackNull = 0;
        private const int MmNoError = 0;
        private const int WaveMapper = -1;
        private const int WaveFormatPcm = 1;
        private const uint HeaderDone = 0x00000001;
        private const uint HeaderPrepared = 0x00000002;
        private const int BufferCount = 3;
        private const uint TargetBufferMilliseconds = 20;

        private readonly IntPtr deviceHandle;
        private readonly BufferSlot[] bufferSlots;
        private bool disposed;

        public WaveOutAudioOutput(uint sampleRate = 44100)
        {
            if (sampleRate == 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));

            SampleRate = sampleRate;

            WaveFormatEx format = new WaveFormatEx
            {
                wFormatTag = WaveFormatPcm,
                nChannels = 1,
                nSamplesPerSec = sampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 2,
                nAvgBytesPerSec = sampleRate * 2,
                cbSize = 0
            };

            int result = waveOutOpen(out deviceHandle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
            if (result != MmNoError)
                throw new InvalidOperationException($"waveOutOpen failed with MMRESULT={result}.");

            uint targetSamplesPerBuffer = Math.Max(1u, (sampleRate * TargetBufferMilliseconds) / 1000u);
            int bufferByteCapacity = checked((int)(targetSamplesPerBuffer * sizeof(short)));
            bufferSlots = new BufferSlot[BufferCount];
            for (int i = 0; i < bufferSlots.Length; i++)
                bufferSlots[i] = new BufferSlot(bufferByteCapacity);
        }

        public uint SampleRate { get; }

        public void WriteSamples(short[] monoSamples)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (monoSamples == null)
                throw new ArgumentNullException(nameof(monoSamples));
            if (monoSamples.Length == 0)
                return;

            int byteCount = checked(monoSamples.Length * sizeof(short));

            ReclaimCompletedBuffers();

            BufferSlot? slot = AcquireWritableSlot();
            if (slot == null)
            {
                FlushBacklog();
                slot = AcquireWritableSlot();
                if (slot == null)
                    return;
            }

            if (slot.ByteCapacity < byteCount)
                slot.EnsureCapacity(byteCount);

            Buffer.BlockCopy(monoSamples, 0, slot.Data!, 0, byteCount);
            slot.Header.dwBufferLength = (uint)byteCount;
            slot.Header.dwBytesRecorded = 0;
            slot.Header.dwLoops = 0;
            slot.Header.dwUser = IntPtr.Zero;
            slot.Header.lpNext = IntPtr.Zero;
            slot.Header.reserved = IntPtr.Zero;
            slot.Header.dwFlags &= HeaderPrepared;
            Marshal.StructureToPtr(slot.Header, slot.HeaderPtr, fDeleteOld: false);

            if (!slot.IsPrepared)
            {
                int prepareResult = waveOutPrepareHeader(deviceHandle, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
                if (prepareResult != MmNoError)
                    throw new InvalidOperationException($"waveOutPrepareHeader failed with MMRESULT={prepareResult}.");

                slot.IsPrepared = true;
                slot.RefreshHeader();
            }

            int writeResult = waveOutWrite(deviceHandle, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
            if (writeResult != MmNoError)
                throw new InvalidOperationException($"waveOutWrite failed with MMRESULT={writeResult}.");

            slot.InUse = true;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            waveOutReset(deviceHandle);

            for (int i = 0; i < bufferSlots.Length; i++)
            {
                BufferSlot slot = bufferSlots[i];
                if (slot.IsPrepared)
                {
                    waveOutUnprepareHeader(deviceHandle, slot.HeaderPtr, (uint)Marshal.SizeOf<WaveHdr>());
                    slot.IsPrepared = false;
                }

                slot.Dispose();
            }

            waveOutClose(deviceHandle);
        }

        private void ReclaimCompletedBuffers()
        {
            for (int i = 0; i < bufferSlots.Length; i++)
            {
                BufferSlot slot = bufferSlots[i];
                if (!slot.InUse)
                    continue;

                slot.RefreshHeader();
                if ((slot.Header.dwFlags & HeaderDone) == 0)
                    continue;

                slot.InUse = false;
            }
        }

        private BufferSlot? AcquireWritableSlot()
        {
            for (int i = 0; i < bufferSlots.Length; i++)
            {
                BufferSlot slot = bufferSlots[i];
                if (!slot.InUse)
                    return slot;
            }

            return null;
        }

        private void FlushBacklog()
        {
            int resetResult = waveOutReset(deviceHandle);
            if (resetResult != MmNoError)
                throw new InvalidOperationException($"waveOutReset failed with MMRESULT={resetResult}.");

            for (int i = 0; i < bufferSlots.Length; i++)
            {
                BufferSlot slot = bufferSlots[i];
                slot.RefreshHeader();
                slot.InUse = false;
                slot.Header.dwBufferLength = 0;
                slot.Header.dwBytesRecorded = 0;
                slot.Header.dwLoops = 0;
                slot.Header.dwUser = IntPtr.Zero;
                slot.Header.lpNext = IntPtr.Zero;
                slot.Header.reserved = IntPtr.Zero;
                slot.Header.dwFlags &= HeaderPrepared;
                Marshal.StructureToPtr(slot.Header, slot.HeaderPtr, fDeleteOld: false);
            }
        }

        private sealed class BufferSlot : IDisposable
        {
            public BufferSlot(int byteCapacity)
            {
                if (byteCapacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(byteCapacity));

                Allocate(byteCapacity);
            }

            public byte[]? Data { get; private set; }
            public GCHandle DataHandle { get; private set; }
            public IntPtr HeaderPtr => HeaderHandle.AddrOfPinnedObject();
            public GCHandle HeaderHandle { get; private set; }
            public WaveHdr Header;
            public int ByteCapacity { get; private set; }
            public bool IsPrepared { get; set; }
            public bool InUse { get; set; }

            public void EnsureCapacity(int requiredBytes)
            {
                if (requiredBytes <= ByteCapacity)
                    return;

                if (InUse)
                    throw new InvalidOperationException("Cannot resize an active waveOut buffer.");
                if (IsPrepared)
                    throw new InvalidOperationException("Cannot resize a prepared waveOut buffer.");

                ReleaseData();
                Allocate(requiredBytes);
            }

            public void RefreshHeader()
            {
                Header = Marshal.PtrToStructure<WaveHdr>(HeaderPtr);
            }

            public void Dispose()
            {
                if (HeaderHandle.IsAllocated)
                    HeaderHandle.Free();
                ReleaseData();
            }

            private void Allocate(int byteCapacity)
            {
                ByteCapacity = byteCapacity;
                Data = new byte[byteCapacity];
                DataHandle = GCHandle.Alloc(Data, GCHandleType.Pinned);

                Header = new WaveHdr
                {
                    lpData = DataHandle.AddrOfPinnedObject(),
                    dwBufferLength = 0,
                    dwBytesRecorded = 0,
                    dwUser = IntPtr.Zero,
                    dwFlags = 0,
                    dwLoops = 0,
                    lpNext = IntPtr.Zero,
                    reserved = IntPtr.Zero
                };

                if (!HeaderHandle.IsAllocated)
                    HeaderHandle = GCHandle.Alloc(Header, GCHandleType.Pinned);
                else
                    Marshal.StructureToPtr(Header, HeaderPtr, fDeleteOld: false);
            }

            private void ReleaseData()
            {
                Data = null;
                ByteCapacity = 0;
                if (DataHandle.IsAllocated)
                    DataHandle.Free();
            }
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceId, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }
    }
}
