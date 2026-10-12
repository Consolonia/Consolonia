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
        private const int Width = CellImageFrameBenchmarks.Width;
        private const int Height = CellImageFrameBenchmarks.Height;

        private System.IDisposable _scope = null!;
        private ConsoloniaLifetime _lifetime = null!;
        private ConsoleWindowImpl _window = null!;
        private RenderTarget _renderTarget = null!;
        private DrawingContextImpl _context = null!;
        private PinnedBitmap _bitmap = null!;
        private PinnedBitmap _newPicture;
        private Rect _destRect;
        private int _stroke;

        // ReSharper disable once UnusedAutoPropertyAccessor.Global BenchmarkDotNet sets it per run
        [Params(ConsoleCapabilities.SupportsSixel, ConsoleCapabilities.SupportsKittyGraphics)]
        public ConsoleCapabilities Protocol { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var console = new UnitTestConsole(new PixelBufferSize(CellImageFrameBenchmarks.CellsWide,
                CellImageFrameBenchmarks.CellsHigh));
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
            _bitmap = new PinnedBitmap(CellImageFrameBenchmarks.MakePicture(0), Width, Height);
            _destRect = new Rect(0, 0, CellImageFrameBenchmarks.CellsWide, CellImageFrameBenchmarks.CellsHigh);

            Frame();
        }

        private void Frame(PinnedBitmap picture)
        {
            _context.DrawBitmap(picture, 1, new Rect(_destRect.Size), _destRect);
            _renderTarget.RenderToDevice();
        }

        private void Frame()
        {
            Frame(_bitmap);
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
            CellImageFrameBenchmarks.Stroke(_bitmap.Pixels, ++_stroke);
            _bitmap.Version++;
            Frame();
        }

        [IterationSetup(Target = nameof(NewPicture))]
        public void SetupNewPicture()
        {
            _newPicture?.Dispose();
            _newPicture = new PinnedBitmap(CellImageFrameBenchmarks.MakePicture(++_stroke), Width, Height);
        }

        /// <summary>A picture never shown before over the whole screen: every cell is new and written.</summary>
        [Benchmark]
        public void NewPicture()
        {
            Frame(_newPicture);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _renderTarget?.Dispose();
            _window?.Dispose();
            _bitmap?.Dispose();
            _newPicture?.Dispose();
            _lifetime?.Dispose();
            _scope?.Dispose();
        }

        private sealed class BenchmarkApp : Application;
    }
}
