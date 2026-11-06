using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     How <see cref="AnsiConsoleOutput" /> shows the cells of the pixel buffer that carry a
    ///     <see cref="CellImage" />, for one image protocol. The pixel buffer only says "this cell shows
    ///     these pixels, under this paint"; what reaches the terminal for it, per cell as it is written and
    ///     per frame once every changed cell has been, is the encoder's.
    /// </summary>
    internal interface IConsoleImageEncoder
    {
        /// <summary>
        ///     A frame starts, inside the output's <see cref="IConsoleOutput.BeginUpdate" />. An encoder that
        ///     gathers the frame's cells starts with none, whatever an earlier frame that threw left behind.
        /// </summary>
        void BeginUpdate()
        {
        }

        /// <summary>
        ///     A changed image cell. The render target writes only the cells that differ from what the
        ///     terminal shows, so these are exactly the cells of the frame whose picture has to reach it:
        ///     written now, or gathered for <see cref="EndUpdate" />.
        /// </summary>
        void WriteImagePixel(PixelBufferCoordinate position, in Pixel pixel);

        /// <summary>
        ///     Every changed cell has been written; <paramref name="screen" /> is what the terminal should
        ///     now show, image cells or not. The protocol's per-frame work goes here, inside the output's
        ///     <see cref="IConsoleOutput.EndUpdate" />, before the update is closed.
        /// </summary>
        void EndUpdate(PixelBuffer screen);

        /// <summary>
        ///     The render target no longer trusts what the terminal shows and will write every cell again:
        ///     whatever the encoder believes is on screen is not.
        /// </summary>
        void Reset();
    }
}