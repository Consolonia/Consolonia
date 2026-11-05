using System;
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
    ///     One whole frame of a full-screen picture: drawn into the pixel buffer as pixel images, then
    ///     rendered to the console.
    /// </summary>
    /// <remarks>
    ///     The console is the unit test console, which keeps no output and has no image encoder, so what
    ///     is measured is the work Consolonia does per frame before any encoding, the same whichever
    ///     protocol an encoder speaks (<see cref="ConsoleOutputBenchmarks" /> measures the output).
    ///     The scenarios: the picture drawn again unchanged (as when anything over it repaints), and a
    ///     brush stroke changing one pixel of a writeable picture in place.
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup")]
    public class BitmapFrameBenchmarks
    {
        private const int Width = PixelImageFrameBenchmarks.Width;
        private const int Height = PixelImageFrameBenchmarks.Height;
        private PinnedBitmap _bitmap = null!;
        private DrawingContextImpl _context = null!;
        private Rect _destRect;
        private ConsoloniaLifetime _lifetime = null!;
        private PinnedBitmap _newPicture;
        private RenderTarget _renderTarget = null!;

        private IDisposable _scope = null!;
        private int _stroke;
        private ConsoleWindowImpl _window = null!;

        [GlobalSetup]
        public void Setup()
        {
            var console = new UnitTestConsole(new PixelBufferSize(PixelImageFrameBenchmarks.CellsWide,
                PixelImageFrameBenchmarks.CellsHigh));
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
            // a console with an image encoder: the renderer makes pixel images whatever its protocol
            console.Capabilities |= ConsoleCapabilities.SupportsPixelImages;

            _window = new ConsoleWindowImpl();
            _renderTarget = new RenderTarget(new IPlatformRenderSurface[] { _window });
            _context = new DrawingContextImpl(_window, null);
            _bitmap = new PinnedBitmap(PixelImageFrameBenchmarks.MakePicture(0), Width, Height);
            _destRect = new Rect(0, 0, PixelImageFrameBenchmarks.CellsWide, PixelImageFrameBenchmarks.CellsHigh);

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

        /// <summary>One pixel painted, as under a brush: one pixel image is new.</summary>
        [Benchmark]
        public void BrushStroke()
        {
            PixelImageFrameBenchmarks.Stroke(_bitmap.Pixels, ++_stroke);
            _bitmap.Version++;
            Frame();
        }

        [IterationSetup(Target = nameof(NewPicture))]
        public void SetupNewPicture()
        {
            _newPicture?.Dispose();
            _newPicture = new PinnedBitmap(PixelImageFrameBenchmarks.MakePicture(++_stroke), Width, Height);
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