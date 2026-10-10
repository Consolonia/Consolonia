using System;
using System.Text;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     How the sixel renderer turns visible pixels into per-cell sixels: cells seen before are reused,
    ///     and only the new ones are quantized, together.
    /// </summary>
    [TestFixture]
    public class SixelRenderCellsTests
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

        private static Sixel CellAt(PixelBuffer buffer, int x, int y)
        {
            return buffer[new Avalonia.PixelPoint(x, y)].Foreground.Symbol.Sixel;
        }

        private static int _seed = Environment.TickCount & 0x7FFF;

        [Test]
        public void AnEditedCellIsTheOnlyOneMadeAgain()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            PixelBuffer first = SixelBitmapRenderer.RenderCells(picture, CellsWide, CellsHigh, CellWidth, CellHeight);

            FillCell(picture, 2, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = SixelBitmapRenderer.RenderCells(picture, CellsWide, CellsHigh, CellWidth, CellHeight);

            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x == 2 && y == 1)
                    Assert.That(CellAt(second, x, y), Is.Not.SameAs(CellAt(first, x, y)), "the edited cell");
                else
                    Assert.That(CellAt(second, x, y), Is.SameAs(CellAt(first, x, y)), $"cell {x},{y} is reused");
        }

        /// <summary>
        ///     A flat canvas (Paintty's blank picture) is one distinct block repeated: every cell must
        ///     share ONE cached sixel from the first frame, or the first brush stroke, which makes every
        ///     cell hit the cache's single instance, re-sends the whole canvas as "changed".
        /// </summary>
        [Test]
        public void IdenticalCellsShareOneSixelFromTheFirstFrame()
        {
            int seed = _seed += 0x100;
            byte[] canvas = new byte[CellsWide * CellWidth * CellsHigh * CellHeight * 4];
            for (int cellY = 0; cellY < CellsHigh; cellY++)
            for (int cellX = 0; cellX < CellsWide; cellX++)
                FillCell(canvas, cellX, cellY, (byte)seed, (byte)(seed >> 8), 0x7F);

            PixelBuffer first = SixelBitmapRenderer.RenderCells(canvas, CellsWide, CellsHigh, CellWidth, CellHeight);
            Sixel shared = CellAt(first, 0, 0);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(first, x, y), Is.SameAs(shared), $"cell {x},{y} in the first frame");

            FillCell(canvas, 1, 1, 0x11, 0x22, 0x33);
            PixelBuffer second = SixelBitmapRenderer.RenderCells(canvas, CellsWide, CellsHigh, CellWidth, CellHeight);
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                if (x != 1 || y != 1)
                    Assert.That(CellAt(second, x, y), Is.SameAs(shared), $"cell {x},{y} after the stroke");
        }

        [Test]
        public void NewCellsShareOnePaletteSoNeighboursCanBeJoined()
        {
            PixelBuffer buffer = SixelBitmapRenderer.RenderCells(Picture(_seed += 0x100), CellsWide, CellsHigh,
                CellWidth, CellHeight);

            byte[] palette = CellAt(buffer, 0, 0).Palette;
            for (int y = 0; y < CellsHigh; y++)
            for (int x = 0; x < CellsWide; x++)
                Assert.That(CellAt(buffer, x, y).Palette, Is.SameAs(palette), $"cell {x},{y}");
        }

        /// <summary>
        ///     A cell's pixels are the quantizer's own indices for it, so a picture with few colors keeps
        ///     them exactly: no nearest-color match against a palette made for something else.
        /// </summary>
        [Test]
        public void ANewCellKeepsItsExactColor()
        {
            int seed = _seed += 0x100;
            byte[] picture = Picture(seed);
            FillCell(picture, 1, 0, 0xFE, 0xFE, 0xFE);

            PixelBuffer buffer = SixelBitmapRenderer.RenderCells(picture, CellsWide, CellsHigh, CellWidth, CellHeight);

            Sixel cell = CellAt(buffer, 1, 0);
            Assert.That(cell.DominantColor, Is.EqualTo(Avalonia.Media.Color.FromRgb(0xFE, 0xFE, 0xFE)));
        }

        /// <summary>
        ///     A cell uses a few colors of a palette shared by a whole picture; only those are defined in
        ///     its escape sequence.
        /// </summary>
        [Test]
        public void RenderDefinesOnlyTheColorsTheImageUses()
        {
            byte[] palette = { 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 255, 255 };
            byte[] pixels = new byte[CellWidth * CellHeight];
            Array.Fill(pixels, (byte)1);

            string rendered = Encoding.ASCII.GetString(
                new Sixel(palette, 3, pixels, CellWidth, CellHeight, CellWidth, CellHeight).Render());

            Assert.That(rendered, Does.Contain("#1;2;100;100;100"));
            Assert.That(rendered, Does.Not.Contain("#0;2;"));
            Assert.That(rendered, Does.Not.Contain("#2;2;"));
        }

        [Test]
        public void ATransientImageRendersTheSameBytesWithoutKeepingThem()
        {
            byte[] palette = { 0, 0, 0, 255, 255, 255, 255, 255 };
            byte[] pixels = new byte[CellWidth * CellHeight];
            pixels[3] = 1;

            string kept = Encoding.ASCII.GetString(
                new Sixel(palette, 2, pixels, CellWidth, CellHeight, CellWidth, CellHeight).Render());
            var transient = new Sixel(palette, 2, pixels, CellWidth, CellHeight, CellWidth, CellHeight)
                { IsTransient = true };

            Assert.That(Encoding.ASCII.GetString(transient.Render()), Is.EqualTo(kept));
        }
    }
}
