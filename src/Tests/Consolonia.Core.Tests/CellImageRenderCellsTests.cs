using System;
using Avalonia;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     How the renderer turns visible pixels into cells showing cell images: cells seen before reuse
    ///     their image, and only a cell with new pixels gets a new one.
    /// </summary>
    [TestFixture]
    public class CellImageRenderCellsTests
    {
        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int CellsWide = 4;
        private const int CellsHigh = 2;
        private const int Width = CellsWide * CellWidth;

        /// <summary>Each cell a solid color unique to this test run, so nothing comes from the shared cache.</summary>
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

        private static CellImage CellAt(PixelBuffer buffer, int x, int y)
        {
            return buffer[new PixelPoint(x, y)].Background.Image;
        }

        private static PixelBuffer Render(byte[] picture)
        {
            var cells = new PixelBuffer(CellsWide, CellsHigh);
            CellImageBitmapRenderer.RenderCells(picture, cells, CellWidth, CellHeight);
            return cells;
        }

        private static int _seed = Environment.TickCount & 0x7FFF;

        [Test]
        public void AnEditedCellIsTheOnlyOneMadeAgain()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            PixelBuffer first = Render(picture);

            FillCell(picture, 2, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = Render(picture);

            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x == 2 && y == 1)
                    Assert.That(CellAt(second, x, y), Is.Not.SameAs(CellAt(first, x, y)), "the edited cell");
                else
                    Assert.That(CellAt(second, x, y), Is.SameAs(CellAt(first, x, y)), $"cell {x},{y} is reused");
        }

        /// <summary>
        ///     A flat canvas (Paintty's blank picture) is one distinct block repeated: every cell must
        ///     share ONE cached image from the first frame, or the first brush stroke, which makes every
        ///     cell hit the cache's single instance, re-sends the whole canvas as "changed".
        /// </summary>
        [Test]
        public void IdenticalCellsShareOneImageFromTheFirstFrame()
        {
            int seed = _seed += 0x100;
            byte[] canvas = new byte[CellsWide * CellWidth * CellsHigh * CellHeight * 4];
            for (int cellY = 0; cellY < CellsHigh; cellY++)
            for (int cellX = 0; cellX < CellsWide; cellX++)
                FillCell(canvas, cellX, cellY, (byte)seed, (byte)(seed >> 8), 0x7F);

            PixelBuffer first = Render(canvas);
            CellImage shared = CellAt(first, 0, 0);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(first, x, y), Is.SameAs(shared), $"cell {x},{y} in the first frame");

            FillCell(canvas, 1, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = Render(canvas);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x != 1 || y != 1)
                    Assert.That(CellAt(second, x, y), Is.SameAs(shared), $"cell {x},{y} after the stroke");
        }

        /// <summary>
        ///     The renderer refills one cell buffer per picture on every edit rather than making a new one:
        ///     rendering into the buffer that holds the previous frame gives exactly the cells a fresh one gets.
        /// </summary>
        [Test]
        public void RenderingIntoTheBufferOfThePreviousFrameGivesTheSameCells()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            var reused = new PixelBuffer(CellsWide, CellsHigh);
            CellImageBitmapRenderer.RenderCells(picture, reused, CellWidth, CellHeight);

            FillCell(picture, 3, 0, 0x44, 0x55, 0x66);
            CellImageBitmapRenderer.RenderCells(picture, reused, CellWidth, CellHeight);
            PixelBuffer fresh = Render(picture);

            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(reused, x, y), Is.SameAs(CellAt(fresh, x, y)), $"cell {x},{y}");
        }

        [Test]
        public void PixelsThatAreNotExactlyTheCellsAreRefused()
        {
            Assert.Throws<ArgumentException>(() => CellImageBitmapRenderer.RenderCells(
                new byte[Width * CellHeight * 4], new PixelBuffer(CellsWide, CellsHigh), CellWidth, CellHeight));
        }

        /// <summary>A cell's image is its own pixels, exactly, and knows the color covering most of them.</summary>
        [Test]
        public void ACellKeepsItsExactPixels()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            FillCell(picture, 1, 0, 0xFE, 0xFD, 0xFC);

            PixelBuffer buffer = Render(picture);

            CellImage cell = CellAt(buffer, 1, 0);
            Assert.That(cell.Width, Is.EqualTo(CellWidth));
            Assert.That(cell.Height, Is.EqualTo(CellHeight));
            Assert.That(cell.Bgra[0], Is.EqualTo(0xFE));
            Assert.That(cell.DominantColor, Is.EqualTo(Color.FromRgb(0xFC, 0xFD, 0xFE)));
        }

        /// <summary>
        ///     Both constructors guard the same way: the one taking a slab checks that the cell's pixels
        ///     lie within it, and the public one, which goes through it, that they are exactly the cell's.
        /// </summary>
        [Test]
        public void CellImagesRefusePixelsThatAreNotTheCells()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new CellImage(null, CellWidth, CellHeight));
            Assert.Throws<ArgumentException>(() => _ = new CellImage(new byte[CellWidth * CellHeight * 4 + 4],
                CellWidth, CellHeight));
            Assert.Throws<ArgumentException>(() => _ = new CellImage(new byte[CellWidth * CellHeight * 4 - 4],
                CellWidth, CellHeight));

            byte[] slab = new byte[2 * CellWidth * CellHeight * 4];
            Assert.That(new CellImage(slab, CellWidth * CellHeight * 4, CellWidth, CellHeight, 0).Bgra.Length,
                Is.EqualTo(CellWidth * CellHeight * 4), "the last cell of a slab");
            Assert.Throws<ArgumentException>(() =>
                _ = new CellImage(slab, CellWidth * CellHeight * 4 + 4, CellWidth, CellHeight, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CellImage(slab, -1, CellWidth, CellHeight, 0));
        }
    }
}
