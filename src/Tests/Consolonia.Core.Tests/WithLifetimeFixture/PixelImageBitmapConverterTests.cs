using System;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.NUnit;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     Tests how a picture is turned into cells that show <see cref="PixelImage" />s. The console is set
    ///     up to support pixel images. The converter works the same for every image protocol.
    /// </summary>
    [TestFixture]
    public sealed class PixelImageBitmapConverterTests : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _console = (UnitTestConsole)AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _originalCapabilities = _console.Capabilities;
            _console.Capabilities = ConsoleCapabilities.SupportsPixelImages;

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

        private PixelImage ImageAt(int x, int y)
        {
            return _buffer[(ushort)x, (ushort)y].Background.Image;
        }

        /// <summary>
        ///     Regression test. When a bitmap is already at the target size, the converter skips the resize.
        ///     The converter used to read the resize result anyway, which was null, and threw.
        ///     Here the bitmap is 4x2 cells, which at 8x16 pixels per cell is exactly 32x32 pixels.
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
                Assert.That(_buffer[(ushort)x, (ushort)y].Background.Color, Is.EqualTo(Colors.Transparent),
                    "nothing painted over the picture yet");
            }
        }

        /// <summary>
        ///     Regression test. Image cells have no character, so they all look the same to the pixel buffer
        ///     diff unless the diff also compares the image. Before the fix, a second picture drawn over the
        ///     first was treated as "unchanged" and was not drawn.
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
            var firstImages = new PixelImage[cellsWide, cellsHigh];
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
                Assert.That(ImageAt(x, y), Is.Not.SameAs(firstImages[x, y]),
                    $"cell {x},{y} should show the second picture");
        }

        /// <summary>A plain one-color picture produces one image, and every cell shares it.</summary>
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
        ///     Regression test. A picture can be larger than the clip, for example when zoomed or partly
        ///     scrolled out of view. It used to be converted whole, at a size some terminals reject, so it
        ///     disappeared. Now only the visible cells are converted, and cells outside the clip are not touched.
        /// </summary>
        [Test]
        public void PictureLargerThanTheClipRendersOnlyItsVisibleCells()
        {
            const int cellsWide = 6;
            const int cellsHigh = 4;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight);
            using var bitmap = new FakeReadableBitmap(size);

            // The picture is scrolled two cells left and one cell up, and clipped to a 3x2 viewport.
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
        ///     Regression test. A partial redraw (for example, a dialog opening over the picture) clips to the
        ///     region being redrawn. The converter used to make a separate conversion for each clip. Cells
        ///     outside the clip still showed images from the earlier conversion, and the cache could then evict
        ///     those images. Now a partial redraw reuses the conversion of the whole visible picture.
        /// </summary>
        [Test]
        public void PartialRedrawReusesTheRenderingOfTheWholePicture()
        {
            using var bitmap = new FakeReadableBitmap(PictureSize);
            DrawPicture(bitmap);
            PixelImage whole = ImageAt(2, 1);
            _buffer[2, 1] = Pixel.Space;

            var destRect = new Rect(0, 0, PictureCellsWide, PictureCellsHigh);
            _dc.PushClip(new Rect(1, 1, 2, 1));
            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);
            _dc.PopClip();

            Assert.That(ImageAt(2, 1), Is.SameAs(whole));
        }

        /// <summary>
        ///     Draws a picture, then draws a new bitmap with one pixel changed (as a paint program might do for
        ///     each stroke). Cells that did not change keep the same image object. Only the changed cell gets a
        ///     new image, so only that cell needs to be written again.
        /// </summary>
        [Test]
        public void EditedPictureKeepsTheImagesOfUntouchedCells()
        {
            int cellWidth = _console.CellPixelWidth;
            int cellHeight = _console.CellPixelHeight;

            using var before = new FakeReadableBitmap(PictureSize);
            PixelImage[,] images = DrawPicture(before);

            // The same picture, with one pixel in cell (2,1) changed.
            using FakeReadableBitmap after = new FakeReadableBitmap(PictureSize)
                .Fill(new PixelRect(2 * cellWidth + 1, cellHeight + 1, 1, 1), 0, 0, 255);
            DrawPicture(after);

            AssertOnlyCellHasANewImage(images, 2, 1);
        }

        /// <summary>
        ///     Edits a bitmap in place (like a writeable bitmap in a paint program) and increments its version.
        ///     The existing conversion is refilled instead of created again. Unchanged cells keep their images,
        ///     and only the changed cell gets a new one.
        /// </summary>
        [Test]
        public void ANewVersionOfTheSameBitmapRefillsItsRendering()
        {
            int cellWidth = _console.CellPixelWidth;
            int cellHeight = _console.CellPixelHeight;

            using var bitmap = new FakeReadableBitmap(PictureSize);
            PixelImage[,] images = DrawPicture(bitmap);

            bitmap.Fill(new PixelRect(cellWidth + 2, 2 * cellHeight + 3, 1, 1), 255, 0, 0).Version++;
            DrawPicture(bitmap);

            AssertOnlyCellHasANewImage(images, 1, 2);
        }

        /// <summary>Draws the picture in the top-left cells and returns the image of each cell.</summary>
        private PixelImage[,] DrawPicture(FakeReadableBitmap bitmap)
        {
            var destRect = new Rect(0, 0, PictureCellsWide, PictureCellsHigh);
            _dc.DrawBitmap(bitmap, 1, new Rect(destRect.Size), destRect);

            var images = new PixelImage[PictureCellsWide, PictureCellsHigh];
            for (int y = 0; y < PictureCellsHigh; y++)
            for (int x = 0; x < PictureCellsWide; x++)
                images[x, y] = ImageAt(x, y);
            return images;
        }

        private void AssertOnlyCellHasANewImage(PixelImage[,] before, int changedX, int changedY)
        {
            for (int y = 0; y < PictureCellsHigh; y++)
            for (int x = 0; x < PictureCellsWide; x++)
                Assert.That(ImageAt(x, y),
                    x == changedX && y == changedY ? Is.Not.SameAs(before[x, y]) : Is.SameAs(before[x, y]),
                    $"cell {x},{y}");
        }
    }
}