using Avalonia.Platform;

namespace Consolonia.Core.Infrastructure
{
    public class ConsoloniaPlatformSettings : DefaultPlatformSettings
    {
        //todo: does make sense to move colormode into here?

        /// <summary>
        ///     Whether not supported input (like unrecognized keys) should be ignored, or should throw an exception. True by
        ///     default
        ///     <seealso cref="ConsoloniaPlatform.NotSupported" />
        /// </summary>
        public bool UnsafeInput { get; init; } = true;

        /// <summary>
        ///     Whether not supported rendering (like unsupported shapes, colors, etc.) should be ignored, or should throw an
        ///     exception. True by default
        ///     <seealso cref="ConsoloniaPlatform.NotSupported" />
        /// </summary>
        public bool UnsafeRendering { get; init; } = true;

        /// <summary>
        ///     Milliseconds curses waits after ESC for a key sequence. Null, or a set ESCDELAY, keeps curses' own.
        /// </summary>
        public int? EscDelay { get; init; } = 50;
    }
}