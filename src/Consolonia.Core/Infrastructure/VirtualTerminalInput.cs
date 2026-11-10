using System;
using System.Runtime.InteropServices;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Turns on virtual terminal input on Windows while waiting for a reply from the terminal, so the
    ///     reply arrives unchanged.
    /// </summary>
    /// <remarks>
    ///     Without it, conhost/ConPTY turns the terminal's escape sequence replies into key events. CSI and DCS
    ///     replies keep all their characters, so cursor position and cell size queries work. But an APC reply
    ///     (the kitty graphics query uses APC) is dropped up to its final escape. Only the trailing backslash
    ///     remains. ENABLE_VIRTUAL_TERMINAL_INPUT passes the reply through unchanged.
    ///     Keep it on only while waiting for a reply, because the input loop needs key events, not raw sequences.
    /// </remarks>
    internal static class VirtualTerminalInput
    {
        private const int StdInputHandle = -10;
        private const uint EnableVirtualTerminalInputFlag = 0x0200;

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetConsoleMode(nint handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool SetConsoleMode(nint handle, uint mode);

        /// <summary>
        ///     Enables virtual terminal input. Returns a scope that restores the previous console mode when
        ///     disposed. If the mode cannot or should not be changed, the returned scope does nothing. This
        ///     happens on other operating systems, with redirected input, when the console refuses the change,
        ///     or when the mode is already enabled.
        /// </summary>
        public static Scope Enable()
        {
            if (!OperatingSystem.IsWindows())
                return default;

            nint handle = GetStdHandle(StdInputHandle);
            if (handle == 0 || handle == -1)
                return default;
            if (!GetConsoleMode(handle, out uint mode))
                return default;

            // Someone else already enabled it, so we must not turn it off later.
            if ((mode & EnableVirtualTerminalInputFlag) != 0)
                return default;

            return SetConsoleMode(handle, mode | EnableVirtualTerminalInputFlag)
                ? new Scope(handle, mode)
                : default;
        }

        /// <summary>
        ///     Restores the previous console mode when disposed, if this scope changed it.
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            private readonly nint _handle;
            private readonly uint _mode;
            private readonly bool _restore;

            internal Scope(nint handle, uint mode)
            {
                _handle = handle;
                _mode = mode;
                _restore = true;
            }

            public void Dispose()
            {
                if (_restore)
                    SetConsoleMode(_handle, _mode);
            }
        }
    }
}