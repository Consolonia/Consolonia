using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public sealed class SixelBitmapRendererTests : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _console = (UnitTestConsole)AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _originalCapabilities = _console.Capabilities;

            // only sixel: the kitty renderer wins the capability check when its flag is present
            _console.Capabilities = ConsoleCapabilities.SupportsSixel;

            _consoleWindowImpl = new ConsoleWindowImpl();
            _buffer = _consoleWindowImpl.PixelBuffer;
            _dc = new DrawingContextImpl(_consoleWindowImpl, null);
        }

        [TearDown]
        public void TearDown()
        {
            _console.Capabilities = _originalCapabilities;
            _consoleWindowImpl?.Dispose();
            _consoleWindowImpl = null;
        }

        private UnitTestConsole _console;
        private ConsoleCapabilities _originalCapabilities;
        private ConsoleWindowImpl _consoleWindowImpl;
        private PixelBuffer _buffer;
        private DrawingContextImpl _dc;

        /// <summary>
        ///     Regression test: a bitmap already at the target size skips the resize, and the renderer used to
        ///     read from the null resize result and throw. Four cells across at 8x16 per cell is exactly 32x32.
        /// </summary>
        [Test]
        public void DrawsBitmapAlreadyAtTargetSizeWithoutResizing()
        {
            const int cellsWide = 4;
            const int cellsHigh = 2;
            var exactSize = new PixelSize(cellsWide * _console.CellPixelWidth,
                cellsHigh * _console.CellPixelHeight);

            using var bitmap = new FakeReadableBitmap(exactSize);
            var destRect = new Rect(0, 0, cellsWide, cellsHigh);

            Assert.DoesNotThrow(() => _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect));

            // the sixel path ran rather than bailing out early
            for (ushort y = 0; y < cellsHigh; y++)
            for (ushort x = 0; x < cellsWide; x++)
                Assert.IsNotNull(_buffer[x, y].Foreground.Symbol.Sixel,
                    $"cell {x},{y} should hold a sixel");
        }

        public void Dispose()
        {
            _consoleWindowImpl?.Dispose();
        }

        /// <summary>
        ///     Minimal readable bitmap over a pinned BGRA buffer. The real render interface cannot create one
        ///     here because Consolonia.Core.Tests has no Skia fallback.
        /// </summary>
        private sealed class FakeReadableBitmap : IReadableBitmapImpl
        {
            private readonly byte[] _pixels;

            public FakeReadableBitmap(PixelSize size)
            {
                PixelSize = size;
                RowBytes = size.Width * 4;
                _pixels = GC.AllocateArray<byte>(RowBytes * size.Height, true);

                // a recognizable gradient, so quantization has more than one color to work with
                for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    int offset = y * RowBytes + x * 4;
                    _pixels[offset] = (byte)(x * 255 / Math.Max(1, size.Width - 1));
                    _pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, size.Height - 1));
                    _pixels[offset + 2] = 128;
                    _pixels[offset + 3] = 255;
                }
            }

            private int RowBytes { get; }

            public Vector Dpi => new(96, 96);

            public PixelSize PixelSize { get; }

            public int Version => 1;

            public PixelFormat? Format => PixelFormat.Bgra8888;

            public AlphaFormat? AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

            public ILockedFramebuffer Lock()
            {
                return new PinnedFramebuffer(_pixels, PixelSize, RowBytes, Dpi);
            }

            public void Dispose()
            {
            }

            public void Save(string fileName, int? quality = null)
            {
                throw new NotSupportedException();
            }

            public void Save(Stream stream, int? quality = null)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class PinnedFramebuffer : ILockedFramebuffer
        {
            public PinnedFramebuffer(byte[] pinnedPixels, PixelSize size, int rowBytes, Vector dpi)
            {
                Address = Marshal.UnsafeAddrOfPinnedArrayElement(pinnedPixels, 0);
                Size = size;
                RowBytes = rowBytes;
                Dpi = dpi;
            }

            public IntPtr Address { get; }

            public PixelSize Size { get; }

            public int RowBytes { get; }

            public Vector Dpi { get; }

            public PixelFormat Format => PixelFormat.Bgra8888;

            public AlphaFormat AlphaFormat => Avalonia.Platform.AlphaFormat.Premul;

            public void Dispose()
            {
            }
        }
    }
}
