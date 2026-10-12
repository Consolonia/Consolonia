using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.Compression;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Shows image cells through the kitty graphics protocol. A kitty picture is a <em>placement</em>:
    ///     an object below the text, not the content of a cell. So an image cell is written as an ordinary
    ///     cell on the default background, and at the end of every frame the image cells on screen are
    ///     gathered into blocks, each block transmitted once per distinct content and placed where it
    ///     is, and the placements reconciled with the ones already live.
    /// </summary>
    /// <remarks>
    ///     Blocks, because kitty cannot portably change part of an image once transmitted, so the image is
    ///     the unit of retransmission: one image per picture meant a brush stroke re-sent the whole picture.
    ///     A block of a few cells is the compromise between how many images a screen is and how much a
    ///     change re-sends. Blocks are aligned to the screen grid; a block's content is the images of its
    ///     cells, so a block that looks like one already in the terminal reuses its image. And a block
    ///     image is good for any subset of its cells, since a placement only ever covers image cells: a
    ///     dialog crossing a block does not remake the block's image, it crops the placements.
    /// </remarks>
    internal sealed class KittyConsoleImageEncoder : IConsoleImageEncoder
    {
        /// <summary>
        ///     Block size in cells: small enough that a stroke touches few, large enough to be few.
        ///     CONSOLONIA_KITTY_BLOCK=&lt;columns&gt;x&lt;rows&gt; overrides it, to try another on a terminal.
        /// </summary>
        internal const int DefaultBlockColumns = 8;

        internal const int DefaultBlockRows = 4;

        internal int BlockColumns { get; }

        internal int BlockRows { get; }

        /// <summary>
        ///     Pixel bytes of block images kept in the terminal at least. The budget grows to
        ///     <see cref="ScreensOfBlockImages" /> screens on a larger screen, so one full-screen picture
        ///     never evicts its own blocks while transmitting them. The terminal has to keep at least as
        ///     much by id, or it drops images this cache still counts on and placing them again shows
        ///     nothing: XTerm.NET keeps the same three screens' worth (its MaxImageRegistryBytes floor).
        /// </summary>
        private const long MinBlockImageBudgetBytes = 24L * 1024 * 1024;

        /// <summary>Screens' worth of block images kept, so pictures shown again need not be resent.</summary>
        private const int ScreensOfBlockImages = 3;

        private readonly AnsiConsoleOutput _output;

        /// <summary>
        ///     The terminal's image id for each block content transmitted. Evicted ids are not deleted at
        ///     once: a block can be evicted while a picture that is not being redrawn still shows it. The
        ///     frame's end deletes each one once no placement shows it (<see cref="DeleteOrphanedBlockImages" />).
        /// </summary>
        private readonly ContentCache<int> _blockImages;

        private readonly HashSet<int> _evictedBlockImages = new();

        /// <summary>
        ///     The image last used at each block position and what its cells showed, image cells by
        ///     their digest and text cells as zero. The image is reused while every cell now showing the
        ///     picture shows the same as it did: the cells that became text are simply not placed. That is
        ///     what keeps a dialog dragged across a picture from transmitting a new image for every block
        ///     along its edges on every frame.
        /// </summary>
        private readonly Dictionary<(int X, int Y), BlockRecord> _blocksAtPositions = new();

        private UInt128[] _blockDigests;

        // placements of blocks at z=-2, keyed by the block's image and screen position, diffed across
        // frames so only changed blocks cross the wire; scratch collects the next frame, then the two swap
        private Dictionary<BlockPlacement, int> _placements = new();
        private Dictionary<BlockPlacement, int> _placementsScratch = new();

        // wash placements tinting the picture (a translucent paint over a rectangle of image cells), keyed
        // by the rectangle they cover and their color, diffed across frames the same way
        private Dictionary<(WashRect Rect, Color Paint), int> _washPlacements = new();
        private Dictionary<(WashRect Rect, Color Paint), int> _washPlacementsScratch = new();

        // wash images live in the terminal, by color, one pixel per cell of the screen they were sent
        // for; freed once no wash placement uses them
        private readonly Dictionary<Color, (int ImageId, ushort Columns, ushort Rows)> _washImages = new();

        // scratch for the bookkeeping, kept between frames so it is not reallocated
        private readonly HashSet<int> _placedBlockImages = new();
        private readonly List<int> _orphanedBlockImages = new();
        private readonly HashSet<Color> _washesInUse = new();
        private readonly List<Color> _washesToDelete = new();
        private readonly List<(WashRect Rect, Color Paint)> _washPlacementsToRemove = new();
        private bool[] _rectangleVisited;
        private bool[] _blockVisited;
        private byte[] _blockScratch;

        /// <summary>
        ///     Placements to delete at the end of the next frame, once it has placed theirs anew: deleted
        ///     first, the screen would show nothing where the picture was until the frame got that far.
        /// </summary>
        private readonly List<(int ImageId, int PlacementId)> _placementsToDeleteAfterFrame = new();

        public KittyConsoleImageEncoder(AnsiConsoleOutput output)
        {
            _output = output;
            _blockImages = new ContentCache<int>(MinBlockImageBudgetBytes, imageId => _evictedBlockImages.Add(imageId));
            (BlockColumns, BlockRows) = BlockSize(Environment.GetEnvironmentVariable("CONSOLONIA_KITTY_BLOCK"));
        }

        /// <summary>The block size an override names, as "columns x rows", or the default.</summary>
        internal static (int Columns, int Rows) BlockSize(string overrideValue)
        {
            string[] parts = overrideValue?.Trim().ToUpperInvariant().Split('X') ?? Array.Empty<string>();
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int columns) && int.TryParse(parts[1], out int rows) &&
                columns > 0 && rows > 0)
                return (columns, rows);
            return (DefaultBlockColumns, DefaultBlockRows);
        }

        /// <summary>The image at a block position and the digest of each of its cells (zero for a text cell).</summary>
        private readonly record struct BlockRecord(ContentKey Key, UInt128[] Digests);

        /// <summary>A rectangle of image cells within one block, placed as a crop of the block's image.</summary>
        private readonly record struct BlockPlacement(
            int ImageId,
            ushort ScreenX,
            ushort ScreenY,
            ushort Width,
            ushort Height);

        private readonly record struct WashRect(ushort ScreenX, ushort ScreenY, ushort Width, ushort Height);

        public void BeginFrame()
        {
        }

        /// <summary>
        ///     The cell is written as text on the terminal's default background, so the placement below
        ///     shows through. Konsole draws below-text (z&lt;0) placements under any explicit cell
        ///     background, so painting the cell a color, even black, hides the picture there; kitty draws
        ///     them above either way.
        /// </summary>
        public void WriteCell(PixelBufferCoordinate position, in Pixel pixel)
        {
            _output.WriteTextCell(position, in pixel, true);
        }

        public void EndFrame(PixelBuffer screen)
        {
            EmitPlacements(screen);
            DeleteWhatTheFrameReplaced();
        }

        /// <summary>
        ///     The placements on screen can no longer be counted on (the screen was resized, and terminals
        ///     differ in what becomes of placements then; or another program had the terminal), so the
        ///     frame places every one again from the cells and deletes the old ones at its end. The images
        ///     stay in the terminal, so a picture is placed from the blocks already there rather than
        ///     transmitted and decoded anew, and where the terminal still shows it nothing changes on screen.
        /// </summary>
        public void Reset()
        {
            foreach ((BlockPlacement placement, int placementId) in _placements)
                _placementsToDeleteAfterFrame.Add((placement.ImageId, placementId));
            _placements.Clear();

            foreach (((WashRect _, Color paint), int placementId) in _washPlacements)
                _placementsToDeleteAfterFrame.Add((_washImages[paint].ImageId, placementId));
            _washPlacements.Clear();
        }

        /// <summary>
        ///     Deletes the placements <see cref="Reset" /> left for the end of the frame, now that it has
        ///     placed their replacements: where the terminal still showed the old picture, the new one is
        ///     there before the old one goes.
        /// </summary>
        private void DeleteWhatTheFrameReplaced()
        {
            foreach ((int imageId, int placementId) in _placementsToDeleteAfterFrame)
                _output.WriteText(KittyGraphics.BuildDeletePlacementSequence(imageId, placementId));
            _placementsToDeleteAfterFrame.Clear();
        }

        /// <summary>
        ///     Walks the screen in blocks, transmits each block of image cells whose content the terminal
        ///     does not hold yet, places the rectangles of image cells of each block not already placed
        ///     (crops of the block's image at their cells, z=-2 so text drawn on the covered cells composites
        ///     over the picture), and deletes the placements no longer backed by cells, with the image data
        ///     retained for cheap re-placement. A rectangle of image cells under one paint gets a wash
        ///     placement of a translucent image at z=-1, which is how a modal backdrop dims a picture.
        /// </summary>
        /// <remarks>
        ///     Only image cells are ever under a placement. A placement below the text is drawn above the
        ///     cells' background colors, and a terminal need not show a cell's color through the image's
        ///     transparent pixels: placing a whole block put the picture over a dialog's body and left only
        ///     its border glyphs.
        /// </remarks>
        private void EmitPlacements(PixelBuffer screen)
        {
            int cellPixelWidth = _output.CellPixelWidth;
            int cellPixelHeight = _output.CellPixelHeight;
            _blockImages.EnsureBudget(ScreensOfBlockImages * 4L *
                                      screen.Width * cellPixelWidth * screen.Height * cellPixelHeight);

            Dictionary<BlockPlacement, int> live = _placements;
            Dictionary<BlockPlacement, int> next = _placementsScratch;
            next.Clear();
            int screenWidth = screen.Width;
            bool[] visited = CellRectangles.Scratch(ref _blockVisited, screenWidth * screen.Height);

            for (int blockY = 0; blockY < screen.Height; blockY += BlockRows)
            for (int blockX = 0; blockX < screen.Width; blockX += BlockColumns)
            {
                int columns = Math.Min(BlockColumns, screen.Width - blockX);
                int rows = Math.Min(BlockRows, screen.Height - blockY);
                if (!TryGetBlockDigests(screen, blockX, blockY, columns, rows, out Span<UInt128> digests))
                    continue;

                int imageId;
                if (_blocksAtPositions.TryGetValue((blockX, blockY), out BlockRecord record) &&
                    ShowsTheSame(record.Digests, digests) && _blockImages.TryGet(record.Key, out imageId))
                {
                    // the image last used here still shows every picture cell as it is now
                }
                else
                {
                    ContentKey blockKey = ContentKey.Of(MemoryMarshal.AsBytes(digests),
                        columns * cellPixelWidth, rows * cellPixelHeight);
                    if (!_blockImages.TryGet(blockKey, out imageId))
                    {
                        imageId = KittyGraphics.AllocateImageId();
                        TransmitBlock(imageId, screen, blockX, blockY, columns, rows, cellPixelWidth,
                            cellPixelHeight);
                        _blockImages.GetOrAdd(blockKey, imageId,
                            columns * cellPixelWidth * rows * cellPixelHeight * 4L);
                    }

                    _blocksAtPositions[(blockX, blockY)] = new BlockRecord(blockKey, digests.ToArray());
                }

                var block = new ImageCellsInBlock(screen, blockX, blockY, columns, rows);
                for (int y = blockY; y < blockY + rows; y++)
                for (int x = blockX; x < blockX + columns; x++)
                {
                    if (visited[y * screenWidth + x] || screen.CellAt(x, y).Background.Image == null)
                        continue;

                    (int width, int height) = CellRectangles.GrowRectangle(block, visited, screenWidth,
                        screen.Height, x, y, false);
                    var placement = new BlockPlacement(imageId, (ushort)x, (ushort)y, (ushort)width,
                        (ushort)height);
                    if (live.Remove(placement, out int placementId))
                    {
                        next[placement] = placementId;
                        continue;
                    }

                    placementId = KittyGraphics.AllocatePlacementId();
                    next[placement] = placementId;
                    _output.SetCaretPosition(new PixelBufferCoordinate((ushort)x, (ushort)y));
                    _output.WriteText(KittyGraphics.BuildRectPlacementSequence(imageId, placementId,
                        (x - blockX) * cellPixelWidth, (y - blockY) * cellPixelHeight,
                        width * cellPixelWidth, height * cellPixelHeight));
                }
            }

            // whatever is left in the live set has no cells backing it anymore
            foreach (KeyValuePair<BlockPlacement, int> stale in live)
                _output.WriteText(KittyGraphics.BuildDeletePlacementSequence(stale.Key.ImageId, stale.Value));
            live.Clear();
            (_placements, _placementsScratch) = (next, live);

            EmitWashes(screen);
            DeleteOrphanedBlockImages();
            FreeUnusedWashImages();
        }

        /// <summary>
        ///     What a block's cells show: each image cell's digest, zero for a cell without an image, row by
        ///     row. False when no cell of the block shows an image.
        /// </summary>
        private bool TryGetBlockDigests(PixelBuffer screen, int blockX, int blockY, int columns, int rows,
            out Span<UInt128> digests)
        {
            int cells = columns * rows;
            if (_blockDigests == null || _blockDigests.Length < cells)
                _blockDigests = new UInt128[BlockColumns * BlockRows];
            digests = _blockDigests.AsSpan(0, cells);

            bool anyImage = false;
            int i = 0;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++, i++)
            {
                CellImage image = screen.CellAt(blockX + column, blockY + row).Background.Image;
                digests[i] = image?.Key.Digest ?? default;
                anyImage |= image != null;
            }

            return anyImage;
        }

        /// <summary>
        ///     Whether an image whose cells showed <paramref name="recorded" /> shows every cell that now
        ///     has a picture (<paramref name="current" />, zero for a text cell) as it is now.
        /// </summary>
        private static bool ShowsTheSame(UInt128[] recorded, ReadOnlySpan<UInt128> current)
        {
            if (recorded.Length != current.Length)
                return false;
            for (int i = 0; i < current.Length; i++)
                if (current[i] != default && current[i] != recorded[i])
                    return false;
            return true;
        }

        /// <summary>
        ///     Sends the block as one RGBA image, its cells' pixels side by side and a cell without an image
        ///     transparent, zlib compressed.
        /// </summary>
        private void TransmitBlock(int imageId, PixelBuffer screen, int blockX, int blockY, int columns, int rows,
            int cellPixelWidth, int cellPixelHeight)
        {
            int blockWidth = columns * cellPixelWidth;
            int blockHeight = rows * cellPixelHeight;
            int blockBytes = blockWidth * blockHeight * 4;
            if (_blockScratch == null || _blockScratch.Length < blockBytes)
                _blockScratch = new byte[BlockColumns * cellPixelWidth * BlockRows * cellPixelHeight * 4];
            Span<byte> rgba = _blockScratch.AsSpan(0, blockBytes);
            rgba.Clear();

            int blockRowBytes = blockWidth * 4;
            int cellRowBytes = cellPixelWidth * 4;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                CellImage image = screen.CellAt(blockX + column, blockY + row).Background.Image;
                if (image == null)
                    continue;

                byte[] bgra = image.Bgra;
                for (int y = 0; y < cellPixelHeight; y++)
                {
                    int source = y * cellRowBytes;
                    int target = (row * cellPixelHeight + y) * blockRowBytes + column * cellRowBytes;
                    for (int x = 0; x < cellPixelWidth; x++, source += 4, target += 4)
                    {
                        rgba[target] = bgra[source + 2];
                        rgba[target + 1] = bgra[source + 1];
                        rgba[target + 2] = bgra[source];
                        rgba[target + 3] = bgra[source + 3];
                    }
                }
            }

            // Optimal is zlib level 6: on photographs a quarter fewer bytes than Fastest (level 1), for
            // a few tens of milliseconds more per full-screen picture. A block is sent once and the
            // bytes go down a terminal link; PNG would halve them again, at the price of an encoder the
            // output does not have (see kittybitmap.md).
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            {
                zlib.Write(rgba);
            }

            KittyGraphics.WriteTransmitSequence(_output, imageId, blockWidth, blockHeight, compressed.ToArray(),
                KittyImageFormat.Rgba, true);
        }

        /// <summary>
        ///     Places a wash over every maximal rectangle of image cells under one visible paint, and
        ///     deletes the washes no longer there.
        /// </summary>
        private void EmitWashes(PixelBuffer screen)
        {
            Dictionary<(WashRect Rect, Color Paint), int> liveWash = _washPlacements;
            Dictionary<(WashRect Rect, Color Paint), int> nextWash = _washPlacementsScratch;
            nextWash.Clear();
            DropWashImagesSmallerThan(screen.Width, screen.Height);

            int bufferWidth = screen.Width;
            bool[] visited = CellRectangles.Scratch(ref _rectangleVisited, bufferWidth * screen.Height);

            for (ushort y = 0; y < screen.Height; y++)
            for (ushort x = 0; x < bufferWidth; x++)
            {
                if (visited[y * bufferWidth + x])
                    continue;

                ref readonly PixelBackground background = ref screen.CellAt(x, y).Background;
                if (background.Image == null || background.Color.A == 0)
                    continue;
                Color paint = background.Color;

                (int rectWidth, int rectHeight) = CellRectangles.GrowRectangle(
                    new PaintedImageCells(screen, paint), visited, bufferWidth, screen.Height, x, y, false);
                var rect = new WashRect(x, y, (ushort)rectWidth, (ushort)rectHeight);

                if (liveWash.Remove((rect, paint), out int washPlacementId))
                {
                    nextWash[(rect, paint)] = washPlacementId;
                    continue;
                }

                washPlacementId = KittyGraphics.AllocatePlacementId();
                nextWash[(rect, paint)] = washPlacementId;
                _output.SetCaretPosition(new PixelBufferCoordinate(x, y));
                _output.WriteText(KittyGraphics.BuildWashPlacementSequence(
                    GetOrTransmitWashImage(paint, screen.Width, screen.Height),
                    washPlacementId, rect.Width, rect.Height));
            }

            foreach (KeyValuePair<(WashRect Rect, Color Paint), int> stale in liveWash)
                _output.WriteText(KittyGraphics.BuildDeletePlacementSequence(
                    _washImages[stale.Key.Paint].ImageId, stale.Value));
            liveWash.Clear();

            (_washPlacements, _washPlacementsScratch) = (nextWash, liveWash);
        }

        /// <summary>
        ///     Deletes from the terminal every block image evicted from the cache that no placement shows
        ///     anymore. One still shown waits: deleting it would take its placements with it and leave a
        ///     hole in a picture that is not being redrawn.
        /// </summary>
        private void DeleteOrphanedBlockImages()
        {
            if (_evictedBlockImages.Count == 0)
                return;

            _placedBlockImages.Clear();
            foreach (BlockPlacement placement in _placements.Keys)
                _placedBlockImages.Add(placement.ImageId);

            SelectOrphans(_evictedBlockImages, _placedBlockImages, _orphanedBlockImages);
            foreach (int imageId in _orphanedBlockImages)
                _output.WriteText(KittyGraphics.BuildDeleteSequence(imageId));
        }

        /// <summary>
        ///     Moves every image in <paramref name="evicted" /> that is not in <paramref name="placed" /> to
        ///     <paramref name="orphans" /> (cleared first): images nothing tracks and nothing shows.
        /// </summary>
        internal static void SelectOrphans(HashSet<int> evicted, HashSet<int> placed, List<int> orphans)
        {
            orphans.Clear();
            foreach (int imageId in evicted)
                if (!placed.Contains(imageId))
                    orphans.Add(imageId);
            foreach (int imageId in orphans)
                evicted.Remove(imageId);
        }

        /// <summary>
        ///     Returns the wash image for <paramref name="paint" />, sending one sized to the screen
        ///     (<paramref name="columns" /> x <paramref name="rows" /> pixels, one per cell) the first time
        ///     the color is needed, so it covers any rectangle the screen can hold.
        /// </summary>
        private int GetOrTransmitWashImage(Color paint, ushort columns, ushort rows)
        {
            if (_washImages.TryGetValue(paint, out (int ImageId, ushort, ushort) image))
                return image.ImageId;

            int imageId = KittyGraphics.AllocateImageId();
            _output.WriteText(KittyGraphics.BuildTransmitWashSequence(imageId, paint, columns, rows));
            _washImages[paint] = (imageId, columns, rows);
            return imageId;
        }

        /// <summary>
        ///     Deletes the wash images too small for a screen of <paramref name="columns" /> x
        ///     <paramref name="rows" /> (it grew since they were sent), along with their placements, which
        ///     this frame then places again on a freshly sent image.
        /// </summary>
        private void DropWashImagesSmallerThan(ushort columns, ushort rows)
        {
            if (_washImages.Count == 0)
                return;

            _washesToDelete.Clear();
            foreach ((Color paint, (int _, ushort imageColumns, ushort imageRows)) in _washImages)
                if (imageColumns < columns || imageRows < rows)
                    _washesToDelete.Add(paint);
            if (_washesToDelete.Count == 0)
                return;

            foreach (Color paint in _washesToDelete)
            {
                // uppercase d=I takes the image's placements with it
                _output.WriteText(KittyGraphics.BuildDeleteSequence(_washImages[paint].ImageId));
                _washImages.Remove(paint);
            }

            // in place: the caller holds this dictionary as the frame's live wash placements
            _washPlacementsToRemove.Clear();
            foreach ((WashRect Rect, Color Paint) key in _washPlacements.Keys)
                if (!_washImages.ContainsKey(key.Paint))
                    _washPlacementsToRemove.Add(key);
            foreach ((WashRect Rect, Color Paint) key in _washPlacementsToRemove)
                _washPlacements.Remove(key);
        }

        /// <summary>
        ///     Deletes the wash images no placement shows anymore. An animated overlay goes through a
        ///     new color every frame, and each one is only a few hundred compressed bytes to send again.
        /// </summary>
        private void FreeUnusedWashImages()
        {
            if (_washImages.Count == 0)
                return;

            _washesInUse.Clear();
            foreach ((WashRect _, Color paint) in _washPlacements.Keys)
                _washesInUse.Add(paint);

            _washesToDelete.Clear();
            foreach (Color paint in _washImages.Keys)
                if (!_washesInUse.Contains(paint))
                    _washesToDelete.Add(paint);

            foreach (Color paint in _washesToDelete)
            {
                _output.WriteText(KittyGraphics.BuildDeleteSequence(_washImages[paint].ImageId));
                _washImages.Remove(paint);
            }
        }

        /// <summary>Image cells within one block.</summary>
        private readonly struct ImageCellsInBlock(PixelBuffer screen, int blockX, int blockY, int columns, int rows)
            : IRectangleCells
        {
            public bool Joins(int x, int y, int dx, int dy)
            {
                int cellX = x + dx;
                int cellY = y + dy;
                return cellX < blockX + columns && cellY < blockY + rows &&
                       screen.CellAt(cellX, cellY).Background.Image != null;
            }
        }

        /// <summary>Image cells under one paint.</summary>
        private readonly struct PaintedImageCells(PixelBuffer screen, Color paint) : IRectangleCells
        {
            public bool Joins(int x, int y, int dx, int dy)
            {
                ref readonly PixelBackground background = ref screen.CellAt(x + dx, y + dy).Background;
                return background.Image != null && background.Color == paint;
            }
        }
    }
}
