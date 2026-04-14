//#define EXTENDED_DEBUG
using System.Drawing.Imaging;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private readonly System.Diagnostics.Stopwatch frameClock = System.Diagnostics.Stopwatch.StartNew();
        private long nextFrameTicks;
        private readonly double ticksPerFrame = (double)System.Diagnostics.Stopwatch.Frequency / 50.0;
        private const int MaxCatchUpFramesPerTick = 3;
        private readonly Bitmap screenBitmap = new Bitmap(Spectrum128Machine.ScreenWidth, Spectrum128Machine.ScreenHeight, PixelFormat.Format32bppArgb);
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 5 };
        private readonly PictureBox screenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            TabStop = true
        };

        private readonly Spectrum128Machine machine;

        public MainForm()
        {
            Text = "Spectrum 128K Emulator";
            ClientSize = new Size(512, 384);
            Controls.Add(screenBox);

            string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            machine = new Spectrum128Machine(romFolder);
            machine.Trace = s =>
            {
                if (s.StartsWith("UNIMPL") || s.StartsWith("[7FFD]"))
                {
                    Console.WriteLine(s);
                    Console.Out.Flush();
                }
            };
            InitializeKeyboard();
            nextFrameTicks = frameClock.ElapsedTicks + (long)ticksPerFrame;
            frameTimer.Tick += FrameTimer_Tick;
            frameTimer.Start();

            Console.WriteLine("=== Emulator started - ROM loaded - CPU Reset ===");
        }

        private void InitializeKeyboard()
        {
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
            KeyUp += MainForm_KeyUp;
            Deactivate += MainForm_Deactivate;
            screenBox.MouseClick += (_, _) => screenBox.Focus();
            Shown += (_, _) => screenBox.Focus();
        }

        private void MainForm_Deactivate(object? sender, EventArgs e)
        {
            machine.ClearKeyboard();
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            HandleKey(e.KeyCode, true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void MainForm_KeyUp(object? sender, KeyEventArgs e)
        {
            HandleKey(e.KeyCode, false);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void HandleKey(Keys key, bool pressed)
        {
            switch (key)
            {
                case Keys.Left:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(3, 4, pressed);
                    break;
                case Keys.Down:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 4, pressed);
                    break;
                case Keys.Up:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 3, pressed);
                    break;
                case Keys.Right:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 2, pressed);
                    break;
                case Keys.Back:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 0, pressed);
                    break;

                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    machine.SetKey(0, 0, pressed); break;
                case Keys.Z:
                    machine.SetKey(0, 1, pressed); break;
                case Keys.X:
                    machine.SetKey(0, 2, pressed); break;
                case Keys.C:
                    machine.SetKey(0, 3, pressed); break;
                case Keys.V:
                    machine.SetKey(0, 4, pressed); break;

                case Keys.A:
                    machine.SetKey(1, 0, pressed); break;
                case Keys.S:
                    machine.SetKey(1, 1, pressed); break;
                case Keys.D:
                    machine.SetKey(1, 2, pressed); break;
                case Keys.F:
                    machine.SetKey(1, 3, pressed); break;
                case Keys.G:
                    machine.SetKey(1, 4, pressed); break;

                case Keys.Q:
                    machine.SetKey(2, 0, pressed); break;
                case Keys.W:
                    machine.SetKey(2, 1, pressed); break;
                case Keys.E:
                    machine.SetKey(2, 2, pressed); break;
                case Keys.R:
                    machine.SetKey(2, 3, pressed); break;
                case Keys.T:
                    machine.SetKey(2, 4, pressed); break;

                case Keys.D1:
                case Keys.NumPad1:
                    machine.SetKey(3, 0, pressed); break;
                case Keys.D2:
                case Keys.NumPad2:
                    machine.SetKey(3, 1, pressed); break;
                case Keys.D3:
                case Keys.NumPad3:
                    machine.SetKey(3, 2, pressed); break;
                case Keys.D4:
                case Keys.NumPad4:
                    machine.SetKey(3, 3, pressed); break;
                case Keys.D5:
                case Keys.NumPad5:
                    machine.SetKey(3, 4, pressed); break;

                case Keys.D0:
                case Keys.NumPad0:
                    machine.SetKey(4, 0, pressed); break;
                case Keys.D9:
                case Keys.NumPad9:
                    machine.SetKey(4, 1, pressed); break;
                case Keys.D8:
                case Keys.NumPad8:
                    machine.SetKey(4, 2, pressed); break;
                case Keys.D7:
                case Keys.NumPad7:
                    machine.SetKey(4, 3, pressed); break;
                case Keys.D6:
                case Keys.NumPad6:
                    machine.SetKey(4, 4, pressed); break;

                case Keys.P:
                    machine.SetKey(5, 0, pressed); break;
                case Keys.O:
                    machine.SetKey(5, 1, pressed); break;
                case Keys.I:
                    machine.SetKey(5, 2, pressed); break;
                case Keys.U:
                    machine.SetKey(5, 3, pressed); break;
                case Keys.Y:
                    machine.SetKey(5, 4, pressed); break;

                case Keys.Enter:
                    machine.SetKey(6, 0, pressed); break;
                case Keys.L:
                    machine.SetKey(6, 1, pressed); break;
                case Keys.K:
                    machine.SetKey(6, 2, pressed); break;
                case Keys.J:
                    machine.SetKey(6, 3, pressed); break;
                case Keys.H:
                    machine.SetKey(6, 4, pressed); break;

                case Keys.Space:
                    machine.SetKey(7, 0, pressed); break;
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    machine.SetKey(7, 1, pressed); break;
                case Keys.M:
                    machine.SetKey(7, 2, pressed); break;
                case Keys.N:
                    machine.SetKey(7, 3, pressed); break;
                case Keys.B:
                    machine.SetKey(7, 4, pressed); break;
            }

            Console.WriteLine(
                $"KEYEVENT key={key} pressed={pressed} PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} MATRIX={string.Join(" ", machine.GetKeyboardMatrixCopy().Select(b => $"0x{b:X2}"))}");
            Console.Out.Flush();
        }

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            int executedFrames = 0;
            long now = frameClock.ElapsedTicks;

            while (now >= nextFrameTicks && executedFrames < MaxCatchUpFramesPerTick)
            {
                machine.ExecuteFrame();
                nextFrameTicks += (long)ticksPerFrame;
                executedFrames++;
                now = frameClock.ElapsedTicks;
            }

            // If we fell badly behind, resync gently instead of spiralling forever.
            if (executedFrames == MaxCatchUpFramesPerTick && now >= nextFrameTicks)
            {
                nextFrameTicks = now + (long)ticksPerFrame;
            }

            if (executedFrames > 0)
            {
                SpectrumRenderer.RenderToBitmap(
                    screenBitmap,
                    machine.GetScreenBankData(),
                    machine.BorderColor,
                    machine.FlashPhase);

                screenBox.Image = screenBitmap;
            }

#if EXTENDED_DEBUG
            if (machine.FrameCount % 20 == 0)
            {
                byte[] bank = machine.GetScreenBankData();
                int nonZeroPixels = 0;
                int nonZeroAttrs = 0;
                for (int i = 0; i < 0x1800; i++) if (bank[i] != 0) nonZeroPixels++;
                for (int i = 0x1800; i < 0x1B00; i++) if (bank[i] != 0x38) nonZeroAttrs++;

                int screenWrites = machine.ScreenWriteLog.Values.Sum();
                int aboveWrites = machine.AboveScreenWriteLog.Values.Sum();
                string writeNote = machine.LastAboveWriteFrame < machine.FrameCount - 20 ? " [WRITES STOPPED]" : string.Empty;

                Console.WriteLine(
                    $"Frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} Pixels={nonZeroPixels} Attrs={nonZeroAttrs} | ScreenAddr writes={screenWrites} AboveAddr writes={aboveWrites} LastWrite@Frame{machine.LastAboveWriteFrame}{writeNote}");
                Console.Out.Flush();
            }
#endif
        }        
    }
}
