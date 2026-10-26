using System;

namespace Spectrum128kEmulator.Input
{
    public enum SpectrumJoystickInterface
    {
        None,
        Kempston,
        Cursor,
        SinclairKeys6To0 = 3,
        SinclairKeys1To5 = 4
    }

    [Flags]
    public enum SpectrumJoystickState : byte
    {
        None = 0,
        Right = 1 << 0,
        Left = 1 << 1,
        Down = 1 << 2,
        Up = 1 << 3,
        Fire = 1 << 4,
        All = Right | Left | Down | Up | Fire
    }
}
