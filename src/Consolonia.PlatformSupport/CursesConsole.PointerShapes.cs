using System;
using System.Collections.Generic;
using System.Text;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using Unix.Terminal;

namespace Consolonia.PlatformSupport
{
    public partial class CursesConsole
    {
        /// <summary>
        ///     Works out which mouse pointer shapes the terminal can draw itself (OSC 22), so the
        ///     pointer can be the terminal's own rather than a character.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Kitty's support query names every shape an Avalonia cursor maps to, split over as
        ///         many queries as keep each short enough to be read whole, and is followed by
        ///         XTVERSION and then a Device Attributes request as a sentinel, which every terminal
        ///         answers. Everything up to the sentinel's answer is read.
        ///     </para>
        ///     <para>
        ///         What the answers mean is decided by <see cref="PointerShapes.Detect" />, shared with
        ///         the Windows console. A terminal that neither answers nor is recognised keeps the
        ///         character pointer.
        ///     </para>
        ///     <para>Skipped on a Linux virtual console, as the Kitty keyboard probe is.</para>
        /// </remarks>
        private void TryToSupportPointerShapes()
        {
            if (IsTtyTerminal())
                return;

            string answers;
            try
            {
                foreach (IReadOnlyList<string> batch in PointerShapes.QueryBatches)
                    WriteText(Esc.QueryPointerShapes(batch));
                WriteText(Esc.QueryTerminalVersion);
                WriteText("\u001b[c"); // sentinel: Device Attributes query
                Flush(); // the queries must actually reach the terminal, otherwise it never responds

                answers = ReadAnswersUntilDeviceAttributes();
            }
            finally
            {
                Curses.timeout(NoInputTimeout);
            }

            (IReadOnlySet<string> supported, bool usesX11Names) =
                PointerShapes.Detect(answers, Environment.GetEnvironmentVariable);
            SupportedPointerShapes = supported;
            PointerShapesUseX11Names = usesX11Names;
        }

        /// <summary>
        ///     Whatever the terminal sends back, up to and including its Device Attributes answer, or
        ///     all it sent before going quiet for 100 ms.
        /// </summary>
        private static string ReadAnswersUntilDeviceAttributes()
        {
            Curses.timeout(100);

            var response = new StringBuilder();
            while (response.Length < 1024)
            {
                int code = Curses.get_wch(out int wch);
                if (code == Curses.ERR)
                    break; // timed out

                if (code != Curses.KEY_CODE_YES)
                    response.Append((char)wch);

                if (KittyDeviceAttributesAnswerRegex().IsMatch(response.ToString()))
                    break;
            }

            return response.ToString();
        }
    }
}