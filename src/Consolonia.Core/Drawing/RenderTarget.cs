#nullable enable
using System;
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

        private ConsoleCursor _consoleCursor;

        // kitty image ids referenced this frame; ids which drop out get their placements deleted terminal side
        private HashSet<int> _kittyImageIdsOnScreen = new();
        private HashSet<int> _kittyImageIdsPreviouslyOnScreen = new();

        // classic rect placements coalesced from KittyTile cell backgrounds at z=-1, diffed across frames
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


        [MethodImpl(MethodImplOptions.Synchronized)]
        private void RenderToDevice(Snapshot.Regions regions)
        {
            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            Snapshot dirtyRegions = regions.GetSnapshotAndClear();
            dirtyRegions.Intersect(0, 0, pixelBuffer.Width, pixelBuffer.Height);
            if (dirtyRegions.IsEmpty) return;

            if (pixelBuffer.Width != _cache.GetLength(0) || pixelBuffer.Height != _cache.GetLength(1))
                InitializeCacheInternal();

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

            // Pass 1: sixel regions; sixelHandled lets pass 2 skip the cells it took
            bool[,]? sixelHandled = null;
            RenderSixelRegions(pixelBuffer, dirtyRegions, ref sixelHandled);

            // Pass 2: Render non-sixel dirty pixels.
            bool sawKittyTiles = false;
            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
                // we can not run only from MinX to MaxX because of wide characters, also we can not run MinY/MaxY because we need to detect caret
                for (ushort x = 0; x < pixelBuffer.Width; x++)
                {
                    Pixel pixel = pixelBuffer[x, y];

                    if (KittyGraphics.TryGetImageId(in pixel, out int kittyImageId))
                        _kittyImageIdsOnScreen.Add(kittyImageId);

                    if (!pixel.Background.Tile.IsEmpty)
                        sawKittyTiles = true;

                    if (pixel.IsCaret())
                    {
                        if (caretPosition != null)
                            throw new InvalidOperationException("Caret is already shown");
                        caretPosition = new PixelBufferCoordinate(x, y);
                        caretStyle = pixel.CaretStyle;
                    }

                    if (!dirtyRegions.Contains(x, y, false))
                        continue;

                    if (sixelHandled != null && sixelHandled[x, y])
                        continue;

                    // sixel cells pass 1 did not claim (shouldn't happen, but never write them as text)
                    if (pixel.Foreground.Symbol.Sixel != null)
                        continue;

                    // a tile cell's color is the wash for its image (laid over it by a wash placement);
                    // the terminal cell itself, seen only through transparent parts of the image, is
                    // black with the wash composited onto it
                    if (!pixel.Background.Tile.IsEmpty)
                        pixel = new Pixel(pixel.Foreground,
                            new PixelBackground(CompositeOverBlack(pixel.Background.Color), pixel.Background.Tile),
                            pixel.CaretStyle);

                    // painting mouse cursor if within the range of current pixel (possibly wide)
                    if (!_consoleCursor.IsEmpty() &&
                        _consoleCursor.Coordinate.Y == y &&
                        _consoleCursor.Coordinate.X <= x && x < _consoleCursor.Coordinate.X + _consoleCursor.Width)
                    {
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

            if (sawKittyTiles || _kittyRectPlacements.Count > 0)
                EmitKittyRectPlacements(pixelBuffer);

            // overwriting the cells is what the protocol prescribes, but terminals which materialize
            // placements as overlays keep showing the image until its placement is deleted
            foreach (int imageId in _kittyImageIdsPreviouslyOnScreen)
                if (!_kittyImageIdsOnScreen.Contains(imageId))
                {
                    _console.WriteText(KittyGraphics.BuildDeletePlacementSequence(imageId));
                    KittyGraphics.MarkPlacementDeleted(imageId);
                }

            (_kittyImageIdsPreviouslyOnScreen, _kittyImageIdsOnScreen) =
                (_kittyImageIdsOnScreen, _kittyImageIdsPreviouslyOnScreen);
            _kittyImageIdsOnScreen.Clear();

            _console.Flush();
#if FPS
            var fps = $"FPS: {_fps: 000}";
            for (ushort i = 0; i < fps.Length; i++)
            {
                var pixel =
 new Pixel(new PixelForeground(new Symbol(fps[i]), Colors.White), new PixelBackground(Colors.Black));
                _console.WritePixel(new PixelBufferCoordinate((ushort)(pixelBuffer.Width - fps.Length + i), (ushort)(pixelBuffer.Height - 1)), in pixel);
            }
            _console.Flush();
#endif

            if (caretPosition != null && caretStyle != CaretStyle.None)
            {
                _console.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                _console.SetCaretStyle((CaretStyle)caretStyle!);
                _console.ShowCaret();
            }
            else
            {
                _console.HideCaret(); //todo: Caret was hidden at the beginning of this method, why to hide it again?
            }
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

            bool[,] visited = new bool[pixelBuffer.Width, pixelBuffer.Height];

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            for (ushort x = 0; x < pixelBuffer.Width; x++)
            {
                if (visited[x, y])
                    continue;

                KittyTile tile = pixelBuffer[x, y].Background.Tile;
                if (tile.IsEmpty)
                    continue;
                Color wash = pixelBuffer[x, y].Background.Color;

                // expand rightward while the tiles continue the same image's row under the same wash
                ushort width = 1;
                while (x + width < pixelBuffer.Width && !visited[x + width, y])
                {
                    PixelBackground nextBackground = pixelBuffer[(ushort)(x + width), y].Background;
                    KittyTile nextTile = nextBackground.Tile;
                    if (nextTile.ImageId != tile.ImageId ||
                        nextTile.X != tile.X + width ||
                        nextTile.Y != tile.Y ||
                        nextBackground.Color != wash)
                        break;
                    width++;
                }

                // expand downward while each row continues the same tile grid at full width
                ushort height = 1;
                while (y + height < pixelBuffer.Height)
                {
                    bool rowMatches = true;
                    for (ushort i = 0; i < width; i++)
                    {
                        PixelBackground rowBackground =
                            pixelBuffer[(ushort)(x + i), (ushort)(y + height)].Background;
                        KittyTile rowTile = rowBackground.Tile;
                        if (visited[x + i, y + height] ||
                            rowTile.ImageId != tile.ImageId ||
                            rowTile.X != tile.X + i ||
                            rowTile.Y != tile.Y + height ||
                            rowBackground.Color != wash)
                        {
                            rowMatches = false;
                            break;
                        }
                    }

                    if (!rowMatches)
                        break;
                    height++;
                }

                for (ushort dy = 0; dy < height; dy++)
                for (ushort dx = 0; dx < width; dx++)
                    visited[x + dx, y + dy] = true;

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
                    KittyGraphics.BuildDeleteRectPlacementSequence(stale.Key.ImageId, stale.Value));
            live.Clear();

            foreach (KeyValuePair<(KittyRect Rect, Color Wash), int> stale in liveWash)
                _console.WriteText(KittyGraphics.BuildDeleteRectPlacementSequence(
                    _kittyWashImages[stale.Key.Wash].ImageId, stale.Value));
            liveWash.Clear();

            (_kittyRectPlacements, _kittyRectPlacementsScratch) = (next, live);
            (_kittyWashPlacements, _kittyWashPlacementsScratch) = (nextWash, liveWash);

            FreeUnusedWashImages();
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

            foreach ((Color wash, (int imageId, ushort imageColumns, ushort imageRows)) in _kittyWashImages.ToList())
            {
                if (imageColumns >= columns && imageRows >= rows)
                    continue;

                // uppercase d=I takes the image's placements with it
                _console.WriteText(KittyGraphics.BuildDeleteSequence(imageId));
                _kittyWashImages.Remove(wash);
                foreach ((KittyRect, Color) key in _kittyWashPlacements.Keys.Where(key => key.Item2 == wash).ToList())
                    _kittyWashPlacements.Remove(key);
            }
        }

        /// <summary>
        ///     Deletes the wash images no placement shows anymore. An animated overlay goes through a
        ///     new color every frame, and each one is only a few hundred compressed bytes to send again.
        /// </summary>
        private void FreeUnusedWashImages()
        {
            if (_kittyWashImages.Count == 0)
                return;

            var inUse = new HashSet<Color>();
            foreach ((KittyRect _, Color wash) in _kittyWashPlacements.Keys)
                inUse.Add(wash);

            foreach (Color wash in _kittyWashImages.Keys.Where(wash => !inUse.Contains(wash)).ToList())
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

        /// <summary>
        ///     Pass 1: finds contiguous dirty sixel cells sharing the same palette, combines them into a
        ///     single Sixel via BitBlt and writes that once, marking the cells handled for pass 2.
        /// </summary>
        private void RenderSixelRegions(PixelBuffer pixelBuffer, Snapshot dirtyRegions, ref bool[,]? sixelHandled)
        {
            bool[,] visited = new bool[pixelBuffer.Width, pixelBuffer.Height];

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            for (ushort x = 0; x < pixelBuffer.Width; x++)
            {
                if (visited[x, y])
                    continue;

                Pixel pixel = pixelBuffer[x, y];
                Sixel? cellSixel = pixel.Foreground.Symbol.Sixel;
                if (cellSixel == null)
                    continue;

                if (!dirtyRegions.Contains(x, y, false))
                    continue;

                // expand to the maximal rectangle of dirty cells sharing this palette instance
                byte[] palette = cellSixel.Palette;
                int cellPixelWidth = cellSixel.CellWidth;
                int cellPixelHeight = cellSixel.CellHeight;

                int maxWidth = 1;
                while (x + maxWidth < pixelBuffer.Width)
                {
                    Pixel nextPixel = pixelBuffer[(ushort)(x + maxWidth), y];
                    Sixel? nextSixel = nextPixel.Foreground.Symbol.Sixel;
                    if (nextSixel == null || !ReferenceEquals(nextSixel.Palette, palette))
                        break;
                    if (!dirtyRegions.Contains((ushort)(x + maxWidth), y, false))
                        break;
                    maxWidth++;
                }

                int rectHeight = 1;
                while (y + rectHeight < pixelBuffer.Height)
                {
                    int rowWidth = 0;
                    while (rowWidth < maxWidth)
                    {
                        Pixel belowPixel = pixelBuffer[(ushort)(x + rowWidth), (ushort)(y + rectHeight)];
                        Sixel? belowSixel = belowPixel.Foreground.Symbol.Sixel;
                        if (belowSixel == null || !ReferenceEquals(belowSixel.Palette, palette))
                            break;
                        if (!dirtyRegions.Contains((ushort)(x + rowWidth), (ushort)(y + rectHeight), false))
                            break;
                        rowWidth++;
                    }

                    if (rowWidth == 0)
                        break;

                    // narrow instead of extending ragged: the region must stay rectangular
                    if (rowWidth < maxWidth)
                        maxWidth = rowWidth;
                    rectHeight++;
                }

                for (int ry = 0; ry < rectHeight; ry++)
                for (int rx = 0; rx < maxWidth; rx++)
                    visited[x + rx, y + ry] = true;

                if (maxWidth == 1 && rectHeight == 1)
                {
                    _console.WriteSixel(new PixelBufferCoordinate(x, y), cellSixel);
                    _cache[x, y] = pixel;
                    sixelHandled ??= new bool[pixelBuffer.Width, pixelBuffer.Height];
                    sixelHandled[x, y] = true;
                    continue;
                }

                // one BitBlt'd sixel for the whole rectangle costs a single escape sequence
                int combinedWidth = maxWidth * cellPixelWidth;
                int combinedHeight = rectHeight * cellPixelHeight;
                byte[] combinedPixels = new byte[combinedWidth * combinedHeight];
                var combined = new Sixel(palette, cellSixel.PaletteCount, combinedPixels,
                    combinedWidth, combinedHeight, cellPixelWidth, cellPixelHeight);

                for (int ry = 0; ry < rectHeight; ry++)
                for (int rx = 0; rx < maxWidth; rx++)
                {
                    Pixel cellPixel = pixelBuffer[(ushort)(x + rx), (ushort)(y + ry)];
                    Sixel? cellData = cellPixel.Foreground.Symbol.Sixel;
                    if (cellData != null)
                        combined.BitBlt(cellData, rx * cellPixelWidth, ry * cellPixelHeight);
                }

                _console.WriteSixel(new PixelBufferCoordinate(x, y), combined);

                sixelHandled ??= new bool[pixelBuffer.Width, pixelBuffer.Height];
                for (int ry = 0; ry < rectHeight; ry++)
                for (int rx = 0; rx < maxWidth; rx++)
                {
                    _cache[x + rx, y + ry] = pixelBuffer[(ushort)(x + rx), (ushort)(y + ry)];
                    sixelHandled[x + rx, y + ry] = true;
                }
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