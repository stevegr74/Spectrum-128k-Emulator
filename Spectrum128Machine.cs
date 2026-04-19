using System;
using System.Collections.Generic;
using System.IO;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed class Spectrum128Machine
    {
        public const int FrameTStates128 = 70908;
        public const int CpuClockHz = 3546900;
        public const int ScreenWidth = 256;
        public const int ScreenHeight = 192;

        private readonly Z80Cpu cpu = new Z80Cpu();
        private readonly byte[][] ramBanks = new byte[8][];
        private readonly byte[][] romBanks = new byte[2][];
        private readonly byte[] keyboardMatrix = new byte[8]
        {
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        };
        
        // AY-3-8912
        private readonly Audio.Ay8912 ay = new Audio.Ay8912();

        public Audio.Ay8912 Ay => ay;

        private byte lastAyRegister;
        private bool speakerHigh;
        private bool frameStartSpeakerHigh;
        private ulong frameStartTStates;
        private readonly List<Audio.BeeperEvent> beeperEvents = new List<Audio.BeeperEvent>();

        public bool SpeakerHigh => speakerHigh;
        public bool SpeakerEdge { get; private set; }

        private void HandleAyPortWrite(ushort port, byte value)
        {
            // 128K AY ports:
            // 0xFFFD selects AY register, 0xBFFD writes the selected register.
            if ((port & 0xC002) == 0xC000)
            {
                lastAyRegister = (byte)(value & 0x0F);
                ay.SelectRegister(lastAyRegister);
                return;
            }

            if ((port & 0xC002) == 0x8000)
            {
                ay.WriteRegister(value);
            }
        }

        private byte last7ffdValue = 0xFF;
        private MountedTape? mountedTape;
        public MountedTape? MountedTape => mountedTape;

        public Spectrum128Machine(string romFolder)
        {
            if (string.IsNullOrWhiteSpace(romFolder))
                throw new ArgumentException("ROM folder must be provided.", nameof(romFolder));

            for (int i = 0; i < ramBanks.Length; i++)
                ramBanks[i] = new byte[16384];

            for (int i = 0; i < romBanks.Length; i++)
                romBanks[i] = new byte[16384];

            LoadRoms(romFolder);
            InitializeScreenRam();
            ClearKeyboard();

            cpu.ReadMemory = ReadMemory;
            cpu.WriteMemory = WriteMemory;
            cpu.ReadPort = ReadPort;
            cpu.WritePort = WritePort;
            cpu.BeforeInstruction = HandleBeforeInstruction;
            cpu.Reset();
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            beeperEvents.Clear();
        }

        public Z80Cpu Cpu => cpu;

        public Action<string>? Trace
        {
            get => cpu.Trace;
            set => cpu.Trace = value;
        }

        public int PagedRamBank { get; private set; }
        public int CurrentRomBank { get; private set; }
        public bool PagingLocked { get; private set; }
        public int ScreenBank { get; private set; } = 5;
        public int BorderColor { get; private set; } = 1;
        public int FrameCount { get; private set; }
        public bool FlashPhase => ((FrameCount / 16) & 1) != 0;

        public Dictionary<ushort, int> ScreenWriteLog { get; } = new Dictionary<ushort, int>();
        public Dictionary<ushort, int> AboveScreenWriteLog { get; } = new Dictionary<ushort, int>();
        public int LastAboveWriteFrame { get; private set; } = -1;
        public bool HasMountedTape => mountedTape != null;
        public string? MountedTapeName => mountedTape?.DisplayName;

        public void Reset()
        {
            PagedRamBank = 0;
            CurrentRomBank = 0;
            PagingLocked = false;
            ScreenBank = 5;
            BorderColor = 1;
            FrameCount = 0;
            LastAboveWriteFrame = -1;
            last7ffdValue = 0xFF;
            mountedTape = null;
            speakerHigh = false;
            SpeakerEdge = false;

            ClearLogs();
            ClearKeyboard();
            ClearRam();
            InitializeScreenRam();
            cpu.Reset();
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
        }

        public void ExecuteFrame()
        {
            BeginFrameAudioCapture();
            TriggerFrameInterrupt();
            cpu.ExecuteCycles(FrameTStates128);
            FrameCount++;
        }

        public Audio.AudioFrame DrainAudioFrame()
        {
            return new Audio.AudioFrame(
                FrameTStates128,
                frameStartSpeakerHigh,
                speakerHigh,
                beeperEvents,
                ay.CaptureAudioState());
        }

        public void ClearLogs()
        {
            ScreenWriteLog.Clear();
            AboveScreenWriteLog.Clear();
            LastAboveWriteFrame = -1;
        }

        public void ClearKeyboard()
        {
            for (int i = 0; i < keyboardMatrix.Length; i++)
                keyboardMatrix[i] = 0xFF;
        }

        public void SetKey(int row, int bit, bool pressed)
        {
            if ((uint)row >= keyboardMatrix.Length)
                throw new ArgumentOutOfRangeException(nameof(row));
            if ((uint)bit >= 5)
                throw new ArgumentOutOfRangeException(nameof(bit));

            if (pressed)
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] & ~(1 << bit));
            else
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] | (1 << bit));
        }

        public byte[] GetKeyboardMatrixCopy() => (byte[])keyboardMatrix.Clone();

        public byte[] GetScreenBankData() => ramBanks[ScreenBank];

        public byte[] GetRamBankCopy(int bank)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));

            return (byte[])ramBanks[bank].Clone();
        }

        public byte PeekMemory(ushort addr) => ReadMemory(addr);

        public void PokeMemory(ushort addr, byte value) => WriteMemory(addr, value);

        public byte DebugReadPort(ushort port) => ReadPort(port);

        public void DebugWritePort(ushort port, byte value) => WritePort(port, value);

        public void MountTape(MountedTape tape)
        {
            mountedTape = tape ?? throw new ArgumentNullException(nameof(tape));
            mountedTape.Reset();
        }

        public void EjectTape()
        {
            mountedTape = null;
        }

        public bool TryServiceTapeTrap()
        {
            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, cpu);
        }

        private bool HandleBeforeInstruction(Z80Cpu z80)
        {
            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, z80);
        }

        private void LoadRoms(string romFolder)
        {
            string rom0Path = Path.Combine(romFolder, "128-0.rom");
            string rom1Path = Path.Combine(romFolder, "128-1.rom");

            byte[] rom0 = File.ReadAllBytes(rom0Path);
            byte[] rom1 = File.ReadAllBytes(rom1Path);

            if (rom0.Length != 16384 || rom1.Length != 16384)
                throw new InvalidOperationException("ROM files must be 16KB each.");

            romBanks[0] = rom0;
            romBanks[1] = rom1;
        }

        private void ClearRam()
        {
            for (int bank = 0; bank < ramBanks.Length; bank++)
                Array.Clear(ramBanks[bank], 0, ramBanks[bank].Length);
        }

        private void InitializeScreenRam()
        {
            byte[] screenRam = ramBanks[5];

            for (int i = 0; i < 0x1800; i++)
                screenRam[i] = 0;

            for (int i = 0x1800; i < 0x1B00; i++)
                screenRam[i] = 0x38;
        }

        private byte ReadMemory(ushort addr)
        {
            if (addr < 0x4000)
                return romBanks[CurrentRomBank][addr];

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => PagedRamBank
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
                _ => PagedRamBank
            };

            if (addr >= 0x4000 && addr < 0x5B00)
            {
                if (!ScreenWriteLog.ContainsKey(addr))
                    ScreenWriteLog[addr] = 0;
                ScreenWriteLog[addr]++;
            }
            else if (addr >= 0x5B00 && addr < 0x5C00)
            {
                if (!AboveScreenWriteLog.ContainsKey(addr))
                    AboveScreenWriteLog[addr] = 0;
                AboveScreenWriteLog[addr]++;
                LastAboveWriteFrame = FrameCount;
            }

            ramBanks[bank][addr & 0x3FFF] = value;
        }

        public byte ReadPort(ushort port)
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

                bool earHigh = mountedTape?.ReadEarBit() ?? true;
                if (earHigh)
                    result |= 0x40;
                else
                    result = (byte)(result & ~0x40);
                return result;
            }

            return 0xFF;
        }

        public void ConfigureFor48kSnapshot(int borderColor)
        {
            // Standard 48K layout inside the current 128K machine model.
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1; // Use the 48 BASIC ROM in your current setup.
            PagingLocked = true;
            BorderColor = borderColor & 0x07;
            FrameCount = 0;
        }

        public void Load48kSnapshotRam(byte[] ram48)
        {
            if (ram48 == null)
                throw new ArgumentNullException(nameof(ram48));
            if (ram48.Length != 48 * 1024)
                throw new ArgumentException("48K snapshot RAM must be exactly 49152 bytes.", nameof(ram48));

            // 0x4000-0x7FFF -> bank 5
            Buffer.BlockCopy(ram48, 0, ramBanks[5], 0, 0x4000);

            // 0x8000-0xBFFF -> bank 2
            Buffer.BlockCopy(ram48, 0x4000, ramBanks[2], 0, 0x4000);

            // 0xC000-0xFFFF -> bank 0 in 48K mode
            Buffer.BlockCopy(ram48, 0x8000, ramBanks[0], 0, 0x4000);
        }


        public void LoadRamBank(int bank, byte[] data)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length != 16 * 1024)
                throw new ArgumentException("RAM bank data must be exactly 16384 bytes.", nameof(data));

            Buffer.BlockCopy(data, 0, ramBanks[bank], 0, data.Length);
        }

        public void ConfigureFor128kSnapshot(byte last7ffdValue, int borderColor)
        {
            PagedRamBank = last7ffdValue & 0x07;
            ScreenBank = ((last7ffdValue & 0x08) != 0) ? 7 : 5;
            CurrentRomBank = ((last7ffdValue & 0x10) != 0) ? 1 : 0;
            PagingLocked = (last7ffdValue & 0x20) != 0;
            BorderColor = borderColor & 0x07;
            FrameCount = 0;
            this.last7ffdValue = (byte)(last7ffdValue & 0x3F);
        }

        private void WritePort(ushort port, byte value)
        {
            SpeakerEdge = false;

            if ((port & 0x0001) == 0)
            {
                BorderColor = value & 0x07;

                bool newSpeakerHigh = (value & 0x10) != 0;
                if (newSpeakerHigh != speakerHigh)
                {
                    speakerHigh = newSpeakerHigh;
                    SpeakerEdge = true;
                    RecordBeeperEvent(newSpeakerHigh);
                }

                HandleAyPortWrite(port, value);
                return;
            }

            HandleAyPortWrite(port, value);

            if ((port & 0x8002) == 0 && (port & 0x00FF) == 0xFD)
            {
                if (PagingLocked)
                    return;

                int oldRam = PagedRamBank;
                int oldScreen = ScreenBank;
                int oldRom = CurrentRomBank;

                PagedRamBank = value & 0x07;
                ScreenBank = ((value & 0x08) != 0) ? 7 : 5;
                CurrentRomBank = ((value & 0x10) != 0) ? 1 : 0;

                if ((value & 0x20) != 0)
                    PagingLocked = true;

                byte newPaging = (byte)(value & 0x3F);
                if (newPaging != last7ffdValue)
                {
                    last7ffdValue = newPaging;
                    Trace?.Invoke(
                        $"[7FFD] PC=0x{cpu.Regs.PC:X4} Frame={FrameCount} RAM {oldRam}->{PagedRamBank} SCREEN {oldScreen}->{ScreenBank} ROM {oldRom}->{CurrentRomBank} VAL=0x{value:X2}");
                }
            }
        }

        private void TriggerFrameInterrupt()
        {
            cpu.InterruptPending = true;
        }

        private void BeginFrameAudioCapture()
        {
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            beeperEvents.Clear();
        }

        private void RecordBeeperEvent(bool newSpeakerHigh)
        {
            ulong elapsed = cpu.TStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)FrameTStates128, elapsed);
            beeperEvents.Add(new Audio.BeeperEvent(offset, newSpeakerHigh));
        }
    }
}
