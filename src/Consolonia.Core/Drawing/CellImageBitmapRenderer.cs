using System;
using System.Collections.Generic;
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

        protected override void Render(DrawingContextImpl context, CellRendering rendering, int cellPixelWidth,
            int cellPixelHeight, IPlatformRenderInterface renderInterface)
        {
            CellImages.EnsureBudget(ScreensOfCellImages * 4L *
                                    context.PixelBuffer.Width * cellPixelWidth *
                                    context.PixelBuffer.Height * cellPixelHeight);
            RenderCells(rendering.Pixels, rendering.Cells, cellPixelWidth, cellPixelHeight);
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
        internal static void RenderCells(byte[] visibleBytes, PixelBuffer cells, int cellPixelWidth,
            int cellPixelHeight)
        {
            int cellsWide = cells.Width;
            int cellsHigh = cells.Height;
            int visibleWidth = cellsWide * cellPixelWidth;
            int cellBytes = cellPixelWidth * cellPixelHeight * 4;
            if (visibleBytes.Length != visibleWidth * cellsHigh * cellPixelHeight * 4)
                throw new ArgumentException("The pixels are not those of exactly the cells.", nameof(visibleBytes));

            // cut into a scratch cell first: most cells are in the cache, and only a new one gets a copy of its own
            byte[] cellBgra = GC.AllocateUninitializedArray<byte>(cellBytes);

            for (int cellY = 0; cellY < cellsHigh; cellY++)
            for (int cellX = 0; cellX < cellsWide; cellX++)
            {
                CopyBlock(visibleBytes, visibleWidth * 4, cellX * cellPixelWidth, cellY * cellPixelHeight,
                    cellPixelWidth, cellPixelHeight, cellBgra);

                ContentKey cellKey = ContentKey.Of(cellBgra, cellPixelWidth, cellPixelHeight);
                if (!CellImages.TryGet(cellKey, out CellImage image))
                {
                    var made = new CellImage((byte[])cellBgra.Clone(), cellPixelWidth, cellPixelHeight);
                    image = CellImages.GetOrAdd(cellKey, made, cellBytes);
                }

                // the picture as the cell's BACKGROUND, the foreground left free so glyphs drawn later land
                // over it. The color starts transparent: it is the paint translucent overlays accumulate
                cells[new PixelPoint(cellX, cellY)] = new Pixel(PixelForeground.Space,
                    new PixelBackground(Colors.Transparent, image));
            }
        }
    }
}
