using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    public static partial class TapLoader
    {
        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static bool TryReadLiveBasicNumericVariable(
            Spectrum128Machine machine,
            string variableName,
            out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                return false;

            char variableChar = char.ToLowerInvariant(variableName[0]);
            if (variableChar < 'a' || variableChar > 'z')
                return false;

            byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));
            ushort varsAddress = ReadWord(machine, VarsAddress);
            ushort eLineAddress = ReadWord(machine, EditLineAddress);
            if (TryReadBasicNumericVariableFromRange(machine, targetHeader, varsAddress, eLineAddress, out value))
                return true;

            if (machine.TryGetPendingMountedLoadBasicVariableSnapshot(out ushort preservedVarsAddress, out byte[] preservedVariableData))
            {
                if (TryReadBasicNumericVariableFromSnapshot(
                    machine,
                    targetHeader,
                    preservedVarsAddress,
                    preservedVariableData,
                    out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadMountedContinuationNumericVariable(
            Spectrum128Machine machine,
            string variableName,
            out int value)
        {
            return TryReadLiveBasicNumericVariable(machine, variableName, out value);
        }

        private static bool TryReadBasicNumericVariableFromSnapshot(
            Spectrum128Machine machine,
            byte targetHeader,
            ushort varsAddress,
            byte[] variableData,
            out int value)
        {
            value = 0;
            if (variableData.Length == 0)
                return false;

            int currentOffset = 0;
            while (currentOffset < variableData.Length)
            {
                byte header = variableData[currentOffset];
                if (header == 0x80)
                    return false;

                int entryLength = GetBasicVariableEntryLength(variableData, currentOffset);
                if (entryLength <= 0 || currentOffset + entryLength > variableData.Length)
                    return false;

                if ((header & 0xE0) == 0x60 && header == targetHeader)
                {
                    ushort entryAddress = (ushort)(varsAddress + currentOffset + 1);
                    return TryReadSpectrumSmallIntegerFromSnapshot(
                        machine,
                        entryAddress,
                        variableData,
                        currentOffset + 1,
                        out value);
                }

                currentOffset += entryLength;
            }

            return false;
        }

        private static bool TryReadBasicNumericVariableFromRange(
            Spectrum128Machine machine,
            byte targetHeader,
            ushort varsAddress,
            ushort eLineAddress,
            out int value)
        {
            value = 0;
            if (varsAddress >= eLineAddress)
                return false;

            ushort current = varsAddress;
            while (current < eLineAddress)
            {
                byte header = machine.PeekMemory(current);
                if (header == 0x80)
                    return false;

                int entryLength = GetLiveBasicVariableEntryLength(machine, current, eLineAddress);
                if (entryLength <= 0 || current + entryLength > eLineAddress)
                    return false;

                if ((header & 0xE0) == 0x60 && header == targetHeader)
                    return TryReadSpectrumSmallInteger(machine, (ushort)(current + 1), out value);

                current = (ushort)(current + entryLength);
            }

            return false;
        }

        private static int GetBasicVariableEntryLength(byte[] variableData, int offset)
        {
            if ((uint)offset >= (uint)variableData.Length)
                return 0;

            byte header = variableData[offset];
            if (header == 0x80)
                return 1;

            switch (header & 0xE0)
            {
                case 0x40:
                {
                    if (offset + 2 >= variableData.Length)
                        return 0;
                    ushort length = (ushort)(variableData[offset + 1] | (variableData[offset + 2] << 8));
                    return 3 + length;
                }

                case 0x60:
                    return 6;

                case 0x80:
                case 0xC0:
                {
                    if (offset + 2 >= variableData.Length)
                        return 0;
                    ushort length = (ushort)(variableData[offset + 1] | (variableData[offset + 2] << 8));
                    return 3 + length;
                }

                case 0xA0:
                {
                    int current = offset + 1;
                    while (current < variableData.Length)
                    {
                        byte nameByte = variableData[current];
                        current++;
                        if ((nameByte & 0x80) != 0)
                        {
                            if (current + 5 > variableData.Length)
                                return 0;
                            return current - offset + 5;
                        }
                    }

                    return 0;
                }

                case 0xE0:
                    return 19;

                default:
                    return 0;
            }
        }

        private static bool TryReadSpectrumSmallIntegerFromSnapshot(
            Spectrum128Machine machine,
            ushort baseAddress,
            byte[] variableData,
            int offset,
            out int value)
        {
            value = 0;
            if (offset + 4 >= variableData.Length)
                return false;

            byte exponent = variableData[offset];
            byte sign = variableData[offset + 1];
            byte low = variableData[offset + 2];
            byte high = variableData[offset + 3];
            byte trailing = variableData[offset + 4];

            if (TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value))
                return true;

            return TryReadSpectrumSmallInteger(machine, baseAddress, out value);
        }

        private static int GetLiveBasicVariableEntryLength(
            Spectrum128Machine machine,
            ushort address,
            ushort eLineAddress)
        {
            if (address >= eLineAddress)
                return 0;

            byte header = machine.PeekMemory(address);
            switch (header & 0xE0)
            {
                case 0x20:
                    return 6;

                case 0x40:
                case 0x80:
                {
                    if (address + 2 >= eLineAddress)
                        return 0;
                    ushort length = ReadWord(machine, (ushort)(address + 1));
                    return 3 + length;
                }

                case 0x60:
                    return 6;

                case 0xA0:
                {
                    ushort current = (ushort)(address + 1);
                    while (current < eLineAddress)
                    {
                        byte nameByte = machine.PeekMemory(current);
                        current++;
                        if ((nameByte & 0x80) != 0)
                        {
                            if (current + 5 > eLineAddress)
                                return 0;
                            return current - address + 5;
                        }
                    }

                    return 0;
                }

                case 0xE0:
                    return 19;

                default:
                    return 0;
            }
        }

        private static bool TryReadSpectrumSmallInteger(
            Spectrum128Machine machine,
            ushort address,
            out int value)
        {
            value = 0;
            if (address + 4 >= 65536)
                return false;

            byte exponent = machine.PeekMemory(address);
            byte sign = machine.PeekMemory((ushort)(address + 1));
            byte low = machine.PeekMemory((ushort)(address + 2));
            byte high = machine.PeekMemory((ushort)(address + 3));
            byte trailing = machine.PeekMemory((ushort)(address + 4));
            return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
        }

        private static bool TryDecodeSpectrumNumericValue(
            byte exponent,
            byte signOrMantissa,
            byte third,
            byte fourth,
            byte fifth,
            out int value)
        {
            value = 0;

            if (exponent == 0x00)
            {
                if (fifth != 0x00)
                    return false;

                short signedValue = unchecked((short)(third | (fourth << 8)));
                value = signOrMantissa == 0xFF ? -Math.Abs(signedValue) : signedValue;
                return true;
            }

            uint mantissa =
                (uint)(((signOrMantissa & 0x7F) | 0x80) << 24) |
                (uint)(third << 16) |
                (uint)(fourth << 8) |
                fifth;
            int sign = (signOrMantissa & 0x80) != 0 ? -1 : 1;
            double numericValue = sign * mantissa * Math.Pow(2.0, exponent - 160);
            double roundedValue = Math.Round(numericValue);
            if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
                return false;

            if (Math.Abs(numericValue - roundedValue) > 1e-6)
                return false;

            value = (int)roundedValue;
            return true;
        }

        private static void WriteWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private sealed class BasicBootstrapExecutor
        {
            private readonly Spectrum128Machine machine;
            private List<BasicLine> lines;
            private Queue<int> dataValues;
            private readonly Dictionary<string, int> variables = new(StringComparer.OrdinalIgnoreCase);
            private readonly bool ignoreLoadStatements;
            private bool restoreInterpreterWorkspaceOnExit = true;
            public int IgnoredLoadCount { get; private set; }
            public int ConsumedMountedLoadCount { get; private set; }

            public BasicBootstrapExecutor(Spectrum128Machine machine, ushort programStart, ushort programLength, bool ignoreLoadStatements)
            {
                this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
                this.ignoreLoadStatements = ignoreLoadStatements;
                lines = ParseLines(machine, programStart, programLength);
                dataValues = new Queue<int>(CollectDataValues());
            }

            public void Execute(ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : lines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return;

                var forStack = new Stack<ForFrame>();
                int lineIndex = startLineIndex;
                int statementIndex = 0;
                int stepCount = 0;

                while (lineIndex >= 0 && lineIndex < lines.Count && stepCount++ < 10000)
                {
                    BasicLine line = lines[lineIndex];
                    if (statementIndex >= line.Statements.Count)
                    {
                        lineIndex++;
                        statementIndex = 0;
                        continue;
                    }

                    List<string> statement = line.Statements[statementIndex];
                    statementIndex++;
                    if (statement.Count == 0)
                        continue;

                    UpdateExecutionContext(line.Number, statementIndex);

                    string keyword = NormalizeStatementKeyword(statement[0]);
                    switch (keyword)
                    {
                        case "REM":
                            statementIndex = line.Statements.Count;
                            break;

                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            ApplyDisplaySideEffects();
                            break;

                        case "?":
                            ApplyPrintSideEffects(line, statementIndex - 1, statement);
                            break;

                        case "LOAD":
                            if (ignoreLoadStatements)
                            {
                                IgnoredLoadCount++;
                            }
                            else
                            {
                                BootstrapTapeLoadResult loadResult = machine.TryConsumeBootstrapTapeLoad();
                                if (!loadResult.Success)
                                    throw new InvalidOperationException("BASIC LOAD could not consume a mounted tape block during bootstrap.");
                                ConsumedMountedLoadCount++;

                                if (loadResult.LoadedBasicProgram &&
                                    TryExecuteImmediateSideEffectProgram(
                                        loadResult.LoadedProgramLength,
                                        loadResult.LoadedDataLength,
                                        loadResult.LoadedAutoStartLine))
                                {
                                    break;
                                }
                                else if (loadResult.LoadedBasicProgram &&
                                         TryAdoptLoadedProgram(
                                             loadResult.LoadedProgramLength,
                                             loadResult.LoadedDataLength,
                                             loadResult.LoadedAutoStartLine,
                                             out int adoptedLineIndex))
                                {
                                    forStack.Clear();
                                    variables.Clear();
                                    lineIndex = adoptedLineIndex;
                                    statementIndex = 0;
                                }
                                else if (loadResult.LoadedBasicProgram)
                                {
                                    return;
                                }
                            }
                            break;

                        case "CLEAR":
                        {
                            int clearAddress = EvaluateExpression(statement, 1, statement.Count - 1);
                            ApplyClear(clearAddress);
                            break;
                        }

                        case "POKE":
                        {
                            int commaIndex = statement.IndexOf(",");
                            if (commaIndex <= 1)
                                throw new InvalidOperationException("Malformed BASIC POKE statement in tape bootstrap.");

                            int address = EvaluateExpression(statement, 1, commaIndex - 1);
                            int value = EvaluateExpression(statement, commaIndex + 1, statement.Count - 1);
                            machine.PokeMemory((ushort)address, (byte)value);
                            if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                machine.ForceApply7ffdValue((byte)value);
                            break;
                        }

                        case "DATA":
                            break;

                        case "RESTORE":
                            RestoreDataValues(statement);
                            break;

                        case "READ":
                        {
                            if (statement.Count < 2)
                                throw new InvalidOperationException("Malformed BASIC READ statement in tape bootstrap.");

                            for (int tokenIndex = 1; tokenIndex < statement.Count; tokenIndex++)
                            {
                                string token = statement[tokenIndex];
                                if (token == ",")
                                    continue;

                                if (dataValues.Count == 0)
                                    throw new InvalidOperationException("BASIC READ exhausted DATA values during tape bootstrap.");

                                variables[token] = dataValues.Dequeue();
                            }
                            break;
                        }

                        case "FOR":
                        {
                            int equalsIndex = statement.IndexOf("=");
                            int toIndex = statement.IndexOf("TO");
                            if (statement.Count < 5 || equalsIndex != 2 || toIndex < 4)
                                throw new InvalidOperationException("Malformed BASIC FOR statement in tape bootstrap.");

                            string variableName = statement[1];
                            int startValue = EvaluateExpression(statement, 3, toIndex - 1);
                            int endValue = EvaluateExpression(statement, toIndex + 1, statement.Count - 1);
                            variables[variableName] = startValue;
                            if (startValue > endValue)
                            {
                                SkipLoopBody(ref lineIndex, ref statementIndex);
                                break;
                            }

                            forStack.Push(new ForFrame(variableName, endValue, lineIndex, statementIndex));
                            break;
                        }

                        case "NEXT":
                        {
                            if (forStack.Count == 0)
                                throw new InvalidOperationException("BASIC NEXT encountered without a matching FOR during tape bootstrap.");

                            string variableName = statement.Count > 1 ? statement[1] : forStack.Peek().VariableName;
                            ForFrame frame = forStack.Peek();
                            if (!string.Equals(variableName, frame.VariableName, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Nested BASIC FOR/NEXT mismatch during tape bootstrap.");

                            int nextValue = variables.GetValueOrDefault(frame.VariableName) + 1;
                            variables[frame.VariableName] = nextValue;
                            if (nextValue <= frame.EndValue)
                            {
                                lineIndex = frame.LineIndex;
                                statementIndex = frame.StatementIndex;
                            }
                            else
                            {
                                forStack.Pop();
                            }
                            break;
                        }

                        case "RANDOMIZE":
                        {
                            int usrIndex = statement.IndexOf("USR");
                            if (usrIndex >= 0 && usrIndex + 1 < statement.Count)
                            {
                                int entryPoint = EvaluateExpression(statement, usrIndex + 1, statement.Count - 1);
                                EnterMachineCode((ushort)entryPoint);
                                return;
                            }
                            break;
                        }
                    }
                }

                if (restoreInterpreterWorkspaceOnExit)
                    RestoreInterpreterWorkspaceAfterImmediateProgram();
            }

            private void RestoreDataValues(List<string> statement)
            {
                int? restoreLineNumber = null;
                if (statement.Count > 1)
                    restoreLineNumber = EvaluateExpression(statement, 1, statement.Count - 1);

                dataValues = new Queue<int>(CollectDataValues(restoreLineNumber));
            }

            private void SkipLoopBody(ref int lineIndex, ref int statementIndex)
            {
                int nestedLoopDepth = 0;

                for (int scanLineIndex = lineIndex; scanLineIndex < lines.Count; scanLineIndex++)
                {
                    BasicLine scanLine = lines[scanLineIndex];
                    int scanStatementIndex = scanLineIndex == lineIndex ? statementIndex : 0;

                    for (; scanStatementIndex < scanLine.Statements.Count; scanStatementIndex++)
                    {
                        List<string> statement = scanLine.Statements[scanStatementIndex];
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                                scanStatementIndex = scanLine.Statements.Count;
                                break;

                            case "FOR":
                                nestedLoopDepth++;
                                break;

                            case "NEXT":
                                if (nestedLoopDepth == 0)
                                {
                                    lineIndex = scanLineIndex;
                                    statementIndex = scanStatementIndex + 1;
                                    return;
                                }

                                nestedLoopDepth--;
                                break;
                        }
                    }
                }

                lineIndex = lines.Count;
                statementIndex = 0;
            }

            private void EnterMachineCode(ushort entryPoint)
            {
                // Match the ROM's USR entry shape closely: on entry the machine-code
                // routine sees STACK-BC as its return address, and successful returns
                // back to BASIC expect H'L' to still reference end-calc.
                machine.Cpu.Regs.SP -= 2;
                WriteWord(machine, machine.Cpu.Regs.SP, machine.RomProfile.UsrReturnAddress);
                machine.Cpu.Regs.BC = entryPoint;
                // The ROM loads HL with STACK-BC before it pushes the return
                // address and RETs into the USR target.
                machine.Cpu.Regs.HL = machine.RomProfile.UsrReturnAddress;
                machine.Cpu.Regs.H_ = (byte)(machine.RomProfile.EndCalcLiteralAddress >> 8);
                machine.Cpu.Regs.L_ = (byte)(machine.RomProfile.EndCalcLiteralAddress & 0xFF);
                machine.Cpu.Regs.PC = entryPoint;
            }

            private void UpdateExecutionContext(ushort lineNumber, int statementOrdinal)
            {
                WriteWord(machine, NewPpcAddress, lineNumber);
                machine.PokeMemory(NspPcAddress, (byte)Math.Clamp(statementOrdinal, 0, 255));
            }

            private void UpdateInterpreterPointersForPrint()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                ushort channelDescriptor = ScreenChannelDescriptorAddress;
                WriteWord(machine, CurChlAddress, channelDescriptor);
                WriteWord(machine, KCurAddress, eLine);
                WriteWord(machine, ChAddAddress, eLine);
            }

            private void UpdateInterpreterPointersForDisplay()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                WriteWord(machine, CurChlAddress, ScreenChannelDescriptorAddress);
                WriteWord(machine, KCurAddress, eLine);
                WriteWord(machine, ChAddAddress, eLine);
            }

            private void ApplyPrintSideEffects(BasicLine line, int statementIndex, List<string> statement)
            {
                UpdateInterpreterPointersForPrint();
                ApplyDisplaySideEffects();

                if (statementIndex >= 0 && statementIndex < line.StatementByteOffsets.Count)
                {
                    ushort xPtr = (ushort)(line.DataAddress + line.StatementByteOffsets[statementIndex] + 1);
                    WriteWord(machine, XPtrAddress, xPtr);
                }

                ushort eLine = ReadWord(machine, EditLineAddress);
                string printable = ExtractPrintableText(statement);
                int printableLength = Math.Clamp(printable.Length, 0, 255);

                for (int i = 0; i < printableLength; i++)
                    machine.PokeMemory((ushort)(eLine + i), EncodeSpectrumPrintChar(printable[i]));

                machine.PokeMemory((ushort)(eLine + printableLength), 0x0D);

                ushort workspace = (ushort)(eLine + printableLength + 1);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
            }

            private static string ExtractPrintableText(List<string> statement)
            {
                if (statement.Count == 0)
                    return string.Empty;

                string first = statement[0];
                if (!string.IsNullOrEmpty(first) && first[0] == '?')
                {
                    string inline = first.Length > 1 ? first[1..] : string.Empty;
                    return inline + string.Concat(statement.Skip(1));
                }

                return string.Concat(statement);
            }

            private static byte EncodeSpectrumPrintChar(char ch)
            {
                if (ch >= 32 && ch <= 126)
                    return (byte)ch;

                return ch switch
                {
                    '\r' => 0x0D,
                    '\n' => 0x0D,
                    _ => (byte)'?'
                };
            }

            private void ApplyDisplaySideEffects()
            {
                machine.PokeMemory(FlagsSystemVariableAddress, (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x20));
                machine.PokeMemory(TvFlagSystemVariableAddress, (byte)(machine.PeekMemory(TvFlagSystemVariableAddress) | 0x20));
                UpdateInterpreterPointersForDisplay();
            }

            private static ushort ClampClearTopAddress(int clearAddress)
            {
                // Spectrum BASIC uses CLEAR -1 as the conventional spelling for
                // "all available RAM". Clamp it after applying that signed value.
                if (clearAddress < 0)
                    return 0xFFFE;

                return (ushort)Math.Clamp(clearAddress, 0x5D00, 0xFFFE);
            }

            private static ushort GetWorkspaceAddressForClear(ushort top)
            {
                return (ushort)(top + 1);
            }

            private static ushort GetMachineStackPointerForClear(ushort top)
            {
                return top >= DefaultStackPointer ? DefaultStackPointer : top;
            }

            private static bool ShouldRelocateMachineStackAfterClear(Spectrum128Machine machine, ushort top)
            {
                return machine.Cpu.Regs.SP > top || machine.Cpu.Regs.SP < 0x8000;
            }

            private bool TryAdoptLoadedProgram(
                ushort loadedProgramLength,
                ushort loadedDataLength,
                ushort loadedAutoStartLine,
                out int adoptedLineIndex)
            {
                adoptedLineIndex = -1;
                if (loadedProgramLength == 0)
                    return false;

                List<BasicLine> loadedLines = ParseLines(machine, BasicProgramStart, loadedProgramLength);
                if (loadedLines.Count == 0 ||
                    (!CanExecuteProgram(loadedLines, loadedAutoStartLine) &&
                     !CanExecuteProtectedProgram(loadedLines, loadedAutoStartLine)))
                    return false;

                int startLineIndex = loadedAutoStartLine == 0
                    ? 0
                    : loadedLines.FindIndex(line => line.Number == loadedAutoStartLine);
                if (startLineIndex < 0)
                    return false;

                PrepareLoadedProgramExecutionContext(loadedProgramLength, loadedDataLength);
                lines = loadedLines;
                dataValues = new Queue<int>(CollectDataValues());
                adoptedLineIndex = startLineIndex;
                return true;
            }

            internal bool TryExecuteImmediateSideEffectProgram(
                ushort loadedProgramLength,
                ushort loadedDataLength,
                ushort loadedAutoStartLine,
                bool preserveProtectedInterpreterHandoffCursor = true)
            {
                if (loadedProgramLength == 0)
                    return false;

                List<BasicLine> loadedLines = ParseLines(machine, BasicProgramStart, loadedProgramLength);
                int startLineIndex = loadedAutoStartLine == 0
                    ? 0
                    : loadedLines.FindIndex(line => line.Number == loadedAutoStartLine);
                if (startLineIndex < 0 || !CanExecuteImmediateSideEffectProgram(loadedLines, startLineIndex))
                    return false;

                bool preservesInterpreterHandoff =
                    preserveProtectedInterpreterHandoffCursor &&
                    TouchesProtectedInterpreterHandoff(loadedLines, startLineIndex);
                ushort liveXPtr = ReadWord(machine, XPtrAddress);
                PrepareLoadedProgramExecutionContext(loadedProgramLength, loadedDataLength);
                if (preservesInterpreterHandoff)
                    WriteWord(machine, XPtrAddress, liveXPtr);
                var savedVariables = new Dictionary<string, int>(variables, StringComparer.OrdinalIgnoreCase);
                variables.Clear();
                try
                {
                    ExecuteImmediateSideEffectStatements(loadedLines, startLineIndex);
                    restoreInterpreterWorkspaceOnExit = !preservesInterpreterHandoff;
                    if (!preservesInterpreterHandoff)
                        RestoreInterpreterWorkspaceAfterImmediateProgram();
                    return true;
                }
                finally
                {
                    variables.Clear();
                    foreach (var pair in savedVariables)
                        variables[pair.Key] = pair.Value;
                }
            }

            private void PrepareLoadedProgramExecutionContext(
                ushort loadedProgramLength,
                ushort loadedDataLength)
            {
                ushort programStart = BasicProgramStart;
                ushort varsAddress = (ushort)(programStart + loadedProgramLength);
                ushort variablesEndAddress = (ushort)(programStart + loadedDataLength);
                ushort editLineAddress = (ushort)(variablesEndAddress + 1);
                ushort workspaceAddress = (ushort)(editLineAddress + 1);

                machine.PokeMemory(variablesEndAddress, 0x80);
                machine.PokeMemory(editLineAddress, 0x0D);

                WriteWord(machine, ProgAddress, programStart);
                WriteWord(machine, VarsAddress, varsAddress);
                WriteWord(machine, NextLineAddress, programStart);
                WriteWord(machine, DataAddress, programStart);
                WriteWord(machine, EditLineAddress, editLineAddress);
                WriteWord(machine, WorkspaceAddress, workspaceAddress);
                WriteWord(machine, StackBottomAddress, workspaceAddress);
                WriteWord(machine, StackEndAddress, workspaceAddress);
                InitializeInterpreterPointersForLoadedProgram(machine, editLineAddress);
            }

            private void RestoreInterpreterWorkspaceAfterImmediateProgram()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                machine.PokeMemory(eLine, 0x0D);
                machine.PokeMemory((ushort)(eLine + 1), 0x00);
                WriteWord(machine, WorkspaceAddress, eLine);
                WriteWord(machine, StackBottomAddress, eLine);
                WriteWord(machine, StackEndAddress, eLine);
                InitializeInterpreterPointersForLoadedProgram(machine, eLine);
            }

            private static bool CanExecuteImmediateSideEffectProgram(List<BasicLine> parsedLines, int startLineIndex)
            {
                bool sawExecutableSideEffect = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        if (IsIgnorableProtectedDecorationStatement(statement))
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "CLEAR":
                            case "POKE":
                                sawExecutableSideEffect = true;
                                continue;
                        }

                        return false;
                    }
                }

                return sawExecutableSideEffect;
            }

            private static bool TouchesProtectedInterpreterHandoff(List<BasicLine> parsedLines, int startLineIndex)
            {
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0 || NormalizeStatementKeyword(statement[0]) != "POKE")
                            continue;

                        string rendered = string.Join(" ", statement);
                        if (rendered.Contains("PEEK 23641", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23642", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23633", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23634", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23618", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23619", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23621", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23647", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23648", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23649", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23650", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private void ExecuteImmediateSideEffectStatements(List<BasicLine> parsedLines, int startLineIndex)
            {
                int stepCount = 0;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count && stepCount++ < 10000; lineIndex++)
                {
                    BasicLine line = parsedLines[lineIndex];
                    for (int statementIndex = 0; statementIndex < line.Statements.Count; statementIndex++)
                    {
                        List<string> statement = line.Statements[statementIndex];
                        if (statement.Count == 0)
                            continue;

                        UpdateExecutionContext(line.Number, statementIndex + 1);
                        if (IsIgnorableProtectedDecorationStatement(statement))
                        {
                            ApplyDisplaySideEffects();
                            continue;
                        }

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                                goto NextLine;

                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                                ApplyDisplaySideEffects();
                                break;

                            case "?":
                                ApplyPrintSideEffects(line, statementIndex, statement);
                                break;

                            case "CLEAR":
                            {
                                int clearAddress = EvaluateExpression(statement, 1, statement.Count - 1);
                                ApplyClear(clearAddress);
                                break;
                            }

                            case "POKE":
                            {
                                int commaIndex = statement.IndexOf(",");
                                if (commaIndex <= 1)
                                    throw new InvalidOperationException("Malformed BASIC POKE statement in protected bootstrap stage.");

                                int address = EvaluateExpression(statement, 1, commaIndex - 1);
                                int value = EvaluateExpression(statement, commaIndex + 1, statement.Count - 1);
                                machine.PokeMemory((ushort)address, (byte)value);
                                if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                    machine.ForceApply7ffdValue((byte)value);
                                break;
                            }

                            default:
                                throw new InvalidOperationException($"Unsupported BASIC statement '{keyword}' in protected bootstrap stage.");
                        }
                    }

                NextLine:
                    continue;
                }
            }

            private static bool IsIgnorableProtectedDecorationStatement(List<string> statement)
            {
                if (statement.Count == 0)
                    return true;

                foreach (string token in statement)
                {
                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    string keyword = NormalizeStatementKeyword(token);
                    if (keyword is "?" or "REM" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or "POKE")
                        return false;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                        return false;

                    for (int i = 0; i < token.Length; i++)
                    {
                        char c = token[i];
                        if (!IsAsciiLetterOrDigit(c))
                            return false;
                    }
                }

                return true;
            }

            private static bool IsKnownBasicKeywordToken(string token)
            {
                string keyword = NormalizeStatementKeyword(token);
                return keyword is
                    "?" or "REM" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or
                    "POKE" or "LOAD" or "DATA" or "READ" or "FOR" or "NEXT" or
                    "RANDOMIZE" or "PAUSE" or "RETURN" or "RESTORE" or "USR" or
                    "LET" or "THEN" or
                    "CODE" or "PEEK" or "TO" or "PRINT";
            }

            private static bool CanExecuteProgram(List<BasicLine> parsedLines, ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawExecutableStatement = false;

                // Repeated or non-monotonic line numbers usually mean a protected or
                // interpreter-sensitive chained loader stage. Let the real ROM path
                // continue with those instead of trying to fake-execute them.
                for (int lineIndex = startLineIndex + 1; lineIndex < parsedLines.Count; lineIndex++)
                {
                    if (parsedLines[lineIndex].Number <= parsedLines[lineIndex - 1].Number)
                        return false;
                }

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "LOAD":
                            case "CLEAR":
                            case "POKE":
                            case "DATA":
                            case "READ":
                            case "RESTORE":
                            case "FOR":
                            case "NEXT":
                            case "RANDOMIZE":
                                sawExecutableStatement = true;
                                continue;
                        }

                        return false;
                    }
                }

                return sawExecutableStatement;
            }

            private static bool CanExecuteProtectedProgram(List<BasicLine> parsedLines, ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "LOAD":
                            case "CLEAR":
                            case "POKE":
                            case "DATA":
                            case "READ":
                            case "FOR":
                            case "NEXT":
                            case "RANDOMIZE":
                                continue;
                        }

                        return false;
                    }
                }

                return true;
            }

            private void ApplyClear(int clearAddress)
            {
                ushort top = ClampClearTopAddress(clearAddress);
                ushort workspace = GetWorkspaceAddressForClear(top);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
                if (ShouldRelocateMachineStackAfterClear(machine, top))
                    machine.Cpu.Regs.SP = GetMachineStackPointerForClear(top);
            }

            private IEnumerable<int> CollectDataValues(int? restoreLineNumber = null)
            {
                bool collecting = restoreLineNumber == null;
                foreach (BasicLine line in lines)
                {
                    if (!collecting && restoreLineNumber.HasValue && line.Number == restoreLineNumber.Value)
                        collecting = true;

                    if (!collecting)
                        continue;

                    foreach (List<string> statement in line.Statements)
                    {
                        if (statement.Count == 0 || statement[0] != "DATA")
                            continue;

                        int itemStart = 1;
                        while (itemStart < statement.Count)
                        {
                            int itemEnd = itemStart;
                            while (itemEnd < statement.Count && statement[itemEnd] != ",")
                                itemEnd++;

                            if (itemEnd > itemStart)
                                yield return EvaluateExpression(statement, itemStart, itemEnd - 1);

                            itemStart = itemEnd + 1;
                        }
                    }
                }
            }

            private int EvaluateExpression(List<string> tokens, int startIndex, int endIndex)
            {
                var parser = new ExpressionParser(machine, variables, tokens, startIndex, endIndex);
                return parser.Parse();
            }

            private static List<BasicLine> ParseLines(Spectrum128Machine machine, ushort programStart, ushort programLength)
            {
                var parsedLines = new List<BasicLine>();
                ushort cursor = programStart;
                ushort endAddress = (ushort)(programStart + programLength);

                while (cursor < endAddress)
                {
                    ushort lineNumber = (ushort)((machine.PeekMemory(cursor) << 8) | machine.PeekMemory((ushort)(cursor + 1)));
                    ushort lineLength = ReadWord(machine, (ushort)(cursor + 2));
                    ushort lineDataAddress = (ushort)(cursor + 4);
                    ushort nextLineAddress = (ushort)(lineDataAddress + lineLength);
                    if (nextLineAddress > endAddress)
                        break;

                    byte[] lineBytes = new byte[lineLength];
                    for (int i = 0; i < lineLength; i++)
                        lineBytes[i] = machine.PeekMemory((ushort)(lineDataAddress + i));

                    (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) = SplitStatements(Tokenize(lineBytes));
                    parsedLines.Add(new BasicLine(lineNumber, statements, statementByteOffsets, statementTokenSpans, lineDataAddress));
                    cursor = nextLineAddress;
                }

                return parsedLines;
            }

            private static (List<List<string>> Statements, List<int> StatementByteOffsets, List<List<TokenSpan>> StatementTokenSpans) SplitStatements(List<TokenSpan> tokens)
            {
                var statements = new List<List<string>>();
                var statementByteOffsets = new List<int>();
                var statementTokenSpans = new List<List<TokenSpan>>();
                var current = new List<string>();
                var currentTokenSpans = new List<TokenSpan>();
                int currentByteOffset = 0;
                bool sawTokenInStatement = false;
                foreach (TokenSpan token in tokens)
                {
                    if (token.Text == ":")
                    {
                        statements.Add(current);
                        statementByteOffsets.Add(currentByteOffset);
                        statementTokenSpans.Add(currentTokenSpans);
                        current = new List<string>();
                        currentTokenSpans = new List<TokenSpan>();
                        currentByteOffset = 0;
                        sawTokenInStatement = false;
                    }
                    else
                    {
                        if (!sawTokenInStatement)
                        {
                            currentByteOffset = token.ByteOffset;
                            sawTokenInStatement = true;
                        }

                        current.Add(token.Text);
                        currentTokenSpans.Add(token);
                    }
                }

                statements.Add(current);
                statementByteOffsets.Add(currentByteOffset);
                statementTokenSpans.Add(currentTokenSpans);
                return (statements, statementByteOffsets, statementTokenSpans);
            }

            private static List<TokenSpan> Tokenize(byte[] lineBytes)
            {
                var tokens = new List<TokenSpan>();
                for (int i = 0; i < lineBytes.Length; i++)
                {
                    byte b = lineBytes[i];
                    if (b == 0x0D)
                        break;

                    if (b == 0x0E)
                    {
                        i += 5;
                        continue;
                    }

                    if (TryGetKeywordToken(b, out string? keyword))
                    {
                        tokens.Add(new TokenSpan(keyword!, i));
                        continue;
                    }

                    char c = (char)b;
                    if (IsAsciiDigit(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && IsAsciiDigit((char)lineBytes[i + 1]))
                            i++;

                        string numericToken = System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1);
                        if (TryDecodeSpectrumNumberMarker(lineBytes, i + 1, out int encodedValue))
                        {
                            numericToken = encodedValue.ToString();
                            i += SpectrumNumberMarkerLength;
                        }

                        tokens.Add(new TokenSpan(numericToken, start));
                        continue;
                    }

                    if (IsAsciiLetter(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && IsAsciiLetterOrDigit((char)lineBytes[i + 1]))
                            i++;
                        tokens.Add(new TokenSpan(System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1), start));
                        continue;
                    }

                    if (c == '"')
                    {
                        int start = ++i;
                        while (i < lineBytes.Length && (char)lineBytes[i] != '"')
                            i++;
                        tokens.Add(new TokenSpan(System.Text.Encoding.ASCII.GetString(lineBytes, start, Math.Max(0, i - start)), start));
                        continue;
                    }

                    if ("()+-*=,:".IndexOf(c) >= 0)
                        tokens.Add(new TokenSpan(c.ToString(), i));
                }

                return tokens;
            }

            private const int SpectrumNumberMarkerLength = 5;

            private static bool TryDecodeSpectrumNumberMarker(byte[] lineBytes, int markerOffset, out int value)
            {
                value = 0;
                if (markerOffset < 0 ||
                    markerOffset + SpectrumNumberMarkerLength >= lineBytes.Length ||
                    lineBytes[markerOffset] != 0x0E)
                {
                    return false;
                }

                byte exponent = lineBytes[markerOffset + 1];
                byte sign = lineBytes[markerOffset + 2];
                byte low = lineBytes[markerOffset + 3];
                byte high = lineBytes[markerOffset + 4];
                byte trailing = lineBytes[markerOffset + 5];

                // Small integers are encoded as an exponent byte of zero followed by a
                // sign byte, the 16-bit integer payload, and a zero terminator.
                if (exponent == 0x00 && trailing == 0x00)
                {
                    short integerValue = unchecked((short)(low | (high << 8)));
                    value = integerValue;
                    return true;
                }

                return false;
            }

            private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

            private static bool IsAsciiLetter(char c) =>
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z');

            private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);

            public static bool RequiresMountedLoadSemantics(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawPreparatorySideEffects = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "RANDOMIZE" && statement.IndexOf("USR") >= 0)
                            return false;

                        if (keyword == "LOAD")
                        {
                            if (sawPreparatorySideEffects || !IsSimpleAnonymousCodeLoad(statement))
                                return true;
                            continue;
                        }

                        if (keyword == "POKE" || keyword == "READ" || keyword == "DATA")
                            sawPreparatorySideEffects = true;
                    }
                }

                return false;
            }

            public static bool CanHandleLoadedProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                if (parsedLines.Count == 0)
                    return false;

                if (CanExecuteProgram(parsedLines, autoStartLine) || CanExecuteProtectedProgram(parsedLines, autoStartLine))
                    return true;

                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                return startLineIndex >= 0 && CanExecuteImmediateSideEffectProgram(parsedLines, startLineIndex);
            }

            public static bool CanRunPreloadedProgramUnderRom(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count > 0 && NormalizeStatementKeyword(statement[0]) == "LOAD")
                            return false;
                    }
                }

                return true;
            }

            public static bool CanExecuteImmediateSideEffectProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                return startLineIndex >= 0 && CanExecuteImmediateSideEffectProgram(parsedLines, startLineIndex);
            }

            public static bool ShouldDeferToRomForMountedLoadProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawLoad = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                            case "?":
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "CLEAR":
                                break;

                            case "LOAD":
                                sawLoad = true;
                                break;

                            default:
                                return false;
                        }
                    }
                }

                return sawLoad;
            }

            public static bool RequiresRomDrivenMountedLoadedProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                return TouchesProtectedInterpreterHandoff(parsedLines, startLineIndex);
            }

            public static bool Requires128kTapeLoadMode(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var variables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var dataValues = new Queue<int>(CollectDataValues(parsedLines, machine, variables));

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        switch (statement[0])
                        {
                            case "READ":
                                if (statement.Count >= 2 && dataValues.Count > 0)
                                    variables[statement[1]] = dataValues.Dequeue();
                                break;

                            case "POKE":
                            {
                                int commaIndex = statement.IndexOf(",");
                                if (commaIndex <= 1)
                                    break;

                                var parser = new ExpressionParser(machine, variables, statement, 1, commaIndex - 1);
                                if (parser.Parse() == Spectrum128TapeLoadBankSelectAddress)
                                    return true;
                                break;
                            }

                            case "RANDOMIZE":
                                if (statement.IndexOf("USR") >= 0)
                                    return false;
                                break;
                        }
                    }
                }

                return false;
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                return CreateMountedLoadUsrContinuationResolver(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    requireUsrReturnAddressBetweenSteps: true);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                bool requireUsrReturnAddressBetweenSteps = true)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        return null;
                    }

                    continuationSteps.Add((MountedUsrContinuationStep)initialStep);
                }

                return CreateMountedUsrContinuationResolver(
                    continuationSteps,
                    0,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    programStart,
                    programLength,
                    requireUsrReturnAddressBetweenSteps);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadRomBasicContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                MountedUsrContinuationStep firstStep;
                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        if (!TryBuildInitialRomBasicResumeStep(
                                machine,
                                programStart,
                                programLength,
                                autoStartLine,
                                out MountedUsrContinuationStep? romResumeStep) ||
                            romResumeStep is null)
                        {
                            return null;
                        }

                        firstStep = (MountedUsrContinuationStep)romResumeStep;
                    }
                    else
                    {
                        firstStep = (MountedUsrContinuationStep)initialStep;
                    }
                }
                else
                {
                    firstStep = continuationSteps[0];
                }

                return currentMachine =>
                {
                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, firstStep);
                    currentMachine.SetPendingMountedLoadBasicResume(
                        firstStep.LineNumber,
                        (byte)Math.Clamp(firstStep.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static bool TryBuildMountedUsrContinuationSteps(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out List<MountedUsrContinuationStep> continuationSteps)
            {
                continuationSteps = new List<MountedUsrContinuationStep>();
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawInitialUsr = false;
                List<List<string>> pendingPreamble = new();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int parsedStatementIndex = 0; parsedStatementIndex < parsedLines[lineIndex].Statements.Count; parsedStatementIndex++)
                    {
                        List<string> statement = parsedLines[lineIndex].Statements[parsedStatementIndex];
                        int usrClauseOrdinal = 0;
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(parsedLines[lineIndex].StatementTokenSpans[parsedStatementIndex]))
                        {
                            if (clauseTokens.Count == 0)
                                continue;

                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (!sawInitialUsr)
                            {
                                if (TryGetRandomizeUsrExpressionTokens(clause, out _))
                                {
                                    sawInitialUsr = true;
                                    continue;
                                }

                                if (IsSafeUsrContinuationPreambleStatement(clause))
                                    continue;

                                return false;
                            }

                            if (TryGetRandomizeUsrExpressionInfo(clauseTokens, out List<string> expressionTokens, out int expressionByteOffset))
                            {
                                continuationSteps.Add(new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    parsedStatementIndex + 1,
                                    usrClauseOrdinal,
                                    parsedLines[lineIndex].StatementByteOffsets[parsedStatementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray()));
                                usrClauseOrdinal++;
                                pendingPreamble.Clear();
                                continue;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            return false;
                        }
                    }
                }

                return true;
            }

            private static bool TryBuildInitialMountedUsrContinuationStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var pendingPreamble = new List<List<string>>();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(statementTokens))
                        {
                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (TryGetRandomizeUsrExpressionInfo(
                                clauseTokens,
                                out List<string> expressionTokens,
                                out int expressionByteOffset))
                            {
                                continuationStep = new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    statementIndex,
                                    0,
                                    parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray());
                                return true;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            return false;
                        }
                    }
                }

                return false;
            }

            private static bool TryBuildInitialRomBasicResumeStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    if (parsedLines[lineIndex].StatementTokenSpans.Count == 0)
                        continue;

                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        if (statementTokens.Count == 0)
                            continue;

                        continuationStep = new MountedUsrContinuationStep(
                            parsedLines[lineIndex].Number,
                            statementIndex,
                            0,
                            parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                            statementTokens[0].ByteOffset,
                            statementTokens[0].ByteOffset,
                            parsedLines[lineIndex].DataAddress,
                            Array.Empty<string[]>(),
                            Array.Empty<string>());
                        return true;
                    }
                }

                return false;
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrBasicResumeResolver(
                MountedUsrContinuationStep step,
                Func<Spectrum128Machine, ushort?>? followOnResolver = null,
                Func<Spectrum128Machine, bool>? canResume = null)
            {
                return currentMachine =>
                {
                    if (canResume != null && !canResume(currentMachine))
                    {
                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                        return null;
                    }

                    if (followOnResolver != null)
                    {
                        // A follow-on step after a ROM-driven BASIC resume should not fire
                        // from the intermediate ROM callback/service loop that the resumed
                        // USR routine may use internally. It should wait until the prior
                        // USR genuinely returns through STACK-BC.
                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                            followOnResolver);
                    }

                    currentMachine.SetPendingMountedLoadBasicResume(
                        step.LineNumber,
                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrContinuationResolver(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int stepIndex,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                bool requireUsrReturnAddressBetweenSteps)
            {
                return currentMachine =>
                {
                    int currentStepIndex = stepIndex;
                    bool allowCachedLiteralUsrZeroFallback = false;
                    while ((uint)currentStepIndex < (uint)steps.Count)
                    {
                        MountedUsrContinuationStep step = steps[currentStepIndex];
                        try
                        {
                            bool hasConditionalPreamble = ContainsConditionalUsrContinuationPreamble(step.PreambleStatements);
                            if (ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step))
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            bool? romBasicResumeDecision = ShouldUseRomBasicResumeForMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step,
                                allowCachedLiteralUsrZeroFallback);

                            if (hasConditionalPreamble)
                            {
                                allowCachedLiteralUsrZeroFallback = false;

                                if (!romBasicResumeDecision.HasValue)
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    return null;
                                }

                                if (romBasicResumeDecision.Value)
                                {
                                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                    if (currentStepIndex + 1 < steps.Count)
                                    {
                                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                            requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                    }
                                    else
                                    {
                                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    }

                                    currentMachine.SetPendingMountedLoadBasicResume(
                                        step.LineNumber,
                                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                    return ushort.MaxValue;
                                }
                            }

                            MountedUsrContinuationPreambleResult preambleResult =
                                ExecuteMountedUsrContinuationPreamble(currentMachine, variables, step.PreambleStatements);
                            if (preambleResult == MountedUsrContinuationPreambleResult.SkipStep)
                            {
                                allowCachedLiteralUsrZeroFallback = true;
                                currentStepIndex++;
                                continue;
                            }

                            allowCachedLiteralUsrZeroFallback = false;

                            if (!romBasicResumeDecision.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (romBasicResumeDecision.Value)
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            if (currentStepIndex + 1 < steps.Count)
                            {
                                currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                    CreateMountedUsrContinuationResolver(
                                        steps,
                                        currentStepIndex + 1,
                                        variables,
                                        programStart,
                                        programLength,
                                        requireUsrReturnAddressBetweenSteps),
                                    requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                            }
                            else
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                            }

                            bool preserveLiveInterpreterStateForDirectUsr =
                                ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                                    currentMachine,
                                    programStart,
                                    programLength,
                                    step) ||
                                ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                                    steps,
                                    currentStepIndex);
                            ushort? entryPoint = EvaluateMountedUsrContinuationExpression(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step);

                            if (!entryPoint.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (entryPoint.Value == 0)
                                return 0;

                            UpdateMountedUsrContinuationDirectUsrContext(
                                currentMachine,
                                step,
                                preserveLiveInterpreterStateForDirectUsr);
                            return entryPoint.Value;
                        }
                        catch
                        {
                            currentMachine.ClearPendingMountedLoadUsrContinuation();
                            return null;
                        }
                    }

                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                    return null;
                };
            }

            private static bool? ShouldUseRomBasicResumeForMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                bool allowCachedLiteralUsrZeroFallback = false)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                bool hasLiveTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);
                if (hasLiveTokens && refreshedTokens is { Length: > 0 })
                    expressionTokens = refreshedTokens;

                if (hasLiveTokens &&
                    ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens))
                {
                    return true;
                }

                if (ContainsConditionalUsrContinuationPreamble(step.PreambleStatements) &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return true;
                }

                if (expressionTokens.Count == 1 &&
                    string.Equals(expressionTokens[0], "0", StringComparison.Ordinal))
                {
                    if (!hasLiveTokens && !allowCachedLiteralUsrZeroFallback)
                        return null;

                    return false;
                }

                return ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens);
            }

            private static bool ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                if (!ContainsConditionalUsrContinuationPreamble(step.PreambleStatements))
                    return false;

                if (CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                    return false;

                return TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out _);
            }

            private static bool ShouldPreferRomBasicResumeForLiveMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (CanEvaluateLiveMountedUsrStepDirectly(machine, variables, step, expressionTokens))
                    return false;

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool CanEvaluateLiveMountedUsrStepDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (step.PreambleStatements.Length != 0 &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return false;
                }

                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                if (TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens) &&
                    refreshedTokens is { Length: > 0 })
                {
                    expressionTokens = refreshedTokens;
                }

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int currentStepIndex)
            {
                for (int i = currentStepIndex + 1; i < steps.Count; i++)
                {
                    MountedUsrContinuationStep followOnStep = steps[i];
                    if (ContainsConditionalUsrContinuationPreamble(followOnStep.PreambleStatements) ||
                        followOnStep.PreambleStatements.Length != 0 ||
                        !IsSimpleLiteralUsrExpression(followOnStep.ExpressionTokens))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool ContainsConditionalUsrContinuationPreamble(IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length != 0 &&
                        NormalizeStatementKeyword(clause[0]) == "IF")
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool CanEvaluateMountedUsrPreambleDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    if (keyword == "IF")
                    {
                        if (!CanEvaluateMountedUsrContinuationIfConditionDirectly(machine, variables, clause))
                            return false;
                        continue;
                    }

                    if (keyword == "LET")
                    {
                        int equalsIndex = Array.IndexOf(clause, "=");
                        if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                            machine,
                            variables,
                            clause.Skip(equalsIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "CLEAR")
                    {
                        if (clause.Length > 1 &&
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "POKE")
                    {
                        int commaIndex = Array.IndexOf(clause, ",");
                        if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).Take(commaIndex - 1).ToArray()) ||
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(commaIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!IsSafeUsrContinuationPreambleStatement(new List<string>(clause)))
                        return false;
                }

                return true;
            }

            private static bool CanEvaluateMountedUsrContinuationIfConditionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    return false;

                string[] left = clause.Skip(1).Take(equalsIndex - 1).ToArray();
                string[] right = clause.Skip(equalsIndex + 1).ToArray();
                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, left) &&
                       CanEvaluateMountedUsrExpressionDirectly(machine, variables, right);
            }

            private static bool CanEvaluateMountedUsrExpressionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens)
            {
                if (!IsSafeUsrContinuationExpression(expressionTokens))
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token) ||
                        token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                    {
                        continue;
                    }

                    if (NormalizeStatementKeyword(token) == "PEEK")
                        continue;

                    if (int.TryParse(token, out _))
                        continue;

                    if (variables.ContainsKey(token))
                        continue;

                    if (TryReadMountedContinuationNumericVariable(machine, token, out _))
                        continue;

                    // Spectrum BASIC numeric variables read as zero until assigned.
                    // Treating an unresolved safe variable token as evaluable keeps
                    // direct mounted-continuation IF/THEN logic aligned with the ROM
                    // instead of needlessly forcing a ROM BASIC resume.
                    continue;
                }

                return true;
            }

            private static bool IsSimpleLiteralUsrExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count != 1)
                    return false;

                string token = expressionTokens[0];
                if (string.IsNullOrEmpty(token))
                    return false;

                for (int i = 0; i < token.Length; i++)
                {
                    if (!char.IsAsciiDigit(token[i]))
                        return false;
                }

                return true;
            }

            private static bool IsSafeUsrContinuationExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count == 0)
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                        continue;

                    string normalized = NormalizeStatementKeyword(token);
                    if (normalized == "PEEK")
                        continue;

                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    for (int i = 0; i < token.Length; i++)
                    {
                        if (!IsAsciiLetterOrDigit(token[i]))
                            return false;
                    }
                }

                return true;
            }

            private static ushort? EvaluateMountedUsrContinuationExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                bool hasRefreshedTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);

                if (hasRefreshedTokens &&
                    TryEvaluateMountedUsrExpression(machine, variables, refreshedTokens!, out ushort? refreshedValue))
                {
                    return refreshedValue;
                }

                return TryEvaluateMountedUsrExpression(machine, variables, step.ExpressionTokens, out ushort? fallbackValue)
                    ? fallbackValue
                    : null;
            }

            private static bool TryEvaluateMountedUsrExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens,
                out ushort? value)
            {
                value = null;
                if (expressionTokens.Count == 0)
                    return false;

                try
                {
                    var parser = new ExpressionParser(
                        machine,
                        variables,
                        expressionTokens.ToList(),
                        0,
                        expressionTokens.Count - 1,
                        requireVariableResolution: true);
                    int parsedValue = parser.Parse();
                    if ((uint)parsedValue > ushort.MaxValue)
                        return false;

                    value = (ushort)parsedValue;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryRefreshMountedUsrExpressionTokens(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;
                if (TryRefreshMountedUsrExpressionTokensFromLiveLine(machine, step, out expressionTokens))
                    return true;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int lineIndex = parsedLines.FindIndex(line => line.Number == step.LineNumber);
                if (lineIndex < 0)
                    return false;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)parsedLines[lineIndex].Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(parsedLines[lineIndex].Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryRefreshMountedUsrExpressionTokensFromLiveLine(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;

                if (!TryParseLiveBasicLine(machine, step.LineDataAddress, step.LineNumber, out BasicLine? parsedLine) ||
                    parsedLine is null)
                    return false;
                BasicLine liveLine = (BasicLine)parsedLine;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)liveLine.Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(liveLine.Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryParseLiveBasicLine(
                Spectrum128Machine machine,
                ushort lineDataAddress,
                ushort expectedLineNumber,
                out BasicLine? line)
            {
                line = null;
                if (lineDataAddress < 4)
                    return false;

                ushort lineHeaderAddress = (ushort)(lineDataAddress - 4);
                ushort liveLineNumber = (ushort)(
                    (machine.PeekMemory(lineHeaderAddress) << 8) |
                    machine.PeekMemory((ushort)(lineHeaderAddress + 1)));
                if (liveLineNumber != expectedLineNumber)
                    return false;

                ushort lineLength = ReadWord(machine, (ushort)(lineHeaderAddress + 2));
                if (lineLength == 0 || lineLength > 1024)
                    return false;

                byte[] lineBytes = new byte[lineLength];
                for (int i = 0; i < lineLength; i++)
                    lineBytes[i] = machine.PeekMemory((ushort)(lineDataAddress + i));

                (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) =
                    SplitStatements(Tokenize(lineBytes));
                line = new BasicLine(liveLineNumber, statements, statementByteOffsets, statementTokenSpans, lineDataAddress);
                return true;
            }

            private static void UpdateMountedUsrContinuationRomResumeContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool suppressCursorOverride = false)
            {
                WriteWord(machine, NewPpcAddress, step.LineNumber);
                machine.PokeMemory(NspPcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));
                WriteWord(machine, PpcAddress, step.LineNumber);
                machine.PokeMemory(SubPpcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));

                if (!suppressCursorOverride)
                {
                    ushort chAdd = (ushort)(step.LineDataAddress + step.ClauseStartByteOffset);
                    machine.SetPendingMountedLoadResumeCursorOverride(chAdd: chAdd);
                }
            }

            private static void UpdateMountedUsrContinuationDirectUsrContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool preserveLiveInterpreterState)
            {
                UpdateMountedUsrContinuationRomResumeContext(
                    machine,
                    step,
                    suppressCursorOverride: preserveLiveInterpreterState);
                machine.SetPendingMountedLoadDirectUsrContextPolicy(preserveLiveInterpreterState);
                if (preserveLiveInterpreterState)
                    return;

                ushort eLine = ReadWord(machine, EditLineAddress);
                ushort chAdd = (ushort)(step.LineDataAddress + step.ExpressionByteOffset + 1);
                machine.SetPendingMountedLoadResumeCursorOverride(kCur: eLine, chAdd: chAdd, xPtr: chAdd);
            }

            private enum MountedUsrContinuationPreambleResult
            {
                ContinueStep,
                SkipStep
            }

            private static MountedUsrContinuationPreambleResult ExecuteMountedUsrContinuationPreamble(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    switch (keyword)
                    {
                        case "LET":
                        {
                            int equalsIndex = Array.IndexOf(clause, "=");
                            if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed LET statement in mounted USR continuation.");

                            string variableName = clause[1];
                            var parser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                equalsIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int value = parser.Parse();
                            variables[variableName] = value;
                            TryUpdateMountedUsrContinuationBasicVariable(machine, variableName, value);
                            break;
                        }

                        case "IF":
                        {
                            if (!EvaluateMountedUsrContinuationIfCondition(machine, variables, clause))
                                return MountedUsrContinuationPreambleResult.SkipStep;
                            break;
                        }

                        case "REM":
                        case "?":
                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            ApplyMountedContinuationDisplaySideEffects(machine);
                            break;

                        case "CLEAR":
                        {
                            if (clause.Length > 1)
                            {
                                var parser = new ExpressionParser(
                                    machine,
                                    variables,
                                    new List<string>(clause),
                                    1,
                                    clause.Length - 1,
                                    requireVariableResolution: true);
                                ApplyMountedContinuationClear(machine, parser.Parse());
                            }
                            else
                            {
                                ApplyMountedContinuationDisplaySideEffects(machine);
                            }
                            break;
                        }

                        case "POKE":
                        {
                            int commaIndex = Array.IndexOf(clause, ",");
                            if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed POKE statement in mounted USR continuation.");

                            var addressParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                1,
                                commaIndex - 1,
                                requireVariableResolution: true);
                            var valueParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                commaIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int address = addressParser.Parse();
                            int value = valueParser.Parse();
                            machine.PokeMemory((ushort)address, (byte)value);
                            if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                machine.ForceApply7ffdValue((byte)value);
                            break;
                        }

                        default:
                            throw new InvalidOperationException($"Unsupported mounted USR continuation preamble '{keyword}'.");
                    }
                }

                return MountedUsrContinuationPreambleResult.ContinueStep;
            }

            private static bool EvaluateMountedUsrContinuationIfCondition(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    throw new InvalidOperationException("Malformed IF statement in mounted USR continuation.");

                var leftParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    1,
                    equalsIndex - 1,
                    requireVariableResolution: true);
                var rightParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    equalsIndex + 1,
                    clause.Count - 1,
                    requireVariableResolution: true);
                return leftParser.Parse() == rightParser.Parse();
            }

            private static void ApplyMountedContinuationDisplaySideEffects(Spectrum128Machine machine)
            {
                machine.PokeMemory(FlagsSystemVariableAddress, (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x20));
                machine.PokeMemory(TvFlagSystemVariableAddress, (byte)(machine.PeekMemory(TvFlagSystemVariableAddress) | 0x20));
            }

            private static void ApplyMountedContinuationClear(Spectrum128Machine machine, int clearAddress)
            {
                ushort top = ClampClearTopAddress(clearAddress);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                ushort workspace = GetWorkspaceAddressForClear(top);
                WriteWord(machine, VarsAddress, workspace);
                WriteWord(machine, EditLineAddress, workspace);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
                WriteWord(machine, KCurAddress, workspace);
                WriteWord(machine, ChAddAddress, workspace);
                if (ShouldRelocateMachineStackAfterClear(machine, top))
                    machine.Cpu.Regs.SP = GetMachineStackPointerForClear(top);
            }

            private static void TryUpdateMountedUsrContinuationBasicVariable(
                Spectrum128Machine machine,
                string variableName,
                int value)
            {
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return;

                if (value < short.MinValue || value > ushort.MaxValue)
                    return;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        WriteSpectrumSmallInteger(machine, (ushort)(current + 1), value);
                        return;
                    }

                    current = (ushort)(current + entryLength);
                }
            }

            private static int GetBasicVariableEntryLength(
                Spectrum128Machine machine,
                ushort address,
                ushort eLineAddress)
            {
                if (address >= eLineAddress)
                    return 0;

                byte header = machine.PeekMemory(address);
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0x80:
                    case 0xC0:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0xA0:
                    {
                        ushort current = (ushort)(address + 1);
                        while (current < eLineAddress)
                        {
                            byte nameByte = machine.PeekMemory(current);
                            current++;
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > eLineAddress)
                                    return 0;
                                return current - address + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private static void WriteSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                int value)
            {
                short signedValue = (short)value;
                ushort magnitude = (ushort)signedValue;
                byte sign = signedValue < 0 ? (byte)0xFF : (byte)0x00;

                machine.PokeMemory(address, 0x00);
                machine.PokeMemory((ushort)(address + 1), sign);
                machine.PokeMemory((ushort)(address + 2), (byte)(magnitude & 0xFF));
                machine.PokeMemory((ushort)(address + 3), (byte)(magnitude >> 8));
                machine.PokeMemory((ushort)(address + 4), 0x00);
            }

            private static bool TryReadMountedContinuationNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                if (TryReadLiveBasicNumericVariable(machine, variableName, out value))
                    return true;

                return TryReadPreservedMountedLoadBasicNumericVariable(machine, variableName, out value);
            }

            private static bool TryReadLiveBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(machine, (ushort)(current + 1), out value);
                    }

                    current = (ushort)(current + entryLength);
                }

                return false;
            }

            private static bool TryReadPreservedMountedLoadBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                if (!machine.TryGetPendingMountedLoadBasicVariableSnapshot(out _, out byte[] data) ||
                    data.Length == 0)
                {
                    return false;
                }

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));
                int index = 0;
                while (index < data.Length)
                {
                    byte header = data[index];
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(data, index);
                    if (entryLength <= 0 || index + entryLength > data.Length)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(data, index + 1, out value);
                    }

                    index += entryLength;
                }

                return false;
            }

            private static bool TryReadSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                out int value)
            {
                value = 0;
                byte exponent = machine.PeekMemory(address);
                byte sign = machine.PeekMemory((ushort)(address + 1));
                byte low = machine.PeekMemory((ushort)(address + 2));
                byte high = machine.PeekMemory((ushort)(address + 3));
                byte trailing = machine.PeekMemory((ushort)(address + 4));
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryReadSpectrumSmallInteger(
                byte[] data,
                int index,
                out int value)
            {
                value = 0;
                if (index < 0 || index + 4 >= data.Length)
                    return false;

                byte exponent = data[index];
                byte sign = data[index + 1];
                byte low = data[index + 2];
                byte high = data[index + 3];
                byte trailing = data[index + 4];
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryDecodeSpectrumNumericValue(
                byte exponent,
                byte signOrMantissa,
                byte third,
                byte fourth,
                byte fifth,
                out int value)
            {
                value = 0;

                if (exponent == 0x00)
                {
                    if (fifth != 0x00)
                        return false;

                    short signedValue = unchecked((short)(third | (fourth << 8)));
                    value = signOrMantissa == 0xFF ? -Math.Abs(signedValue) : signedValue;
                    return true;
                }

                uint mantissa =
                    (uint)(((signOrMantissa & 0x7F) | 0x80) << 24) |
                    (uint)(third << 16) |
                    (uint)(fourth << 8) |
                    fifth;
                int sign = (signOrMantissa & 0x80) != 0 ? -1 : 1;
                double numericValue = sign * mantissa * Math.Pow(2.0, exponent - 160);
                double roundedValue = Math.Round(numericValue);
                if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
                    return false;

                if (Math.Abs(numericValue - roundedValue) > 1e-6)
                    return false;

                value = (int)roundedValue;
                return true;
            }

            private static int GetBasicVariableEntryLength(byte[] data, int index)
            {
                if ((uint)index >= (uint)data.Length)
                    return 0;

                byte header = data[index];
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    case 0x80:
                    case 0xC0:
                    {
                        if (index + 2 >= data.Length)
                            return 0;

                        ushort length = (ushort)(data[index + 1] | (data[index + 2] << 8));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0xA0:
                    {
                        int current = index + 1;
                        while (current < data.Length)
                        {
                            byte nameByte = data[current++];
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > data.Length)
                                    return 0;

                                return current - index + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private sealed record MountedUsrContinuationStep(
                ushort LineNumber,
                int StatementIndex,
                int UsrClauseOrdinal,
                int StatementStartByteOffset,
                int ClauseStartByteOffset,
                int ExpressionByteOffset,
                ushort LineDataAddress,
                string[][] PreambleStatements,
                string[] ExpressionTokens);

            private static bool TryGetRandomizeUsrExpressionTokens(List<string> statement, out List<string> expressionTokens)
            {
                expressionTokens = new List<string>();
                if (statement.Count == 0 || NormalizeStatementKeyword(statement[0]) != "RANDOMIZE")
                    return false;

                int usrIndex = statement.IndexOf("USR");
                if (usrIndex < 0 || usrIndex + 1 >= statement.Count)
                    return false;

                expressionTokens = statement.GetRange(usrIndex + 1, statement.Count - usrIndex - 1);
                return expressionTokens.Count > 0;
            }

            private static bool TryGetRandomizeUsrExpressionInfo(
                List<TokenSpan> statementTokens,
                out List<string> expressionTokens,
                out int expressionByteOffset)
            {
                expressionTokens = new List<string>();
                expressionByteOffset = 0;
                if (statementTokens.Count == 0 || NormalizeStatementKeyword(statementTokens[0].Text) != "RANDOMIZE")
                    return false;

                int usrIndex = statementTokens.FindIndex(token => token.Text == "USR");
                if (usrIndex < 0 || usrIndex + 1 >= statementTokens.Count)
                    return false;

                expressionByteOffset = statementTokens[usrIndex + 1].ByteOffset;
                expressionTokens = statementTokens
                    .GetRange(usrIndex + 1, statementTokens.Count - usrIndex - 1)
                    .ConvertAll(token => token.Text);
                return expressionTokens.Count > 0;
            }

            private static IEnumerable<List<string>> EnumerateUsrContinuationClauses(List<string> statement)
            {
                if (statement.Count == 0)
                    yield break;

                int clauseStart = 0;
                for (int tokenIndex = 0; tokenIndex < statement.Count; tokenIndex++)
                {
                    if (!string.Equals(statement[tokenIndex], "THEN", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (tokenIndex > clauseStart)
                        yield return statement.GetRange(clauseStart, tokenIndex - clauseStart);

                    clauseStart = tokenIndex + 1;
                }

                if (clauseStart < statement.Count)
                    yield return statement.GetRange(clauseStart, statement.Count - clauseStart);
            }

            private static IEnumerable<List<TokenSpan>> EnumerateUsrContinuationClauses(
                List<TokenSpan> statementTokens)
            {
                if (statementTokens.Count == 0)
                    yield break;

                int clauseStartIndex = 0;
                for (int tokenIndex = 0; tokenIndex < statementTokens.Count; tokenIndex++)
                {
                    if (!string.Equals(statementTokens[tokenIndex].Text, "THEN", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (tokenIndex > clauseStartIndex)
                    {
                        yield return statementTokens.GetRange(clauseStartIndex, tokenIndex - clauseStartIndex);
                    }

                    clauseStartIndex = tokenIndex + 1;
                }

                if (clauseStartIndex < statementTokens.Count)
                {
                    yield return statementTokens.GetRange(clauseStartIndex, statementTokens.Count - clauseStartIndex);
                }
            }

            private static bool IsSafeUsrContinuationPreambleStatement(List<string> statement)
            {
                if (statement.Count == 0)
                    return true;

                string keyword = NormalizeStatementKeyword(statement[0]);
                if (keyword is "REM" or "?" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or "POKE")
                    return true;

                if (keyword == "LET")
                    return statement.Contains("=");

                if (keyword == "IF")
                    return statement.Contains("=");

                foreach (string token in statement)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                    {
                        continue;
                    }

                    string normalized = NormalizeStatementKeyword(token);
                    if (normalized == "PEEK")
                        continue;

                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    bool isAsciiWord = true;
                    for (int i = 0; i < token.Length; i++)
                    {
                        char c = token[i];
                        if (!IsAsciiLetterOrDigit(c))
                        {
                            isAsciiWord = false;
                            break;
                        }
                    }

                    if (!isAsciiWord)
                        return false;
                }

                return true;
            }

            private static bool IsSimpleAnonymousCodeLoad(List<string> statement)
            {
                if (statement.Count <= 1)
                    return true;

                for (int i = 1; i < statement.Count; i++)
                {
                    string token = statement[i];
                    if (string.IsNullOrEmpty(token) || token == "CODE")
                        continue;
                    return false;
                }

                return true;
            }

            private static string NormalizeStatementKeyword(string keyword)
            {
                if (string.Equals(keyword, "PRINT", StringComparison.OrdinalIgnoreCase))
                    return "?";

                if (!string.IsNullOrEmpty(keyword) && keyword[0] == '?')
                    return "?";

                return keyword;
            }

            private static IEnumerable<int> CollectDataValues(
                List<BasicLine> parsedLines,
                Spectrum128Machine machine,
                Dictionary<string, int> variables)
            {
                foreach (BasicLine line in parsedLines)
                {
                    foreach (List<string> statement in line.Statements)
                    {
                        if (statement.Count == 0 || statement[0] != "DATA")
                            continue;

                        int itemStart = 1;
                        while (itemStart < statement.Count)
                        {
                            int itemEnd = itemStart;
                            while (itemEnd < statement.Count && statement[itemEnd] != ",")
                                itemEnd++;

                            if (itemEnd > itemStart)
                            {
                                var parser = new ExpressionParser(machine, variables, statement, itemStart, itemEnd - 1);
                                yield return parser.Parse();
                            }

                            itemStart = itemEnd + 1;
                        }
                    }
                }
            }

            private static bool TryGetKeywordToken(byte value, out string? keyword)
            {
                keyword = value switch
                {
                    190 => "PEEK",
                    192 => "USR",
                    217 => "INK",
                    218 => "PAPER",
                    227 => "READ",
                    229 => "RESTORE",
                    231 => "BORDER",
                    234 => "REM",
                    239 => "LOAD",
                    244 => "POKE",
                    245 => "PRINT",
                    249 => "RANDOMIZE",
                    228 => "DATA",
                    235 => "FOR",
                    175 => "CODE",
                    242 => "PAUSE",
                    243 => "NEXT",
                    203 => "THEN",
                    241 => "LET",
                    250 => "IF",
                    251 => "CLS",
                    253 => "CLEAR",
                    254 => "RETURN",
                    204 => "TO",
                    _ => null
                };

                return keyword != null;
            }

            private readonly record struct TokenSpan(string Text, int ByteOffset);
            private readonly record struct BasicLine(
                ushort Number,
                List<List<string>> Statements,
                List<int> StatementByteOffsets,
                List<List<TokenSpan>> StatementTokenSpans,
                ushort DataAddress);
            private readonly record struct ForFrame(string VariableName, int EndValue, int LineIndex, int StatementIndex);
        }

        private sealed class ExpressionParser
        {
            private readonly Spectrum128Machine machine;
            private readonly Dictionary<string, int> variables;
            private readonly List<string> tokens;
            private readonly bool requireVariableResolution;
            private readonly int endIndex;
            private int index;

            public ExpressionParser(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> tokens,
                int startIndex,
                int endIndex,
                bool requireVariableResolution = false)
            {
                this.machine = machine;
                this.variables = variables;
                this.tokens = tokens;
                this.requireVariableResolution = requireVariableResolution;
                index = startIndex;
                this.endIndex = endIndex;
            }

            public int Parse()
            {
                int value = ParseAdditive();
                return value;
            }

            private int ParseAdditive()
            {
                int value = ParseMultiplicative();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "+")
                    {
                        index++;
                        value += ParseMultiplicative();
                    }
                    else if (token == "-")
                    {
                        index++;
                        value -= ParseMultiplicative();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseMultiplicative()
            {
                int value = ParseUnary();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "*")
                    {
                        index++;
                        value *= ParseUnary();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseUnary()
            {
                if (index <= endIndex && tokens[index] == "-")
                {
                    index++;
                    return -ParseUnary();
                }

                return ParsePrimary();
            }

            private int ParsePrimary()
            {
                if (index > endIndex)
                    throw new InvalidOperationException("Unexpected end of BASIC expression during tape bootstrap.");

                string token = tokens[index++];
                if (token == "(")
                {
                    int value = ParseAdditive();
                    if (index > endIndex || tokens[index] != ")")
                        throw new InvalidOperationException("Missing closing parenthesis in BASIC tape-bootstrap expression.");
                    index++;
                    return value;
                }

                if (token == "PEEK")
                {
                    int address = ParsePrimary();
                    return machine.PeekMemory((ushort)address);
                }

                if (int.TryParse(token, out int number))
                    return number;

                if (variables.TryGetValue(token, out int variableValue))
                    return variableValue;

                if (TryReadMountedContinuationNumericVariable(machine, token, out int liveVariableValue))
                    return liveVariableValue;

                // Spectrum BASIC treats an unread numeric variable as zero until it
                // is assigned. Mounted continuation evaluation should follow that
                // model instead of forcing a ROM BASIC resume just because a safe
                // direct IF/THEN or USR expression references an unset variable.
                if (requireVariableResolution)
                    return 0;

                return 0;
            }
        }

        internal sealed class TapHeaderInfo
        {
            public TapHeaderInfo(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
            {
                Type = type;
                FileName = string.IsNullOrWhiteSpace(fileName) ? "unnamed" : fileName;
                DataLength = dataLength;
                AutoStartLine = parameter1;
                ProgramLength = parameter2;
                StartAddress = parameter1;
            }

            public byte Type { get; }
            public string FileName { get; }
            public ushort DataLength { get; }
            public ushort AutoStartLine { get; }
            public ushort ProgramLength { get; }
            public ushort StartAddress { get; }
        }
    }
}
