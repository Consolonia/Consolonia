using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     A window viewport into a ConsoleSurface. Each window has its own PixelBuffer
    ///     that its visual tree renders to.
    /// </summary>
    public interface IPixelBufferWindow
    {
        ConsoleSurface Surface { get; }

        /// <summary>
        ///     This window's own PixelBuffer. The visual tree renders to this buffer.
        /// </summary>
        PixelBuffer PixelBuffer { get; }

        /// <summary>
        ///     Dirty regions within this window's PixelBuffer.
        /// </summary>
        Snapshot.Regions DirtyRegions { get; }
    }
}
