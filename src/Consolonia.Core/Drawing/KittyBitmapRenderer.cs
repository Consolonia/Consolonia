//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Concurrent;
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
    ///     Renders a bitmap via the kitty graphics protocol. The on-screen part of the picture is cut into
    ///     tiles of a few cells, and each tile is transmitted to the terminal as its own image, once per
    ///     distinct content: a change to the picture retransmits only the tiles it touched, and tiles
    ///     that look alike (a blank canvas) share one image. Every covered cell carries a
    ///     <see cref="KittyTile" /> in its BACKGROUND, which RenderTarget coalesces into classic placements
    ///     below text (z=-2); a tile whose image is unchanged keeps its placement. Glyphs drawn later
    ///     composite over the picture, an opaque background evicts it, and pixel buffer diffing and
    ///     occlusion work unchanged.
    /// </summary>
    internal sealed class KittyBitmapRenderer : BitmapRenderer
    {
        /// <summary>Tile size in cells: small enough that a stroke touches few, large enough to be few.</summary>
        internal const int TileColumns = 8;

        internal const int TileRows = 4;

        /// <summary>
        ///     Pixel bytes of tile images kept in the terminal. A few screens' worth: XTerm.NET, for one,
        ///     holds 64MB of live images. Tiles in use are touched on every draw, so only ones off screen
        ///     age out.
        /// </summary>
        private const long TileImageBudgetBytes = 24L * 1024 * 1024;

        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<BitmapQuantizedCacheKey, KittyRenderedBitmap>>>
            RenderedBitmapCache = new();

        /// <summary>Image ids of evicted tiles, deleted from the terminal on the next draw.</summary>
        private static readonly ConcurrentQueue<int> EvictedTileImages = new();

        /// <summary>The terminal's image id for each tile content transmitted.</summary>
        private static readonly ContentCache<int> TileImages =
            new(TileImageBudgetBytes, imageId => EvictedTileImages.Enqueue(imageId));

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
            PixelRect visibleCells = OnScreenCellsInTarget(targetRect);
            var key = new BitmapQuantizedCacheKey(GetCacheBitmapImpl(source).Version, targetSize, visibleCells,
                interpolationMode);

            // A rendering is reused only while every tile image it shows is still in the terminal;
            // using one keeps them all fresh, so a picture on screen does not lose tiles to the budget.
            KittyRenderedBitmap renderedBitmap = GetOrRender(RenderedBitmapCache, source, key,
                () => TransmitAndCreateCells(source, renderInterface, targetSize, visibleCells, interpolationMode,
                    cellPixelWidth, cellPixelHeight),
                validate: rendering => rendering.TileKeys.TrueForAll(tileKey => TileImages.TryGet(tileKey, out _)));

            while (EvictedTileImages.TryDequeue(out int evictedImageId))
                Context.ConsoleWindowImpl.Console.WriteText(KittyGraphics.BuildDeleteSequence(evictedImageId));

            // only the on-screen cells are rendered, so the rendering starts at the first of them
            CopyRenderedBitmapTrackingDirtyRegions(renderedBitmap.Cells, intersectedRect,
                IntersectedRectInRendering(targetRect, visibleCells, intersectedRect));
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

            var cellBuffer = new PixelBuffer((ushort)visibleCells.Width, (ushort)visibleCells.Height);
            var tileKeys = new List<ContentKey>();
            byte[] tileBytes = GC.AllocateUninitializedArray<byte>(
                TileColumns * cellPixelWidth * TileRows * cellPixelHeight * 4);

            for (int tileY = 0; tileY < visibleCells.Height; tileY += TileRows)
            for (int tileX = 0; tileX < visibleCells.Width; tileX += TileColumns)
            {
                int columns = Math.Min(TileColumns, visibleCells.Width - tileX);
                int rows = Math.Min(TileRows, visibleCells.Height - tileY);
                var tileSize = new PixelSize(columns * cellPixelWidth, rows * cellPixelHeight);
                Span<byte> tile = tileBytes.AsSpan(0, tileSize.Width * tileSize.Height * 4);
                CopyTile(visibleBytes, visibleSize.Width, tileX * cellPixelWidth, tileY * cellPixelHeight,
                    tileSize, tile);

                ContentKey tileKey = ContentKey.Of(tile, tileSize.Width, tileSize.Height);
                tileKeys.Add(tileKey);
                if (!TileImages.TryGet(tileKey, out int imageId))
                {
                    imageId = KittyGraphics.AllocateImageId();
                    byte[] imageData = EncodeImageData(tile.ToArray(), tileSize, renderInterface,
                        out KittyImageFormat imageFormat);
                    Context.ConsoleWindowImpl.Console.WriteText(
                        KittyGraphics.BuildTransmitSequence(imageId, tileSize.Width, tileSize.Height, imageData,
                            imageFormat));
                    TileImages.Add(tileKey, imageId, tile.Length);
                }

                // image as cell BACKGROUND, foreground left free so glyphs drawn later composite
                // over the picture. The background color starts transparent: it is the wash that
                // translucent overlays accumulate, which RenderTarget lays over the image, and the
                // terminal cell itself is written black (occluding what the picture was drawn over)
                for (int cellY = 0; cellY < rows; cellY++)
                for (int cellX = 0; cellX < columns; cellX++)
                    cellBuffer[new PixelPoint(tileX + cellX, tileY + cellY)] = new Pixel(
                        new PixelForeground(Symbol.Space, Colors.Transparent),
                        new PixelBackground(Colors.Transparent,
                            new KittyTile(imageId, (ushort)cellX, (ushort)cellY)));
            }

            return new KittyRenderedBitmap(tileKeys, cellBuffer);
        }

        private static void CopyTile(byte[] bgra, int imageWidth, int x, int y, PixelSize tileSize,
            Span<byte> tile)
        {
            int rowBytes = tileSize.Width * 4;
            for (int row = 0; row < tileSize.Height; row++)
                bgra.AsSpan(((y + row) * imageWidth + x) * 4, rowBytes).CopyTo(tile.Slice(row * rowBytes, rowBytes));
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
            public KittyRenderedBitmap(List<ContentKey> tileKeys, PixelBuffer cells)
            {
                TileKeys = tileKeys;
                Cells = cells;
            }

            /// <summary>The tiles the cells show, which must still be in the terminal to reuse them.</summary>
            public List<ContentKey> TileKeys { get; }

            public PixelBuffer Cells { get; }
        }
    }
}
