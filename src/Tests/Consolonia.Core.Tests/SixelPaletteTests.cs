using System;
using System.Text;
using Consolonia.Core.Drawing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>What a sixel accepts and how it writes its palette.</summary>
    [TestFixture]
    public class SixelPaletteTests
    {
        private const int Width = 8;
        private const int Height = 16;

        /// <summary>
        ///     Render reads the arrays with unchecked offsets, so the constructor refuses arrays smaller
        ///     than its counts and dimensions say.
        /// </summary>
        [Test]
        public void TheConstructorRefusesArraysTooSmallForItsDimensions()
        {
            byte[] palette = { 0, 0, 0, 255 };
            byte[] pixels = new byte[Width * Height];

            Assert.Throws<ArgumentException>(() => new Sixel(palette, 1, new byte[Width * Height - 1],
                Width, Height, Width, Height));
            Assert.Throws<ArgumentException>(() => new Sixel(palette, 2, pixels, Width, Height, Width, Height));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Sixel(palette, 0, pixels, Width, Height, Width,
                Height));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Sixel(new byte[257 * 4], 257, pixels, Width,
                Height, Width, Height));
            Assert.That(new Sixel(palette, 1, pixels, Width, Height, Width, Height).Width, Is.EqualTo(Width));
        }

        /// <summary>
        ///     Sixel colors are percentages. Truncating made 254 into 99%, which the terminal reads back
        ///     as 252; rounding keeps a near-white near white.
        /// </summary>
        [Test]
        public void PalettePercentagesAreRounded()
        {
            byte[] palette = { 254, 254, 254, 255 };
            var sixel = new Sixel(palette, 1, new byte[Width * Height], Width, Height, Width, Height);

            string rendered = Encoding.ASCII.GetString(sixel.Render());

            Assert.That(rendered, Does.Contain("#0;2;100;100;100"));
        }

        /// <summary>A picture of few colors is indexed exactly; one of many is quantized to 256.</summary>
        [Test]
        public void FewColorsAreKeptExactlyAndManyAreQuantized()
        {
            byte[] twoColors = new byte[Width * Height * 4];
            for (int i = 0; i < twoColors.Length; i += 4)
            {
                twoColors[i] = twoColors[i + 1] = twoColors[i + 2] = (byte)(i % 8 == 0 ? 0x10 : 0xFE);
                twoColors[i + 3] = 0xFF;
            }

            Sixel exact = Sixel.CreateFromBitmap(twoColors, Width, Height, Width, Height);
            Assert.That(exact.PaletteCount, Is.EqualTo(2));
            Assert.That(exact.Pixels[0], Is.Not.EqualTo(exact.Pixels[1]));

            byte[] manyColors = new byte[64 * 64 * 4];
            for (int i = 0; i < manyColors.Length; i += 4)
            {
                manyColors[i] = (byte)(i / 4);
                manyColors[i + 1] = (byte)(i / 4 >> 6);
                manyColors[i + 2] = (byte)(i / 4 >> 3);
                manyColors[i + 3] = 0xFF;
            }

            Sixel quantized = Sixel.CreateFromBitmap(manyColors, 64, 64, Width, Height);
            Assert.That(quantized.PaletteCount, Is.InRange(2, 256));
        }
    }
}
