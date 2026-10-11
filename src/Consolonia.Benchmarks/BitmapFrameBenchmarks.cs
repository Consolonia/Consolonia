using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using BenchmarkDotNet.Attributes;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Fonts;
using Consolonia.NUnit;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     One whole frame of a full-screen picture: drawn into the pixel buffer, then rendered to the
    ///     console, for the sixel and the kitty renderer.
    /// </summary>
    /// <remarks>
    ///     The console is the unit test console, which keeps no output, so what is measured is the work
    ///     Consolonia does per frame, not the terminal's. The scenarios: the picture drawn again unchanged
    ///     (as when anything over it repaints), and a brush stroke changing one pixel of a writeable
    ///     picture in place.
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup")]
    public class BitmapFrameBenchmarks
    {
        private const int Width = SixelFrameBenchmarks.Width;
        private const int Height = SixelFrameBenchmarks.Height;

        private System.IDisposable _scope = null!;
        private ConsoloniaLifetime _lifetime = null!;
        private ConsoleWindowImpl _window = null!;
        private RenderTarget _renderTarget = null!;
        private DrawingContextImpl _context = null!;
        private PinnedBitmap _bitmap = null!;
        private Rect _destRect;
        private int _stroke;

        [Params(ConsoleCapabilities.SupportsSixel, ConsoleCapabilities.SupportsKittyGraphics)]
        public ConsoleCapabilities Protocol { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var console = new UnitTestConsole(new PixelBufferSize(SixelFrameBenchmarks.CellsWide,
                SixelFrameBenchmarks.CellsHigh));
            _scope = AvaloniaLocator.EnterScope();
            _lifetime = ApplicationStartup.CreateLifetime(AppBuilder.Configure<BenchmarkApp>()
                .UseConsole(console)
                .UseConsolonia()
                .UseConsoleColorMode(new RgbConsoleColorMode())
                .WithConsoleFonts()
                .With<IPlatformSettings>(new ConsoloniaPlatformSettings
                {
                    UnsafeInput = false,
                    UnsafeRendering = false
                }), []);
            console.SetupLifetime(_lifetime);
            console.Capabilities = Protocol;

            _window = new ConsoleWindowImpl();
            _renderTarget = new RenderTarget(new IPlatformRenderSurface[] { _window });
            _context = new DrawingContextImpl(_window, null);
            _bitmap = new PinnedBitmap(SixelFrameBenchmarks.MakePicture(0), Width, Height);
            _destRect = new Rect(0, 0, SixelFrameBenchmarks.CellsWide, SixelFrameBenchmarks.CellsHigh);

            Frame();
        }

        private void Frame()
        {
            _context.DrawBitmap(_bitmap, 1, new Rect(_destRect.Size), _destRect);
            _renderTarget.RenderToDevice();
        }

        /// <summary>The picture drawn again as it was: nothing reaches the terminal.</summary>
        [Benchmark(Baseline = true)]
        public void RedrawUnchanged()
        {
            Frame();
        }

        /// <summary>One pixel painted, as under a brush: one cell (sixel) or one tile (kitty) is new.</summary>
        [Benchmark]
        public void BrushStroke()
        {
            SixelFrameBenchmarks.Stroke(_bitmap.Pixels, ++_stroke);
            _bitmap.Version++;
            Frame();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _renderTarget?.Dispose();
            _window?.Dispose();
            _bitmap?.Dispose();
            _lifetime?.Dispose();
            _scope?.Dispose();
        }

        private sealed class BenchmarkApp : Application;
    }
}
