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
    ///     The cache key for a converted bitmap. It holds the size the bitmap is drawn at and the scaling mode.
    ///     The bitmap version and the visible part are not in the key. When they change, the existing
    ///     conversion is refilled in place (see <see cref="CellConversion" />).
    /// </summary>
    // The properties are only read by the generated Equals and GetHashCode, because this type is a cache key.
    // ReSharper disable NotAccessedPositionalProperty.Global
    internal readonly record struct ConversionKey(
        PixelSize TargetSize,
        BitmapInterpolationMode InterpolationMode);
    // ReSharper restore NotAccessedPositionalProperty.Global

    /// <summary>
    ///     Draws bitmaps into the pixel buffer. The implementation depends on the console's capabilities.
    ///     If the console supports an image protocol, each cell gets a <see cref="PixelImage" /> background.
    ///     Otherwise each cell gets a block character.
    /// </summary>
    internal abstract class BitmapConverter
    {
        protected BitmapConverter(IConsoleOutput console)
        {
            ConsoleOutput = console;
        }

        /// <summary>The console output. Provides the cell size in pixels and the capabilities.</summary>
        protected IConsoleOutput ConsoleOutput { get; }

        /// <summary>
        ///     Creates the converter for the console. Returns a pixel image converter if the console supports
        ///     pixel images, and a block character converter otherwise.
        /// </summary>
        /// <remarks>Call this after PrepareConsole, which detects the capabilities.</remarks>
        internal static BitmapConverter Create(IConsoleOutput console)
        {
            if (console.Capabilities.HasFlag(ConsoleCapabilities.SupportsPixelImages))
                return new PixelImageBitmapConverter(console);

            return new QuadPixelBitmapConverter(console);
        }

        /// <summary>
        ///     Draws <paramref name="source" /> into the pixel buffer of <paramref name="context" /> over
        ///     <paramref name="targetRect" /> (cell coordinates), limited to the visible
        ///     <paramref name="intersectedRect" />. Marks the drawn cells as dirty.
        /// </summary>
        public abstract void Draw(DrawingContextImpl context, IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelRect intersectedRect,
            BitmapInterpolationMode interpolationMode);

        /// <summary>
        ///     Returns the part of <paramref name="targetRect" /> that is on screen, in cells relative to the
        ///     target. A conversion covers exactly this area.
        /// </summary>
        /// <remarks>
        ///     This uses the screen bounds, not the clip. A partial redraw (for example, a dialog opening over
        ///     the image) clips to the redrawn region. Converting only the clipped area created a separate
        ///     conversion for each redraw region. Cells outside the region still showed the older conversion.
        ///     When the cache evicted that older conversion, the image protocol deleted its image and those
        ///     cells went blank. The screen bounds also limit the size, and they do not change between redraws.
        /// </remarks>
        protected static PixelRect OnScreenCellsInTarget(DrawingContextImpl context, PixelRect targetRect)
        {
            var screen = new PixelRect(0, 0, context.PixelBuffer.Width, context.PixelBuffer.Height);
            PixelRect onScreen = screen.Intersect(targetRect);
            return new PixelRect(onScreen.X - targetRect.X, onScreen.Y - targetRect.Y,
                onScreen.Width, onScreen.Height);
        }

        /// <summary>
        ///     Returns the position of <paramref name="intersectedRect" /> inside a conversion that covers
        ///     <paramref name="convertedCells" />. <paramref name="convertedCells" /> is in cells relative to
        ///     <paramref name="targetRect" />.
        /// </summary>
        protected static PixelRect IntersectedRectInConversion(PixelRect targetRect, PixelRect convertedCells,
            PixelRect intersectedRect)
        {
            return new PixelRect(intersectedRect.X - targetRect.X - convertedCells.X,
                intersectedRect.Y - targetRect.Y - convertedCells.Y,
                intersectedRect.Width, intersectedRect.Height);
        }

        /// <summary>
        ///     Scales <paramref name="source" /> to <paramref name="targetSize" /> and copies the
        ///     <paramref name="visible" /> part into <paramref name="destination" /> as tightly packed BGRA rows.
        /// </summary>
        /// <param name="source">The bitmap.</param>
        /// <param name="renderInterface">Scales the bitmap when the target size differs from its own size.</param>
        /// <param name="targetSize">The size, in pixels, that the whole bitmap is drawn at.</param>
        /// <param name="visible">The part to copy, in pixels of <paramref name="targetSize" />.</param>
        /// <param name="interpolationMode">The scaling mode.</param>
        /// <param name="destination">
        ///     Must be exactly 4 bytes per visible pixel. The caller owns this buffer. This lets a converter
        ///     reuse one buffer on every edit instead of allocating a screen-sized buffer each time.
        /// </param>
        /// <exception cref="NotSupportedException">
        ///     The pixels are not 32 bits each. Only BGRA and RGBA are supported. RGBA is converted to BGRA.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     <paramref name="destination" /> is not the size of the visible pixels.
        /// </exception>
        /// <remarks>
        ///     Only the visible part is scaled, never the whole bitmap. If the bitmap is zoomed far beyond the
        ///     screen, scaling the whole bitmap could produce an image larger than the terminal accepts
        ///     (XTerm.NET stops at four megapixels). The terminal would then show nothing at all, instead of
        ///     the visible part.
        /// </remarks>
        internal static void GetVisiblePixels(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelSize targetSize, PixelRect visible, BitmapInterpolationMode interpolationMode, Span<byte> destination)
        {
            if (destination.Length != visible.Width * visible.Height * 4)
                throw new ArgumentException("The destination is not the size of the visible pixels.",
                    nameof(destination));

            var readableSource = (IReadableBitmapImpl)source;
            using ILockedFramebuffer sourceFrame = readableSource.Lock();

            // Use the real size of the pixel data, not source.PixelSize. An AspectRatioAdjustedBitmap reports
            // half its height to layout. Using that size drew only the top half of the image, stretched.
            PixelSize sourceSize = sourceFrame.Size;

            // No scaling needed and already BGRA: copy the visible part directly.
            if (sourceSize == targetSize && sourceFrame.Format == PixelFormat.Bgra8888)
            {
                CopyWindow(sourceFrame, visible.X, visible.Y, visible.Width, visible.Height, destination);
                return;
            }

            // The source pixels under the visible part, rounded outward to whole pixels.
            double scaleX = (double)sourceSize.Width / targetSize.Width;
            double scaleY = (double)sourceSize.Height / targetSize.Height;
            int left = Math.Clamp((int)Math.Floor(visible.X * scaleX), 0, sourceSize.Width - 1);
            int top = Math.Clamp((int)Math.Floor(visible.Y * scaleY), 0, sourceSize.Height - 1);
            int right = Math.Clamp((int)Math.Ceiling(visible.Right * scaleX), left + 1, sourceSize.Width);
            int bottom = Math.Clamp((int)Math.Ceiling(visible.Bottom * scaleY), top + 1, sourceSize.Height);

            // Scale that source area, then crop to the visible part. Rounding outward can put the edge of
            // the area a fraction of a pixel outside the visible part. The offsets below remove that extra.
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
        ///     Copies the <paramref name="width" /> x <paramref name="height" /> block at (x, y) from
        ///     <paramref name="image" /> into <paramref name="block" />. The image has 4 bytes per pixel and
        ///     <paramref name="imageRowBytes" /> bytes per row. The block is tightly packed.
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
        ///     Blends the visible part of a converted bitmap into the pixel buffer and marks those cells dirty.
        /// </summary>
        /// <param name="context">The drawing context that owns the target pixel buffer.</param>
        /// <param name="convertedBitmap">The converted cells.</param>
        /// <param name="intersectedRect">The cells of the pixel buffer to fill.</param>
        /// <param name="convertedOrigin">
        ///     The cell in <paramref name="convertedBitmap" /> that is copied to the top-left cell of
        ///     <paramref name="intersectedRect" />.
        /// </param>
        protected static void CopyConvertedBitmap(DrawingContextImpl context, PixelBuffer convertedBitmap,
            PixelRect intersectedRect, PixelPoint convertedOrigin)
        {
            PixelBuffer pixelBuffer = context.PixelBuffer;
            for (int y = 0; y < intersectedRect.Height; y++)
            for (int x = 0; x < intersectedRect.Width; x++)
            {
                var destPoint = new PixelPoint(intersectedRect.X + x, intersectedRect.Y + y);
                pixelBuffer[destPoint] = pixelBuffer[destPoint]
                    .Blend(convertedBitmap[new PixelPoint(convertedOrigin.X + x, convertedOrigin.Y + y)]);
            }

            context.ConsoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        /// <summary>
        ///     Returns the inner bitmap if <paramref name="bitmapImpl" /> is an
        ///     <see cref="AspectRatioAdjustedBitmap" />. Conversions are cached per inner bitmap, because a new
        ///     wrapper is created each time the bitmap is drawn.
        /// </summary>
        protected static IBitmapImpl UnwrapBitmap(IBitmapImpl bitmapImpl)
        {
            return bitmapImpl is AspectRatioAdjustedBitmap adjustedBitmap
                ? adjustedBitmap.InnerBitmap
                : bitmapImpl;
        }
    }

    /// <summary>
    ///     A converter that converts the on-screen part of a bitmap into cells and caches the result per bitmap
    ///     and size. Drawing the same bitmap again reuses the cached cells. A new bitmap version or a scroll
    ///     refills the cached conversion in place.
    /// </summary>
    /// <remarks>
    ///     The source pixels go through one scratch buffer owned by the converter, not one buffer per
    ///     conversion. The buffer is never larger than the screen in BGRA, because only visible cells are
    ///     converted. Each cell copies the pixels it needs, so the buffer is not read after conversion.
    ///     A buffer per conversion would keep four screens of pixels for a bitmap shown at four zoom levels.
    /// </remarks>
    /// <typeparam name="TConversion">The conversion type. It can hold extra data besides the cells.</typeparam>
    internal abstract class CellBitmapConverter<TConversion> : BitmapConverter
        where TConversion : CellConversion, new()
    {
        /// <summary>
        ///     The maximum number of conversions cached per bitmap. There is one conversion per drawn size, so a
        ///     bitmap shown at several zoom levels has several. Above this limit, the least recently drawn is
        ///     removed.
        /// </summary>
        private const int MaxConversionsPerBitmap = 4;

        /// <summary>Scratch buffer for the visible pixels being converted. See the class remarks.</summary>
        private byte[] _pixels;

        protected CellBitmapConverter(IConsoleOutput console)
            : base(console)
        {
        }

        /// <summary>The cached conversions of each bitmap. They are released when the bitmap is collected.</summary>
        protected abstract ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<ConversionKey, TConversion>>>
            Conversions { get; }

        /// <summary>
        ///     Fills every cell of <see cref="CellConversion.Cells" /> in <paramref name="conversion" /> from
        ///     <paramref name="pixels" />. The pixels are BGRA rows covering exactly the visible cells.
        ///     They are only valid during this call.
        /// </summary>
        protected abstract void Convert(DrawingContextImpl context, TConversion conversion, ReadOnlySpan<byte> pixels,
            int cellPixelWidth, int cellPixelHeight, IPlatformRenderInterface renderInterface);

        /// <summary>
        ///     Returns true if a conversion of the current version can still be used. Returns false if it must
        ///     be converted again.
        /// </summary>
        protected virtual bool IsReusable(TConversion conversion)
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

            TConversion conversion = FindOrAdd(Conversions.GetOrCreateValue(bitmap),
                new ConversionKey(targetSize, interpolationMode));
            if (!conversion.Shows(bitmap.Version, visibleCells) || !IsReusable(conversion))
            {
                // Only the visible cells are converted, so the conversion starts at the first visible cell.
                conversion.Prepare(visibleCells);
                var visiblePixels = new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                    visibleCells.Width * cellPixelWidth, visibleCells.Height * cellPixelHeight);
                int pixelBytes = visiblePixels.Width * visiblePixels.Height * 4;
                if (_pixels == null || _pixels.Length < pixelBytes)
                    _pixels = GC.AllocateUninitializedArray<byte>(pixelBytes);
                Span<byte> pixels = _pixels.AsSpan(0, pixelBytes);
                GetVisiblePixels(source, renderInterface, targetSize, visiblePixels, interpolationMode, pixels);
                Convert(context, conversion, pixels, cellPixelWidth, cellPixelHeight, renderInterface);
                conversion.Complete(bitmap.Version);
            }

            CopyConvertedBitmap(context, conversion.Cells, intersectedRect,
                IntersectedRectInConversion(targetRect, visibleCells, intersectedRect).Position);
        }

        /// <summary>
        ///     Returns the conversion for <paramref name="key" />, creating it if needed. Moves it to the end of
        ///     the list, which marks it as the most recently drawn. Removes the least recently drawn conversions
        ///     when there are too many.
        /// </summary>
        internal static TConversion FindOrAdd(List<KeyValuePair<ConversionKey, TConversion>> conversions,
            ConversionKey key)
        {
            for (int i = 0; i < conversions.Count; i++)
            {
                if (conversions[i].Key != key)
                    continue;

                KeyValuePair<ConversionKey, TConversion> found = conversions[i];
                if (i != conversions.Count - 1)
                {
                    conversions.RemoveAt(i);
                    conversions.Add(found);
                }

                return found.Value;
            }

            while (conversions.Count >= MaxConversionsPerBitmap)
                conversions.RemoveAt(0);

            var conversion = new TConversion();
            conversions.Add(new KeyValuePair<ConversionKey, TConversion>(key, conversion));
            return conversion;
        }
    }

    /// <summary>
    ///     The cells that a bitmap converts to at one size. A converter keeps one per bitmap and size. It
    ///     refills the cells in place for each new bitmap version or visible area. This way the cell buffer
    ///     (72 bytes per cell) is allocated once, not on every edit (for example, every brush stroke).
    ///     Only used from the render thread.
    /// </summary>
    internal class CellConversion
    {
        private bool _complete;

        /// <summary>The bitmap version that the cells show. Only valid after <see cref="Complete" />.</summary>
        public int Version { get; private set; }

        /// <summary>The part of the bitmap that is converted, in cells relative to where the bitmap is drawn.</summary>
        public PixelRect VisibleCells { get; private set; }

        /// <summary>The converted cells, one for each cell in <see cref="VisibleCells" />.</summary>
        public PixelBuffer Cells { get; private set; }

        /// <summary>
        ///     Returns true if the cells are complete and show <paramref name="version" /> of the bitmap over
        ///     <paramref name="visibleCells" />.
        /// </summary>
        public bool Shows(int version, PixelRect visibleCells)
        {
            return _complete && Version == version && VisibleCells == visibleCells;
        }

        /// <summary>
        ///     Prepares the cells for a conversion of <paramref name="visibleCells" />. The cell buffer is kept if
        ///     its size does not change. The conversion is marked incomplete until <see cref="Complete" /> is
        ///     called. So if the conversion throws, it is converted again next time instead of claiming to show
        ///     the previous version.
        /// </summary>
        internal void Prepare(PixelRect visibleCells)
        {
            _complete = false;
            VisibleCells = visibleCells;

            if (Cells == null || Cells.Width != visibleCells.Width || Cells.Height != visibleCells.Height)
                Cells = new PixelBuffer((ushort)visibleCells.Width, (ushort)visibleCells.Height);
        }

        /// <summary>
        ///     Marks the conversion as complete. The cells now show <paramref name="version" /> over the area
        ///     given to <see cref="Prepare" />.
        /// </summary>
        internal void Complete(int version)
        {
            Version = version;
            _complete = true;
        }
    }
}