using System;
using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     What the sixel renderer does for one frame of a full-screen picture: turn the visible
    ///     pixels into per-cell sixels, reusing every cell it has made before.
    /// </summary>
    /// <remarks>
    ///     1920x1072 pixels in 8x16 cells is 240x67 cells. The scenarios are the ones a paint program
    ///     produces: nothing changed, one cell changed (a brush stroke), and every cell new (a new
    ///     picture, or a scroll).
    /// </remarks>
    [MemoryDiagnoser]
    public class SixelFrameBenchmarks
    {
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int CellsWide = 240;
        private const int CellsHigh = 67;
        private const int Width = CellsWide * CellWidth;
        private const int Height = CellsHigh * CellHeight;

        private byte[] _frame = null!;
        private byte[] _newFrame = null!;
        private int _stroke;

        [GlobalSetup]
        public void Setup()
        {
            _frame = MakePicture(0);
            SixelBitmapRenderer.RenderCells(_frame, CellsWide, CellsHigh, CellWidth, CellHeight);
        }

        /// <summary>A photo-like picture: smooth gradients with fine noise, so cells differ.</summary>
        private static byte[] MakePicture(int seed)
        {
            byte[] bgrx = new byte[Width * Height * 4];
            var random = new Random(seed);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                int noise = random.Next(8);
                bgrx[offset] = (byte)((x * 255 / Width + noise + seed) & 0xFF);
                bgrx[offset + 1] = (byte)((y * 255 / Height + noise) & 0xFF);
                bgrx[offset + 2] = (byte)(((x + y) * 255 / (Width + Height) + noise) & 0xFF);
                bgrx[offset + 3] = 0xFF;
            }

            return bgrx;
        }

        /// <summary>
        ///     Every cell already cached, as after scrolling back to a picture: a rendering-key miss that
        ///     only hashes. (A frame where nothing changed at all reuses its rendering and never gets here.)
        /// </summary>
        [Benchmark(Baseline = true)]
        public PixelBuffer FrameUnchanged()
        {
            return SixelBitmapRenderer.RenderCells(_frame, CellsWide, CellsHigh, CellWidth, CellHeight);
        }

        /// <summary>
        ///     One cell gets pixels it has never had, as under a brush: every other cell is reused.
        /// </summary>
        [Benchmark]
        public PixelBuffer FrameOneCellChanged()
        {
            int stroke = ++_stroke;
            int cellX = stroke % CellsWide;
            int cellY = stroke / CellsWide % CellsHigh;
            int offset = (cellY * CellHeight * Width + cellX * CellWidth) * 4;
            _frame[offset] = (byte)stroke;
            _frame[offset + 1] = (byte)(stroke >> 8);
            _frame[offset + 2] = (byte)(stroke >> 16);
            return SixelBitmapRenderer.RenderCells(_frame, CellsWide, CellsHigh, CellWidth, CellHeight);
        }

        [IterationSetup(Target = nameof(FrameAllCellsNew))]
        public void SetupAllCellsNew()
        {
            _newFrame = MakePicture(++_stroke);
        }

        [Benchmark]
        public PixelBuffer FrameAllCellsNew()
        {
            return SixelBitmapRenderer.RenderCells(_newFrame, CellsWide, CellsHigh, CellWidth, CellHeight);
        }
    }
}
