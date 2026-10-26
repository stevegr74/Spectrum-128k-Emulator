using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class EmulatorHelpFormInteractionTests
    {
        [Fact]
        public void Form_ShowsEverySectionWithoutClippingShortcutText()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new EmulatorHelpForm();
                    form.Show();
                    Application.DoEvents();

                    Assert.Equal(Color.FromArgb(246, 246, 242), form.BackColor);
                    Assert.Equal("Segoe UI", form.Font.Name);
                    Assert.True(form.ClientSize.Width >= 930);

                    ListBox sections = Descendants(form).OfType<ListBox>().Single();
                    Assert.Equal(5, sections.Items.Count);
                    GroupBox details = Descendants(form).OfType<GroupBox>()
                        .Single(group => group.Text != "Sections");

                    for (int index = 0; index < sections.Items.Count; index++)
                    {
                        sections.SelectedIndex = index;
                        Application.DoEvents();
                        foreach (Label label in Descendants(details).OfType<Label>())
                        {
                            int textWidth = TextRenderer.MeasureText(label.Text, label.Font).Width;
                            Assert.True(
                                textWidth <= label.ClientSize.Width - label.Padding.Horizontal,
                                $"Help text is clipped in '{details.Text}': {label.Text}");
                        }
                    }

                    Button close = Descendants(form).OfType<Button>().Single(button => button.Text == "Close");
                    Assert.Equal(Color.FromArgb(8, 121, 101), close.BackColor);
                    form.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The Help form interaction test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

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
