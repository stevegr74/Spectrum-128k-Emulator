using System;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    internal sealed class MountedLoadContinuationController
    {
        private const ushort NewPpcAddress = 23618;
        private const ushort NspPcAddress = 23620;
        private const ushort PpcAddress = 23621;
        private const ushort SubPpcAddress = 23623;
        private const ushort OldPpcAddress = 23662;
        private const ushort OspPcAddress = 23664;
        private const ushort VarsAddress = 23627;
        private const ushort ProgAddress = 23635;
        private const ushort NextLineAddress = 23637;
        private const ushort DataAddress = 23639;
        private const ushort CurChlAddress = 23633;
        private const ushort EditLineAddress = 23641;
        private const ushort KCurAddress = 23643;
        private const ushort ChAddAddress = 23645;
        private const ushort XPtrAddress = 23647;
        private const ushort WorkspaceAddress = 23649;
        private const ushort StackBottomAddress = 23651;
        private const ushort StackEndAddress = 23653;
        private const ushort KeyboardChannelDescriptorAddress = 23739;
        private const ulong StreamingInterpreterRefreshIntervalTStates = 4096;

        private readonly Spectrum128Machine machine;
        private Func<Spectrum128Machine, ushort?>? resolver;
        private bool requiresUsrReturnAddress;
        private ushort? basicResumeLine;
        private byte basicResumeStatement;
        private InterpreterContext? interpreterContext;
        private BasicVariableArea? basicVariableArea;
        private ResumeCursorOverride? resumeCursorOverride;
        private bool preserveLiveInterpreterStateForDirectUsrEntry;
        private ulong nextStreamingInterpreterRefreshTStates;

        internal MountedLoadContinuationController(Spectrum128Machine machine)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        }

        internal bool HasPending =>
            resolver != null || basicResumeLine.HasValue;

        internal Func<Spectrum128Machine, ushort?>? Resolver
        {
            get => resolver;
            set => resolver = value;
        }

        internal bool RequiresUsrReturnAddress => requiresUsrReturnAddress;

        internal void SetEntryPoint(ushort entryPoint)
        {
            if (!CanArm())
                return;

            CaptureInterpreterContextIfNeeded();
            resolver = _ => entryPoint;
            requiresUsrReturnAddress = false;
            preserveLiveInterpreterStateForDirectUsrEntry = false;
            nextStreamingInterpreterRefreshTStates = 0;
            Trace($"Arm direct continuation entry=0x{entryPoint:X4} requireUsrReturn=0");
        }

        internal void SetResolver(
            Func<Spectrum128Machine, ushort?> resolver,
            bool requireUsrReturnAddress)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            if (!CanArm())
                return;

            CaptureInterpreterContextIfNeeded();
            this.resolver = resolver;
            requiresUsrReturnAddress = requireUsrReturnAddress;
            preserveLiveInterpreterStateForDirectUsrEntry = false;
            nextStreamingInterpreterRefreshTStates = 0;
            Trace($"Arm resolver requireUsrReturn={(requireUsrReturnAddress ? 1 : 0)}");
        }

        internal void SetDirectUsrContextPolicy(bool preserveLiveInterpreterState)
        {
            if (!CanArm())
                return;

            preserveLiveInterpreterStateForDirectUsrEntry = preserveLiveInterpreterState;
            Trace($"DirectUsrContext preserveLiveInterpreter={(preserveLiveInterpreterState ? 1 : 0)}");
        }

        internal void Clear()
        {
            bool hadPendingContinuation = HasPending;
            resolver = null;
            requiresUsrReturnAddress = false;
            basicResumeLine = null;
            basicResumeStatement = 0;
            interpreterContext = null;
            basicVariableArea = null;
            resumeCursorOverride = null;
            preserveLiveInterpreterStateForDirectUsrEntry = false;
            nextStreamingInterpreterRefreshTStates = 0;
            if (hadPendingContinuation)
                Trace("Clear pending continuation");
        }

        internal void RefreshInterpreterContext(bool forceVariableAreaRefresh = false)
        {
            if (forceVariableAreaRefresh || !basicVariableArea.HasValue)
            {
                ushort vars = ReadWord(VarsAddress);
                ushort eLine = ReadWord(EditLineAddress);
                byte[] variableData = Array.Empty<byte>();
                if (vars < eLine)
                {
                    int length = eLine - vars;
                    variableData = new byte[length];
                    for (int i = 0; i < length; i++)
                        variableData[i] = machine.PeekMemory((ushort)(vars + i));
                }

                var refreshedVariableArea = new BasicVariableArea(vars, eLine, variableData);
                if (!basicVariableArea.HasValue ||
                    ShouldReplaceBasicVariableArea(basicVariableArea.Value, refreshedVariableArea))
                {
                    basicVariableArea = refreshedVariableArea;
                }
            }

            if (interpreterContext.HasValue)
            {
                InterpreterContext original = interpreterContext.Value;
                interpreterContext = new InterpreterContext(
                    original.Vars,
                    original.Prog,
                    original.NextLine,
                    original.Data,
                    ReadWord(CurChlAddress),
                    ReadWord(EditLineAddress),
                    ReadWord(KCurAddress),
                    ReadWord(ChAddAddress),
                    ReadWord(XPtrAddress),
                    ReadWord(WorkspaceAddress),
                    ReadWord(StackBottomAddress),
                    ReadWord(StackEndAddress));
                return;
            }

            interpreterContext = new InterpreterContext(
                ReadWord(VarsAddress),
                ReadWord(ProgAddress),
                ReadWord(NextLineAddress),
                ReadWord(DataAddress),
                ReadWord(CurChlAddress),
                ReadWord(EditLineAddress),
                ReadWord(KCurAddress),
                ReadWord(ChAddAddress),
                ReadWord(XPtrAddress),
                ReadWord(WorkspaceAddress),
                ReadWord(StackBottomAddress),
                ReadWord(StackEndAddress));
        }

        internal void SetBasicResume(ushort lineNumber, byte statementIndex)
        {
            if (!CanArm())
                return;

            basicResumeLine = lineNumber;
            basicResumeStatement = statementIndex;
            Trace($"Arm BASIC resume line={lineNumber} stmt={statementIndex}");
        }

        internal void SetResumeCursorOverride(ushort? kCur, ushort? chAdd, ushort? xPtr)
        {
            if (!CanArm())
                return;

            resumeCursorOverride = new ResumeCursorOverride(kCur, chAdd, xPtr);
            Trace(
                $"Cursor override KCUR={(kCur.HasValue ? $"0x{kCur.Value:X4}" : "live")} " +
                $"CHADD={(chAdd.HasValue ? $"0x{chAdd.Value:X4}" : "live")} " +
                $"XPTR={(xPtr.HasValue ? $"0x{xPtr.Value:X4}" : "live")}");
        }

        internal bool TryGetBasicVariableArea(out ushort vars, out ushort eLine)
        {
            vars = 0;
            eLine = 0;
            if (!basicVariableArea.HasValue)
                return false;

            BasicVariableArea context = basicVariableArea.Value;
            if (context.Vars >= context.EditLine)
                return false;

            vars = context.Vars;
            eLine = context.EditLine;
            return true;
        }

        internal bool TryGetBasicVariableSnapshot(out ushort vars, out byte[] data)
        {
            vars = 0;
            data = Array.Empty<byte>();
            if (!basicVariableArea.HasValue)
                return false;

            BasicVariableArea context = basicVariableArea.Value;
            if (context.Vars >= context.EditLine || context.Data.Length == 0)
                return false;

            vars = context.Vars;
            data = context.Data;
            return true;
        }

        internal bool TryResume(Z80Cpu cpu)
        {
            MountedTape? mountedTape = machine.MountedTape;
            if (!machine.SupportsRomTapeAcceleration ||
                resolver == null ||
                mountedTape == null)
            {
                return false;
            }

            SpectrumRomProfile profile = machine.RomProfile;
            if (cpu.Regs.PC == profile.UsrReturnAddress)
                RefreshInterpreterContext(forceVariableAreaRefresh: true);

            if (requiresUsrReturnAddress && cpu.Regs.PC != profile.UsrReturnAddress)
                return false;

            if (!requiresUsrReturnAddress &&
                mountedTape.IsActivelyStreamingEarSignal &&
                !HasPreservedBasicVariableSnapshot() &&
                cpu.TStates >= nextStreamingInterpreterRefreshTStates)
            {
                RefreshInterpreterContext(forceVariableAreaRefresh: true);
                nextStreamingInterpreterRefreshTStates =
                    cpu.TStates + StreamingInterpreterRefreshIntervalTStates;
            }

            if (mountedTape.IsActivelyStreamingEarSignal ||
                !CanResume(cpu.Regs.PC, mountedTape, profile))
            {
                return false;
            }

            Trace(
                $"Resume gate open basicResume={(basicResumeLine.HasValue ? 1 : 0)} " +
                $"requireUsrReturn={(requiresUsrReturnAddress ? 1 : 0)}");

            Func<Spectrum128Machine, ushort?> continuationResolver = resolver;
            resolver = null;
            requiresUsrReturnAddress = false;
            ushort? resolvedEntryPoint = continuationResolver(machine);
            bool preserveLiveInterpreterState = preserveLiveInterpreterStateForDirectUsrEntry;
            preserveLiveInterpreterStateForDirectUsrEntry = false;
            if (!resolvedEntryPoint.HasValue)
            {
                Trace("Resolver dropped continuation");
                return false;
            }

            if (basicResumeLine.HasValue)
            {
                ushort lineNumber = basicResumeLine.Value;
                byte statementIndex = basicResumeStatement;
                basicResumeLine = null;
                basicResumeStatement = 0;
                RestoreInterpreterWorkspaceForBasicResume();
                ApplyResumeCursorOverride();
                WriteWord(NewPpcAddress, lineNumber);
                machine.PokeMemory(NspPcAddress, statementIndex);
                WriteWord(PpcAddress, lineNumber);
                machine.PokeMemory(SubPpcAddress, statementIndex);
                cpu.Regs.PC = profile.BasicResumeExecutionLoopAddress;
                Trace($"Resume BASIC line={lineNumber} stmt={statementIndex} -> pc=0x{cpu.Regs.PC:X4}");
                return true;
            }

            ushort entryPoint = resolvedEntryPoint.Value;
            if (entryPoint == 0)
            {
                EnterUsr0Mode(cpu, profile);
                Trace($"Resume USR0 pc=0x{cpu.Regs.PC:X4}");
                return true;
            }

            if (!preserveLiveInterpreterState)
            {
                RestoreInterpreterWorkspaceForDirectUsrEntry();
                ApplyResumeCursorOverride();
            }
            else
            {
                resumeCursorOverride = null;
            }

            cpu.Regs.SP -= 2;
            WriteWord(cpu.Regs.SP, profile.UsrReturnAddress);
            cpu.Regs.BC = entryPoint;
            cpu.Regs.H_ = (byte)(profile.EndCalcLiteralAddress >> 8);
            cpu.Regs.L_ = (byte)profile.EndCalcLiteralAddress;
            cpu.Regs.PC = entryPoint;
            Trace($"Resume direct entry=0x{entryPoint:X4} sp=0x{cpu.Regs.SP:X4}");
            return true;
        }

        internal QuickState CaptureQuickState()
        {
            return new QuickState
            {
                Resolver = resolver,
                RequiresUsrReturnAddress = requiresUsrReturnAddress,
                BasicResumeLine = basicResumeLine,
                BasicResumeStatement = basicResumeStatement,
                InterpreterContext = interpreterContext,
                BasicVariableArea = basicVariableArea.HasValue
                    ? new BasicVariableArea(
                        basicVariableArea.Value.Vars,
                        basicVariableArea.Value.EditLine,
                        (byte[])basicVariableArea.Value.Data.Clone())
                    : null,
                ResumeCursorOverride = resumeCursorOverride,
                PreserveLiveInterpreterState = preserveLiveInterpreterStateForDirectUsrEntry,
                NextStreamingInterpreterRefreshTStates = nextStreamingInterpreterRefreshTStates
            };
        }

        internal void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            resolver = state.Resolver;
            requiresUsrReturnAddress = state.RequiresUsrReturnAddress;
            basicResumeLine = state.BasicResumeLine;
            basicResumeStatement = state.BasicResumeStatement;
            interpreterContext = state.InterpreterContext;
            basicVariableArea = state.BasicVariableArea.HasValue
                ? new BasicVariableArea(
                    state.BasicVariableArea.Value.Vars,
                    state.BasicVariableArea.Value.EditLine,
                    (byte[])state.BasicVariableArea.Value.Data.Clone())
                : null;
            resumeCursorOverride = state.ResumeCursorOverride;
            preserveLiveInterpreterStateForDirectUsrEntry = state.PreserveLiveInterpreterState;
            nextStreamingInterpreterRefreshTStates = state.NextStreamingInterpreterRefreshTStates;
        }

        private bool CanArm()
        {
            if (machine.SupportsRomTapeAcceleration)
                return true;

            machine.Trace?.Invoke(
                $"[MountedLoad] Address-based continuation disabled for ROM profile '{machine.RomProfile.Name}'.");
            return false;
        }

        private bool HasPreservedBasicVariableSnapshot() =>
            basicVariableArea.HasValue && basicVariableArea.Value.Data.Length > 0;

        private static bool ShouldReplaceBasicVariableArea(BasicVariableArea existing, BasicVariableArea candidate)
        {
            bool existingUsable = IsUsableBasicVariableArea(existing);
            bool candidateUsable = IsUsableBasicVariableArea(candidate);
            if (!existingUsable)
                return true;
            if (!candidateUsable)
                return false;
            return candidate.Data.Length >= existing.Data.Length;
        }

        private static bool IsUsableBasicVariableArea(BasicVariableArea variableArea) =>
            variableArea.Vars >= 0x5B00 &&
            variableArea.Vars < variableArea.EditLine &&
            variableArea.Data.Length > 1 &&
            variableArea.Data[0] != 0x80;

        private static bool CanResume(ushort pc, MountedTape mountedTape, SpectrumRomProfile profile)
        {
            if (pc == profile.UsrReturnAddress)
                return true;
            return profile.IsMountedLoadReturnAddress(pc) &&
                   mountedTape.IsAtMountedLoadUsrContinuationBoundary;
        }

        private void RestoreInterpreterWorkspaceForBasicResume()
        {
            if (interpreterContext.HasValue)
            {
                RestoreInterpreterContext(interpreterContext.Value);
                machine.PokeMemory(interpreterContext.Value.EditLine, 0x0D);
                machine.PokeMemory((ushort)(interpreterContext.Value.EditLine + 1), 0x00);
                return;
            }

            ushort eLine = ReadWord(EditLineAddress);
            machine.PokeMemory(eLine, 0x0D);
            machine.PokeMemory((ushort)(eLine + 1), 0x00);
            WriteWord(WorkspaceAddress, eLine);
            WriteWord(StackBottomAddress, eLine);
            WriteWord(StackEndAddress, eLine);
            WriteWord(KCurAddress, eLine);
            WriteWord(ChAddAddress, eLine);
            WriteWord(XPtrAddress, eLine);
        }

        private void CaptureInterpreterContextIfNeeded()
        {
            if (!interpreterContext.HasValue)
                RefreshInterpreterContext();
        }

        private void RestoreInterpreterWorkspaceForDirectUsrEntry()
        {
            if (interpreterContext.HasValue)
                RestoreInterpreterContext(interpreterContext.Value);
        }

        private void RestoreInterpreterContext(InterpreterContext context)
        {
            WriteWord(VarsAddress, context.Vars);
            WriteWord(ProgAddress, context.Prog);
            WriteWord(NextLineAddress, context.NextLine);
            WriteWord(DataAddress, context.Data);
            WriteWord(CurChlAddress, context.CurChl);
            WriteWord(EditLineAddress, context.EditLine);
            WriteWord(KCurAddress, context.KCur);
            WriteWord(ChAddAddress, context.ChAdd);
            WriteWord(XPtrAddress, context.XPtr);
            WriteWord(WorkspaceAddress, context.Workspace);
            WriteWord(StackBottomAddress, context.StackBottom);
            WriteWord(StackEndAddress, context.StackEnd);
        }

        private void ApplyResumeCursorOverride()
        {
            if (!resumeCursorOverride.HasValue)
                return;

            ResumeCursorOverride cursor = resumeCursorOverride.Value;
            resumeCursorOverride = null;
            if (cursor.KCur.HasValue)
                WriteWord(KCurAddress, cursor.KCur.Value);
            if (cursor.ChAdd.HasValue)
                WriteWord(ChAddAddress, cursor.ChAdd.Value);
            if (cursor.XPtr.HasValue)
                WriteWord(XPtrAddress, cursor.XPtr.Value);
        }

        private void EnterUsr0Mode(Z80Cpu cpu, SpectrumRomProfile profile)
        {
            basicResumeLine = null;
            basicResumeStatement = 0;
            interpreterContext = null;
            basicVariableArea = null;
            resumeCursorOverride = null;
            preserveLiveInterpreterStateForDirectUsrEntry = false;
            nextStreamingInterpreterRefreshTStates = 0;
            machine.PrepareMountedLoadUsr0HardwareMode(cpu);

            WriteWord(NewPpcAddress, 0);
            machine.PokeMemory(NspPcAddress, 0);
            WriteWord(PpcAddress, 0);
            machine.PokeMemory(SubPpcAddress, 0);
            WriteWord(OldPpcAddress, 0);
            machine.PokeMemory(OspPcAddress, 0);
            InitializeUsr0InterpreterWorkspace();

            cpu.Regs.BC = 0;
            cpu.Regs.H_ = (byte)(profile.EndCalcLiteralAddress >> 8);
            cpu.Regs.L_ = (byte)profile.EndCalcLiteralAddress;
            cpu.Regs.PC = 0;
        }

        private void InitializeUsr0InterpreterWorkspace()
        {
            ushort eLine = ReadWord(EditLineAddress);
            if (eLine < 0x5B00)
                eLine = (ushort)(ReadWord(VarsAddress) + 1);

            machine.PokeMemory(eLine, 0x0D);
            machine.PokeMemory((ushort)(eLine + 1), 0x00);
            WriteWord(CurChlAddress, KeyboardChannelDescriptorAddress);
            WriteWord(KCurAddress, eLine);
            WriteWord(ChAddAddress, eLine);
            WriteWord(XPtrAddress, 0);
            ushort workspace = (ushort)(eLine + 1);
            WriteWord(WorkspaceAddress, workspace);
            WriteWord(StackBottomAddress, workspace);
            WriteWord(StackEndAddress, workspace);
        }

        private ushort ReadWord(ushort address) =>
            (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));

        private void WriteWord(ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)value);
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private void Trace(string message)
        {
            machine.Trace?.Invoke(
                $"[MountedLoad] {message} frame={machine.FrameCount} pc=0x{machine.Cpu.Regs.PC:X4}");
        }

        internal sealed class QuickState
        {
            internal Func<Spectrum128Machine, ushort?>? Resolver;
            internal bool RequiresUsrReturnAddress;
            internal ushort? BasicResumeLine;
            internal byte BasicResumeStatement;
            internal InterpreterContext? InterpreterContext;
            internal BasicVariableArea? BasicVariableArea;
            internal ResumeCursorOverride? ResumeCursorOverride;
            internal bool PreserveLiveInterpreterState;
            internal ulong NextStreamingInterpreterRefreshTStates;
        }

        internal readonly record struct InterpreterContext(
            ushort Vars,
            ushort Prog,
            ushort NextLine,
            ushort Data,
            ushort CurChl,
            ushort EditLine,
            ushort KCur,
            ushort ChAdd,
            ushort XPtr,
            ushort Workspace,
            ushort StackBottom,
            ushort StackEnd);

        internal readonly record struct BasicVariableArea(ushort Vars, ushort EditLine, byte[] Data);
        internal readonly record struct ResumeCursorOverride(ushort? KCur, ushort? ChAdd, ushort? XPtr);
    }
}
