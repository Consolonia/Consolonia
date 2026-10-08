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

        /// <summary>
        ///     Regression test: all sixel cells look alike to the pixel buffer diff unless the image itself is
        ///     part of the comparison, and a second picture drawn over the first was dropped as "unchanged".
        /// </summary>
        [Test]
        public void DrawingAnotherBitmapOverTheFirstReplacesTheSixelCells()
        {
            const int cellsWide = 2;
            const int cellsHigh = 2;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight);
            var destRect = new Rect(0, 0, cellsWide, cellsHigh);

            using var first = new FakeReadableBitmap(size);
            _dc.DrawBitmap(first, 1, new Rect(destRect.Size), destRect);

            var firstSixels = new Sixel[cellsWide, cellsHigh];
            for (ushort y = 0; y < cellsHigh; y++)
            for (ushort x = 0; x < cellsWide; x++)
            {
                firstSixels[x, y] = _buffer[x, y].Foreground.Symbol.Sixel;
                Assert.IsNotNull(firstSixels[x, y]);
            }

            using var second = new FakeReadableBitmap(size, 32);
            _dc.DrawBitmap(second, 1, new Rect(destRect.Size), destRect);

            for (ushort y = 0; y < cellsHigh; y++)
            for (ushort x = 0; x < cellsWide; x++)
                Assert.That(_buffer[x, y].Foreground.Symbol.Sixel, Is.Not.SameAs(firstSixels[x, y]),
                    $"cell {x},{y} should hold the second image");
        }

        /// <summary>
        ///     Regression test: a picture larger than the clip (zoomed, or scrolled partly out of view) was
        ///     rendered whole, at a size a terminal can refuse outright, so it vanished. Only the visible
        ///     cells are rendered now, and the cells outside the clip are left alone.
        /// </summary>
        [Test]
        public void PictureLargerThanTheClipRendersOnlyItsVisibleCells()
        {
            const int cellsWide = 6;
            const int cellsHigh = 4;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight);
            using var bitmap = new FakeReadableBitmap(size);

            // scrolled two cells left and one up, and clipped to a 3x2 viewport
            var destRect = new Rect(-2, -1, cellsWide, cellsHigh);
            _dc.PushClip(new Rect(0, 0, 3, 2));
            Assert.DoesNotThrow(() => _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect));
            _dc.PopClip();

            for (ushort y = 0; y < 3; y++)
            for (ushort x = 0; x < 4; x++)
            {
                bool visible = x < 3 && y < 2;
                Assert.That(_buffer[x, y].Foreground.Symbol.Sixel, visible ? Is.Not.Null : Is.Null,
                    $"cell {x},{y}");
            }
        }

        /// <summary>
        ///     Regression test: a partial redraw (a dialog opening over the picture) clips to the region being
        ///     redrawn, and a rendering was made per clip. The cells outside it kept showing the earlier one,
        ///     which the cache could then evict, blanking them under kitty. A redraw of part of the picture
        ///     reuses the rendering of the whole on-screen picture.
        /// </summary>
        [Test]
        public void PartialRedrawReusesTheRenderingOfTheWholePicture()
        {
            const int cellsWide = 4;
            const int cellsHigh = 3;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight);
            using var bitmap = new FakeReadableBitmap(size);
            var destRect = new Rect(0, 0, cellsWide, cellsHigh);

            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);
            Sixel whole = _buffer[2, 1].Foreground.Symbol.Sixel;
            _buffer[2, 1] = Pixel.Space;

            _dc.PushClip(new Rect(1, 1, 2, 1));
            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);
            _dc.PopClip();

            Assert.That(_buffer[2, 1].Foreground.Symbol.Sixel, Is.SameAs(whole));
        }

        /// <summary>
        ///     An edit to the picture (a new bitmap, as a paint program publishes per stroke) keeps the sixel
        ///     of every cell it did not touch, so only the touched cells are dirty and written again.
        /// </summary>
        [Test]
        public void EditedPictureKeepsTheSixelsOfUntouchedCells()
        {
            const int cellsWide = 4;
            const int cellsHigh = 3;
            int cellWidth = _console.CellPixelWidth;
            int cellHeight = _console.CellPixelHeight;
            var size = new PixelSize(cellsWide * cellWidth, cellsHigh * cellHeight);
            var destRect = new Rect(0, 0, cellsWide, cellsHigh);

            using var before = new FakeReadableBitmap(size);
            _dc.DrawBitmap(before, 1, new Rect(destRect.Size), destRect);
            var sixels = new Sixel[cellsWide, cellsHigh];
            for (ushort y = 0; y < cellsHigh; y++)
            for (ushort x = 0; x < cellsWide; x++)
                sixels[x, y] = _buffer[x, y].Foreground.Symbol.Sixel;

            // the same picture with one pixel of cell 2,1 painted over
            using FakeReadableBitmap after = new FakeReadableBitmap(size)
                .Fill(new PixelRect(2 * cellWidth + 1, cellHeight + 1, 1, 1), 0, 0, 255);
            _dc.DrawBitmap(after, 1, new Rect(destRect.Size), destRect);

            for (ushort y = 0; y < cellsHigh; y++)
            for (ushort x = 0; x < cellsWide; x++)
                Assert.That(_buffer[x, y].Foreground.Symbol.Sixel,
                    x == 2 && y == 1 ? Is.Not.SameAs(sixels[x, y]) : Is.SameAs(sixels[x, y]),
                    $"cell {x},{y}");
        }

        public void Dispose()
        {
            _consoleWindowImpl?.Dispose();
        }

        /// <summary>
        ///     Minimal readable bitmap over a pinned BGRA buffer. The real render interface cannot create one
        ///     here because Consolonia.Core.Tests has no Skia fallback.
        /// </summary>
        internal sealed class FakeReadableBitmap : IReadableBitmapImpl
        {
            private readonly byte[] _pixels;

            public FakeReadableBitmap(PixelSize size, byte tint = 128)
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
                    _pixels[offset + 2] = tint;
                    _pixels[offset + 3] = 255;
                }
            }

            private int RowBytes { get; }

            /// <summary>Paints <paramref name="area" /> one opaque color.</summary>
            public FakeReadableBitmap Fill(PixelRect area, byte blue, byte green, byte red)
            {
                for (int y = area.Y; y < area.Bottom; y++)
                for (int x = area.X; x < area.Right; x++)
                {
                    int offset = y * RowBytes + x * 4;
                    _pixels[offset] = blue;
                    _pixels[offset + 1] = green;
                    _pixels[offset + 2] = red;
                    _pixels[offset + 3] = 255;
                }

                return this;
            }

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

            public AlphaFormat AlphaFormat => AlphaFormat.Premul;

            public void Dispose()
            {
            }
        }
    }
}