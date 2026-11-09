using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media.Imaging;
using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     Measures the pixel image converter for one frame of a full-screen picture. The converter turns the
    ///     visible pixels into cells that show pixel images. It reuses any image it has already made.
    /// </summary>
    /// <remarks>
    ///     The picture is 1920x1072 pixels. With 8x16-pixel cells, that is 240x67 cells.
    ///     The scenarios match what a paint program does: nothing changed, one cell changed (a brush
    ///     stroke), and every cell new (a new picture, or a scroll).
    ///     The pixel array and the cell buffer are reused between frames, as the converter's CellConversion
    ///     does. So the results show the time and allocations of one frame, not of the setup.
    ///     Nothing is sent to a terminal. <see cref="ConsoleOutputBenchmarks" /> measures the console output.
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup, which releases the bitmap")]
    public class PixelImageFrameBenchmarks
    {
        internal const int CellWidth = 8;
        internal const int CellHeight = 16;
        internal const int CellsWide = 240;
        internal const int CellsHigh = 67;
        internal const int Width = CellsWide * CellWidth;
        internal const int Height = CellsHigh * CellHeight;
        private PinnedBitmap _bitmap = null!;
        private PixelBuffer _cells = null!;

        private byte[] _frame = null!;
        private byte[] _newFrame = null!;
        private int _stroke;

        [GlobalSetup]
        public void Setup()
        {
            _frame = MakePicture(0);
            _cells = new PixelBuffer(CellsWide, CellsHigh);
            _bitmap = new PinnedBitmap(MakePicture(0), Width, Height);
            PixelImageBitmapConverter.ConvertCells(_frame, _cells, CellWidth, CellHeight);
        }

        /// <summary>
        ///     Makes a photo-like BGRX picture: smooth gradients plus fine noise, so that cells differ.
        ///     The same seed always gives the same picture.
        /// </summary>
        internal static byte[] MakePicture(int seed)
        {
            byte[] bgrx = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                // Noise from a hash of the position and seed, so it is repeatable.
                int noise = (int)((uint)((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)) >> 13) & 7;
                bgrx[offset] = (byte)((x * 255 / Width + noise + seed) & 0xFF);
                bgrx[offset + 1] = (byte)((y * 255 / Height + noise) & 0xFF);
                bgrx[offset + 2] = (byte)(((x + y) * 255 / (Width + Height) + noise) & 0xFF);
                bgrx[offset + 3] = 0xFF;
            }

            return bgrx;
        }

        /// <summary>
        ///     Simulates a brush stroke. Sets the top-left pixel of one cell to a color made from
        ///     <paramref name="stroke" />. The stroke number picks the cell, so each stroke changes a different cell.
        /// </summary>
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
        ///     Converts a picture whose cells are all in the cache already, as after scrolling back to it.
        ///     The only work is hashing each cell and looking it up. (In the real renderer, a frame with no
        ///     change at all reuses the previous conversion and does not call the converter.)
        /// </summary>
        [Benchmark(Baseline = true)]
        public PixelBuffer FrameUnchanged()
        {
            PixelImageBitmapConverter.ConvertCells(_frame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        /// <summary>
        ///     Changes one cell to new pixels, like a brush stroke, and converts the frame.
        ///     Every other cell reuses its cached image.
        /// </summary>
        [Benchmark]
        public PixelBuffer FrameOneCellChanged()
        {
            Stroke(_frame, ++_stroke);
            PixelImageBitmapConverter.ConvertCells(_frame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        /// <summary>
        ///     Same brush stroke, but applied to the bitmap. The visible pixels are first copied out of the
        ///     bitmap into the pixel array, then converted. This covers all the converter's work per edit,
        ///     up to (but not including) the terminal output.
        /// </summary>
        [Benchmark]
        public PixelBuffer FrameOneCellChangedFromBitmap()
        {
            Stroke(_bitmap.Pixels, ++_stroke);
            BitmapConverter.GetVisiblePixels(_bitmap, null, new PixelSize(Width, Height),
                new PixelRect(0, 0, Width, Height), BitmapInterpolationMode.None, _frame);
            PixelImageBitmapConverter.ConvertCells(_frame, _cells, CellWidth, CellHeight);
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
            PixelImageBitmapConverter.ConvertCells(_newFrame, _cells, CellWidth, CellHeight);
            return _cells;
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _bitmap?.Dispose();
        }
    }
}