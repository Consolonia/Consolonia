using System;
using Avalonia;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     Tests how the converter turns visible pixels into cells that show pixel images. A cell whose pixels
    ///     were seen before reuses the same image. Only a cell with new pixels gets a new image.
    /// </summary>
    [TestFixture]
    public class PixelImageConvertCellsTests
    {
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int CellsWide = 4;
        private const int CellsHigh = 2;
        private const int Width = CellsWide * CellWidth;

        /// <summary>
        ///     Makes a picture where each cell is one solid color. The colors depend on <paramref name="seed" />,
        ///     so they are new to this test run and no image comes from the shared cache.
        /// </summary>
        private static byte[] Picture(int seed)
        {
            byte[] bgrx = new byte[CellsWide * CellWidth * CellsHigh * CellHeight * 4];
            for (int cellY = 0; cellY < CellsHigh; cellY++)
            for (int cellX = 0; cellX < CellsWide; cellX++)
                FillCell(bgrx, cellX, cellY, (byte)(seed + cellY * CellsWide + cellX), (byte)(seed >> 8), 0x40);
            return bgrx;
        }

        private static void FillCell(byte[] bgrx, int cellX, int cellY, byte b, byte g, byte r)
        {
            for (int y = 0; y < CellHeight; y++)
            for (int x = 0; x < CellWidth; x++)
            {
                int offset = ((cellY * CellHeight + y) * Width + cellX * CellWidth + x) * 4;
                bgrx[offset] = b;
                bgrx[offset + 1] = g;
                bgrx[offset + 2] = r;
                bgrx[offset + 3] = 0xFF;
            }
        }

        private static PixelImage CellAt(PixelBuffer buffer, int x, int y)
        {
            return buffer[new PixelPoint(x, y)].Background.Image;
        }

        private static PixelBuffer Convert(byte[] picture)
        {
            var cells = new PixelBuffer(CellsWide, CellsHigh);
            PixelImageBitmapConverter.ConvertCells(picture, cells, CellWidth, CellHeight);
            return cells;
        }

        private static int _seed = Environment.TickCount & 0x7FFF;

        [Test]
        public void AnEditedCellIsTheOnlyOneMadeAgain()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            PixelBuffer first = Convert(picture);

            FillCell(picture, 2, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = Convert(picture);

            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x == 2 && y == 1)
                    Assert.That(CellAt(second, x, y), Is.Not.SameAs(CellAt(first, x, y)), "the edited cell");
                else
                    Assert.That(CellAt(second, x, y), Is.SameAs(CellAt(first, x, y)), $"cell {x},{y} is reused");
        }

        /// <summary>
        ///     A blank canvas in a paint program has the same pixels in every cell. Every cell must share one
        ///     cached image from the first frame. Otherwise, on the first brush stroke every cell would switch
        ///     to the one cached image, which is a different object from the image it had before. Then the
        ///     whole canvas would be sent again as "changed".
        /// </summary>
        [Test]
        public void IdenticalCellsShareOneImageFromTheFirstFrame()
        {
            int seed = _seed += 0x100;
            byte[] canvas = new byte[CellsWide * CellWidth * CellsHigh * CellHeight * 4];
            for (int cellY = 0; cellY < CellsHigh; cellY++)
            for (int cellX = 0; cellX < CellsWide; cellX++)
                FillCell(canvas, cellX, cellY, (byte)seed, (byte)(seed >> 8), 0x7F);

            PixelBuffer first = Convert(canvas);
            PixelImage shared = CellAt(first, 0, 0);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(first, x, y), Is.SameAs(shared), $"cell {x},{y} in the first frame");

            FillCell(canvas, 1, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = Convert(canvas);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x != 1 || y != 1)
                    Assert.That(CellAt(second, x, y), Is.SameAs(shared), $"cell {x},{y} after the stroke");
        }

        /// <summary>
        ///     On each edit, the converter refills the same cell buffer instead of making a new one. This test
        ///     checks that converting into the buffer that holds the previous frame gives the same cells as
        ///     converting into a new buffer.
        /// </summary>
        [Test]
        public void ConvertingIntoTheBufferOfThePreviousFrameGivesTheSameCells()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            var reused = new PixelBuffer(CellsWide, CellsHigh);
            PixelImageBitmapConverter.ConvertCells(picture, reused, CellWidth, CellHeight);

            FillCell(picture, 3, 0, 0x44, 0x55, 0x66);
            PixelImageBitmapConverter.ConvertCells(picture, reused, CellWidth, CellHeight);
            PixelBuffer fresh = Convert(picture);

            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(reused, x, y), Is.SameAs(CellAt(fresh, x, y)), $"cell {x},{y}");
        }

        [Test]
        public void PixelsThatAreNotExactlyTheCellsAreRefused()
        {
            Assert.Throws<ArgumentException>(() => PixelImageBitmapConverter.ConvertCells(
                new byte[Width * CellHeight * 4], new PixelBuffer(CellsWide, CellsHigh), CellWidth, CellHeight));
        }

        /// <summary>
        ///     A cell's image holds exactly the cell's pixels. Its dominant color is the color of most of
        ///     those pixels.
        /// </summary>
        [Test]
        public void ACellKeepsItsExactPixels()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            FillCell(picture, 1, 0, 0xFE, 0xFD, 0xFC);

            PixelBuffer buffer = Convert(picture);

            PixelImage cell = CellAt(buffer, 1, 0);
            Assert.That(cell.Width, Is.EqualTo(CellWidth));
            Assert.That(cell.Height, Is.EqualTo(CellHeight));
            Assert.That(cell.Bgra[0], Is.EqualTo(0xFE));
            Assert.That(cell.DominantColor, Is.EqualTo(Color.FromRgb(0xFC, 0xFD, 0xFE)));
        }

        /// <summary>
        ///     A picture cell has a space as its foreground, like an empty cell. The encoders show the picture
        ///     until a character is drawn on top of it.
        /// </summary>
        [Test]
        public void PictureCellsAreSpaces()
        {
            Pixel cell = Convert(Picture(_seed += 0x100))[0, 0];

            Assert.That(cell.Foreground, Is.EqualTo(PixelForeground.Space));
            Assert.That(cell.Width, Is.EqualTo(1));
        }

        /// <summary>
        ///     Both constructors reject bad input. The internal constructor takes a shared array (a slab) and an
        ///     offset, and checks that the cell's pixels fit inside the array. The public constructor calls the
        ///     internal one, and also checks that the array is exactly the size of one cell.
        /// </summary>
        [Test]
        public void PixelImagesRefusePixelsThatAreNotTheCells()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new PixelImage(null, CellWidth, CellHeight));
            Assert.Throws<ArgumentException>(() => _ = new PixelImage(new byte[CellWidth * CellHeight * 4 + 4],
                CellWidth, CellHeight));
            Assert.Throws<ArgumentException>(() => _ = new PixelImage(new byte[CellWidth * CellHeight * 4 - 4],
                CellWidth, CellHeight));

            byte[] slab = new byte[2 * CellWidth * CellHeight * 4];
            Assert.That(new PixelImage(slab, CellWidth * CellHeight * 4, CellWidth, CellHeight, 0).Bgra.Length,
                Is.EqualTo(CellWidth * CellHeight * 4), "the last cell of a slab");
            Assert.Throws<ArgumentException>(() =>
                _ = new PixelImage(slab, CellWidth * CellHeight * 4 + 4, CellWidth, CellHeight, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PixelImage(slab, -1, CellWidth, CellHeight, 0));
        }
    }
}