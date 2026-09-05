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
        SupportsComplexEmoji = 0x20,

        /// <summary>
        ///     Supports sixel graphics output
        /// </summary>
        SupportsSixel = 0x40,

        /// <summary>
        ///     Supports the kitty graphics protocol
        /// </summary>
        SupportsKittyGraphics = 0x80,

        /// <summary>
        ///     Supports synchronized output (DEC private mode 2026), letting a whole frame be applied atomically
        /// </summary>
        SupportsSynchronizedOutput = 0x100
    }

    public interface IConsoleCapabilities
    {
        /// <summary>
        ///     Console Capabilities
        /// </summary>
        ConsoleCapabilities Capabilities { get; }

        int CellPixelWidth { get; }
        int CellPixelHeight { get; }
    }
}