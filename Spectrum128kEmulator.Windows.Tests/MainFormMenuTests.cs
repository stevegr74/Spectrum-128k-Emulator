using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class MainFormMenuTests
    {
        [Fact]
        public void ContextMenu_KeepsFunctionKeysOrderedBeforePokeManager()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new MainForm();
                    var menu = (ContextMenuStrip)typeof(MainForm)
                        .GetField("displayContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(form)!;
                    string[] labels = menu.Items
                        .Cast<ToolStripItem>()
                        .Select(item => item is ToolStripSeparator ? "---" : item.Text ?? string.Empty)
                        .ToArray();

                    Assert.Equal(
                        [
                            "F1 - Help",
                            "---",
                            "F2 - Status Overlay",
                            "F3 - Reset / Switch Model",
                            "F4 - Display Size",
                            "---",
                            "F5 - Stop / Resume Tape",
                            "---",
                            "F6 - Debugger / Disassembler",
                            "F7 - Save Quick State",
                            "F8 - Restore Quick State",
                            "---",
                            "F9 - Load .sna Snapshot",
                            "F10 - Load .z80/.szx Snapshot / Play .rzx",
                            "F11 - Mount .tap or .tzx Tape Image",
                            "Shift+F11 - Replace Tape (Preserve Machine)",
                            "---",
                            "F12 - Write Machine Diagnostic Dump",
                            "---",
                            "Joystick",
                            "Poke Manager..."
                        ],
                        labels);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The MainForm menu test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        [Theory]
        [InlineData(Keys.F9)]
        [InlineData(Keys.F10)]
        [InlineData(Keys.F11)]
        [InlineData(Keys.F12)]
        public void FileAndDiagnosticShortcuts_RequireAnUnmodifiedFunctionKey(Keys key)
        {
            MethodInfo method = typeof(MainForm).GetMethod(
                "IsPlainShortcut",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.True((bool)method.Invoke(null, [new KeyEventArgs(key), key])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(key | Keys.Shift), key])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(key | Keys.Control), key])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(key | Keys.Alt), key])!);
        }

        [Fact]
        public void TapeReplacementShortcut_RequiresShiftF11()
        {
            MethodInfo method = typeof(MainForm).GetMethod(
                "IsTapeReplacementShortcut",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.True((bool)method.Invoke(null, [new KeyEventArgs(Keys.F11 | Keys.Shift)])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(Keys.F11)])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(Keys.F11 | Keys.Control)])!);
            Assert.False((bool)method.Invoke(null, [new KeyEventArgs(Keys.F11 | Keys.Shift | Keys.Alt)])!);
        }

        [Fact]
        public void ReplaceMountedTape_PreservesRunningMachineState()
        {
            string tapePath = Path.Combine(Path.GetTempPath(), $"replacement-{Guid.NewGuid():N}.tap");
            File.WriteAllBytes(tapePath, [0x02, 0x00, 0xFF, 0xFF]);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new MainForm();
                    MethodInfo acquirePauseMethod = typeof(MainForm).GetMethod(
                        "AcquireEmulationPauseLease",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    using var pauseLease = (IDisposable)acquirePauseMethod.Invoke(form, null)!;
                    FieldInfo machineField = typeof(MainForm).GetField(
                        "machine",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var machine = (Spectrum128Machine)machineField.GetValue(form)!;
                    machine.Reset(SpectrumMachineModel.Spectrum48K);
                    machine.Cpu.Regs.PC = 0x8123;
                    machine.PokeMemory(0x9000, 0x5A);
                    machine.MountTape(new Tap.MountedTape("side-1.tzx", Array.Empty<Tap.TapeBlock>()));

                    MethodInfo replaceMethod = typeof(MainForm).GetMethod(
                        "ReplaceMountedTape",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var result = (Tap.TapMountResult)replaceMethod.Invoke(form, [tapePath])!;

                    Assert.Same(machine, machineField.GetValue(form));
                    Assert.Equal(SpectrumMachineModel.Spectrum48K, machine.MachineModel);
                    Assert.Equal((ushort)0x8123, machine.Cpu.Regs.PC);
                    Assert.Equal((byte)0x5A, machine.PeekMemory(0x9000));
                    Assert.Equal(Path.GetFileName(tapePath), machine.MountedTapeName);
                    Assert.Equal(1, result.TotalBlockCount);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The tape replacement test timed out.");
            File.Delete(tapePath);
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }
}
