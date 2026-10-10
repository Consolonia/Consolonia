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
    ///     What a rendering of a bitmap is for: the size it is drawn at and how it was scaled. The picture's
    ///     version and the part of it on screen are not part of the key: a rendering is refilled in place
    ///     when they change (see <see cref="CellRendering" />).
    /// </summary>
    // The properties are only read through the generated Equals/GetHashCode: together they are a cache key.
    // ReSharper disable NotAccessedPositionalProperty.Global
    internal readonly record struct RenderingKey(
        PixelSize TargetSize,
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
        ///     Copies the pixels <paramref name="source" /> shows in <paramref name="visible" /> when it is drawn
        ///     at <paramref name="targetSize" /> into <paramref name="destination" />, as tightly packed BGRA rows.
        /// </summary>
        /// <param name="destination">
        ///     Exactly 4 bytes per visible pixel. The caller owns it, so a rendering refilled on every edit of
        ///     a picture keeps one rather than allocating a screen of pixels each time.
        /// </param>
        /// <exception cref="NotSupportedException">
        ///     The scaled pixels are not 32 bits each. Only BGRA and RGBA (converted) are read.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     <paramref name="destination" /> is not the size of the visible pixels.
        /// </exception>
        /// <remarks>
        ///     Only the visible part is scaled, never the whole picture at the size it is drawn. A picture
        ///     zoomed far past the screen would otherwise be scaled, and for the image protocols sent to the
        ///     terminal, at a size the terminal refuses (XTerm.NET stops at four megapixels), and the whole
        ///     picture would vanish rather than show its visible part.
        /// </remarks>
        internal static void GetVisiblePixels(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelSize targetSize, PixelRect visible, BitmapInterpolationMode interpolationMode, byte[] destination)
        {
            if (destination.Length != visible.Width * visible.Height * 4)
                throw new ArgumentException("The destination is not the size of the visible pixels.",
                    nameof(destination));

            var readableSource = (IReadableBitmapImpl)source;
            using ILockedFramebuffer sourceFrame = readableSource.Lock();

            // The pixels actually there. Not source.PixelSize: an AspectRatioAdjustedBitmap reports half
            // its height to layout, and measuring by that put the picture's top half where all of it goes.
            PixelSize sourceSize = sourceFrame.Size;

            // drawn at its own size: the visible part is a plain copy, unless the pixels need converting
            if (sourceSize == targetSize && sourceFrame.Format == PixelFormat.Bgra8888)
            {
                CopyWindow(sourceFrame, visible.X, visible.Y, visible.Width, visible.Height, destination);
                return;
            }

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

            CopyWindow(scaledFrame, offsetX, offsetY, visible.Width, visible.Height, destination);
        }

        private static unsafe void CopyWindow(ILockedFramebuffer frame, int x, int y, int width, int height,
            byte[] window)
        {
            if (frame.Format.BitsPerPixel != 32)
                throw new NotSupportedException($"Bitmaps with {frame.Format.BitsPerPixel} bits per pixel are not supported.");

            ReadOnlySpan<byte> pixels = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.AsRef<byte>((void*)frame.Address), frame.RowBytes * frame.Size.Height);
            CopyBlock(pixels, frame.RowBytes, x, y, width, height, window);

            if (frame.Format == PixelFormat.Rgba8888)
                for (int i = 0; i < window.Length; i += 4)
                    (window[i], window[i + 2]) = (window[i + 2], window[i]);
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
    ///     A renderer that turns the on-screen part of a picture into cells, once per picture and size:
    ///     drawing the same again reuses the rendering, a new version or a scroll refills it in place, and
    ///     only the cells that changed are copied into the pixel buffer.
    /// </summary>
    /// <typeparam name="TRendering">What a rendering holds besides its cells and pixels.</typeparam>
    internal abstract class CellBitmapRenderer<TRendering> : BitmapRenderer
        where TRendering : CellRendering, new()
    {
        /// <summary>
        ///     Renderings kept per bitmap, one per size it is drawn at: a picture shown at several zoom
        ///     levels has one for each, and the least recently drawn goes once there are more than this.
        /// </summary>
        private const int MaxRenderingsPerBitmap = 4;

        protected CellBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        /// <summary>The renderings of each picture, kept for as long as the picture is.</summary>
        protected abstract ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, TRendering>>>
            Renderings { get; }

        /// <summary>
        ///     Fills <see cref="CellRendering.Cells" /> of <paramref name="rendering" />, every cell, from its
        ///     <see cref="CellRendering.Pixels" />: BGRA rows of exactly the visible cells.
        /// </summary>
        protected abstract void Render(TRendering rendering, int cellPixelWidth, int cellPixelHeight,
            IPlatformRenderInterface renderInterface);

        /// <summary>Whether a rendering of the current version can still be shown, or must be made again.</summary>
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
            IBitmapImpl bitmap = GetCacheBitmapImpl(source);

            TRendering rendering = FindOrAdd(Renderings.GetOrCreateValue(bitmap),
                new RenderingKey(targetSize, interpolationMode));
            if (!rendering.Shows(bitmap.Version, visibleCells) || !IsReusable(rendering))
            {
                // only the visible cells are rendered, so the rendering starts at the first of them
                rendering.Prepare(visibleCells, cellPixelWidth, cellPixelHeight);
                GetVisiblePixels(source, renderInterface, targetSize,
                    new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                        visibleCells.Width * cellPixelWidth, visibleCells.Height * cellPixelHeight),
                    interpolationMode, rendering.Pixels);
                Render(rendering, cellPixelWidth, cellPixelHeight, renderInterface);
                rendering.Complete(bitmap.Version);
            }

            CopyRenderedBitmapTrackingDirtyRegions(rendering.Cells, intersectedRect,
                IntersectedRectInRendering(targetRect, visibleCells, intersectedRect));
        }

        /// <summary>
        ///     The rendering for <paramref name="key" />, made when there is none, and now the most recently
        ///     drawn (last in the list). The least recently drawn goes once there are too many.
        /// </summary>
        internal static TRendering FindOrAdd(List<KeyValuePair<RenderingKey, TRendering>> renderings,
            RenderingKey key)
        {
            for (int i = 0; i < renderings.Count; i++)
            {
                if (renderings[i].Key != key)
                    continue;

                KeyValuePair<RenderingKey, TRendering> found = renderings[i];
                if (i != renderings.Count - 1)
                {
                    renderings.RemoveAt(i);
                    renderings.Add(found);
                }

                return found.Value;
            }

            while (renderings.Count >= MaxRenderingsPerBitmap)
                renderings.RemoveAt(0);

            var rendering = new TRendering();
            renderings.Add(new KeyValuePair<RenderingKey, TRendering>(key, rendering));
            return rendering;
        }
    }

    /// <summary>
    ///     The cells a picture renders to at one size, with the pixels they were made from. A renderer
    ///     keeps one per picture and size and refills it in place for each new version or visible part,
    ///     so an edit allocates nothing the size of the screen: the pixel copy (8 MB for a full HD
    ///     picture) and the cell buffer (72 bytes a cell) are made once, not once per brush stroke.
    ///     Touched only from the render thread, like everything a renderer draws with.
    /// </summary>
    internal class CellRendering
    {
        private bool _complete;

        /// <summary>The version of the picture the cells show, once <see cref="Shows" /> can be true.</summary>
        public int Version { get; private set; }

        /// <summary>The cells of the picture the rendering covers, relative to where it is drawn.</summary>
        public PixelRect VisibleCells { get; private set; }

        /// <summary>
        ///     BGRA pixels of exactly the visible cells, row by row: what the cells were made from. Exactly
        ///     that long, as the quantizer reads the whole array.
        /// </summary>
        public byte[] Pixels { get; private set; }

        /// <summary>One cell per visible cell.</summary>
        public PixelBuffer Cells { get; private set; }

        /// <summary>
        ///     Whether the cells show <paramref name="version" /> of the picture over
        ///     <paramref name="visibleCells" />.
        /// </summary>
        public bool Shows(int version, PixelRect visibleCells)
        {
            return _complete && Version == version && VisibleCells == visibleCells;
        }

        /// <summary>
        ///     Makes room for a rendering of <paramref name="visibleCells" />, keeping the buffers when their
        ///     size is unchanged, and shows nothing until <see cref="Complete" />: a render that throws
        ///     leaves a rendering that is made again, not one claiming to show the previous version.
        /// </summary>
        internal void Prepare(PixelRect visibleCells, int cellPixelWidth, int cellPixelHeight)
        {
            _complete = false;
            VisibleCells = visibleCells;

            int pixelBytes = visibleCells.Width * cellPixelWidth * visibleCells.Height * cellPixelHeight * 4;
            if (Pixels == null || Pixels.Length != pixelBytes)
                Pixels = GC.AllocateUninitializedArray<byte>(pixelBytes);
            if (Cells == null || Cells.Width != visibleCells.Width || Cells.Height != visibleCells.Height)
                Cells = new PixelBuffer((ushort)visibleCells.Width, (ushort)visibleCells.Height);
        }

        /// <summary>
        ///     The cells now show <paramref name="version" /> over the cells given to <see cref="Prepare" />.
        /// </summary>
        internal void Complete(int version)
        {
            Version = version;
            _complete = true;
        }
    }
}