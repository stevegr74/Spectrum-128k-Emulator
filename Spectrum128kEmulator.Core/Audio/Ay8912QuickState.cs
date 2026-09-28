namespace Spectrum128kEmulator.Audio
{
    public partial class Ay8912
    {
        public sealed class QuickState
        {
            private readonly byte[] registers;

            internal QuickState(byte[] registers, int selectedRegister)
            {
                this.registers = (byte[])registers.Clone();
                SelectedRegister = selectedRegister;
            }

            internal int SelectedRegister { get; }
            internal byte[] GetRegistersCopy() => (byte[])registers.Clone();
        }

        public QuickState CaptureQuickState() => new QuickState(registers, selectedRegister);

        public void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            byte[] restoredRegisters = state.GetRegistersCopy();
            Buffer.BlockCopy(restoredRegisters, 0, registers, 0, registers.Length);
            selectedRegister = state.SelectedRegister & 0x0F;
        }
    }
}
