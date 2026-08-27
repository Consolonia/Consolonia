using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Consolonia.Controls;
using Consolonia.Core.Helpers.InputProcessing;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;
using Consolonia.Core.Text;
using Unix.Terminal;

namespace Consolonia.PlatformSupport
{
    public partial class CursesConsole
    {
        private static readonly FlagTranslator<KittyKeyCode, Key>
            KittyKeyFlagTranslator = new([
                ((KittyKeyCode)27, Key.Escape),
                ((KittyKeyCode)13, Key.Return),
                ((KittyKeyCode)9, Key.Tab),
                ((KittyKeyCode)127, Key.Back),
                ((KittyKeyCode)57348, Key.Insert),
                ((KittyKeyCode)57349, Key.Delete),
                ((KittyKeyCode)57350, Key.Left),
                ((KittyKeyCode)57351, Key.Right),
                ((KittyKeyCode)57352, Key.Up),
                ((KittyKeyCode)57353, Key.Down),
                ((KittyKeyCode)57354, Key.PageUp),
                ((KittyKeyCode)57355, Key.PageDown),
                ((KittyKeyCode)57356, Key.Home),
                ((KittyKeyCode)57357, Key.End),
                ((KittyKeyCode)57358, Key.CapsLock),
                ((KittyKeyCode)57359, Key.Scroll),
                ((KittyKeyCode)57360, Key.NumLock),
                ((KittyKeyCode)57361, Key.PrintScreen),
                ((KittyKeyCode)57362, Key.Pause),
                ((KittyKeyCode)57363, Key.Apps),
                ((KittyKeyCode)57364, Key.F1),
                ((KittyKeyCode)57365, Key.F2),
                ((KittyKeyCode)57366, Key.F3),
                ((KittyKeyCode)57367, Key.F4),
                ((KittyKeyCode)57368, Key.F5),
                ((KittyKeyCode)57369, Key.F6),
                ((KittyKeyCode)57370, Key.F7),
                ((KittyKeyCode)57371, Key.F8),
                ((KittyKeyCode)57372, Key.F9),
                ((KittyKeyCode)57373, Key.F10),
                ((KittyKeyCode)57374, Key.F11),
                ((KittyKeyCode)57375, Key.F12),
                ((KittyKeyCode)57376, Key.F13),
                ((KittyKeyCode)57377, Key.F14),
                ((KittyKeyCode)57378, Key.F15),
                ((KittyKeyCode)57379, Key.F16),
                ((KittyKeyCode)57380, Key.F17),
                ((KittyKeyCode)57381, Key.F18),
                ((KittyKeyCode)57382, Key.F19),
                ((KittyKeyCode)57383, Key.F20),
                ((KittyKeyCode)57384, Key.F21),
                ((KittyKeyCode)57385, Key.F22),
                ((KittyKeyCode)57386, Key.F23),
                ((KittyKeyCode)57387, Key.F24),
                ((KittyKeyCode)57441, Key.LeftShift),
                ((KittyKeyCode)57442, Key.LeftCtrl),
                ((KittyKeyCode)57443, Key.LeftAlt),
                ((KittyKeyCode)57444, Key.LWin),
                ((KittyKeyCode)57447, Key.RightShift),
                ((KittyKeyCode)57448, Key.RightCtrl),
                ((KittyKeyCode)57449, Key.RightAlt),
                ((KittyKeyCode)57450, Key.RWin)
            ]);

        /// <summary>
        ///     Legacy CSI letter terminators
        /// </summary>
        private static readonly FlagTranslator<CsiLetterKeyCode, Key>
            CsiLetterKeyFlagTranslator = new([
                ((CsiLetterKeyCode)'A', Key.Up),
                ((CsiLetterKeyCode)'B', Key.Down),
                ((CsiLetterKeyCode)'C', Key.Right),
                ((CsiLetterKeyCode)'D', Key.Left),
                ((CsiLetterKeyCode)'H', Key.Home),
                ((CsiLetterKeyCode)'F', Key.End),
                ((CsiLetterKeyCode)'P', Key.F1),
                ((CsiLetterKeyCode)'Q', Key.F2),
                ((CsiLetterKeyCode)'R', Key.F3),
                ((CsiLetterKeyCode)'S', Key.F4),
                ((CsiLetterKeyCode)'Z', Key.Tab),
                ((CsiLetterKeyCode)'E', Key.Clear)
            ]);

        /// <summary>
        ///     Legacy CSI tilde key numbers
        /// </summary>
        private static readonly FlagTranslator<CsiTildeKeyCode, Key>
            CsiTildeKeyFlagTranslator = new([
                ((CsiTildeKeyCode)1, Key.Home),
                ((CsiTildeKeyCode)2, Key.Insert),
                ((CsiTildeKeyCode)3, Key.Delete),
                ((CsiTildeKeyCode)4, Key.End),
                ((CsiTildeKeyCode)5, Key.PageUp),
                ((CsiTildeKeyCode)6, Key.PageDown),
                ((CsiTildeKeyCode)15, Key.F5),
                ((CsiTildeKeyCode)17, Key.F6),
                ((CsiTildeKeyCode)18, Key.F7),
                ((CsiTildeKeyCode)19, Key.F8),
                ((CsiTildeKeyCode)20, Key.F9),
                ((CsiTildeKeyCode)21, Key.F10),
                ((CsiTildeKeyCode)23, Key.F11),
                ((CsiTildeKeyCode)24, Key.F12)
            ]);

        private bool _isKittyKeyboardEnabled;

        private void TryToSupportKitty()
        {
            if (QueryIsKittyKeyboardProtocol())
            {
                WriteText(Esc.EnableKittyKeyboard);
                _isKittyKeyboardEnabled = true;

                Capabilities |= ConsoleCapabilities.SupportsAltSolo;

                // Kitty terminals support SGR mouse tracking directly
                // Enable it even if ncurses couldn't set it up
                if (!Capabilities.HasFlag(ConsoleCapabilities.SupportsMouseMove))
                {
                    WriteText(Esc.EnableAllMouseEvents);
                    WriteText(Esc.EnableExtendedMouseTracking);
                    Capabilities |= ConsoleCapabilities.SupportsMouseButtons | ConsoleCapabilities.SupportsMouseMove;
                    DetectSupportsMouseCursor();
                }
            }

            return;

            bool QueryIsKittyKeyboardProtocol()
            {
                if (IsTtyTerminal())
                    return false;

                // todo: this detetion is written by Claude Sonnet. In my opinion this is complete shitcode which can collapse.
                // todo: also we timeout-driven approach we can fail so easily in general. From this todo lets propose a variable for kitty detection to kitty developers
                try
                {
                    WriteText(Esc.QueryKittyKeyboardFlags);
                    WriteText("\u001b[c"); // sentinel: Device Attributes query
                    Flush(); // the queries must actually reach the terminal, otherwise it never responds

                    Curses.timeout(100);

                    var response = new StringBuilder();
                    while (response.Length < 64)
                    {
                        int code = Curses.get_wch(out int wch);
                        if (code == Curses.ERR)
                            break; // timed out

                        if (code != Curses.KEY_CODE_YES)
                            response.Append((char)wch);

                        string collected = response.ToString();
                        if (KittySupportAnswerRegex().IsMatch(collected))
                            return true;

                        // Sentinel (Device Attributes) response arrived without a preceding
                        // kitty keyboard response -> protocol is not supported.
                        if (KittyDeviceAttributesAnswerRegex().IsMatch(collected))
                            break;
                    }

                    return false;
                }
                finally
                {
                    Curses.timeout(NoInputTimeout);
                }
            }
        }

        private IEnumerable<IMatcher<(int, int)>> TryGetKittyMatchers()
        {
            if (!_isKittyKeyboardEnabled)
                yield break;

            // CSI sequences (CSI u, CSI letter, CSI tilde)
            yield return new SafeLockMatcher(
                new CsiKeyboardMatcher<int>(HandleCsiKeyboardEvent, cp => new Rune(cp)), 0, 0, 0);

            // SGR extended mouse sequences (ESC [ &lt; button ; x ; y M/m)
            yield return new SafeLockMatcher(
                new SgrMouseMatcher<int>(HandleSgrMouseEvent, cp => new Rune(cp)), 0, 0, 0);
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
            var rawModifiers = RawInputModifiers.None;
            if ((modifierValue & 1) != 0) rawModifiers |= RawInputModifiers.Shift;
            if ((modifierValue & 2) != 0) rawModifiers |= RawInputModifiers.Alt;
            if ((modifierValue & 4) != 0) rawModifiers |= RawInputModifiers.Control;

            // Try to map the keycode based on terminator type
            Key key;
            char character = char.MinValue;

            if (terminator != 'u' && terminator != '~' &&
                CsiLetterKeyFlagTranslator.Translate((CsiLetterKeyCode)terminator, true) is var letterKey &&
                letterKey != Key.None)
            {
                // Legacy CSI letter sequence (arrows, Home, End, F1-F4)
                key = letterKey;
                if (terminator == 'Z') rawModifiers |= RawInputModifiers.Shift;
            }
            else
            {
                switch (terminator)
                {
                    case '~' when
                        CsiTildeKeyFlagTranslator.Translate((CsiTildeKeyCode)keyCode, true) is var tildeKey &&
                        tildeKey != Key.None:
                        // Legacy CSI tilde sequence (Insert, Delete, PgUp, PgDn, F5-F12)
                        key = tildeKey;
                        break;
                    case 'u' when
                        KittyKeyFlagTranslator.Translate((KittyKeyCode)keyCode, true) is var mappedKey &&
                        mappedKey != Key.None:
                    {
                        key = mappedKey;
                        character = keyCode switch
                        {
                            13 => '\r',
                            9 => '\t',
                            127 => '\b',
                            27 => '\x1B',
                            _ => character
                        };
                        break;
                    }
                    case 'u' when keyCode is >= 32 and < 127:
                    {
                        character = (char)keyCode;
                        switch (keyCode)
                        {
                            case >= 'a' and <= 'z':
                                key = Key.A + (keyCode - 'a');
                                break;
                            case >= 'A' and <= 'Z':
                                key = Key.A + (keyCode - 'A');
                                rawModifiers |= RawInputModifiers.Shift;
                                break;
                            case >= '0' and <= '9':
                                key = Key.D0 + (keyCode - '0');
                                break;
                            case ' ':
                                key = Key.Space;
                                break;
                            default:
                                key = (char)keyCode switch
                                {
                                    '.' => Key.OemPeriod,
                                    ',' => Key.OemComma,
                                    ';' => Key.OemSemicolon,
                                    '/' => Key.Oem2,
                                    '\\' => Key.Oem5,
                                    '=' => Key.OemPlus,
                                    '-' => Key.OemMinus,
                                    '[' => Key.Oem4,
                                    ']' => Key.Oem6,
                                    '\'' => Key.Oem7,
                                    '`' => Key.Oem3,
                                    _ => Key.None
                                };
                                break;
                        }

                        break;
                    }
                    default:
                        key = ConsoloniaPlatform.RaiseNotSupported<Key>(NotSupportedRequestCode.InputNotSupported,
                            csiEvent);
                        break;
                }
            }

            RaiseKeyPress(key, character, rawModifiers, isDown, (ulong)Environment.TickCount64);
        }

        private void HandleSgrMouseEvent((int button, int x, int y, bool isRelease) mouseEvent)
        {
            const double velocity = 1;
            // SGR mouse coordinates are 1-based
            var point = new Point(mouseEvent.x - 1, mouseEvent.y - 1);

            int buttonCode = mouseEvent.button;

            var modifiers = RawInputModifiers.None;
            if ((buttonCode & 4) != 0) modifiers |= RawInputModifiers.Shift;
            if ((buttonCode & 8) != 0) modifiers |= RawInputModifiers.Alt;
            if ((buttonCode & 16) != 0) modifiers |= RawInputModifiers.Control;

            // Determine event type
            int buttonIndex = buttonCode & 3;
            bool isMotion = (buttonCode & 32) != 0;
            bool isWheel = (buttonCode & 64) != 0;

            if (isWheel)
            {
                double delta = buttonIndex == 0 ? velocity : -velocity;
                RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, delta), modifiers);
            }
            else if (isMotion)
            {
                // Add button modifier for drag
                RawInputModifiers buttonModifier = buttonIndex switch
                {
                    0 => RawInputModifiers.LeftMouseButton,
                    1 => RawInputModifiers.MiddleMouseButton,
                    2 => RawInputModifiers.RightMouseButton,
                    _ => RawInputModifiers.None
                };

                RaiseMouseEvent(RawPointerEventType.Move, point, null, modifiers | buttonModifier | _moveModifers);
            }
            else if (mouseEvent.isRelease)
            {
                RawInputModifiers buttonModifier = buttonIndex switch
                {
                    0 => RawInputModifiers.LeftMouseButton,
                    1 => RawInputModifiers.MiddleMouseButton,
                    2 => RawInputModifiers.RightMouseButton,
                    _ => RawInputModifiers.None
                };
                RawPointerEventType eventType = buttonIndex switch
                {
                    0 => RawPointerEventType.LeftButtonUp,
                    1 => RawPointerEventType.MiddleButtonUp,
                    2 => RawPointerEventType.RightButtonUp,
                    _ => RawPointerEventType.LeftButtonUp
                };

                _moveModifers = RawInputModifiers.None;
                RaiseMouseEvent(eventType, point, null, modifiers | buttonModifier);
            }
            else
            {
                // Button press
                RawInputModifiers buttonModifier = buttonIndex switch
                {
                    0 => RawInputModifiers.LeftMouseButton,
                    1 => RawInputModifiers.MiddleMouseButton,
                    2 => RawInputModifiers.RightMouseButton,
                    _ => RawInputModifiers.None
                };
                RawPointerEventType eventType = buttonIndex switch
                {
                    0 => RawPointerEventType.LeftButtonDown,
                    1 => RawPointerEventType.MiddleButtonDown,
                    2 => RawPointerEventType.RightButtonDown,
                    _ => RawPointerEventType.LeftButtonDown
                };

                _moveModifers = modifiers | buttonModifier;
                RaiseMouseEvent(eventType, point, null, modifiers | buttonModifier);
            }
        }

        [GeneratedRegex(@"\x1b\[\?[0-9]*u")]
        private static partial Regex KittySupportAnswerRegex();

        [GeneratedRegex(@"\x1b\[\?[0-9;]*c")]
        private static partial Regex KittyDeviceAttributesAnswerRegex();

        #region FlagTranslatorAllowers

        private enum KittyKeyCode;

        private enum CsiLetterKeyCode;

        private enum CsiTildeKeyCode;

        #endregion
    }
}