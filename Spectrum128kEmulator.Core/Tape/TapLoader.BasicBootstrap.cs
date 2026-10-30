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

        private sealed partial class BasicBootstrapExecutor
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
                        {
                            int usrIndex = statement.IndexOf("USR");
                            if (usrIndex >= 0 && usrIndex + 1 < statement.Count)
                            {
                                int entryPoint = EvaluateExpression(statement, usrIndex + 1, statement.Count - 1);
                                EnterMachineCode((ushort)entryPoint);
                                return;
                            }

                            ApplyPrintSideEffects(line, statementIndex - 1, statement);
                            break;
                        }

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
                // The report address beneath STACK-BC is the normal BASIC caller;
                // without it, a returning USR routine eventually RETs to address zero.
                WriteWord(machine, machine.Cpu.Regs.SP, machine.RomProfile.MainExecutionReportAddress);
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

            private static List<BasicLine> ParseLinesFromData(byte[] programData, ushort programLength)
            {
                var parsedLines = new List<BasicLine>();
                int cursor = 0;
                int endOffset = Math.Min(programLength, programData.Length);

                while (cursor + 4 <= endOffset)
                {
                    ushort lineNumber = (ushort)((programData[cursor] << 8) | programData[cursor + 1]);
                    ushort lineLength = (ushort)(programData[cursor + 2] | (programData[cursor + 3] << 8));
                    int lineDataOffset = cursor + 4;
                    int nextLineOffset = lineDataOffset + lineLength;
                    if (nextLineOffset > endOffset)
                        break;

                    byte[] lineBytes = new byte[lineLength];
                    Buffer.BlockCopy(programData, lineDataOffset, lineBytes, 0, lineLength);
                    (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) = SplitStatements(Tokenize(lineBytes));
                    parsedLines.Add(new BasicLine(
                        lineNumber,
                        statements,
                        statementByteOffsets,
                        statementTokenSpans,
                        (ushort)(BasicProgramStart + lineDataOffset)));
                    cursor = nextLineOffset;
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

                    if ("()+-*/=,:".IndexOf(c) >= 0)
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

                        // PAUSE may wait for a key (PAUSE 0) or preserve visible/timed
                        // loader behavior. Keep such programs under the real ROM.
                        if (keyword == "PAUSE")
                            return true;

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

                if (!CanEvaluateProgramExpressions(machine, parsedLines, autoStartLine))
                    return false;

                if (CanExecuteProgram(parsedLines, autoStartLine) || CanExecuteProtectedProgram(parsedLines, autoStartLine))
                    return true;

                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                return startLineIndex >= 0 && CanExecuteImmediateSideEffectProgram(parsedLines, startLineIndex);
            }

            private static bool CanEvaluateProgramExpressions(
                Spectrum128Machine machine,
                List<BasicLine> parsedLines,
                ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var variables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                    {
                        foreach (List<string> statement in parsedLines[lineIndex].Statements)
                        {
                            if (!CanEvaluateStatementExpressions(machine, variables, statement))
                                return false;
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or DivideByZeroException or OverflowException)
                {
                    return false;
                }

                return true;
            }

            private static bool CanEvaluateStatementExpressions(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> statement)
            {
                if (statement.Count == 0)
                    return true;

                string keyword = NormalizeStatementKeyword(statement[0]);
                switch (keyword)
                {
                    case "BORDER":
                    case "PAPER":
                    case "INK":
                    case "CLEAR":
                    case "RESTORE":
                        return statement.Count == 1 || CanParseExpression(machine, variables, statement, 1, statement.Count - 1);

                    case "POKE":
                    {
                        int commaIndex = statement.IndexOf(",");
                        return commaIndex > 1 &&
                               commaIndex < statement.Count - 1 &&
                               CanParseExpression(machine, variables, statement, 1, commaIndex - 1) &&
                               CanParseExpression(machine, variables, statement, commaIndex + 1, statement.Count - 1);
                    }

                    case "DATA":
                    {
                        int itemStart = 1;
                        while (itemStart < statement.Count)
                        {
                            int itemEnd = statement.IndexOf(",", itemStart);
                            if (itemEnd < 0)
                                itemEnd = statement.Count;
                            if (itemEnd == itemStart ||
                                !CanParseExpression(machine, variables, statement, itemStart, itemEnd - 1))
                            {
                                return false;
                            }

                            itemStart = itemEnd + 1;
                        }

                        return true;
                    }

                    case "FOR":
                    {
                        int equalsIndex = statement.IndexOf("=");
                        int toIndex = statement.IndexOf("TO");
                        return equalsIndex > 1 &&
                               toIndex > equalsIndex + 1 &&
                               toIndex < statement.Count - 1 &&
                               CanParseExpression(machine, variables, statement, equalsIndex + 1, toIndex - 1) &&
                               CanParseExpression(machine, variables, statement, toIndex + 1, statement.Count - 1);
                    }

                    case "RANDOMIZE":
                    {
                        int usrIndex = statement.IndexOf("USR");
                        int expressionStart = usrIndex >= 0 ? usrIndex + 1 : 1;
                        return expressionStart >= statement.Count ||
                               CanParseExpression(machine, variables, statement, expressionStart, statement.Count - 1);
                    }

                    default:
                        return true;
                }
            }

            private static bool CanParseExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> tokens,
                int startIndex,
                int endIndex)
            {
                if (startIndex > endIndex)
                    return false;

                var parser = new ExpressionParser(machine, variables, tokens, startIndex, endIndex);
                parser.Parse();
                return true;
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

            public static bool RequiresRomDrivenMountedLoadedProgramData(
                byte[] programData,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLinesFromData(programData, programLength);
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

                if (keyword == "LOAD")
                    return statement.Contains("CODE");

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
                    186 => "INT",
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
