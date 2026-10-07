using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using static Vanara.PInvoke.Kernel32;

namespace Consolonia.PlatformSupport
{
    public partial class Win32Console
    {
        /// <summary>How long to wait for the terminal's answers before deciding it gave none.</summary>
        private const int PointerShapesProbeTimeoutMs = 300;

        private static readonly Regex DeviceAttributesAnswer =
            new(@"\u001b\[\?[0-9;]*c");

        /// <summary>
        ///     Works out which mouse pointer shapes the terminal can draw itself (OSC 22), as the curses
        ///     console does: the same three queries, and the same <see cref="PointerShapes.Detect" />.
        /// </summary>
        /// <remarks>
        ///     Through ConPTY the queries reach the terminal hosting the console, and its answers come
        ///     back as key events, one character each. Classic conhost answers the Device Attributes
        ///     sentinel itself and ignores the rest, so it is found to have no shapes, and keeps the
        ///     character pointer.
        /// </remarks>
        private void TryToSupportPointerShapes()
        {
            WriteText(Esc.QueryPointerShapes(PointerShapes.Used));
            WriteText(Esc.QueryTerminalVersion);
            WriteText("\u001b[c"); // sentinel: Device Attributes query
            Flush(); // the queries must actually reach the terminal, otherwise it never responds

            (IReadOnlySet<string> supported, bool usesX11Names) =
                PointerShapes.Detect(ReadAnswersUntilDeviceAttributes(), Environment.GetEnvironmentVariable);
            SupportedPointerShapes = supported;
            PointerShapesUseX11Names = usesX11Names;
        }

        /// <summary>
        ///     Gives the pointer back to the terminal, as the curses console does, so it is not left
        ///     showing the last shape (an I-beam, say) after the program exits.
        /// </summary>
        /// <remarks>Written before the base restore, while the console still takes VT sequences.</remarks>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void RestoreConsole()
        {
            if (SupportedPointerShapes.Count > 0)
                WriteText(Esc.ResetPointerShape);

            base.RestoreConsole();
        }

        /// <summary>
        ///     The characters of whatever the terminal sends back, up to and including its Device
        ///     Attributes answer, or all it sent before the probe's time ran out.
        /// </summary>
        private string ReadAnswersUntilDeviceAttributes()
        {
            var response = new StringBuilder();
            long deadline = Environment.TickCount64 + PointerShapesProbeTimeoutMs;

            while (Environment.TickCount64 < deadline && response.Length < 1024)
            {
                if (!GetNumberOfConsoleInputEvents(_windowsConsole.InputHandle, out uint pending) || pending == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                foreach (INPUT_RECORD record in _windowsConsole.ReadConsoleInput())
                    if (record.EventType == EVENT_TYPE.KEY_EVENT &&
                        record.Event.KeyEvent.bKeyDown &&
                        record.Event.KeyEvent.uChar != 0)
                        response.Append(record.Event.KeyEvent.uChar);

                if (DeviceAttributesAnswer.IsMatch(response.ToString()))
                    break;
            }

            return response.ToString();
        }
    }
}