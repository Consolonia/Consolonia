using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Draws the pictures of the pixel buffer's image cells, those carrying a <see cref="PixelImage" />,
    ///     for one image protocol. <see cref="AnsiConsoleOutput" /> writes the cells' text as the frame goes;
    ///     once it has, the encoder makes the terminal's pictures match the screen, from what it remembers
    ///     sending. An encoder comes from the <see cref="IConsoleImageEncoderFactory" /> registered with
    ///     Avalonia's locator and writes through the <see cref="IConsoleOutput" /> it is created for.
    /// </summary>
    public interface IConsoleImageEncoder
    {
        /// <summary>
        ///     Whether the protocol draws its pictures below the text, so text can show over them (kitty
        ///     graphics), rather than in place of the cells it covers (sixel). The output writes a glyph over
        ///     a picture on the terminal's default background when it can, so the picture shows through, and
        ///     on the picture's color when it cannot; a cell with no glyph is a space on the default
        ///     background when it can, and is left to the picture when it cannot.
        /// </summary>
        bool SupportsTextOverlay { get; }

        /// <summary>
        ///     Every changed cell's text has been written; <paramref name="screen" /> is what the terminal
        ///     should now show. The encoder draws, moves or removes its pictures to match it, sending only
        ///     what differs from what it sent before. Called inside the output's
        ///     <see cref="IConsoleOutput.EndUpdate" />, before the update is closed; the output puts the
        ///     cursor back afterwards.
        /// </summary>
        void WriteImages(PixelBuffer screen);

        /// <summary>
        ///     The terminal no longer shows what the encoder sent (the screen was resized, another program
        ///     had the terminal, the console was restored): forget it, so the next
        ///     <see cref="WriteImages" /> sends every picture again. Writes nothing.
        /// </summary>
        void Reset();

        /// <summary>
        ///     The application is giving the terminal back: whatever the terminal still holds for the
        ///     encoder (stored images) is freed here, before the alternate screen is left. The output calls
        ///     <see cref="Reset" /> afterwards.
        /// </summary>
        void RestoreConsole()
        {
        }
    }
}