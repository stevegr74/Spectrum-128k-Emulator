using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumKeyboardMessageFilter : IMessageFilter
    {
        internal const int KeyDownMessage = 0x0100;
        internal const int KeyUpMessage = 0x0101;
        internal const int SystemKeyDownMessage = 0x0104;
        internal const int SystemKeyUpMessage = 0x0105;

        private readonly Func<bool> shouldCapture;
        private readonly Action<Keys, bool> handleKey;

        public SpectrumKeyboardMessageFilter(Func<bool> shouldCapture, Action<Keys, bool> handleKey)
        {
            this.shouldCapture = shouldCapture;
            this.handleKey = handleKey;
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (!shouldCapture())
                return false;

            bool? isDown = message.Msg switch
            {
                KeyDownMessage or SystemKeyDownMessage => true,
                KeyUpMessage or SystemKeyUpMessage => false,
                _ => null
            };
            if (!isDown.HasValue)
                return false;

            Keys key = (Keys)((long)message.WParam & (long)Keys.KeyCode);
            if (!SpectrumKeyboardMapping.IsMappedKey(key))
                return false;

            handleKey(key, isDown.Value);
            return true;
        }
    }
}
