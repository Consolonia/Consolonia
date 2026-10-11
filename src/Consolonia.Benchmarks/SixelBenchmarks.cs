using System;
using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     The sixel encoder's building blocks: quantizing (exactly, for a cell of few colors; with Wu's
    ///     algorithm for one of many) and serializing.
    /// </summary>
    [MemoryDiagnoser]
    public class SixelBenchmarks
    {
        private const int Width = 320;
        private const int Height = 192;
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private byte[] _bitmap = null!;
        private byte[] _photoCell = null!;
        private byte[] _flatCell = null!;
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

            _photoCell = new byte[CellWidth * CellHeight * 4];
            for (int row = 0; row < CellHeight; row++)
                Array.Copy(_bitmap, row * Width * 4, _photoCell, row * CellWidth * 4, CellWidth * 4);

            // a brush stroke's cell: canvas white with a few dark pixels
            _flatCell = new byte[CellWidth * CellHeight * 4];
            Array.Fill(_flatCell, (byte)0xFF);
            for (int i = 0; i < 5 * 4; i += 4)
                _flatCell[i] = _flatCell[i + 1] = _flatCell[i + 2] = 0x20;

            _sixel = Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight);
            _cellSixel = Sixel.CreateFromBitmap(_photoCell, CellWidth, CellHeight, CellWidth, CellHeight);
        }

        /// <summary>One cell of few colors, indexed exactly: what a brush stroke costs per cell.</summary>
        [Benchmark(Baseline = true)]
        public int QuantizeFlatCell()
        {
            Sixel.Quantize(_flatCell, out _, out int paletteCount, out _);
            return paletteCount;
        }

        /// <summary>One cell of more than 256 colors: Wu's fixed cost, however small the image.</summary>
        [Benchmark]
        public int QuantizePhotoCell()
        {
            Sixel.Quantize(_photoCell, out _, out int paletteCount, out _);
            return paletteCount;
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
            _sixel = new Sixel(_sixel.Palette, _sixel.PaletteCount, _sixel.Pixels, Width, Height, CellWidth,
                CellHeight);
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
