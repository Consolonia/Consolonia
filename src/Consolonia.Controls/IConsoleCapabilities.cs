using System;

namespace Consolonia.Controls
{
    [Flags]
    public enum ConsoleCapabilities
    {
        None = 0,

        /// <summary>
        ///     Supports detection of Alt key by itself
        /// </summary>
        SupportsAltSolo = 0x01,

        /// <summary>
        ///     Console supports general mouse input
        /// </summary>
        SupportsMouse = 0x02,

        /// <summary>
        ///     Console supports mouse move input
        /// </summary>
        SupportsMouseMove = 0x04,

        /// <summary>
        ///     Console environment supports mouse cursor (for example GUI terminal emulator has GUI cursor)
        /// </summary>
        SupportsMouseCursor = 0x08,

        /// <summary>
        ///     Supports complex composite emoji rendering
        /// </summary>
        SupportsComplexEmoji = 0x10
    }

    public interface IConsoleCapabilities
    {
        /// <summary>
        ///     Console Capabilities
        /// </summary>
        ConsoleCapabilities Capabilities { get; }
    }
}