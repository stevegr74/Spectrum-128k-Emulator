using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblerFormInteractionTests
    {
        [Fact]
        public void Form_ShowsDataReferencesAndInspectsFrozenMemory()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(folder);
                    DisassemblySnapshot snapshot = CreateSnapshotWithCpu();
                    using var form = new DisassemblerForm(
                        () => snapshot,
                        new DisassemblerWindowSettingsStore(Path.Combine(folder, "disassembler.json")));
                    form.Show();
                    Application.DoEvents();

                    Label summary = Descendants(form).OfType<Label>()
                        .Single(label => label.Name == "MemoryMapSummary");
                    Label screenBank = Descendants(form).OfType<Label>()
                        .Single(label => label.Name == "MemoryMapScreenBank");
                    Label compactMap = Descendants(form).OfType<Label>()
                        .Single(label => label.Name == "CompactMemoryMap");
                    Panel memoryMap = Descendants(form).OfType<Panel>()
                        .Single(panel => panel.Name == "MemoryMapPanel");
                    Label[] memoryBlocks = Descendants(form).OfType<Label>()
                        .Where(label => label.Name.StartsWith("MemoryMapBlock", StringComparison.Ordinal))
                        .OrderBy(label => label.Name)
                        .ToArray();
                    Assert.Equal(4, memoryBlocks.Length);
                    Assert.True(memoryBlocks.All(block => block.Visible));
                    Assert.InRange(memoryBlocks.Max(block => block.Width) - memoryBlocks.Min(block => block.Width), 0, 2);
                    Assert.Contains("LISTING 8000H | PC 8000H | 128K", summary.Text);
                    Assert.Equal(["0000 ROM 1", "4000 RAM 5", "8000 RAM 2", "C000 RAM 7"], memoryBlocks.Select(block => block.Text));
                    Assert.Equal(Color.FromArgb(214, 237, 229), memoryBlocks[1].BackColor);
                    Assert.Equal(Color.FromArgb(95, 225, 195), memoryBlocks[2].BackColor);
                    Assert.Equal("DISPLAY RAM 5", screenBank.Text);
                    Assert.False(compactMap.Visible);

                    NavigateTo(form, "8020");
                    ListView listing = Descendants(form).OfType<ListView>().Single();
                    ListBox references = Descendants(form).OfType<ListBox>()
                        .Single(list => list.Name == "CrossReferenceList");
                    Assert.Equal("LD A,(9000H)", listing.Items[0].SubItems[4].Text);
                    Assert.Equal("R 9000", listing.Items[0].SubItems[5].Text);
                    Assert.Contains("OUT READ8   MAP:9000", references.Items[0]!.ToString());

                    typeof(DisassemblerForm)
                        .GetMethod("InspectSelectedInstructionTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, null);
                    Application.DoEvents();

                    TabControl tabs = Descendants(form).OfType<TabControl>().Single();
                    ListBox memory = Descendants(form).OfType<ListBox>()
                        .Single(list => list.Name == "MemoryInspectorList");
                    ComboBox sources = Descendants(form).OfType<ComboBox>()
                        .Single(combo => combo.Name == "MemorySourceComboBox");
                    Assert.Equal("Memory", tabs.SelectedTab!.Text);
                    Assert.Contains("9000", memory.SelectedItem!.ToString());
                    Assert.Contains("[5A]", memory.SelectedItem!.ToString());
                    Assert.Contains(sources.Items.Cast<object>(), item => item.ToString()!.StartsWith("SP "));

                    sources.SelectedIndex = 1;
                    Application.DoEvents();
                    Assert.Contains("A000", memory.SelectedItem!.ToString());

                    NavigateTo(form, "8020");
                    typeof(DisassemblerForm)
                        .GetMethod("NavigateToSelectedTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, null);
                    Application.DoEvents();
                    Assert.Equal("9000", listing.Items[0].Text);
                    Assert.Contains("IN  READ8   MAP:8020", references.Items[0]!.ToString());

                    memoryMap.Width = 380;
                    Application.DoEvents();
                    Assert.True(compactMap.Visible);
                    Assert.False(summary.Visible);
                    Assert.Contains("CAPTURED PC 8000H", compactMap.Text);

                    form.Close();
                    Application.DoEvents();
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
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The WinForms memory-inspector test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

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
                    DisassemblySnapshot snapshot = CreateSnapshot("game-a");
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
                        .Single(button => button.Name == "ListingGoButton");
                    TextBox address = Descendants(form).OfType<TextBox>()
                        .Single(textBox => textBox.Name == "ListingAddressTextBox");
                    ToolStripMenuItem classificationMenu = listing.ContextMenuStrip!.Items
                        .OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == "Classification");

                    Assert.Equal(
                        ["Mark Selected As Code", "Mark Selected As Data", "Clear Selected Marking"],
                        classificationMenu.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text));

                    Rectangle formBounds = form.RectangleToScreen(form.ClientRectangle);
                    Rectangle selectorBounds = memoryView.RectangleToScreen(memoryView.ClientRectangle);
                    Assert.True(memoryView.Visible);
                    Assert.True(formBounds.Contains(selectorBounds));
                    Assert.Equal(DisassemblyAddressSpace.Mapped, memoryView.SelectedItem);

                    memoryView.SelectedItem = DisassemblyAddressSpace.Ram(7);
                    Application.DoEvents();
                    Assert.StartsWith("RAM7:", listing.Items[0].Text);
                    Assert.False(runToCursor.Enabled);
                    SetClassification(form, DisassemblyClassification.Data);
                    Application.DoEvents();
                    Assert.Equal("DATA", listing.Items[0].SubItems[1].Text);
                    Assert.Equal("DB 00H", listing.Items[0].SubItems[4].Text);
                    Assert.Equal(Color.FromArgb(255, 244, 204), listing.Items[0].BackColor);
                    SetClassification(form, DisassemblyClassification.Code);
                    Application.DoEvents();
                    Assert.Equal("CODE", listing.Items[0].SubItems[1].Text);
                    Assert.Equal("NOP", listing.Items[0].SubItems[4].Text);
                    Assert.Equal(Color.FromArgb(225, 239, 247), listing.Items[0].BackColor);
                    SetClassification(form, DisassemblyClassification.Data);
                    Application.DoEvents();

                    memoryView.SelectedItem = DisassemblyAddressSpace.Mapped;
                    Application.DoEvents();
                    Assert.Equal("8000", listing.Items[0].Text);
                    Assert.Equal(string.Empty, listing.Items[0].SubItems[1].Text);
                    Assert.True(runToCursor.Enabled);
                    Assert.Contains("OUT CALL MAP:8010", crossReferences.Items[0]!.ToString());

                    SetClassification(form, DisassemblyClassification.Data);
                    Application.DoEvents();
                    Assert.Equal("DATA", listing.Items[0].SubItems[1].Text);
                    Assert.Equal("DB CDH", listing.Items[0].SubItems[4].Text);
                    Assert.Equal("No direct references", crossReferences.Items[0]!.ToString());
                    Assert.Equal((byte)0xCD, snapshot.ReadMemory(DisassemblyAddressSpace.Mapped, 0x8000));

                    SetClassification(form, DisassemblyClassification.Code);
                    Application.DoEvents();
                    Assert.Equal("CODE", listing.Items[0].SubItems[1].Text);
                    Assert.Equal("CALL 8010H", listing.Items[0].SubItems[4].Text);
                    Assert.Contains("OUT CALL MAP:8010", crossReferences.Items[0]!.ToString());
                    Assert.Equal((byte)0xCD, snapshot.ReadMemory(DisassemblyAddressSpace.Mapped, 0x8000));

                    SetClassification(form, DisassemblyClassification.Unknown);
                    Application.DoEvents();
                    Assert.Equal(string.Empty, listing.Items[0].SubItems[1].Text);
                    Assert.Equal("CALL 8010H", listing.Items[0].SubItems[4].Text);

                    SetClassification(form, DisassemblyClassification.Code);
                    Application.DoEvents();

                    listing.Items[0].Selected = true;
                    listing.Items[0].Focused = true;
                    listing.Focus();
                    typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(listing, [EventArgs.Empty]);
                    Application.DoEvents();

                    Assert.Equal("8010", listing.Items[0].Text);
                    Assert.Equal("SUB_8010", listing.Items[0].SubItems[2].Text);
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
                    Assert.Equal(2, saved.Classifications.Count);
                    DisassemblerClassificationSetting mappedClassification = Assert.Single(
                        saved.Classifications,
                        classification => classification.AddressSpaceKind == DisassemblyAddressSpaceKind.Mapped);
                    Assert.Equal(DisassemblyClassification.Code, mappedClassification.Classification);
                    Assert.Equal((ushort)0x8000, mappedClassification.StartAddress);
                    Assert.Equal((ushort)0x8002, mappedClassification.EndAddress);
                    DisassemblerClassificationSetting bankClassification = Assert.Single(
                        saved.Classifications,
                        classification => classification.AddressSpaceKind == DisassemblyAddressSpaceKind.Ram);
                    Assert.Equal(7, bankClassification.Bank);
                    Assert.Equal(DisassemblyClassification.Data, bankClassification.Classification);

                    DisassemblySnapshot otherSnapshot = CreateSnapshot("game-b");
                    using (var unrelated = new DisassemblerForm(() => otherSnapshot, store))
                    {
                        unrelated.Show();
                        Application.DoEvents();
                        ComboBox unrelatedMemoryView = Descendants(unrelated).OfType<ComboBox>()
                            .Single(combo => combo.Items.Count == 11);
                        ListView unrelatedListing = Descendants(unrelated).OfType<ListView>().Single();
                        unrelatedMemoryView.SelectedItem = DisassemblyAddressSpace.Mapped;
                        Application.DoEvents();
                        NavigateTo(unrelated, "8000");
                        Assert.Equal(string.Empty, unrelatedListing.Items[0].SubItems[1].Text);
                        unrelated.Close();
                        Application.DoEvents();
                    }

                    using var reopened = new DisassemblerForm(() => snapshot, store);
                    reopened.Show();
                    Application.DoEvents();
                    ComboBox reopenedMemoryView = Descendants(reopened).OfType<ComboBox>()
                        .Single(combo => combo.Items.Count == 11);
                    ListView reopenedListing = Descendants(reopened).OfType<ListView>().Single();
                    reopenedMemoryView.SelectedItem = DisassemblyAddressSpace.Mapped;
                    Application.DoEvents();
                    NavigateTo(reopened, "8000");
                    Assert.Equal("CODE", reopenedListing.Items[0].SubItems[1].Text);
                    reopenedMemoryView.SelectedItem = DisassemblyAddressSpace.Ram(7);
                    Application.DoEvents();
                    Assert.Equal("DATA", reopenedListing.Items[0].SubItems[1].Text);
                    reopened.Close();
                    Application.DoEvents();
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

        private static DisassemblySnapshot CreateSnapshot(string analysisContext)
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
                ramBanks: ramBanks,
                analysisContext: analysisContext);
        }

        private static DisassemblySnapshot CreateSnapshotWithCpu()
        {
            var memory = new byte[65536];
            memory[0x8000] = 0xC9;
            memory[0x8020] = 0x3A;
            memory[0x8021] = 0x00;
            memory[0x8022] = 0x90;
            memory[0x8023] = 0xC9;
            memory[0x9000] = 0x5A;
            memory[0xA000] = 0x41;
            var cpu = new DisassemblyCpuState(
                AF: 0x1200,
                BC: 0x8100,
                DE: 0x8200,
                HL: 0x8300,
                AlternateAF: 0,
                AlternateBC: 0,
                AlternateDE: 0,
                AlternateHL: 0,
                IX: 0x8400,
                IY: 0x8500,
                SP: 0xA000,
                PC: 0x8000,
                I: 0,
                R: 0,
                IFF1: true,
                IFF2: true,
                InterruptMode: 1,
                IsHalted: false,
                InterruptPending: false,
                TStates: 100,
                StackWords: [0x8020]);
            return new DisassemblySnapshot(
                memory,
                0x8000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5,
                cpuState: cpu,
                romBanks: Enumerable.Range(0, 2).Select(_ => new byte[0x4000]).ToArray(),
                ramBanks: Enumerable.Range(0, 8).Select(_ => new byte[0x4000]).ToArray(),
                analysisContext: "memory-inspector-test");
        }

        private static void SetClassification(DisassemblerForm form, DisassemblyClassification classification) =>
            typeof(DisassemblerForm)
                .GetMethod("SetClassificationAtSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, [classification]);

        private static void NavigateTo(DisassemblerForm form, string address)
        {
            TextBox addressBox = Descendants(form).OfType<TextBox>()
                .Single(textBox => textBox.Name == "ListingAddressTextBox");
            Button go = Descendants(form).OfType<Button>()
                .Single(button => button.Name == "ListingGoButton");
            addressBox.Text = address;
            go.PerformClick();
            Application.DoEvents();
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
