using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using static Vanara.PInvoke.Kernel32;

namespace Consolonia.PlatformSupport
{
    public partial class Win32Console
    {
        /// <summary>
        ///     How long the terminal may stay silent, in milliseconds, before the probe stops waiting
        ///     for it.
        /// </summary>
        /// <remarks>
        ///     Counted from the last input record, not from the start of the probe, so a slow terminal
        ///     loses nothing as long as it keeps answering, and the reading ends at the sentinel's
        ///     answer, which keeps the answers out of the input loop. The whole wait is paid only by a
        ///     terminal that answers none of the queries, not even the sentinel -- which classic
        ///     conhost answers itself -- and with no answer at all there is nothing else to wait for.
        /// </remarks>
        private const uint PointerShapesProbeQuietMs = 1000;

        private static readonly Regex DeviceAttributesAnswer =
            new(@"\u001b\[\?[0-9;]*c");

        /// <summary>
        ///     Sets the console up, then asks it which pointer shapes it has (see
        ///     <see cref="ConsoleBase.TryToSupportPointerShapes(Func{string})" />), then clears the
        ///     screen.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Through ConPTY the queries reach the terminal hosting the console, and its answers
        ///         come back as key events, one character each. Classic conhost answers the Device
        ///         Attributes sentinel itself and ignores the rest, so it is found to have no shapes,
        ///         and keeps the character pointer.
        ///     </para>
        ///     <para>
        ///         The probe comes after the base setup because that is what moves output to the
        ///         alternate screen (or the legacy console's second buffer); probing first would leave
        ///         whatever the console makes of the queries on the user's own screen. The clear comes
        ///         after the probe because a console that does not understand the queries prints them,
        ///         and the renderer only draws cells it thinks changed -- the query text stayed
        ///         wherever the first frame was blank.
        ///     </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public override void PrepareConsole()
        {
            base.PrepareConsole();
            TryToSupportPointerShapes(ReadAnswersUntilDeviceAttributes);
            ClearScreen();
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
        ///     Attributes answer, or all it sent before going quiet for
        ///     <see cref="PointerShapesProbeQuietMs" />.
        /// </summary>
        private string ReadAnswersUntilDeviceAttributes()
        {
            var response = new StringBuilder();

            // The input handle is signalled while records are waiting to be read, so this blocks
            // only for as long as the terminal is silent, and never past the quiet time.
            while (response.Length < 1024 &&
                   WaitForSingleObject(_windowsConsole.InputHandle, PointerShapesProbeQuietMs) ==
                   WAIT_STATUS.WAIT_OBJECT_0)
            {
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