using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    /// Renders to device via IConsoleOutput implementation.
    /// </summary>
    /// <remarks>
    /// NOTE: The ConsoleOutputRenderer does not have to listen to resize changes because 
    /// a new RenderTarget/Console/Renderer will be created by Avalonia on layer resizing.
    /// </remarks>
    internal class ConsoleOutputRenderer : IConsoleDeviceRenderer
    {
        private readonly IConsoleOutput _consoleOutput;

        private readonly ConsoleWindowImpl _consoleTopLevelImpl;

        // cache of pixels written so we can ignore them if unchanged.
        private Pixel?[,] _cache;
        private ConsoleCursor _consoleCursor = new ConsoleCursor(new PixelBufferCoordinate((ushort)0, (ushort)0), String.Empty);

        internal ConsoleOutputRenderer(ConsoleWindowImpl consoleTopLevelImpl, IConsoleOutput consoleOutput)
        {
            _consoleOutput = consoleOutput;
            _consoleTopLevelImpl = consoleTopLevelImpl;
            _cache = InitializeCache(_consoleTopLevelImpl.PixelBuffer.Width, _consoleTopLevelImpl.PixelBuffer.Height);
            // _consoleTopLevelImpl.Resized += OnResized;
            _consoleTopLevelImpl.CursorChanged += OnCursorChanged;
        }

        public PixelBuffer Buffer => _consoleTopLevelImpl.PixelBuffer;

        public void Dispose()
        {
            _consoleTopLevelImpl.CursorChanged -= OnCursorChanged;
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
        public void RenderToDevice()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            Snapshot dirtyRegions = _consoleTopLevelImpl.DirtyRegions.GetSnapshotAndClear();

            _consoleOutput.HideCaret();

            PixelBufferCoordinate? caretPosition = null;
            CaretStyle? caretStyle = null;

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
                for (ushort x = 0; x < pixelBuffer.Width; x++)
                {
                    Pixel pixel = pixelBuffer[x, y];

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
                                new PixelBackground(pixel.Background.Color.GetContrastColor()));
                        }
                        else
                        {
                            char cursorChar = _consoleCursor.Type[x - _consoleCursor.Coordinate.X];
                            // simply draw the mouse cursor character in the current pixel colors.
                            Color foreground = pixel.Foreground.Color != Colors.Transparent
                                ? pixel.Foreground.Color
                                : pixel.Background.Color.GetContrastColor();
                            pixel = new Pixel(
                                new PixelForeground(new Symbol(cursorChar, 1), foreground,
                                    pixel.Foreground.Weight, pixel.Foreground.Style, pixel.Foreground.TextDecoration),
                                pixel.Background, pixel.CaretStyle);
                        }
                    }


                    // Handle wide glyphs similarly to ConsoleOutputDeviceRenderer.
                    if (pixel.Width > 1)
                    {
                        isWide = true;

                        // If the wide glyph would overlap non-empty continuation cells, render space instead.
                        for (ushort i = 1; i < pixel.Width && x + i < pixelBuffer.Width; i++)
                        {
                            if (pixelBuffer[(ushort)(x + i), y].Width != 0)
                            {
                                pixel = Pixel.Space;
                                break;
                            }
                        }
                    }

                    if (pixel.Width == 0 && !isWide)
                        pixel = Pixel.Space;


                    if (pixel.IsCaret())
                    {
                        if (caretPosition != null)
                            throw new InvalidOperationException("Caret is already shown");
                        caretPosition = new PixelBufferCoordinate(x, y);
                        caretStyle = pixel.CaretStyle;
                    }

                    if (!dirtyRegions.Contains(x, y, false))
                        continue;

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

                    //todo: indexOutOfRange during resize

                    _consoleOutput.WritePixel(new PixelBufferCoordinate(x, y), in pixel);

                    _cache[x, y] = pixel;
                }
            }

            _consoleOutput.Flush();
            sw.Stop();
            _consoleOutput.SetCaretPosition(new PixelBufferCoordinate(0, 0));
            _consoleOutput.WriteText("RenderToDevice time: " + sw.ElapsedMilliseconds + " ms\n");

            if (caretPosition != null && caretStyle != CaretStyle.None)
            {
                _consoleOutput.SetCaretPosition((PixelBufferCoordinate)caretPosition);
                _consoleOutput.SetCaretStyle((CaretStyle)caretStyle!);
                _consoleOutput.ShowCaret();
            }
            else
            {
                _consoleOutput.HideCaret(); //todo: Caret was hidden at the beginning of this method, why to hide it again?
            }
        }


        private void OnCursorChanged(ConsoleCursor consoleCursor)
        {
            if (_consoleCursor.CompareTo(consoleCursor) == 0)
                return;

            ConsoleCursor oldConsoleCursor = _consoleCursor;
            _consoleCursor = consoleCursor;

            //todo: low excessive refresh, emptiness can be checked

            // Dirty rects expanded to handle potential wide char overlap
            var oldCursorRect = new PixelRect(oldConsoleCursor.Coordinate.X - 1,
                oldConsoleCursor.Coordinate.Y, oldConsoleCursor.Width + 1, 1);
            var newCursorRect = new PixelRect(consoleCursor.Coordinate.X - 1,
                consoleCursor.Coordinate.Y, consoleCursor.Width + 1, 1);
            _consoleTopLevelImpl.DirtyRegions.AddRect(oldCursorRect);
            _consoleTopLevelImpl.DirtyRegions.AddRect(newCursorRect);

            RenderToDevice();
        }
    }
}