using System.Linq;
using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public static class SpectrumKeyboardMapping
    {
        public readonly record struct MatrixPosition(int Row, int Bit);

        public static bool IsMappedKey(Keys key) => GetPositions(key).Length != 0;

        public static MatrixPosition[] GetPositions(Keys key)
        {
            return key switch
            {
                Keys.Left => Positions((0, 0), (3, 4)),
                Keys.Down => Positions((0, 0), (4, 4)),
                Keys.Up => Positions((0, 0), (4, 3)),
                Keys.Right => Positions((0, 0), (4, 2)),
                Keys.Back or Keys.Delete => Positions((0, 0), (4, 0)),
                Keys.Escape => Positions((0, 0), (7, 0)),
                Keys.CapsLock => Positions((0, 0), (3, 1)),
                Keys.Tab => Positions((0, 0), (7, 1)),
                Keys.Oemcomma => Positions((7, 1), (7, 3)),
                Keys.OemPeriod => Positions((7, 1), (7, 2)),
                Keys.OemSemicolon => Positions((7, 1), (5, 1)),
                Keys.OemQuotes => Positions((7, 1), (5, 0)),
                Keys.OemQuestion => Positions((7, 1), (0, 4)),
                Keys.OemMinus => Positions((7, 1), (6, 3)),
                Keys.Oemplus => Positions((7, 1), (6, 2)),

                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => Positions((0, 0)),
                Keys.Z => Positions((0, 1)),
                Keys.X => Positions((0, 2)),
                Keys.C => Positions((0, 3)),
                Keys.V => Positions((0, 4)),

                Keys.A => Positions((1, 0)),
                Keys.S => Positions((1, 1)),
                Keys.D => Positions((1, 2)),
                Keys.F => Positions((1, 3)),
                Keys.G => Positions((1, 4)),

                Keys.Q => Positions((2, 0)),
                Keys.W => Positions((2, 1)),
                Keys.E => Positions((2, 2)),
                Keys.R => Positions((2, 3)),
                Keys.T => Positions((2, 4)),

                Keys.D1 or Keys.NumPad1 => Positions((3, 0)),
                Keys.D2 or Keys.NumPad2 => Positions((3, 1)),
                Keys.D3 or Keys.NumPad3 => Positions((3, 2)),
                Keys.D4 or Keys.NumPad4 => Positions((3, 3)),
                Keys.D5 or Keys.NumPad5 => Positions((3, 4)),

                Keys.D0 or Keys.NumPad0 => Positions((4, 0)),
                Keys.D9 or Keys.NumPad9 => Positions((4, 1)),
                Keys.D8 or Keys.NumPad8 => Positions((4, 2)),
                Keys.D7 or Keys.NumPad7 => Positions((4, 3)),
                Keys.D6 or Keys.NumPad6 => Positions((4, 4)),

                Keys.P => Positions((5, 0)),
                Keys.O => Positions((5, 1)),
                Keys.I => Positions((5, 2)),
                Keys.U => Positions((5, 3)),
                Keys.Y => Positions((5, 4)),

                Keys.Enter => Positions((6, 0)),
                Keys.L => Positions((6, 1)),
                Keys.K => Positions((6, 2)),
                Keys.J => Positions((6, 3)),
                Keys.H => Positions((6, 4)),

                Keys.Space => Positions((7, 0)),
                Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
                Keys.Menu or Keys.LMenu or Keys.RMenu => Positions((7, 1)),
                Keys.M => Positions((7, 2)),
                Keys.N => Positions((7, 3)),
                Keys.B => Positions((7, 4)),
                _ => Array.Empty<MatrixPosition>()
            };
        }

        public static int[] GetRows(Keys key) =>
            GetPositions(key).Select(position => position.Row).Distinct().ToArray();

        private static MatrixPosition[] Positions(params (int Row, int Bit)[] positions) =>
            positions.Select(position => new MatrixPosition(position.Row, position.Bit)).ToArray();
    }
}
