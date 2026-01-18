// Adapted from GUI.cs (MIT license)

// ReSharper disable All
using System;
using System.Runtime.Versioning;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace Terminal.Gui
{
    [SupportedOSPlatform("windows")]
    internal class WindowsConsole
    {

        internal HFILE InputHandle;
        readonly CONSOLE_INPUT_MODE originalConsoleMode;

        public WindowsConsole()
        {
            InputHandle = GetStdHandle(StdHandleType.STD_INPUT_HANDLE);
            originalConsoleMode = ConsoleMode;
            var newConsoleMode = originalConsoleMode;
            newConsoleMode |= (CONSOLE_INPUT_MODE.ENABLE_MOUSE_INPUT |
                               CONSOLE_INPUT_MODE.ENABLE_EXTENDED_FLAGS);
            newConsoleMode &= ~CONSOLE_INPUT_MODE.ENABLE_QUICK_EDIT_MODE;
            newConsoleMode &= ~CONSOLE_INPUT_MODE.ENABLE_PROCESSED_INPUT;
            // Harden input mode: disable line/echo (this fixes CTRL+S freezes)
            newConsoleMode &= ~CONSOLE_INPUT_MODE.ENABLE_LINE_INPUT;
            newConsoleMode &= ~CONSOLE_INPUT_MODE.ENABLE_ECHO_INPUT; 
            //newConsoleMode |= CONSOLE_INPUT_MODE.ENABLE_VIRTUAL_TERMINAL_INPUT;
            ConsoleMode = newConsoleMode;
        }

        public CONSOLE_INPUT_MODE ConsoleMode
        {
            get
            {
                if (!GetConsoleMode(InputHandle, out CONSOLE_INPUT_MODE v))
                    throw GetLastError().GetException();
                return v;
            }
            set
            {
                if (!SetConsoleMode(InputHandle, value))
                    throw GetLastError().GetException();
            }
        }

        const int bufferSize = 0xffff;
        private static INPUT_RECORD[] s_inputBuffer = new INPUT_RECORD[bufferSize];

        public INPUT_RECORD[] ReadConsoleInput()
        {
            if (!Kernel32.ReadConsoleInput(InputHandle, s_inputBuffer, bufferSize,
                out var numberEventsRead))
                throw GetLastError().GetException();
            INPUT_RECORD[] recordsToReturn = new INPUT_RECORD[numberEventsRead];
            Array.Copy(s_inputBuffer, recordsToReturn, numberEventsRead);
            return recordsToReturn;
        }
    }
}