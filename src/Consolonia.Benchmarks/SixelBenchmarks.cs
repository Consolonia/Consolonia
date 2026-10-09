using System;
using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     The sixel encoder's building blocks on one small image: quantizing and serializing.
    /// </summary>
    [MemoryDiagnoser]
    public class SixelBenchmarks
    {
        private const int Width = 320;
        private const int Height = 192;
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private byte[] _bitmap = null!;
        private byte[] _cellBitmap = null!;
        private byte[] _palette = null!;
        private Sixel _sixel = null!;
        private Sixel _cellSixel = null!;

        [GlobalSetup]
        public void Setup()
        {
            _bitmap = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                _bitmap[offset] = (byte)((x * 13 + y * 7) & 0xFF);
                _bitmap[offset + 1] = (byte)((x * 3 + y * 11) & 0xFF);
                _bitmap[offset + 2] = (byte)((x * 17 + y * 5) & 0xFF);
                _bitmap[offset + 3] = 0xFF;
            }

            _cellBitmap = new byte[CellWidth * CellHeight * 4];
            for (int row = 0; row < CellHeight; row++)
                Array.Copy(_bitmap, row * Width * 4, _cellBitmap, row * CellWidth * 4, CellWidth * 4);

            _sixel = Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight);
            _palette = _sixel.Palette;
            _cellSixel = Sixel.CreateFromBitmap(_cellBitmap, CellWidth, CellHeight, CellWidth, CellHeight,
                _palette);
        }

        /// <summary>
        ///     Maps the image onto a palette it has seen before, so the palette's lookup table is
        ///     already built. Only the per-pixel mapping is measured.
        /// </summary>
        [Benchmark(Baseline = true)]
        public Sixel QuantizeWithSharedPalette()
        {
            return Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight, _palette);
        }

        /// <summary>
        ///     Maps one cell onto a palette it has never seen, as the renderer does for the first new
        ///     cell after every change to a picture.
        /// </summary>
        [Benchmark]
        public Sixel QuantizeCellWithFreshPalette()
        {
            return Sixel.CreateFromBitmap(_cellBitmap, CellWidth, CellHeight, CellWidth, CellHeight,
                (byte[])_palette.Clone());
        }

        [Benchmark]
        public Sixel QuantizeFull()
        {
            return Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight);
        }

        // Render() caches its bytes on the instance, so without a fresh Sixel per invocation this
        // measures the cache check. IterationSetup pins InvocationCount to 1, which is tolerable for
        // a render of this size.
        [IterationSetup(Target = nameof(SerializeToBytes))]
        public void SetupSerializeToBytes()
        {
            _sixel = Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight, _palette);
        }

        [Benchmark]
        public int SerializeToBytes()
        {
            return _sixel.Render().Length;
        }

        /// <summary>
        ///     Serializes one cell, as the renderer sends a cell on its own. A new instance shares the
        ///     arrays, so the encoding is measured rather than a cached result.
        /// </summary>
        [Benchmark]
        public int SerializeOneCell()
        {
            var cell = new Sixel(_cellSixel.Palette, _cellSixel.PaletteCount, _cellSixel.Pixels,
                CellWidth, CellHeight, CellWidth, CellHeight);
            return cell.Render().Length;
        }
    }
}
