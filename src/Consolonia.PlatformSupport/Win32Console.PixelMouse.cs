using Consolonia.Core.Text;
using static Vanara.PInvoke.Kernel32;

namespace Consolonia.PlatformSupport
{
    public partial class Win32Console
    {
        /// <summary>Set while the mouse is read in pixels; it decodes all input from then on.</summary>
        private Win32VtInputDecoder _vtInputDecoder;

        /// <summary>
        ///     Has the terminal report the mouse in pixels (SGR-Pixels, DEC private mode 1016) when it
        ///     can, so pointer positions land within a cell rather than on its corner.
        /// </summary>
        /// <remarks>
        ///     Read as input records, conhost decodes those reports as cells, so the console switches
        ///     to virtual terminal input, where they pass through untouched, and asks for
        ///     win32-input-mode so keys still arrive whole. <see cref="Win32VtInputDecoder" /> turns
        ///     both back into records. A terminal that does not recognize mode 1016 (classic conhost
        ///     among them) keeps the plain record input.
        /// </remarks>
        private void TryToSupportPixelMouse()
        {
            if (!TerminalSupportsPixelMouse)
                return;

            _windowsConsole.ConsoleMode |= CONSOLE_INPUT_MODE.ENABLE_VIRTUAL_TERMINAL_INPUT;
            _vtInputDecoder = new Win32VtInputDecoder();

            WriteText(Esc.EnableWin32InputMode);
            WriteText(Esc.EnableAllMouseEvents);
            WriteText(Esc.EnableExtendedMouseTracking);
            WriteText(Esc.EnableSgrPixelsMouse);
            Flush();
        }

        private INPUT_RECORD[] ReadInputRecords()
        {
            INPUT_RECORD[] records = _windowsConsole.ReadConsoleInput();
            return _vtInputDecoder?.Decode(records) ?? records;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _vtInputDecoder != null)
            {
                // written ahead of the base restore, which flushes them with the rest
                WriteText(Esc.DisableSgrPixelsMouse);
                WriteText(Esc.DisableWin32InputMode);
                _windowsConsole.ConsoleMode &= ~CONSOLE_INPUT_MODE.ENABLE_VIRTUAL_TERMINAL_INPUT;
                _vtInputDecoder = null;
            }

            base.Dispose(disposing);
        }
    }
}
