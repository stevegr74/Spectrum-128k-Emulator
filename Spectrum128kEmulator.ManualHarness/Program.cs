using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Spectrum128kEmulator;

string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
var machine = new Spectrum128Machine(romFolder)
{
    Trace = s =>
    {
        if (s.StartsWith("UNIMPL", StringComparison.Ordinal) ||
            s.StartsWith("[7FFD]", StringComparison.Ordinal))
        {
            Console.WriteLine(s);
        }
    }
};

if (args.Length > 0)
{
    string snapshotPath = args[0];
    int? initialInterruptDelay = null;
    int frameLimit = 300;
    int floatingBusDisplayStartAdjust = 0;
    int floatingBusSampleAdjust = 0;
    List<ScheduledKeyEvent> scheduledKeyEvents = new();
    List<ScheduledPcEvent> scheduledPcEvents = new();
    List<ScheduledRegisterEvent> scheduledRegisterEvents = new();
    List<ScheduledMemoryWriteEvent> scheduledMemoryWriteEvents = new();
    if (args.Length > 1)
        initialInterruptDelay = int.Parse(args[1]);
    if (args.Length > 2)
        frameLimit = int.Parse(args[2]);
    for (int argIndex = 3; argIndex < args.Length; argIndex++)
    {
        string arg = args[argIndex];
        if (arg.StartsWith("fbstart=", StringComparison.OrdinalIgnoreCase))
        {
            floatingBusDisplayStartAdjust = int.Parse(arg["fbstart=".Length..]);
        }
        else if (arg.StartsWith("fbsample=", StringComparison.OrdinalIgnoreCase))
        {
            floatingBusSampleAdjust = int.Parse(arg["fbsample=".Length..]);
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
        else
        {
            scheduledKeyEvents = ParseKeyScript(arg);
        }
    }

    Console.WriteLine($"Loading snapshot: {snapshotPath}");

    string extension = Path.GetExtension(snapshotPath);
    if (extension.Equals(".sna", StringComparison.OrdinalIgnoreCase))
    {
        SnapshotLoader.LoadSna48k(machine, snapshotPath);
    }
    else if (extension.Equals(".z80", StringComparison.OrdinalIgnoreCase))
    {
        Z80SnapshotLoader.Load(machine, snapshotPath);
    }
    else
    {
        throw new InvalidOperationException($"Unsupported snapshot extension: {extension}");
    }

    if (initialInterruptDelay.HasValue)
    {
        machine.SetInitialInterruptDelay(initialInterruptDelay.Value);
        Console.WriteLine($"Initial interrupt delay: {initialInterruptDelay.Value} T-states");
    }

    if (floatingBusDisplayStartAdjust != 0 || floatingBusSampleAdjust != 0)
    {
        machine.Set48kFloatingBusTimingAdjustments(floatingBusDisplayStartAdjust, floatingBusSampleAdjust);
        Console.WriteLine(
            $"Floating bus timing adjust: displayStart={floatingBusDisplayStartAdjust} sample={floatingBusSampleAdjust}");
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

    for (int frame = 0; frame < frameLimit; frame++)
    {
        foreach (ScheduledKeyEvent keyEvent in scheduledKeyEvents)
        {
            if (keyEvent.Frame == frame)
            {
                ApplyKey(machine, keyEvent.KeyName, keyEvent.Pressed);
                Console.WriteLine($"KEY frame={frame} key={keyEvent.KeyName} pressed={keyEvent.Pressed}");
            }
        }

        foreach (ScheduledPcEvent pcEvent in scheduledPcEvents)
        {
            if (pcEvent.Frame == frame)
            {
                machine.Cpu.Regs.PC = pcEvent.ProgramCounter;
                Console.WriteLine($"PC frame={frame} pc=0x{pcEvent.ProgramCounter:X4}");
            }
        }

        foreach (ScheduledRegisterEvent registerEvent in scheduledRegisterEvents)
        {
            if (registerEvent.Frame == frame)
            {
                ApplyRegister(machine, registerEvent.RegisterName, registerEvent.Value);
                Console.WriteLine($"REG frame={frame} {registerEvent.RegisterName}=0x{registerEvent.Value:X4}");
            }
        }

        foreach (ScheduledMemoryWriteEvent memoryWriteEvent in scheduledMemoryWriteEvents)
        {
            if (memoryWriteEvent.Frame == frame)
            {
                machine.Cpu.WriteMemory(memoryWriteEvent.Address, memoryWriteEvent.Value);
                Console.WriteLine($"POKE frame={frame} [{memoryWriteEvent.Address:X4}]=0x{memoryWriteEvent.Value:X2}");
            }
        }

        machine.ExecuteFrame();

        if (machine.TryConsumeAutoDebugDump(out string reason, out string dump))
        {
            WriteHarnessArtifacts(machine, dump, "auto");
            Console.WriteLine(reason);
            Console.WriteLine(dump);
            return;
        }

        if (machine.Cpu.Regs.SP < 0x0100)
        {
            string lowStackReason = $"ManualHarness low-stack trap at frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4}";
            string lowStackDump = machine.BuildDebugDump(lowStackReason);
            WriteHarnessArtifacts(machine, lowStackDump, "low-stack");
            Console.WriteLine(lowStackReason);
            Console.WriteLine(lowStackDump);
            return;
        }

        if (frame % 10 == 0)
        {
            Console.WriteLine(
                $"Frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} IFF2={machine.Cpu.IFF2} INTP={machine.Cpu.InterruptPending}");
        }
    }

    string finalDump = machine.BuildDebugDump($"ManualHarness end-of-run dump after {machine.FrameCount} frames.");
    WriteHarnessArtifacts(machine, finalDump, "end");
    Console.WriteLine("No auto debug dump was triggered.");
    return;
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

readonly record struct ScheduledKeyEvent(int Frame, string KeyName, bool Pressed);
readonly record struct ScheduledPcEvent(int Frame, ushort ProgramCounter);
readonly record struct ScheduledRegisterEvent(int Frame, string RegisterName, ushort Value);
readonly record struct ScheduledMemoryWriteEvent(int Frame, ushort Address, byte Value);
