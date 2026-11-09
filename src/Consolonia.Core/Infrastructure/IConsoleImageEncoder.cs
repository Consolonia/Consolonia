using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Draws the images of image cells (cells with a <see cref="PixelImage" />) using one image protocol.
    ///     <see cref="AnsiConsoleOutput" /> first writes the text of the changed cells. Then the encoder updates
    ///     the images on the terminal to match the screen. It compares against what it sent before.
    ///     The encoder is created by the <see cref="IConsoleImageEncoderFactory" /> registered with
    ///     Avalonia's locator. It writes through the <see cref="IConsoleOutput" /> it was created for.
    /// </summary>
    public interface IConsoleImageEncoder
    {
        /// <summary>
        ///     True if the protocol draws images below the text, so text can appear over them (kitty graphics).
        ///     False if the image replaces the cells it covers (sixel).
        /// </summary>
        /// <remarks>
        ///     When true, the output writes cells over an image on the terminal's default background, so the
        ///     image shows through. When false, the output writes a glyph on the cell's effective background
        ///     color, and does not write spaces, so the image stays visible.
        /// </remarks>
        bool SupportsTextOverlay { get; }

        /// <summary>
        ///     Draws, moves or removes images so the terminal matches <paramref name="screen" />. Sends only
        ///     what changed since the last call.
        /// </summary>
        /// <remarks>
        ///     The output calls this from <see cref="IConsoleOutput.EndUpdate" />, after the text of every changed
        ///     cell is written and before the synchronized update is closed. The output moves the cursor back
        ///     afterwards.
        /// </remarks>
        /// <param name="screen">What the terminal should show now.</param>
        void WriteImages(PixelBuffer screen);

        /// <summary>
        ///     Forgets everything the encoder sent, because the terminal no longer shows it. This happens when
        ///     the screen is resized, another program used the terminal, or the console is restored. The next
        ///     <see cref="WriteImages" /> sends every image again. This method writes nothing.
        /// </summary>
        void Reset();

        /// <summary>
        ///     Called when the application gives the terminal back. Frees any data the terminal still stores
        ///     for the encoder (such as uploaded images), before the alternate screen is left. The output calls
        ///     <see cref="Reset" /> afterwards.
        /// </summary>
        void RestoreConsole()
        {
        }
    }
}