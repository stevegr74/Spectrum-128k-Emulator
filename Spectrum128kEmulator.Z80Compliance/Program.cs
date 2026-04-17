using System;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

if (args.Length == 1 && string.Equals(args[0], "fast-daa-group", StringComparison.OrdinalIgnoreCase))
{
    var z80cpu = new Z80Cpu();
    DaaGroupTest.Run(z80cpu);
    return 0;
}

if (args.Length < 1 || args.Length > 2)
{
    Console.WriteLine("Usage: Spectrum128kEmulator.Z80Compliance <path-to-zexdoc.com> [max-steps]");
    Console.WriteLine("   or: Spectrum128kEmulator.Z80Compliance fast-daa-group");
    return 1;
}

string comPath = args[0];
if (!File.Exists(comPath))
{
    Console.WriteLine($"File not found: {comPath}");
    return 1;
}

long maxSteps = 2_000_000_000;
if (args.Length == 2 && !long.TryParse(args[1], out maxSteps))
{
    Console.WriteLine($"Invalid max-steps value: {args[1]}");
    return 1;
}

byte[] program = File.ReadAllBytes(comPath);
byte[] memory = new byte[65536];

if (program.Length + 0x100 > 65536)
{
    Console.WriteLine("Program is too large to fit in memory.");
    return 1;
}

Buffer.BlockCopy(program, 0, memory, 0x0100, program.Length);

memory[0x0000] = 0xC9;
memory[0x0005] = 0xC9;

var cpu = new Z80Cpu
{
    ReadMemory = addr => memory[addr],
    WriteMemory = (addr, value) => memory[addr] = value,
    ReadPort = _ => 0xFF,
    WritePort = (_, _) => { }
};

cpu.Reset();
cpu.Regs.PC = 0x0100;
cpu.Regs.SP = 0xF000;

var output = new StringBuilder();
bool finished = false;
long steps = 0;

const long progressInterval = 5_000_000;
const long repeatedPcWarningThreshold = 2_000_000;

ushort lastObservedPc = cpu.Regs.PC;
long repeatedPcCount = 0;
int bdosCallCount = 0;
int bdosStringCallCount = 0;
int outputCharCount = 0;

var wallClock = System.Diagnostics.Stopwatch.StartNew();

while (!finished && steps < maxSteps)
{
    ushort pcBeforeStep = cpu.Regs.PC;

    if (pcBeforeStep == 0x0005)
    {
        finished = HandleBdos(cpu, memory, output, ref bdosCallCount, ref bdosStringCallCount, ref outputCharCount);
        continue;
    }

    if (pcBeforeStep == 0x0000)
    {
        finished = true;
        break;
    }

    cpu.Step();
    steps++;

    if (cpu.Regs.PC == lastObservedPc)
    {
        repeatedPcCount++;
    }
    else
    {
        lastObservedPc = cpu.Regs.PC;
        repeatedPcCount = 0;
    }

    if (repeatedPcCount == repeatedPcWarningThreshold)
    {
        Console.WriteLine(
            $"[warn] PC repeated for a long time: PC=0x{cpu.Regs.PC:X4} " +
            $"steps={steps:N0} tstates={cpu.TStates:N0} " +
            $"AF=0x{cpu.Regs.AF:X4} BC=0x{cpu.Regs.BC:X4} DE=0x{cpu.Regs.DE:X4} HL=0x{cpu.Regs.HL:X4} SP=0x{cpu.Regs.SP:X4}");
    }

    if (steps % progressInterval == 0)
    {
        Console.WriteLine(
            $"[progress] steps={steps:N0} tstates={cpu.TStates:N0} elapsed={wallClock.Elapsed} " +
            $"PC=0x{cpu.Regs.PC:X4} SP=0x{cpu.Regs.SP:X4} " +
            $"AF=0x{cpu.Regs.AF:X4} BC=0x{cpu.Regs.BC:X4} DE=0x{cpu.Regs.DE:X4} HL=0x{cpu.Regs.HL:X4} " +
            $"BDOS={bdosCallCount} BDOS9={bdosStringCallCount} outChars={outputCharCount}");
    }
}

Console.WriteLine(output.ToString());

Console.WriteLine(
    $"[summary] finished={finished} steps={steps:N0} tstates={cpu.TStates:N0} elapsed={wallClock.Elapsed} " +
    $"PC=0x{cpu.Regs.PC:X4} SP=0x{cpu.Regs.SP:X4} " +
    $"AF=0x{cpu.Regs.AF:X4} BC=0x{cpu.Regs.BC:X4} DE=0x{cpu.Regs.DE:X4} HL=0x{cpu.Regs.HL:X4} " +
    $"BDOS={bdosCallCount} BDOS9={bdosStringCallCount} outChars={outputCharCount}");

if (steps >= maxSteps)
{
    Console.WriteLine("Execution stopped: step limit reached.");
    return 2;
}

return 0;

static bool HandleBdos(
    Z80Cpu cpu,
    byte[] memory,
    StringBuilder output,
    ref int bdosCallCount,
    ref int bdosStringCallCount,
    ref int outputCharCount)
{
    byte function = cpu.Regs.C;
    bool shouldExit = false;
    bdosCallCount++;

    switch (function)
    {
        case 0:
            shouldExit = true;
            break;

        case 2:
            output.Append((char)cpu.Regs.E);
            outputCharCount++;
            break;

        case 9:
            bdosStringCallCount++;
            ushort addr = cpu.Regs.DE;
            while (memory[addr] != (byte)'$')
            {
                output.Append((char)memory[addr]);
                outputCharCount++;
                addr++;
            }
            break;

        default:
            throw new InvalidOperationException(
                $"Unhandled BDOS function {function} at PC=0x{cpu.Regs.PC:X4}");
    }

    ushort returnAddress = Pop(cpu, memory);
    cpu.Regs.PC = returnAddress;

    return shouldExit;
}

static ushort Pop(Z80Cpu cpu, byte[] memory)
{
    ushort sp = cpu.Regs.SP;
    byte low = memory[sp];
    byte high = memory[(ushort)(sp + 1)];
    cpu.Regs.SP += 2;
    return (ushort)(low | (high << 8));
}
