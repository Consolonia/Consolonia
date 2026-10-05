//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Dummy;

namespace Consolonia.Core.Drawing
{
    internal readonly record struct BitmapQuantizedCacheKey(
        int Version,
        PixelSize TargetSize);

    /// <summary>
    ///     Bitmap - drawing implementation
    /// </summary>
    internal partial class DrawingContextImpl
    {
        private BitmapRenderer _bitmapRenderer;

        public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            switch (source)
            {
                case DummyBitmap:
                    return;
                case PixelBufferBitmapImpl pixelBufferBitmap:
                    DrawPixelBufferBitmap(pixelBufferBitmap, sourceRect, destRect);
                    return;
            }

            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;

            var renderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();

            _bitmapRenderer ??= CreateBitmapRenderer();
            _bitmapRenderer.Draw(source, renderInterface, targetRect, intersectedRect);
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Picks the best bitmap renderer the terminal is capable of (capabilities are detected in PrepareConsole).
        /// </summary>
        private BitmapRenderer CreateBitmapRenderer()
        {
            ConsoleCapabilities capabilities = _consoleWindowImpl.Console.Capabilities;

            // rect placements let glyphs composite over the picture; the unicode placeholder mode is
            // kept for hosts where classic placements cannot survive, but nothing selects it today
            if (capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics) &&
                AvaloniaLocator.Current.GetService<IConsoleColorMode>() is RgbConsoleColorMode)
                return new KittyBitmapRenderer(this, true);

            if (capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return new SixelBitmapRenderer(this);

            return new QuadPixelBitmapRenderer(this);
        }

        /// <summary>
        ///     Strategy for drawing a bitmap into the pixel buffer. A renderer is selected once per drawing context
        ///     based on the console capabilities and owns whatever caching its image protocol needs.
        /// </summary>
        private abstract class BitmapRenderer
        {
            protected BitmapRenderer(DrawingContextImpl context)
            {
                Context = context;
            }

            protected DrawingContextImpl Context { get; }

            /// <summary>
            ///     Draws <paramref name="source" /> into the pixel buffer over <paramref name="targetRect" /> (cell
            ///     coordinates), limited to the visible <paramref name="intersectedRect" />, and tracks dirty regions.
            /// </summary>
            public abstract void Draw(IBitmapImpl source, IPlatformRenderInterface renderInterface,
                PixelRect targetRect, PixelRect intersectedRect);

            /// <summary>
            ///     Copies the visible part of a rendered per-cell bitmap into the pixel buffer,
            ///     tracking only the cells that actually changed as dirty (in horizontal runs).
            /// </summary>
            protected void CopyRenderedBitmapTrackingDirtyRegions(PixelBuffer renderedBitmap,
                PixelRect intersectedRect, PixelRect visibleRectInTarget)
            {
                for (int y = 0; y < intersectedRect.Height; y++)
                {
                    int dirtyRunStart = -1;
                    for (int x = 0; x < intersectedRect.Width; x++)
                    {
                        var sourcePoint = new PixelPoint(visibleRectInTarget.X + x, visibleRectInTarget.Y + y);
                        var destPoint = new PixelPoint(intersectedRect.X + x, intersectedRect.Y + y);
                        Pixel newPixel = renderedBitmap[sourcePoint];
                        if (Context._pixelBuffer[destPoint] == newPixel)
                        {
                            if (dirtyRunStart >= 0)
                            {
                                Context._consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
                                    intersectedRect.X + dirtyRunStart,
                                    intersectedRect.Y + y,
                                    x - dirtyRunStart,
                                    1));
                                dirtyRunStart = -1;
                            }

                            continue;
                        }

                        Context._pixelBuffer[destPoint] = newPixel;
                        if (dirtyRunStart < 0)
                            dirtyRunStart = x;
                    }

                    if (dirtyRunStart >= 0)
                        Context._consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
                            intersectedRect.X + dirtyRunStart,
                            intersectedRect.Y + y,
                            intersectedRect.Width - dirtyRunStart,
                            1));
                }
            }

            /// <summary>
            ///     Unwraps the bitmap used as identity for render caches.
            /// </summary>
            protected static IBitmapImpl GetCacheBitmapImpl(IBitmapImpl bitmapImpl)
            {
                return bitmapImpl is AspectRatioAdjustedBitmap adjustedBitmap
                    ? adjustedBitmap.InnerBitmap
                    : bitmapImpl;
            }
        }
    }
}