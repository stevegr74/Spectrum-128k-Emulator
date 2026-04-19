using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class AySampleGenerator
    {
        private const double AyClockHz = Spectrum128Machine.CpuClockHz / 2.0;

        private readonly double[] phase = new double[3];
        private double noisePhase;
        private uint noiseShiftRegister = 0x1FFFF;
        private bool noiseHigh = true;
        private double envelopePhase;
        private int currentEnvelopePeriod = 1;
        private int currentEnvelopeShape = -1;
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

            AyAudioState? ay = frame.AyState;
            if (ay == null)
                return;

            byte mixer = ay.ReadRegister(7);
            UpdateEnvelopeConfiguration(ay);

            int noisePeriod = ay.ReadRegister(6) & 0x1F;
            if (noisePeriod <= 0)
                noisePeriod = 1;

            ChannelConfig channelA = CreateChannelConfig(ay, mixer, 0);
            ChannelConfig channelB = CreateChannelConfig(ay, mixer, 1);
            ChannelConfig channelC = CreateChannelConfig(ay, mixer, 2);

            bool anyAudibleChannel = channelA.CanProduceSound || channelB.CanProduceSound || channelC.CanProduceSound;
            if (!anyAudibleChannel)
            {
                AdvanceQuietFrame(channelA, channelB, channelC, noisePeriod, sampleCount);
                return;
            }

            double phaseA = phase[0];
            double phaseB = phase[1];
            double phaseC = phase[2];
            double phaseStepA = GetTonePhaseStep(channelA.Period, SampleRate);
            double phaseStepB = GetTonePhaseStep(channelB.Period, SampleRate);
            double phaseStepC = GetTonePhaseStep(channelC.Period, SampleRate);
            double noisePhaseStep = GetNoisePhaseStep(noisePeriod, SampleRate);
            double envelopePhaseStep = GetEnvelopePhaseStep(currentEnvelopePeriod, SampleRate);
            bool usesEnvelope = channelA.UseEnvelope || channelB.UseEnvelope || channelC.UseEnvelope;

            for (int i = 0; i < sampleCount; i++)
            {
                short envelopeAmplitude = usesEnvelope
                    ? VolumeTables.GetAyAmplitudeFromLevel(GetCurrentEnvelopeLevel())
                    : (short)0;

                int mixedSample = 0;
                mixedSample += GetChannelSample(ref phaseA, phaseStepA, channelA, envelopeAmplitude, noiseHigh);
                mixedSample += GetChannelSample(ref phaseB, phaseStepB, channelB, envelopeAmplitude, noiseHigh);
                mixedSample += GetChannelSample(ref phaseC, phaseStepC, channelC, envelopeAmplitude, noiseHigh);

                if (mixedSample > short.MaxValue)
                    mixedSample = short.MaxValue;
                else if (mixedSample < short.MinValue)
                    mixedSample = short.MinValue;

                destination[i] = (short)(destination[i] + mixedSample);
                AdvanceNoise(noisePhaseStep);
                AdvanceEnvelope(envelopePhaseStep);
            }

            phase[0] = phaseA;
            phase[1] = phaseB;
            phase[2] = phaseC;
        }

        private static int GetSampleCount(int frameTStates, uint sampleRate)
        {
            long numerator = (long)frameTStates * sampleRate + (Spectrum128Machine.CpuClockHz / 2);
            int sampleCount = (int)(numerator / Spectrum128Machine.CpuClockHz);
            return sampleCount > 0 ? sampleCount : 1;
        }

        private static ChannelConfig CreateChannelConfig(AyAudioState ay, byte mixer, int channel)
        {
            bool toneEnabled = (mixer & (1 << channel)) == 0;
            bool noiseEnabled = (mixer & (1 << (channel + 3))) == 0;
            int period = GetTonePeriod(ay, channel);
            if (period <= 0)
                period = 1;

            byte volumeRegister = ay.ReadRegister(8 + channel);
            bool useEnvelope = (volumeRegister & 0x10) != 0;
            short fixedAmplitude = VolumeTables.GetAyAmplitude(volumeRegister);
            bool canProduceSound = useEnvelope || fixedAmplitude > 0;

            return new ChannelConfig(toneEnabled, noiseEnabled, period, useEnvelope, fixedAmplitude, canProduceSound);
        }

        private void AdvanceQuietFrame(ChannelConfig channelA, ChannelConfig channelB, ChannelConfig channelC, int noisePeriod, int sampleCount)
        {
            phase[0] = AdvanceWrappedPhase(phase[0], GetTonePhaseStep(channelA.Period, SampleRate) * sampleCount);
            phase[1] = AdvanceWrappedPhase(phase[1], GetTonePhaseStep(channelB.Period, SampleRate) * sampleCount);
            phase[2] = AdvanceWrappedPhase(phase[2], GetTonePhaseStep(channelC.Period, SampleRate) * sampleCount);

            AdvanceNoise(GetNoisePhaseStep(noisePeriod, SampleRate) * sampleCount);
            AdvanceEnvelope(GetEnvelopePhaseStep(currentEnvelopePeriod, SampleRate) * sampleCount);
        }

        private static int GetChannelSample(ref double channelPhase, double phaseStep, ChannelConfig config, short envelopeAmplitude, bool currentNoiseHigh)
        {
            short amplitude = config.UseEnvelope ? envelopeAmplitude : config.FixedAmplitude;
            if (amplitude <= 0)
            {
                channelPhase = AdvanceWrappedPhase(channelPhase, phaseStep);
                return 0;
            }

            if (!config.ToneEnabled && !config.NoiseEnabled)
            {
                channelPhase = AdvanceWrappedPhase(channelPhase, phaseStep);
                return 0;
            }

            bool toneGate = !config.ToneEnabled || channelPhase < 0.5;
            bool noiseGate = !config.NoiseEnabled || currentNoiseHigh;
            bool channelHigh = toneGate && noiseGate;

            channelPhase = AdvanceWrappedPhase(channelPhase, phaseStep);
            return channelHigh ? amplitude : 0;
        }

        private void UpdateEnvelopeConfiguration(AyAudioState ay)
        {
            int envelopePeriod = ay.ReadRegister(11) | (ay.ReadRegister(12) << 8);
            if (envelopePeriod <= 0)
                envelopePeriod = 1;

            int envelopeShape = ay.ReadRegister(13) & 0x0F;
            currentEnvelopePeriod = envelopePeriod;

            if (envelopeShape != currentEnvelopeShape)
                RestartEnvelope(envelopeShape);
        }

        private void RestartEnvelope(int envelopeShape)
        {
            currentEnvelopeShape = envelopeShape;
            envelopePhase = 0.0;
            envelopeStep = 15;
            envelopeAttackMask = (envelopeShape & 0x04) != 0 ? 0x0F : 0x00;
            envelopeAlternate = (envelopeShape & 0x02) != 0;
            envelopeHold = (envelopeShape & 0x01) != 0;
            envelopeHolding = false;

            if ((envelopeShape & 0x08) == 0)
            {
                envelopeHold = true;
                envelopeAlternate = envelopeAttackMask != 0;
            }
        }

        private void AdvanceEnvelope(double envelopePhaseStep)
        {
            if (envelopeHolding)
                return;

            envelopePhase += envelopePhaseStep;
            while (envelopePhase >= 1.0)
            {
                envelopePhase -= 1.0;
                ClockEnvelopeStep();
                if (envelopeHolding)
                    break;
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

        private int GetCurrentEnvelopeLevel()
        {
            return envelopeStep ^ envelopeAttackMask;
        }

        private void AdvanceNoise(double noisePhaseStep)
        {
            noisePhase += noisePhaseStep;
            while (noisePhase >= 1.0)
            {
                noisePhase -= 1.0;
                ClockNoiseStep();
            }
        }

        private void ClockNoiseStep()
        {
            uint feedback = ((noiseShiftRegister ^ (noiseShiftRegister >> 3)) & 0x01u);
            noiseShiftRegister = (noiseShiftRegister >> 1) | (feedback << 16);
            noiseHigh = (noiseShiftRegister & 0x01u) != 0;
        }

        private static double GetTonePhaseStep(int period, uint sampleRate)
        {
            double frequency = AyClockHz / (16.0 * period);
            return frequency / sampleRate;
        }

        private static double GetEnvelopePhaseStep(int period, uint sampleRate)
        {
            double frequency = AyClockHz / (256.0 * period);
            return frequency / sampleRate;
        }

        private static double GetNoisePhaseStep(int period, uint sampleRate)
        {
            double frequency = AyClockHz / (16.0 * period);
            return frequency / sampleRate;
        }

        private static double AdvanceWrappedPhase(double currentPhase, double phaseStep)
        {
            double nextPhase = currentPhase + phaseStep;
            return nextPhase - Math.Floor(nextPhase);
        }

        private static int GetTonePeriod(AyAudioState ay, int channel)
        {
            int fineRegister = channel * 2;
            int coarseRegister = fineRegister + 1;
            int fine = ay.ReadRegister(fineRegister);
            int coarse = ay.ReadRegister(coarseRegister) & 0x0F;
            return fine | (coarse << 8);
        }

        private readonly struct ChannelConfig
        {
            public ChannelConfig(bool toneEnabled, bool noiseEnabled, int period, bool useEnvelope, short fixedAmplitude, bool canProduceSound)
            {
                ToneEnabled = toneEnabled;
                NoiseEnabled = noiseEnabled;
                Period = period;
                UseEnvelope = useEnvelope;
                FixedAmplitude = fixedAmplitude;
                CanProduceSound = canProduceSound;
            }

            public bool ToneEnabled { get; }
            public bool NoiseEnabled { get; }
            public int Period { get; }
            public bool UseEnvelope { get; }
            public short FixedAmplitude { get; }
            public bool CanProduceSound { get; }
        }
    }
}
