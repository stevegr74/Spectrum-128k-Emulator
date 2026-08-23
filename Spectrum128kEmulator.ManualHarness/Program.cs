using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator;
using System.Reflection;

string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
var machine = new Spectrum128Machine(romFolder);

if (args.Length > 0)
{
    string snapshotPath = args[0];
    int? initialInterruptDelay = null;
    int frameLimit = 300;
    int? frameTStatesOverride = null;
    uint? initialTStatesOverride = null;
    int floatingBusDisplayStartAdjust = 0;
    int floatingBusSampleAdjust = 0;
    bool enableDebugCapture = false;
    bool dumpTapeBlocks = false;
    bool dumpBatmanBasic = false;
    bool enableMachineTrace = false;
    TapeLoadStrategy? forcedTapeStrategy = null;
    (ushort Start, ushort End)? focusedTraceRange = null;
    int focusedTraceStartFrame = 0;
    int focusedTraceFrameLimit = 0;
    int focusedTraceMaxEntries = 2048;
    string executionMode = "frame";
    string? batmanForceContinuationMode = null;
    List<ScheduledKeyEvent> scheduledKeyEvents = new();
    List<ScheduledPcEvent> scheduledPcEvents = new();
    List<ScheduledRegisterEvent> scheduledRegisterEvents = new();
    List<ScheduledMemoryWriteEvent> scheduledMemoryWriteEvents = new();
    List<ScheduledFrameTimingEvent> scheduledFrameTimingEvents = new();
    int optionStartIndex = 1;
    if (args.Length > 1 && !args[1].Contains('='))
    {
        initialInterruptDelay = int.Parse(args[1]);
        optionStartIndex = 2;
    }

    if (args.Length > optionStartIndex && !args[optionStartIndex].Contains('='))
    {
        frameLimit = int.Parse(args[optionStartIndex]);
        optionStartIndex++;
    }

    for (int argIndex = optionStartIndex; argIndex < args.Length; argIndex++)
    {
        string arg = args[argIndex];
        if (arg.StartsWith("interrupt=", StringComparison.OrdinalIgnoreCase))
        {
            initialInterruptDelay = int.Parse(arg["interrupt=".Length..]);
        }
        else if (arg.StartsWith("frames=", StringComparison.OrdinalIgnoreCase))
        {
            frameLimit = int.Parse(arg["frames=".Length..]);
        }
        else
        if (arg.StartsWith("fbstart=", StringComparison.OrdinalIgnoreCase))
        {
            floatingBusDisplayStartAdjust = int.Parse(arg["fbstart=".Length..]);
        }
        else if (arg.StartsWith("frametstates=", StringComparison.OrdinalIgnoreCase))
        {
            frameTStatesOverride = int.Parse(arg["frametstates=".Length..]);
        }
        else if (arg.StartsWith("tstates=", StringComparison.OrdinalIgnoreCase))
        {
            initialTStatesOverride = uint.Parse(arg["tstates=".Length..]);
        }
        else if (arg.StartsWith("fbsample=", StringComparison.OrdinalIgnoreCase))
        {
            floatingBusSampleAdjust = int.Parse(arg["fbsample=".Length..]);
        }
        else if (arg.Equals("debugcapture=1", StringComparison.OrdinalIgnoreCase))
        {
            enableDebugCapture = true;
        }
        else if (arg.Equals("dumpblocks=1", StringComparison.OrdinalIgnoreCase))
        {
            dumpTapeBlocks = true;
        }
        else if (arg.Equals("dumpbatmanbasic=1", StringComparison.OrdinalIgnoreCase))
        {
            dumpBatmanBasic = true;
        }
        else if (arg.Equals("machinetrace=1", StringComparison.OrdinalIgnoreCase))
        {
            enableMachineTrace = true;
        }
        else if (arg.StartsWith("strategy=", StringComparison.OrdinalIgnoreCase))
        {
            forcedTapeStrategy = ParseTapeStrategy(arg["strategy=".Length..]);
        }
        else if (arg.StartsWith("pc=", StringComparison.OrdinalIgnoreCase))
        {
            scheduledPcEvents.Add(ParsePcEvent(arg["pc=".Length..]));
        }
        else if (arg.StartsWith("reg=", StringComparison.OrdinalIgnoreCase))
        {
            scheduledRegisterEvents.Add(ParseRegisterEvent(arg["reg=".Length..]));
        }
        else if (arg.StartsWith("poke=", StringComparison.OrdinalIgnoreCase))
        {
            scheduledMemoryWriteEvents.Add(ParseMemoryWriteEvent(arg["poke=".Length..]));
        }
        else if (arg.StartsWith("tracepc=", StringComparison.OrdinalIgnoreCase))
        {
            focusedTraceRange = ParseTraceRange(arg["tracepc=".Length..]);
        }
        else if (arg.StartsWith("framet=", StringComparison.OrdinalIgnoreCase))
        {
            scheduledFrameTimingEvents.Add(ParseFrameTimingEvent(arg["framet=".Length..]));
        }
        else if (arg.StartsWith("traceframes=", StringComparison.OrdinalIgnoreCase))
        {
            focusedTraceFrameLimit = int.Parse(arg["traceframes=".Length..]);
        }
        else if (arg.StartsWith("tracefromframe=", StringComparison.OrdinalIgnoreCase))
        {
            focusedTraceStartFrame = int.Parse(arg["tracefromframe=".Length..]);
        }
        else if (arg.StartsWith("tracemax=", StringComparison.OrdinalIgnoreCase))
        {
            focusedTraceMaxEntries = int.Parse(arg["tracemax=".Length..]);
        }
        else if (arg.StartsWith("exec=", StringComparison.OrdinalIgnoreCase))
        {
            executionMode = arg["exec=".Length..].Trim().ToLowerInvariant();
        }
        else if (arg.StartsWith("batforce=", StringComparison.OrdinalIgnoreCase))
        {
            batmanForceContinuationMode = arg["batforce=".Length..].Trim().ToLowerInvariant();
        }
        else
        {
            scheduledKeyEvents = ParseKeyScript(arg);
        }
    }

    Console.WriteLine($"Loading image: {snapshotPath}");

    if (enableMachineTrace)
    {
        machine.Trace = message => Console.WriteLine("TRACE " + message);
        Console.WriteLine("Machine trace enabled.");
    }

    if (dumpTapeBlocks)
    {
        DumpTapeBlocks(snapshotPath, machine.FrameTStates == Spectrum128Machine.FrameTStates48);
        return;
    }

    if (dumpBatmanBasic)
    {
        DumpBatmanBasic(snapshotPath);
        return;
    }

    string extension = Path.GetExtension(snapshotPath);
    if (extension.Equals(".sna", StringComparison.OrdinalIgnoreCase))
    {
        SnapshotLoader.LoadSna48k(machine, snapshotPath);
    }
    else if (extension.Equals(".z80", StringComparison.OrdinalIgnoreCase))
    {
        Z80SnapshotLoader.Load(machine, snapshotPath);
    }
    else if (extension.Equals(".tap", StringComparison.OrdinalIgnoreCase))
    {
        TapeExecutionResult result = forcedTapeStrategy.HasValue
            ? LoadTapeWithForcedStrategy(machine, snapshotPath, forcedTapeStrategy.Value, isTzx: false)
            : TapLoader.LoadWithPolicy(machine, snapshotPath);
        Console.WriteLine(DescribeTapeExecution("TAP", result));
    }
    else if (extension.Equals(".tzx", StringComparison.OrdinalIgnoreCase))
    {
        TapeExecutionResult result = forcedTapeStrategy.HasValue
            ? LoadTapeWithForcedStrategy(machine, snapshotPath, forcedTapeStrategy.Value, isTzx: true)
            : TzxLoader.LoadWithPolicy(machine, snapshotPath);
        Console.WriteLine(DescribeTapeExecution("TZX", result));
    }
    else if (extension.Equals(".rzx", StringComparison.OrdinalIgnoreCase))
    {
        RzxLoader.Load(machine, snapshotPath);
        Console.WriteLine($"RZX playback loaded: {Path.GetFileName(snapshotPath)}");
    }
    else
    {
        throw new InvalidOperationException($"Unsupported image extension: {extension}");
    }

    if (initialInterruptDelay.HasValue)
    {
        machine.SetInitialInterruptDelay(initialInterruptDelay.Value);
        Console.WriteLine($"Initial interrupt delay: {initialInterruptDelay.Value} T-states");
    }

    if (frameTStatesOverride.HasValue)
    {
        machine.SetFrameTimingForDebug(frameTStatesOverride.Value);
        Console.WriteLine($"Frame T-states override: {frameTStatesOverride.Value}");
    }

    if (initialTStatesOverride.HasValue)
    {
        machine.Cpu.AdvanceTStates(initialTStatesOverride.Value);
        Console.WriteLine($"Initial T-states override: {initialTStatesOverride.Value}");
    }

    if (floatingBusDisplayStartAdjust != 0 || floatingBusSampleAdjust != 0)
    {
        machine.Set48kFloatingBusTimingAdjustments(floatingBusDisplayStartAdjust, floatingBusSampleAdjust);
        Console.WriteLine(
            $"Floating bus timing adjust: displayStart={floatingBusDisplayStartAdjust} sample={floatingBusSampleAdjust}");
    }

    if (enableDebugCapture)
    {
        machine.SetDebugEventCaptureEnabled(true);
        Console.WriteLine("Debug event capture enabled.");
    }

    if (focusedTraceRange.HasValue)
    {
        int effectiveFocusedTraceFrameLimit = focusedTraceFrameLimit > 0 ? focusedTraceFrameLimit : frameLimit;
        machine.EnableFocusedInstructionTrace(
            focusedTraceRange.Value.Start,
            focusedTraceRange.Value.End,
            focusedTraceStartFrame,
            effectiveFocusedTraceFrameLimit,
            focusedTraceMaxEntries);
        Console.WriteLine(
            $"Focused trace: pc=0x{focusedTraceRange.Value.Start:X4}-0x{focusedTraceRange.Value.End:X4} " +
            $"startFrame={focusedTraceStartFrame} frames={effectiveFocusedTraceFrameLimit} maxEntries={focusedTraceMaxEntries}");
    }

    if (scheduledKeyEvents.Count > 0)
    {
        Console.WriteLine("Scheduled key events:");
        foreach (ScheduledKeyEvent keyEvent in scheduledKeyEvents)
            Console.WriteLine($"  frame={keyEvent.Frame} key={keyEvent.KeyName} pressed={keyEvent.Pressed}");
    }

    if (scheduledPcEvents.Count > 0)
    {
        Console.WriteLine("Scheduled PC events:");
        foreach (ScheduledPcEvent pcEvent in scheduledPcEvents)
            Console.WriteLine($"  frame={pcEvent.Frame} pc=0x{pcEvent.ProgramCounter:X4}");
    }

    if (scheduledRegisterEvents.Count > 0)
    {
        Console.WriteLine("Scheduled register events:");
        foreach (ScheduledRegisterEvent registerEvent in scheduledRegisterEvents)
            Console.WriteLine($"  frame={registerEvent.Frame} {registerEvent.RegisterName}=0x{registerEvent.Value:X4}");
    }

    if (scheduledMemoryWriteEvents.Count > 0)
    {
        Console.WriteLine("Scheduled memory writes:");
        foreach (ScheduledMemoryWriteEvent memoryWriteEvent in scheduledMemoryWriteEvents)
            Console.WriteLine($"  frame={memoryWriteEvent.Frame} [{memoryWriteEvent.Address:X4}]=0x{memoryWriteEvent.Value:X2}");
    }

    if (scheduledFrameTimingEvents.Count > 0)
    {
        Console.WriteLine("Scheduled frame timing events:");
        foreach (ScheduledFrameTimingEvent frameTimingEvent in scheduledFrameTimingEvents)
            Console.WriteLine($"  frame={frameTimingEvent.Frame} frametstates={frameTimingEvent.FrameTStates}");
    }

    Console.WriteLine($"Execution mode: {executionMode}");

    int iteration = 0;
    int lastReportedFrame = -1;
    bool? lastPendingMountedLoadContinuation = null;
    while (machine.FrameCount < frameLimit)
    {
        foreach (ScheduledKeyEvent keyEvent in scheduledKeyEvents)
        {
            if (keyEvent.Frame == iteration)
            {
                ApplyKey(machine, keyEvent.KeyName, keyEvent.Pressed);
                Console.WriteLine($"KEY frame={iteration} key={keyEvent.KeyName} pressed={keyEvent.Pressed}");
            }
        }

        foreach (ScheduledPcEvent pcEvent in scheduledPcEvents)
        {
            if (pcEvent.Frame == iteration)
            {
                machine.Cpu.Regs.PC = pcEvent.ProgramCounter;
                Console.WriteLine($"PC frame={iteration} pc=0x{pcEvent.ProgramCounter:X4}");
            }
        }

        foreach (ScheduledRegisterEvent registerEvent in scheduledRegisterEvents)
        {
            if (registerEvent.Frame == iteration)
            {
                ApplyRegister(machine, registerEvent.RegisterName, registerEvent.Value);
                Console.WriteLine($"REG frame={iteration} {registerEvent.RegisterName}=0x{registerEvent.Value:X4}");
            }
        }

        foreach (ScheduledMemoryWriteEvent memoryWriteEvent in scheduledMemoryWriteEvents)
        {
            if (memoryWriteEvent.Frame == iteration)
            {
                machine.Cpu.WriteMemory(memoryWriteEvent.Address, memoryWriteEvent.Value);
                Console.WriteLine($"POKE frame={iteration} [{memoryWriteEvent.Address:X4}]=0x{memoryWriteEvent.Value:X2}");
            }
        }

        foreach (ScheduledFrameTimingEvent frameTimingEvent in scheduledFrameTimingEvents)
        {
            if (frameTimingEvent.Frame == iteration)
            {
                machine.SetFrameTimingForDebug(frameTimingEvent.FrameTStates);
                Console.WriteLine($"FRAMET frame={iteration} frametstates={frameTimingEvent.FrameTStates}");
            }
        }

        ExecuteHarnessStep(machine, executionMode);

        if (machine.TryConsumeAutoDebugDump(out string reason, out string dump))
        {
            WriteHarnessArtifacts(machine, dump, "auto");
            Console.WriteLine(reason);
            Console.WriteLine(dump);
            return;
        }

        if (machine.HasPendingMountedLoadUsrContinuation != lastPendingMountedLoadContinuation)
        {
            lastPendingMountedLoadContinuation = machine.HasPendingMountedLoadUsrContinuation;
            Console.WriteLine(
                $"PENDING frame={machine.FrameCount} pending={machine.HasPendingMountedLoadUsrContinuation} " +
                $"pc=0x{machine.Cpu.Regs.PC:X4} tape={machine.GetMountedTapeDebugState()}");
            Console.WriteLine(BuildBatmanSysVarDebug(machine));
        }

        if (machine.FrameCount != lastReportedFrame && machine.FrameCount % 10 == 0)
        {
            lastReportedFrame = machine.FrameCount;
            Console.WriteLine(
                $"Frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} " +
                $"IFF1={machine.Cpu.IFF1} IFF2={machine.Cpu.IFF2} INTP={machine.Cpu.InterruptPending} " +
                $"Tape={machine.GetMountedTapeDebugState()}");
            if (machine.FrameCount >= 14110 && machine.FrameCount <= 14180)
                Console.WriteLine(BuildBatmanSysVarDebug(machine));
        }

        if (!string.IsNullOrEmpty(batmanForceContinuationMode) &&
            machine.FrameCount == 13820 &&
            machine.HasPendingMountedLoadUsrContinuation)
        {
            FieldInfo? pendingField = typeof(Spectrum128Machine).GetField(
                "pendingMountedLoadUsrContinuationResolver",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (pendingField?.GetValue(machine) is Func<Spectrum128Machine, ushort?> pendingResolver)
            {
                Console.WriteLine("PREVIEW " + BuildBatmanSysVarDebug(machine));
                Console.WriteLine(BuildPendingMountedLoadDebug(machine));
                if (!string.IsNullOrEmpty(batmanForceContinuationMode))
                {
                    if (batmanForceContinuationMode == "curchl")
                    {
                        ushort curChl = (ushort)(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8));
                        machine.SetPendingMountedLoadUsrContinuation(curChl);
                        Console.WriteLine($"PREVIEW forced=curchl entry=0x{curChl:X4}");
                    }
                    else if (batmanForceContinuationMode == "usr0")
                    {
                        machine.SetPendingMountedLoadUsrContinuation(0);
                        Console.WriteLine("PREVIEW forced=usr0 entry=0x0000");
                    }
                    else if (batmanForceContinuationMode == "natural")
                    {
                        ushort? preview = pendingResolver(machine);
                        Console.WriteLine($"PREVIEW resolver={(preview.HasValue ? $"0x{preview.Value:X4}" : "null")} pc=0x{machine.Cpu.Regs.PC:X4}");
                        return;
                    }
                    else if (batmanForceContinuationMode == "clear")
                    {
                        machine.ClearPendingMountedLoadUsrContinuation();
                        Console.WriteLine("PREVIEW forced=clearpending");
                    }
                }
                else
                {
                    ushort? preview = pendingResolver(machine);
                    Console.WriteLine($"PREVIEW resolver={(preview.HasValue ? $"0x{preview.Value:X4}" : "null")} pc=0x{machine.Cpu.Regs.PC:X4}");
                    return;
                }
            }
        }

        iteration++;
    }

    string finalDump = machine.BuildDebugDump($"ManualHarness end-of-run dump after {machine.FrameCount} frames.");
    finalDump += BuildPendingMountedLoadDebug(machine);
    finalDump += BuildBasicProgramMemoryDebug(machine);
    WriteHarnessArtifacts(machine, finalDump, "end");
    if (machine.HasPendingMountedLoadUsrContinuation)
    {
        MethodInfo? resumeMethod = typeof(Spectrum128Machine).GetMethod(
            "TryResumePendingMountedLoadUsrContinuation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (resumeMethod != null)
        {
            bool forcedResume = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
            Console.WriteLine(
                $"FORCED-RESUME frame={machine.FrameCount} resumed={forcedResume} pc=0x{machine.Cpu.Regs.PC:X4} " +
                $"bc=0x{machine.Cpu.Regs.BC:X4} tape={machine.GetMountedTapeDebugState()}");
        }
    }
    Console.WriteLine("No auto debug dump was triggered.");
    return;
}

static void ExecuteHarnessStep(Spectrum128Machine machine, string executionMode)
{
    switch (executionMode)
    {
        case "frame":
            machine.ExecuteFrame();
            return;
        case "slice1":
            machine.ExecuteTimeSlice(Math.Max(1, machine.CurrentCpuClockHz / 1000));
            return;
        case "slice8":
            machine.ExecuteTimeSlice(Math.Max(1, (machine.CurrentCpuClockHz / 1000) * 8));
            return;
        default:
            throw new InvalidOperationException($"Unsupported execution mode '{executionMode}'.");
    }
}

static string DescribeTapeExecution(string format, TapeExecutionResult result)
{
    return result.Strategy switch
    {
        TapeLoadStrategy.FullFakeLoad =>
            $"{format} full-load complete: blocks={result.TotalBlockCount} consumed={result.ConsumedBlockCount} autoStart={result.AutoStartFileName ?? "(none)"}",
        TapeLoadStrategy.LeadingStandardChainFakeLoad =>
            $"{format} leading standard chain fake-load: blocks={result.TotalBlockCount} consumed={result.ConsumedBlockCount} autoStart={result.AutoStartFileName ?? "(none)"} mounted={result.DisplayName}",
        TapeLoadStrategy.BootstrapHybrid =>
            $"{format} hybrid bootstrap complete: blocks={result.TotalBlockCount} consumed={result.ConsumedBlockCount} autoStart={result.AutoStartFileName ?? "(none)"} mounted={result.DisplayName}",
        TapeLoadStrategy.RomBootstrapMounted =>
            $"{format} ROM bootstrap mounted: blocks={result.TotalBlockCount} consumed={result.ConsumedBlockCount} autoStart={result.AutoStartFileName ?? "(none)"} mounted={result.DisplayName}",
        _ =>
            $"{format} mounted: blocks={result.TotalBlockCount} display={result.DisplayName}"
    };
}

static void DumpTapeBlocks(string tapePath, bool stopTapeIf48k)
{
    byte[] data = File.ReadAllBytes(tapePath);
    IReadOnlyList<TapeBlock> parsedBlocks;
    IReadOnlyList<TapeBlock> executionBlocks;

    if (Path.GetExtension(tapePath).Equals(".tzx", StringComparison.OrdinalIgnoreCase))
    {
        MethodInfo parseMethod = typeof(TzxLoader).GetMethod(
            "ParseBlocks",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        parsedBlocks = (IReadOnlyList<TapeBlock>)parseMethod.Invoke(null, new object[] { data, stopTapeIf48k })!;

        MethodInfo prepareMethod = typeof(TzxLoader).GetMethod(
            "PrepareBlocksForExecution",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        executionBlocks = (IReadOnlyList<TapeBlock>)prepareMethod.Invoke(null, new object[] { parsedBlocks })!;
    }
    else
    {
        MethodInfo parseMethod = typeof(TapLoader).GetMethod(
            "ParseBlocks",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        parsedBlocks = (IReadOnlyList<TapeBlock>)parseMethod.Invoke(null, new object[] { data })!;
        executionBlocks = parsedBlocks;
    }

    Console.WriteLine("=== PARSED BLOCKS ===");
    DumpBlockList(parsedBlocks);
    Console.WriteLine("=== EXECUTION BLOCKS ===");
    DumpBlockList(executionBlocks);
}

static void DumpBatmanBasic(string tapePath)
{
    Type tapLoaderType = typeof(TapLoader);
    MethodInfo parseHeaderInfoMethod = tapLoaderType.GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
    MethodInfo initMachineMethod = tapLoaderType.GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
    MethodInfo loadBasicProgramMethod = tapLoaderType
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method =>
        {
            if (method.Name != "LoadBasicProgram")
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3 &&
                   parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                   parameters[2].ParameterType == typeof(byte[]);
        });
    Type executorType = tapLoaderType.GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
    MethodInfo parseLinesMethod = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;
    MethodInfo createResolverMethod = executorType
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
    MethodInfo tryGetSnapshotMethod = typeof(Spectrum128Machine).GetMethod(
        "TryGetPendingMountedLoadBasicVariableSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    FieldInfo pendingResolverField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadUsrContinuationResolver",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    FieldInfo pendingVariableAreaField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadBasicVariableArea",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    var blocks = TzxLoader.ParseBlocks(File.ReadAllBytes(tapePath));
    Console.WriteLine($"BATMAN blocks={blocks.Count}");
    for (int i = 0; i < blocks.Count; i++)
    {
        TapeBlock block = blocks[i];
        Console.WriteLine(
            $"BATMAN block[{i}] kind={block.Kind} flag=0x{block.Flag:X2} loadable={block.IsLoadableRomBlock} trap={block.CanUseRomLoadTrap} " +
            $"pause={block.PauseAfterBlockMs} payload={(block.Payload?.Length ?? -1)} stream={(block.StreamData?.Length ?? -1)}");
    }

    object batmanHeader = parseHeaderInfoMethod.Invoke(null, new object[] { blocks[0] })!;
    Type batmanHeaderType = batmanHeader.GetType();
    ushort programLength = (ushort)batmanHeaderType.GetProperty("ProgramLength")!.GetValue(batmanHeader)!;
    ushort autoStartLine = (ushort)batmanHeaderType.GetProperty("AutoStartLine")!.GetValue(batmanHeader)!;
    Console.WriteLine($"BATMAN header len={programLength} auto={autoStartLine}");

    string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
    var machine = new Spectrum128Machine(romFolder);
    initMachineMethod.Invoke(null, new object[] { machine, false });
    loadBasicProgramMethod.Invoke(null, new object[] { machine, batmanHeader, blocks[1].Payload! });

    object lines = parseLinesMethod.Invoke(null, new object[] { machine, (ushort)23755, programLength })!;
    int lineCounter = 0;
    foreach (object line in (System.Collections.IEnumerable)lines)
    {
        Type lineType = line.GetType();
        ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
        var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
        var renderedStatements = new List<string>();
        foreach (object stmtObj in statements)
        {
            var stmtTokens = new List<string>();
            foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                stmtTokens.Add(token?.ToString() ?? string.Empty);
            renderedStatements.Add(string.Join(" ", stmtTokens));
        }

        Console.WriteLine($"BATMAN line[{lineCounter++}] {number}: {string.Join(" : ", renderedStatements)}");
    }

    object? resolver = createResolverMethod.Invoke(null, new object[] { machine, (ushort)23755, programLength, autoStartLine });
    Console.WriteLine($"BATMAN resolverCreated={(resolver != null ? 1 : 0)}");

    TapeExecutionResult result = TzxLoader.LoadWithPolicy(machine, tapePath);
    Console.WriteLine($"BATMAN strategy={result.Strategy} consumed={result.ConsumedBlockCount}/{result.TotalBlockCount}");
    DumpBatmanSnapshot(machine, tryGetSnapshotMethod, "BATMAN snapshot after LoadWithPolicy");

    for (int frame = 0; frame < 15000; frame++)
    {
        machine.ExecuteFrame();
        if (machine.FrameCount is 1 or 10 or 100 or 1000)
            DumpBatmanSnapshot(machine, tryGetSnapshotMethod, $"BATMAN snapshot frame={machine.FrameCount}");
        if (machine.FrameCount == 14110)
        {
            Console.WriteLine($"BATMAN preview frame={machine.FrameCount} pc=0x{machine.Cpu.Regs.PC:X4} tape={machine.GetMountedTapeDebugState()}");
            Console.WriteLine(
                $"BATMAN sysvars CHANS=0x{ReadWord(machine, 23631):X4} CURCHL=0x{ReadWord(machine, 23633):X4} " +
                $"PROG=0x{ReadWord(machine, 23635):X4} VARS=0x{ReadWord(machine, 23627):X4} ELINE=0x{ReadWord(machine, 23641):X4}");

            DumpBatmanSnapshot(machine, tryGetSnapshotMethod, "BATMAN snapshot preview");

            if (pendingResolverField.GetValue(machine) is Func<Spectrum128Machine, ushort?> previewResolver)
            {
                Console.WriteLine($"BATMAN resolver preview=0x{previewResolver(machine):X4}");
                object? preservedVariableArea = pendingVariableAreaField.GetValue(machine);
                pendingVariableAreaField.SetValue(machine, null);
                Console.WriteLine($"BATMAN resolver without snapshot=0x{previewResolver(machine):X4}");
                pendingVariableAreaField.SetValue(machine, preservedVariableArea);
            }

            break;
        }
    }
}

static void DumpBatmanSnapshot(Spectrum128Machine machine, MethodInfo tryGetSnapshotMethod, string label)
{
    object[] snapshotArgs = { (ushort)0, Array.Empty<byte>() };
    if (!(bool)tryGetSnapshotMethod.Invoke(machine, snapshotArgs)!)
    {
        Console.WriteLine($"{label}: none");
        return;
    }

    ushort vars = (ushort)snapshotArgs[0];
    byte[] data = (byte[])snapshotArgs[1];
    Console.WriteLine($"{label}: vars=0x{vars:X4} bytes={data.Length}");
    Console.Write($"{label} data:");
    for (int i = 0; i < data.Length; i++)
        Console.Write($" {data[i]:X2}");
    Console.WriteLine();
    DumpNumericVariable(data, 'a');
}

static ushort ReadWord(Spectrum128Machine machine, ushort address)
{
    return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
}

static void DumpNumericVariable(byte[] data, char variableName)
{
    byte targetHeader = (byte)(0x60 | (char.ToLowerInvariant(variableName) - 'a'));
    int index = 0;
    while (index < data.Length)
    {
        byte header = data[index];
        if (header == 0x80)
        {
            Console.WriteLine($"BATMAN variable {variableName} not found before end marker");
            return;
        }

        int entryLength = GetVariableEntryLength(data, index, header);
        if (entryLength <= 0 || index + entryLength > data.Length)
        {
            Console.WriteLine($"BATMAN variable decode failed at index {index} header=0x{header:X2}");
            return;
        }

        if ((header & 0xE0) == 0x60 && header == targetHeader)
        {
            Console.WriteLine(
                $"BATMAN variable {variableName} bytes={data[index + 1]:X2} {data[index + 2]:X2} {data[index + 3]:X2} {data[index + 4]:X2} {data[index + 5]:X2}");
            return;
        }

        index += entryLength;
    }

    Console.WriteLine($"BATMAN variable {variableName} not found");
}

static int GetVariableEntryLength(byte[] data, int index, byte header)
{
    if ((header & 0xE0) == 0x60)
        return 6;

    if ((header & 0xE0) == 0x40 || (header & 0xE0) == 0xA0 || (header & 0xE0) == 0xC0)
    {
        if (index + 2 >= data.Length)
            return -1;
        ushort totalLength = (ushort)(data[index + 1] | (data[index + 2] << 8));
        return 3 + totalLength;
    }

    if ((header & 0xE0) == 0x20)
    {
        int current = index + 1;
        while (current < data.Length)
        {
            byte nameByte = data[current++];
            if ((nameByte & 0x80) != 0)
            {
                if (current + 5 > data.Length)
                    return -1;
                return (current - index) + 5;
            }
        }
    }

    return -1;
}

static void DumpBlockList(IReadOnlyList<TapeBlock> blocks)
{
    for (int index = 0; index < blocks.Count; index++)
    {
        TapeBlock block = blocks[index];
        int byteCount = block.StreamByteCount;
        int previewCount = Math.Min(byteCount, 16);
        Span<string> preview = previewCount == 0 ? [] : new string[previewCount];
        for (int i = 0; i < previewCount; i++)
            preview[i] = $"{block.GetStreamByte(i):X2}";

        Console.WriteLine(
            $"[{index}] Kind={block.Kind} Rom={block.IsLoadableRomBlock} Trap={block.CanUseRomLoadTrap} " +
            $"Bytes={byteCount} Flag={block.Flag:X2} UsedBits={block.UsedBitsInLastByte} Pause={block.PauseAfterBlockMs} " +
            $"Pilot={block.PilotPulseCount}@{block.PilotPulseLength} Zero={block.ZeroBitPulseLength} One={block.OneBitPulseLength} " +
            $"Head={(previewCount == 0 ? "-" : string.Join(' ', preview.ToArray()))}");
    }
}

Console.WriteLine("Manual smoke harness starting...");
for (int frame = 0; frame < 120; frame++)
{
    if (frame == 60)
    {
        machine.SetKey(0, 0, true);
        machine.SetKey(4, 4, true);
    }

    if (frame == 62)
    {
        machine.SetKey(0, 0, false);
        machine.SetKey(4, 4, false);
    }

    machine.ExecuteFrame();

    if (frame % 10 == 0)
    {
        Console.WriteLine(
            $"Frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} ROM={machine.CurrentRomBank} RAM={machine.PagedRamBank} SCREEN={machine.ScreenBank}");
    }
}

var hostClock = System.Diagnostics.Stopwatch.StartNew();

for (int frame = 0; frame < 250; frame++)
{
    machine.ExecuteFrame();
}

hostClock.Stop();
Console.WriteLine($"250 frames executed in {hostClock.ElapsedMilliseconds} ms");

Console.WriteLine("Manual smoke harness complete.");

static List<ScheduledKeyEvent> ParseKeyScript(string script)
{
    var events = new List<ScheduledKeyEvent>();
    if (string.IsNullOrWhiteSpace(script))
        return events;

    string[] tokens = script.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (string token in tokens)
    {
        string[] parts = token.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            throw new InvalidOperationException($"Invalid key script token '{token}'. Expected frame:key:down|up.");

        int frame = int.Parse(parts[0]);
        string keyName = parts[1];
        bool pressed = parts[2].Equals("down", StringComparison.OrdinalIgnoreCase);
        if (!pressed && !parts[2].Equals("up", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid key action '{parts[2]}' in token '{token}'. Use down or up.");

        events.Add(new ScheduledKeyEvent(frame, keyName, pressed));
    }

    return events;
}

static ScheduledPcEvent ParsePcEvent(string script)
{
    string[] parts = script.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
        throw new InvalidOperationException($"Invalid PC event '{script}'. Expected frame:address.");

    int frame = int.Parse(parts[0]);
    ushort programCounter = ParseAddress(parts[1]);
    return new ScheduledPcEvent(frame, programCounter);
}

static ScheduledRegisterEvent ParseRegisterEvent(string script)
{
    string[] parts = script.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 3)
        throw new InvalidOperationException($"Invalid register event '{script}'. Expected frame:name:value.");

    int frame = int.Parse(parts[0]);
    return new ScheduledRegisterEvent(frame, parts[1], ParseAddress(parts[2]));
}

static ScheduledMemoryWriteEvent ParseMemoryWriteEvent(string script)
{
    string[] parts = script.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 3)
        throw new InvalidOperationException($"Invalid memory write event '{script}'. Expected frame:address:value.");

    int frame = int.Parse(parts[0]);
    ushort address = ParseAddress(parts[1]);
    byte value = ParseByte(parts[2]);
    return new ScheduledMemoryWriteEvent(frame, address, value);
}

static ScheduledFrameTimingEvent ParseFrameTimingEvent(string script)
{
    string[] parts = script.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
        throw new InvalidOperationException($"Invalid frame timing event '{script}'. Expected frame:tstates.");

    int frame = int.Parse(parts[0]);
    int frameTStates = int.Parse(parts[1]);
    return new ScheduledFrameTimingEvent(frame, frameTStates);
}

static TapeLoadStrategy ParseTapeStrategy(string value)
{
    return value.Trim().ToLowerInvariant() switch
    {
        "full" => TapeLoadStrategy.FullFakeLoad,
        "leadchain" => TapeLoadStrategy.LeadingStandardChainFakeLoad,
        "rom" => TapeLoadStrategy.RomBootstrapMounted,
        "hybrid" => TapeLoadStrategy.BootstrapHybrid,
        "mounted" => TapeLoadStrategy.MountedRealtime,
        _ => throw new InvalidOperationException($"Unsupported tape strategy '{value}'.")
    };
}

static TapeExecutionResult LoadTapeWithForcedStrategy(
    Spectrum128Machine machine,
    string path,
    TapeLoadStrategy strategy,
    bool isTzx)
{
    byte[] fileData = File.ReadAllBytes(path);
    IReadOnlyList<TapeBlock> blocks = isTzx
        ? TzxLoader.ParseBlocks(fileData)
        : InvokeTapLoaderParseBlocks(fileData);
    TapeLoadPlan plan = new(strategy, $"Forced harness strategy {strategy}");
    return InvokeTapLoaderExecutePlan(machine, Path.GetFileName(path), blocks, plan, initialEarLevelHigh: !isTzx);
}

static IReadOnlyList<TapeBlock> InvokeTapLoaderParseBlocks(byte[] fileData)
{
    MethodInfo? parseBlocks = typeof(TapLoader).GetMethod(
        "ParseBlocks",
        BindingFlags.Static | BindingFlags.NonPublic);
    if (parseBlocks == null)
        throw new InvalidOperationException("Could not find TapLoader.ParseBlocks via reflection.");

    return (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { fileData })!;
}

static TapeExecutionResult InvokeTapLoaderExecutePlan(
    Spectrum128Machine machine,
    string displayName,
    IReadOnlyList<TapeBlock> blocks,
    TapeLoadPlan plan,
    bool initialEarLevelHigh)
{
    MethodInfo? executePlan = typeof(TapLoader).GetMethod(
        "ExecutePlan",
        BindingFlags.Static | BindingFlags.NonPublic);
    if (executePlan == null)
        throw new InvalidOperationException("Could not find TapLoader.ExecutePlan via reflection.");

    return (TapeExecutionResult)executePlan.Invoke(null, new object[] { machine, displayName, blocks, plan, initialEarLevelHigh })!;
}

static (ushort Start, ushort End) ParseTraceRange(string script)
{
    string[] parts = script.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
        throw new InvalidOperationException($"Invalid trace range '{script}'. Expected start-end.");

    return (ParseAddress(parts[0]), ParseAddress(parts[1]));
}

static ushort ParseAddress(string value)
{
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return Convert.ToUInt16(value[2..], 16);

    return Convert.ToUInt16(value, 16);
}

static byte ParseByte(string value)
{
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return Convert.ToByte(value[2..], 16);

    return Convert.ToByte(value, 16);
}

static void ApplyRegister(Spectrum128Machine machine, string registerName, ushort value)
{
    switch (registerName.ToUpperInvariant())
    {
        case "AF": machine.Cpu.Regs.AF = value; break;
        case "BC": machine.Cpu.Regs.BC = value; break;
        case "DE": machine.Cpu.Regs.DE = value; break;
        case "HL": machine.Cpu.Regs.HL = value; break;
        case "IX": machine.Cpu.Regs.IX = value; break;
        case "IY": machine.Cpu.Regs.IY = value; break;
        case "SP": machine.Cpu.Regs.SP = value; break;
        case "PC": machine.Cpu.Regs.PC = value; break;
        default:
            throw new InvalidOperationException($"Unsupported register name '{registerName}'.");
    }
}

static void ApplyKey(Spectrum128Machine machine, string keyName, bool pressed)
{
    foreach ((int row, int bit) in ResolveKey(keyName))
        machine.SetKey(row, bit, pressed);
}

static IEnumerable<(int row, int bit)> ResolveKey(string keyName)
{
    switch (keyName.ToLowerInvariant())
    {
        case "1": yield return (3, 0); yield break;
        case "2": yield return (3, 1); yield break;
        case "3": yield return (3, 2); yield break;
        case "4": yield return (3, 3); yield break;
        case "5": yield return (3, 4); yield break;
        case "0": yield return (4, 0); yield break;
        case "9": yield return (4, 1); yield break;
        case "8": yield return (4, 2); yield break;
        case "7": yield return (4, 3); yield break;
        case "6": yield return (4, 4); yield break;
        case "y": yield return (5, 4); yield break;
        case "u": yield return (5, 3); yield break;
        case "i": yield return (5, 2); yield break;
        case "o": yield return (5, 1); yield break;
        case "p": yield return (5, 0); yield break;
        case "r": yield return (2, 3); yield break;
        case "h": yield return (6, 4); yield break;
        case "j": yield return (6, 3); yield break;
        case "k": yield return (6, 2); yield break;
        case "l": yield return (6, 1); yield break;
        case "n": yield return (7, 3); yield break;
        case "m": yield return (7, 2); yield break;
        case "enter": yield return (6, 0); yield break;
        case "space": yield return (7, 0); yield break;
        case "shift": yield return (0, 0); yield break;
        case "fire": yield return (7, 1); yield break;
        case "left":
            yield return (0, 0);
            yield return (3, 4);
            yield break;
        case "down":
            yield return (0, 0);
            yield return (4, 4);
            yield break;
        case "up":
            yield return (0, 0);
            yield return (4, 3);
            yield break;
        case "right":
            yield return (0, 0);
            yield return (4, 2);
            yield break;
        case "back":
            yield return (0, 0);
            yield return (4, 0);
            yield break;
        default:
            throw new InvalidOperationException($"Unsupported key name '{keyName}'.");
    }
}

static void WriteHarnessArtifacts(Spectrum128Machine machine, string dump, string tag)
{
    string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
    Directory.CreateDirectory(debugFolder);
    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");

    string dumpPath = Path.Combine(debugFolder, $"harness-{tag}-{stamp}.txt");
    File.WriteAllText(dumpPath, dump);

    string imagePath = Path.Combine(debugFolder, $"harness-{tag}-{stamp}.png");
    using var bitmap = new Bitmap(Spectrum128Machine.ScreenWidth, Spectrum128Machine.ScreenHeight, PixelFormat.Format32bppArgb);
    SpectrumRenderer.RenderToBitmap(bitmap, machine.GetScreenBankData(), machine.BorderColor, machine.FlashPhase);
    bitmap.Save(imagePath, ImageFormat.Png);

    Console.WriteLine($"Harness artifacts: {dumpPath}");
    Console.WriteLine($"Harness frame image: {imagePath}");
}

static string BuildPendingMountedLoadDebug(Spectrum128Machine machine)
{
    FieldInfo? pendingResolverField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadUsrContinuationResolver",
        BindingFlags.Instance | BindingFlags.NonPublic);
    FieldInfo? pendingResumeLineField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadBasicResumeLine",
        BindingFlags.Instance | BindingFlags.NonPublic);
    FieldInfo? pendingResumeStatementField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadBasicResumeStatement",
        BindingFlags.Instance | BindingFlags.NonPublic);
    FieldInfo? pendingVariableAreaField = typeof(Spectrum128Machine).GetField(
        "pendingMountedLoadBasicVariableArea",
        BindingFlags.Instance | BindingFlags.NonPublic);

    object? resolver = pendingResolverField?.GetValue(machine);
    object? resumeLine = pendingResumeLineField?.GetValue(machine);
    object? resumeStatement = pendingResumeStatementField?.GetValue(machine);
    object? pendingVariableArea = pendingVariableAreaField?.GetValue(machine);
    string variableDebug = BuildMountedVariableDebug(machine);
    string variableAreaDebug = pendingVariableArea?.ToString() ?? "(null)";
    string variableBytesDebug = BuildMountedVariableBytesDebug(machine);

    return Environment.NewLine +
           "=== PENDING MOUNTED LOAD ===" + Environment.NewLine +
           $"HasPendingResolver={(resolver != null ? 1 : 0)}" + Environment.NewLine +
           $"PendingResumeLine={(resumeLine ?? "(null)")}" + Environment.NewLine +
           $"PendingResumeStatement={(resumeStatement ?? "(null)")}" + Environment.NewLine +
           $"PendingVariableArea={variableAreaDebug}" + Environment.NewLine +
           $"{variableBytesDebug}" + Environment.NewLine +
           $"{variableDebug}" + Environment.NewLine;
}

static string BuildMountedVariableDebug(Spectrum128Machine machine)
{
    MethodInfo? variableReader = typeof(TapLoader).GetMethod(
        "TryReadMountedContinuationNumericVariable",
        BindingFlags.Static | BindingFlags.NonPublic);
    if (variableReader == null)
        return "MountedVar[a]=(unavailable)";

    object[] args = new object[] { machine, "a", 0 };
    bool success = (bool)variableReader.Invoke(null, args)!;
    return success
        ? $"MountedVar[a]={args[2]}"
        : "MountedVar[a]=(missing)";
}

static string BuildMountedVariableBytesDebug(Spectrum128Machine machine)
{
    MethodInfo? snapshotMethod = typeof(Spectrum128Machine).GetMethod(
        "TryGetPendingMountedLoadBasicVariableSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    if (snapshotMethod == null)
        return "MountedVarBytes=(unavailable)";

    object[] args = new object[] { (ushort)0, Array.Empty<byte>() };
    bool success = (bool)snapshotMethod.Invoke(machine, args)!;
    if (!success || args[1] is not byte[] data || data.Length == 0)
        return "MountedVarBytes=(missing)";

    int count = Math.Min(24, data.Length);
    string[] bytes = new string[count];
    for (int i = 0; i < count; i++)
        bytes[i] = data[i].ToString("X2");

    return $"MountedVarBytes={string.Join(' ', bytes)}";
}

static string BuildBatmanSysVarDebug(Spectrum128Machine machine)
{
    static ushort ReadWord(Spectrum128Machine machine, ushort address) =>
        (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));

    ushort vars = ReadWord(machine, 23627);

    return
        $"SYSV frame={machine.FrameCount} " +
        $"CHANS=0x{ReadWord(machine, 23631):X4} CURCHL=0x{ReadWord(machine, 23633):X4} " +
        $"PROG=0x{ReadWord(machine, 23635):X4} VARS=0x{vars:X4} " +
        $"ELINE=0x{ReadWord(machine, 23641):X4} KCUR=0x{ReadWord(machine, 23643):X4} " +
        $"CHADD=0x{ReadWord(machine, 23645):X4} XPTR=0x{ReadWord(machine, 23647):X4} " +
        $"WORKSP=0x{ReadWord(machine, 23649):X4} STKBOT=0x{ReadWord(machine, 23651):X4} STKEND=0x{ReadWord(machine, 23653):X4} " +
        $"NEWPPC=0x{ReadWord(machine, 23618):X4} NSPPC={machine.PeekMemory(23620)} " +
        $"PPC=0x{ReadWord(machine, 23621):X4} SUBPPC={machine.PeekMemory(23623)}";
}

static string BuildBasicProgramMemoryDebug(Spectrum128Machine machine)
{
    static ushort ReadWord(Spectrum128Machine machine, ushort address) =>
        (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));

    static string DumpMemory(Spectrum128Machine machine, ushort address, int length)
    {
        var builder = new System.Text.StringBuilder();
        for (int offset = 0; offset < length; offset += 16)
        {
            builder.Append($"{(ushort)(address + offset):X4}: ");
            int rowLength = Math.Min(16, length - offset);
            for (int i = 0; i < rowLength; i++)
                builder.Append($"{machine.PeekMemory((ushort)(address + offset + i)):X2} ");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    ushort prog = ReadWord(machine, 23635);
    ushort vars = ReadWord(machine, 23627);
    ushort eLine = ReadWord(machine, 23641);
    if (prog == 0 || vars <= prog)
        return string.Empty;

    int dumpLength = Math.Min(128, vars - prog);
    return Environment.NewLine +
           "=== BASIC PROGRAM MEMORY ===" + Environment.NewLine +
           $"PROG=0x{prog:X4} VARS=0x{vars:X4} LEN={vars - prog}" + Environment.NewLine +
           DumpMemory(machine, prog, dumpLength);
}

readonly record struct ScheduledKeyEvent(int Frame, string KeyName, bool Pressed);
readonly record struct ScheduledPcEvent(int Frame, ushort ProgramCounter);
readonly record struct ScheduledRegisterEvent(int Frame, string RegisterName, ushort Value);
readonly record struct ScheduledMemoryWriteEvent(int Frame, ushort Address, byte Value);
readonly record struct ScheduledFrameTimingEvent(int Frame, int FrameTStates);
