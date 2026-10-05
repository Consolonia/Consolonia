//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    internal readonly record struct BitmapQuantizedCacheKey(
        int Version,
        PixelSize TargetSize);

    /// <summary>
    ///     Strategy for drawing a bitmap into the pixel buffer. A renderer is selected once per drawing context
    ///     based on the console capabilities and owns whatever caching its image protocol needs.
    /// </summary>
    internal abstract class BitmapRenderer
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
                    if (Context.PixelBuffer[destPoint] == newPixel)
                    {
                        if (dirtyRunStart >= 0)
                        {
                            Context.ConsoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
                                intersectedRect.X + dirtyRunStart,
                                intersectedRect.Y + y,
                                x - dirtyRunStart,
                                1));
                            dirtyRunStart = -1;
                        }

                        continue;
                    }

                    Context.PixelBuffer[destPoint] = newPixel;
                    if (dirtyRunStart < 0)
                        dirtyRunStart = x;
                }

                if (dirtyRunStart >= 0)
                    Context.ConsoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
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