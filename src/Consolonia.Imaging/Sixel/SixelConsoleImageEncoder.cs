using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Imaging
{
    /// <summary>
    ///     Shows image cells as sixels. A sixel is the content of the cells it covers, so at the frame's end
    ///     the picture cells that changed since they were last sent are sent as one sixel per run of
    ///     neighbouring cells on a row: one escape sequence and one palette for the run rather than one per
    ///     cell.
    /// </summary>
    /// <remarks>
    ///     A terminal shows either a sixel or text in a cell, never a glyph over a picture
    ///     (<see cref="SupportsTextOverlay" /> is false): the output writes a glyph over a picture as text
    ///     on the picture's dominant color, and such a cell is not a picture cell here. The runs a
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

        /// <summary>The picture cells of the frame to send, row by row.</summary>
        private readonly List<ImageCell> _cells = new();

        private readonly IConsoleOutput _output;
        private readonly RenderedRunCache _renderedRuns = new(RunCacheBudgetBytes);
        private readonly List<Run> _runs = new();
        private byte[] _frameBgra;
        private byte[] _runKey;

        /// <summary>
        ///     What each cell of the screen was last sent as, row by row: its picture and the paint over it,
        ///     default where no sixel of this encoder's is.
        /// </summary>
        private SentCell[] _sent = Array.Empty<SentCell>();

        private int _sentWidth;

        public SixelConsoleImageEncoder(IConsoleOutput output)
        {
            _output = output;
        }

        public bool SupportsTextOverlay => false;

        /// <summary>
        ///     Gathers the picture cells whose picture or paint is not what was last sent there, then sends
        ///     them. A cell with a glyph over its picture is the output's text; a wide glyph's continuation
        ///     cell is the glyph's.
        /// </summary>
        public void WriteImages(PixelBuffer screen)
        {
            int width = screen.Width;
            int height = screen.Height;
            if (_sentWidth != width || _sent.Length != width * height)
            {
                // a new size: the render target writes every cell again, and nothing sent is trusted
                _sent = new SentCell[width * height];
                _sentWidth = width;
            }

            _cells.Clear();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                ref readonly Pixel pixel = ref screen.CellAt(x, y);
                ref SentCell sent = ref _sent[y * width + x];
                PixelImage image = pixel.Background.Image;
                if (image == null || pixel.Width <= 0 || !pixel.Foreground.IsNothingToDraw())
                {
                    // text, which replaced whatever sixel was in the cell
                    sent = default;
                    continue;
                }

                var now = new SentCell(image.Digest, pixel.Background.Color);
                if (sent == now)
                    continue;

                sent = now;
                _cells.Add(new ImageCell(new PixelBufferCoordinate((ushort)x, (ushort)y), image,
                    pixel.Background.Color));
            }

            if (_cells.Count == 0)
                return;

            int cellWidth = _cells[0].Image.Width;
            int cellHeight = _cells[0].Image.Height;

            // gathered row by row, so neighbours on a row are consecutive here
            _runs.Clear();
            int start = 0;
            while (start < _cells.Count)
            {
                int end = start + 1;
                while (end < _cells.Count &&
                       _cells[end].Position.Y == _cells[start].Position.Y &&
                       _cells[end].Position.X == _cells[end - 1].Position.X + 1)
                    end++;

                PictureKey key = RunKey(start, end, cellWidth, cellHeight);
                _renderedRuns.TryGet(key, out byte[] rendered);
                _runs.Add(new Run(start, end, key, rendered, 0));
                start = end;
            }

            RenderNewRuns(cellWidth, cellHeight);

            // the cursor is where the sixel is drawn; after one, the output knows it no longer knows where
            // the cursor is
            foreach (Run run in _runs)
            {
                _output.SetCaretPosition(_cells[run.Start].Position);
                _output.WriteBytes(run.Rendered);
            }
        }

        /// <summary>The terminal may no longer show what was sent: every picture cell is sent again.</summary>
        public void Reset()
        {
            Array.Clear(_sent);
        }

        /// <summary>What a run shows: each cell's image and the paint over it.</summary>
        private PictureKey RunKey(int start, int end, int cellWidth, int cellHeight)
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

            return PictureKey.Of(key, cells * cellWidth, cellHeight);
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
                        PixelImage.Paint(bgra, frameRowBytes, column * cellRowBytes, cellWidth, cellHeight, cell.Paint);
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
                _renderedRuns.Add(run.Key, rendered);
                _runs[r] = run with { Rendered = rendered };
            }
        }

        /// <summary>A cell's picture, by its digest, and the paint over it; default for none.</summary>
        private readonly record struct SentCell(UInt128 Digest, Color Paint);

        private readonly record struct ImageCell(PixelBufferCoordinate Position, PixelImage Image, Color Paint);

        /// <summary>Cells <see cref="Start" /> to <see cref="End" /> (exclusive) of the frame's list, one sixel.</summary>
        private record struct Run(int Start, int End, PictureKey Key, byte[] Rendered, int FrameColumn)
        {
            public int Cells => End - Start;
        }
    }
}