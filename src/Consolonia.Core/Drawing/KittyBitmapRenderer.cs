//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Renders a bitmap via the kitty graphics protocol. Pixels are transmitted to the terminal once
    ///     per bitmap version and visible part; every covered cell then carries a <see cref="KittyTile" /> in
    ///     its BACKGROUND, which RenderTarget coalesces into classic placements below text (z=-2). Glyphs
    ///     drawn later composite over the picture, an opaque background evicts it, and pixel buffer
    ///     diffing and occlusion work unchanged while redraws cost no pixel retransmission.
    /// </summary>
    internal sealed class KittyBitmapRenderer : BitmapRenderer
    {
        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<BitmapQuantizedCacheKey, KittyRenderedBitmap>>>
            RenderedBitmapCache = new();

        public KittyBitmapRenderer(DrawingContextImpl context)
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
            PixelRect visibleCells = VisibleCellsInTarget(targetRect, intersectedRect);
            var key = new BitmapQuantizedCacheKey(GetCacheBitmapImpl(source).Version, targetSize, visibleCells,
                interpolationMode);

            // A new version (next animation frame) or visible part takes a fresh image id: a classic
            // placement binds to the image it was created against, so the tiles must be re-keyed.
            // Evicted renderings are deleted; GC'd bitmaps are cleaned up by KittyDeleteAllImages on restore.
            KittyRenderedBitmap renderedBitmap = GetOrRender(RenderedBitmapCache, source, key,
                () => TransmitAndCreateCells(source, renderInterface, targetSize, visibleCells, interpolationMode,
                    cellPixelWidth, cellPixelHeight),
                evicted => Context.ConsoleWindowImpl.Console.WriteText(
                    KittyGraphics.BuildDeleteSequence(evicted.ImageId)));

            // only the visible cells are rendered, so the rendering starts at the first of them
            CopyRenderedBitmapTrackingDirtyRegions(renderedBitmap.Cells, intersectedRect,
                new PixelRect(0, 0, visibleCells.Width, visibleCells.Height));
        }

        private KittyRenderedBitmap TransmitAndCreateCells(IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelSize targetSize, PixelRect visibleCells,
            BitmapInterpolationMode interpolationMode, int cellPixelWidth, int cellPixelHeight)
        {
            var visibleSize = new PixelSize(visibleCells.Width * cellPixelWidth,
                visibleCells.Height * cellPixelHeight);
            byte[] visibleBytes = GetVisiblePixels(source, renderInterface, targetSize,
                new PixelRect(visibleCells.X * cellPixelWidth, visibleCells.Y * cellPixelHeight,
                    visibleSize.Width, visibleSize.Height),
                interpolationMode);
            byte[] imageData = EncodeImageData(visibleBytes, visibleSize, renderInterface,
                out KittyImageFormat imageFormat);

            int imageId = KittyGraphics.AllocateImageId();
            Context.ConsoleWindowImpl.Console.WriteText(
                KittyGraphics.BuildTransmitSequence(imageId, visibleSize.Width, visibleSize.Height, imageData,
                    imageFormat));

            var cellBuffer = new PixelBuffer((ushort)visibleCells.Width, (ushort)visibleCells.Height);

            // image as cell BACKGROUND, foreground left free so glyphs drawn later composite
            // over the picture. The background color starts transparent: it is the wash that
            // translucent overlays accumulate, which RenderTarget lays over the image, and the
            // terminal cell itself is written black (occluding what the picture was drawn over)
            for (int cellY = 0; cellY < visibleCells.Height; cellY++)
            for (int cellX = 0; cellX < visibleCells.Width; cellX++)
                cellBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                    new PixelForeground(Symbol.Space, Colors.Transparent),
                    new PixelBackground(Colors.Transparent,
                        new KittyTile(imageId, (ushort)cellX, (ushort)cellY)));

            return new KittyRenderedBitmap(imageId, cellBuffer);
        }

        private static byte[] EncodeImageData(byte[] bgra, PixelSize size, IPlatformRenderInterface renderInterface,
            out KittyImageFormat format)
        {
            // PNG is far smaller on the wire than raw RGBA (a full screen image is ~7MB raw, over
            // 9MB base64), which matters for the first paint and for every animation frame
            byte[] png = TryEncodePng(bgra, size, renderInterface);
            if (png != null)
            {
                format = KittyImageFormat.Png;
                return png;
            }

            // fallback for render interfaces which cannot make an encodable bitmap
            format = KittyImageFormat.Rgba;
            return ConvertBgraToRgba(bgra, size.Width * 4, size);
        }

        private static unsafe byte[] TryEncodePng(byte[] bgra, PixelSize size, IPlatformRenderInterface renderInterface)
        {
            try
            {
                // Avalonia saves as PNG via Skia
                fixed (byte* pixels = bgra)
                {
                    using IBitmapImpl bitmap = renderInterface.LoadBitmap(PixelFormat.Bgra8888, AlphaFormat.Premul,
                        (IntPtr)pixels, size, new Vector(96, 96), size.Width * 4);
                    if (bitmap == null)
                        return null;
                    using var stream = new MemoryStream();
                    bitmap.Save(stream);
                    return stream.ToArray();
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or NotImplementedException
                                                  or ConsoloniaNotSupportedException)
            {
                return null;
            }
        }

        private static byte[] ConvertBgraToRgba(ReadOnlySpan<byte> bgra, int rowBytes, PixelSize size)
        {
            byte[] rgba = GC.AllocateUninitializedArray<byte>(size.Width * size.Height * 4);
            for (int row = 0; row < size.Height; row++)
            {
                int sourceOffset = row * rowBytes;
                int targetOffset = row * size.Width * 4;
                for (int x = 0; x < size.Width; x++)
                {
                    rgba[targetOffset] = bgra[sourceOffset + 2];
                    rgba[targetOffset + 1] = bgra[sourceOffset + 1];
                    rgba[targetOffset + 2] = bgra[sourceOffset];
                    rgba[targetOffset + 3] = bgra[sourceOffset + 3];
                    sourceOffset += 4;
                    targetOffset += 4;
                }
            }

            return rgba;
        }

        private sealed class KittyRenderedBitmap
        {
            public KittyRenderedBitmap(int imageId, PixelBuffer cells)
            {
                ImageId = imageId;
                Cells = cells;
            }

            public int ImageId { get; }

            public PixelBuffer Cells { get; }
        }
    }
}
