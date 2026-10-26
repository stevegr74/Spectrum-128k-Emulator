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
                            "F10 - Load .z80 Snapshot / Play .rzx Recording",
                            "F11 - Mount .tap or .tzx Tape Image",
                            "---",
                            "F12 - Write Machine Diagnostic Dump",
                            "---",
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
    }
}
