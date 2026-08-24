using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Consolonia.Core.InternalHelpers;
using Consolonia.Core.Text;
using Unix.Terminal;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     Kitty keyboard protocol support: detection (via "CSI ? u" query) and CSI keyboard
    ///     sequence decoding (Kitty CSI u, legacy CSI letter, legacy CSI tilde).
    /// </summary>
    public partial class CursesConsole
    {
        private bool _isKittyKeyboardEnabled;

        /// <summary>
        ///     Dummy enum used only to satisfy <see cref="FlagTranslator{TInput,TOutput}" />'s
        ///     Enum constraint, allowing plain numeric key codes to be looked up via
        ///     <see cref="FlagTranslator{TInput,TOutput}.Translate(TInput,bool)" /> instead of a Dictionary.
        /// </summary>
        private enum KittyKeyCode
        {
        }

        /// <summary>
        ///     Kitty keyboard protocol special key codepoints mapped to Avalonia Key.
        /// </summary>
        private static readonly FlagTranslator<KittyKeyCode, Avalonia.Input.Key>
            KittyKeyFlagTranslator = new([
                ((KittyKeyCode)27, Avalonia.Input.Key.Escape),
                ((KittyKeyCode)13, Avalonia.Input.Key.Return),
                ((KittyKeyCode)9, Avalonia.Input.Key.Tab),
                ((KittyKeyCode)127, Avalonia.Input.Key.Back),
                ((KittyKeyCode)57348, Avalonia.Input.Key.Insert),
                ((KittyKeyCode)57349, Avalonia.Input.Key.Delete),
                ((KittyKeyCode)57350, Avalonia.Input.Key.Left),
                ((KittyKeyCode)57351, Avalonia.Input.Key.Right),
                ((KittyKeyCode)57352, Avalonia.Input.Key.Up),
                ((KittyKeyCode)57353, Avalonia.Input.Key.Down),
                ((KittyKeyCode)57354, Avalonia.Input.Key.PageUp),
                ((KittyKeyCode)57355, Avalonia.Input.Key.PageDown),
                ((KittyKeyCode)57356, Avalonia.Input.Key.Home),
                ((KittyKeyCode)57357, Avalonia.Input.Key.End),
                ((KittyKeyCode)57358, Avalonia.Input.Key.CapsLock),
                ((KittyKeyCode)57359, Avalonia.Input.Key.Scroll),
                ((KittyKeyCode)57360, Avalonia.Input.Key.NumLock),
                ((KittyKeyCode)57361, Avalonia.Input.Key.PrintScreen),
                ((KittyKeyCode)57362, Avalonia.Input.Key.Pause),
                ((KittyKeyCode)57363, Avalonia.Input.Key.Apps),
                ((KittyKeyCode)57364, Avalonia.Input.Key.F1),
                ((KittyKeyCode)57365, Avalonia.Input.Key.F2),
                ((KittyKeyCode)57366, Avalonia.Input.Key.F3),
                ((KittyKeyCode)57367, Avalonia.Input.Key.F4),
                ((KittyKeyCode)57368, Avalonia.Input.Key.F5),
                ((KittyKeyCode)57369, Avalonia.Input.Key.F6),
                ((KittyKeyCode)57370, Avalonia.Input.Key.F7),
                ((KittyKeyCode)57371, Avalonia.Input.Key.F8),
                ((KittyKeyCode)57372, Avalonia.Input.Key.F9),
                ((KittyKeyCode)57373, Avalonia.Input.Key.F10),
                ((KittyKeyCode)57374, Avalonia.Input.Key.F11),
                ((KittyKeyCode)57375, Avalonia.Input.Key.F12),
                ((KittyKeyCode)57376, Avalonia.Input.Key.F13),
                ((KittyKeyCode)57377, Avalonia.Input.Key.F14),
                ((KittyKeyCode)57378, Avalonia.Input.Key.F15),
                ((KittyKeyCode)57379, Avalonia.Input.Key.F16),
                ((KittyKeyCode)57380, Avalonia.Input.Key.F17),
                ((KittyKeyCode)57381, Avalonia.Input.Key.F18),
                ((KittyKeyCode)57382, Avalonia.Input.Key.F19),
                ((KittyKeyCode)57383, Avalonia.Input.Key.F20),
                ((KittyKeyCode)57384, Avalonia.Input.Key.F21),
                ((KittyKeyCode)57385, Avalonia.Input.Key.F22),
                ((KittyKeyCode)57386, Avalonia.Input.Key.F23),
                ((KittyKeyCode)57387, Avalonia.Input.Key.F24),
                ((KittyKeyCode)57441, Avalonia.Input.Key.LeftShift),
                ((KittyKeyCode)57442, Avalonia.Input.Key.LeftCtrl),
                ((KittyKeyCode)57443, Avalonia.Input.Key.LeftAlt),
                ((KittyKeyCode)57444, Avalonia.Input.Key.LWin),
                ((KittyKeyCode)57447, Avalonia.Input.Key.RightShift),
                ((KittyKeyCode)57448, Avalonia.Input.Key.RightCtrl),
                ((KittyKeyCode)57449, Avalonia.Input.Key.RightAlt),
                ((KittyKeyCode)57450, Avalonia.Input.Key.RWin)
            ]);

        /// <summary>
        ///     Dummy enum used only to satisfy <see cref="FlagTranslator{TInput,TOutput}" />'s
        ///     Enum constraint, allowing plain char key codes to be looked up via
        ///     <see cref="FlagTranslator{TInput,TOutput}.Translate(TInput,bool)" /> instead of a Dictionary.
        /// </summary>
        private enum CsiLetterKeyCode
        {
        }

        /// <summary>
        ///     Legacy CSI letter terminators mapped to Avalonia Key.
        ///     Used for arrow keys (A-D), Home (H), End (F), F1-F4 (P-S).
        /// </summary>
        private static readonly FlagTranslator<CsiLetterKeyCode, Avalonia.Input.Key>
            CsiLetterKeyFlagTranslator = new([
                ((CsiLetterKeyCode)'A', Avalonia.Input.Key.Up),
                ((CsiLetterKeyCode)'B', Avalonia.Input.Key.Down),
                ((CsiLetterKeyCode)'C', Avalonia.Input.Key.Right),
                ((CsiLetterKeyCode)'D', Avalonia.Input.Key.Left),
                ((CsiLetterKeyCode)'H', Avalonia.Input.Key.Home),
                ((CsiLetterKeyCode)'F', Avalonia.Input.Key.End),
                ((CsiLetterKeyCode)'P', Avalonia.Input.Key.F1),
                ((CsiLetterKeyCode)'Q', Avalonia.Input.Key.F2),
                ((CsiLetterKeyCode)'R', Avalonia.Input.Key.F3),
                ((CsiLetterKeyCode)'S', Avalonia.Input.Key.F4),
                ((CsiLetterKeyCode)'Z', Avalonia.Input.Key.Tab),
                ((CsiLetterKeyCode)'E', Avalonia.Input.Key.Clear)
            ]);

        /// <summary>
        ///     Dummy enum used only to satisfy <see cref="FlagTranslator{TInput,TOutput}" />'s
        ///     Enum constraint, allowing plain numeric key codes to be looked up via
        ///     <see cref="FlagTranslator{TInput,TOutput}.Translate(TInput,bool)" /> instead of a Dictionary.
        /// </summary>
        private enum CsiTildeKeyCode
        {
        }

        /// <summary>
        ///     Legacy CSI tilde key numbers mapped to Avalonia Key.
        ///     Used for Insert, Delete, PageUp, PageDown, F5-F12.
        /// </summary>
        private static readonly FlagTranslator<CsiTildeKeyCode, Avalonia.Input.Key>
            CsiTildeKeyFlagTranslator = new([
                ((CsiTildeKeyCode)1, Avalonia.Input.Key.Home),
                ((CsiTildeKeyCode)2, Avalonia.Input.Key.Insert),
                ((CsiTildeKeyCode)3, Avalonia.Input.Key.Delete),
                ((CsiTildeKeyCode)4, Avalonia.Input.Key.End),
                ((CsiTildeKeyCode)5, Avalonia.Input.Key.PageUp),
                ((CsiTildeKeyCode)6, Avalonia.Input.Key.PageDown),
                ((CsiTildeKeyCode)15, Avalonia.Input.Key.F5),
                ((CsiTildeKeyCode)17, Avalonia.Input.Key.F6),
                ((CsiTildeKeyCode)18, Avalonia.Input.Key.F7),
                ((CsiTildeKeyCode)19, Avalonia.Input.Key.F8),
                ((CsiTildeKeyCode)20, Avalonia.Input.Key.F9),
                ((CsiTildeKeyCode)21, Avalonia.Input.Key.F10),
                ((CsiTildeKeyCode)23, Avalonia.Input.Key.F11),
                ((CsiTildeKeyCode)24, Avalonia.Input.Key.F12)
            ]);

        private const int KittyQueryResponseTimeout = 100;

        /// <summary>
        ///     Detects whether the terminal actually supports the Kitty keyboard protocol by
        ///     sending "CSI ? u" (query current progressive enhancement flags) followed by a
        ///     sentinel Device Attributes query ("CSI c"), then reading the reply.
        ///     A terminal supporting the protocol replies with "CSI ? &lt;flags&gt; u" before/instead
        ///     of (or in addition to) the Device Attributes response. This works correctly even
        ///     through tmux/screen or over SSH, unlike relying solely on environment variables.
        /// </summary>
        private bool QuerySupportsKittyKeyboardProtocol()
        {
            if (IsTtyTerminal())
                return false;

            try
            {
                WriteText(Esc.QueryKittyKeyboardFlags);
                WriteText("\u001b[c"); // sentinel: Device Attributes query

                Curses.timeout(KittyQueryResponseTimeout);

                var response = new StringBuilder();
                while (response.Length < 64)
                {
                    int code = Curses.get_wch(out int wch);
                    if (code == Curses.ERR)
                        break; // timed out, no more data coming

                    if (code != Curses.KEY_CODE_YES)
                        response.Append((char)wch);

                    string collected = response.ToString();
                    if (Regex.IsMatch(collected, @"\x1b\[\?[0-9]*u"))
                        return true;

                    // Sentinel (Device Attributes) response arrived without a preceding
                    // kitty keyboard response -> protocol is not supported.
                    if (Regex.IsMatch(collected, @"\x1b\[\?[0-9;]*c"))
                        break;
                }

                return false;
            }
            finally
            {
                Curses.timeout(NoInputTimeout);
            }
        }

        private void HandleCsiKeyboardEvent((int keyCode, int modifiers, int eventType, char terminator) csiEvent)
        {
            int keyCode = csiEvent.keyCode;
            int modifierValue = csiEvent.modifiers - 1; // Protocol uses modifiers + 1
            int eventType = csiEvent.eventType;
            char terminator = csiEvent.terminator;

            // event type 3 = release, we handle press (1) and repeat (2)
            bool isDown = eventType != 3;

            // Decode modifiers
            RawInputModifiers rawModifiers = RawInputModifiers.None;
            if ((modifierValue & 1) != 0) rawModifiers |= RawInputModifiers.Shift;
            if ((modifierValue & 2) != 0) rawModifiers |= RawInputModifiers.Alt;
            if ((modifierValue & 4) != 0) rawModifiers |= RawInputModifiers.Control;

            // Try to map the keycode based on terminator type
            Avalonia.Input.Key key;
            char character = char.MinValue;

            if (terminator != 'u' && terminator != '~' &&
                CsiLetterKeyFlagTranslator.Translate((CsiLetterKeyCode)terminator, true) is var letterKey &&
                letterKey != Avalonia.Input.Key.None)
            {
                // Legacy CSI letter sequence (arrows, Home, End, F1-F4)
                key = letterKey;
                if (terminator == 'Z') rawModifiers |= RawInputModifiers.Shift;
            }
            else if (terminator == '~' &&
                     CsiTildeKeyFlagTranslator.Translate((CsiTildeKeyCode)keyCode, true) is var tildeKey &&
                     tildeKey != Avalonia.Input.Key.None)
            {
                // Legacy CSI tilde sequence (Insert, Delete, PgUp, PgDn, F5-F12)
                key = tildeKey;
            }
            else if (terminator == 'u' &&
                     KittyKeyFlagTranslator.Translate((KittyKeyCode)keyCode, true) is var mappedKey &&
                     mappedKey != Avalonia.Input.Key.None)
            {
                key = mappedKey;
                // For Enter, Tab, Backspace, Space - set the character
                if (keyCode == 13) character = '\r';
                else if (keyCode == 9) character = '\t';
                else if (keyCode == 127) character = '\b';
                else if (keyCode == 27) character = '\x1B';
            }
            else if (terminator == 'u' && keyCode >= 32 && keyCode < 127)
            {
                // Printable ASCII via CSI u
                character = (char)keyCode;

                // Map to Avalonia Key enum
                if (keyCode >= 'a' && keyCode <= 'z')
                    key = Avalonia.Input.Key.A + (keyCode - 'a');
                else if (keyCode >= 'A' && keyCode <= 'Z')
                {
                    key = Avalonia.Input.Key.A + (keyCode - 'A');
                    rawModifiers |= RawInputModifiers.Shift;
                }
                else if (keyCode >= '0' && keyCode <= '9')
                    key = Avalonia.Input.Key.D0 + (keyCode - '0');
                else if (keyCode == ' ')
                    key = Avalonia.Input.Key.Space;
                else
                {
                    // Map punctuation characters to Avalonia Key
                    key = character switch
                    {
                        '.' => Avalonia.Input.Key.OemPeriod,
                        ',' => Avalonia.Input.Key.OemComma,
                        ';' => Avalonia.Input.Key.OemSemicolon,
                        '/' => Avalonia.Input.Key.Oem2,
                        '\\' => Avalonia.Input.Key.Oem5,
                        '=' => Avalonia.Input.Key.OemPlus,
                        '-' => Avalonia.Input.Key.OemMinus,
                        '[' => Avalonia.Input.Key.Oem4,
                        ']' => Avalonia.Input.Key.Oem6,
                        '\'' => Avalonia.Input.Key.Oem7,
                        '`' => Avalonia.Input.Key.Oem3,
                        _ => Avalonia.Input.Key.None
                    };
                }
            }
            else
            {
                // Unknown keycode or terminator
                return;
            }

            RaiseKeyPress(key, character, rawModifiers, isDown, (ulong)Environment.TickCount64);
            if (eventType != 3) // For press events, also raise key up
            {
                Thread.Yield();
                RaiseKeyPress(key, character, rawModifiers, false, (ulong)Environment.TickCount64);
            }
        }
    }
}
