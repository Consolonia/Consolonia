using System;

namespace Consolonia.Controls
{
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
    [Flags]
    public enum ConsoleCapabilities
#pragma warning restore CA2217 // Do not mark enums with FlagsAttribute
    {
        None = 0,

        /// <summary>
        ///     Console supports mouse input buttons
        /// </summary>
        SupportsMouseButtons = 0x01,

        /// <summary>
        ///     Console supports mouse move input
        /// </summary>
        SupportsMouseMove = SupportsMouseButtons | 0x02,

        /// <summary>
        ///     Console environment supports mouse cursor (for example GUI terminal emulator has GUI cursor)
        /// </summary>
        SupportsMouseCursor = SupportsMouseMove | 0x04,

        /// <summary>
        ///     Supports detection of Alt key by itself
        /// </summary>
        SupportsAltSolo = 0x10,

        /// <summary>
        ///     Supports complex composite emoji rendering
        /// </summary>
        SupportsComplexEmoji = 0x20
    }

    public interface IConsoleCapabilities
    {
        /// <summary>
        ///     Console Capabilities
        /// </summary>
        ConsoleCapabilities Capabilities { get; }
    }
}