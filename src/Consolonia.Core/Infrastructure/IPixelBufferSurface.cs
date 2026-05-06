using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Abstraction for a surface that owns a PixelBuffer for rendering.
    ///     Implemented by ConsoleWindowImpl (main window) and ManagedWindowImpl (child windows).
    ///     Returned via IWindowImpl.Surfaces so that RenderTarget and DrawingContextImpl
    ///     can work with any pixel buffer owner without being coupled to ConsoleWindowImpl.
    /// </summary>
    public interface IPixelBufferSurface
    {
        PixelBuffer PixelBuffer { get; }

        Snapshot.Regions DirtyRegions { get; }

        IConsole Console { get; }

        /// <summary>
        ///     Screen-space position of this surface's content area for compositing.
        ///     Only meaningful for child surfaces; main window returns (0,0).
        /// </summary>
        PixelPoint Position { get; }

        /// <summary>
        ///     Z-order for compositing. Higher values are drawn on top.
        /// </summary>
        int ZIndex { get; }

        /// <summary>
        ///     Whether this surface is currently active (has keyboard focus).
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        ///     The IWindowImpl.Input callback for routing raw input events to this surface's window.
        /// </summary>
        Action<RawInputEventArgs> InputCallback { get; }

        /// <summary>
        ///     The input root for this surface's window, used when constructing RawInputEventArgs.
        /// </summary>
        IInputRoot InputRoot { get; }

        event Action<Size, WindowResizeReason> Resized;

        event Action<ConsoleCursor> CursorChanged;

        event Action ClearScreenRequested;
    }
}
