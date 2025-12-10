//This file is partly copypaste (idea is very same) from:
//
// Driver.cs: Curses-based Driver
//
// Authors:
//   Miguel de Icaza (miguel@gnome.org)
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Logging;
using Avalonia.Threading;
using Consolonia.Core.Helpers;
using Consolonia.Core.Helpers.InputProcessing;
using Consolonia.Core.Helpers.Logging;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;
using Consolonia.Core.Text;
using Unix.Terminal;
using Key = Terminal.Gui.Key;
using KeyModifiers = Terminal.Gui.KeyModifiers;

namespace Consolonia.PlatformSupport
{
    public class CursesConsole : ConsoleBase
    {
        private static readonly FlagTranslator<Key, RawInputModifiers>
            KeyModifiersFlagTranslator = new([
                (Key.ShiftMask, RawInputModifiers.Shift),
                (Key.AltMask, RawInputModifiers.Alt),
                (Key.CtrlMask, RawInputModifiers.Control),
                (Key.BackTab, RawInputModifiers.Shift)
            ]);

        private static readonly FlagTranslator<Key, ConsoleKey>
            KeyFlagTranslator = new([
                (Key.BackTab, ConsoleKey.Tab),
                (Key.Backspace, ConsoleKey.Backspace),
                (Key.CursorDown, ConsoleKey.DownArrow),
                (Key.CursorLeft, ConsoleKey.LeftArrow),
                (Key.CursorRight, ConsoleKey.RightArrow),
                (Key.CursorUp, ConsoleKey.UpArrow),
                (Key.DeleteChar, ConsoleKey.Delete),
                (Key.Delete, ConsoleKey.Delete),
                (Key.End, ConsoleKey.End),
                (Key.Enter, ConsoleKey.Enter),
                (Key.Esc, ConsoleKey.Escape),
                (Key.F1, ConsoleKey.F1),
                (Key.F2, ConsoleKey.F2),
                (Key.F3, ConsoleKey.F3),
                (Key.F4, ConsoleKey.F4),
                (Key.F5, ConsoleKey.F5),
                (Key.F6, ConsoleKey.F6),
                (Key.F7, ConsoleKey.F7),
                (Key.F8, ConsoleKey.F8),
                (Key.F9, ConsoleKey.F9),
                (Key.F10, ConsoleKey.F10),
                (Key.F11, ConsoleKey.F11),
                (Key.F12, ConsoleKey.F12),
                (Key.Home, ConsoleKey.Home),
                (Key.InsertChar, ConsoleKey.Insert),
                (Key.PageDown, ConsoleKey.PageDown),
                (Key.PageUp, ConsoleKey.PageUp),
                (Key.Space, ConsoleKey.Spacebar),
                (Key.Tab, ConsoleKey.Tab),
                (Key.BackTab,
                    ConsoleKey.Tab), // backtab somehow contains SHIFT mask which does not deduct // todo: check why
                // Proposed by ChatGPT, I've found supporting source: https://devblogs.microsoft.com/dotnet/console-readkey-improvements-in-net-7/   
                (Key.Unknown, ConsoleKey.NoName),
                ((Key)46, ConsoleKey.OemPeriod),
                // rest is by chatGPT
                ((Key)44, ConsoleKey.OemComma),
                ((Key)59, ConsoleKey.Oem1),
                ((Key)47, ConsoleKey.Oem2), // Oem2 usually represents the '/' key
                ((Key)92, ConsoleKey.Oem5), // Oem5 usually represents the '\\' key
                ((Key)61, ConsoleKey.OemPlus), // OemPlus might be used for both '=' and '+'
                ((Key)45, ConsoleKey.OemMinus),
                ((Key)91, ConsoleKey.Oem4), // '[' key
                ((Key)93, ConsoleKey.Oem6), // ']' key
                ((Key)39, ConsoleKey.Oem7) // '\'' key
            ]);

        private static readonly FlagTranslator<Curses.Event, RawInputModifiers>
            MouseModifiersFlagTranslator = new([
                (Curses.Event.ButtonAlt, RawInputModifiers.Alt),
                (Curses.Event.ButtonCtrl, RawInputModifiers.Control),
                (Curses.Event.ButtonShift, RawInputModifiers.Shift),
                (Curses.Event.Button1Pressed, RawInputModifiers.LeftMouseButton),
                (Curses.Event.Button1Released, RawInputModifiers.LeftMouseButton),
                (Curses.Event.Button2Pressed, RawInputModifiers.MiddleMouseButton),
                (Curses.Event.Button2Released, RawInputModifiers.MiddleMouseButton),
                (Curses.Event.Button3Pressed, RawInputModifiers.RightMouseButton),
                (Curses.Event.Button3Released, RawInputModifiers.RightMouseButton),
                (Curses.Event.Button4Pressed, RawInputModifiers.XButton1MouseButton),
                (Curses.Event.Button4Released, RawInputModifiers.XButton1MouseButton)
            ]);

        private static readonly FlagTranslator<Curses.Event, RawPointerEventType>
            RawPointerEventTypeFlagTranslator = new([
                (Curses.Event.Button1Pressed, RawPointerEventType.LeftButtonDown),
                (Curses.Event.Button1Released, RawPointerEventType.LeftButtonUp),
                (Curses.Event.Button2Pressed, RawPointerEventType.MiddleButtonDown),
                (Curses.Event.Button2Released, RawPointerEventType.MiddleButtonUp),
                (Curses.Event.Button3Pressed, RawPointerEventType.RightButtonDown),
                (Curses.Event.Button3Released, RawPointerEventType.RightButtonUp),
                (Curses.Event.Button4Pressed, RawPointerEventType.XButton1Down),
                (Curses.Event.Button4Released, RawPointerEventType.XButton1Up),
                (Curses.Event.ReportMousePosition, RawPointerEventType.Move),
                (Curses.Event.ButtonWheeledUp, RawPointerEventType.Wheel),
                (Curses.Event.ButtonWheeledDown, RawPointerEventType.Wheel)
            ]);

        private static readonly FlagTranslator<RawPointerEventType, EventClass>
            EventClassFlagTranslator = new([
                (RawPointerEventType.Move, EventClass.Move),
                (RawPointerEventType.LeftButtonDown, EventClass.ButtonDown),
                (RawPointerEventType.MiddleButtonDown, EventClass.ButtonDown),
                (RawPointerEventType.RightButtonDown, EventClass.ButtonDown),
                (RawPointerEventType.LeftButtonUp, EventClass.ButtonUp),
                (RawPointerEventType.MiddleButtonUp, EventClass.ButtonUp),
                (RawPointerEventType.RightButtonUp, EventClass.ButtonUp),
                (RawPointerEventType.Wheel, EventClass.Wheel)
            ]);

        private readonly FastBuffer<(int, int)> _inputBuffer;
        private readonly InputProcessor<(int, int)> _inputProcessor;
        private readonly ParametrizedLogger _verboseLogger = Log.CreateInputLogger(LogEventLevel.Verbose);

        private Curses.Window _cursesWindow;

        private GpmMonitor _gpmMonitor;

        private KeyModifiers _keyModifiers; // todo: it's left from GUI.cs, we should remove this

        private RawInputModifiers _moveModifers = RawInputModifiers.None;

        private bool _supportsMouse;

        private bool _supportsMouseMove;

        private bool _showMouseCursor;

        // ReSharper disable UnusedMember.Local
        [Flags]
        private enum EventClass
        {
            None = 0,
            Move = 1,
            ButtonDown = 2,
            ButtonUp = 4,
            Wheel = 8,
            All = Move | ButtonDown | ButtonUp | Wheel
        }
        // ReSharper enable UnusedMember.Local

        public CursesConsole()
            : base(new AnsiConsoleOutput())
        {
            _inputBuffer = new FastBuffer<(int, int)>(ReadInputFunction);
            _inputProcessor = new InputProcessor<(int, int)>(GetMatchers());
            StartSizeCheckTimerAsync(2500);
            StartEventLoop();
        }

        public override bool SupportsAltSolo => false;
        public override bool SupportsMouse => _supportsMouse;
        public override bool SupportsMouseMove => _supportsMouseMove;
        public override bool ShowMouseCursor => _showMouseCursor;

        private void StartEventLoop()
        {
            // ReSharper disable VirtualMemberCallInConstructor
            PrepareConsole();

            Task _ = Task.Run(async () =>
            {
                await Helper.WaitDispatcherInitialized();

                _inputBuffer.StartReading();

                while (!Disposed)
                    try
                    {
                        (int, int)[] inputs = _inputBuffer.Dequeue();
                        await DispatchInputAsync(() =>
                        {
                            _keyModifiers = new KeyModifiers();
                            _inputProcessor.ProcessChunk(inputs);
                        });
                    }
                    catch (Exception exception)
                    {
                        Dispatcher.UIThread.Post(
                            () => throw new ConsoloniaException("Exception in input processing loop", exception),
                            DispatcherPriority.MaxValue);
                    }
            });
        }

        private readonly List<(int code, int wch)> _rowInputBuffer = new(1000); //todo: low magic number

        /// <summary>
        ///     https://github.com/gui-cs/Terminal.Gui/blob/v2_develop/Terminal.Gui/ConsoleDrivers/CursesDriver/CursesDriver.cs#L790
        /// </summary>
        private const int NoInputTimeout = 10;

        private (int, int)[] ReadInputFunction()
        {
            _rowInputBuffer.Clear();
            do
            {
                Task pauseTask = PauseTask;
                pauseTask?.Wait();

                int code = Curses.get_wch(out int wch);
                if (code != Curses.ERR)
                {
                    _rowInputBuffer.Add((code, wch));
                    //check if was escape, wait for one more escape

                    if (code != Curses.KEY_CODE_YES && wch == 27)
                    {
                        int code2 = Curses.get_wch(out int wch2);

                        // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                        if (code2 == Curses.ERR)
                        {
                            // it looks like first one was solo ESCAPE and terminal does send escape of escape
                            // Hence, simulating it
                            _rowInputBuffer.Add((code, wch));
                            break;
                        }

                        _rowInputBuffer.Add((code2, wch2));
                    }
                }
                else
                {
                    if (_rowInputBuffer.Count == 0)
                        continue;

                    break;
                }
            } while (!Disposed);

            return [.. _rowInputBuffer];
        }

        public override void PrepareConsole()
        {
            _cursesWindow = Curses.initscr();
            Curses.raw();
            Curses.noecho();
            _cursesWindow.keypad(true);
            Curses.cbreak();

            Curses.Event mouseMask = Curses.mousemask(
                Curses.Event.Button1Pressed | Curses.Event.Button1Released |
                Curses.Event.Button2Pressed | Curses.Event.Button2Released |
                Curses.Event.Button3Pressed | Curses.Event.Button3Released |
                Curses.Event.Button4Pressed | Curses.Event.Button4Released |
                Curses.Event.ButtonWheeledUp | Curses.Event.ButtonWheeledDown |
                Curses.Event.ButtonShift | Curses.Event.ButtonCtrl | Curses.Event.ButtonAlt |
                Curses.Event.ReportMousePosition,
                out Curses.Event _);

            string term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
            bool dumbTerminals = term.StartsWith("linux", StringComparison.OrdinalIgnoreCase) ||
                                 term.StartsWith("vt100", StringComparison.OrdinalIgnoreCase) ||
                                 term.Equals("dumb", StringComparison.OrdinalIgnoreCase);
            _supportsMouse = mouseMask != 0;
            _supportsMouseMove = mouseMask.HasFlag(Curses.Event.ReportMousePosition) &&
                                 DoesCursesActuallySupportMouseMove() &&
                                 !dumbTerminals;

            Curses.mouseinterval(0); // if we don't do this mouse events are dropped
            
            // DISPLAY will have for X11/Wayland virtual terminal GUI Sessions.
            _showMouseCursor = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

            if (_supportsMouseMove)
            {
                // old ncurses messes up with this
                WriteText(Esc.EnableAllMouseEvents);
                WriteText(Esc.EnableExtendedMouseTracking);
            }
            else
            {
                try
                {
                    // reset mousemask or we compete for events.
                    Curses.mousemask(0, out _);

                    // use GPM to get mouse move events
                    _gpmMonitor = new GpmMonitor();
                    _gpmMonitor.MouseEvent += RaiseMouseEvent;
                    _supportsMouseMove = true;
                }
                catch (DllNotFoundException)
                {
                    // ignore
                }
                catch (EntryPointNotFoundException)
                {
                    // ignore
                }
            }

            Curses.timeout(NoInputTimeout);
            WriteText(Esc.EnableBracketedPasteMode);

            base.PrepareConsole();
        }

        public override void RestoreConsole()
        {
            base.RestoreConsole();

            WriteText(Esc.DisableAllMouseEvents);
            WriteText(Esc.DisableExtendedMouseTracking);
            WriteText(Esc.DisableBracketedPasteMode);
            Flush();
            Curses.mousemask(0, out Curses.Event _);
            Curses.nocbreak();
            _cursesWindow.keypad(false);
            Curses.echo();
            Curses.noraw();
            Curses.endwin();
        }

        /// <summary>
        ///     Bug workaround https://lists.gnu.org/archive/html/bug-ncurses/2022-04/msg00020.html
        /// </summary>
        private static bool DoesCursesActuallySupportMouseMove()
        {
            string cursesVersion = Curses.curses_version();
            if (string.IsNullOrEmpty(cursesVersion) ||
                !cursesVersion.ToUpperInvariant().Contains("NCURSES")) // bug is known to exist in ncurses only
                return true;

            string versionOnly = cursesVersion[(cursesVersion.LastIndexOf(' ') + 1)..];

            return Version.Parse(versionOnly) >= Version.Parse("6.4");
        }

        public override void PauseIO(Task task)
        {
            base.PauseIO(task);
            Curses.unget_wch((int)Key.Unknown);
        }

        private IEnumerable<IMatcher<(int, int)>> GetMatchers()
        {
            // PASTE block
            yield return new SafeLockMatcher(
                new PasteBlockMatcher<int>(buffer => { RaiseTextInput(buffer, (ulong)Environment.TickCount64); },
                    cp => new Rune(cp)), 0, 0, 0);

            (string, Key)[] fSequences =
            [
                // Ctrl+Alt+(F1 - F4)
                (@"\x1B[1;7P", Key.CtrlMask | Key.AltMask | MapCursesKey(80 + 185)),
                (@"\x1B[1;7Q", Key.CtrlMask | Key.AltMask | MapCursesKey(81 + 185)),
                (@"\x1B[1;7R", Key.CtrlMask | Key.AltMask | MapCursesKey(82 + 185)),
                (@"\x1B[1;7S", Key.CtrlMask | Key.AltMask | MapCursesKey(83 + 185)),
                // Ctrl+Alt+(F5 - F8)
                (@"\x1B[53;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(53 + 216)),
                (@"\x1B[54;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(55 + 215)),
                (@"\x1B[55;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(56 + 215)),
                (@"\x1B[56;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(57 + 215)),
                // Ctrl+Alt+(F9 - F12)
                (@"\x1B[48;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(48 + 225)),
                (@"\x1B[49;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(49 + 225)),
                (@"\x1B[50;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(50 + 225)),
                (@"\x1B[51;7~", Key.CtrlMask | Key.AltMask | MapCursesKey(51 + 225)),
                // Ctrl+Shift+Alt+(F1 - F4)
                (@"\x1B[1;8;7P", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(80 + 185)),
                (@"\x1B[1;8;7Q", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(81 + 185)),
                (@"\x1B[1;8;7R", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(82 + 185)),
                (@"\x1B[1;8;7S", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(83 + 185)),
                // Ctrl+Shift+Alt+(F5 - F8)
                (@"\x1B[53;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(53 + 216)),
                (@"\x1B[54;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(55 + 215)),
                (@"\x1B[55;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(56 + 215)),
                (@"\x1B[56;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(57 + 215)),
                // Ctrl+Shift+Alt+(F9 - F12)
                (@"\x1B[48;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(48 + 225)),
                (@"\x1B[49;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(49 + 225)),
                (@"\x1B[50;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(50 + 225)),
                (@"\x1B[51;8;7~", Key.CtrlMask | Key.ShiftMask | Key.AltMask | MapCursesKey(51 + 225)),
                // Shift+Alt+(F4)
                (@"\x1B[1;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(268)),
                // Shift+Alt+(F5 - F8)
                (@"\x1B[53;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(53 + 216)),
                (@"\x1B[54;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(55 + 215)),
                (@"\x1B[55;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(56 + 215)),
                (@"\x1B[56;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(57 + 215)),
                // Shift+Alt+(F9 - F12)
                (@"\x1B[48;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(48 + 225)),
                (@"\x1B[49;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(49 + 225)),
                (@"\x1B[50;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(50 + 225)),
                (@"\x1B[51;6~", Key.ShiftMask | Key.AltMask | MapCursesKey(51 + 225)),
                // Shift+Ctrl+Alt+KeyNPage
                (@"\x1B[54;6~", Key.ShiftMask | Key.CtrlMask | Key.AltMask | Key.PageDown),
                // Shift+Ctrl+Alt+KeyPPage
                (@"\x1B[53;6~", Key.ShiftMask | Key.CtrlMask | Key.AltMask | Key.PageUp),
                // Shift+Ctrl+Alt+KeyHome
                (@"\x1B[1;6H", Key.ShiftMask | Key.CtrlMask | Key.AltMask | Key.Home),
                // Shift+Ctrl+Alt+KeyEnd
                (@"\x1B[1;6F", Key.ShiftMask | Key.CtrlMask | Key.AltMask | Key.End)
            ];

            foreach ((string, Key) fSequence in fSequences)
                yield return new SafeLockMatcher(
                    new StartsEndsWithMatcher<int>(_ => { RaiseKeyPressInternal(fSequence.Item2); }, cp => new Rune(cp),
                        fSequence.Item1, fSequence.Item1), 0, 0, 0);

            // escape of ESC
            yield return new SafeLockMatcher(
                new RegexMatcher<int>(_ => { RaiseKeyPressInternal(Key.Esc); }, cp => new Rune(cp), @"^\x1B+$", 2), 0,
                0);

            // SHIFT+TAB is received as ESC then TAB, both locked by key 0: https://unix.stackexchange.com/a/238412
            yield return new SafeLockMatcher(
                new RegexMatcher<int>(_ => { RaiseKeyPressInternal(Key.BackTab); }, cp => new Rune(cp), @"^\x1B\t?$",
                    2), 0, 0);

            // The ESC-number handling, debatable.
            yield return new SafeLockMatcher(new RegexMatcher<int>(tuple =>
            {
                var k = Key.Unknown;
                // Simulates the AltMask itself by pressing Alt + Space.
                int wch = tuple.Item2[0];
                int wch2 = tuple.Item2[1];

                if (wch2 == (int)Key.Space)
                {
                    k = Key.AltMask;
                }
                else if (wch2 - (int)Key.Space >= (uint)Key.A && wch2 - (int)Key.Space <= (uint)Key.Z)
                {
                    k = (Key)((uint)Key.AltMask + (wch2 - (int)Key.Space));
                }
                else if (wch2 >= (uint)Key.A - 64 && wch2 <= (uint)Key.Z - 64)
                {
                    k = (Key)((uint)(Key.AltMask | Key.CtrlMask) + (wch2 + 64));
                }
                else if (wch2 >= (uint)Key.D0 && wch2 <= (uint)Key.D9)
                {
                    k = (Key)((uint)Key.AltMask + (uint)Key.D0 + (wch2 - (uint)Key.D0));
                }
                else
                {
                    // Unfortunately there are no way to differentiate Ctrl+Alt+alfa and Ctrl+Shift+Alt+alfa.
                    if (((Key)wch2 & Key.CtrlMask) != 0) _keyModifiers.Ctrl = true;

                    if (wch2 == 0)
                    {
                        k = Key.CtrlMask | Key.AltMask | Key.Space;
                    }
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse todo: check why
                    else if (wch >= (uint)Key.A && wch <= (uint)Key.Z)
                    {
                        _keyModifiers.Shift = true;
                        _keyModifiers.Alt = true;
                    }
                    else if (wch2 < 256)
                    {
                        k = (Key)wch2;
                        _keyModifiers.Alt = true;
                    }
                    else
                    {
                        k = (Key)((uint)(Key.AltMask | Key.CtrlMask) + wch2);
                    }
                }

                RaiseKeyPressInternal(k);
            }, cp => new Rune(cp), @"^\x1B[^\x1B\[]*$", 2), 0, 0);

            // alt mask
            yield return new SafeLockMatcher(new RegexMatcher<int>(tuple =>
            {
                int wch = tuple.Item2[0];
                Key k = Key.AltMask | MapCursesKey(wch);
                RaiseKeyPressInternal(k);
            }, cp => new Rune(cp), @"^\x1B[^\x00]*$", 2), 0, Curses.KEY_CODE_YES);

            // mouse and resize detection and some special processing
            yield return new SafeLockMatcher(new GenericMatcher<int>(wch =>
            {
                switch (wch)
                {
                    case Curses.KeyResize when Curses.CheckWinChange():
                        CheckSize();
                        return;
                    case Curses.KeyMouse:
                        if (Curses.getmouse(out Curses.MouseEvent ev) == 0)
                        {
                            _verboseLogger.Log2(
                                $"Mouse Event: {ev.ID} - {string.Join(" ", ev.ButtonState.GetFlags())}");
                            HandleMouseInput(ev);
                        }

                        return;
                }

                Key k = MapCursesKey(wch);

                switch (wch)
                {
                    case >= 277 and <= 288:
                        // Shift+(F1 - F12)
                        wch -= 12;
                        k = Key.ShiftMask | MapCursesKey(wch);
                        break;
                    case >= 289 and <= 300:
                        // Ctrl+(F1 - F12)
                        wch -= 24;
                        k = Key.CtrlMask | MapCursesKey(wch);
                        break;
                    case >= 301 and <= 312:
                        // Ctrl+Shift+(F1 - F12)
                        wch -= 36;
                        k = Key.CtrlMask | Key.ShiftMask | MapCursesKey(wch);
                        break;
                    case >= 313 and <= 324:
                        // Alt+(F1 - F12)
                        wch -= 48;
                        k = Key.AltMask | MapCursesKey(wch);
                        break;
                    case >= 325 and <= 327:
                        // Shift+Alt+(F1 - F3)
                        wch -= 60;
                        k = Key.ShiftMask | Key.AltMask | MapCursesKey(wch);
                        break;
                    case >= 523 and <= 570:
                        // Ctrl/Shift/Alt and navigation keys (arrow, home, end)
                        string distro = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
                        if (!string.IsNullOrEmpty(distro))
                            wch -= 1;
                        else
                            wch -= 9;
                        k = MapCursesKey(wch); // has appropriate XxxMask internal
                        break;
                }

                RaiseKeyPressInternal(k);
            }), Curses.KEY_CODE_YES);

            // text detection
            var textInputMatcher = new SafeLockMatcher(new TextInputMatcher<int>(tuple =>
            {
                CanBeHandledEventArgs canBeHandledEventArgs = new();
                RaiseTextInput(tuple.Item1, (ulong)Environment.TickCount64, canBeHandledEventArgs);
                bool processSeparateKeys = !canBeHandledEventArgs.Handled;

                if (processSeparateKeys)
                    foreach (int key in tuple.Item2)
                        ProcessKeyInternal(key);
            }, cp => new Rune(cp), 10 /* todo: low: magic number here*/), 0);
            yield return textInputMatcher;

            // general keys backup
            yield return new SafeLockMatcher(
                new GenericMatcher<int>(i =>
                {
                    ProcessKeyInternal(i);
                    // related to 15F2A2C4-218D-4B4D-86CE-330A312EF6A6, we have to reset text input ourselves, because 
                    // common logic of InputProcessor resets only everything below
                    // todo: should we reset all other matchers actually?
                    textInputMatcher.Reset();
                }, EscapeDoesNotComeItselfInCurses), 0);

            yield break;

            static bool EscapeDoesNotComeItselfInCurses(int i)
            {
                return i != 27;
            }
        }

        private void ProcessKeyInternal(int wch)
        {
            Key k;
            if (wch == Curses.KeyTab)
            {
                k = MapCursesKey(wch);
            }
            else
            {
                // Unfortunately there are no way to differentiate Ctrl+alfa and Ctrl+Shift+alfa.
                k = (Key)wch;
                if (wch == 0)
                {
                    k = Key.CtrlMask | Key.Space;
                }
                else if (wch >= (uint)Key.A - 64 && wch <= (uint)Key.Z - 64)
                {
                    if ((Key)(wch + 64) != Key.J) k = Key.CtrlMask | (Key)(wch + 64);
                }
                else if (wch >= (uint)Key.A && wch <= (uint)Key.Z)
                {
                    _keyModifiers.Shift = true;
                }
            }

            RaiseKeyPressInternal(k);
        }

        private void RaiseKeyPressInternal(Key key)
        {
            int keyValue = (int)key;
            RawInputModifiers modifiers = KeyModifiersFlagTranslator.Translate(key);

            if (_keyModifiers.Alt) modifiers |= RawInputModifiers.Alt;
            if (_keyModifiers.Ctrl) modifiers |= RawInputModifiers.Control;
            if (_keyModifiers.Shift) modifiers |= RawInputModifiers.Shift;

            key = key & ~Key.CtrlMask & ~Key.ShiftMask & ~Key.AltMask;

            char character;
            ConsoleKey consoleKey =
                KeyFlagTranslator.Translate(key, true);

            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (consoleKey)
            {
                case ConsoleKey.NoName:
                    return;
                case 0
                    when
                    Enum.IsDefined(
                        key) /*because we want string representation only when defined, we don't want numeric value*/:
                {
                    bool _ = Enum.TryParse(key.ToString(), true, out consoleKey);
                    break;
                }
            }

            if (((uint)keyValue & (uint)Key.CharMask) > 27)
            {
                character = (char)keyValue;
            }
            else
            {
                if (consoleKey == default)
                    throw new NotImplementedException();
                character = char.MinValue;
                if (char.IsUpper(character))
                    modifiers |= RawInputModifiers.Shift;
            }

            Avalonia.Input.Key convertToKey = DefaultNetConsole.ConvertToKey(consoleKey);

            RaiseKeyPress(convertToKey,
                character, modifiers, true, (ulong)Environment.TickCount64);
            Thread.Yield();
            RaiseKeyPress(convertToKey,
                character, modifiers, false, (ulong)Environment.TickCount64);
            Thread.Yield();
        }

        private void HandleMouseInput(Curses.MouseEvent ev)
        {
            if (_gpmMonitor != null)
                // when GPM is used, we should ignore all mouse events from curses
                return;

            const double velocity = 1;

            var point = new Point(ev.X, ev.Y);
            RawInputModifiers modifiers = MouseModifiersFlagTranslator.Translate(ev.ButtonState);
            RawPointerEventType rawPointerEventType = RawPointerEventTypeFlagTranslator.Translate(ev.ButtonState);

            //stop monitor event type we are getting from curses
            EventClass eventClass = EventClassFlagTranslator.Translate(rawPointerEventType, true);
            if (eventClass == EventClass.ButtonDown)
                _moveModifers = modifiers;
            else if (eventClass == EventClass.ButtonUp) _moveModifers = RawInputModifiers.None;

            if (ev.ButtonState.HasFlag(Curses.Event.ButtonWheeledDown))
            {
                RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, -velocity),
                    modifiers);
            }
            else if (ev.ButtonState.HasFlag(Curses.Event.ButtonWheeledUp))
            {
                RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, velocity),
                    modifiers);
            }
            else if (rawPointerEventType == RawPointerEventType.Move)
            {
                RaiseMouseEvent(RawPointerEventType.Move, point, null,
                    _moveModifers | modifiers);
            }
            else if (rawPointerEventType == RawPointerEventType.XButton1Down ||
                     rawPointerEventType == RawPointerEventType.XButton1Up)
            {
                // ignore
            }
            else
            {
                RaiseMouseEvent(rawPointerEventType, point, null, modifiers);
            }
        }

        private static Key MapCursesKey(int cursesKey)
        {
            return cursesKey switch
            {
                Curses.KeyF1 => Key.F1,
                Curses.KeyF2 => Key.F2,
                Curses.KeyF3 => Key.F3,
                Curses.KeyF4 => Key.F4,
                Curses.KeyF5 => Key.F5,
                Curses.KeyF6 => Key.F6,
                Curses.KeyF7 => Key.F7,
                Curses.KeyF8 => Key.F8,
                Curses.KeyF9 => Key.F9,
                Curses.KeyF10 => Key.F10,
                Curses.KeyF11 => Key.F11,
                Curses.KeyF12 => Key.F12,
                Curses.KeyUp => Key.CursorUp,
                Curses.KeyDown => Key.CursorDown,
                Curses.KeyLeft => Key.CursorLeft,
                Curses.KeyRight => Key.CursorRight,
                Curses.KeyHome => Key.Home,
                Curses.KeyEnd => Key.End,
                Curses.KeyNPage => Key.PageDown,
                Curses.KeyPPage => Key.PageUp,
                Curses.KeyDeleteChar => Key.DeleteChar,
                Curses.KeyInsertChar => Key.InsertChar,
                Curses.KeyTab => Key.Tab,
                Curses.KeyBackTab => Key.BackTab,
                Curses.KeyBackspace => Key.Backspace,
                Curses.ShiftKeyUp => Key.CursorUp | Key.ShiftMask,
                Curses.ShiftKeyDown => Key.CursorDown | Key.ShiftMask,
                Curses.ShiftKeyLeft => Key.CursorLeft | Key.ShiftMask,
                Curses.ShiftKeyRight => Key.CursorRight | Key.ShiftMask,
                Curses.ShiftKeyHome => Key.Home | Key.ShiftMask,
                Curses.ShiftKeyEnd => Key.End | Key.ShiftMask,
                Curses.ShiftKeyNPage => Key.PageDown | Key.ShiftMask,
                Curses.ShiftKeyPPage => Key.PageUp | Key.ShiftMask,
                Curses.AltKeyUp => Key.CursorUp | Key.AltMask,
                Curses.AltKeyDown => Key.CursorDown | Key.AltMask,
                Curses.AltKeyLeft => Key.CursorLeft | Key.AltMask,
                Curses.AltKeyRight => Key.CursorRight | Key.AltMask,
                Curses.AltKeyHome => Key.Home | Key.AltMask,
                Curses.AltKeyEnd => Key.End | Key.AltMask,
                Curses.AltKeyNPage => Key.PageDown | Key.AltMask,
                Curses.AltKeyPPage => Key.PageUp | Key.AltMask,
                Curses.CtrlKeyUp => Key.CursorUp | Key.CtrlMask,
                Curses.CtrlKeyDown => Key.CursorDown | Key.CtrlMask,
                Curses.CtrlKeyLeft => Key.CursorLeft | Key.CtrlMask,
                Curses.CtrlKeyRight => Key.CursorRight | Key.CtrlMask,
                Curses.CtrlKeyHome => Key.Home | Key.CtrlMask,
                Curses.CtrlKeyEnd => Key.End | Key.CtrlMask,
                Curses.CtrlKeyNPage => Key.PageDown | Key.CtrlMask,
                Curses.CtrlKeyPPage => Key.PageUp | Key.CtrlMask,
                Curses.ShiftCtrlKeyUp => Key.CursorUp | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyDown => Key.CursorDown | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyLeft => Key.CursorLeft | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyRight => Key.CursorRight | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyHome => Key.Home | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyEnd => Key.End | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyNPage => Key.PageDown | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftCtrlKeyPPage => Key.PageUp | Key.ShiftMask | Key.CtrlMask,
                Curses.ShiftAltKeyUp => Key.CursorUp | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyDown => Key.CursorDown | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyLeft => Key.CursorLeft | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyRight => Key.CursorRight | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyNPage => Key.PageDown | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyPPage => Key.PageUp | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyHome => Key.Home | Key.ShiftMask | Key.AltMask,
                Curses.ShiftAltKeyEnd => Key.End | Key.ShiftMask | Key.AltMask,
                Curses.AltCtrlKeyNPage => Key.PageDown | Key.AltMask | Key.CtrlMask,
                Curses.AltCtrlKeyPPage => Key.PageUp | Key.AltMask | Key.CtrlMask,
                Curses.AltCtrlKeyHome => Key.Home | Key.AltMask | Key.CtrlMask,
                Curses.AltCtrlKeyEnd => Key.End | Key.AltMask | Key.CtrlMask,
                _ => Key.Unknown
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inputBuffer.Dispose();

            base.Dispose(disposing);
        }
    }
}