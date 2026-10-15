using System;
using System.Buffers;
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
    ///     a glyph over a picture, so the glyph sits on the picture's dominant color instead. The runs a
    ///     frame has not sent before are quantized together, to one palette: Wu's quantizer has a large
    ///     fixed cost per call, and a new picture is a run per row. The rendered bytes of a run are cached
    ///     by its content: a modal closing over a picture re-sends the runs it dimmed from the cache
    ///     instead of quantizing the screen again.
    /// </remarks>
    internal sealed class SixelConsoleImageEncoder : IConsoleImageEncoder
    {
        /// <summary>Bytes of rendered runs kept: a few full screens' worth of sixel.</summary>
        private const long RunCacheBudgetBytes = 16L * 1024 * 1024;

        private const int RunKeyBytesPerCell = 16 + 4;
        private readonly List<ImageCell> _cells = new();

        private readonly AnsiConsoleOutput _output;
        private readonly ContentCache<byte[]> _renderedRuns = new(RunCacheBudgetBytes);
        private readonly List<Run> _runs = new();
        private byte[] _frameBgra;
        private byte[] _runKey;

        public SixelConsoleImageEncoder(AnsiConsoleOutput output)
        {
            _output = output;
        }

        public void BeginFrame()
        {
            _cells.Clear();
        }

        public void WriteCell(PixelBufferCoordinate position, in Pixel pixel)
        {
            // a blank symbol shows nothing, whatever color an overlay blended into it: the cell is the picture
            if (!pixel.Foreground.IsNothingToDraw() && !pixel.Foreground.Symbol.IsBlank())
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
            if (_cells.Count == 0)
                return;

            int cellWidth = _cells[0].Image.Width;
            int cellHeight = _cells[0].Image.Height;

            // the render target writes cells row by row, so neighbours on a row are consecutive here
            _runs.Clear();
            int start = 0;
            while (start < _cells.Count)
            {
                int end = start + 1;
                while (end < _cells.Count &&
                       _cells[end].Position.Y == _cells[start].Position.Y &&
                       _cells[end].Position.X == _cells[end - 1].Position.X + 1)
                    end++;

                ContentKey key = RunKey(start, end, cellWidth, cellHeight);
                _renderedRuns.TryGet(key, out byte[] rendered);
                _runs.Add(new Run(start, end, key, rendered, 0));
                start = end;
            }

            RenderNewRuns(cellWidth, cellHeight);

            foreach (Run run in _runs)
                _output.WriteImageBytes(_cells[run.Start].Position, run.Rendered, run.Cells);

            _cells.Clear();
            _runs.Clear();
        }

        public void Reset()
        {
            _cells.Clear();
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
                MemoryMarshal.Write(key[offset..], cell.Image.Digest);
                BitConverter.TryWriteBytes(key[(offset + 16)..], cell.Paint.ToUInt32());
            }

            return ContentKey.Of(key, cells * cellWidth, cellHeight);
        }

        /// <summary>
        ///     Renders the runs the cache did not have: their cells' pixels side by side in one image, each
        ///     cell's paint composited over its own, quantized once to one palette, then each run's slice
        ///     encoded and cached.
        /// </summary>
        private void RenderNewRuns(int cellWidth, int cellHeight)
        {
            int newCells = 0;
            for (int i = 0; i < _runs.Count; i++)
            {
                Run run = _runs[i];
                if (run.Rendered != null)
                    continue;
                _runs[i] = run with { FrameColumn = newCells };
                newCells += run.Cells;
            }

            if (newCells == 0)
                return;

            int frameWidth = newCells * cellWidth;
            int frameRowBytes = frameWidth * 4;
            int cellRowBytes = cellWidth * 4;

            // exactly the new pixels: the quantizer reads the whole array
            int bytes = frameRowBytes * cellHeight;
            if (_frameBgra == null || _frameBgra.Length != bytes)
                _frameBgra = GC.AllocateUninitializedArray<byte>(bytes);
            byte[] bgra = _frameBgra;

            foreach (Run run in _runs)
            {
                if (run.Rendered != null)
                    continue;

                for (int i = run.Start; i < run.End; i++)
                {
                    ImageCell cell = _cells[i];
                    ReadOnlySpan<byte> source = cell.Image.Bgra;
                    int column = run.FrameColumn + i - run.Start;
                    for (int y = 0; y < cellHeight; y++)
                        source.Slice(y * cellRowBytes, cellRowBytes)
                            .CopyTo(bgra.AsSpan(y * frameRowBytes + column * cellRowBytes, cellRowBytes));

                    if (cell.Paint.A != 0)
                        CellImage.Paint(bgra, frameRowBytes, column * cellRowBytes, cellWidth, cellHeight, cell.Paint);
                }
            }

            Sixel.Quantize(bgra, out byte[] palette, out int paletteCount, out byte[] indexed);

            for (int r = 0; r < _runs.Count; r++)
            {
                Run run = _runs[r];
                if (run.Rendered != null)
                    continue;

                // the run's slice of the indexes, rented: only the rendered bytes outlive this
                int runWidth = run.Cells * cellWidth;
                byte[] runIndexed = ArrayPool<byte>.Shared.Rent(runWidth * cellHeight);
                for (int y = 0; y < cellHeight; y++)
                    indexed.AsSpan(y * frameWidth + run.FrameColumn * cellWidth, runWidth)
                        .CopyTo(runIndexed.AsSpan(y * runWidth));

                // transient: the bytes are kept by the run cache, not by a sixel nothing holds on to
                byte[] rendered = new Sixel(palette, paletteCount, runIndexed, runWidth, cellHeight, cellWidth,
                    cellHeight) { IsTransient = true }.Render().ToArray();
                ArrayPool<byte>.Shared.Return(runIndexed);
                _renderedRuns.GetOrAdd(run.Key, rendered, rendered.Length);
                _runs[r] = run with { Rendered = rendered };
            }
        }

        private readonly record struct ImageCell(PixelBufferCoordinate Position, CellImage Image, Color Paint);

        /// <summary>Cells <see cref="Start" /> to <see cref="End" /> (exclusive) of the frame's list, one sixel.</summary>
        private record struct Run(int Start, int End, ContentKey Key, byte[] Rendered, int FrameColumn)
        {
            public int Cells => End - Start;
        }
    }
}