using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private readonly Bitmap screenBitmap = new Bitmap(256, 192, PixelFormat.Format32bppArgb);
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 20 };

        private readonly Z80Cpu cpu = new Z80Cpu();

        private readonly byte[][] ramBanks = new byte[8][];
        private readonly byte[][] romBanks = new byte[2][];

        private int pagedRamBank = 0;
        private int currentRomBank = 0;
        private bool pagingLocked = false;
        private int screenBank = 5;
        private int borderColor = 1;
        private byte last7ffdValue = 0xFF;

        private readonly byte[] keyboardMatrix = new byte[8]
        {
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        };

        private int frameCount = 0;

        private readonly PictureBox screenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            TabStop = true
        };

        private readonly Dictionary<ushort, int> screenWriteLog = new Dictionary<ushort, int>();
        private readonly Dictionary<ushort, int> aboveScreenWriteLog = new Dictionary<ushort, int>();
        private int lastAboveWriteFrame = -1;

        public MainForm()
        {
            Text = "Spectrum 128K Emulator - Pure .NET";
            ClientSize = new Size(512, 384);
            Controls.Add(screenBox);

            for (int i = 0; i < 8; i++) ramBanks[i] = new byte[16384];
            for (int i = 0; i < 2; i++) romBanks[i] = new byte[16384];

            LoadRoms();
            InitializeScreenRam();
            InitializeKeyboard();
            ClearKeyboard();

            cpu.ReadMemory = ReadMemory;
            cpu.WriteMemory = WriteMemory;
            cpu.ReadPort = ReadPort;
            cpu.WritePort = WritePort;

            cpu.Trace = s =>
            {
                if (s.StartsWith("UNIMPL"))
                    Console.WriteLine(s);
            };

            cpu.Reset();

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
            ClearKeyboard();
        }

        private void ClearKeyboard()
        {
            for (int i = 0; i < 8; i++)
                keyboardMatrix[i] = 0xFF;
        }

        private void SetKey(int row, int bit, bool pressed)
        {
            if (pressed)
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] & ~(1 << bit));
            else
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] | (1 << bit));

            Console.WriteLine($"KEY row={row} bit={bit} pressed={pressed} -> {string.Join(" ", keyboardMatrix.Select(b => $"0x{b:X2}"))}");
            Console.Out.Flush();
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
                // PC arrow keys -> Spectrum cursor combos
                case Keys.Left:
                    SetKey(0, 0, pressed); // CAPS SHIFT
                    SetKey(3, 4, pressed); // 5
                    break;

                case Keys.Down:
                    SetKey(0, 0, pressed); // CAPS SHIFT
                    SetKey(4, 4, pressed); // 6
                    break;

                case Keys.Up:
                    SetKey(0, 0, pressed); // CAPS SHIFT
                    SetKey(4, 3, pressed); // 7
                    break;

                case Keys.Right:
                    SetKey(0, 0, pressed); // CAPS SHIFT
                    SetKey(4, 2, pressed); // 8
                    break;

                case Keys.Back:
                    SetKey(0, 0, pressed); // CAPS SHIFT
                    SetKey(4, 0, pressed); // 0
                    break;
                // Row 0: CAPS SHIFT, Z, X, C, V
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    SetKey(0, 0, pressed); break;
                case Keys.Z:
                    SetKey(0, 1, pressed); break;
                case Keys.X:
                    SetKey(0, 2, pressed); break;
                case Keys.C:
                    SetKey(0, 3, pressed); break;
                case Keys.V:
                    SetKey(0, 4, pressed); break;

                // Row 1: A, S, D, F, G
                case Keys.A:
                    SetKey(1, 0, pressed); break;
                case Keys.S:
                    SetKey(1, 1, pressed); break;
                case Keys.D:
                    SetKey(1, 2, pressed); break;
                case Keys.F:
                    SetKey(1, 3, pressed); break;
                case Keys.G:
                    SetKey(1, 4, pressed); break;

                // Row 2: Q, W, E, R, T
                case Keys.Q:
                    SetKey(2, 0, pressed); break;
                case Keys.W:
                    SetKey(2, 1, pressed); break;
                case Keys.E:
                    SetKey(2, 2, pressed); break;
                case Keys.R:
                    SetKey(2, 3, pressed); break;
                case Keys.T:
                    SetKey(2, 4, pressed); break;

                // Row 3: 1, 2, 3, 4, 5
                case Keys.D1:
                case Keys.NumPad1:
                    SetKey(3, 0, pressed); break;
                case Keys.D2:
                case Keys.NumPad2:
                    SetKey(3, 1, pressed); break;
                case Keys.D3:
                case Keys.NumPad3:
                    SetKey(3, 2, pressed); break;
                case Keys.D4:
                case Keys.NumPad4:
                    SetKey(3, 3, pressed); break;
                case Keys.D5:
                case Keys.NumPad5:
                    SetKey(3, 4, pressed); break;

                // Row 4: 0, 9, 8, 7, 6
                case Keys.D0:
                case Keys.NumPad0:
                    SetKey(4, 0, pressed); break;
                case Keys.D9:
                case Keys.NumPad9:
                    SetKey(4, 1, pressed); break;
                case Keys.D8:
                case Keys.NumPad8:
                    SetKey(4, 2, pressed); break;
                case Keys.D7:
                case Keys.NumPad7:
                    SetKey(4, 3, pressed); break;
                case Keys.D6:
                case Keys.NumPad6:
                    SetKey(4, 4, pressed); break;

                // Row 5: P, O, I, U, Y
                case Keys.P:
                    SetKey(5, 0, pressed); break;
                case Keys.O:
                    SetKey(5, 1, pressed); break;
                case Keys.I:
                    SetKey(5, 2, pressed); break;
                case Keys.U:
                    SetKey(5, 3, pressed); break;
                case Keys.Y:
                    SetKey(5, 4, pressed); break;

                // Row 6: ENTER, L, K, J, H
                case Keys.Enter:
                    SetKey(6, 0, pressed); break;
                case Keys.L:
                    SetKey(6, 1, pressed); break;
                case Keys.K:
                    SetKey(6, 2, pressed); break;
                case Keys.J:
                    SetKey(6, 3, pressed); break;
                case Keys.H:
                    SetKey(6, 4, pressed); break;

                // Row 7: SPACE, SYMBOL SHIFT, M, N, B
                case Keys.Space:
                    SetKey(7, 0, pressed); break;
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    SetKey(7, 1, pressed); break;
                case Keys.M:
                    SetKey(7, 2, pressed); break;
                case Keys.N:
                    SetKey(7, 3, pressed); break;
                case Keys.B:
                    SetKey(7, 4, pressed); break;
            }
        }

        private void InitializeScreenRam()
        {
            byte[] screenRam = ramBanks[5];

            for (int i = 0; i < 0x1800; i++)
                screenRam[i] = 0;

            for (int i = 0x1800; i < 0x1B00; i++)
                screenRam[i] = 0x38;

            Console.WriteLine("Screen RAM initialized with white paper, black ink");
        }

        private void LoadRoms()
        {
            try
            {
                string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
                string rom0Path = Path.Combine(romFolder, "128-0.rom");
                string rom1Path = Path.Combine(romFolder, "128-1.rom");

                byte[] rom0 = File.ReadAllBytes(rom0Path);
                byte[] rom1 = File.ReadAllBytes(rom1Path);

                if (rom0.Length != 16384 || rom1.Length != 16384)
                    throw new Exception("ROM files must be 16KB each.");

                romBanks[0] = rom0;
                romBanks[1] = rom1;

                Console.WriteLine("✅ Both 128K ROMs loaded successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("ROM load failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private byte ReadMemory(ushort addr)
        {
            if (addr < 0x4000)
                return romBanks[currentRomBank][addr];

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => pagedRamBank
            };

            return ramBanks[bank][addr & 0x3FFF];
        }

        private void WriteMemory(ushort addr, byte value)
        {
            if (addr < 0x4000)
                return;

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => pagedRamBank
            };

            if (addr >= 0x4000 && addr < 0x5B00)
            {
                if (!screenWriteLog.ContainsKey(addr))
                    screenWriteLog[addr] = 0;
                screenWriteLog[addr]++;
            }
            else if (addr >= 0x5B00 && addr < 0x5C00)
            {
                if (!aboveScreenWriteLog.ContainsKey(addr))
                    aboveScreenWriteLog[addr] = 0;
                aboveScreenWriteLog[addr]++;
                lastAboveWriteFrame = frameCount;
            }

            ramBanks[bank][addr & 0x3FFF] = value;
        }

        private byte ReadPort(ushort port)
        {
            if ((port & 0x0001) == 0)
            {
                byte result = 0xFF;
                byte high = (byte)(port >> 8);

                for (int row = 0; row < 8; row++)
                {
                    if ((high & (1 << row)) == 0)
                        result &= keyboardMatrix[row];
                }

                // Bit 6 = EAR input, keep high for now.
                result |= 0x40;
                return result;
            }

            return 0xFF;
        }

        private void WritePort(ushort port, byte value)
        {
            // ULA / FE first
            if ((port & 0x0001) == 0)
            {
                borderColor = value & 0x07;
                return;
            }

            // 128K paging: only ports with A15=0, A1=0, and low byte FD
            if ((port & 0x8002) == 0 && (port & 0x00FF) == 0xFD)
            {
                if (pagingLocked)
                    return;

                pagedRamBank = value & 0x07;
                screenBank = ((value & 0x08) != 0) ? 7 : 5;
                currentRomBank = ((value & 0x10) != 0) ? 1 : 0;

                if ((value & 0x20) != 0)
                    pagingLocked = true;

                byte newPaging = (byte)(value & 0x3F);
                if (newPaging != last7ffdValue)
                {
                    last7ffdValue = newPaging;
                    Console.WriteLine($"[7FFD] PC=0x{cpu.Regs.PC:X4} Frame{frameCount} RAM={pagedRamBank} SCREEN={screenBank} ROM={currentRomBank}");
                    Console.Out.Flush();
                }

                return;
            }
        }

        /*private void WritePort(ushort port, byte value)
        {
            // 128K paging first
            if ((port & 0x8002) == 0)
            {
                if (!pagingLocked)
                {
                    pagedRamBank = value & 0x07;
                    screenBank = ((value & 0x08) != 0) ? 7 : 5;
                    currentRomBank = ((value & 0x10) != 0) ? 1 : 0;

                    if ((value & 0x20) != 0)
                        pagingLocked = true;

                    byte newPaging = (byte)(value & 0x3F);
                    if (newPaging != last7ffdValue)
                    {
                        last7ffdValue = newPaging;
                        Console.WriteLine(
                            $"[7FFD] PC=0x{cpu.Regs.PC:X4} Frame{frameCount} RAM={pagedRamBank} SCREEN={screenBank} ROM={currentRomBank}");
                        Console.Out.Flush();
                    }
                }

                return;
            }

            // ULA / FE family
            if ((port & 0x0001) == 0)
            {
                borderColor = value & 0x07;

                if (frameCount < 10)
                {
                    Console.WriteLine($"[FE] Border={borderColor}");
                    Console.Out.Flush();
                }

                return;
            }
        }*/

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            if (frameCount > 0)
                cpu.InterruptPending = true;

            cpu.ExecuteCycles(70908);
            frameCount++;

            if (frameCount == 80 || frameCount == 200 || frameCount == 300)
            {
                Console.WriteLine($"\n=== FRAME {frameCount} BANK CONTENT DEBUG ===");
                for (int b = 0; b < 8; b++)
                {
                    int pixelCount = 0;
                    int attrCount = 0;
                    for (int i = 0; i < 0x1800; i++) if (ramBanks[b][i] != 0) pixelCount++;
                    for (int i = 0x1800; i < 0x1B00; i++) if (ramBanks[b][i] != 0x38) attrCount++;
                    if (pixelCount > 0 || attrCount > 0)
                    {
                        var pixelvals = new Dictionary<byte, int>();
                        for (int i = 0; i < 0x1800; i++)
                        {
                            if (ramBanks[b][i] != 0)
                            {
                                if (!pixelvals.ContainsKey(ramBanks[b][i])) pixelvals[ramBanks[b][i]] = 0;
                                pixelvals[ramBanks[b][i]]++;
                            }
                        }
                        string pixelTypes = string.Join(";", pixelvals.OrderByDescending(x => x.Value).Take(3).Select(x => $"0x{x.Key:X2}({x.Value}x)"));
                        Console.WriteLine($"Bank {b}: Pixels={pixelCount} Attrs={attrCount} PixelTypes=[{pixelTypes}]");
                    }
                }
                Console.WriteLine("=== END DEBUG ===\n");
                Console.Out.Flush();
            }

            if (frameCount % 20 == 0)
            {
                int nonZeroPixels = 0, nonZeroAttrs = 0;
                byte[] bank = ramBanks[screenBank];

                for (int i = 0; i < 0x1800; i++) if (bank[i] != 0) nonZeroPixels++;
                for (int i = 0x1800; i < 0x1B00; i++) if (bank[i] != 0x38) nonZeroAttrs++;

                int screenWrites = screenWriteLog.Values.Sum();
                int aboveWrites = aboveScreenWriteLog.Values.Sum();
                string writeNote = lastAboveWriteFrame < frameCount - 20 ? " [WRITES STOPPED]" : "";
                Console.WriteLine($"Frame {frameCount}: PC=0x{cpu.Regs.PC:X4} SP=0x{cpu.Regs.SP:X4} IFF1={cpu.IFF1} Pixels={nonZeroPixels} Attrs={nonZeroAttrs} | ScreenAddr writes={screenWrites} AboveAddr writes={aboveWrites} LastWrite@Frame{lastAboveWriteFrame}{writeNote}");
                Console.Out.Flush();

                if (frameCount == 100)
                {
                    if (screenWriteLog.Count > 0)
                    {
                        Console.WriteLine("Screen area writes (0x4000-0x5AFF):");
                        foreach (var kvp in screenWriteLog.OrderBy(x => x.Key).Take(20))
                            Console.WriteLine($"  0x{kvp.Key:X4}: {kvp.Value} times");
                    }
                    else
                    {
                        Console.WriteLine("NO writes to screen area (0x4000-0x5AFF)!");
                    }

                    if (aboveScreenWriteLog.Count > 0)
                    {
                        Console.WriteLine("Writes above screen (0x5B00-0x5C00):");
                        foreach (var kvp in aboveScreenWriteLog.OrderBy(x => x.Key))
                            Console.WriteLine($"  0x{kvp.Key:X4}: {kvp.Value} times");
                    }
                    Console.Out.Flush();
                }
            }

            if (cpu.Regs.PC == 0x00E5 || cpu.Regs.PC == 0x1C7D || cpu.Regs.PC == 0x5B10 || cpu.Regs.PC == 0x02A1)
            {
                Console.WriteLine($"TRACE Frame {frameCount}: PC=0x{cpu.Regs.PC:X4} SP=0x{cpu.Regs.SP:X4} IFF1={cpu.IFF1}");
            }

            RenderSpectrumScreen();
            screenBox.Image = screenBitmap;
        }

        private void RenderSpectrumScreen()
        {
            using var g = Graphics.FromImage(screenBitmap);
            g.Clear(GetSpectrumColor(borderColor, false));

            if (frameCount == 80)
            {
                Console.WriteLine($"\n[BANK DIAGNOSTICS Frame 80] Current screenBank={screenBank}");
                for (int b = 0; b < 8; b++)
                {
                    int nonZeroPixels = 0;
                    for (int i = 0; i < 0x1800; i++) if (ramBanks[b][i] != 0) nonZeroPixels++;
                    Console.WriteLine($"  Bank {b}: {nonZeroPixels} non-zero pixels, Attrs at 0x1800: {string.Join(" ", ramBanks[b].Skip(0x1800).Take(4).Select(x => $"0x{x:X2}"))}");
                }
                Console.Out.Flush();
            }

            byte[] screenRam = ramBanks[screenBank];

            if (frameCount == 80)
            {
                int nonZeroPixels = 0;
                for (int i = 0; i < 0x1800; i++) if (screenRam[i] != 0) nonZeroPixels++;
                Console.WriteLine($"[RENDER DEBUG Frame 80] Using Bank={screenBank} NonZeroPixels={nonZeroPixels} First 16 pixels: {string.Join(" ", screenRam.Take(16).Select(b => $"0x{b:X2}"))}");
                Console.WriteLine($"[RENDER DEBUG Frame 80] Attributes at 0x1800-0x181F: {string.Join(" ", screenRam.Skip(0x1800).Take(16).Select(b => $"0x{b:X2}"))}");
                Console.Out.Flush();
            }

            for (int y = 0; y < 192; y++)
            {
                int charRow = y >> 3;
                int charLine = y & 7;

                for (int x = 0; x < 256; x += 8)
                {
                    int column = x >> 3;

                    int pixelOffset = ((charRow & 0x18) << 8) |
                                      ((charRow & 0x07) << 5) |
                                      (charLine << 8) |
                                      column;

                    int attrOffset = 0x1800 + (charRow * 32) + column;

                    byte pixelByte = screenRam[pixelOffset];
                    byte attr = screenRam[attrOffset];

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
                1 => Color.FromArgb(0, 0, intensity),                 // Blue
                2 => Color.FromArgb(intensity, 0, 0),                 // Red
                3 => Color.FromArgb(intensity, 0, intensity),         // Magenta
                4 => Color.FromArgb(0, intensity, 0),                 // Green
                5 => Color.FromArgb(0, intensity, intensity),         // Cyan
                6 => Color.FromArgb(intensity, intensity, 0),         // Yellow
                7 => Color.FromArgb(intensity, intensity, intensity), // White
                _ => Color.Black
            };
        }
    }
}