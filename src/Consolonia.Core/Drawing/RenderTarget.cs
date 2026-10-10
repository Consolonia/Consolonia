#nullable enable
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal class RenderTarget : IRenderTarget
    {
        private readonly IConsoleOutput _console;

        private readonly ConsoleWindowImpl _consoleTopLevelImpl;

        // cache of pixels written so we can ignore them if unchanged.
        private Pixel?[,] _cache = null!; //todo: why Pixel can be null

        /// <summary>Per frame, which cells are dirty: the dirty regions marked once, not searched per cell.</summary>
        private bool[]? _dirtyCells;

        /// <summary>The rectangle passes' per-cell scratch, kept between frames so it is not reallocated.</summary>
        private bool[]? _rectangleVisited;

        /// <summary>Pixels of the sixel combined from a rectangle of cells, kept between frames.</summary>
        private byte[]? _combinedPixels;

        private ConsoleCursor _consoleCursor;

        // classic rect placements coalesced from KittyTile cell backgrounds at z=-2, diffed across frames
        // so only changed rectangles cross the wire; scratch collects the next frame, then the two swap
        private Dictionary<KittyRect, int> _kittyRectPlacements = new();
        private Dictionary<KittyRect, int> _kittyRectPlacementsScratch = new();

        // wash placements tinting rect placements (a translucent overlay over a picture), keyed by the
        // rect they cover and their color, diffed across frames the same way
        private Dictionary<(KittyRect Rect, Color Wash), int> _kittyWashPlacements = new();
        private Dictionary<(KittyRect Rect, Color Wash), int> _kittyWashPlacementsScratch = new();

        // wash images live in the terminal, by color, one pixel per cell of the screen they were sent
        // for; freed once no wash placement uses them
        private readonly Dictionary<Color, (int ImageId, ushort Columns, ushort Rows)> _kittyWashImages = new();

        /// <summary>
        ///     Tile images evicted from the kitty renderer's cache that a placement still showed when they
        ///     were taken; each is deleted from the terminal once no placement does.
        /// </summary>
        private readonly HashSet<int> _evictedTileImages = new();

        // scratch for the kitty bookkeeping, kept between frames so it is not reallocated
        private readonly HashSet<int> _placedTileImages = new();
        private readonly List<int> _orphanedTileImages = new();
        private readonly HashSet<Color> _washesInUse = new();
        private readonly List<Color> _washesToDelete = new();
        private readonly List<(KittyRect Rect, Color Wash)> _washPlacementsToRemove = new();

        /// <summary>
        ///     Placements to delete at the end of the next frame, once it has placed theirs anew: deleted
        ///     first, the screen would show nothing where the picture was until the frame got that far.
        /// </summary>
        private readonly List<(int ImageId, int PlacementId)> _placementsToDeleteAfterFrame = new();

        private readonly record struct KittyRect(
            int ImageId,
            ushort TileX,
            ushort TileY,
            ushort Width,
            ushort Height,
            ushort ScreenX,
            ushort ScreenY);

        private readonly Snapshot.Regions _cursorDirtyRegions = new();
        private Timer? _cursorTimer;

        /// <summary>
        ///     DrawingContextImpl contains number of fields which are initialized every time. We just keep a single instance
        ///     hoping it can be re-used with each drawing
        /// </summary>
        private DrawingContextImpl? _drawingContextImpl;

#if FPS
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _framesThisSecond;
        private int _fps;
        private TimeSpan _lastFpsUpdate;
#endif
        private RenderTarget(ConsoleWindowImpl consoleTopLevelImpl)
        {
            _console = AvaloniaLocator.Current.GetRequiredService<IConsoleOutput>();
            _consoleTopLevelImpl = consoleTopLevelImpl;
            InitializeCacheInternal();
            _cursorTimer = new Timer(_ =>
                {
                    lock (this)
                    {
                        if (_cursorTimer == null)
                            return;
                        _cursorTimer.Stop();
                        RenderToDevice(_cursorDirtyRegions);
                    }
                }, null, Timeout.Infinite,
                Timeout.Infinite);
            _consoleTopLevelImpl.CursorChanged += OnCursorChanged;
            _consoleTopLevelImpl.TerminalContentsLost += ForgetTerminalContents;
        }

        private void InitializeCacheInternal()
        {
            _cache = InitializeCache(_consoleTopLevelImpl.PixelBuffer.Width, _consoleTopLevelImpl.PixelBuffer.Height);
        }

        public RenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
            : this(surfaces.OfType<ConsoleWindowImpl>()
                .Single())
        {
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Dispose()
        {
            _consoleTopLevelImpl.CursorChanged -= OnCursorChanged;
            _consoleTopLevelImpl.TerminalContentsLost -= ForgetTerminalContents;
            _cursorTimer!.Dispose();
            _cursorTimer = null;
        }

        public void Save(string fileName, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public void Save(Stream stream, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public const double AvaloniaHardcodedDpi = 96;
        public Vector Dpi { get; } = new(AvaloniaHardcodedDpi, AvaloniaHardcodedDpi);
        public PixelSize PixelSize { get; } = new(1, 1);

        internal void RenderToDevice()
        {
            try
            {
                RenderToDevice(_consoleTopLevelImpl.DirtyRegions);
            }
            catch (InvalidDrawingContextException)
            {
            }
        }

        public RenderTargetProperties Properties => new()
        {
            RetainsPreviousFrameContents = true, // both to true means no need to create a layer
            IsSuitableForDirectRendering = true
        };

        public PlatformRenderTargetState PlatformRenderTargetState => new()
        {
            IsReady = true,
            IsCorrupted = false
        };

        public IDrawingContextImpl CreateDrawingContext(IRenderTarget.RenderTargetSceneInfo sceneInfo,
            out RenderTargetDrawingContextProperties properties)
        {
            properties = new RenderTargetDrawingContextProperties
            {
                PreviousFrameIsRetained = true // otherwise full redrawing happens
            };

            if (_drawingContextImpl is null || _drawingContextImpl.PixelBuffer != _consoleTopLevelImpl.PixelBuffer)
                _drawingContextImpl = new DrawingContextImpl(_consoleTopLevelImpl, this);

            return _drawingContextImpl;
        }

        private static Pixel?[,] InitializeCache(ushort width, ushort height)
        {
            var cache = new Pixel?[width, height];

            // initialize the cache with Pixel.Empty as it literally means nothing
            for (ushort y = 0; y < height; y++)
            for (ushort x = 0; x < width; x++)
                cache[x, y] = Pixel.Empty;

            return cache;
        }

        /// <summary>
        ///     The terminal may no longer show what was written to it: another program drew while console
        ///     I/O was paused. Everything written is forgotten, so the next frame writes every cell again
        ///     and places every kitty picture again. The images stay: the terminal still holds them (the
        ///     alternate screen was never left, and nothing but this app deletes by id), so a picture is
        ///     placed from the tiles already there rather than transmitted and decoded anew, and where the
        ///     terminal still shows it nothing changes on screen.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void ForgetTerminalContents()
        {
            InitializeCacheInternal();
            ForgetPlacements();

            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            _consoleTopLevelImpl.DirtyRegions.AddRect(new PixelRect(0, 0, pixelBuffer.Width, pixelBuffer.Height));
        }

        /// <summary>
        ///     The placements on screen can no longer be counted on (the screen was resized, and terminals
        ///     differ in what becomes of placements then; or another program had the terminal), so the
        ///     frame places every one again from the cells and deletes the old ones at its end. The images
        ///     stay in the terminal.
        /// </summary>
        private void ForgetPlacements()
        {
            foreach ((KittyRect rect, int placementId) in _kittyRectPlacements)
                _placementsToDeleteAfterFrame.Add((rect.ImageId, placementId));
            _kittyRectPlacements.Clear();

            foreach (((KittyRect _, Color wash), int placementId) in _kittyWashPlacements)
                _placementsToDeleteAfterFrame.Add((_kittyWashImages[wash].ImageId, placementId));
            _kittyWashPlacements.Clear();
        }

        /// <summary>
        ///     Deletes the placements <see cref="ForgetPlacements" /> left for the end of the frame, now that
        ///     it has placed their replacements: where the terminal still showed the old picture, the new one
        ///     is there before the old one goes.
        /// </summary>
        private void DeleteWhatTheFrameReplaced()
        {
            foreach ((int imageId, int placementId) in _placementsToDeleteAfterFrame)
                _console.WriteText(KittyGraphics.BuildDeletePlacementSequence(imageId, placementId));
            _placementsToDeleteAfterFrame.Clear();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void RenderToDevice(Snapshot.Regions regions)
        {
            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            Snapshot dirtyRegions = regions.GetSnapshotAndClear();
            dirtyRegions.Intersect(0, 0, pixelBuffer.Width, pixelBuffer.Height);
            // a frame with nothing to draw still deletes tile images evicted since the last one
            if (dirtyRegions.IsEmpty && !KittyBitmapRenderer.HasEvictedImages) return;

            // a new cache means everything is redrawn, kitty placements included
            bool kittyCellsChanged = false;
            if (pixelBuffer.Width != _cache.GetLength(0) || pixelBuffer.Height != _cache.GetLength(1))
            {
                InitializeCacheInternal();
                ForgetPlacements();
                kittyCellsChanged = true;
            }

#if FPS
            var now = _stopwatch.Elapsed;
            var elapsed = now - _lastFpsUpdate;

            ++_framesThisSecond;

            if (elapsed.TotalSeconds > 1)
            {
                _fps = (int)(_framesThisSecond / elapsed.TotalSeconds);
                _framesThisSecond = 0;
                _lastFpsUpdate = now;
            }
#endif
            _console.HideCaret();

            PixelBufferCoordinate? caretPosition = null;
            CaretStyle? caretStyle = null;

            bool[] dirty = Scratch(ref _dirtyCells, pixelBuffer.Width * pixelBuffer.Height);
            dirtyRegions.MarkCells(dirty, pixelBuffer.Width);

            // Pass 1: sixel regions
            RenderSixelRegions(pixelBuffer, dirty);

            // Pass 2: Render non-sixel dirty pixels.
            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
                // we can not run only from MinX to MaxX because of wide characters, also we can not run MinY/MaxY because we need to detect caret
                for (ushort x = 0; x < pixelBuffer.Width; x++)
                {
                    Pixel pixel = pixelBuffer[x, y];

                    if (pixel.IsCaret())
                    {
                        if (caretPosition != null)
                            throw new InvalidOperationException("Caret is already shown");
                        caretPosition = new PixelBufferCoordinate(x, y);
                        caretStyle = pixel.CaretStyle;
                    }

                    if (!dirty[y * pixelBuffer.Width + x])
                        continue;

                    // Placements change only where a cell shows a kitty tile or showed one, and every
                    // change to the pixel buffer marks its cells dirty, so clean cells never need a look.
                    if (!pixel.Background.Tile.IsEmpty || _cache[x, y]?.Background.Tile.IsEmpty == false)
                        kittyCellsChanged = true;

                    Sixel? cellSixel = pixel.Foreground.Symbol.Sixel;
                    if (cellSixel != null)
                    {
                        // pass 1 has written this cell, unless the mouse cursor is over it: the cursor
                        // is text, so the cell becomes a text cell on the picture's dominant color
                        if (!IsUnderMouseCursor(x, y))
                            continue;
                        pixel = new Pixel(new PixelForeground(Symbol.Space, Colors.Transparent),
                            new PixelBackground(cellSixel.DominantColor));
                    }

                    // painting mouse cursor if within the range of current pixel (possibly wide)
                    if (IsUnderMouseCursor(x, y))
                    {
                        // a tile cell's color is the wash for its image (laid over it by a wash
                        // placement); the cell itself is written with the terminal's default background
                        // (see AnsiConsoleOutput.WritePixel), so the composite is only for the cursor
                        if (!pixel.Background.Tile.IsEmpty)
                            pixel = new Pixel(pixel.Foreground,
                                new PixelBackground(CompositeOverBlack(pixel.Background.Color),
                                    pixel.Background.Tile),
                                pixel.CaretStyle);

                        if (_consoleCursor.Type == " " && pixel.Width == 1)
                        {
                            // floating cursor tracking effect
                            // if we are drawing a " " and the pixel underneath is not wide char
                            // then we lift the character from the underlying pixel and invert it
                            char cursorChar = pixel.Foreground.Symbol.Character != '\0'
                                ? pixel.Foreground.Symbol.Character
                                : ' ';
                            pixel = new Pixel(new PixelForeground(new Symbol(cursorChar, 1), pixel.Background.Color),
                                new PixelBackground(GetContrastColor(pixel.Background.Color)));
                        }
                        else
                        {
                            char cursorChar = _consoleCursor.Type[x - _consoleCursor.Coordinate.X];
                            // simply draw the mouse cursor character in the current pixel colors.
                            Color foreground = pixel.Foreground.Color != Colors.Transparent
                                ? pixel.Foreground.Color
                                : GetContrastColor(pixel.Background.Color);
                            pixel = new Pixel(
                                new PixelForeground(new Symbol(cursorChar, 1), foreground,
                                    pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                                pixel.Background, pixel.CaretStyle);
                        }
                    }

                    if (pixel.Width > 1)

                        // checking that there are enough empty pixels after current wide character and if no, we want to render just empty space instead
                        for (ushort i = 1; i < pixel.Width && x + i < pixelBuffer.Width; i++)
                            if (pixelBuffer[(ushort)(x + i), y].Width != 0)
                            {
                                pixel = new Pixel(
                                    new PixelForeground(Symbol.Space, pixel.Foreground.Color, pixel.Foreground.Weight,
                                        pixel.Foreground.Style, pixel.Foreground.TextDecoration), pixel.Background,
                                    pixel.CaretStyle);
                                break;
                            }

                    {
                        // tracking if we are on wide character sequence currently
                        if (pixel.Width > 1)
                            isWide = true;
                        else if (pixel.Width == 1)
                            isWide = false;
                    }

                    if (pixel.Width == 0 && !isWide)
                        // fallback to spaces instead of empty chars in case wide character at the beginning was overwritten or we detected there is no room for it previously
                        pixel = new Pixel(
                            new PixelForeground(Symbol.Space, pixel.Foreground.Color, pixel.Foreground.Weight,
                                pixel.Foreground.Style, pixel.Foreground.TextDecoration), pixel.Background,
                            pixel.CaretStyle);

                    {
                        // checking cache
                        //todo: it does not consider that some of them will be replaced by space. But issue is pessimistic, just unnecessary redraws
                        bool anyDifferent = false;
                        for (ushort i = 0; i < ushort.Max(pixel.Width, 1); i++)
                            if ((i == 0 ? pixel : pixelBuffer[(ushort)(x + i), y]) != _cache[x + i, y])
                            {
                                anyDifferent = true;
                                break;
                            }

                        if (!anyDifferent)
                            continue;
                    }

                    _console.WritePixel(new PixelBufferCoordinate(x, y), in pixel);

                    _cache[x, y] = pixel;
                }
            }

            // New evictions are checked right away. One still shown is checked again when kitty cells
            // change, which is the only way the placement showing it can go.
            if (kittyCellsChanged || KittyBitmapRenderer.HasEvictedImages)
                EmitKittyRectPlacements(pixelBuffer);
            DeleteWhatTheFrameReplaced();

#if FPS
            var fps = $"FPS: {_fps: 000}";
            for (ushort i = 0; i < fps.Length; i++)
            {
                var pixel =
 new Pixel(new PixelForeground(new Symbol(fps[i]), Colors.White), new PixelBackground(Colors.Black));
                _console.WritePixel(new PixelBufferCoordinate((ushort)(pixelBuffer.Width - fps.Length + i), (ushort)(pixelBuffer.Height - 1)), in pixel);
            }
#endif

            // the caret was hidden at the start of the frame
            if (caretPosition != null && caretStyle != CaretStyle.None)
            {
                _console.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                _console.SetCaretStyle((CaretStyle)caretStyle!);
                _console.ShowCaret();
            }

            // one write for the whole frame, caret included, so a synchronized update covers all of it
            _console.Flush();
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
        private void EmitKittyRectPlacements(PixelBuffer pixelBuffer)
        {
            Dictionary<KittyRect, int> live = _kittyRectPlacements;
            Dictionary<KittyRect, int> next = _kittyRectPlacementsScratch;
            next.Clear();
            Dictionary<(KittyRect Rect, Color Wash), int> liveWash = _kittyWashPlacements;
            Dictionary<(KittyRect Rect, Color Wash), int> nextWash = _kittyWashPlacementsScratch;
            nextWash.Clear();
            DropWashImagesSmallerThan(pixelBuffer.Width, pixelBuffer.Height);

            int bufferWidth = pixelBuffer.Width;
            bool[] visited = Scratch(ref _rectangleVisited, bufferWidth * pixelBuffer.Height);

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
                (int rectWidth, int rectHeight) = GrowRectangle(new KittyTileCells(pixelBuffer, tile, wash),
                    visited, bufferWidth, pixelBuffer.Height, x, y, false);
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

                    int cellPixelWidth = _console.CellPixelWidth;
                    int cellPixelHeight = _console.CellPixelHeight;
                    _console.SetCaretPosition(new PixelBufferCoordinate(rect.ScreenX, rect.ScreenY));
                    _console.WriteText(KittyGraphics.BuildRectPlacementSequence(
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
                _console.SetCaretPosition(new PixelBufferCoordinate(rect.ScreenX, rect.ScreenY));
                _console.WriteText(KittyGraphics.BuildWashPlacementSequence(
                    GetOrTransmitWashImage(wash, pixelBuffer.Width, pixelBuffer.Height),
                    washPlacementId, rect.Width, rect.Height));
            }

            // whatever is left in the live set has no tiles backing it anymore
            foreach (KeyValuePair<KittyRect, int> stale in live)
                _console.WriteText(
                    KittyGraphics.BuildDeletePlacementSequence(stale.Key.ImageId, stale.Value));
            live.Clear();

            foreach (KeyValuePair<(KittyRect Rect, Color Wash), int> stale in liveWash)
                _console.WriteText(KittyGraphics.BuildDeletePlacementSequence(
                    _kittyWashImages[stale.Key.Wash].ImageId, stale.Value));
            liveWash.Clear();

            (_kittyRectPlacements, _kittyRectPlacementsScratch) = (next, live);
            (_kittyWashPlacements, _kittyWashPlacementsScratch) = (nextWash, liveWash);

            DeleteOrphanedTileImages();
            FreeUnusedWashImages();
        }

        /// <summary>
        ///     Deletes from the terminal every tile image evicted from the kitty renderer's cache that no
        ///     placement shows anymore. One still shown waits: deleting it would take its placements with it
        ///     and leave a hole in a picture that is not being redrawn.
        /// </summary>
        private void DeleteOrphanedTileImages()
        {
            while (KittyBitmapRenderer.TryTakeEvictedImage(out int imageId))
                _evictedTileImages.Add(imageId);
            if (_evictedTileImages.Count == 0)
                return;

            _placedTileImages.Clear();
            foreach (KittyRect rect in _kittyRectPlacements.Keys)
                _placedTileImages.Add(rect.ImageId);

            SelectOrphans(_evictedTileImages, _placedTileImages, _orphanedTileImages);
            foreach (int imageId in _orphanedTileImages)
                _console.WriteText(KittyGraphics.BuildDeleteSequence(imageId));
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
            if (_kittyWashImages.TryGetValue(wash, out (int ImageId, ushort, ushort) image))
                return image.ImageId;

            int imageId = KittyGraphics.AllocateImageId();
            _console.WriteText(KittyGraphics.BuildTransmitWashSequence(imageId, wash, columns, rows));
            _kittyWashImages[wash] = (imageId, columns, rows);
            return imageId;
        }

        /// <summary>
        ///     Deletes the wash images too small for a screen of <paramref name="columns" /> x
        ///     <paramref name="rows" /> (it grew since they were sent), along with their placements, which
        ///     this frame then places again on a freshly sent image.
        /// </summary>
        private void DropWashImagesSmallerThan(ushort columns, ushort rows)
        {
            if (_kittyWashImages.Count == 0)
                return;

            _washesToDelete.Clear();
            foreach ((Color wash, (int _, ushort imageColumns, ushort imageRows)) in _kittyWashImages)
                if (imageColumns < columns || imageRows < rows)
                    _washesToDelete.Add(wash);
            if (_washesToDelete.Count == 0)
                return;

            foreach (Color wash in _washesToDelete)
            {
                // uppercase d=I takes the image's placements with it
                _console.WriteText(KittyGraphics.BuildDeleteSequence(_kittyWashImages[wash].ImageId));
                _kittyWashImages.Remove(wash);
            }

            // in place: the caller holds this dictionary as the frame's live wash placements
            _washPlacementsToRemove.Clear();
            foreach ((KittyRect Rect, Color Wash) key in _kittyWashPlacements.Keys)
                if (!_kittyWashImages.ContainsKey(key.Wash))
                    _washPlacementsToRemove.Add(key);
            foreach ((KittyRect Rect, Color Wash) key in _washPlacementsToRemove)
                _kittyWashPlacements.Remove(key);
        }

        /// <summary>
        ///     Deletes the wash images no placement shows anymore. An animated overlay goes through a
        ///     new color every frame, and each one is only a few hundred compressed bytes to send again.
        /// </summary>
        private void FreeUnusedWashImages()
        {
            if (_kittyWashImages.Count == 0)
                return;

            _washesInUse.Clear();
            foreach ((KittyRect _, Color wash) in _kittyWashPlacements.Keys)
                _washesInUse.Add(wash);

            _washesToDelete.Clear();
            foreach (Color wash in _kittyWashImages.Keys)
                if (!_washesInUse.Contains(wash))
                    _washesToDelete.Add(wash);

            foreach (Color wash in _washesToDelete)
            {
                _console.WriteText(KittyGraphics.BuildDeleteSequence(_kittyWashImages[wash].ImageId));
                _kittyWashImages.Remove(wash);
            }
        }

        /// <summary>
        ///     Composites a (possibly translucent) wash over opaque black.
        /// </summary>
        private static Color CompositeOverBlack(Color wash)
        {
            return Color.FromRgb((byte)(wash.R * wash.A / 255), (byte)(wash.G * wash.A / 255),
                (byte)(wash.B * wash.A / 255));
        }

        private bool IsUnderMouseCursor(int x, int y)
        {
            return !_consoleCursor.IsEmpty() &&
                   _consoleCursor.Coordinate.Y == y &&
                   _consoleCursor.Coordinate.X <= x && x < _consoleCursor.Coordinate.X + _consoleCursor.Width;
        }

        /// <summary>
        ///     The sixel cell (x, y) needs written this frame, or null when it doesn't: it is not a sixel
        ///     cell, not dirty, already on screen, or under the mouse cursor (pass 2 draws that as text).
        /// </summary>
        private Sixel? SixelToWrite(PixelBuffer pixelBuffer, bool[] dirty, int x, int y)
        {
            Sixel? sixel = pixelBuffer.CellAt(x, y).Foreground.Symbol.Sixel;
            if (sixel == null ||
                !dirty[y * pixelBuffer.Width + x] ||
                ReferenceEquals(_cache[x, y]?.Foreground.Symbol.Sixel, sixel) ||
                IsUnderMouseCursor(x, y))
                return null;
            return sixel;
        }

        /// <summary>
        ///     Pass 1: finds contiguous sixel cells to write that share the same palette, combines them into
        ///     a single Sixel via BitBlt and writes that once. Pass 2 skips sixel cells.
        /// </summary>
        /// <remarks>
        ///     A dirty cell whose sixel the terminal already shows (the same instance as in the cache) is
        ///     not sent again: a mouse move or a neighbour repainting marks cells dirty without changing them.
        /// </remarks>
        private void RenderSixelRegions(PixelBuffer pixelBuffer, bool[] dirty)
        {
            // sixel cells only exist where the console draws sixels
            if (!_console.Capabilities.HasFlag(ConsoleCapabilities.SupportsSixel))
                return;

            int width = pixelBuffer.Width;
            bool[]? visited = null;

            for (int y = 0; y < pixelBuffer.Height; y++)
            for (int x = 0; x < width; x++)
            {
                if (visited != null && visited[y * width + x])
                    continue;

                Sixel? cellSixel = SixelToWrite(pixelBuffer, dirty, x, y);
                if (cellSixel == null)
                    continue;

                visited ??= Scratch(ref _rectangleVisited, width * pixelBuffer.Height);

                // the maximal rectangle of cells to write sharing this palette instance, narrowed rather
                // than extended ragged: the region must stay rectangular
                (int rectWidth, int rectHeight) = GrowRectangle(
                    new SixelCells(this, pixelBuffer, dirty, cellSixel.Palette), visited, width,
                    pixelBuffer.Height, x, y, true);

                if (rectWidth == 1 && rectHeight == 1)
                {
                    _console.WriteSixel(new PixelBufferCoordinate((ushort)x, (ushort)y), cellSixel);
                    _cache[x, y] = pixelBuffer[(ushort)x, (ushort)y];
                    continue;
                }

                // one BitBlt'd sixel for the whole rectangle costs a single escape sequence; it is written
                // once, so its bytes are not kept (IsTransient) and BitBlt fills every pixel
                int cellPixelWidth = cellSixel.CellWidth;
                int cellPixelHeight = cellSixel.CellHeight;
                int combinedWidth = rectWidth * cellPixelWidth;
                int combinedHeight = rectHeight * cellPixelHeight;
                int combinedLength = combinedWidth * combinedHeight;
                if (_combinedPixels == null || _combinedPixels.Length < combinedLength)
                    _combinedPixels = GC.AllocateUninitializedArray<byte>(Math.Max(combinedLength, 64 * 1024));
                var combined = new Sixel(cellSixel.Palette, cellSixel.PaletteCount, _combinedPixels,
                    combinedWidth, combinedHeight, cellPixelWidth, cellPixelHeight) { IsTransient = true };

                for (int ry = 0; ry < rectHeight; ry++)
                for (int rx = 0; rx < rectWidth; rx++)
                {
                    Sixel cell = pixelBuffer.CellAt(x + rx, y + ry).Foreground.Symbol.Sixel!;
                    // every cell fills its own part, so nothing of the scratch's earlier contents shows
                    Debug.Assert(cell.Width == cellPixelWidth && cell.Height == cellPixelHeight,
                        "cells joined into one sixel must be the same size");
                    combined.BitBlt(cell, rx * cellPixelWidth, ry * cellPixelHeight);
                }

                _console.WriteSixel(new PixelBufferCoordinate((ushort)x, (ushort)y), combined);

                for (int ry = 0; ry < rectHeight; ry++)
                for (int rx = 0; rx < rectWidth; rx++)
                    _cache[x + rx, y + ry] = pixelBuffer[(ushort)(x + rx), (ushort)(y + ry)];
            }
        }

        /// <summary>A cleared scratch array of at least <paramref name="length" />, reused across frames.</summary>
        private static bool[] Scratch(ref bool[]? scratch, int length)
        {
            if (scratch == null || scratch.Length < length)
                scratch = new bool[length];
            else
                Array.Clear(scratch, 0, length);
            return scratch;
        }

        /// <summary>Whether a cell joins the rectangle being grown from (x, y), at offset (dx, dy).</summary>
        private interface IRectangleCells
        {
            bool Joins(int x, int y, int dx, int dy);
        }

        /// <summary>
        ///     Grows a rectangle of cells from (x, y): rightward while cells join, then downward row by row,
        ///     never into a cell already <paramref name="visited" />, and marks the cells it takes as visited.
        /// </summary>
        /// <param name="narrowToShortRows">
        ///     A row that joins for only part of the width narrows the rectangle to it; otherwise such a row
        ///     ends the rectangle.
        /// </param>
        private static (int Width, int Height) GrowRectangle<TCells>(TCells cells, bool[] visited,
            int bufferWidth, int bufferHeight, int x, int y, bool narrowToShortRows)
            where TCells : struct, IRectangleCells
        {
            int width = 1;
            while (x + width < bufferWidth && !visited[y * bufferWidth + x + width] &&
                   cells.Joins(x, y, width, 0))
                width++;

            int height = 1;
            while (y + height < bufferHeight)
            {
                int rowStart = (y + height) * bufferWidth + x;
                int rowWidth = 0;
                while (rowWidth < width && !visited[rowStart + rowWidth] && cells.Joins(x, y, rowWidth, height))
                    rowWidth++;

                if (rowWidth == 0 || (rowWidth < width && !narrowToShortRows))
                    break;
                width = rowWidth;
                height++;
            }

            for (int row = 0; row < height; row++)
                visited.AsSpan((y + row) * bufferWidth + x, width).Fill(true);

            return (width, height);
        }

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

        /// <summary>Sixel cells to write this frame that share one palette.</summary>
        private readonly struct SixelCells(RenderTarget target, PixelBuffer pixelBuffer, bool[] dirty, byte[] palette)
            : IRectangleCells
        {
            public bool Joins(int x, int y, int dx, int dy)
            {
                return ReferenceEquals(target.SixelToWrite(pixelBuffer, dirty, x + dx, y + dy)?.Palette, palette);
            }
        }

        private static Color GetContrastColor(Color color)
        {
            // Calculate relative luminance using the formula from WCAG 2.0
            // https://www.w3.org/TR/WCAG20/#relativeluminancedef
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            // Choose black or white based on which provides better contrast
            // White luminance = 1.0, Black luminance = 0.0
            double contrastWithWhite = (1.0 + 0.05) / (luminance + 0.05);
            double contrastWithBlack = (luminance + 0.05) / (0.0 + 0.05);
            Color result = contrastWithWhite > contrastWithBlack ? Colors.White : Colors.Black;
            return result;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void OnCursorChanged(ConsoleCursor consoleCursor)
        {
            if (_consoleCursor.CompareTo(consoleCursor) == 0)
                return;

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_cursorTimer == null)
                return;

            _cursorTimer.Stop();

            ConsoleCursor oldConsoleCursor = _consoleCursor;
            _consoleCursor = consoleCursor;

            // Dirty rects expanded to handle potential wide char overlap
            var oldCursorRect = new PixelRect(oldConsoleCursor.Coordinate.X - 1,
                oldConsoleCursor.Coordinate.Y, oldConsoleCursor.Width + 1, 1);
            var newCursorRect = new PixelRect(consoleCursor.Coordinate.X - 1,
                consoleCursor.Coordinate.Y, consoleCursor.Width + 1, 1);

            _cursorDirtyRegions.AddRect(oldCursorRect);
            _cursorDirtyRegions.AddRect(newCursorRect);

            _cursorTimer.StartOnce(16);
        }
    }
}