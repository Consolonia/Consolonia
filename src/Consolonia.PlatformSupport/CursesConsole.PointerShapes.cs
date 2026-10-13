using System;
using System.Text;
using Consolonia.Core.Infrastructure;
using Unix.Terminal;

namespace Consolonia.PlatformSupport
{
    public partial class CursesConsole
    {
        /// <summary>
        ///     Asks the terminal which mouse pointer shapes it can draw itself (OSC 22); see
        ///     <see cref="ConsoleBase.TryToSupportPointerShapes(Func{string})" />. Skipped on a
        ///     Linux virtual console, as the Kitty keyboard probe is.
        /// </summary>
        private void TryToSupportPointerShapes()
        {
            if (IsTtyTerminal())
                return;

            TryToSupportPointerShapes(() =>
            {
                try
                {
                    return ReadAnswersUntilDeviceAttributes();
                }
                finally
                {
                    Curses.timeout(NoInputTimeout);
                }
            });
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