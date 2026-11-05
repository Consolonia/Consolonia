using Consolonia.Controls;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Imaging
{
    /// <summary>
    ///     The encoder for the image protocol the console's terminal answered the probe with: kitty graphics
    ///     when it has them, sixel otherwise, none when it has neither.
    /// </summary>
    /// <remarks>
    ///     Kitty first: its pictures are below the text, so text shows over them, and a terminal keeps them
    ///     by id, so an unchanged picture costs nothing to keep on screen.
    /// </remarks>
    public sealed class ConsoleImageEncoderFactory : IConsoleImageEncoderFactory
    {
        public IConsoleImageEncoder CreateEncoder(IConsoleOutput output)
        {
            if (output.Capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics))
                return new KittyConsoleImageEncoder(output);

            if (output.Capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return new SixelConsoleImageEncoder(output);

            return null;
        }
    }
}
