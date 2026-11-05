using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     The render target writes only the cells that differ from what it last wrote. When the terminal
    ///     may no longer show that (console I/O resumed after another program had the terminal), it forgets
    ///     what it wrote and writes everything again.
    /// </summary>
    [TestFixture]
    public sealed class RenderTargetTests : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _console = (UnitTestConsole)AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _consoleWindowImpl = new ConsoleWindowImpl();
            _renderTarget = new RenderTarget(new IPlatformRenderSurface[] { _consoleWindowImpl });
        }

        [TearDown]
        public void TearDown()
        {
            Dispose();
        }

        private UnitTestConsole _console;
        private ConsoleWindowImpl _consoleWindowImpl;
        private RenderTarget _renderTarget;

        public void Dispose()
        {
            _renderTarget?.Dispose();
            _renderTarget = null;
            _consoleWindowImpl?.Dispose();
            _consoleWindowImpl = null;
        }

        [Test]
        public void AfterTheTerminalContentsAreLostEveryCellIsWrittenAgain()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('X'), Colors.White),
                new PixelBackground(Colors.Blue));
            _consoleWindowImpl.PixelBuffer[3, 2] = pixel;
            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(3, 2, 1, 1));
            _renderTarget.RenderToDevice();
            Assert.That(_console.PixelBuffer[3, 2], Is.EqualTo(pixel));

            // another program clears the terminal while the render target still believes the cell is there
            _console.PixelBuffer[3, 2] = Pixel.Space;
            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(3, 2, 1, 1));
            _renderTarget.RenderToDevice();
            Assert.That(_console.PixelBuffer[3, 2], Is.EqualTo(Pixel.Space),
                "unchanged as far as the render target knows, so not written");

            _consoleWindowImpl.NotifyTerminalContentsLost();
            _renderTarget.RenderToDevice();
            Assert.That(_console.PixelBuffer[3, 2], Is.EqualTo(pixel));
        }

        /// <summary>
        ///     A frame is never written while a drawing context is open: from another thread (the mouse
        ///     cursor's timer) it waits until the context is disposed, and then carries the finished drawing,
        ///     not the half of it the first cell held. Written mid-draw, a resizing dialog showed the undimmed
        ///     page drawn under it, and an image encoder sent its pictures.
        /// </summary>
        [Test]
        public void AFrameFromAnotherThreadWaitsForTheDrawingUnderWay()
        {
            var halfDrawn = new Pixel(new PixelForeground(new Symbol('H'), Colors.White),
                new PixelBackground(Colors.Red));
            var finished = new Pixel(new PixelForeground(new Symbol('F'), Colors.White),
                new PixelBackground(Colors.Blue));
            // a new window is dirty all over
            _renderTarget.RenderToDevice();

            IDrawingContextImpl context = _renderTarget.CreateDrawingContext(default, out _);
            _consoleWindowImpl.PixelBuffer[3, 2] = halfDrawn;
            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(3, 2, 1, 1));

            Task frame = Task.Run(() => _renderTarget.RenderToDevice());
            Assert.That(frame.Wait(200), Is.False, "waits while the context is open");
            Assert.That(_console.PixelBuffer[3, 2], Is.Not.EqualTo(halfDrawn), "nothing half drawn written");

            _consoleWindowImpl.PixelBuffer[3, 2] = finished;
            context.Dispose();

            Assert.That(frame.Wait(5000), Is.True, "goes once the context is disposed");
            Assert.That(_console.PixelBuffer[3, 2], Is.EqualTo(finished));
        }

        /// <summary>
        ///     The screen a frame ends with is what was written, not the pixel buffer: a cell changed but not
        ///     marked dirty is not written, so an image encoder must not see it either.
        /// </summary>
        [Test]
        public void AFrameEndsWithTheScreenAsWrittenNotThePixelBuffer()
        {
            var written = new Pixel(new PixelForeground(new Symbol('X'), Colors.White),
                new PixelBackground(Colors.Blue));
            var outsideDirty = new Pixel(new PixelForeground(new Symbol('Y'), Colors.White),
                new PixelBackground(Colors.Red));
            // a new window is dirty all over: written once, so only what is marked below is dirty
            _renderTarget.RenderToDevice();

            _consoleWindowImpl.PixelBuffer[3, 2] = written;
            _consoleWindowImpl.PixelBuffer[5, 2] = outsideDirty;
            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(3, 2, 1, 1));

            _renderTarget.RenderToDevice();

            PixelBuffer screen = _console.LastFrameScreen;
            Assert.That(screen, Is.Not.SameAs(_consoleWindowImpl.PixelBuffer));
            Assert.That(screen[3, 2], Is.EqualTo(written), "the dirty cell, as written");
            Assert.That(screen[5, 2], Is.Not.EqualTo(outsideDirty), "drawn outside the dirty regions, not written");
            Assert.That(_console.PixelBuffer[5, 2], Is.Not.EqualTo(outsideDirty));
        }
    }
}