using System;
using System.Collections.Generic;
using System.Text;
using static Vanara.PInvoke.Kernel32;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     Turns the Windows console's virtual terminal input back into the input records the
    ///     <see cref="Win32Console" /> otherwise reads, so it can take SGR-Pixels mouse reports
    ///     without giving up anything it knew about keys.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Pixel mouse reports only survive the console in virtual terminal input mode: read as
    ///         records, conhost decodes them as cell positions. In that mode every key arrives as
    ///         characters too, so the console also asks for win32-input-mode, which encodes each key
    ///         with its virtual key, scan code, character and modifiers, and those become the key
    ///         records they were.
    ///     </para>
    ///     <para>
    ///         A terminal that doesn't take win32-input-mode sends plain VT keys, which are mapped
    ///         as well as VT allows. Records that aren't characters (resize, focus) pass through.
    ///         Mouse records carry the zero-based pixel position, for the console to divide into cells.
    ///     </para>
    /// </remarks>
    internal sealed class Win32VtInputDecoder
    {
        private const char Escape = '\u001b';

        /// <summary>A sequence longer than this is not one we know, and is dropped rather than held.</summary>
        private const int MaxSequenceLength = 64;

        private const int WheelDelta = 120;

        private readonly StringBuilder _pending = new();

        /// <summary>The buttons the SGR reports have said are down, which every mouse record carries.</summary>
        private MOUSE_BUTTON_STATE _buttons = MOUSE_BUTTON_STATE.NONE;

        public INPUT_RECORD[] Decode(IReadOnlyList<INPUT_RECORD> records)
        {
            var decoded = new List<INPUT_RECORD>(records.Count);

            foreach (INPUT_RECORD record in records)
                if (record.EventType == EVENT_TYPE.KEY_EVENT &&
                    record.Event.KeyEvent.wVirtualKeyCode == 0 &&
                    record.Event.KeyEvent.uChar != 0)
                {
                    // terminal input arrives as key-down records carrying one character each
                    if (record.Event.KeyEvent.bKeyDown)
                        Append(record.Event.KeyEvent.uChar, decoded);
                }
                else
                {
                    decoded.Add(record);
                }

            // nothing follows a lone escape in this read, so it was the Esc key
            if (_pending.Length == 1 && _pending[0] == Escape)
            {
                _pending.Clear();
                AddKeyPress(decoded, VirtualKey.Escape, Escape, 0);
            }

            return [.. decoded];
        }

        private void Append(char c, List<INPUT_RECORD> decoded)
        {
            if (_pending.Length == 0)
            {
                if (c == Escape)
                    _pending.Append(c);
                else
                    AddCharacter(decoded, c, 0);
                return;
            }

            if (c == Escape)
            {
                // escape escape: the first was the Esc key; a sequence cut short by an escape is dropped
                if (_pending.Length == 1)
                    AddKeyPress(decoded, VirtualKey.Escape, Escape, 0);
                _pending.Clear();
                _pending.Append(c);
                return;
            }

            _pending.Append(c);

            if (_pending.Length == 2)
            {
                // ESC [ and ESC O start sequences; ESC and anything else is Alt and that key
                if (c is '[' or 'O')
                    return;
                _pending.Clear();
                AddCharacter(decoded, c, CONTROL_KEY_STATE.LEFT_ALT_PRESSED);
                return;
            }

            bool ss3 = _pending[1] == 'O';
            // a CSI sequence ends at its final byte, an SS3 one at the character after the O
            if (!ss3 && c is < '@' or > '~')
            {
                if (_pending.Length > MaxSequenceLength)
                    _pending.Clear();
                return;
            }

            string sequence = _pending.ToString();
            _pending.Clear();
            if (ss3)
                DecodeSs3(sequence[2], decoded);
            else
                DecodeCsi(sequence[2..^1], sequence[^1], decoded);
        }

        private void DecodeCsi(string parameters, char final, List<INPUT_RECORD> decoded)
        {
            if (final is 'M' or 'm' && parameters.StartsWith('<'))
            {
                DecodeSgrMouse(parameters[1..], final == 'm', decoded);
                return;
            }

            // replies to queries (DECRPM, DA1 and the like) carry a private marker; none are keys
            if (parameters.Length > 0 && parameters[0] is '?' or '>' or '=' or '<')
                return;
            if (parameters.Contains('$', StringComparison.Ordinal))
                return;

            int[] values = ParseParameters(parameters);

            switch (final)
            {
                case '_':
                    DecodeWin32Key(values, decoded);
                    return;
                case 'I':
                    decoded.Add(new INPUT_RECORD
                    {
                        EventType = EVENT_TYPE.FOCUS_EVENT,
                        Event = { FocusEvent = new FOCUS_EVENT_RECORD { bSetFocus = true } }
                    });
                    return;
                case 'O':
                    decoded.Add(new INPUT_RECORD
                    {
                        EventType = EVENT_TYPE.FOCUS_EVENT,
                        Event = { FocusEvent = new FOCUS_EVENT_RECORD { bSetFocus = false } }
                    });
                    return;
                case '~':
                {
                    ushort key = values.Length > 0 ? TildeKey(values[0]) : (ushort)0;
                    if (key != 0)
                        AddKeyPress(decoded, key, '\0', Modifiers(values, 1));
                    return;
                }
                case 'Z':
                    AddKeyPress(decoded, VirtualKey.Tab, '\t', CONTROL_KEY_STATE.SHIFT_PRESSED);
                    return;
                default:
                {
                    ushort key = LetterKey(final);
                    if (key != 0)
                        AddKeyPress(decoded, key, '\0', Modifiers(values, 1));
                    return;
                }
            }
        }

        private static void DecodeSs3(char final, List<INPUT_RECORD> decoded)
        {
            ushort key = LetterKey(final);
            if (key != 0)
                AddKeyPress(decoded, key, '\0', 0);
        }

        /// <summary>CSI Vk ; Sc ; Uc ; Kd ; Cs ; Rc _ , every parameter optional.</summary>
        private static void DecodeWin32Key(int[] values, List<INPUT_RECORD> decoded)
        {
            int Value(int index, int fallback)
            {
                return index < values.Length && values[index] >= 0 ? values[index] : fallback;
            }

            decoded.Add(new INPUT_RECORD
            {
                EventType = EVENT_TYPE.KEY_EVENT,
                Event =
                {
                    KeyEvent = new KEY_EVENT_RECORD
                    {
                        wVirtualKeyCode = (ushort)Value(0, 0),
                        wVirtualScanCode = (ushort)Value(1, 0),
                        uChar = (char)Value(2, 0),
                        bKeyDown = Value(3, 0) != 0,
                        dwControlKeyState = (CONTROL_KEY_STATE)Value(4, 0),
                        wRepeatCount = (ushort)Value(5, 1)
                    }
                }
            });
        }

        /// <summary>
        ///     CSI &lt; button ; x ; y M (press or motion) or m (release), x and y in one-based pixels.
        /// </summary>
        private void DecodeSgrMouse(string parameters, bool release, List<INPUT_RECORD> decoded)
        {
            int[] values = ParseParameters(parameters);
            if (values.Length != 3 || values[0] < 0 || values[1] < 0 || values[2] < 0)
                return;

            int code = values[0];
            var controlKeys = (CONTROL_KEY_STATE)0;
            if ((code & 4) != 0) controlKeys |= CONTROL_KEY_STATE.SHIFT_PRESSED;
            if ((code & 8) != 0) controlKeys |= CONTROL_KEY_STATE.LEFT_ALT_PRESSED;
            if ((code & 16) != 0) controlKeys |= CONTROL_KEY_STATE.LEFT_CTRL_PRESSED;

            MOUSE_BUTTON_STATE button = (code & 3, code & 128) switch
            {
                (0, 0) => MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED,
                (1, 0) => MOUSE_BUTTON_STATE.FROM_LEFT_2ND_BUTTON_PRESSED,
                (2, 0) => MOUSE_BUTTON_STATE.RIGHTMOST_BUTTON_PRESSED,
                (0, 128) => MOUSE_BUTTON_STATE.FROM_LEFT_3RD_BUTTON_PRESSED,
                (1, 128) => MOUSE_BUTTON_STATE.FROM_LEFT_4TH_BUTTON_PRESSED,
                _ => MOUSE_BUTTON_STATE.NONE
            };

            MOUSE_EVENT_FLAG flags;
            MOUSE_BUTTON_STATE state;
            if ((code & 64) != 0)
            {
                // wheel: the delta rides in the high word of the button state, signed
                int delta = (code & 1) == 0 ? WheelDelta : -WheelDelta;
                flags = (code & 2) == 0 ? MOUSE_EVENT_FLAG.MOUSE_WHEELED : MOUSE_EVENT_FLAG.MOUSE_HWHEELED;
                state = (MOUSE_BUTTON_STATE)(delta << 16) | _buttons;
            }
            else if ((code & 32) != 0)
            {
                flags = MOUSE_EVENT_FLAG.MOUSE_MOVED;
                state = _buttons;
            }
            else
            {
                flags = MOUSE_EVENT_FLAG.NONE;
                _buttons = release ? _buttons & ~button : _buttons | button;
                state = _buttons;
            }

            decoded.Add(new INPUT_RECORD
            {
                EventType = EVENT_TYPE.MOUSE_EVENT,
                Event =
                {
                    MouseEvent = new MOUSE_EVENT_RECORD
                    {
                        dwMousePosition = new COORD(ClampToShort(values[1] - 1), ClampToShort(values[2] - 1)),
                        dwButtonState = state,
                        dwControlKeyState = controlKeys,
                        dwEventFlags = flags
                    }
                }
            });
        }

        private static void AddCharacter(List<INPUT_RECORD> decoded, char c, CONTROL_KEY_STATE controlKeys)
        {
            switch (c)
            {
                case '\r' or '\n':
                    AddKeyPress(decoded, VirtualKey.Return, '\r', controlKeys);
                    return;
                case '\t':
                    AddKeyPress(decoded, VirtualKey.Tab, '\t', controlKeys);
                    return;
                case '\b' or '\u007f':
                    AddKeyPress(decoded, VirtualKey.Back, '\b', controlKeys);
                    return;
                case Escape:
                    AddKeyPress(decoded, VirtualKey.Escape, Escape, controlKeys);
                    return;
                case ' ':
                    AddKeyPress(decoded, VirtualKey.Space, ' ', controlKeys);
                    return;
                case >= '\u0001' and <= '\u001a':
                    // Ctrl+A to Ctrl+Z
                    AddKeyPress(decoded, (ushort)('A' + c - 1), c, controlKeys | CONTROL_KEY_STATE.LEFT_CTRL_PRESSED);
                    return;
                case >= 'a' and <= 'z':
                    AddKeyPress(decoded, (ushort)char.ToUpperInvariant(c), c, controlKeys);
                    return;
                case >= 'A' and <= 'Z':
                    AddKeyPress(decoded, c, c, controlKeys | CONTROL_KEY_STATE.SHIFT_PRESSED);
                    return;
                case >= '0' and <= '9':
                    AddKeyPress(decoded, c, c, controlKeys);
                    return;
                default:
                    // punctuation and other text: the character is what matters
                    AddKeyPress(decoded, 0, c, controlKeys);
                    return;
            }
        }

        private static void AddKeyPress(List<INPUT_RECORD> decoded, ushort virtualKey, char c,
            CONTROL_KEY_STATE controlKeys)
        {
            foreach (bool down in new[] { true, false })
                decoded.Add(new INPUT_RECORD
                {
                    EventType = EVENT_TYPE.KEY_EVENT,
                    Event =
                    {
                        KeyEvent = new KEY_EVENT_RECORD
                        {
                            bKeyDown = down,
                            wRepeatCount = 1,
                            wVirtualKeyCode = virtualKey,
                            uChar = c,
                            dwControlKeyState = controlKeys
                        }
                    }
                });
        }

        /// <summary>The xterm modifier parameter: one plus Shift 1, Alt 2, Ctrl 4.</summary>
        private static CONTROL_KEY_STATE Modifiers(int[] values, int index)
        {
            if (index >= values.Length || values[index] < 2)
                return 0;

            int bits = values[index] - 1;
            var controlKeys = (CONTROL_KEY_STATE)0;
            if ((bits & 1) != 0) controlKeys |= CONTROL_KEY_STATE.SHIFT_PRESSED;
            if ((bits & 2) != 0) controlKeys |= CONTROL_KEY_STATE.LEFT_ALT_PRESSED;
            if ((bits & 4) != 0) controlKeys |= CONTROL_KEY_STATE.LEFT_CTRL_PRESSED;
            return controlKeys;
        }

        private static ushort LetterKey(char final)
        {
            return final switch
            {
                'A' => VirtualKey.Up,
                'B' => VirtualKey.Down,
                'C' => VirtualKey.Right,
                'D' => VirtualKey.Left,
                'H' => VirtualKey.Home,
                'F' => VirtualKey.End,
                'P' => VirtualKey.F1,
                'Q' => VirtualKey.F1 + 1,
                'R' => VirtualKey.F1 + 2,
                'S' => VirtualKey.F1 + 3,
                _ => 0
            };
        }

        private static ushort TildeKey(int number)
        {
            return number switch
            {
                1 or 7 => VirtualKey.Home,
                2 => VirtualKey.Insert,
                3 => VirtualKey.Delete,
                4 or 8 => VirtualKey.End,
                5 => VirtualKey.Prior,
                6 => VirtualKey.Next,
                >= 11 and <= 15 => (ushort)(VirtualKey.F1 + number - 11),
                >= 17 and <= 21 => (ushort)(VirtualKey.F1 + 5 + number - 17),
                23 or 24 => (ushort)(VirtualKey.F1 + 10 + number - 23),
                _ => 0
            };
        }

        /// <summary>Semicolon-separated numbers; an empty one is -1, so a default can stand in.</summary>
        private static int[] ParseParameters(string parameters)
        {
            if (parameters.Length == 0)
                return [];

            string[] parts = parameters.Split(';');
            var values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                values[i] = int.TryParse(parts[i], out int value) ? value : -1;
            return values;
        }

        private static short ClampToShort(int value)
        {
            return (short)Math.Clamp(value, 0, short.MaxValue);
        }

        private static class VirtualKey
        {
            public const ushort Back = 0x08;
            public const ushort Tab = 0x09;
            public const ushort Return = 0x0D;
            public const ushort Escape = 0x1B;
            public const ushort Space = 0x20;
            public const ushort Prior = 0x21;
            public const ushort Next = 0x22;
            public const ushort End = 0x23;
            public const ushort Home = 0x24;
            public const ushort Left = 0x25;
            public const ushort Up = 0x26;
            public const ushort Right = 0x27;
            public const ushort Down = 0x28;
            public const ushort Insert = 0x2D;
            public const ushort Delete = 0x2E;
            public const ushort F1 = 0x70;
        }
    }
}
