using System;
using System.Runtime.InteropServices;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Turns on the Windows console's pass-through of terminal replies, for as long as one is expected.
    /// </summary>
    /// <remarks>
    ///     Without it, conhost/ConPTY turns the terminal's escape sequence replies into key events. CSI and DCS
    ///     replies survive as their own characters (so cursor position and cell size queries work), but an APC
    ///     reply, which is how the kitty graphics query is answered, is swallowed up to its terminating escape,
    ///     leaving only the trailing backslash. ENABLE_VIRTUAL_TERMINAL_INPUT passes the reply through raw.
    ///     Held only for the round trip, because the input loop wants key events, not raw sequences.
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
        ///     Enables virtual terminal input, returning a scope which restores the previous console
        ///     mode when disposed. Every case which cannot or must not change the mode (another
        ///     operating system, redirected input, a console that refused, the mode already enabled
        ///     by someone else) returns a scope which restores nothing.
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

            // already enabled by someone else, so not ours to turn off again
            if ((mode & EnableVirtualTerminalInputFlag) != 0)
                return default;

            return SetConsoleMode(handle, mode | EnableVirtualTerminalInputFlag)
                ? new Scope(handle, mode)
                : default;
        }

        /// <summary>
        ///     Puts back whatever console mode was there before, if this scope changed it.
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