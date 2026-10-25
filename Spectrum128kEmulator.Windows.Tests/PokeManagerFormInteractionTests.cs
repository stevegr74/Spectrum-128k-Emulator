using System.Drawing;
using System.Windows.Forms;
using Spectrum128kEmulator.Pokes;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class PokeManagerFormInteractionTests
    {
        private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        [Fact]
        public void Form_AppliesMatchedLibraryAndOneOffWritesAndRestoresLast()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    PokeLibrary library = PokeLibrary.Parse(
                        $$"""
                        {
                          "version": 1,
                          "games": [{
                            "id": "example",
                            "title": "Example Game",
                            "aliases": ["Example"],
                            "models": ["128K"],
                            "sha256": ["{{Hash}}"],
                            "pokes": [{
                              "id": "lives",
                              "name": "Infinite lives",
                              "description": "Test poke",
                              "writes": [{ "space": "mapped", "address": "8000H", "value": "2AH" }]
                            }]
                          }]
                        }
                        """);
                    var memory = new Dictionary<string, byte> { ["Mapped 8000H"] = 0x11, ["Mapped 9000H"] = 0x22 };
                    var applications = new Stack<PokeApplication>();
                    IReadOnlyList<PokeWritePreview> Preview(IReadOnlyList<PokeWrite> writes) =>
                        writes.Select(write => new PokeWritePreview(write, memory.GetValueOrDefault(write.Target))).ToArray();
                    PokeApplication Apply(IReadOnlyList<PokeWrite> writes)
                    {
                        AppliedPokeWrite[] applied = writes
                            .Select(write => new AppliedPokeWrite(write, memory.GetValueOrDefault(write.Target)))
                            .ToArray();
                        foreach (PokeWrite write in writes)
                            memory[write.Target] = write.Value;
                        var application = new PokeApplication(DateTimeOffset.UtcNow, applied);
                        applications.Push(application);
                        return application;
                    }
                    (bool Success, string Message) Restore()
                    {
                        PokeApplication application = applications.Pop();
                        foreach (AppliedPokeWrite write in application.Writes)
                            memory[write.Write.Target] = write.OriginalValue;
                        return (true, "Restored.");
                    }

                    using var form = new PokeManagerForm(
                        library,
                        new PokeMediaIdentity("Example Game.tzx", Hash),
                        SpectrumMachineModel.Spectrum128K,
                        Preview,
                        Apply,
                        () => applications.Count > 0,
                        Restore,
                        (_, _) => true);
                    form.Show();
                    Application.DoEvents();

                    ComboBox games = Control<ComboBox>(form, "PokeGameSelector");
                    CheckedListBox pokes = Control<CheckedListBox>(form, "PokeList");
                    Label match = Control<Label>(form, "PokeMatchLabel");
                    ListView preview = Control<ListView>(form, "PokeLibraryPreview");
                    Button applyLibrary = Control<Button>(form, "ApplyLibraryPokes");
                    Button restore = Control<Button>(form, "RestoreLastPoke");
                    Assert.Single(games.Items);
                    Assert.StartsWith("[MATCH]", games.SelectedItem!.ToString());
                    Assert.Contains("EXACT MEDIA MATCH", match.Text);
                    Assert.Single(pokes.Items);
                    Assert.Single(preview.Items);
                    Assert.Equal("11H", preview.Items[0].SubItems[1].Text);
                    Assert.Equal("2AH", preview.Items[0].SubItems[2].Text);

                    pokes.SetItemChecked(0, true);
                    Application.DoEvents();
                    applyLibrary.PerformClick();
                    Application.DoEvents();
                    Assert.Equal(0x2A, memory["Mapped 8000H"]);
                    Assert.True(restore.Enabled);
                    restore.PerformClick();
                    Application.DoEvents();
                    Assert.Equal(0x11, memory["Mapped 8000H"]);

                    TabControl tabs = Control<TabControl>(form, "PokeTabs");
                    tabs.SelectedIndex = 1;
                    Control<TextBox>(form, "PokeAddressText").Text = "9000H";
                    Control<TextBox>(form, "PokeValueText").Text = "44H";
                    Control<Button>(form, "AddPendingPoke").PerformClick();
                    Application.DoEvents();
                    ListView pending = Control<ListView>(form, "PokePendingList");
                    Assert.Single(pending.Items);
                    Assert.Equal("22H", pending.Items[0].SubItems[1].Text);
                    Assert.Equal("44H", pending.Items[0].SubItems[2].Text);
                    Control<Button>(form, "ApplyPendingPokes").PerformClick();
                    Application.DoEvents();
                    Assert.Equal(0x44, memory["Mapped 9000H"]);
                    Assert.Empty(pending.Items);

                    Rectangle client = form.RectangleToScreen(form.ClientRectangle);
                    foreach (Control control in Descendants(form).Where(control => control.Visible && control.Width > 0 && control.Height > 0))
                        Assert.True(client.IntersectsWith(control.RectangleToScreen(control.ClientRectangle)));
                    form.Close();
                    Application.DoEvents();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The Poke Manager interaction test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        [Fact]
        public void Form_KeepsOneOffWorkflowAvailableWithEmptyLibrary()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new PokeManagerForm(
                        PokeLibrary.Empty,
                        null,
                        SpectrumMachineModel.Spectrum48K,
                        writes => writes.Select(write => new PokeWritePreview(write, 0)).ToArray(),
                        writes => new PokeApplication(DateTimeOffset.UtcNow, writes.Select(write => new AppliedPokeWrite(write, 0)).ToArray()),
                        () => false,
                        () => (false, "Nothing to restore."),
                        (_, _) => true);
                    form.Show();
                    Application.DoEvents();

                    Assert.False(Control<ComboBox>(form, "PokeGameSelector").Enabled);
                    Assert.False(Control<Button>(form, "ApplyLibraryPokes").Enabled);
                    Assert.False(Control<Button>(form, "ApplyPendingPokes").Enabled);
                    Assert.True(Control<TabControl>(form, "PokeTabs").TabPages[1].Enabled);
                    form.Close();
                    Application.DoEvents();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The empty-library Poke Manager test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        private static T Control<T>(Control root, string name) where T : Control =>
            Descendants(root).OfType<T>().Single(control => control.Name == name);

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
