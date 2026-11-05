using Consolonia.Controls;
using Consolonia.Core.Text;

namespace Consolonia.PlatformSupport
{
    public partial class CursesConsole
    {
        /// <summary>Set while the terminal reports the mouse in pixels rather than cells.</summary>
        private bool _pixelMouse;

        /// <summary>
        ///     Has the terminal report the mouse in pixels (SGR-Pixels, DEC private mode 1016) when it
        ///     can, so pointer positions land within a cell rather than on its corner.
        /// </summary>
        /// <remarks>
        ///     The reports keep the SGR (1006) format, so ncurses, and the kitty input matcher, read
        ///     them as before; only their numbers change meaning, and the mouse handlers divide
        ///     them back into cells. GPM reports cells of its own, and a terminal without mouse
        ///     motion has nothing to place within a cell, so both keep cells.
        /// </remarks>
        private void TryToSupportPixelMouse()
        {
            if (_gpmMonitor != null ||
                !Capabilities.HasFlag(ConsoleCapabilities.SupportsMouseMove) ||
                !TerminalSupportsPixelMouse)
                return;

            WriteText(Esc.EnableSgrPixelsMouse);
            Flush();
            _pixelMouse = true;
        }
    }
}
