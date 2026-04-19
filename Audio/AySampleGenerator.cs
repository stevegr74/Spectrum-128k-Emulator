
using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class AySampleGenerator
    {
        private const double AyClockHz = Spectrum128Machine.CpuClockHz / 2.0;

        private readonly byte[] currentRegisters = new byte[16];
        private readonly int[] toneCounters = new int[3];
        private readonly bool[] toneOutputs = { true, true, true };

        private bool stateInitialized;
        private double ayClockAccumulator;
        private int toneNoisePrescaler;
        private int envelopePrescaler;

        private int noiseCounter = 1;
        private uint noiseShiftRegister = 0x1FFFF;
        private bool noiseOutput = true;

        private int currentEnvelopeShape = -1;
        private int envelopeCounter = 1;
        private int envelopeStep = 15;
        private int envelopeAttackMask;
        private bool envelopeHold;
        private bool envelopeAlternate;
        private bool envelopeHolding;

        public AySampleGenerator(uint sampleRate)
        {
            if (sampleRate == 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));

            SampleRate = sampleRate;
        }

        public uint SampleRate { get; }

        public short[] GenerateFrameSamples(AudioFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            int sampleCount = GetSampleCount(frame.FrameTStates, SampleRate);
            short[] samples = new short[sampleCount];
            MixFrameSamples(frame, samples);
            return samples;
        }

        internal void MixFrameSamples(AudioFrame frame, short[] destination)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            int sampleCount = GetSampleCount(frame.FrameTStates, SampleRate);
            if (destination.Length < sampleCount)
                throw new ArgumentException("Destination buffer is too small.", nameof(destination));

            AyAudioState? startState = frame.InitialAyState ?? frame.AyState;
            if (startState == null)
                return;

            LoadFrameStartState(startState);

            int nextWriteIndex = 0;
            var writes = frame.AyWrites;
            while (nextWriteIndex < writes.Count && MapWriteToSampleIndex(writes[nextWriteIndex].TState, frame.FrameTStates, sampleCount) <= 0)
            {
                ApplyRegisterWrite(writes[nextWriteIndex].Register, writes[nextWriteIndex].Value);
                nextWriteIndex++;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                while (nextWriteIndex < writes.Count && MapWriteToSampleIndex(writes[nextWriteIndex].TState, frame.FrameTStates, sampleCount) == i)
                {
                    ApplyRegisterWrite(writes[nextWriteIndex].Register, writes[nextWriteIndex].Value);
                    nextWriteIndex++;
                }

                StepForOneSample();
                destination[i] = AddClamped(destination[i], MixCurrentSample());
            }

            while (nextWriteIndex < writes.Count)
            {
                ApplyRegisterWrite(writes[nextWriteIndex].Register, writes[nextWriteIndex].Value);
                nextWriteIndex++;
            }

            if (frame.AyState != null)
                CopyRegisters(frame.AyState.GetRegistersCopy(), restartEnvelopeIfShapeChanged: false);
        }

        private void LoadFrameStartState(AyAudioState state)
        {
            byte[] registers = state.GetRegistersCopy();

            if (!stateInitialized)
            {
                CopyRegisters(registers, restartEnvelopeIfShapeChanged: true);
                for (int channel = 0; channel < 3; channel++)
                    toneCounters[channel] = GetTonePeriod(channel);

                noiseCounter = GetNoisePeriod();
                envelopeCounter = GetEnvelopePeriod();
                stateInitialized = true;
                return;
            }

            bool restartEnvelope = (currentRegisters[13] & 0x0F) != (registers[13] & 0x0F);
            CopyRegisters(registers, restartEnvelope);
        }

        private void CopyRegisters(byte[] registers, bool restartEnvelopeIfShapeChanged)
        {
            if (registers.Length != 16)
                throw new ArgumentException("AY state must contain exactly 16 registers.", nameof(registers));

            byte previousShape = currentRegisters[13];
            Buffer.BlockCopy(registers, 0, currentRegisters, 0, 16);

            if (restartEnvelopeIfShapeChanged || previousShape != currentRegisters[13])
                RestartEnvelope(currentRegisters[13] & 0x0F);
        }

        private void ApplyRegisterWrite(byte register, byte value)
        {
            register &= 0x0F;
            currentRegisters[register] = value;

            switch (register)
            {
                case 13:
                    RestartEnvelope(value & 0x0F);
                    break;
                default:
                    break;
            }
        }

        private void StepForOneSample()
        {
            ayClockAccumulator += AyClockHz / SampleRate;
            int wholeAyClocks = (int)ayClockAccumulator;
            ayClockAccumulator -= wholeAyClocks;

            for (int i = 0; i < wholeAyClocks; i++)
                StepOneAyClock();
        }

        private void StepOneAyClock()
        {
            toneNoisePrescaler++;
            if (toneNoisePrescaler >= 8)
            {
                toneNoisePrescaler = 0;
                ClockToneAndNoise();
            }

            envelopePrescaler++;
            if (envelopePrescaler >= 16)
            {
                envelopePrescaler = 0;
                ClockEnvelope();
            }
        }

        private void ClockToneAndNoise()
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (--toneCounters[channel] <= 0)
                {
                    toneCounters[channel] = GetTonePeriod(channel);
                    toneOutputs[channel] = !toneOutputs[channel];
                }
            }

            if (--noiseCounter <= 0)
            {
                noiseCounter = GetNoisePeriod();
                ClockNoiseShiftRegister();
            }
        }

        private void ClockNoiseShiftRegister()
        {
            uint feedback = ((noiseShiftRegister ^ (noiseShiftRegister >> 3)) & 0x01u);
            noiseShiftRegister = (noiseShiftRegister >> 1) | (feedback << 16);
            noiseOutput = (noiseShiftRegister & 0x01u) != 0;
        }

        private void ClockEnvelope()
        {
            if (envelopeHolding)
                return;

            if (--envelopeCounter <= 0)
            {
                envelopeCounter = GetEnvelopePeriod();
                ClockEnvelopeStep();
            }
        }

        private void RestartEnvelope(int envelopeShape)
        {
            currentEnvelopeShape = envelopeShape & 0x0F;
            envelopeCounter = GetEnvelopePeriod();
            envelopeStep = 15;
            envelopeAttackMask = (currentEnvelopeShape & 0x04) != 0 ? 0x0F : 0x00;
            envelopeHold = (currentEnvelopeShape & 0x01) != 0;
            envelopeAlternate = (currentEnvelopeShape & 0x02) != 0;
            envelopeHolding = false;

            if ((currentEnvelopeShape & 0x08) == 0)
            {
                envelopeHold = true;
                envelopeAlternate = envelopeAttackMask != 0;
            }
        }

        private void ClockEnvelopeStep()
        {
            envelopeStep--;
            if (envelopeStep >= 0)
                return;

            if (envelopeHold)
            {
                envelopeHolding = true;
                envelopeStep = 0;
                return;
            }

            if (envelopeAlternate)
                envelopeAttackMask ^= 0x0F;

            envelopeStep = 15;
        }

        private short MixCurrentSample()
        {
            int mixed = 0;
            short envelopeAmplitude = GetEnvelopeAmplitude();

            for (int channel = 0; channel < 3; channel++)
            {
                short channelSample = GetChannelSample(channel, envelopeAmplitude);
                mixed += channelSample;
            }

            if (mixed > short.MaxValue)
                return short.MaxValue;
            if (mixed < short.MinValue)
                return short.MinValue;

            return (short)mixed;
        }

        private short GetChannelSample(int channel, short envelopeAmplitude)
        {
            byte mixer = currentRegisters[7];
            bool toneEnabled = (mixer & (1 << channel)) == 0;
            bool noiseEnabled = (mixer & (1 << (channel + 3))) == 0;

            if (!toneEnabled && !noiseEnabled)
                return 0;

            byte volumeRegister = currentRegisters[8 + channel];
            short amplitude = (volumeRegister & 0x10) != 0
                ? envelopeAmplitude
                : VolumeTables.GetAyAmplitude(volumeRegister);

            if (amplitude <= 0)
                return 0;

            if (toneEnabled && !toneOutputs[channel])
                return 0;

            if (noiseEnabled && !noiseOutput)
                return 0;

            return amplitude;
        }

        private short GetEnvelopeAmplitude()
        {
            bool anyChannelUsesEnvelope =
                (currentRegisters[8] & 0x10) != 0 ||
                (currentRegisters[9] & 0x10) != 0 ||
                (currentRegisters[10] & 0x10) != 0;

            if (!anyChannelUsesEnvelope)
                return 0;

            return VolumeTables.GetAyAmplitudeFromLevel(GetCurrentEnvelopeLevel());
        }

        private int GetCurrentEnvelopeLevel()
        {
            return envelopeStep ^ envelopeAttackMask;
        }

        private int GetTonePeriod(int channel)
        {
            int fineRegister = channel * 2;
            int coarseRegister = fineRegister + 1;
            int fine = currentRegisters[fineRegister];
            int coarse = currentRegisters[coarseRegister] & 0x0F;
            int period = fine | (coarse << 8);
            return period <= 0 ? 1 : period;
        }

        private int GetNoisePeriod()
        {
            int period = currentRegisters[6] & 0x1F;
            return period <= 0 ? 1 : period;
        }

        private int GetEnvelopePeriod()
        {
            int period = currentRegisters[11] | (currentRegisters[12] << 8);
            return period <= 0 ? 1 : period;
        }

        private static int GetSampleCount(int frameTStates, uint sampleRate)
        {
            long numerator = (long)frameTStates * sampleRate + (Spectrum128Machine.CpuClockHz / 2);
            int sampleCount = (int)(numerator / Spectrum128Machine.CpuClockHz);
            return sampleCount > 0 ? sampleCount : 1;
        }

        private static int MapWriteToSampleIndex(int writeTState, int frameTStates, int sampleCount)
        {
            if (writeTState <= 0)
                return 0;
            if (writeTState >= frameTStates)
                return sampleCount;

            long mapped = (long)writeTState * sampleCount / frameTStates;
            if (mapped < 0)
                return 0;
            if (mapped > sampleCount)
                return sampleCount;

            return (int)mapped;
        }

        private static short AddClamped(short existing, short addition)
        {
            int mixed = existing + addition;
            if (mixed > short.MaxValue)
                return short.MaxValue;
            if (mixed < short.MinValue)
                return short.MinValue;
            return (short)mixed;
        }
    }
}
