using System;
using System.Runtime.InteropServices;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Turns on the Windows console's pass-through of terminal replies, for as long as one is expected.
    /// </summary>
    /// <remarks>
    ///     Without it, conhost/ConPTY reads the escape sequences arriving from the terminal and hands the
    ///     program key events it made out of them. CSI and DCS replies survive that translation as their
    ///     own characters, so cursor position and cell size queries have always worked. An APC reply,
    ///     which is what the kitty graphics query is answered with, does not: it is swallowed up to the
    ///     escape that ends it, and all the program receives is the leftover backslash. With
    ///     ENABLE_VIRTUAL_TERMINAL_INPUT set the reply passes through as raw characters instead.
    ///     Held only for the round trip: the input loop wants the console's reading of an arrow key,
    ///     not the three characters the terminal actually sent.
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

            // Already on is not this code's doing, so it is not this code's to turn off again.
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
