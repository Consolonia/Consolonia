using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Called around every frame <see cref="RenderTarget" /> writes to the terminal, for work that is
    ///     not per cell. The render target writes every changed cell itself, whatever the cell shows; a
    ///     hook does what its protocol needs besides. Kitty shows its pictures this way: a placement is a
    ///     terminal object below the text, not the content of a cell, so once per frame the tile cells on
    ///     screen are turned into placements.
    /// </summary>
    internal interface IRenderTargetHook
    {
        /// <summary>The frame starts: its dirty cells are about to be written.</summary>
        void BeginFrame();

        /// <summary>Every changed cell of the frame has been written to the console, which is not flushed yet.</summary>
        void EndFrame(PixelBuffer buffer);

        /// <summary>
        ///     The render target no longer trusts what the terminal shows (a resize, or another program had
        ///     the terminal) and writes every cell again this frame. Whatever the hook keeps about the
        ///     terminal's state is forgotten the same way.
        /// </summary>
        void ResetCache();
    }
}
