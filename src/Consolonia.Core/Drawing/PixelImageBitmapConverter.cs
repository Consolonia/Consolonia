using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Converts a bitmap into cells whose background is a <see cref="PixelImage" />. Each pixel image holds
    ///     the pixels of one cell. Used for any console that supports an image protocol. The console output
    ///     decides which protocol to use and what to send for each cell.
    /// </summary>
    /// <remarks>
    ///     Pixel images are cached by content, so cells with the same pixels share one instance. The pixel
    ///     buffer compares images by reference. A cell whose pixels did not change keeps the same instance,
    ///     so it is not dirty and is not written to the terminal again. When the bitmap changes, only the
    ///     changed cells are rewritten. A bitmap of one solid color uses one image for all its cells.
    /// </remarks>
    internal sealed class PixelImageBitmapConverter : CellBitmapConverter<CellConversion>
    {
        /// <summary>
        ///     The minimum cache size, in bytes of pixel data. On a large screen the budget grows to
        ///     <see cref="ScreensOfPixelImages" /> screens, so a full-screen bitmap never evicts its own cells
        ///     while it is being converted. Eviction only stops sharing. A cell on screen still holds a
        ///     reference to its image.
        /// </summary>
        private const long MinPixelImageBudgetBytes = 24L * 1024 * 1024;

        /// <summary>
        ///     The cache size in screens of pixel data. This lets a bitmap that is shown again reuse its
        ///     cached images.
        /// </summary>
        private const int ScreensOfPixelImages = 3;

        /// <summary>
        ///     The number of cells in one slab. A slab is one array shared by several new pixel images. It is
        ///     filled across conversions until full. So a new bitmap needs a few hundred allocations instead of
        ///     one per cell, and a brush stroke usually uses one slot of the current slab. The slab is small
        ///     enough to stay out of the large object heap. It is also small enough that one surviving cell
        ///     does not keep much memory alive. The cache counts only each cell's own bytes, and a slab is
        ///     freed when none of its cells are referenced.
        /// </summary>
        internal const int CellsPerSlab = 64;

        /// <summary>
        ///     The foreground of an image cell: a space, the same as an empty cell. A space over an image
        ///     shows the image. Text drawn later replaces the space.
        /// </summary>
        internal static readonly PixelForeground PictureCellForeground = PixelForeground.Space;

        /// <summary>
        ///     The current slab for new pixel images, and the number of bytes already used in it.
        ///     One per render thread.
        /// </summary>
        [ThreadStatic] private static byte[] _slab;

        [ThreadStatic] private static int _slabUsed;

        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<ConversionKey, CellConversion>>>
            ConvertedBitmapCache = new();

        private static readonly PixelImageCache PixelImages = new(MinPixelImageBudgetBytes);

        public PixelImageBitmapConverter(IConsoleOutput console)
            : base(console)
        {
        }

        protected override ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<ConversionKey, CellConversion>>>
            Conversions => ConvertedBitmapCache;

        protected override void Convert(DrawingContextImpl context, CellConversion conversion,
            ReadOnlySpan<byte> pixels, int cellPixelWidth, int cellPixelHeight,
            IPlatformRenderInterface renderInterface)
        {
            PixelImages.EnsureBudget(ScreensOfPixelImages * 4L *
                                     context.PixelBuffer.Width * cellPixelWidth *
                                     context.PixelBuffer.Height * cellPixelHeight);
            ConvertCells(pixels, conversion.Cells, cellPixelWidth, cellPixelHeight);
        }

        /// <summary>
        ///     Converts the visible pixels into cells with pixel image backgrounds. Reuses a cached image
        ///     when one already exists for the same pixels.
        /// </summary>
        /// <param name="visibleBytes">BGRA pixels of exactly the visible cells, row by row.</param>
        /// <param name="cells">One cell per visible cell. Every cell is overwritten. Old contents are not read.</param>
        /// <param name="cellPixelWidth">The width of a cell in pixels.</param>
        /// <param name="cellPixelHeight">The height of a cell in pixels.</param>
        /// <exception cref="ArgumentException">
        ///     <paramref name="visibleBytes" /> is not the pixels of exactly the cells.
        /// </exception>
        internal static void ConvertCells(ReadOnlySpan<byte> visibleBytes, PixelBuffer cells, int cellPixelWidth,
            int cellPixelHeight)
        {
            int cellsWide = cells.Width;
            int cellsHigh = cells.Height;
            int visibleWidth = cellsWide * cellPixelWidth;
            int cellBytes = cellPixelWidth * cellPixelHeight * 4;
            if (visibleBytes.Length != visibleWidth * cellsHigh * cellPixelHeight * 4)
                throw new ArgumentException("The pixels are not those of exactly the cells.", nameof(visibleBytes));

            // Copy each cell into a scratch buffer first. Most cells are already cached, so only new cells
            // need their own copy.
            Span<byte> cellBgra = cellBytes <= 4096 ? stackalloc byte[cellBytes] : new byte[cellBytes];
            byte[] slab = _slab;
            int slabUsed = _slabUsed;

            for (int cellY = 0; cellY < cellsHigh; cellY++)
            for (int cellX = 0; cellX < cellsWide; cellX++)
            {
                CopyBlock(visibleBytes, visibleWidth * 4, cellX * cellPixelWidth, cellY * cellPixelHeight,
                    cellPixelWidth, cellPixelHeight, cellBgra);

                UInt128 digest = XxHash128.HashToUInt128(cellBgra);
                if (!PixelImages.TryGet(digest, cellPixelWidth, cellPixelHeight, out PixelImage image))
                {
                    if (slab == null || slabUsed + cellBytes > slab.Length)
                    {
                        slab = _slab = GC.AllocateUninitializedArray<byte>(CellsPerSlab * cellBytes);
                        slabUsed = 0;
                    }

                    cellBgra.CopyTo(slab.AsSpan(slabUsed, cellBytes));
                    var made = new PixelImage(slab, slabUsed, cellPixelWidth, cellPixelHeight, digest);
                    image = PixelImages.GetOrAdd(made);
                    if (ReferenceEquals(image, made))
                        _slabUsed = slabUsed += cellBytes;
                }

                // The image is the cell's background. The foreground stays free, so text drawn later appears
                // on top of the image. The background color starts transparent. Translucent overlays drawn
                // later blend into this color.
                cells[new PixelPoint(cellX, cellY)] = new Pixel(PictureCellForeground,
                    new PixelBackground(Colors.Transparent, image));
            }
        }
    }
}