//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

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
    internal sealed class SixelBitmapRenderer : BitmapRenderer
    {
        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<BitmapQuantizedCacheKey, PixelBuffer>>>
            RenderedBitmapCache = new();

        public SixelBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        public override void Draw(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelRect targetRect, PixelRect intersectedRect, BitmapInterpolationMode interpolationMode)
        {
            int cellPixelWidth = Context.ConsoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = Context.ConsoleWindowImpl.Console.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            PixelRect visibleCells = OnScreenCellsInTarget(targetRect);
            var key = new BitmapQuantizedCacheKey(GetCacheBitmapImpl(source).Version, targetSize, visibleCells,
                interpolationMode);

            // only the visible cells are rendered, so the rendering starts at the first of them
            PixelBuffer renderedBitmap = GetOrRender(RenderedBitmapCache, source, key, () =>
            {
                var visibleSize = new PixelSize(visibleCells.Width * cellPixelWidth,
                    visibleCells.Height * cellPixelHeight);
                byte[] visibleBytes = GetVisiblePixels(source, renderInterface, targetSize,
                    new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                        visibleSize.Width, visibleSize.Height),
                    interpolationMode);

                // Quantize the visible image once to get a shared palette
                var fullSixel = Sixel.CreateFromBitmap(visibleBytes,
                    visibleSize.Width, visibleSize.Height,
                    cellPixelWidth, cellPixelHeight);

                var bitmapBuffer = new PixelBuffer((ushort)visibleCells.Width, (ushort)visibleCells.Height);
                byte[] cellBgrx = GC.AllocateUninitializedArray<byte>(cellPixelWidth * cellPixelHeight * 4);

                for (int cellY = 0; cellY < visibleCells.Height; cellY++)
                for (int cellX = 0; cellX < visibleCells.Width; cellX++)
                {
                    FillCellBgrxBuffer(visibleBytes, visibleSize.Width, cellX, cellY,
                        cellPixelWidth, cellPixelHeight, cellBgrx);

                    var cellSixel = Sixel.CreateFromBitmap(cellBgrx,
                        cellPixelWidth, cellPixelHeight,
                        cellPixelWidth, cellPixelHeight, fullSixel.Palette);
                    bitmapBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                        new PixelForeground(new Symbol(cellSixel, 1), Colors.Transparent),
                        PixelBackground.Transparent);
                }

                return bitmapBuffer;
            });

            CopyRenderedBitmapTrackingDirtyRegions(renderedBitmap, intersectedRect,
                IntersectedRectInRendering(targetRect, visibleCells, intersectedRect));
        }

        private static void FillCellBgrxBuffer(ReadOnlySpan<byte> bgrx, int imageWidth, int cellX, int cellY,
            int cellPixelWidth, int cellPixelHeight, Span<byte> cellBgrx)
        {
            int bytesPerPixel = 4;
            int srcRowBytes = imageWidth * bytesPerPixel;
            int cellRowBytes = cellPixelWidth * bytesPerPixel;
            for (int row = 0; row < cellPixelHeight; row++)
            {
                int sourceOffset = (cellY * cellPixelHeight + row) * srcRowBytes +
                                   cellX * cellPixelWidth * bytesPerPixel;
                int targetOffset = row * cellRowBytes;
                bgrx.Slice(sourceOffset, cellRowBytes)
                    .CopyTo(cellBgrx.Slice(targetOffset, cellRowBytes));
            }
        }
    }
}
