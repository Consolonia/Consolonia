using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;
using Microsoft.VSDiagnostics;

namespace BenchmarkSuite1
{
    [CPUUsageDiagnoser]
    [DotNetObjectAllocDiagnoser]
    [DotNetObjectAllocJobConfiguration]
    public class SixelBenchmarks
    {
        private const int Width = 320;
        private const int Height = 192;
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private byte[] _bitmap = null !;
        private byte[] _palette = null !;
        private Sixel _sixel = null !;

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

            _sixel = Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight);
            _palette = _sixel.Palette;
        }

        [Benchmark(Baseline = true)]
        public Sixel QuantizeWithSharedPalette()
        {
            return Sixel.CreateFromBitmap(_bitmap, Width, Height, CellWidth, CellHeight, _palette);
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
    }
}