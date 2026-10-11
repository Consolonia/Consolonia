using System.Collections.Generic;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     The frame side of kitty graphics. A tile cell in the pixel buffer shows a slice of an image the
    ///     terminal holds, but the terminal shows nothing until the image is <em>placed</em>: a placement
    ///     is an object below the text, not the content of a cell. So at the end of every frame the tile
    ///     cells on screen are coalesced into placements and reconciled with the ones already live.
    /// </summary>
    internal sealed partial class KittyBitmapRenderer : IRenderTargetHook
    {
        /// <summary>
        ///     Tile images evicted from <see cref="TileImages" /> that a placement still showed when they
        ///     were taken; each is deleted from the terminal once no placement does.
        /// </summary>
        private readonly HashSet<int> _evictedTileImages = new();

        private readonly List<int> _orphanedTileImages = new();

        // scratch for the bookkeeping, kept between frames so it is not reallocated
        private readonly HashSet<int> _placedTileImages = new();

        /// <summary>
        ///     Placements to delete at the end of the next frame, once it has placed theirs anew: deleted
        ///     first, the screen would show nothing where the picture was until the frame got that far.
        /// </summary>
        private readonly List<(int ImageId, int PlacementId)> _placementsToDeleteAfterFrame = new();

        // wash images live in the terminal, by color, one pixel per cell of the screen they were sent
        // for; freed once no wash placement uses them
        private readonly Dictionary<Color, (int ImageId, ushort Columns, ushort Rows)> _washImages = new();
        private readonly List<(KittyRect Rect, Color Wash)> _washPlacementsToRemove = new();
        private readonly HashSet<Color> _washesInUse = new();

        private readonly List<Color> _washesToDelete = new();

        // classic rect placements coalesced from KittyTile cell backgrounds at z=-2, diffed across frames
        // so only changed rectangles cross the wire; scratch collects the next frame, then the two swap
        private Dictionary<KittyRect, int> _rectPlacements = new();
        private Dictionary<KittyRect, int> _rectPlacementsScratch = new();
        private bool[] _rectangleVisited;

        // wash placements tinting rect placements (a translucent overlay over a picture), keyed by the
        // rect they cover and their color, diffed across frames the same way
        private Dictionary<(KittyRect Rect, Color Wash), int> _washPlacements = new();
        private Dictionary<(KittyRect Rect, Color Wash), int> _washPlacementsScratch = new();

        public void BeginFrame()
        {
        }

        public void EndFrame(PixelBuffer buffer)
        {
            EmitPlacements(buffer);
            DeleteWhatTheFrameReplaced();
        }

        /// <summary>
        ///     The placements on screen can no longer be counted on (the screen was resized, and terminals
        ///     differ in what becomes of placements then; or another program had the terminal), so the
        ///     frame places every one again from the cells and deletes the old ones at its end. The images
        ///     stay in the terminal, so a picture is placed from the tiles already there rather than
        ///     transmitted and decoded anew, and where the terminal still shows it nothing changes on screen.
        /// </summary>
        public void ResetCache()
        {
            foreach ((KittyRect rect, int placementId) in _rectPlacements)
                _placementsToDeleteAfterFrame.Add((rect.ImageId, placementId));
            _rectPlacements.Clear();

            foreach (((KittyRect _, Color wash), int placementId) in _washPlacements)
                _placementsToDeleteAfterFrame.Add((_washImages[wash].ImageId, placementId));
            _washPlacements.Clear();
        }

        /// <summary>
        ///     Deletes the placements <see cref="ResetCache" /> left for the end of the frame, now that it
        ///     has placed their replacements: where the terminal still showed the old picture, the new one
        ///     is there before the old one goes.
        /// </summary>
        private void DeleteWhatTheFrameReplaced()
        {
            foreach ((int imageId, int placementId) in _placementsToDeleteAfterFrame)
                ConsoleOutput.WriteText(KittyGraphics.BuildDeletePlacementSequence(imageId, placementId));
            _placementsToDeleteAfterFrame.Clear();
        }

        /// <summary>
        ///     Coalesces the KittyTile cell backgrounds present in the buffer into maximal
        ///     rectangles and reconciles them with the classic placements currently live in the
        ///     terminal: unchanged rectangles cost nothing, new ones are placed (at the rectangle's
        ///     cell position, cropped to its slice of the pre-scaled image, z=-2 so text drawn on
        ///     the covered cells composites over the picture), vanished ones are deleted by
        ///     placement id with the image data retained for cheap re-placement.
        ///     A rectangle's cells share one wash (see <see cref="PixelBackground" />); a visible wash
        ///     gets its own placement of a 1x1 translucent image stretched over the rectangle at z=-1,
        ///     which is how a modal backdrop dims a picture.
        /// </summary>
        private void EmitPlacements(PixelBuffer pixelBuffer)
        {
            Dictionary<KittyRect, int> live = _rectPlacements;
            Dictionary<KittyRect, int> next = _rectPlacementsScratch;
            next.Clear();
            Dictionary<(KittyRect Rect, Color Wash), int> liveWash = _washPlacements;
            Dictionary<(KittyRect Rect, Color Wash), int> nextWash = _washPlacementsScratch;
            nextWash.Clear();
            DropWashImagesSmallerThan(pixelBuffer.Width, pixelBuffer.Height);

            int bufferWidth = pixelBuffer.Width;
            bool[] visited = CellRectangles.Scratch(ref _rectangleVisited, bufferWidth * pixelBuffer.Height);

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            for (ushort x = 0; x < bufferWidth; x++)
            {
                if (visited[y * bufferWidth + x])
                    continue;

                ref readonly PixelBackground background = ref pixelBuffer.CellAt(x, y).Background;
                KittyTile tile = background.Tile;
                if (tile.IsEmpty)
                    continue;
                Color wash = background.Color;

                // rightward while the tiles continue the same image's row under the same wash, then
                // downward while each row continues the same tile grid at full width
                (int rectWidth, int rectHeight) = CellRectangles.GrowRectangle(
                    new KittyTileCells(pixelBuffer, tile, wash), visited, bufferWidth, pixelBuffer.Height, x, y,
                    false);
                ushort width = (ushort)rectWidth;
                ushort height = (ushort)rectHeight;

                var rect = new KittyRect(tile.ImageId, tile.X, tile.Y, width, height, x, y);

                if (live.Remove(rect, out int placementId))
                {
                    next[rect] = placementId;
                }
                else
                {
                    placementId = KittyGraphics.AllocatePlacementId();
                    next[rect] = placementId;

                    int cellPixelWidth = ConsoleOutput.CellPixelWidth;
                    int cellPixelHeight = ConsoleOutput.CellPixelHeight;
                    ConsoleOutput.SetCaretPosition(new PixelBufferCoordinate(rect.ScreenX, rect.ScreenY));
                    ConsoleOutput.WriteText(KittyGraphics.BuildRectPlacementSequence(
                        rect.ImageId, placementId,
                        rect.TileX * cellPixelWidth, rect.TileY * cellPixelHeight,
                        rect.Width * cellPixelWidth, rect.Height * cellPixelHeight));
                }

                if (wash.A == 0)
                    continue;

                if (liveWash.Remove((rect, wash), out int washPlacementId))
                {
                    nextWash[(rect, wash)] = washPlacementId;
                    continue;
                }

                washPlacementId = KittyGraphics.AllocatePlacementId();
                nextWash[(rect, wash)] = washPlacementId;
                ConsoleOutput.SetCaretPosition(new PixelBufferCoordinate(rect.ScreenX, rect.ScreenY));
                ConsoleOutput.WriteText(KittyGraphics.BuildWashPlacementSequence(
                    GetOrTransmitWashImage(wash, pixelBuffer.Width, pixelBuffer.Height),
                    washPlacementId, rect.Width, rect.Height));
            }

            // whatever is left in the live set has no tiles backing it anymore
            foreach (KeyValuePair<KittyRect, int> stale in live)
                ConsoleOutput.WriteText(
                    KittyGraphics.BuildDeletePlacementSequence(stale.Key.ImageId, stale.Value));
            live.Clear();

            foreach (KeyValuePair<(KittyRect Rect, Color Wash), int> stale in liveWash)
                ConsoleOutput.WriteText(KittyGraphics.BuildDeletePlacementSequence(
                    _washImages[stale.Key.Wash].ImageId, stale.Value));
            liveWash.Clear();

            (_rectPlacements, _rectPlacementsScratch) = (next, live);
            (_washPlacements, _washPlacementsScratch) = (nextWash, liveWash);

            DeleteOrphanedTileImages();
            FreeUnusedWashImages();
        }

        /// <summary>
        ///     Deletes from the terminal every tile image evicted from the tile cache that no placement
        ///     shows anymore. One still shown waits: deleting it would take its placements with it and
        ///     leave a hole in a picture that is not being redrawn.
        /// </summary>
        private void DeleteOrphanedTileImages()
        {
            while (EvictedTileImages.TryDequeue(out int imageId))
                _evictedTileImages.Add(imageId);
            if (_evictedTileImages.Count == 0)
                return;

            _placedTileImages.Clear();
            foreach (KittyRect rect in _rectPlacements.Keys)
                _placedTileImages.Add(rect.ImageId);

            SelectOrphans(_evictedTileImages, _placedTileImages, _orphanedTileImages);
            foreach (int imageId in _orphanedTileImages)
                ConsoleOutput.WriteText(KittyGraphics.BuildDeleteSequence(imageId));
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
        ///     Returns the wash image for <paramref name="wash" />, sending one sized to the screen
        ///     (<paramref name="columns" /> x <paramref name="rows" /> pixels, one per cell) the first time
        ///     the color is needed, so it covers any rectangle the screen can hold.
        /// </summary>
        private int GetOrTransmitWashImage(Color wash, ushort columns, ushort rows)
        {
            if (_washImages.TryGetValue(wash, out (int ImageId, ushort, ushort) image))
                return image.ImageId;

            int imageId = KittyGraphics.AllocateImageId();
            ConsoleOutput.WriteText(KittyGraphics.BuildTransmitWashSequence(imageId, wash, columns, rows));
            _washImages[wash] = (imageId, columns, rows);
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
            foreach ((Color wash, (int _, ushort imageColumns, ushort imageRows)) in _washImages)
                if (imageColumns < columns || imageRows < rows)
                    _washesToDelete.Add(wash);
            if (_washesToDelete.Count == 0)
                return;

            foreach (Color wash in _washesToDelete)
            {
                // uppercase d=I takes the image's placements with it
                ConsoleOutput.WriteText(KittyGraphics.BuildDeleteSequence(_washImages[wash].ImageId));
                _washImages.Remove(wash);
            }

            // in place: the caller holds this dictionary as the frame's live wash placements
            _washPlacementsToRemove.Clear();
            foreach ((KittyRect Rect, Color Wash) key in _washPlacements.Keys)
                if (!_washImages.ContainsKey(key.Wash))
                    _washPlacementsToRemove.Add(key);
            foreach ((KittyRect Rect, Color Wash) key in _washPlacementsToRemove)
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
            foreach ((KittyRect _, Color wash) in _washPlacements.Keys)
                _washesInUse.Add(wash);

            _washesToDelete.Clear();
            foreach (Color wash in _washImages.Keys)
                if (!_washesInUse.Contains(wash))
                    _washesToDelete.Add(wash);

            foreach (Color wash in _washesToDelete)
            {
                ConsoleOutput.WriteText(KittyGraphics.BuildDeleteSequence(_washImages[wash].ImageId));
                _washImages.Remove(wash);
            }
        }

        private readonly record struct KittyRect(
            int ImageId,
            ushort TileX,
            ushort TileY,
            ushort Width,
            ushort Height,
            ushort ScreenX,
            ushort ScreenY);

        /// <summary>Kitty tile cells continuing one image's tile grid under one wash.</summary>
        private readonly struct KittyTileCells(PixelBuffer pixelBuffer, KittyTile tile, Color wash) : IRectangleCells
        {
            public bool Joins(int x, int y, int dx, int dy)
            {
                ref readonly PixelBackground background = ref pixelBuffer.CellAt(x + dx, y + dy).Background;
                return background.Tile.ImageId == tile.ImageId &&
                       background.Tile.X == tile.X + dx &&
                       background.Tile.Y == tile.Y + dy &&
                       background.Color == wash;
            }
        }
    }
}