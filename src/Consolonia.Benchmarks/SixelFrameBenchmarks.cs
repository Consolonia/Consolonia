using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media.Imaging;
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
    ///     picture, or a scroll). The pixel copy and the cell buffer are reused from frame to frame, as
    ///     the renderer's CellRendering reuses them, so what is measured is the work and the garbage of
    ///     one frame, not of setting up for it.
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup, which releases the bitmap")]
    public class SixelFrameBenchmarks
    {
        internal const int CellWidth = 8;
        internal const int CellHeight = 16;
        internal const int CellsWide = 240;
        internal const int CellsHigh = 67;
        internal const int Width = CellsWide * CellWidth;
        internal const int Height = CellsHigh * CellHeight;

        private byte[] _frame = null!;
        private byte[] _newFrame = null!;
        private PixelBuffer _cells = null!;
        private PinnedBitmap _bitmap = null!;
        private int _stroke;

        [GlobalSetup]
        public void Setup()
        {
            _frame = MakePicture(0);
            _cells = new PixelBuffer(CellsWide, CellsHigh);
            _bitmap = new PinnedBitmap(MakePicture(0), Width, Height);
            SixelBitmapRenderer.RenderCells(_frame, _cells, CellWidth, CellHeight);
        }

        /// <summary>A photo-like picture: smooth gradients with fine noise, so cells differ.</summary>
        internal static byte[] MakePicture(int seed)
        {
            byte[] bgrx = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                // a hash of the position: fine noise that is the same picture for the same seed
                int noise = (int)((uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791) >> 13) & 7;
                bgrx[offset] = (byte)((x * 255 / Width + noise + seed) & 0xFF);
                bgrx[offset + 1] = (byte)((y * 255 / Height + noise) & 0xFF);
                bgrx[offset + 2] = (byte)(((x + y) * 255 / (Width + Height) + noise) & 0xFF);
                bgrx[offset + 3] = 0xFF;
            }

            return bgrx;
        }

        /// <summary>Paints a color no cell has had into one pixel of the cell the stroke lands on.</summary>
        internal static void Stroke(byte[] bgrx, int stroke)
        {
            int cellX = stroke % CellsWide;
            int cellY = stroke / CellsWide % CellsHigh;
            int offset = (cellY * CellHeight * Width + cellX * CellWidth) * 4;
            bgrx[offset] = (byte)stroke;
            bgrx[offset + 1] = (byte)(stroke >> 8);
            bgrx[offset + 2] = (byte)(stroke >> 16);
        }

        /// <summary>
        ///     Every cell already cached, as after scrolling back to a picture: a rendering refilled by
        ///     hashing alone. (A frame where nothing changed at all reuses its rendering and never gets here.)
        /// </summary>
        [Benchmark(Baseline = true)]
        public PixelBuffer FrameUnchanged()
        {
            SixelBitmapRenderer.RenderCells(_frame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        /// <summary>
        ///     One cell gets pixels it has never had, as under a brush: every other cell is reused.
        /// </summary>
        [Benchmark]
        public PixelBuffer FrameOneCellChanged()
        {
            Stroke(_frame, ++_stroke);
            SixelBitmapRenderer.RenderCells(_frame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        /// <summary>
        ///     The brush stroke from the bitmap: the visible pixels copied out of it into the rendering's
        ///     pixel buffer, then rendered. Everything the renderer does per edit short of the terminal.
        /// </summary>
        [Benchmark]
        public PixelBuffer FrameOneCellChangedFromBitmap()
        {
            Stroke(_bitmap.Pixels, ++_stroke);
            BitmapRenderer.GetVisiblePixels(_bitmap, null, new PixelSize(Width, Height),
                new PixelRect(0, 0, Width, Height), BitmapInterpolationMode.None, _frame);
            SixelBitmapRenderer.RenderCells(_frame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        [IterationSetup(Target = nameof(FrameAllCellsNew))]
        public void SetupAllCellsNew()
        {
            _newFrame = MakePicture(++_stroke);
        }

        [Benchmark]
        public PixelBuffer FrameAllCellsNew()
        {
            SixelBitmapRenderer.RenderCells(_newFrame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _bitmap?.Dispose();
        }
    }
}
