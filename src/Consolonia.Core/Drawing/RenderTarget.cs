#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal class RenderTarget : IDrawingContextLayerImpl
    {
        private readonly IConsoleOutput _console;

        private readonly ConsoleWindowImpl _consoleTopLevelImpl;

        // cache of pixels written so we can ignore them if unchanged.
        private Pixel?[,] _cache;
        private ConsoleCursor _consoleCursor;

        internal RenderTarget(ConsoleWindowImpl consoleTopLevelImpl)
        {
            _console = AvaloniaLocator.Current.GetService<IConsoleOutput>()!;
            _consoleTopLevelImpl = consoleTopLevelImpl;
            _cache = InitializeCache(_consoleTopLevelImpl.PixelBuffer.Width, _consoleTopLevelImpl.PixelBuffer.Height);
            _consoleTopLevelImpl.Resized += OnResized;
            _consoleTopLevelImpl.CursorChanged += OnCursorChanged;
        }

        public RenderTarget(IEnumerable<object> surfaces)
            : this(surfaces.OfType<ConsoleWindowImpl>()
                .Single())
        {
        }

        public PixelBuffer Buffer => _consoleTopLevelImpl.PixelBuffer;

        public void Dispose()
        {
            _consoleTopLevelImpl.Resized -= OnResized;
            _consoleTopLevelImpl.CursorChanged -= OnCursorChanged;
        }

        public void Save(string fileName, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public void Save(Stream stream, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public Vector Dpi { get; } = Vector.One;
        public PixelSize PixelSize { get; } = new(1, 1);
        public int Version => 0;

        void IDrawingContextLayerImpl.Blit(IDrawingContextImpl context)
        {
            try
            {
                RenderToDevice();
            }
            catch (InvalidDrawingContextException)
            {
            }
        }

        bool IDrawingContextLayerImpl.CanBlit => true;

        public bool IsCorrupted => false;

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
        {
            if (useScaledDrawing)
                throw new NotImplementedException("Consolonia doesn't support useScaledDrawing");
            return new DrawingContextImpl(_consoleTopLevelImpl);
        }


        private void OnResized(Size size, WindowResizeReason reason)
        {
            // todo: should we check the reason?
            _cache = InitializeCache(_consoleTopLevelImpl.PixelBuffer.Width, _consoleTopLevelImpl.PixelBuffer.Height);
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
        private void RenderToDevice()
        {
            PixelBuffer pixelBuffer = _consoleTopLevelImpl.PixelBuffer;
            Snapshot dirtyRegions = _consoleTopLevelImpl.DirtyRegions.GetSnapshotAndClear();

            _console.HideCaret();

            PixelBufferCoordinate? caretPosition = null;
            CaretStyle? caretStyle = null;

            for (ushort y = 0; y < pixelBuffer.Height; y++)
            {
                bool isWide = false;
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

                    if (!dirtyRegions.Contains(x, y, false))
                        continue;

                    // painting mouse cursor if within the range of current pixel (possibly wide)
                    if (!_consoleCursor.IsEmpty() &&
                        _consoleCursor.Coordinate.Y == y &&
                        _consoleCursor.Coordinate.X <= x && x < _consoleCursor.Coordinate.X + _consoleCursor.Width)
                        pixel = pixel.Blend(new Pixel(new PixelForeground(
                            new Symbol(_consoleCursor.Type[x - _consoleCursor.Coordinate.X], 1),
                            GetInvertColor(pixel.Background.Color))));

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

                    _console.WritePixel(new PixelBufferCoordinate(x, y), in pixel);

                    _cache[x, y] = pixel;
                }
            }

            _console.Flush();

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

        private static Color GetInvertColor(Color color)
        {
            return Color.FromRgb((byte)(255 - color.R),
                (byte)(255 - color.G),
                (byte)(255 - color.B));
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