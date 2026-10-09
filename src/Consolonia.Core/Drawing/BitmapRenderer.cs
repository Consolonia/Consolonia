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
    internal readonly record struct RenderingKey(
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
        ///     The part of <paramref name="targetRect" /> on screen, in cells relative to the target: what a
        ///     rendering covers.
        /// </summary>
        /// <remarks>
        ///     The screen, not the clip. A partial redraw (a dialog opening over the picture, say) clips to
        ///     the region being redrawn, and rendering by that clip made a rendering per redraw region. The
        ///     cells outside it still showed the earlier rendering, and once the cache evicted that one, the
        ///     kitty renderer deleted its image and those cells went blank. The screen bounds the size just
        ///     as well, and stays the same however the picture is redrawn.
        /// </remarks>
        protected PixelRect OnScreenCellsInTarget(PixelRect targetRect)
        {
            var screen = new PixelRect(0, 0, Context.PixelBuffer.Width, Context.PixelBuffer.Height);
            PixelRect onScreen = screen.Intersect(targetRect);
            return new PixelRect(onScreen.X - targetRect.X, onScreen.Y - targetRect.Y,
                onScreen.Width, onScreen.Height);
        }

        /// <summary>
        ///     Where <paramref name="intersectedRect" /> falls in a rendering of <paramref name="renderedCells" />
        ///     (cells relative to <paramref name="targetRect" />).
        /// </summary>
        protected static PixelRect IntersectedRectInRendering(PixelRect targetRect, PixelRect renderedCells,
            PixelRect intersectedRect)
        {
            return new PixelRect(intersectedRect.X - targetRect.X - renderedCells.X,
                intersectedRect.Y - targetRect.Y - renderedCells.Y,
                intersectedRect.Width, intersectedRect.Height);
        }

        /// <summary>
        ///     The pixels <paramref name="source" /> shows in <paramref name="visible" /> when it is drawn at
        ///     <paramref name="targetSize" />, as tightly packed BGRA rows.
        /// </summary>
        /// <exception cref="NotSupportedException">
        ///     The scaled pixels are not 32 bits each. Only BGRA and RGBA (converted) are read.
        /// </exception>
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

            // drawn at its own size: the visible part is a plain copy, unless the pixels need converting
            if (sourceSize == targetSize && sourceFrame.Format == PixelFormat.Bgra8888)
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
            if (frame.Format.BitsPerPixel != 32)
                throw new NotSupportedException($"Bitmaps with {frame.Format.BitsPerPixel} bits per pixel are not supported.");

            byte[] window = GC.AllocateUninitializedArray<byte>(width * height * 4);
            ReadOnlySpan<byte> pixels = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.AsRef<byte>((void*)frame.Address), frame.RowBytes * frame.Size.Height);
            CopyBlock(pixels, frame.RowBytes, x, y, width, height, window);

            if (frame.Format == PixelFormat.Rgba8888)
                for (int i = 0; i < window.Length; i += 4)
                    (window[i], window[i + 2]) = (window[i + 2], window[i]);

            return window;
        }

        /// <summary>
        ///     Copies the <paramref name="width" /> x <paramref name="height" /> block at (x, y) out of an
        ///     image of 4 bytes per pixel, <paramref name="imageRowBytes" /> per row, into the tightly packed
        ///     <paramref name="block" />.
        /// </summary>
        protected static void CopyBlock(ReadOnlySpan<byte> image, int imageRowBytes, int x, int y,
            int width, int height, Span<byte> block)
        {
            int blockRowBytes = width * 4;
            for (int row = 0; row < height; row++)
                image.Slice((y + row) * imageRowBytes + x * 4, blockRowBytes)
                    .CopyTo(block.Slice(row * blockRowBytes, blockRowBytes));
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

    /// <summary>
    ///     A renderer that turns the on-screen part of a picture into cells, once per picture, size and
    ///     visible part: drawing the same again reuses the rendering, and copies only the cells that
    ///     changed into the pixel buffer.
    /// </summary>
    /// <typeparam name="TRendering">What a rendering holds besides its cells.</typeparam>
    internal abstract class CellBitmapRenderer<TRendering> : BitmapRenderer
        where TRendering : class
    {
        /// <summary>
        ///     Renderings kept per bitmap. Each visible part is its own rendering, so scrolling a large
        ///     picture makes new ones; the oldest go once there are more than this.
        /// </summary>
        private const int MaxRenderingsPerBitmap = 4;

        protected CellBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        /// <summary>The renderings of each picture, kept for as long as the picture is.</summary>
        protected abstract ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, TRendering>>>
            Renderings { get; }

        /// <summary>Makes a rendering of the visible pixels, BGRA rows of exactly the visible cells.</summary>
        protected abstract TRendering Render(byte[] visibleBytes, PixelRect visibleCells, int cellPixelWidth,
            int cellPixelHeight, IPlatformRenderInterface renderInterface);

        /// <summary>The cells of <paramref name="rendering" />, one per visible cell.</summary>
        protected abstract PixelBuffer CellsOf(TRendering rendering);

        /// <summary>Whether a cached rendering can still be shown, or must be made again.</summary>
        protected virtual bool IsReusable(TRendering rendering)
        {
            return true;
        }

        public sealed override void Draw(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelRect targetRect, PixelRect intersectedRect, BitmapInterpolationMode interpolationMode)
        {
            int cellPixelWidth = Context.ConsoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = Context.ConsoleWindowImpl.Console.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            PixelRect visibleCells = OnScreenCellsInTarget(targetRect);
            var key = new RenderingKey(GetCacheBitmapImpl(source).Version, targetSize, visibleCells,
                interpolationMode);

            List<KeyValuePair<RenderingKey, TRendering>> renderings =
                Renderings.GetOrCreateValue(GetCacheBitmapImpl(source));
            TRendering rendering = FindReusable(renderings, key);
            if (rendering == null)
            {
                // only the visible cells are rendered, so the rendering starts at the first of them
                byte[] visibleBytes = GetVisiblePixels(source, renderInterface, targetSize,
                    new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                        visibleCells.Width * cellPixelWidth, visibleCells.Height * cellPixelHeight),
                    interpolationMode);
                rendering = Render(visibleBytes, visibleCells, cellPixelWidth, cellPixelHeight, renderInterface);
                Remember(renderings, key, rendering);
            }

            CopyRenderedBitmapTrackingDirtyRegions(CellsOf(rendering), intersectedRect,
                IntersectedRectInRendering(targetRect, visibleCells, intersectedRect));
        }

        private TRendering FindReusable(List<KeyValuePair<RenderingKey, TRendering>> renderings, RenderingKey key)
        {
            for (int i = 0; i < renderings.Count; i++)
            {
                if (renderings[i].Key != key)
                    continue;
                if (IsReusable(renderings[i].Value))
                    return renderings[i].Value;
                renderings.RemoveAt(i);
                return null;
            }

            return null;
        }

        /// <summary>
        ///     Keeps <paramref name="rendering" />. Renderings of older versions are dropped, and the oldest
        ///     of the current version once there are too many.
        /// </summary>
        private static void Remember(List<KeyValuePair<RenderingKey, TRendering>> renderings, RenderingKey key,
            TRendering rendering)
        {
            // a new version (next animation frame) makes every older version's renderings unreachable
            for (int i = renderings.Count - 1; i >= 0; i--)
                if (renderings[i].Key.Version != key.Version)
                    renderings.RemoveAt(i);

            while (renderings.Count >= MaxRenderingsPerBitmap)
                renderings.RemoveAt(0);

            renderings.Add(new KeyValuePair<RenderingKey, TRendering>(key, rendering));
        }
    }
}