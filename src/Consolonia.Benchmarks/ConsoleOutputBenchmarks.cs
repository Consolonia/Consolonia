using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media;
using BenchmarkDotNet.Attributes;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     What AnsiConsoleOutput costs to get a frame out: the escape sequences built, buffered and written
    ///     to the output stream (a null stream here, so the terminal's side is not measured), as the render
    ///     target drives it: the changed cells written between the frame's begin and end. With no image
    ///     encoder registered, a picture goes out as block glyphs.
    /// </summary>
    /// <remarks>
    ///     A full screen is 240x67 cells of 8x16 pixels (1920x1072), a photo's worth of distinct cells.
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

        private TextWriter _originalOut = null!;
        private AnsiConsoleOutput _output = null!;
        private PixelBuffer _picture = null!;
        private PixelBuffer _text = null!;

        private static (string Name, Action<ConsoleOutputBenchmarks> Run)[] Scenarios =>
        [
            (nameof(TextFullScreen), b => b.TextFullScreen()),
            (nameof(BlockGlyphPicture), b => b.BlockGlyphPicture())
        ];

        [GlobalSetup]
        public void Setup()
        {
            Setup(Stream.Null);
        }

        private void Setup(Stream stream)
        {
            AvaloniaLocator.CurrentMutable.Bind<IConsoleColorMode>().ToConstant(new RgbConsoleColorMode());

            // a screen of text in runs of eight cells per color, as a UI draws it
            _text = new PixelBuffer(CellsWide, CellsHigh);
            for (int i = 0; i < CellsWide * CellsHigh; i++)
            {
                int run = i / 8;
                Color foreground = Color.FromRgb((byte)(run * 37), (byte)(run * 11), 200);
                Color background = Color.FromRgb(20, (byte)(run * 5), (byte)(run * 3));
                _text[(ushort)(i % CellsWide), (ushort)(i / CellsWide)] = new Pixel(
                    new PixelForeground(new Symbol((char)('A' + i % 26)), foreground),
                    new PixelBackground(background));
            }

            // a screen of picture as the renderer makes it
            _picture = Picture(Width, Height, 0);

            _originalOut = Console.Out;
            _output = new AnsiConsoleOutput { Size = new PixelBufferSize(CellsWide, CellsHigh) };
            _output.RedirectOutput(stream);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Console.Out.Flush();
            Console.SetOut(_originalOut);
        }

        /// <summary>A photo-like picture, as the renderer cuts it into cells.</summary>
        private static PixelBuffer Picture(int width, int height, int seed)
        {
            var cells = new PixelBuffer((ushort)(width / CellWidth), (ushort)(height / CellHeight));
            PixelImageBitmapRenderer.RenderCells(Photo(width, height, seed), cells, CellWidth, CellHeight);
            return cells;
        }

        private static byte[] Photo(int width, int height, int seed)
        {
            byte[] bgrx = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                int noise = (int)((uint)((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)) >> 13) & 7;
                bgrx[offset] = (byte)((x * 255 / width + noise + seed) & 0xFF);
                bgrx[offset + 1] = (byte)(y * 255 / height + noise);
                bgrx[offset + 2] = (byte)((x + y) * 255 / (width + height) + noise);
                bgrx[offset + 3] = 0xFF;
            }

            return bgrx;
        }

        /// <summary>A frame in which every cell of <paramref name="screen" /> changed.</summary>
        private static void WholeScreen(AnsiConsoleOutput output, PixelBuffer screen)
        {
            output.BeginUpdate();
            for (ushort y = 0; y < CellsHigh; y++)
            for (ushort x = 0; x < CellsWide; x++)
                output.WritePixel(new PixelBufferCoordinate(x, y), screen[x, y]);
            output.EndUpdate(screen);
        }

        [Benchmark(Baseline = true)]
        public void TextFullScreen()
        {
            WholeScreen(_output, _text);
        }

        /// <summary>A full-screen picture with no image encoder: a block glyph per cell.</summary>
        [Benchmark]
        public void BlockGlyphPicture()
        {
            WholeScreen(_output, _picture);
        }

        /// <summary>
        ///     The SHA-256 of the bytes each scenario writes, to show a change to the output path writes the
        ///     same bytes.
        /// </summary>
        public static void PrintDigests()
        {
            TextWriter console = Console.Out;
            foreach ((string name, Action<ConsoleOutputBenchmarks> run) in Scenarios)
            {
                using var captured = new MemoryStream();
                var benchmarks = new ConsoleOutputBenchmarks();
                benchmarks.Setup(captured);
                captured.SetLength(0);
                run(benchmarks);
                benchmarks.Cleanup();
                console.WriteLine(
                    $"{name,-18} {captured.Length,10} bytes  {Convert.ToHexString(SHA256.HashData(captured.ToArray()))}");
            }
        }

        /// <summary>
        ///     <c>--alloc</c>: the bytes each scenario allocates in one run, after a warm-up run, from the
        ///     thread's allocation counter.
        /// </summary>
        public static void PrintAllocations()
        {
            TextWriter console = Console.Out;
            foreach ((string name, Action<ConsoleOutputBenchmarks> run) in Scenarios)
            {
                var benchmarks = new ConsoleOutputBenchmarks();
                benchmarks.Setup(Stream.Null);
                run(benchmarks);
                long before = GC.GetAllocatedBytesForCurrentThread();
                run(benchmarks);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                benchmarks.Cleanup();
                console.WriteLine($"{name,-18} {allocated,12:N0} bytes allocated");
            }
        }
    }
}
