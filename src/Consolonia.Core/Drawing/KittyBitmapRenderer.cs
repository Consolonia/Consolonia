using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
    internal sealed class KittyBitmapRenderer : CellBitmapRenderer<KittyBitmapRenderer.KittyRendering>
    {
        /// <summary>Tile size in cells: small enough that a stroke touches few, large enough to be few.</summary>
        internal const int TileColumns = 8;

        internal const int TileRows = 4;

        /// <summary>
        ///     Pixel bytes of tile images kept in the terminal at least. The budget grows to
        ///     <see cref="ScreensOfTileImages" /> screens on a larger screen, so one full-screen picture
        ///     never evicts its own tiles while transmitting them. The terminal has to keep at least as
        ///     much by id, or it drops tiles this cache still counts on and placing them again shows
        ///     nothing: XTerm.NET keeps the same three screens' worth (its MaxImageRegistryBytes floor).
        /// </summary>
        private const long MinTileImageBudgetBytes = 24L * 1024 * 1024;

        /// <summary>Screens' worth of tile images kept, so pictures shown again need not be resent.</summary>
        private const int ScreensOfTileImages = 3;

        private static readonly
            ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, KittyRendering>>>
            RenderedBitmapCache = new();

        /// <summary>
        ///     Image ids of tiles evicted from <see cref="TileImages" />. They are not deleted here: a tile
        ///     can be evicted while a picture that is not being redrawn still shows it. RenderTarget deletes
        ///     each one once no placement shows it (see <see cref="TryTakeEvictedImage" />).
        /// </summary>
        private static readonly ConcurrentQueue<int> EvictedTileImages = new();

        /// <summary>The terminal's image id for each tile content transmitted.</summary>
        private static readonly ContentCache<int> TileImages =
            new(MinTileImageBudgetBytes, imageId => EvictedTileImages.Enqueue(imageId));

        /// <summary>Whether tile images have been evicted that RenderTarget has not taken yet.</summary>
        internal static bool HasEvictedImages => !EvictedTileImages.IsEmpty;

        /// <summary>
        ///     Takes the next image id evicted from the tile cache. No rendering will place it again, so
        ///     once no placement on screen shows it, it is an orphan and must be deleted from the terminal.
        /// </summary>
        internal static bool TryTakeEvictedImage(out int imageId)
        {
            return EvictedTileImages.TryDequeue(out imageId);
        }

        public KittyBitmapRenderer(DrawingContextImpl context)
            : base(context)
        {
        }

        protected override ConditionalWeakTable<IBitmapImpl, List<KeyValuePair<RenderingKey, KittyRendering>>>
            Renderings => RenderedBitmapCache;

        /// <summary>
        ///     A rendering is reused only while every tile image it shows is still in the terminal; using one
        ///     keeps them all fresh, so a picture on screen does not lose tiles to the budget.
        /// </summary>
        protected override bool IsReusable(KittyRendering rendering)
        {
            foreach (ContentKey tileKey in rendering.TileKeys)
                if (!TileImages.TryGet(tileKey, out _))
                    return false;
            return true;
        }

        /// <summary>Cuts the visible pixels into tiles and transmits each tile not already in the terminal.</summary>
        protected override void Render(KittyRendering rendering, int cellPixelWidth, int cellPixelHeight,
            IPlatformRenderInterface renderInterface)
        {
            PixelRect visibleCells = rendering.VisibleCells;
            byte[] visibleBytes = rendering.Pixels;
            PixelBuffer cellBuffer = rendering.Cells;
            int visibleWidth = visibleCells.Width * cellPixelWidth;
            TileImages.EnsureBudget(ScreensOfTileImages * 4L *
                                    Context.PixelBuffer.Width * cellPixelWidth *
                                    Context.PixelBuffer.Height * cellPixelHeight);

            List<ContentKey> tileKeys = rendering.TileKeys;
            tileKeys.Clear();
            byte[] tileBytes = rendering.TileScratch(TileColumns * cellPixelWidth * TileRows * cellPixelHeight * 4);

            for (int tileY = 0; tileY < visibleCells.Height; tileY += TileRows)
            for (int tileX = 0; tileX < visibleCells.Width; tileX += TileColumns)
            {
                int columns = Math.Min(TileColumns, visibleCells.Width - tileX);
                int rows = Math.Min(TileRows, visibleCells.Height - tileY);
                var tileSize = new PixelSize(columns * cellPixelWidth, rows * cellPixelHeight);
                Span<byte> tile = tileBytes.AsSpan(0, tileSize.Width * tileSize.Height * 4);
                CopyBlock(visibleBytes, visibleWidth * 4, tileX * cellPixelWidth, tileY * cellPixelHeight,
                    tileSize.Width, tileSize.Height, tile);

                ContentKey tileKey = ContentKey.Of(tile, tileSize.Width, tileSize.Height);
                tileKeys.Add(tileKey);
                if (!TileImages.TryGet(tileKey, out int imageId))
                {
                    imageId = KittyGraphics.AllocateImageId();
                    byte[] imageData = EncodeImageData(tile, tileSize, renderInterface,
                        out KittyImageFormat imageFormat);
                    Context.ConsoleWindowImpl.Console.WriteText(
                        KittyGraphics.BuildTransmitSequence(imageId, tileSize.Width, tileSize.Height, imageData,
                            imageFormat));
                    TileImages.GetOrAdd(tileKey, imageId, tile.Length);
                }

                // image as cell BACKGROUND, foreground left free so glyphs drawn later composite
                // over the picture. The background color starts transparent: it is the wash that
                // translucent overlays accumulate, which RenderTarget lays over the image, and the
                // terminal cell itself is written black (occluding what the picture was drawn over)
                for (int cellY = 0; cellY < rows; cellY++)
                for (int cellX = 0; cellX < columns; cellX++)
                    cellBuffer[new PixelPoint(tileX + cellX, tileY + cellY)] = new Pixel(
                        PixelForeground.Space,
                        new PixelBackground(Colors.Transparent,
                            new KittyTile(imageId, (ushort)cellX, (ushort)cellY)));
            }
        }

        private static byte[] EncodeImageData(ReadOnlySpan<byte> bgra, PixelSize size,
            IPlatformRenderInterface renderInterface,
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

        private static unsafe byte[] TryEncodePng(ReadOnlySpan<byte> bgra, PixelSize size,
            IPlatformRenderInterface renderInterface)
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

        /// <summary>A cell rendering with the tiles it shows, and room to cut one out.</summary>
        internal sealed class KittyRendering : CellRendering
        {
            private byte[] _tileScratch;

            /// <summary>The tiles the cells show, which must still be in the terminal to reuse them.</summary>
            public List<ContentKey> TileKeys { get; } = new();

            /// <summary>Room for the pixels of one tile, at least <paramref name="bytes" />, kept between renders.</summary>
            internal byte[] TileScratch(int bytes)
            {
                if (_tileScratch == null || _tileScratch.Length < bytes)
                    _tileScratch = GC.AllocateUninitializedArray<byte>(bytes);
                return _tileScratch;
            }
        }
    }
}
