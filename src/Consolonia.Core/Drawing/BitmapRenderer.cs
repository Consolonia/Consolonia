//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     What a rendered bitmap was rendered for: the picture's version, the size it is drawn at, the part
    ///     of it on screen and how it was scaled.
    /// </summary>
    // The properties are only read through the generated Equals/GetHashCode: together they are a cache key.
    // ReSharper disable NotAccessedPositionalProperty.Global
    internal readonly record struct BitmapQuantizedCacheKey(
        int Version,
        PixelSize TargetSize,
        PixelRect VisibleCells,
        BitmapInterpolationMode InterpolationMode);
    // ReSharper restore NotAccessedPositionalProperty.Global

    /// <summary>
    ///     Strategy for drawing a bitmap into the pixel buffer. A renderer is selected once per drawing context
    ///     based on the console capabilities and owns whatever caching its image protocol needs.
    /// </summary>
    internal abstract class BitmapRenderer
    {
        /// <summary>
        ///     Renderings kept per bitmap. Each visible part is its own rendering, so scrolling a large
        ///     picture makes new ones; the oldest go once there are more than this.
        /// </summary>
        private const int MaxRenderingsPerBitmap = 4;

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
            PixelRect targetRect, PixelRect intersectedRect, BitmapInterpolationMode interpolationMode);

        /// <summary>
        ///     The part of <paramref name="intersectedRect" /> inside <paramref name="targetRect" />, in cells
        ///     relative to the target.
        /// </summary>
        protected static PixelRect VisibleCellsInTarget(PixelRect targetRect, PixelRect intersectedRect)
        {
            return new PixelRect(intersectedRect.X - targetRect.X, intersectedRect.Y - targetRect.Y,
                intersectedRect.Width, intersectedRect.Height);
        }

        /// <summary>
        ///     The pixels <paramref name="source" /> shows in <paramref name="visible" /> when it is drawn at
        ///     <paramref name="targetSize" />, as tightly packed BGRA rows.
        /// </summary>
        /// <remarks>
        ///     Only the visible part is scaled, never the whole picture at the size it is drawn. A picture
        ///     zoomed far past the screen would otherwise be scaled, and for the image protocols sent to the
        ///     terminal, at a size the terminal refuses (XTerm.NET stops at four megapixels), and the whole
        ///     picture would vanish rather than show its visible part.
        /// </remarks>
        internal static byte[] GetVisiblePixels(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelSize targetSize, PixelRect visible, BitmapInterpolationMode interpolationMode)
        {
            var readableSource = (IReadableBitmapImpl)source;
            using ILockedFramebuffer sourceFrame = readableSource.Lock();

            // The pixels actually there. Not source.PixelSize: an AspectRatioAdjustedBitmap reports half
            // its height to layout, and measuring by that put the picture's top half where all of it goes.
            PixelSize sourceSize = sourceFrame.Size;

            // drawn at its own size: the visible part is a plain copy
            if (sourceSize == targetSize)
                return CopyWindow(sourceFrame, visible.X, visible.Y, visible.Width, visible.Height);

            // the source pixels behind the visible part, widened to whole pixels
            double scaleX = (double)sourceSize.Width / targetSize.Width;
            double scaleY = (double)sourceSize.Height / targetSize.Height;
            int left = Math.Clamp((int)Math.Floor(visible.X * scaleX), 0, sourceSize.Width - 1);
            int top = Math.Clamp((int)Math.Floor(visible.Y * scaleY), 0, sourceSize.Height - 1);
            int right = Math.Clamp((int)Math.Ceiling(visible.Right * scaleX), left + 1, sourceSize.Width);
            int bottom = Math.Clamp((int)Math.Ceiling(visible.Bottom * scaleY), top + 1, sourceSize.Height);

            // Scaled as a piece, then cut down to the visible part: widening put the piece's edge a
            // fraction of a pixel outside it, and this is where that fraction comes back off.
            int offsetX = Math.Max(0, (int)Math.Round(visible.X - left / scaleX));
            int offsetY = Math.Max(0, (int)Math.Round(visible.Y - top / scaleY));
            var scaledSize = new PixelSize(
                Math.Max((int)Math.Round((right - left) / scaleX), offsetX + visible.Width),
                Math.Max((int)Math.Round((bottom - top) / scaleY), offsetY + visible.Height));

            int bytesPerPixel = sourceFrame.Format.BitsPerPixel / 8;
            using IBitmapImpl piece = renderInterface.LoadBitmap(sourceFrame.Format,
                readableSource.AlphaFormat ?? AlphaFormat.Premul,
                sourceFrame.Address + top * sourceFrame.RowBytes + left * bytesPerPixel,
                new PixelSize(right - left, bottom - top), sourceFrame.Dpi, sourceFrame.RowBytes);
            using IBitmapImpl scaled = renderInterface.ResizeBitmap(piece, scaledSize, interpolationMode);
            using ILockedFramebuffer scaledFrame = ((IReadableBitmapImpl)scaled).Lock();

            return CopyWindow(scaledFrame, offsetX, offsetY, visible.Width, visible.Height);
        }

        private static unsafe byte[] CopyWindow(ILockedFramebuffer frame, int x, int y, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] window = GC.AllocateUninitializedArray<byte>(rowBytes * height);
            ReadOnlySpan<byte> pixels = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.AsRef<byte>((void*)frame.Address), frame.RowBytes * frame.Size.Height);

            for (int row = 0; row < height; row++)
                pixels.Slice((y + row) * frame.RowBytes + x * 4, rowBytes)
                    .CopyTo(window.AsSpan(row * rowBytes, rowBytes));

            return window;
        }

        /// <summary>
        ///     Returns the rendering cached for <paramref name="key" />, or makes one with
        ///     <paramref name="render" />. Renderings of older versions are dropped, and the oldest of the
        ///     current version once there are too many, each passed to <paramref name="evicted" />.
        /// </summary>
        protected static T GetOrRender<T>(
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<BitmapQuantizedCacheKey, T>>> cache,
            IBitmapImpl source, BitmapQuantizedCacheKey key, Func<T> render, Action<T> evicted = null)
        {
            List<KeyValuePair<BitmapQuantizedCacheKey, T>> renderings =
                cache.GetOrCreateValue(GetCacheBitmapImpl(source));

            foreach (KeyValuePair<BitmapQuantizedCacheKey, T> rendering in renderings)
                if (rendering.Key == key)
                    return rendering.Value;

            // a new version (next animation frame) makes every older version's renderings unreachable
            renderings.RemoveAll(rendering =>
            {
                if (rendering.Key.Version == key.Version)
                    return false;
                evicted?.Invoke(rendering.Value);
                return true;
            });

            while (renderings.Count >= MaxRenderingsPerBitmap)
            {
                evicted?.Invoke(renderings[0].Value);
                renderings.RemoveAt(0);
            }

            T value = render();
            renderings.Add(new KeyValuePair<BitmapQuantizedCacheKey, T>(key, value));
            return value;
        }

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
