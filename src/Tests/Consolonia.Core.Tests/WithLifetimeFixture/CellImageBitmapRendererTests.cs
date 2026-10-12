using System;
using Avalonia;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     How a picture becomes cells showing <see cref="CellImage" />s, on any console with an image
    ///     protocol: the renderer is the same whichever protocol the console has.
    /// </summary>
    [TestFixture(ConsoleCapabilities.SupportsSixel)]
    [TestFixture(ConsoleCapabilities.SupportsKittyGraphics)]
    public sealed class CellImageBitmapRendererTests(ConsoleCapabilities protocol) : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _console = (UnitTestConsole)AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _originalCapabilities = _console.Capabilities;
            _console.Capabilities = protocol;

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

        private const int PictureCellsWide = 4;
        private const int PictureCellsHigh = 3;

        private UnitTestConsole _console;
        private ConsoleCapabilities _originalCapabilities;
        private ConsoleWindowImpl _consoleWindowImpl;
        private PixelBuffer _buffer;
        private DrawingContextImpl _dc;

        private PixelSize PictureSize => new(PictureCellsWide * _console.CellPixelWidth,
            PictureCellsHigh * _console.CellPixelHeight);

        public void Dispose()
        {
            _consoleWindowImpl?.Dispose();
        }

        private CellImage ImageAt(int x, int y)
        {
            return _buffer[(ushort)x, (ushort)y].Background.Image;
        }

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

            for (int y = 0; y < cellsHigh; y++)
            for (int x = 0; x < cellsWide; x++)
            {
                Assert.That(ImageAt(x, y), Is.Not.Null, $"cell {x},{y} should show the picture");
                Assert.That(_buffer[(ushort)x, (ushort)y].Background.Color, Is.EqualTo(Avalonia.Media.Colors.Transparent),
                    "nothing painted over the picture yet");
            }
        }

        /// <summary>
        ///     Regression test: all image cells look alike to the pixel buffer diff unless the image itself is
        ///     part of the comparison, and a second picture drawn over the first was dropped as "unchanged".
        /// </summary>
        [Test]
        public void DrawingAnotherBitmapOverTheFirstReplacesTheCells()
        {
            const int cellsWide = 2;
            const int cellsHigh = 2;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight);
            var destRect = new Rect(0, 0, cellsWide, cellsHigh);

            using var first = new FakeReadableBitmap(size);
            _dc.DrawBitmap(first, 1, new Rect(destRect.Size), destRect);
            var firstImages = new CellImage[cellsWide, cellsHigh];
            for (int y = 0; y < cellsHigh; y++)
            for (int x = 0; x < cellsWide; x++)
            {
                firstImages[x, y] = ImageAt(x, y);
                Assert.That(firstImages[x, y], Is.Not.Null);
            }

            using var second = new FakeReadableBitmap(size, 32);
            _dc.DrawBitmap(second, 1, new Rect(destRect.Size), destRect);

            for (int y = 0; y < cellsHigh; y++)
            for (int x = 0; x < cellsWide; x++)
                Assert.That(ImageAt(x, y), Is.Not.SameAs(firstImages[x, y]), $"cell {x},{y} should show the second picture");
        }

        /// <summary>A flat canvas is one image, shared by every cell showing it.</summary>
        [Test]
        public void CellsThatLookAlikeShareOneImage()
        {
            const int cellsWide = 3;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, _console.CellPixelHeight);
            using FakeReadableBitmap blank = new FakeReadableBitmap(size).Fill(new PixelRect(size), 255, 255, 255);

            _dc.DrawBitmap(blank, 1, new Rect(size.ToSize(1)), new Rect(0, 0, cellsWide, 1));

            Assert.That(ImageAt(1, 0), Is.SameAs(ImageAt(0, 0)));
            Assert.That(ImageAt(2, 0), Is.SameAs(ImageAt(0, 0)));
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

            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                bool visible = x < 3 && y < 2;
                Assert.That(ImageAt(x, y), visible ? Is.Not.Null : Is.Null, $"cell {x},{y}");
            }
        }

        /// <summary>
        ///     Regression test: a partial redraw (a dialog opening over the picture) clips to the region being
        ///     redrawn, and a rendering was made per clip. The cells outside it kept showing the earlier one,
        ///     which the cache could then evict. A redraw of part of the picture reuses the rendering of the
        ///     whole on-screen picture.
        /// </summary>
        [Test]
        public void PartialRedrawReusesTheRenderingOfTheWholePicture()
        {
            using var bitmap = new FakeReadableBitmap(PictureSize);
            DrawPicture(bitmap);
            CellImage whole = ImageAt(2, 1);
            _buffer[2, 1] = Pixel.Space;

            var destRect = new Rect(0, 0, PictureCellsWide, PictureCellsHigh);
            _dc.PushClip(new Rect(1, 1, 2, 1));
            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);
            _dc.PopClip();

            Assert.That(ImageAt(2, 1), Is.SameAs(whole));
        }

        /// <summary>
        ///     An edit to the picture (a new bitmap, as a paint program publishes per stroke) keeps the image
        ///     of every cell it did not touch, so only the touched cell is dirty and written again.
        /// </summary>
        [Test]
        public void EditedPictureKeepsTheImagesOfUntouchedCells()
        {
            int cellWidth = _console.CellPixelWidth;
            int cellHeight = _console.CellPixelHeight;

            using var before = new FakeReadableBitmap(PictureSize);
            CellImage[,] images = DrawPicture(before);

            // the same picture with one pixel of cell 2,1 painted over
            using FakeReadableBitmap after = new FakeReadableBitmap(PictureSize)
                .Fill(new PixelRect(2 * cellWidth + 1, cellHeight + 1, 1, 1), 0, 0, 255);
            DrawPicture(after);

            AssertOnlyCellHasANewImage(images, 2, 1);
        }

        /// <summary>
        ///     A picture edited in place (a writeable bitmap, as a paint program draws into) is the same
        ///     bitmap at a new version. Its rendering is refilled, not remade: untouched cells keep their
        ///     images and the touched one gets a new one.
        /// </summary>
        [Test]
        public void ANewVersionOfTheSameBitmapRefillsItsRendering()
        {
            int cellWidth = _console.CellPixelWidth;
            int cellHeight = _console.CellPixelHeight;

            using var bitmap = new FakeReadableBitmap(PictureSize);
            CellImage[,] images = DrawPicture(bitmap);

            bitmap.Fill(new PixelRect(cellWidth + 2, 2 * cellHeight + 3, 1, 1), 255, 0, 0).Version++;
            DrawPicture(bitmap);

            AssertOnlyCellHasANewImage(images, 1, 2);
        }

        /// <summary>Draws a picture over the top-left cells and returns the image each cell got.</summary>
        private CellImage[,] DrawPicture(FakeReadableBitmap bitmap)
        {
            var destRect = new Rect(0, 0, PictureCellsWide, PictureCellsHigh);
            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);

            var images = new CellImage[PictureCellsWide, PictureCellsHigh];
            for (int y = 0; y < PictureCellsHigh; y++)
            for (int x = 0; x < PictureCellsWide; x++)
                images[x, y] = ImageAt(x, y);
            return images;
        }

        private void AssertOnlyCellHasANewImage(CellImage[,] before, int changedX, int changedY)
        {
            for (int y = 0; y < PictureCellsHigh; y++)
            for (int x = 0; x < PictureCellsWide; x++)
                Assert.That(ImageAt(x, y),
                    x == changedX && y == changedY ? Is.Not.SameAs(before[x, y]) : Is.SameAs(before[x, y]),
                    $"cell {x},{y}");
        }
    }
}
