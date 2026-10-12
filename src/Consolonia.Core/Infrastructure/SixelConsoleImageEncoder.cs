using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Shows image cells as sixels. A sixel is the content of the cells it covers, so the changed image
    ///     cells of a frame are gathered as they are written and sent at the frame's end as one sixel per
    ///     run of neighbouring cells on a row: one escape sequence and one palette for the run rather than
    ///     one per cell.
    /// </summary>
    /// <remarks>
    ///     A cell with a glyph is written as text: a terminal shows either a sixel or text in a cell, never
    ///     a glyph over a picture, so the glyph sits on the picture's dominant color instead. The rendered
    ///     bytes of a run are cached by its content: a modal closing over a picture re-sends the runs it
    ///     dimmed from the cache instead of quantizing the screen again.
    /// </remarks>
    internal sealed class SixelConsoleImageEncoder : IConsoleImageEncoder
    {
        /// <summary>Bytes of rendered runs kept: a few full screens' worth of sixel.</summary>
        private const long RunCacheBudgetBytes = 16L * 1024 * 1024;

        private const int RunKeyBytesPerCell = 16 + 4;

        private readonly AnsiConsoleOutput _output;
        private readonly List<ImageCell> _cells = new();
        private readonly ContentCache<byte[]> _renderedRuns = new(RunCacheBudgetBytes);
        private byte[] _runBgra;
        private byte[] _runKey;

        public SixelConsoleImageEncoder(AnsiConsoleOutput output)
        {
            _output = output;
        }

        private readonly record struct ImageCell(PixelBufferCoordinate Position, CellImage Image, Color Paint);

        public void BeginFrame()
        {
            _cells.Clear();
        }

        public void WriteCell(PixelBufferCoordinate position, in Pixel pixel)
        {
            if (!pixel.Foreground.IsNothingToDraw())
            {
                // text over a picture: the glyph on the color the cell looks, as a text cell
                var textCell = new Pixel(pixel.Foreground, new PixelBackground(pixel.Background.EffectiveColor),
                    pixel.CaretStyle);
                _output.WriteTextCell(position, in textCell, false);
                return;
            }

            _cells.Add(new ImageCell(position, pixel.Background.Image, pixel.Background.Color));
        }

        public void EndFrame(PixelBuffer screen)
        {
            // the render target writes cells row by row, so neighbours on a row are consecutive here
            int start = 0;
            while (start < _cells.Count)
            {
                int end = start + 1;
                while (end < _cells.Count &&
                       _cells[end].Position.Y == _cells[start].Position.Y &&
                       _cells[end].Position.X == _cells[end - 1].Position.X + 1)
                    end++;

                WriteRun(start, end);
                start = end;
            }

            _cells.Clear();
        }

        public void Reset()
        {
            _cells.Clear();
        }

        /// <summary>Sends the cells <paramref name="start" /> to <paramref name="end" /> (exclusive) as one sixel.</summary>
        private void WriteRun(int start, int end)
        {
            int cells = end - start;
            CellImage first = _cells[start].Image;
            int cellWidth = first.Width;
            int cellHeight = first.Height;

            ContentKey runKey = RunKey(start, end, cellWidth, cellHeight);
            if (!_renderedRuns.TryGet(runKey, out byte[] rendered))
            {
                rendered = RenderRun(start, end, cellWidth, cellHeight);
                _renderedRuns.GetOrAdd(runKey, rendered, rendered.Length);
            }

            _output.WriteImageBytes(_cells[start].Position, rendered, cells);
        }

        /// <summary>What a run shows: each cell's image and the paint over it.</summary>
        private ContentKey RunKey(int start, int end, int cellWidth, int cellHeight)
        {
            int cells = end - start;
            int bytes = cells * RunKeyBytesPerCell;
            if (_runKey == null || _runKey.Length < bytes)
                _runKey = new byte[Math.Max(bytes, 256 * RunKeyBytesPerCell)];
            Span<byte> key = _runKey.AsSpan(0, bytes);

            int offset = 0;
            for (int i = start; i < end; i++, offset += RunKeyBytesPerCell)
            {
                ImageCell cell = _cells[i];
                MemoryMarshal.Write(key[offset..], cell.Image.Key.Digest);
                BitConverter.TryWriteBytes(key[(offset + 16)..], cell.Paint.ToUInt32());
            }

            return ContentKey.Of(key, cells * cellWidth, cellHeight);
        }

        /// <summary>
        ///     The sixel bytes of a run: the cells' pixels side by side, each cell's paint composited over
        ///     its own, quantized to one palette and encoded.
        /// </summary>
        private byte[] RenderRun(int start, int end, int cellWidth, int cellHeight)
        {
            int cells = end - start;
            int runWidth = cells * cellWidth;
            int runRowBytes = runWidth * 4;
            int cellRowBytes = cellWidth * 4;
            int bytes = runRowBytes * cellHeight;

            // exactly the run's pixels: the quantizer reads the whole array
            if (_runBgra == null || _runBgra.Length != bytes)
                _runBgra = GC.AllocateUninitializedArray<byte>(bytes);
            byte[] bgra = _runBgra;

            for (int i = start; i < end; i++)
            {
                ImageCell cell = _cells[i];
                byte[] source = cell.Image.Bgra;
                int column = i - start;
                for (int y = 0; y < cellHeight; y++)
                    source.AsSpan(y * cellRowBytes, cellRowBytes)
                        .CopyTo(bgra.AsSpan(y * runRowBytes + column * cellRowBytes, cellRowBytes));

                if (cell.Paint.A != 0)
                    Paint(bgra, runRowBytes, column * cellRowBytes, cellWidth, cellHeight, cell.Paint);
            }

            // transient: the bytes are kept by the run cache, not by a sixel nothing holds on to
            return Sixel.CreateFromBitmap(bgra, runWidth, cellHeight, cellWidth, cellHeight, true).Render().ToArray();
        }

        /// <summary>Composites <paramref name="paint" /> over the pixels of one cell of the run.</summary>
        private static void Paint(byte[] bgra, int rowBytes, int cellOffset, int cellWidth, int cellHeight,
            Color paint)
        {
            int alpha = paint.A;
            int inverse = 255 - alpha;
            int blue = paint.B * alpha;
            int green = paint.G * alpha;
            int red = paint.R * alpha;
            for (int y = 0; y < cellHeight; y++)
            {
                int offset = y * rowBytes + cellOffset;
                for (int x = 0; x < cellWidth; x++, offset += 4)
                {
                    bgra[offset] = (byte)((blue + bgra[offset] * inverse) / 255);
                    bgra[offset + 1] = (byte)((green + bgra[offset + 1] * inverse) / 255);
                    bgra[offset + 2] = (byte)((red + bgra[offset + 2] * inverse) / 255);
                }
            }
        }
    }
}
