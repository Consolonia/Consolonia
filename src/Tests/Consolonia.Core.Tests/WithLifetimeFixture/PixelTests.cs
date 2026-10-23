// DUPFINDER_ignore

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class PixelTests
    {
        [Test]
        public void ConstructorCaret()
        {
            var pixel = new Pixel(CaretStyle.BlinkingBar);
            Assert.That(pixel.IsCaret());
            Assert.That(pixel.Foreground == PixelForeground.Default);
            Assert.That(pixel.Background == PixelBackground.Transparent);
        }

        [Test]
        public void ConstructorColorOnly()
        {
            var pixel = new Pixel(new PixelBackground(Colors.Red));
            Assert.That(pixel.Background.Color, Is.EqualTo(Colors.Red));
        }

        [Test]
        public void ConstructorColorAndSymbol()
        {
            var pixel = new Pixel(new Symbol('a'), Colors.Red);
            Assert.That(pixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(pixel.Foreground.Color, Is.EqualTo(Colors.Red));
            Assert.That(pixel.Foreground.Style, Is.EqualTo(FontStyle.Normal));
            Assert.That(pixel.Foreground.Weight, Is.EqualTo(FontWeight.Normal));
            Assert.That(pixel.Foreground.TextDecoration, Is.Null);
            Assert.That(pixel.Background.Color, Is.EqualTo(Colors.Transparent));
        }

        [Test]
        public void ConstructorSymbol()
        {
            var pixel = new Pixel(new Symbol(0b0000_1111), Colors.Red);
            Assert.That(pixel.Foreground.Symbol.Character, Is.EqualTo('┼'));
            Assert.That(pixel.Foreground.Color, Is.EqualTo(Colors.Red));
            Assert.That(pixel.Foreground.Style, Is.EqualTo(FontStyle.Normal));
            Assert.That(pixel.Foreground.Weight, Is.EqualTo(FontWeight.Normal));
            Assert.That(pixel.Foreground.TextDecoration, Is.Null);
            Assert.That(pixel.Background.Color, Is.EqualTo(Colors.Transparent));
        }

        [Test]
        public void ConstructorSymbolAndColor()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol(0b0000_1111), Colors.Red),
                new PixelBackground(Colors.Blue));
            Assert.That(pixel.Foreground.Symbol.Character, Is.EqualTo('┼'));
            Assert.That(pixel.Foreground.Color, Is.EqualTo(Colors.Red));
            Assert.IsNull(pixel.Foreground.Style);
            Assert.IsNull(pixel.Foreground.Weight);
            Assert.IsNull(pixel.Foreground.TextDecoration);
            Assert.That(pixel.Background.Color, Is.EqualTo(Colors.Blue));
        }

        [Test]
        public void Equality()
        {
            var pixel1 = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            var pixel2 = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            Assert.That(pixel1.Equals((object)pixel2));
            Assert.That(pixel1.Equals(pixel2));
            Assert.That(pixel1 == pixel2);
        }

        [Test]
        public void NotEqual()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            var pixel2 = new Pixel(new PixelForeground(new Symbol('b'), Colors.Red),
                new PixelBackground(Colors.Blue));
            Assert.That(!pixel.Equals((object)pixel2));
            Assert.That(!pixel.Equals(pixel2));
            Assert.That(pixel != pixel2);

            pixel = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            pixel2 = new Pixel(new PixelForeground(new Symbol('a'), Colors.Blue),
                new PixelBackground(Colors.Blue));
            Assert.That(!pixel.Equals((object)pixel2));
            Assert.That(!pixel.Equals(pixel2));
            Assert.That(pixel != pixel2);
        }

        /// <summary>
        ///     A Pixel is copied on every blend and every frame and compared for every dirty cell: its
        ///     fields are stored flat so that two references, three ints, a char and five bytes take 40
        ///     bytes, not the 56 the nested structs padded to, image reference included.
        /// </summary>
        [Test]
        public void PixelIsFortyBytes()
        {
            Assert.That(Unsafe.SizeOf<Pixel>(), Is.EqualTo(40));
        }

        [Test]
        public void ImageCellsCompareByImage()
        {
            // regression: an image cell carries no character and no pattern, so pixels which ignored the
            // image compared equal and the pixel buffer diff kept showing the previous picture
            CellImage image = CreateCellImage(10);
            CellImage otherImage = CreateCellImage(200);

            Pixel pixel = CreateImagePixel(image);
            Pixel samePixel = CreateImagePixel(image);
            Pixel otherPixel = CreateImagePixel(otherImage);

            Assert.That(pixel == samePixel);
            Assert.That(pixel.GetHashCode(), Is.EqualTo(samePixel.GetHashCode()));
            Assert.That(pixel != otherPixel);
            Assert.That(pixel.Equals((object)otherPixel), Is.False);
        }

        /// <summary>
        ///     The "image as cell background" model: cell = (color | picture under a paint) + foreground
        ///     glyph. The paint accumulates through the same blend as a plain cell's color.
        /// </summary>
        [Test]
        public void ImageBackgroundComposesWithForegroundAndDiesByOpaqueBackground()
        {
            CellImage image = CreateCellImage(200);
            Pixel imagePixel = CreateImagePixel(image);

            Pixel withGlyph = imagePixel.Blend(new Pixel(
                new PixelForeground(new Symbol('▕'), Colors.Gray),
                PixelBackground.Transparent));
            Assert.That(withGlyph.Foreground.Symbol.Character, Is.EqualTo('▕'));
            Assert.That(withGlyph.Background.Image, Is.SameAs(image),
                "a glyph with transparent background lands OVER the picture, not evicting it");

            var backdrop = new Pixel(new PixelBackground(Color.Parse("#7F000000")));
            Pixel dimmed = imagePixel.Blend(backdrop);
            Assert.That(dimmed.Background.Image, Is.SameAs(image), "a translucent paint must not evict the picture");
            Assert.That(dimmed.Background.Color, Is.EqualTo(Color.Parse("#7F000000")),
                "the paint accumulates in the cell's color, for the output to lay over the picture");
            Assert.That(imagePixel.Blend(backdrop), Is.EqualTo(dimmed),
                "the same paint must yield the same cell, else every frame re-sends it");

            Pixel covered = imagePixel.Blend(new Pixel(new PixelBackground(Colors.White)));
            Assert.That(covered.Background.Image, Is.Null,
                "an opaque background owns the cell: the picture must be evicted");

            Pixel overText = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue)).Blend(imagePixel);
            Assert.That(overText, Is.EqualTo(imagePixel), "an image cell drawn over a text cell takes it whole");
        }

        /// <summary>
        ///     Where a cell needs one color for a picture (the mouse cursor's contrast, text on a terminal
        ///     that cannot draw it over an image), the paint composited over the picture's dominant color.
        /// </summary>
        [Test]
        public void TheEffectiveColorOfAnImageCellIsThePaintOverItsDominantColor()
        {
            // a mostly gray picture with a sliver of red: gray is the dominant color
            byte[] bgra = new byte[8 * 16 * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bool red = i < 8 * 4;
                bgra[i] = red ? (byte)0 : (byte)200;
                bgra[i + 1] = red ? (byte)0 : (byte)200;
                bgra[i + 2] = red ? (byte)255 : (byte)200;
                bgra[i + 3] = 255;
            }

            var image = new CellImage(bgra, 8, 16);
            Pixel pixel = CreateImagePixel(image);
            Assert.That(image.DominantColor, Is.EqualTo(Color.FromRgb(200, 200, 200)));
            Assert.That(pixel.Background.EffectiveColor, Is.EqualTo(Color.FromRgb(200, 200, 200)));

            Pixel dimmed = pixel.Blend(new Pixel(new PixelBackground(Color.Parse("#7F000000"))));
            Color effective = dimmed.Background.EffectiveColor;
            Assert.That(effective.A, Is.EqualTo(0xFF));
            Assert.That(effective.R, Is.InRange(90, 110), "black at half alpha over gray 200");

            Assert.That(new PixelBackground(Colors.Blue).EffectiveColor, Is.EqualTo(Colors.Blue),
                "a plain cell's effective color is its color");
        }

        private static CellImage CreateCellImage(byte gray)
        {
            byte[] bgra = new byte[8 * 16 * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = bgra[i + 1] = bgra[i + 2] = gray;
                bgra[i + 3] = 0xFF;
            }

            return new CellImage(bgra, 8, 16);
        }

        private static Pixel CreateImagePixel(CellImage image)
        {
            return new Pixel(PixelForeground.Space, new PixelBackground(Colors.Transparent, image));
        }

        [Test]
        public void EqualityCaret()
        {
            var pixel = new Pixel(CaretStyle.BlinkingBar);
            var pixel2 = new Pixel(CaretStyle.BlinkingBar);
            Assert.That(pixel.Equals((object)pixel2));
            Assert.That(pixel.Equals(pixel2));
            Assert.That(pixel == pixel2);
        }

        [Test]
        public void InequalityCaret()
        {
            var pixel = new Pixel(CaretStyle.BlinkingBar);
            var pixel2 = new Pixel(CaretStyle.None);
            Assert.That(!pixel.Equals((object)pixel2));
            Assert.That(!pixel.Equals(pixel2));
            Assert.That(pixel != pixel2);
        }

        [Test]
        public void PixelShade()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('a'),
                    Colors.Red,
                    FontWeight.Bold,
                    FontStyle.Italic,
                    TextDecorationLocation.Underline),
                new PixelBackground(Colors.Green));
            Pixel newPixel = pixel.Shade();
            Assert.That(newPixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(newPixel.Foreground.Color, Is.EqualTo(Colors.Red.Shade()));
            Assert.That(newPixel.Background.Color, Is.EqualTo(Colors.Green.Shade()));
            Assert.That(newPixel.Foreground.Weight, Is.EqualTo(FontWeight.Bold));
            Assert.That(newPixel.Foreground.Style, Is.EqualTo(FontStyle.Italic));
            Assert.That(newPixel.Foreground.TextDecoration, Is.EqualTo(TextDecorationLocation.Underline));
        }

        [Test]
        public void PixelBrighten()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('a'),
                    Colors.Red,
                    FontWeight.Bold,
                    FontStyle.Italic,
                    TextDecorationLocation.Underline),
                new PixelBackground(Colors.Green));
            Pixel newPixel = pixel.Brighten();
            Assert.That(newPixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(newPixel.Foreground.Color, Is.EqualTo(Colors.Red.Brighten()));
            Assert.That(newPixel.Background.Color, Is.EqualTo(Colors.Green.Brighten()));
            Assert.That(newPixel.Foreground.Weight, Is.EqualTo(FontWeight.Bold));
            Assert.That(newPixel.Foreground.Style, Is.EqualTo(FontStyle.Italic));
            Assert.That(newPixel.Foreground.TextDecoration, Is.EqualTo(TextDecorationLocation.Underline));
        }

        [Test]
        public void PixelInvert()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('a'),
                    Colors.Red,
                    FontWeight.Bold,
                    FontStyle.Italic,
                    TextDecorationLocation.Underline),
                new PixelBackground(Colors.Green));
            Pixel newPixel = pixel.Invert();
            Assert.That(newPixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(newPixel.Foreground.Color, Is.EqualTo(Colors.Green));
            Assert.That(newPixel.Background.Color, Is.EqualTo(Colors.Red));
            Assert.That(newPixel.Foreground.Weight, Is.EqualTo(FontWeight.Bold));
            Assert.That(newPixel.Foreground.Style, Is.EqualTo(FontStyle.Italic));
            Assert.That(newPixel.Foreground.TextDecoration, Is.EqualTo(TextDecorationLocation.Underline));
        }


        [Test]
        public void BlendTransparentBackground()
        {
            var pixel = new Pixel(new PixelBackground(Colors.Green));
            var pixel2 = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Transparent));
            Pixel newPixel = pixel.Blend(pixel2);
            Assert.That(newPixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(newPixel.Foreground.Color, Is.EqualTo(Colors.Red));
            Assert.That(newPixel.Background.Color, Is.EqualTo(Colors.Green));
        }

        [Test]
        public void BlendColoredBackground()
        {
            var pixel = new Pixel(new PixelBackground(Colors.Green));
            var pixel2 = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            Pixel newPixel = pixel.Blend(pixel2);
            Assert.That(newPixel.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(newPixel.Foreground.Color, Is.EqualTo(Colors.Red));
            Assert.That(newPixel.Background.Color, Is.EqualTo(Colors.Blue));
        }

        [Test]
        public void BlendShadedBackground()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('x'), Colors.Gray),
                new PixelBackground(Colors.White));
            var pixel2 = new Pixel(new PixelBackground(Color.Parse("#7F000000")));
            Pixel newPixel = pixel.Blend(pixel2);
            Assert.True(newPixel.Foreground.Symbol.Character == 'x');
            // foreground should be darker than original
            Assert.True(newPixel.Foreground.Color.R < pixel.Foreground.Color.R &&
                        newPixel.Foreground.Color.G < pixel.Foreground.Color.G &&
                        newPixel.Foreground.Color.B < pixel.Foreground.Color.B);
            // background should be darker than original
            Assert.True(newPixel.Background.Color.R < pixel.Background.Color.R &&
                        newPixel.Background.Color.G < pixel.Background.Color.G &&
                        newPixel.Background.Color.B < pixel.Background.Color.B);
        }

        [Test]
        public void BlendShadedBackground2()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('x'), Colors.Gray),
                new PixelBackground(Colors.Black));
            var pixel2 = new Pixel(new PixelBackground(Color.Parse("#7F000000")));
            Pixel newPixel = pixel.Blend(pixel2);
            Assert.True(newPixel.Foreground.Symbol.Character == 'x');
            // foreground should be darker than original
            Assert.True(newPixel.Foreground.Color.R < pixel.Foreground.Color.R &&
                        newPixel.Foreground.Color.G < pixel.Foreground.Color.G &&
                        newPixel.Foreground.Color.B < pixel.Foreground.Color.B);
            // background should be not lighter than original
            Assert.True(newPixel.Background.Color.R <= pixel.Background.Color.R &&
                        newPixel.Background.Color.G <= pixel.Background.Color.G &&
                        newPixel.Background.Color.B <= pixel.Background.Color.B);
        }

        [Test]
        public void TextBelowSemiTransparentBackgroundIsStillVisible()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('x'), Colors.Gray),
                new PixelBackground(Colors.White));
            var pixel2 = new Pixel(new PixelBackground(Color.Parse("#7F000000")));
            Pixel newPixel = pixel2.Blend(pixel);
            Assert.True(newPixel.Foreground.Symbol.Character == 'x');
        }

        [Test]
        public void TextBelowNoneTransparentNullCharacterIsStillVisible()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('x'), Colors.Gray),
                new PixelBackground(Colors.White));
            var pixel2 = new Pixel(new PixelForeground(new Symbol(char.MinValue), Colors.White),
                new PixelBackground(Color.Parse("#7F000000")));
            Pixel newPixel = pixel2.Blend(pixel);
            Assert.True(newPixel.Foreground.Symbol.Character == 'x');
        }

        [Test]
        public void HashCode()
        {
            var set = new HashSet<Pixel>();
            set.Add(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue)));
            set.Add(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue)));
            Assert.That(set.Count, Is.EqualTo(1));
        }

        [Test]
        public void JsonSerialization()
        {
            var pixel = new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue));
            string json = JsonSerializer.Serialize(pixel);
            var pixel2 = JsonSerializer.Deserialize<Pixel>(json);
            Assert.That(pixel.Equals(pixel2));
        }
    }
}