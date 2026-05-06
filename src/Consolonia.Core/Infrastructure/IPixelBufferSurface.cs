using System;
using Avalonia;
using Avalonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Abstraction for a surface that owns a PixelBuffer for rendering.
    ///     Implemented by ConsoleWindowImpl (main window) and ChildWindowSurface (child windows).
    ///     Returned via IWindowImpl.Surfaces so that RenderTarget and DrawingContextImpl
    ///     can work with any pixel buffer owner without being coupled to ConsoleWindowImpl.
    /// </summary>
    public interface IPixelBufferSurface
    {
        PixelBuffer PixelBuffer { get; }

        Snapshot.Regions DirtyRegions { get; }

        IConsole Console { get; }

        event Action<Size, WindowResizeReason> Resized;

        event Action<ConsoleCursor> CursorChanged;

        event Action ClearScreenRequested;
    }
}
