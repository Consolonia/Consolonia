using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;
using Terminal.Gui;
using Key = Avalonia.Input.Key;
using Point = Avalonia.Point;
using static Vanara.PInvoke.Kernel32;

// ReSharper disable UnusedMember.Local
#pragma warning disable CS0649

namespace Consolonia.PlatformSupport
{
    [SupportedOSPlatform("windows")]
    public class Win32Console : ConsoleBase
    {
        private static readonly FlagTranslator<CONTROL_KEY_STATE, RawInputModifiers>
            KeyModifiersTranslator = new(
            [
                (CONTROL_KEY_STATE.SHIFT_PRESSED, RawInputModifiers.Shift),
                (CONTROL_KEY_STATE.LEFT_ALT_PRESSED, RawInputModifiers.Alt),
                (CONTROL_KEY_STATE.RIGHT_ALT_PRESSED, RawInputModifiers.Alt),
                (CONTROL_KEY_STATE.LEFT_CTRL_PRESSED, RawInputModifiers.Control),
                (CONTROL_KEY_STATE.RIGHT_CTRL_PRESSED, RawInputModifiers.Control)
            ]);

        private static readonly FlagTranslator<MOUSE_BUTTON_STATE, RawInputModifiers>
            MouseModifiersTranslator = new(
            [
                (MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED, RawInputModifiers.LeftMouseButton),
                (MOUSE_BUTTON_STATE.RIGHTMOST_BUTTON_PRESSED, RawInputModifiers.RightMouseButton),
                (MOUSE_BUTTON_STATE.FROM_LEFT_2ND_BUTTON_PRESSED, RawInputModifiers.MiddleMouseButton),
                (MOUSE_BUTTON_STATE.FROM_LEFT_3RD_BUTTON_PRESSED, RawInputModifiers.XButton1MouseButton),
                (MOUSE_BUTTON_STATE.FROM_LEFT_4TH_BUTTON_PRESSED, RawInputModifiers.XButton2MouseButton)
            ]);


        private static readonly FlagTranslator<MOUSE_BUTTON_STATE, RawPointerEventType>
            MouseButtonDownEventTypeTranslator = new(
            [
                (MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED, RawPointerEventType.LeftButtonDown),
                (MOUSE_BUTTON_STATE.RIGHTMOST_BUTTON_PRESSED, RawPointerEventType.RightButtonDown),
                (MOUSE_BUTTON_STATE.FROM_LEFT_2ND_BUTTON_PRESSED, RawPointerEventType.MiddleButtonDown),
                (MOUSE_BUTTON_STATE.FROM_LEFT_3RD_BUTTON_PRESSED, RawPointerEventType.XButton1Down),
                (MOUSE_BUTTON_STATE.FROM_LEFT_4TH_BUTTON_PRESSED, RawPointerEventType.XButton2Down)
            ]);

        private static readonly FlagTranslator<MOUSE_BUTTON_STATE, RawPointerEventType>
            MouseButtonUpEventTypeTranslator = new(
            [
                (MOUSE_BUTTON_STATE.FROM_LEFT_1ST_BUTTON_PRESSED, RawPointerEventType.LeftButtonUp),
                (MOUSE_BUTTON_STATE.RIGHTMOST_BUTTON_PRESSED, RawPointerEventType.RightButtonUp),
                (MOUSE_BUTTON_STATE.FROM_LEFT_2ND_BUTTON_PRESSED, RawPointerEventType.MiddleButtonUp),
                (MOUSE_BUTTON_STATE.FROM_LEFT_3RD_BUTTON_PRESSED, RawPointerEventType.XButton1Up),
                (MOUSE_BUTTON_STATE.FROM_LEFT_4TH_BUTTON_PRESSED, RawPointerEventType.XButton2Up)
            ]);


        private readonly WindowsConsole _windowsConsole;

        private MOUSE_BUTTON_STATE _mouseButtonsState = MOUSE_BUTTON_STATE.NONE;

        public Win32Console(IConsoleOutput console)
            : base(console)
        {
            _windowsConsole = new WindowsConsole();

            // ReSharper disable VirtualMemberCallInConstructor
            PrepareConsole();
            
            // we will have a consolewindow if we are running in GUI context.
            Capabilities |= ConsoleCapabilities.SupportsMouseButtons |
                            ConsoleCapabilities.SupportsMouseMove |
                            ConsoleCapabilities.SupportsAltSolo;
            if (GetConsoleWindow() != IntPtr.Zero)
                Capabilities |= ConsoleCapabilities.SupportsMouseCursor;

            StartEventLoop();
        }

        public override void PauseIO(Task task)
        {
            base.PauseIO(task);

            var inputRecords = new INPUT_RECORD[1];

            // Create a focus event
            inputRecords[0].EventType = EVENT_TYPE.FOCUS_EVENT; // FOCUS_EVENT
            inputRecords[0].Event.FocusEvent = new FOCUS_EVENT_RECORD
            {
                bSetFocus = true
            };

            // ReSharper disable once InvertIf
            if (!WriteConsoleInput(_windowsConsole.InputHandle, inputRecords, 1, out uint _))
            {
                // Handle error
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"Error writing console input: {error}");
            }
        }

        private void StartEventLoop()
        {
            Task.Run(async () =>
            {
                await Helper.WaitDispatcherInitialized();

                while (!Disposed /*inject ThreadAbortException*/)
                {
                    PauseTask?.Wait();
                    INPUT_RECORD[] inputRecords = _windowsConsole.ReadConsoleInput();
                    var clipboard = AvaloniaLocator.Current.GetService<IClipboard>();
                    if (clipboard != null &&
                        inputRecords.Where(evt => evt.EventType == EVENT_TYPE.KEY_EVENT).Skip(1).Any())
                        // when console is translating CTRL+V to sequence of key strokes it comes in as multiple key events.
                        await ProcessClipboardInputAsync(clipboard, inputRecords);
                    else
                        await DispatchInputAsync(() =>
                        {
                            foreach (INPUT_RECORD inputRecord in inputRecords)
                                HandleInputRecord(inputRecord);
                        });
                }
            });
        }

        /// <summary>
        ///     Process clipboard input and compare to clipboard text to determine if we should paste clipboard text.
        /// </summary>
        /// <param name="clipboard"></param>
        /// <param name="inputRecords"></param>
        /// <returns></returns>
        private async Task ProcessClipboardInputAsync(IClipboard clipboard, INPUT_RECORD[] inputRecords)
        {
            string clipboardText = await clipboard?.TryGetTextAsync() ?? string.Empty;
            if (clipboardText.Trim().Length == 0)
            {
                // no text in clipboard, just process input records
                await DispatchInputAsync(() =>
                {
                    foreach (INPUT_RECORD inputRecord in inputRecords)
                        HandleInputRecord(inputRecord);
                });
                return;
            }

            // KEY_EVENTS will emit \r instead of \n, so we need to remove \n from clipboard text
            clipboardText = clipboardText.Replace("\n", string.Empty, StringComparison.Ordinal);
            var bufferText = new StringBuilder();
            List<INPUT_RECORD> bufferedKeyEvents = [];

            bool breakTheLoop = false;
            while (inputRecords.Any() && !breakTheLoop)
            {
                // process all input records

                await DispatchInputAsync(() =>
                {
                    for (int i = 0; i < inputRecords.Length; i++)
                    {
                        INPUT_RECORD inputRecord = inputRecords[i];
                        if (inputRecord.EventType != EVENT_TYPE.KEY_EVENT)
                        {
                            // handle non-key board events
                            HandleInputRecord(inputRecord);
                        }
                        else
                        {
                            // capture the key event so we can play it back if we don't match clipboard text
                            bufferedKeyEvents.Add(inputRecord);

                            // for key down events for chars that are not 0 (control keys)
                            if (inputRecord.Event.KeyEvent.bKeyDown && inputRecord.Event.KeyEvent.uChar != 0)
                            {
                                // append the char to the buffer text
                                bufferText.Append(inputRecord.Event.KeyEvent.uChar);

                                string currentBufferText = bufferText.ToString();
                                if (clipboardText.Trim() == currentBufferText.Trim())
                                {
                                    // buffered text matches clipboard, emit CTRL+V sequence and ignore buffered keyboard events
                                    //foreach (KEY_EVENT_RECORD ctrlVEvent in CtrlVKeyEvents)
                                    //    HandleKeyInput(ctrlVEvent);
                                    RaiseTextInput(currentBufferText,
                                        (ulong)Environment.TickCount64);

                                    // process remaining input records
                                    for (++i; i < inputRecords.Length; i++)
                                        HandleInputRecord(inputRecords[i]);

                                    breakTheLoop = true;
                                    return;
                                }

                                if (!clipboardText.StartsWith(currentBufferText, StringComparison.Ordinal))
                                {
                                    // buffered text doesn't match clipboard, emit buffered key events (we already played other events live)
                                    foreach (INPUT_RECORD bufferedEvent in bufferedKeyEvents)
                                        HandleInputRecord(bufferedEvent);

                                    // process remaining input records
                                    for (++i; i < inputRecords.Length; i++)
                                        HandleInputRecord(inputRecords[i]);

                                    breakTheLoop = true;
                                    return;
                                }
                            }
                        }
                    }
                });

                inputRecords = _windowsConsole.ReadConsoleInput();
            }
        }

        private void HandleInputRecord(INPUT_RECORD inputRecord)
        {
            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
            switch (inputRecord.EventType)
            {
                case EVENT_TYPE.WINDOW_BUFFER_SIZE_EVENT:
                    WINDOW_BUFFER_SIZE_RECORD windowBufferSize = inputRecord.Event.WindowBufferSizeEvent;
                    Size = new PixelBufferSize((ushort)windowBufferSize.dwSize.X,
                        (ushort)windowBufferSize.dwSize.Y);
                    break;
                case EVENT_TYPE.FOCUS_EVENT:
                    FOCUS_EVENT_RECORD focusEvent = inputRecord.Event.FocusEvent;
                    RaiseFocusEvent(focusEvent.bSetFocus != 0);
                    break;
                case EVENT_TYPE.KEY_EVENT:
                    HandleKeyInput(inputRecord.Event.KeyEvent);
                    break;
                case EVENT_TYPE.MOUSE_EVENT:
                    MOUSE_EVENT_RECORD mouseEvent = inputRecord.Event.MouseEvent;
                    HandleMouseInput(mouseEvent);
                    break;
            }
        }

        // ReSharper disable ExpressionIsAlwaysNull

        private void HandleMouseInput(MOUSE_EVENT_RECORD mouseEvent)
        {
            var point = new Point(mouseEvent.dwMousePosition.X, mouseEvent.dwMousePosition.Y);
            RawInputModifiers inputModifiers =
                KeyModifiersTranslator.Translate(mouseEvent.dwControlKeyState) |
                MouseModifiersTranslator.Translate(mouseEvent.dwButtonState);

            var eventType = RawPointerEventType.Move;
            Vector? wheelDelta = null;

            switch (mouseEvent.dwEventFlags)
            {
                case MOUSE_EVENT_FLAG.DOUBLE_CLICK:
                    RawPointerEventType downButtonEvent =
                        MouseButtonDownEventTypeTranslator.Translate(mouseEvent.dwButtonState);
                    // we will already have processed the original click as down/up
                    // so now we just generate down/up again and avalonia
                    // will turn it into a double click internally.
                    RaiseMouseEvent(downButtonEvent,
                        point,
                        wheelDelta,
                        inputModifiers);

                    // the up will be generated by the original click processing
                    break;

                case MOUSE_EVENT_FLAG.MOUSE_MOVED | MOUSE_EVENT_FLAG.DOUBLE_CLICK:
                // Win32 maps MOUSE_DOWN/DRAG => as MOVED|DOUBLE_CLICK 
                // Avalonia wants it to just be MOUSE_DOWN, and then subsequent MOUSE_MOVE events
                case MOUSE_EVENT_FLAG.NONE:
                {
                    bool emitted = false;
                    foreach (MOUSE_BUTTON_STATE flag in Enum.GetValues<MOUSE_BUTTON_STATE>())
                        if (!_mouseButtonsState.HasFlag(flag) && mouseEvent.dwButtonState.HasFlag(flag))
                        {
                            // If we went from flag off to flag on
                            RawPointerEventType buttonDownEventType =
                                MouseButtonDownEventTypeTranslator.Translate(flag);
#pragma warning disable IDE0034
                            // Simplify 'default' expression
                            if (buttonDownEventType != default)
                            {
                                RaiseMouseEvent(buttonDownEventType,
                                    point,
                                    null,
                                    inputModifiers);
                                _mouseButtonsState = mouseEvent.dwButtonState;
                                emitted = true;
                                break;
                            }
#pragma warning restore IDE0034
                            // Simplify 'default' expression
                        }

                        else if (_mouseButtonsState.HasFlag(flag) && !mouseEvent.dwButtonState.HasFlag(flag))
                        {
                            // If we went from flag On to flag off
                            RawPointerEventType buttonEventType = MouseButtonUpEventTypeTranslator.Translate(flag);
#pragma warning disable IDE0034
                            // Simplify 'default' expression
                            if (buttonEventType != default)
                            {
                                RaiseMouseEvent(buttonEventType,
                                    point,
                                    null,
                                    inputModifiers);
                                _mouseButtonsState = mouseEvent.dwButtonState;
                                emitted = true;
                                break;
                            }
#pragma warning restore IDE0034
                            // Simplify 'default' expression
                        }

                    if (!emitted)
                    {
                        // If we didn't emit any button up/down transition events, emit a move event
                        RaiseMouseEvent(eventType,
                            point,
                            wheelDelta,
                            inputModifiers);
                        _mouseButtonsState = mouseEvent.dwButtonState;
                    }
                }
                    break;

                case MOUSE_EVENT_FLAG.MOUSE_WHEELED:
                    double velocity = mouseEvent.dwButtonState < 0 ? -1 : 1;
                    wheelDelta = new Vector(0, velocity);
                    RaiseMouseEvent(RawPointerEventType.Wheel,
                        point,
                        wheelDelta,
                        inputModifiers);
                    break;
                case MOUSE_EVENT_FLAG.MOUSE_HWHEELED:
                    break;
                case MOUSE_EVENT_FLAG.MOUSE_MOVED:
                    RaiseMouseEvent(RawPointerEventType.Move,
                        point,
                        wheelDelta,
                        inputModifiers);
                    _mouseButtonsState = mouseEvent.dwButtonState;
                    break;
                default:
                    throw new InvalidOperationException(mouseEvent.dwEventFlags.ToString());
            }

            _mouseButtonsState = mouseEvent.dwButtonState;
        }

        private void HandleKeyInput(KEY_EVENT_RECORD keyEvent)
        {
            char character = keyEvent.uChar;
            RawInputModifiers modifiers =
                KeyModifiersTranslator.Translate(keyEvent.dwControlKeyState);
            Key key = DefaultNetConsole.ConvertToKey((ConsoleKey)keyEvent.wVirtualKeyCode);
            if (key == Key.LeftAlt || key == Key.RightAlt)
                modifiers |= RawInputModifiers.Alt;
            RaiseKeyPress(key,
                character, modifiers, keyEvent.bKeyDown, (ulong)Environment.TickCount64);
        }
    }
}