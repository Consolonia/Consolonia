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
    ///     Renders a bitmap as cells whose background is a <see cref="CellImage" />: the pixels of that cell
    ///     of the picture. For any console with an image protocol; which protocol, and what is sent for a
    ///     cell, is the console output's business.
    /// </summary>
    /// <remarks>
    ///     Each cell's image is kept by the pixels it shows and reused wherever they show again. The
    ///     pixel buffer compares images by reference, so a cell whose pixels did not change keeps the
    ///     same image, is not dirty, and is not written again: a change to the picture rewrites only the
    ///     cells it touched, and a flat canvas is one image shared by every cell.
    /// </remarks>
    internal sealed class CellImageBitmapRenderer : CellBitmapRenderer<CellRendering>
    {
        /// <summary>
        ///     Pixel bytes of cell images kept at least. The budget grows to <see cref="ScreensOfCellImages" />
        ///     screens on a larger screen, so a full-screen picture never evicts its own cells while it is
        ///     being made. Eviction only ends deduplication: a cell on screen holds its image by reference.
        /// </summary>
        private const long MinCellImageBudgetBytes = 24L * 1024 * 1024;

        /// <summary>Screens' worth of cell images kept, so pictures shown again are not cut again.</summary>
        private const int ScreensOfCellImages = 3;

        /// <summary>
        ///     New cell images share an array of this many cells (a slab), filled across renders until full,
        ///     so a new picture is a few hundred allocations rather than one per cell and a brush stroke is
        ///     one slot of the current slab. Small enough that a slab stays out of the large object heap,
        ///     and that a cell outliving its slab-mates holds on to little: the cache counts each cell's own
        ///     bytes, and a slab is freed once no cell of it is held anywhere.
        /// </summary>
        internal const int CellsPerSlab = 64;

        /// <summary>The slab the next new cell image goes into, and how much of it is taken. Per render thread.</summary>
        [ThreadStatic] private static byte[] _slab;

        [ThreadStatic] private static int _slabUsed;

        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, CellRendering>>>
            RenderedBitmapCache = new();

        private static readonly ContentCache<CellImage> CellImages = new(MinCellImageBudgetBytes);

        public CellImageBitmapRenderer(IConsoleOutput console)
            : base(console)
        {
        }

        protected override ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, CellRendering>>>
            Renderings => RenderedBitmapCache;

        protected override void Render(DrawingContextImpl context, CellRendering rendering,
            ReadOnlySpan<byte> pixels, int cellPixelWidth, int cellPixelHeight,
            IPlatformRenderInterface renderInterface)
        {
            CellImages.EnsureBudget(ScreensOfCellImages * 4L *
                                    context.PixelBuffer.Width * cellPixelWidth *
                                    context.PixelBuffer.Height * cellPixelHeight);
            RenderCells(pixels, rendering.Cells, cellPixelWidth, cellPixelHeight);
        }

        /// <summary>
        ///     Turns the visible pixels into a buffer of cells showing their cell images, reusing every image
        ///     already made for the same pixels.
        /// </summary>
        /// <param name="visibleBytes">BGRA pixels of exactly the visible cells, row by row.</param>
        /// <param name="cells">One cell per visible cell. Every cell is written; what it held is not read.</param>
        /// <param name="cellPixelWidth">The width of a cell in pixels.</param>
        /// <param name="cellPixelHeight">The height of a cell in pixels.</param>
        /// <exception cref="ArgumentException">
        ///     <paramref name="visibleBytes" /> is not the pixels of exactly the cells.
        /// </exception>
        internal static void RenderCells(ReadOnlySpan<byte> visibleBytes, PixelBuffer cells, int cellPixelWidth,
            int cellPixelHeight)
        {
            int cellsWide = cells.Width;
            int cellsHigh = cells.Height;
            int visibleWidth = cellsWide * cellPixelWidth;
            int cellBytes = cellPixelWidth * cellPixelHeight * 4;
            if (visibleBytes.Length != visibleWidth * cellsHigh * cellPixelHeight * 4)
                throw new ArgumentException("The pixels are not those of exactly the cells.", nameof(visibleBytes));

            // cut into a scratch cell first: most cells are in the cache, and only a new one gets a copy of its own
            Span<byte> cellBgra = cellBytes <= 4096 ? stackalloc byte[cellBytes] : new byte[cellBytes];
            byte[] slab = _slab;
            int slabUsed = _slabUsed;

            for (int cellY = 0; cellY < cellsHigh; cellY++)
            for (int cellX = 0; cellX < cellsWide; cellX++)
            {
                CopyBlock(visibleBytes, visibleWidth * 4, cellX * cellPixelWidth, cellY * cellPixelHeight,
                    cellPixelWidth, cellPixelHeight, cellBgra);

                UInt128 digest = XxHash128.HashToUInt128(cellBgra);
                var cellKey = new ContentKey(digest, cellPixelWidth, cellPixelHeight);
                if (!CellImages.TryGet(cellKey, out CellImage image))
                {
                    if (slab == null || slabUsed + cellBytes > slab.Length)
                    {
                        slab = _slab = GC.AllocateUninitializedArray<byte>(CellsPerSlab * cellBytes);
                        slabUsed = 0;
                    }

                    cellBgra.CopyTo(slab.AsSpan(slabUsed, cellBytes));
                    var made = new CellImage(slab, slabUsed, cellPixelWidth, cellPixelHeight, digest);
                    image = CellImages.GetOrAdd(cellKey, made, cellBytes);
                    if (ReferenceEquals(image, made))
                        _slabUsed = slabUsed += cellBytes;
                }

                // the picture as the cell's BACKGROUND, the foreground left free so glyphs drawn later land
                // over it. The color starts transparent: it is the paint translucent overlays accumulate
                cells[new PixelPoint(cellX, cellY)] = new Pixel(PixelForeground.Space,
                    new PixelBackground(Colors.Transparent, image));
            }
        }
    }
}
