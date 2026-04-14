using System;
using System.IO;
using Spectrum128kEmulator;

string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
var machine = new Spectrum128Machine(romFolder)
{
    Trace = s =>
    {
        if (s.StartsWith("UNIMPL") || s.StartsWith("[7FFD]"))
            Console.WriteLine(s);
    }
};

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
