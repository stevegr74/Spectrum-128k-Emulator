using System;
using System.Security.Cryptography;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumRomProfile
    {
        private const string BundledRom0Sha256 = "3BA308F23B9471D13D9BA30C23030059A9CE5D4B317B85B86274B132651D1425";
        private const string BundledRom1Sha256 = "8D93C3342321E9D1E51D60AFCD7D15F6A7AFD978C231B43435A7C0757C60B9A3";

        private SpectrumRomProfile(string name, bool supportsAddressBasedTapeServices)
        {
            Name = name;
            SupportsAddressBasedTapeServices = supportsAddressBasedTapeServices;
        }

        public string Name { get; }
        public bool SupportsAddressBasedTapeServices { get; }

        internal static SpectrumRomProfile SyntheticTest { get; } =
            new SpectrumRomProfile("Synthetic test ROMs", supportsAddressBasedTapeServices: true);

        internal ushort TapeReturnAddress => 0x053F;
        internal ushort LoadBytesTrapAddress => 0x056B;
        internal ushort LoadBytesSyncLoopAddress => 0x0574;
        internal ushort UsrReturnAddress => 0x2D2B;
        internal ushort EndCalcLiteralAddress => 0x2758;
        internal ushort BasicResumeExecutionLoopAddress => 0x1555;
        internal ushort TapeAutoStartExecutionLoopAddress => 0x12A2;
        internal ushort MainExecutionReportAddress => 0x1303;
        internal ushort BasicLineNewAddress => 0x1B9E;
        internal ushort KeyboardInputLoopAddress => 0x15E7;

        internal bool IsMountedLoadReturnAddress(ushort address)
        {
            return IsInRange(address, 0x15F7, 0x15FA) ||
                   IsInRange(address, 0x0E51, 0x0E5C) ||
                   IsInRange(address, 0x15FD, 0x1600) ||
                   IsInRange(address, 0x10A8, 0x10B4) ||
                   address == 0x5E87 ||
                   IsInRange(address, 0x1615, 0x16E4) ||
                   IsInRange(address, 0x3631, 0x3634);
        }

        internal static SpectrumRomProfile Detect(byte[] rom0, byte[] rom1)
        {
            ArgumentNullException.ThrowIfNull(rom0);
            ArgumentNullException.ThrowIfNull(rom1);

            // Blank ROMs are inert fixtures used throughout the unit suite. They opt
            // into the bundled layout without pretending that an arbitrary ROM does.
            if (IsZeroFilled(rom0) && IsZeroFilled(rom1))
                return SyntheticTest;

            string rom0Hash = Convert.ToHexString(SHA256.HashData(rom0));
            string rom1Hash = Convert.ToHexString(SHA256.HashData(rom1));
            if (rom0Hash == BundledRom0Sha256 && rom1Hash == BundledRom1Sha256)
                return new SpectrumRomProfile("Sinclair ZX Spectrum 128K", supportsAddressBasedTapeServices: true);

            return new SpectrumRomProfile("Unknown 128K ROM pair", supportsAddressBasedTapeServices: false);
        }

        private static bool IsInRange(ushort value, ushort minimum, ushort maximum) =>
            value >= minimum && value <= maximum;

        private static bool IsZeroFilled(byte[] data)
        {
            foreach (byte value in data)
            {
                if (value != 0)
                    return false;
            }

            return true;
        }

    }
}
