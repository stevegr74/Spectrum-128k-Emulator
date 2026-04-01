using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private readonly Bitmap screenBitmap = new Bitmap(256, 192, PixelFormat.Format32bppArgb);
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 20 }; // ~50 fps

        private readonly Z80Cpu cpu = new Z80Cpu();

        private byte[] ram = new byte[128 * 1024];           // 128KB RAM
        private byte[] rom = new byte[32 * 1024];            // 32KB total ROM (bank 0 + bank 1)
        private int currentRomBank = 0;                      // 0 or 1

        private readonly PictureBox screenBox = new PictureBox 
        { 
            Dock = DockStyle.Fill, 
            SizeMode = PictureBoxSizeMode.StretchImage 
        };

        public MainForm()
        {
            Text = "Spectrum 128K Emulator - Pure .NET";
            ClientSize = new Size(512, 384); // 2x scale
            Controls.Add(screenBox);

            // Initial border
            using (var g = Graphics.FromImage(screenBitmap))
                g.Clear(Color.FromArgb(0, 0, 192));

            screenBox.Image = screenBitmap;

            LoadRoms();

            // Hook CPU callbacks - use the proper methods
            cpu.ReadMemory = ReadMemory;
            cpu.WriteMemory = WriteMemory;
            cpu.WritePort = WritePort;        // ← This was missing!

            cpu.Reset();

            // Timer
            frameTimer.Tick += FrameTimer_Tick;
            frameTimer.Start();

            // Keyboard (todo)
            KeyDown += MainForm_KeyDown;
            KeyUp += MainForm_KeyUp;
        }

        private void LoadRoms()
        {
            try
            {
                byte[] rom0 = System.IO.File.ReadAllBytes("roms/128-0.rom");
                byte[] rom1 = System.IO.File.ReadAllBytes("roms/128-1.rom");

                if (rom0.Length != 16384 || rom1.Length != 16384)
                {
                    MessageBox.Show("ROM files must be exactly 16KB each!");
                    return;
                }

                Buffer.BlockCopy(rom0, 0, rom, 0, 16384);
                Buffer.BlockCopy(rom1, 0, rom, 16384, 16384);

                Console.WriteLine("✅ Both 128K ROMs loaded successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load ROMs:\n{ex.Message}\n\nMake sure the files are in a 'roms' folder.");
            }
        }

        // ====================== MEMORY ======================
        private byte ReadMemory(ushort addr)
        {
            if (addr < 0x8000) // ROM area (0x0000-0x7FFF)
            {
                int offset = currentRomBank * 16384 + addr;
                return rom[offset];
            }
            else
            {
                return ram[addr];   // for now flat; later we'll add full 128K banking
            }
        }

        private void WriteMemory(ushort addr, byte value)
        {
            if (addr >= 0x8000) // Protect ROM
            {
                ram[addr] = value;
            }
        }

        // ====================== PORTS ======================
        private void WritePort(ushort port, byte value)
        {
            if ((port & 0xFF) == 0xFD) // 0x7FFD paging port (and mirrors)
            {
                int oldBank = currentRomBank;
                currentRomBank = (value & 0x10) != 0 ? 1 : 0;   // Bit 4 = ROM select

                if (oldBank != currentRomBank)
                    Console.WriteLine($"[Paging] ROM bank changed to {currentRomBank} (value 0x{value:X2})");
            }
            else if ((port & 0xFF) == 0xFE)
            {
                // Border / beeper port (you can add border color later)
            }
        }

        // ====================== FRAME ======================
        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            cpu.ExecuteOneFrame(100000);   // Increased a bit — helps the ROM run further

            // Optional debug (remove or comment out later)
            // if (Environment.TickCount % 500 < 20)
            //     Console.WriteLine($"PC: 0x{cpu.Regs.PC:X4}  A: 0x{cpu.Regs.A:X2}");

            RenderSpectrumScreen();
            screenBox.Invalidate();
        }

        // ====================== RENDERING ======================
        private void RenderSpectrumScreen()
        {
            using var g = Graphics.FromImage(screenBitmap);
            g.Clear(Color.FromArgb(0, 0, 192)); // blue border

            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 256; x += 8)
                {
                    int charRow = y >> 3;
                    int charLine = y & 7;

                    ushort pixelAddr = (ushort)(
                        0x4000 +
                        ((charRow & 0x18) << 8) +
                        ((charRow & 0x07) << 5) +
                        (charLine << 8) +
                        (x >> 3)
                    );

                    byte pixelByte = ReadMemory(pixelAddr);

                    ushort attrAddr = (ushort)(0x5800 + (charRow * 32) + (x >> 3));
                    byte attr = ReadMemory(attrAddr);

                    bool bright = (attr & 0x40) != 0;
                    int ink = attr & 0x07;
                    int paper = (attr >> 3) & 0x07;

                    Color inkColor = GetSpectrumColor(ink, bright);
                    Color paperColor = GetSpectrumColor(paper, bright);

                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool on = (pixelByte & (0x80 >> bit)) != 0;
                        screenBitmap.SetPixel(x + bit, y, on ? inkColor : paperColor);
                    }
                }
            }
        }

        private Color GetSpectrumColor(int color, bool bright)
        {
            int intensity = bright ? 255 : 192;
            return color switch
            {
                0 => Color.Black,
                1 => Color.FromArgb(0, 0, intensity),
                2 => Color.FromArgb(0, intensity, 0),
                3 => Color.FromArgb(0, intensity, intensity),
                4 => Color.FromArgb(intensity, 0, 0),
                5 => Color.FromArgb(intensity, 0, intensity),
                6 => Color.FromArgb(intensity, intensity, 0),
                7 => Color.FromArgb(intensity, intensity, intensity),
                _ => Color.Black
            };
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e) { }
        private void MainForm_KeyUp(object? sender, KeyEventArgs e) { }
    }
}
