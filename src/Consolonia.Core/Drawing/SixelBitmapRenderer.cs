using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Renders a bitmap as per-cell sixel images with fine-grained dirty region tracking.
    ///     Requires a terminal with sixel support.
    /// </summary>
    /// <remarks>
    ///     Each cell's sixel is kept by the pixels it shows and reused wherever they show again. The
    ///     pixel buffer compares sixels by identity, so a cell whose pixels did not change stays the
    ///     same sixel, is not dirty, and is not written again: a change to the picture rewrites only
    ///     the cells it touched. Every sixel carries its own palette, so one made with an earlier
    ///     rendering's palette sits beside new ones unchanged.
    /// </remarks>
    internal sealed class SixelBitmapRenderer : CellBitmapRenderer<PixelBuffer>
    {
        /// <summary>Cell sixels kept for reuse: a few screens' worth.</summary>
        private const int CellSixelBudget = 32 * 1024;

        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, PixelBuffer>>>
            RenderedBitmapCache = new();

        private static readonly ContentCache<Sixel> CellSixels = new(CellSixelBudget);

        public SixelBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        protected override ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, PixelBuffer>>>
            Renderings => RenderedBitmapCache;

        protected override PixelBuffer Render(byte[] visibleBytes, PixelRect visibleCells, int cellPixelWidth,
            int cellPixelHeight, IPlatformRenderInterface renderInterface)
        {
            return RenderCells(visibleBytes, visibleCells.Width, visibleCells.Height, cellPixelWidth,
                cellPixelHeight);
        }

        protected override PixelBuffer CellsOf(PixelBuffer rendering)
        {
            return rendering;
        }

        /// <summary>
        ///     Turns the visible pixels into a buffer of per-cell sixels, reusing every cell already
        ///     made for the same pixels.
        /// </summary>
        /// <remarks>
        ///     Only the cells not made before are quantized, all of them together: they share one palette,
        ///     so the renderer can still join neighbours into one image, and each cell's pixels are a
        ///     slice of the quantizer's own result rather than a nearest-color match against it. A brush
        ///     stroke that changes one cell quantizes that one cell, not the whole picture.
        /// </remarks>
        /// <param name="visibleBytes">BGRX pixels of exactly the visible cells, row by row.</param>
        internal static PixelBuffer RenderCells(byte[] visibleBytes, int cellsWide, int cellsHigh,
            int cellPixelWidth, int cellPixelHeight)
        {
            int visibleWidth = cellsWide * cellPixelWidth;
            int cellPixels = cellPixelWidth * cellPixelHeight;
            int cellBytes = cellPixels * 4;

            var bitmapBuffer = new PixelBuffer((ushort)cellsWide, (ushort)cellsHigh);
            byte[] cellBgrx = GC.AllocateUninitializedArray<byte>(cellBytes);

            // The cells not in the cache, by distinct block: a flat canvas is one block repeated, and
            // every cell showing it must end up with the SAME sixel, or the first edit finds each cell
            // "changed" against the one instance the cache kept and re-sends the whole picture.
            List<(int CellX, int CellY, int Block)> newCells = null;
            Dictionary<ContentKey, int> blocks = null;
            List<(int CellX, int CellY, ContentKey Key)> blockFirstCells = null;

            for (int cellY = 0; cellY < cellsHigh; cellY++)
            for (int cellX = 0; cellX < cellsWide; cellX++)
            {
                CopyBlock(visibleBytes, visibleWidth * 4, cellX * cellPixelWidth, cellY * cellPixelHeight,
                    cellPixelWidth, cellPixelHeight, cellBgrx);

                ContentKey cellKey = ContentKey.Of(cellBgrx, cellPixelWidth, cellPixelHeight);
                if (CellSixels.TryGet(cellKey, out Sixel cellSixel))
                {
                    bitmapBuffer[new PixelPoint(cellX, cellY)] = CellPixel(cellSixel);
                    continue;
                }

                blocks ??= new Dictionary<ContentKey, int>();
                blockFirstCells ??= new List<(int, int, ContentKey)>();
                newCells ??= new List<(int, int, int)>();
                if (!blocks.TryGetValue(cellKey, out int block))
                {
                    block = blockFirstCells.Count;
                    blocks[cellKey] = block;
                    blockFirstCells.Add((cellX, cellY, cellKey));
                }

                newCells.Add((cellX, cellY, block));
            }

            if (newCells == null)
                return bitmapBuffer;

            // When every cell is new (a new picture, a scroll) the visible image is quantized as it is and
            // each block's indices are gathered from its first cell's rows. Otherwise the distinct blocks
            // are copied side by side and quantized as one image.
            bool allNew = newCells.Count == cellsWide * cellsHigh;
            byte[] palette, indexed;
            int paletteCount;
            if (allNew)
            {
                Sixel.Quantize(visibleBytes, out palette, out paletteCount, out indexed);
            }
            else
            {
                byte[] blocksBgrx = GC.AllocateUninitializedArray<byte>(blockFirstCells.Count * cellBytes);
                for (int i = 0; i < blockFirstCells.Count; i++)
                    CopyBlock(visibleBytes, visibleWidth * 4, blockFirstCells[i].CellX * cellPixelWidth,
                        blockFirstCells[i].CellY * cellPixelHeight, cellPixelWidth, cellPixelHeight,
                        blocksBgrx.AsSpan(i * cellBytes, cellBytes));
                Sixel.Quantize(blocksBgrx, out palette, out paletteCount, out indexed);
            }

            var made = new Sixel[blockFirstCells.Count];
            for (int i = 0; i < made.Length; i++)
            {
                byte[] pixels = GC.AllocateUninitializedArray<byte>(cellPixels);
                if (allNew)
                    for (int row = 0; row < cellPixelHeight; row++)
                        indexed.AsSpan(
                                (blockFirstCells[i].CellY * cellPixelHeight + row) * visibleWidth +
                                blockFirstCells[i].CellX * cellPixelWidth, cellPixelWidth)
                            .CopyTo(pixels.AsSpan(row * cellPixelWidth));
                else
                    indexed.AsSpan(i * cellPixels, cellPixels).CopyTo(pixels);

                var cellSixel = new Sixel(palette, paletteCount, pixels,
                    cellPixelWidth, cellPixelHeight, cellPixelWidth, cellPixelHeight);
                made[i] = CellSixels.GetOrAdd(blockFirstCells[i].Key, cellSixel, 1);
            }

            foreach ((int cellX, int cellY, int block) in newCells)
                bitmapBuffer[new PixelPoint(cellX, cellY)] = CellPixel(made[block]);

            return bitmapBuffer;
        }

        private static Pixel CellPixel(Sixel cellSixel)
        {
            return new Pixel(new PixelForeground(new Symbol(cellSixel), Colors.Transparent),
                PixelBackground.Transparent);
        }
    }
}
