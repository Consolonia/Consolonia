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
    ///     Tests what the render target writes to the console, and when. The render target writes only the
    ///     cells that differ from what it last wrote. If the terminal contents may have been lost (for example,
    ///     another program used the terminal), it forgets what it wrote and writes every cell again.
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

            // Simulate another program clearing the terminal. The render target still thinks the cell is shown.
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
        ///     A frame must not be written while a drawing context is open. Here a frame is requested from
        ///     another thread (as the mouse cursor's timer does) while drawing is in progress. The frame waits
        ///     until the context is disposed, and then writes the finished drawing, not the half-drawn cell.
        ///     Before the fix, frames written in the middle of drawing caused bugs. For example, a resizing
        ///     dialog showed the page under it without the dim overlay, and an image encoder sent half-drawn
        ///     pictures.
        /// </summary>
        [Test]
        public void AFrameFromAnotherThreadWaitsForTheDrawingUnderWay()
        {
            var halfDrawn = new Pixel(new PixelForeground(new Symbol('H'), Colors.White),
                new PixelBackground(Colors.Red));
            var finished = new Pixel(new PixelForeground(new Symbol('F'), Colors.White),
                new PixelBackground(Colors.Blue));
            // A new window is dirty everywhere. Render it once to clear that.
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
        ///     After the terminal changes size, no frame is written until the pixel buffer is resized to match.
        ///     A frame at the old size would wrap in the resized terminal and put pictures in the wrong cells.
        ///     Cells that were dirty stay dirty and are written by the first frame after the resize.
        /// </summary>
        [Test]
        public void NoFrameIsWrittenWhileTheConsoleSizeDiffersFromTheBuffer()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('R'), Colors.White),
                new PixelBackground(Colors.Blue));
            // A new window is dirty everywhere. Render it once to clear that.
            _renderTarget.RenderToDevice();
            PixelBufferSize size = _console.Size;

            _consoleWindowImpl.PixelBuffer[3, 2] = pixel;
            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(3, 2, 1, 1));
            try
            {
                _console.Size = new PixelBufferSize((ushort)(size.Width - 1), size.Height);
                _renderTarget.RenderToDevice();
                Assert.That(_console.PixelBuffer[3, 2], Is.Not.EqualTo(pixel), "not written at the old size");
            }
            finally
            {
                _console.Size = size;
            }

            _renderTarget.RenderToDevice();
            Assert.That(_console.PixelBuffer[3, 2], Is.EqualTo(pixel), "written by the next frame");
        }

        /// <summary>
        ///     At the end of a frame, the screen passed to the output must be what was actually written, not the
        ///     pixel buffer. A cell that changed but was not marked dirty is not written, so an image encoder
        ///     must not see it either.
        /// </summary>
        [Test]
        public void AFrameEndsWithTheScreenAsWrittenNotThePixelBuffer()
        {
            var written = new Pixel(new PixelForeground(new Symbol('X'), Colors.White),
                new PixelBackground(Colors.Blue));
            var outsideDirty = new Pixel(new PixelForeground(new Symbol('Y'), Colors.White),
                new PixelBackground(Colors.Red));
            // A new window is dirty everywhere. Render it once so that only the cells marked below are dirty.
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