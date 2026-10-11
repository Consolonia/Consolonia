using System;
using Avalonia;
using Avalonia.Media;
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
        private UnitTestConsole _console;
        private ConsoleWindowImpl _consoleWindowImpl;
        private RenderTarget _renderTarget;

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
    }
}
