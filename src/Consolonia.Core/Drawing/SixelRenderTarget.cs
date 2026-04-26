#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Renders to a Skia offscreen bitmap and outputs the frame as a Sixel image.
    /// </summary>
    internal class SixelRenderTarget : IDrawingContextLayerImpl
    {
        private readonly ConsoleWindowImpl _consoleWindow;
        private readonly IConsoleOutput _console;
        private readonly IPlatformRenderInterface _fallbackRenderInterface;
        private readonly IPlatformRenderInterfaceContext _fallbackContext;
        private readonly int _cellPixelWidth;
        private readonly int _cellPixelHeight;
        private IDrawingContextLayerImpl _innerTarget;

        internal SixelRenderTarget(
            ConsoleWindowImpl consoleWindow,
            IDrawingContextLayerImpl innerTarget,
            IConsoleOutput console,
            IPlatformRenderInterface fallbackRenderInterface,
            IPlatformRenderInterfaceContext fallbackContext,
            int cellPixelWidth,
            int cellPixelHeight)
        {
            _consoleWindow = consoleWindow;
            _innerTarget = innerTarget;
            _console = console;
            _fallbackRenderInterface = fallbackRenderInterface;
            _fallbackContext = fallbackContext;
            _cellPixelWidth = cellPixelWidth;
            _cellPixelHeight = cellPixelHeight;
            _consoleWindow.Resized += OnResized;
        }

        public Vector Dpi => _innerTarget.Dpi;
        public PixelSize PixelSize => _innerTarget.PixelSize;
        public int Version => _innerTarget.Version;
        public bool IsCorrupted => _innerTarget.IsCorrupted;
        bool IDrawingContextLayerImpl.CanBlit => true;

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
        {
            IDrawingContextImpl innerCtx = _innerTarget.CreateDrawingContext(useScaledDrawing);
            return new SixelDrawingContext(innerCtx, this);
        }

        void IDrawingContextLayerImpl.Blit(IDrawingContextImpl context)
        {
            // In Avalonia 11's composition renderer, Blit is only called on layers,
            // not on the top-level render target. Frame output is triggered from
            // SixelDrawingContext.Dispose() instead.
        }

        public void Save(string fileName, int? quality = null) => _innerTarget.Save(fileName, quality);

        public void Save(Stream stream, int? quality = null) => _innerTarget.Save(stream, quality);

        public void Dispose()
        {
            _consoleWindow.Resized -= OnResized;
            _innerTarget.Dispose();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void RenderToDevice()
        {
            _console.HideCaret();

            IReadableBitmapImpl? readableBitmap = GetReadableBitmap();
            if (readableBitmap != null)
            {
                using ILockedFramebuffer framebuffer = readableBitmap.Lock();

                int width = framebuffer.Size.Width;
                int height = framebuffer.Size.Height;
                int stride = framebuffer.RowBytes;

                // Convert framebuffer to BGRX byte array
                byte[] bgrx = new byte[width * height * 4];
                unsafe
                {
                    byte* src = (byte*)framebuffer.Address;
                    for (int y = 0; y < height; y++)
                    {
                        var srcSpan = new ReadOnlySpan<byte>(src + y * stride, width * 4);
                        srcSpan.CopyTo(bgrx.AsSpan(y * width * 4, width * 4));
                    }
                }

                // Dispose the loaded bitmap if we created one (not the inner target itself)
                if (readableBitmap != _innerTarget)
                    ((IDisposable)readableBitmap).Dispose();

                // Create a full-frame Sixel
                Sixel sixel = Sixel.CreateFromBitmap(bgrx, width, height, _cellPixelWidth, _cellPixelHeight);

                // Position at top-left and write the full sixel frame
                _console.SetCaretPosition(new PixelBufferCoordinate(0, 0));
                _console.WriteSixel(new PixelBufferCoordinate(0, 0), sixel);
            }

            _console.Flush();
        }

        private IReadableBitmapImpl? GetReadableBitmap()
        {
            // Fast path: inner target directly supports pixel readback
            if (_innerTarget is IReadableBitmapImpl readable)
                return readable;

            // Slow path: Skia SurfaceRenderTarget is IBitmapImpl but not IReadableBitmapImpl.
            // Save to PNG stream, then load back via fallback which gives us IReadableBitmapImpl.
            if (_innerTarget is IBitmapImpl)
            {
                var ms = new MemoryStream();
                _innerTarget.Save(ms);
                ms.Position = 0;
                IBitmapImpl loaded = _fallbackRenderInterface.LoadBitmap(ms);
                if (loaded is IReadableBitmapImpl readableLoaded)
                    return readableLoaded;
                loaded.Dispose();
            }

            return null;
        }

        private void OnResized(Size size, WindowResizeReason reason)
        {
            // size is already in pixels (ConsoleWindowImpl reports pixel dimensions in Sixel mode)
            var pixelSize = new PixelSize((int)size.Width, (int)size.Height);

            _innerTarget.Dispose();
            _innerTarget = _fallbackContext.CreateOffscreenRenderTarget(pixelSize, 1.0);
        }
    }
}
