using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblerFormInteractionTests
    {
        [Fact]
        public void Form_SwitchesBanksFollowsTargetsAndPersistsSymbols()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                string settingsPath = Path.Combine(folder, "disassembler.json");
                try
                {
                    Directory.CreateDirectory(folder);
                    DisassemblySnapshot snapshot = CreateSnapshot();
                    var store = new DisassemblerWindowSettingsStore(settingsPath);
                    using var form = new DisassemblerForm(() => snapshot, store);
                    form.Show();
                    Application.DoEvents();

                    ComboBox memoryView = Descendants(form).OfType<ComboBox>()
                        .Single(combo => combo.Items.Count == 11);
                    ListView listing = Descendants(form).OfType<ListView>().Single();
                    ListBox crossReferences = Descendants(form).OfType<ListBox>()
                        .Single(list => list.Name == "CrossReferenceList");
                    Button runToCursor = Descendants(form).OfType<Button>()
                        .Single(button => button.Text == "Run to Cursor");
                    Button go = Descendants(form).OfType<Button>()
                        .Single(button => button.Text == "Go");
                    TextBox address = Descendants(form).OfType<TextBox>()
                        .Single(textBox => textBox.MaxLength == 6);

                    Rectangle formBounds = form.RectangleToScreen(form.ClientRectangle);
                    Rectangle selectorBounds = memoryView.RectangleToScreen(memoryView.ClientRectangle);
                    Assert.True(memoryView.Visible);
                    Assert.True(formBounds.Contains(selectorBounds));
                    Assert.Equal(DisassemblyAddressSpace.Mapped, memoryView.SelectedItem);

                    memoryView.SelectedItem = DisassemblyAddressSpace.Ram(7);
                    Application.DoEvents();
                    Assert.StartsWith("RAM7:", listing.Items[0].Text);
                    Assert.False(runToCursor.Enabled);

                    memoryView.SelectedItem = DisassemblyAddressSpace.Mapped;
                    Application.DoEvents();
                    Assert.Equal("8000", listing.Items[0].Text);
                    Assert.True(runToCursor.Enabled);
                    Assert.Contains("OUT CALL MAP:8010", crossReferences.Items[0]!.ToString());

                    listing.Items[0].Selected = true;
                    listing.Items[0].Focused = true;
                    listing.Focus();
                    typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(listing, [EventArgs.Empty]);
                    Application.DoEvents();

                    Assert.Equal("8010", listing.Items[0].Text);
                    Assert.Equal("SUB_8010", listing.Items[0].SubItems[1].Text);
                    Assert.Contains("IN  CALL MAP:8000", crossReferences.Items[0]!.ToString());

                    crossReferences.SelectedIndex = 0;
                    typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(crossReferences, [EventArgs.Empty]);
                    Application.DoEvents();
                    Assert.Equal("8000", listing.Items[0].Text);

                    address.Text = "SUB_8010";
                    go.PerformClick();
                    Application.DoEvents();
                    Assert.Equal("8010", listing.Items[0].Text);

                    form.Close();
                    Application.DoEvents();

                    DisassemblerWindowSettings saved = store.Load();
                    DisassemblerSymbolSetting symbol = Assert.Single(saved.Symbols);
                    Assert.Equal("SUB_8010", symbol.Name);
                    Assert.True(symbol.IsAutomatic);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The WinForms interaction test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        private static DisassemblySnapshot CreateSnapshot()
        {
            var memory = new byte[65536];
            memory[0x8000] = 0xCD;
            memory[0x8001] = 0x10;
            memory[0x8002] = 0x80;
            byte[][] romBanks = Enumerable.Range(0, 2).Select(_ => new byte[0x4000]).ToArray();
            byte[][] ramBanks = Enumerable.Range(0, 8).Select(_ => new byte[0x4000]).ToArray();
            return new DisassemblySnapshot(
                memory,
                0x8000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5,
                romBanks: romBanks,
                ramBanks: ramBanks);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
