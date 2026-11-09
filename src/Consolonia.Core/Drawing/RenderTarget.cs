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
        // It holds what the terminal shows, cell by cell. Cells never written hold Pixel.Empty.
        // At the end of a frame, the console gets this cache as the screen, not the pixel buffer. The two can
        // differ because only dirty cells are written, and the cell under the mouse cursor shows the cursor.
        private PixelBuffer _cache = null!;

        private ConsoleCursor _consoleCursor;

        /// <summary>The converter that draws bitmaps on this terminal. See <see cref="BitmapConverter" />.</summary>
        private BitmapConverter? _bitmapRenderer;

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
                    // Wait for any drawing in progress to finish. A frame written from a half-drawn buffer
                    // would show half-drawn cells, and the image encoder would place half-drawn images.
                    // Take RenderLock before the lock on this object, never after it.
                    lock (_consoleTopLevelImpl.RenderLock)
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
            _consoleTopLevelImpl.TerminalContentsLost += ResetCache;
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
            _consoleTopLevelImpl.TerminalContentsLost -= ResetCache;
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

        /// <summary>
        ///     The converter that draws bitmaps on this terminal. It is created on first use, when the console's
        ///     capabilities are known. Drawing contexts use it but do not own it. A resize creates a new drawing
        ///     context, and the converter keeps its cached conversions across that change.
        /// </summary>
        internal BitmapConverter BitmapConverter => _bitmapRenderer ??= BitmapConverter.Create(_console);

        internal void RenderToDevice()
        {
            // EndDrawing already holds this lock when it calls here. Monitor locks are re-entrant, so that is fine.
            lock (_consoleTopLevelImpl.RenderLock)
            {
                try
                {
                    RenderToDevice(_consoleTopLevelImpl.DirtyRegions);
                }
                catch (InvalidDrawingContextException)
                {
                }
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

            // The lock stays held until the context is disposed and the frame is written (see EndDrawing).
            Monitor.Enter(_consoleTopLevelImpl.RenderLock);
            try
            {
                if (_drawingContextImpl is null ||
                    _drawingContextImpl.PixelBuffer != _consoleTopLevelImpl.PixelBuffer)
                    _drawingContextImpl = new DrawingContextImpl(_consoleTopLevelImpl, this);

                return _drawingContextImpl;
            }
            catch
            {
                Monitor.Exit(_consoleTopLevelImpl.RenderLock);
                throw;
            }
        }

        /// <summary>
        ///     Called when the drawing context from <see cref="CreateDrawingContext" /> is finished. Writes the
        ///     frame, then releases the window's render lock that <see cref="CreateDrawingContext" /> took.
        /// </summary>
        internal void EndDrawing()
        {
            try
            {
                RenderToDevice();
            }
            finally
            {
                Monitor.Exit(_consoleTopLevelImpl.RenderLock);
            }
        }

        private static PixelBuffer InitializeCache(ushort width, ushort height)
        {
            var cache = new PixelBuffer(width, height);

            // initialize the cache with Pixel.Empty as it literally means nothing
            for (ushort y = 0; y < height; y++)
            for (ushort x = 0; x < width; x++)
                cache[x, y] = Pixel.Empty;

            return cache;
        }

        /// <summary>
        ///     Forgets what was written to the terminal, because the terminal may no longer show it. This happens
        ///     when the screen is resized, or when another program used the terminal while console I/O was paused.
        ///     The next frame writes every cell again. The console also clears its cached terminal state, so the
        ///     image encoder places every image again.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void ResetCache()
        {
            InitializeCacheInternal();
            _console.ResetCache();

            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            _consoleTopLevelImpl.DirtyRegions.AddRect(new PixelRect(0, 0, pixelBuffer.Width, pixelBuffer.Height));
        }

        /// <summary>
        ///     Writes a frame. Callers must hold the window's render lock, so the buffer is not half drawn.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        private void RenderToDevice(Snapshot.Regions regions)
        {
            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;

            // Skip the frame if the terminal changed size but the buffer has not been replaced yet.
            // A frame of the old size would not fit the new terminal. Long rows would wrap, the last row
            // would scroll the screen, and images would be placed at the wrong cells. The resize redraws
            // everything, so the dirty regions are kept for it.
            PixelBufferSize consoleSize = _console.Size;
            if (consoleSize.Width != pixelBuffer.Width || consoleSize.Height != pixelBuffer.Height) return;

            Snapshot dirtyRegions = regions.GetSnapshotAndClear();
            dirtyRegions.Intersect(0, 0, pixelBuffer.Width, pixelBuffer.Height);
            if (dirtyRegions.IsEmpty) return;

            // The buffer size changed, so reset the cache. This redraws everything.
            if (pixelBuffer.Width != _cache.Width || pixelBuffer.Height != _cache.Height)
                ResetCache();

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
            _console.BeginUpdate();
            try
            {
                RenderFrame(pixelBuffer, dirtyRegions);
            }
            finally
            {
                // Close the update even if writing a cell throws. Otherwise the terminal holds back all later output.
                _console.EndUpdate(_cache);
            }
        }

        private void RenderFrame(PixelBuffer pixelBuffer, Snapshot dirtyRegions)
        {
            _console.HideCaret();

            PixelBufferCoordinate? caretPosition = null;
            CaretStyle? caretStyle = null;

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
                // we can not run only from MinX to MaxX because of wide characters, also we can not run MinY/MaxY because we need to detect caret
                for (ushort x = 0; x < pixelBuffer.Width; x++)
                {
                    // the cell is looked at in place; only a dirty one is copied out, since it may be
                    // rewritten below before it is compared and written
                    ref readonly Pixel cell = ref pixelBuffer.CellAt(x, y);

                    if (cell.IsCaret())
                    {
                        if (caretPosition != null)
                            throw new InvalidOperationException("Caret is already shown");
                        caretPosition = new PixelBufferCoordinate(x, y);
                        caretStyle = cell.CaretStyle;
                    }

                    if (!dirtyRegions.Contains(x, y, false))
                        continue;

                    Pixel pixel = cell;

                    // painting mouse cursor if within the range of current pixel (possibly wide)
                    if (IsUnderMouseCursor(x, y))
                    {
                        // The background color under the cursor glyph. For a cell with an image, this is the
                        // image's main color blended with any color painted over it.
                        Color cellColor = pixel.EffectiveBackgroundColor;

                        if (_consoleCursor.Type == " " && pixel.Width == 1)
                        {
                            // floating cursor tracking effect
                            // if we are drawing a " " and the pixel underneath is not wide char
                            // then we lift the character from the underlying pixel and invert it
                            char cursorChar = pixel.Foreground.Symbol.Character != '\0'
                                ? pixel.Foreground.Symbol.Character
                                : ' ';
                            pixel = new Pixel(new PixelForeground(new Symbol(cursorChar, 1), cellColor),
                                new PixelBackground(GetContrastColor(cellColor)));
                        }
                        else
                        {
                            char cursorChar = _consoleCursor.Type[x - _consoleCursor.Coordinate.X];
                            // simply draw the mouse cursor character in the current pixel colors.
                            Color foreground = pixel.Foreground.Color != Colors.Transparent
                                ? pixel.Foreground.Color
                                : GetContrastColor(cellColor);
                            pixel = new Pixel(
                                new PixelForeground(new Symbol(cursorChar, 1), foreground,
                                    pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                                pixel.Background, pixel.CaretStyle);
                        }
                    }

                    if (pixel.Width > 1)
                        // checking that there are enough empty pixels after current wide character and if no, we want to render just empty space instead
                        for (ushort i = 1; i < pixel.Width && x + i < pixelBuffer.Width; i++)
                            if (pixelBuffer.CellAt(x + i, y).Width != 0)
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
                        // both sides compared in place (the operators take the cells by reference)
                        bool anyDifferent = pixel != _cache.CellAt(x, y);
                        for (ushort i = 1; !anyDifferent && i < pixel.Width; i++)
                            anyDifferent = pixelBuffer.CellAt(x + i, y) != _cache.CellAt(x + i, y);

                        if (!anyDifferent)
                            continue;
                    }

                    _console.WritePixel(new PixelBufferCoordinate(x, y), in pixel);

                    _cache[x, y] = pixel;
                }
            }

#if FPS
            var fps = $"FPS: {_fps: 000}";
            for (ushort i = 0; i < fps.Length; i++)
            {
                var pixel =
 new Pixel(new PixelForeground(new Symbol(fps[i]), Colors.White), new PixelBackground(Colors.Black));
                _console.WritePixel(new PixelBufferCoordinate((ushort)(pixelBuffer.Width - fps.Length + i), (ushort)(pixelBuffer.Height - 1)), in pixel);
            }
#endif

            // The caret was hidden at the start of the frame, so it only needs to be shown here.
            if (caretPosition != null && caretStyle != CaretStyle.None)
            {
                _console.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                _console.SetCaretStyle((CaretStyle)caretStyle!);
                _console.ShowCaret();
            }
        }

        private bool IsUnderMouseCursor(int x, int y)
        {
            return !_consoleCursor.IsEmpty() &&
                   _consoleCursor.Coordinate.Y == y &&
                   _consoleCursor.Coordinate.X <= x && x < _consoleCursor.Coordinate.X + _consoleCursor.Width;
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