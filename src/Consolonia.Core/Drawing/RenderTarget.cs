#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal class RenderTarget : IDrawingContextLayerImpl
    {
        private readonly ConsoleSurface _consoleSurface;
        private readonly IPixelBufferWindow _window;
        private bool _renderPending;

        internal RenderTarget(IPixelBufferWindow window)
        {
            _window = window;
            _consoleSurface = window.Surface;

            // Only the main window's RenderTarget handles surface events.
            if (window is ConsoleWindowImpl)
            {
                _consoleSurface.Resized += OnResized;
                _consoleSurface.CursorChanged += OnCursorChanged;
            }
        }

        public RenderTarget(IEnumerable<object> surfaces)
            : this(surfaces.OfType<IPixelBufferWindow>().Single())
        {
        }

        public PixelBuffer Buffer => _window.PixelBuffer;

        public void Dispose()
        {
            if (_window is ConsoleWindowImpl)
            {
                _consoleSurface.Resized -= OnResized;
                _consoleSurface.CursorChanged -= OnCursorChanged;
            }
        }

        public void Save(string fileName, int? quality = null) => throw new NotImplementedException();
        public void Save(Stream stream, int? quality = null) => throw new NotImplementedException();
        public Vector Dpi { get; } = Vector.One;
        public PixelSize PixelSize { get; } = new(1, 1);
        public int Version => 0;
        bool IDrawingContextLayerImpl.CanBlit => true;
        public bool IsCorrupted => false;

        void IDrawingContextLayerImpl.Blit(IDrawingContextImpl context)
        {
            if (_window is ConsoleWindowImpl)
            {
                // Main window: flush to console
                try
                {
                    var dirtyRegions = _window.DirtyRegions.GetSnapshotAndClear();
                    _consoleSurface.RenderPixelBuffer(_window.PixelBuffer, dirtyRegions);
                }
                catch (InvalidDrawingContextException)
                {
                }
            }
            else
            {
                // Child rendered to its own PixelBuffer.
                // Invalidate the PixelBufferPresenter so its Render() fires
                // on the next main render tick.
                //if (_window is ContentControl cc && cc.Content is Visual presenter)
                //    presenter.InvalidateVisual();
            }
        }

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
        {
            if (useScaledDrawing)
                throw new NotImplementedException("Consolonia doesn't support useScaledDrawing");
            return new DrawingContextImpl(_window);
        }

        private void OnResized(Size size, WindowResizeReason reason)
        {
            _consoleSurface.ClearScreen();
        }

        private void OnCursorChanged(ConsoleCursor consoleCursor)
        {
            if (!_renderPending)
            {
                _renderPending = true;
                DispatcherTimer.RunOnce(() =>
                {
                    if (_renderPending)
                    {
                        _renderPending = false;
                        var dirtyRegions = _window.DirtyRegions.GetSnapshotAndClear();
                        _consoleSurface.RenderPixelBuffer(_window.PixelBuffer, dirtyRegions);
                    }
                }, TimeSpan.FromMilliseconds(16), DispatcherPriority.UiThreadRender);
            }
        }
    }
}
