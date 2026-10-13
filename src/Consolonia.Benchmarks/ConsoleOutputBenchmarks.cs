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
    ///     to the output stream (a null stream here, so the terminal's side is not measured). The image
    ///     scenarios go through the output as the render target drives it: the changed image cells written,
    ///     then the frame ended, which is where the sixel runs are encoded and the kitty blocks transmitted
    ///     and placed.
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
        private static readonly Color Dim = Color.FromArgb(0x7F, 0, 0, 0);
        private AnsiConsoleOutput _kittyOutput = null!;
        private PixelBuffer _newPicture = null!;

        private TextWriter _originalOut = null!;
        private PixelBuffer _picture = null!;
        private int _seed;
        private AnsiConsoleOutput _sixelOutput = null!;
        private Pixel _strokeCell;
        private PixelBuffer _strokeScreen = null!;
        private Pixel[] _text = null!;

        private static (string Name, Action<ConsoleOutputBenchmarks> Run)[] Scenarios =>
        [
            (nameof(TextFullScreen), b => b.TextFullScreen()),
            (nameof(SixelNewPicture), b => b.SixelNewPicture()),
            (nameof(SixelDialogClosed), b => b.SixelDialogClosed()),
            (nameof(SixelOneCell), b => b.SixelOneCell()),
            (nameof(KittyNewPicture), b => b.KittyNewPicture()),
            (nameof(KittyAfterReset), b => b.KittyAfterReset()),
            (nameof(KittyOneCell), b => b.KittyOneCell())
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
            _text = new Pixel[CellsWide * CellsHigh];
            for (int i = 0; i < _text.Length; i++)
            {
                int run = i / 8;
                Color foreground = Color.FromRgb((byte)(run * 37), (byte)(run * 11), 200);
                Color background = Color.FromRgb(20, (byte)(run * 5), (byte)(run * 3));
                _text[i] = new Pixel(new PixelForeground(new Symbol((char)('A' + i % 26)), foreground),
                    new PixelBackground(background));
            }

            // a screen of picture as the renderer makes it
            _picture = Picture(Width, Height, 0);

            // one cell of a picture never seen, as under a brush
            PixelBuffer strokeCells = Picture(CellWidth, CellHeight, 1);
            _strokeCell = strokeCells[0, 0];
            // the screen as it is after the stroke: the picture, with that one cell changed
            _strokeScreen = new PixelBuffer(CellsWide, CellsHigh);
            for (ushort y = 0; y < CellsHigh; y++)
            for (ushort x = 0; x < CellsWide; x++)
                _strokeScreen[x, y] = _picture[x, y];
            _strokeScreen[10, 5] = _strokeCell;

            _originalOut = Console.Out;
            _sixelOutput = new AnsiConsoleOutput { Size = new PixelBufferSize(CellsWide, CellsHigh) };
            _sixelOutput.RedirectOutput(stream, ConsoleCapabilities.SupportsSixel);
            _kittyOutput = new AnsiConsoleOutput { Size = new PixelBufferSize(CellsWide, CellsHigh) };
            _kittyOutput.RedirectOutput(stream, ConsoleCapabilities.SupportsKittyGraphics);

            // the picture shown, dimmed and shown again, so the per-frame scenarios start from a terminal that
            // has it and the sixel run cache holds both
            foreach (AnsiConsoleOutput output in new[] { _sixelOutput, _kittyOutput })
            {
                WholeScreen(output, _picture);
                WholeScreen(output, Dimmed(_picture));
                WholeScreen(output, _picture);
            }
        }

        /// <summary>A picture no cell of which has been seen before, for the scenarios that send one.</summary>
        [IterationSetup(Targets = [nameof(SixelNewPicture), nameof(KittyNewPicture)])]
        public void SetupNewPicture()
        {
            _newPicture = Picture(Width, Height, ++_seed + 2);
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
            CellImageBitmapRenderer.RenderCells(Photo(width, height, seed), cells, CellWidth, CellHeight);
            return cells;
        }

        private static PixelBuffer Dimmed(PixelBuffer picture)
        {
            var dimmed = new PixelBuffer(picture.Width, picture.Height);
            for (ushort y = 0; y < picture.Height; y++)
            for (ushort x = 0; x < picture.Width; x++)
                dimmed[x, y] = picture[x, y].Blend(new Pixel(new PixelBackground(Dim)));
            return dimmed;
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
            output.BeginFrame();
            for (ushort y = 0; y < CellsHigh; y++)
            for (ushort x = 0; x < CellsWide; x++)
                output.WritePixel(new PixelBufferCoordinate(x, y), screen[x, y]);
            output.EndFrame(screen);
            output.Flush();
        }

        /// <summary>A frame in which one cell changed to pixels never seen before, as under a brush.</summary>
        private void OneCell(AnsiConsoleOutput output)
        {
            _strokeCell = Picture(CellWidth, CellHeight, ++_seed + 100000)[0, 0];
            _strokeScreen[10, 5] = _strokeCell;
            output.BeginFrame();
            output.WritePixel(new PixelBufferCoordinate(10, 5), in _strokeCell);
            output.EndFrame(_strokeScreen);
            output.Flush();
        }

        [Benchmark(Baseline = true)]
        public void TextFullScreen()
        {
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                _sixelOutput.WritePixel(new PixelBufferCoordinate((ushort)x, (ushort)y), in _text[y * CellsWide + x]);
            _sixelOutput.Flush();
        }

        /// <summary>A full-screen picture never sent: a run per row, quantized and encoded.</summary>
        [Benchmark]
        public void SixelNewPicture()
        {
            WholeScreen(_sixelOutput, _newPicture);
        }

        /// <summary>The picture shown again after a modal dimmed it: every run comes from the cache.</summary>
        [Benchmark]
        public void SixelDialogClosed()
        {
            WholeScreen(_sixelOutput, _picture);
        }

        [Benchmark]
        public void SixelOneCell()
        {
            OneCell(_sixelOutput);
        }

        /// <summary>A full-screen picture never sent: every block transmitted and placed.</summary>
        [Benchmark]
        public void KittyNewPicture()
        {
            WholeScreen(_kittyOutput, _newPicture);
        }

        /// <summary>Every block sent again under its id, as after a resize or a resume.</summary>
        [Benchmark]
        public void KittyAfterReset()
        {
            _kittyOutput.ResetCache();
            WholeScreen(_kittyOutput, _picture);
        }

        [Benchmark]
        public void KittyOneCell()
        {
            OneCell(_kittyOutput);
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
                benchmarks.SetupNewPicture();
                captured.SetLength(0);
                run(benchmarks);
                benchmarks.Cleanup();
                console.WriteLine(
                    $"{name,-18} {captured.Length,10} bytes  {Convert.ToHexString(SHA256.HashData(captured.ToArray()))}");
            }
        }

        /// <summary>
        ///     <c>--alloc</c>: the bytes each scenario allocates in one run, after a warm-up run, from the
        ///     thread's allocation counter. Exact and quick where the memory diagnoser's figure for a scenario
        ///     with a per-iteration setup folds the setup in.
        /// </summary>
        public static void PrintAllocations()
        {
            TextWriter console = Console.Out;
            foreach ((string name, Action<ConsoleOutputBenchmarks> run) in Scenarios)
            {
                var benchmarks = new ConsoleOutputBenchmarks();
                benchmarks.Setup(Stream.Null);
                benchmarks.SetupNewPicture();
                run(benchmarks);
                benchmarks.SetupNewPicture();
                long before = GC.GetAllocatedBytesForCurrentThread();
                run(benchmarks);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                benchmarks.Cleanup();
                console.WriteLine($"{name,-18} {allocated,12:N0} bytes allocated");
            }
        }
    }
}