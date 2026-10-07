//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        private static readonly ConditionalWeakTable<IBitmapImpl, Dictionary<BitmapQuantizedCacheKey, PixelBuffer>>
            RenderedBitmapCache = new();

        public SixelBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        public override void Draw(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelRect targetRect, PixelRect intersectedRect)
        {
            int cellPixelWidth = Context.ConsoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = Context.ConsoleWindowImpl.Console.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            var visibleRectInTarget = new PixelRect(
                intersectedRect.X - targetRect.X,
                intersectedRect.Y - targetRect.Y,
                intersectedRect.Width,
                intersectedRect.Height);

            PixelBuffer renderedBitmap = GetOrCreateRenderedBitmap(source, targetSize, () =>
            {
                var fullTargetSize = new PixelSize(targetSize.Width, targetSize.Height);

                using IBitmapImpl resizedBitmap = !source.PixelSize.Equals(targetSize)
                    ? renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality)
                    : null;

                IBitmapImpl bitmapToRead = resizedBitmap ?? source;
                var readableBitmap = (IReadableBitmapImpl)bitmapToRead;

                using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

                unsafe
                {
                    ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.AsRef<byte>((void*)frameBuffer.Address),
                        frameBuffer.RowBytes * frameBuffer.Size.Height);

                    byte[] fullBytes = CopyVisibleBitmapBytes(pixelBytes, frameBuffer.RowBytes,
                        fullTargetSize, 0, 0);

                    // Quantize the full image once to get a shared palette
                    var fullSixel = Sixel.CreateFromBitmap(fullBytes,
                        fullTargetSize.Width, fullTargetSize.Height,
                        cellPixelWidth, cellPixelHeight);

                    var bitmapBuffer = new PixelBuffer((ushort)targetRect.Width, (ushort)targetRect.Height);
                    byte[] cellBgrx = GC.AllocateUninitializedArray<byte>(cellPixelWidth * cellPixelHeight * 4);

                    for (int cellY = 0; cellY < targetRect.Height; cellY++)
                    for (int cellX = 0; cellX < targetRect.Width; cellX++)
                    {
                        FillCellBgrxBuffer(fullBytes, fullTargetSize.Width, cellX, cellY,
                            cellPixelWidth, cellPixelHeight, cellBgrx);

                        var cellSixel = Sixel.CreateFromBitmap(cellBgrx,
                            cellPixelWidth, cellPixelHeight,
                            cellPixelWidth, cellPixelHeight, fullSixel.Palette);
                        bitmapBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                            new PixelForeground(new Symbol(cellSixel, 1), Colors.Transparent),
                            PixelBackground.Transparent);
                    }

                    return bitmapBuffer;
                }
            });

            CopyRenderedBitmapTrackingDirtyRegions(renderedBitmap, intersectedRect, visibleRectInTarget);
        }

        private static byte[] CopyVisibleBitmapBytes(ReadOnlySpan<byte> pixelBytes, int rowBytes,
            PixelSize visibleTargetSize, int visibleOffsetX, int visibleOffsetY)
        {
            int visibleRowBytes = visibleTargetSize.Width * 4;
            byte[] visibleBytes = GC.AllocateUninitializedArray<byte>(visibleRowBytes * visibleTargetSize.Height);

            for (int row = 0; row < visibleTargetSize.Height; row++)
            {
                int sourceOffset = (visibleOffsetY + row) * rowBytes + visibleOffsetX * 4;
                int targetOffset = row * visibleRowBytes;
                pixelBytes.Slice(sourceOffset, visibleRowBytes)
                    .CopyTo(visibleBytes.AsSpan(targetOffset, visibleRowBytes));
            }

            return visibleBytes;
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

        private static PixelBuffer GetOrCreateRenderedBitmap(IBitmapImpl source, PixelSize targetSize,
            Func<PixelBuffer> factory)
        {
            IBitmapImpl cacheSource = GetCacheBitmapImpl(source);
            Dictionary<BitmapQuantizedCacheKey, PixelBuffer> perBitmap =
                RenderedBitmapCache.GetOrCreateValue(cacheSource);
            var key = new BitmapQuantizedCacheKey(cacheSource.Version, targetSize);

            if (perBitmap.TryGetValue(key, out PixelBuffer renderedBitmap))
                return renderedBitmap;

            // A new version (next animation frame) makes every older version's cells unreachable;
            // drop them so an animated bitmap does not keep one rendered buffer per frame.
            List<BitmapQuantizedCacheKey> staleKeys = null;
            foreach (BitmapQuantizedCacheKey existing in perBitmap.Keys)
                if (existing.Version != cacheSource.Version)
                    (staleKeys ??= new List<BitmapQuantizedCacheKey>()).Add(existing);

            if (staleKeys != null)
                foreach (BitmapQuantizedCacheKey staleKey in staleKeys)
                    perBitmap.Remove(staleKey);

            renderedBitmap = factory();
            perBitmap[key] = renderedBitmap;
            return renderedBitmap;
        }
    }
}