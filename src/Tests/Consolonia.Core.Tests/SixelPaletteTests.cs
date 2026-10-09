using System.Text;
using Consolonia.Core.Drawing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     A cell's sixel is quantized against the palette of the whole visible picture. Cells that
    ///     did not change keep the sixel they already have, so a cell re-quantized after the picture
    ///     changed sits beside cells quantized before it -- and any color the two disagree on shows
    ///     as a seam around everything that was redrawn.
    /// </summary>
    [TestFixture]
    public class SixelPaletteTests
    {
        private const int Width = 8;
        private const int Height = 16;

        // BGRX: index 0 is a light antialiasing gray, index 1 is white.
        private static readonly byte[] WhiteAndLightGray =
        {
            247, 247, 247, 255,
            255, 255, 255, 255
        };

        private static byte[] Solid(byte value)
        {
            byte[] bgrx = new byte[Width * Height * 4];
            for (int i = 0; i < bgrx.Length; i += 4)
            {
                bgrx[i] = bgrx[i + 1] = bgrx[i + 2] = value;
                bgrx[i + 3] = 255;
            }

            return bgrx;
        }

        /// <summary>
        ///     White used to be matched as 248 -- the low corner of its lookup bin -- so a palette that
        ///     also held a light gray turned every white pixel in a redrawn cell gray, and lines drawn on
        ///     a white canvas grew gray halos the shape of the cells they touched.
        /// </summary>
        [Test]
        public void White_stays_white_beside_a_light_gray()
        {
            Sixel sixel = Sixel.CreateFromBitmap(Solid(255), Width, Height, Width, Height, WhiteAndLightGray);

            Assert.That(sixel.Pixels, Is.All.EqualTo(1));
        }

        [Test]
        public void An_exact_palette_color_is_always_chosen()
        {
            Sixel sixel = Sixel.CreateFromBitmap(Solid(247), Width, Height, Width, Height, WhiteAndLightGray);

            Assert.That(sixel.Pixels, Is.All.EqualTo(0));
        }

        /// <summary>
        ///     Sixel colors are percentages. Truncating made 254 into 99%, which the terminal reads back
        ///     as 252; rounding keeps a near-white near white.
        /// </summary>
        [Test]
        public void Palette_percentages_are_rounded()
        {
            byte[] palette = { 254, 254, 254, 255 };
            Sixel sixel = Sixel.CreateFromBitmap(Solid(254), Width, Height, Width, Height, palette);

            string rendered = Encoding.ASCII.GetString(sixel.Render());

            Assert.That(rendered, Does.Contain("#0;2;100;100;100"));
        }
    }
}
