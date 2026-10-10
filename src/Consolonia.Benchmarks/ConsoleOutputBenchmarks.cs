using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media;
using BenchmarkDotNet.Attributes;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     What AnsiConsoleOutput costs to get a frame out: the escape sequences built, buffered and written
    ///     to the output stream (a null stream here, so the terminal's side is not measured).
    /// </summary>
    /// <remarks>
    ///     A full screen is 240x67 cells of 8x16 pixels (1920x1072). A full-screen kitty picture is 510
    ///     tiles of 8x4 cells, each sent as an 8 KB PNG (about 4 MB, a photo's worth).
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup")]
    public class ConsoleOutputBenchmarks
    {
        private const int CellsWide = 240;
        private const int CellsHigh = 67;
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int Width = CellsWide * CellWidth;
        private const int Height = CellsHigh * CellHeight;
        private const int Tiles = 510;
        private const int TileBytes = 8 * 1024;

        private AnsiConsoleOutput _output = null!;
        private TextWriter _originalOut = null!;
        private Pixel[] _text = null!;
        private Sixel _screenSixel = null!;
        private Sixel _cellSixel = null!;
        private byte[][] _tiles = null!;

        [GlobalSetup]
        public void Setup()
        {
            Setup(Stream.Null);
        }

        private void Setup(Stream stream)
        {
            AvaloniaLocator.CurrentMutable.Bind<IConsoleColorMode>().ToConstant(new RgbConsoleColorMode());

            // a screen of text in runs of eight cells per color, as a UI draws it
            _text = new Pixel[CellsWide * CellsHigh];
            for (int i = 0; i < _text.Length; i++)
            {
                int run = i / 8;
                var foreground = Color.FromRgb((byte)(run * 37), (byte)(run * 11), 200);
                var background = Color.FromRgb(20, (byte)(run * 5), (byte)(run * 3));
                _text[i] = new Pixel(new PixelForeground(new Symbol((char)('A' + i % 26)), foreground),
                    new PixelBackground(background));
            }

            byte[] photo = Photo(Width, Height);
            _screenSixel = Sixel.CreateFromBitmap(photo, Width, Height, CellWidth, CellHeight);
            _screenSixel.Render(); // kept: what is measured is getting it out, not encoding it
            _cellSixel = Sixel.CreateFromBitmap(Photo(CellWidth, CellHeight), CellWidth, CellHeight, CellWidth,
                CellHeight);
            _cellSixel.Render();

            _tiles = new byte[Tiles][];
            for (int t = 0; t < Tiles; t++)
            {
                _tiles[t] = new byte[TileBytes];
                for (int i = 0; i < TileBytes; i++)
                    _tiles[t][i] = (byte)(((uint)(i * 2654435761u) ^ (uint)(t * 40503)) >> 13);
            }

            _originalOut = Console.Out;
            _output = new AnsiConsoleOutput { Size = new PixelBufferSize(CellsWide, CellsHigh) };
            RedirectTo(_output, stream);
        }

        private static void RedirectTo(AnsiConsoleOutput output, Stream stream)
        {
            output.RedirectOutput(stream);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Console.Out.Flush();
            Console.SetOut(_originalOut);
        }

        private static byte[] Photo(int width, int height)
        {
            byte[] bgrx = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                int noise = (int)((uint)(x * 73856093 ^ y * 19349663) >> 13) & 7;
                bgrx[offset] = (byte)(x * 255 / width + noise);
                bgrx[offset + 1] = (byte)(y * 255 / height + noise);
                bgrx[offset + 2] = (byte)((x + y) * 255 / (width + height) + noise);
                bgrx[offset + 3] = 0xFF;
            }

            return bgrx;
        }

        [Benchmark(Baseline = true)]
        public void TextFullScreen()
        {
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                _output.WritePixel(new PixelBufferCoordinate((ushort)x, (ushort)y), in _text[y * CellsWide + x]);
            _output.Flush();
        }

        [Benchmark]
        public void SixelFullScreen()
        {
            _output.WriteSixel(new PixelBufferCoordinate(0, 0), _screenSixel);
            _output.Flush();
        }

        [Benchmark]
        public void SixelOneCell()
        {
            _output.WriteSixel(new PixelBufferCoordinate(10, 5), _cellSixel);
            _output.Flush();
        }

        [Benchmark]
        public void KittyFullScreen()
        {
            for (int t = 0; t < Tiles; t++)
                KittyGraphics.WriteTransmitSequence(_output, t + 1, 64, 64, _tiles[t], KittyImageFormat.Png);
            _output.Flush();
        }

        [Benchmark]
        public void KittyOneTile()
        {
            KittyGraphics.WriteTransmitSequence(_output, 1, 64, 64, _tiles[0], KittyImageFormat.Png);
            _output.Flush();
        }

        /// <summary>
        ///     The SHA-256 of the bytes each scenario writes, to show a change to the output path writes the
        ///     same bytes.
        /// </summary>
        public static void PrintDigests()
        {
            (string Name, Action<ConsoleOutputBenchmarks> Run)[] scenarios =
            [
                (nameof(TextFullScreen), b => b.TextFullScreen()),
                (nameof(SixelFullScreen), b => b.SixelFullScreen()),
                (nameof(SixelOneCell), b => b.SixelOneCell()),
                (nameof(KittyFullScreen), b => b.KittyFullScreen()),
                (nameof(KittyOneTile), b => b.KittyOneTile())
            ];

            TextWriter console = Console.Out;
            foreach ((string name, Action<ConsoleOutputBenchmarks> run) in scenarios)
            {
                using var captured = new MemoryStream();
                var benchmarks = new ConsoleOutputBenchmarks();
                benchmarks.Setup(captured);
                run(benchmarks);
                benchmarks.Cleanup();
                console.WriteLine(
                    $"{name,-16} {captured.Length,10} bytes  {Convert.ToHexString(SHA256.HashData(captured.ToArray()))}");
            }
        }
    }
}
