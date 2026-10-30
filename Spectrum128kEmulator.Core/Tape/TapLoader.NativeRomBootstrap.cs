using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Tap
{
    public static partial class TapLoader
    {
        private static TapBootstrapResult StartNativeRomTapeLoader(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            int nonRomTimingDivisor,
            int loadableTimingDivisor,
            bool initialEarLevelHigh)
        {
            return machine.MachineModel == SpectrumMachineModel.Spectrum128K
                ? StartNative128kRomTapeLoader(
                    machine,
                    displayName,
                    blocks,
                    nonRomTimingDivisor,
                    loadableTimingDivisor,
                    initialEarLevelHigh)
                : StartNative48kRomTapeLoader(
                    machine,
                    displayName,
                    blocks,
                    nonRomTimingDivisor,
                    loadableTimingDivisor,
                    initialEarLevelHigh);
        }

        private static TapBootstrapResult StartNative48kRomTapeLoader(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            int nonRomTimingDivisor,
            int loadableTimingDivisor,
            bool initialEarLevelHigh)
        {
            InitializeMachineForRomDrivenTapeLoad(machine, use128kMode: false);

            PressRomKey(machine, row: 6, bit: 3); // J: LOAD keyword in K mode.
            PressRomKeyChord(machine, (7, 1), (5, 0)); // Symbol Shift+P: quote.
            PressRomKeyChord(machine, (7, 1), (5, 0));

            var tape = new MountedTape(
                displayName,
                blocks,
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false,
                nonRomTimingDivisor: nonRomTimingDivisor,
                loadableTimingDivisor: loadableTimingDivisor,
                initialEarLevelHigh: initialEarLevelHigh,
                allowRomLoadTraps: false);
            machine.MountTape(tape);

            PressRomKey(machine, row: 6, bit: 0); // Enter.

            bool enteredTapeLoader = false;
            for (int frame = 0; frame < 100; frame++)
            {
                machine.ExecuteTimeSlice(machine.FrameTStates);
                ushort programCounter = machine.Cpu.Regs.PC;
                if ((machine.PeekMemory(FlagsSystemVariableAddress) & 0x10) != 0 ||
                    programCounter is >= 0x0556 and <= 0x0605)
                {
                    enteredTapeLoader = true;
                    break;
                }
            }

            machine.ClearKeyboard();
            if (!enteredTapeLoader)
                throw new InvalidOperationException("The Spectrum 48 ROM did not enter its tape loader.");

            string? autoStartFileName = null;
            foreach (TapeBlock block in blocks)
            {
                if (!IsStandardHeaderBlock(block))
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(block);
                if (header.Type == ProgramType)
                    autoStartFileName = header.FileName;
                break;
            }

            return new TapBootstrapResult(blocks.Count, 0, displayName, autoStartFileName);
        }

        private static void PressRomKey(Spectrum128Machine machine, int row, int bit)
        {
            machine.SetKey(row, bit, true);
            ExecuteRomInputFrames(machine, 4);
            machine.SetKey(row, bit, false);
            ExecuteRomInputFrames(machine, 4);
        }

        private static void PressRomKeyChord(
            Spectrum128Machine machine,
            (int Row, int Bit) first,
            (int Row, int Bit) second)
        {
            machine.SetKey(first.Row, first.Bit, true);
            machine.SetKey(second.Row, second.Bit, true);
            ExecuteRomInputFrames(machine, 4);
            machine.SetKey(second.Row, second.Bit, false);
            machine.SetKey(first.Row, first.Bit, false);
            ExecuteRomInputFrames(machine, 4);
        }

        private static void ExecuteRomInputFrames(Spectrum128Machine machine, int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
                machine.ExecuteTimeSlice(machine.FrameTStates);
        }
    }
}
