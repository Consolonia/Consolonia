using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     A window viewport into a ConsoleSurface. Each window has its own PixelBuffer
    ///     that its visual tree renders to. ConsoleSurface composites all window buffers
    ///     bottom-up into the final frame for console output.
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

        /// <summary>
        ///     Screen-space position of this window's content area.
        ///     Main window returns (0,0). Child windows return the content area offset.
        /// </summary>
        PixelPoint Position { get; }

        Size ContentSize { get; }

        bool IsActive { get; }

        Action<RawInputEventArgs> InputCallback { get; }

        IInputRoot InputRoot { get; }
    }
}
