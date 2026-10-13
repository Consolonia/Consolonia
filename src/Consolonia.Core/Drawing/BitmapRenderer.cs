using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

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
    ///     Strategy for drawing a bitmap into the pixel buffer. One renderer serves a terminal (the render
    ///     target keeps it, the drawing contexts borrow it), chosen by the console's capabilities. A
    ///     console with an image protocol gets cells showing a <see cref="CellImage" />, which its output
    ///     encodes; any other gets text cells.
    /// </summary>
    internal abstract class BitmapRenderer
    {
        protected BitmapRenderer(IConsoleOutput console)
        {
            ConsoleOutput = console;
        }

        /// <summary>The console the pictures go to: its cell size and capabilities.</summary>
        protected IConsoleOutput ConsoleOutput { get; }

        /// <summary>
        ///     The renderer for the console (capabilities are detected in PrepareConsole): cell images when
        ///     the console can show pictures, block glyphs otherwise.
        /// </summary>
        internal static BitmapRenderer Create(IConsoleOutput console)
        {
            ConsoleCapabilities capabilities = console.Capabilities;

            if (capabilities.HasFlag(ConsoleCapabilities.SupportsKittyGraphics) ||
                capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return new CellImageBitmapRenderer(console);

            return new QuadPixelBitmapRenderer(console);
        }

        /// <summary>
        ///     Draws <paramref name="source" /> into the pixel buffer of <paramref name="context" /> over
        ///     <paramref name="targetRect" /> (cell coordinates), limited to the visible
        ///     <paramref name="intersectedRect" />, and tracks dirty regions.
        /// </summary>
        public abstract void Draw(DrawingContextImpl context, IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelRect intersectedRect,
            BitmapInterpolationMode interpolationMode);

        /// <summary>
        ///     The part of <paramref name="targetRect" /> on screen, in cells relative to the target: what a
        ///     rendering covers.
        /// </summary>
        /// <remarks>
        ///     The screen, not the clip. A partial redraw (a dialog opening over the picture, say) clips to
        ///     the region being redrawn, and rendering by that clip made a rendering per redraw region. The
        ///     cells outside it still showed the earlier rendering, and once the cache evicted that one, the
        ///     image protocol deleted its image and those cells went blank. The screen bounds the size just
        ///     as well, and stays the same however the picture is redrawn.
        /// </remarks>
        protected static PixelRect OnScreenCellsInTarget(DrawingContextImpl context, PixelRect targetRect)
        {
            var screen = new PixelRect(0, 0, context.PixelBuffer.Width, context.PixelBuffer.Height);
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
        /// <param name="source">The picture.</param>
        /// <param name="renderInterface">Scales the picture when it is not drawn at its own size.</param>
        /// <param name="targetSize">The size in pixels the whole picture is drawn at.</param>
        /// <param name="visible">The part of the drawn picture to copy, in pixels of <paramref name="targetSize" />.</param>
        /// <param name="interpolationMode">How the picture is scaled.</param>
        /// <param name="destination">
        ///     Exactly 4 bytes per visible pixel. The caller owns it, so a renderer refilling a rendering on
        ///     every edit of a picture keeps one rather than allocating a screen of pixels each time.
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
            PixelSize targetSize, PixelRect visible, BitmapInterpolationMode interpolationMode, Span<byte> destination)
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
            Span<byte> window)
        {
            if (frame.Format.BitsPerPixel != 32)
                throw new NotSupportedException(
                    $"Bitmaps with {frame.Format.BitsPerPixel} bits per pixel are not supported.");

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
        ///     Blends the visible part of a rendered per-cell bitmap into the pixel buffer and marks it
        ///     dirty, as every drawing operation does. Cells that did not change are not written to the
        ///     terminal again: the render target compares each dirty cell with what it last wrote.
        /// </summary>
        /// <param name="context">The drawing context whose pixel buffer takes the cells.</param>
        /// <param name="renderedBitmap">The rendering, one cell per cell.</param>
        /// <param name="intersectedRect">The cells of the pixel buffer to fill.</param>
        /// <param name="renderedOrigin">
        ///     The cell of the rendering that goes to the top-left cell of <paramref name="intersectedRect" />.
        /// </param>
        protected static void CopyRenderedBitmap(DrawingContextImpl context, PixelBuffer renderedBitmap,
            PixelRect intersectedRect, PixelPoint renderedOrigin)
        {
            PixelBuffer pixelBuffer = context.PixelBuffer;
            for (int y = 0; y < intersectedRect.Height; y++)
            for (int x = 0; x < intersectedRect.Width; x++)
            {
                var destPoint = new PixelPoint(intersectedRect.X + x, intersectedRect.Y + y);
                pixelBuffer[destPoint] = pixelBuffer[destPoint]
                    .Blend(renderedBitmap[new PixelPoint(renderedOrigin.X + x, renderedOrigin.Y + y)]);
            }

            context.ConsoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        /// <summary>
        ///     The picture itself, out of the <see cref="AspectRatioAdjustedBitmap" /> it may be wrapped in:
        ///     the renderings are kept per picture, and a wrapper is made anew each time it is drawn.
        /// </summary>
        protected static IBitmapImpl UnwrapBitmap(IBitmapImpl bitmapImpl)
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
    /// <remarks>
    ///     The pixels a rendering is made from pass through one buffer the renderer keeps, never larger than
    ///     the screen in BGRA (only visible cells are rendered), rather than a buffer per rendering: a cell
    ///     copies the pixels it needs, so nothing reads them once the cells are made, and a picture at four
    ///     zoom levels kept four screens of them.
    /// </remarks>
    /// <typeparam name="TRendering">What a rendering holds besides its cells.</typeparam>
    internal abstract class CellBitmapRenderer<TRendering> : BitmapRenderer
        where TRendering : CellRendering, new()
    {
        /// <summary>
        ///     Renderings kept per bitmap, one per size it is drawn at: a picture shown at several zoom
        ///     levels has one for each, and the least recently drawn goes once there are more than this.
        /// </summary>
        private const int MaxRenderingsPerBitmap = 4;

        /// <summary>The visible pixels of the picture being rendered; see the remarks.</summary>
        private byte[] _pixels;

        protected CellBitmapRenderer(IConsoleOutput console)
            : base(console)
        {
        }

        /// <summary>The renderings of each picture, kept for as long as the picture is.</summary>
        protected abstract ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, TRendering>>>
            Renderings { get; }

        /// <summary>
        ///     Fills <see cref="CellRendering.Cells" /> of <paramref name="rendering" />, every cell, from
        ///     <paramref name="pixels" />: BGRA rows of exactly the visible cells, valid for this call only.
        /// </summary>
        protected abstract void Render(DrawingContextImpl context, TRendering rendering, ReadOnlySpan<byte> pixels,
            int cellPixelWidth, int cellPixelHeight, IPlatformRenderInterface renderInterface);

        /// <summary>Whether a rendering of the current version can still be shown, or must be made again.</summary>
        protected virtual bool IsReusable(TRendering rendering)
        {
            return true;
        }

        public sealed override void Draw(DrawingContextImpl context, IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelRect intersectedRect,
            BitmapInterpolationMode interpolationMode)
        {
            int cellPixelWidth = ConsoleOutput.CellPixelWidth;
            int cellPixelHeight = ConsoleOutput.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            PixelRect visibleCells = OnScreenCellsInTarget(context, targetRect);
            IBitmapImpl bitmap = UnwrapBitmap(source);

            TRendering rendering = FindOrAdd(Renderings.GetOrCreateValue(bitmap),
                new RenderingKey(targetSize, interpolationMode));
            if (!rendering.Shows(bitmap.Version, visibleCells) || !IsReusable(rendering))
            {
                // only the visible cells are rendered, so the rendering starts at the first of them
                rendering.Prepare(visibleCells);
                var visiblePixels = new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                    visibleCells.Width * cellPixelWidth, visibleCells.Height * cellPixelHeight);
                int pixelBytes = visiblePixels.Width * visiblePixels.Height * 4;
                if (_pixels == null || _pixels.Length < pixelBytes)
                    _pixels = GC.AllocateUninitializedArray<byte>(pixelBytes);
                Span<byte> pixels = _pixels.AsSpan(0, pixelBytes);
                GetVisiblePixels(source, renderInterface, targetSize, visiblePixels, interpolationMode, pixels);
                Render(context, rendering, pixels, cellPixelWidth, cellPixelHeight, renderInterface);
                rendering.Complete(bitmap.Version);
            }

            CopyRenderedBitmap(context, rendering.Cells, intersectedRect,
                IntersectedRectInRendering(targetRect, visibleCells, intersectedRect).Position);
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
    ///     The cells a picture renders to at one size. A renderer keeps one per picture and size and
    ///     refills it in place for each new version or visible part, so an edit allocates nothing the size
    ///     of the screen: the cell buffer (40 bytes a cell) is made once, not once per brush stroke.
    ///     Touched only from the render thread, like everything a renderer draws with.
    /// </summary>
    internal class CellRendering
    {
        private bool _complete;

        /// <summary>The version of the picture the cells show, once <see cref="Shows" /> can be true.</summary>
        public int Version { get; private set; }

        /// <summary>The cells of the picture the rendering covers, relative to where it is drawn.</summary>
        public PixelRect VisibleCells { get; private set; }

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
        ///     Makes room for a rendering of <paramref name="visibleCells" />, keeping the cells when their
        ///     size is unchanged, and shows nothing until <see cref="Complete" />: a render that throws
        ///     leaves a rendering that is made again, not one claiming to show the previous version.
        /// </summary>
        internal void Prepare(PixelRect visibleCells)
        {
            _complete = false;
            VisibleCells = visibleCells;

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