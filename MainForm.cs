using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
            SizeMode = PictureBoxSizeMode.StretchImage
        };

        public MainForm()
        {
            Text = "Spectrum 128K Emulator - Pure .NET";
            ClientSize = new Size(512, 384);
            Controls.Add(screenBox);

            for (int i = 0; i < 8; i++) ramBanks[i] = new byte[16384];
            for (int i = 0; i < 2; i++) romBanks[i] = new byte[16384];

            LoadRoms();
            
            // Initialize screen with default pattern (white paper, black ink, with border)
            InitializeScreenRam();

            cpu.ReadMemory = ReadMemory;
            cpu.WriteMemory = WriteMemory;
            cpu.ReadPort = ReadPort;
            cpu.WritePort = WritePort;
/*            cpu.Trace = s =>
            {
                if (frameCount < 50 || s.Contains("PC=0x00"))
                    Console.WriteLine(s);
            };*/
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
        
        private void InitializeScreenRam()
        {
            // Fill screen bank 5 with default attributes (white paper, black ink)
            byte[] screenRam = ramBanks[5];
            
            // Clear the pixel area with zeros
            for (int i = 0; i < 0x1800; i++)
                screenRam[i] = 0;
            
            // Set attributes (0x1800-0x1AFF): white paper (0x38 = bits: 0|0 bright|ink(0)|paper(7))
            // Ink=0 (black), Paper=7 (white)
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

        private Dictionary<ushort, int> screenWriteLog = new Dictionary<ushort, int>();
        private Dictionary<ushort, int> aboveScreenWriteLog = new Dictionary<ushort, int>();
        private int lastAboveWriteFrame = -1;

        private void WriteMemory(ushort addr, byte value)
        {
            if (addr < 0x4000) return;

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => pagedRamBank
            };

            // Track ALL writes to screen area (0x4000-0x5AFF) and above
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

                // Keep upper bits high for now
                result |= 0xE0;
                return result;
            }

            return 0xFF;
        }

        private void WritePort(ushort port, byte value)
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
        }

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            if (frameCount > 0)
                cpu.InterruptPending = true;
            
            cpu.ExecuteCycles(70908);
            frameCount++;

            if (frameCount == 80 || frameCount == 200 || frameCount == 300)
            {
                // DEBUG: Show which banks have data
                Console.WriteLine($"\n=== FRAME {frameCount} BANK CONTENT DEBUG ===");
                for (int b = 0; b < 8; b++)
                {
                    int pixelCount = 0;
                    int attrCount = 0;
                    for (int i = 0; i < 0x1800; i++) if (ramBanks[b][i] != 0) pixelCount++;
                    for (int i = 0x1800; i < 0x1B00; i++) if (ramBanks[b][i] != 0x38) attrCount++;
                    if (pixelCount > 0 || attrCount > 0)
                    {
                        // Get variety of pixel values
                        var pixelvals = new System.Collections.Generic.Dictionary<byte, int>();
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
                        Console.WriteLine("NO writes to screen area (0x4000-0x5AFF)!");
                    
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

            // Debug: Check what's in each bank
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
            
            // Debug: log what we're rendering
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
    }
}