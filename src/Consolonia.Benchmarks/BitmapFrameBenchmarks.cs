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
    ///     Measures one full frame of a full-screen picture. The picture is drawn into the pixel buffer as
    ///     pixel images, and then the render target renders the frame to the console.
    /// </summary>
    /// <remarks>
    ///     The console is the unit test console. It keeps no output and has no image encoder. So this
    ///     measures only the work Consolonia does per frame before encoding. That work is the same for
    ///     every image protocol. <see cref="ConsoleOutputBenchmarks" /> measures the output side.
    ///     Scenarios: the same picture drawn again with no changes (as when something on top of it
    ///     repaints), a brush stroke that changes one pixel of the picture in place, and a picture
    ///     that has never been shown.
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
            // Pretend the console supports pixel images, so the converter produces pixel images.
            // The converter does not depend on which image protocol the console uses.
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

        /// <summary>Draws the same picture again with no changes. Nothing is sent to the terminal.</summary>
        [Benchmark(Baseline = true)]
        public void RedrawUnchanged()
        {
            Frame();
        }

        /// <summary>Changes one pixel, like a brush stroke, and draws the frame. Only one cell gets a new image.</summary>
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

        /// <summary>Draws a full-screen picture that was never shown before. Every cell is new.</summary>
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