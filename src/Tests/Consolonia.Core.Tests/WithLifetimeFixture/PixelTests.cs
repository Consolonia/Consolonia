// DUPFINDER_ignore

using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
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

        [Test]
        public void SixelSymbolsCompareByImage()
        {
            // regression: a sixel cell carries no character and no pattern, so symbols which ignored the
            // image compared equal and the pixel buffer diff kept showing the previous picture
            Sixel sixel = CreateCellSixel(10);
            Sixel otherSixel = CreateCellSixel(200);

            Pixel pixel = CreateSixelPixel(sixel);
            Pixel samePixel = CreateSixelPixel(sixel);
            Pixel otherPixel = CreateSixelPixel(otherSixel);

            Assert.That(pixel == samePixel);
            Assert.That(pixel.GetHashCode(), Is.EqualTo(samePixel.GetHashCode()));
            Assert.That(pixel != otherPixel);
            Assert.That(pixel.Equals((object)otherPixel), Is.False);
        }

        [Test]
        public void TranslucentOverlayWashesSixelPalette()
        {
            Sixel sixel = CreateCellSixel(200);
            Pixel pixel = CreateSixelPixel(sixel);
            var backdrop = new Pixel(new PixelBackground(Color.Parse("#7F000000")));

            Pixel dimmed = pixel.Blend(backdrop);
            Sixel washed = dimmed.Foreground.Symbol.Sixel;

            Assert.That(washed, Is.Not.SameAs(sixel));
            Assert.That(washed.Pixels, Is.SameAs(sixel.Pixels), "only the palette changes");
            Assert.That(washed.Palette[0], Is.LessThan(sixel.Palette[0]), "black at half alpha darkens");
            Assert.That(sixel.Palette[0], Is.EqualTo(200), "the source image is untouched");
            Assert.That(pixel.Blend(backdrop), Is.EqualTo(dimmed),
                "the same overlay must yield the same image, else every frame re-sends it");
        }

        [Test]
        public void BlockGlyphTakesATextCellOverSixel()
        {
            // a window edge is text like any glyph; painted into the image, its color had to come
            // from the image's palette and shifted to the nearest image color
            Sixel sixel = CreateCellSixel(200);
            Pixel pixel = CreateSixelPixel(sixel);
            var edge = new Pixel(new PixelForeground(new Symbol('▁'), Color.FromRgb(0x10, 0x20, 0x30)));

            Pixel withEdge = pixel.Blend(edge);

            Assert.That(withEdge.Foreground.Symbol.Sixel, Is.Null);
            Assert.That(withEdge.Foreground.Symbol.Character, Is.EqualTo('▁'));
        }

        [Test]
        public void TextOverSixelTakesDominantColorAsBackground()
        {
            // a mostly gray picture with a sliver of red: gray is the dominant color
            byte[] palette = { 200, 200, 200, 0, 0, 0, 255, 0 };
            byte[] pixels = new byte[8 * 16];
            Array.Fill(pixels, (byte)1, 0, 8);
            var sixel = new Sixel(palette, 2, pixels, 8, 16, 8, 16);
            Pixel pixel = CreateSixelPixel(sixel);

            Assert.That(sixel.DominantColor, Is.EqualTo(Color.FromRgb(200, 200, 200)));

            Pixel withText = pixel.Blend(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red)));
            Assert.That(withText.Foreground.Symbol.Character, Is.EqualTo('a'));
            Assert.That(withText.Background.Color, Is.EqualTo(Color.FromRgb(200, 200, 200)),
                "text without a background sits on the picture's dominant color");

            // an opaque background of its own still wins
            Pixel withOpaqueText = pixel.Blend(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Colors.Blue)));
            Assert.That(withOpaqueText.Background.Color, Is.EqualTo(Colors.Blue));

            // a translucent background is blended over the dominant color, not over transparent
            Pixel withTranslucentText = pixel.Blend(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red),
                new PixelBackground(Color.Parse("#7F000000"))));
            Assert.That(withTranslucentText.Background.Color.A, Is.EqualTo(0xFF));
            Assert.That(withTranslucentText.Background.Color.R, Is.InRange(1, 199), "black at half alpha darkens");

            // a washed image gives the washed dominant color
            Pixel dimmed = pixel.Blend(new Pixel(new PixelBackground(Color.Parse("#7F000000"))));
            Pixel dimmedWithText = dimmed.Blend(new Pixel(new PixelForeground(new Symbol('a'), Colors.Red)));
            Assert.That(dimmedWithText.Background.Color,
                Is.EqualTo(dimmed.Foreground.Symbol.Sixel.DominantColor));
            Assert.That(dimmedWithText.Background.Color.R, Is.LessThan(200));
        }

        [Test]
        public void ShadeAndBrightenWashImageCells()
        {
            Pixel sixelPixel = CreateSixelPixel(CreateCellSixel(128));
            Assert.That(sixelPixel.Shade().Foreground.Symbol.Sixel.Palette[0], Is.LessThan(128));
            Assert.That(sixelPixel.Brighten().Foreground.Symbol.Sixel.Palette[0], Is.GreaterThan(128));

            var tilePixel = new Pixel(new PixelForeground(Symbol.Space, Colors.Transparent),
                new PixelBackground(Colors.Transparent, new KittyTile(0x42, 0, 0)));
            Color shadeWash = tilePixel.Shade().Background.Color;
            Assert.That(shadeWash.A, Is.GreaterThan(0));
            Assert.That(shadeWash.R, Is.EqualTo(0));
        }

        private static Sixel CreateCellSixel(byte gray)
        {
            byte[] palette = { gray, gray, gray, 0 };
            return new Sixel(palette, 1, new byte[8 * 16], 8, 16, 8, 16);
        }

        private static Pixel CreateSixelPixel(Sixel sixel)
        {
            return new Pixel(new PixelForeground(new Symbol(sixel), Colors.Transparent),
                PixelBackground.Transparent);
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
        public void KittyTileBackgroundComposesWithForegroundAndDiesByOpaqueBackground()
        {
            // the "image as cell background" model: cell = (backgroundColor|backgroundImage) +
            // foreground character
            var tile = new KittyTile(0x42, 3, 5);
            var tilePixel = new Pixel(
                new PixelForeground(Symbol.Space, Colors.Transparent),
                new PixelBackground(Colors.Transparent, tile));

            Pixel withGlyph = tilePixel.Blend(new Pixel(
                new PixelForeground(new Symbol('▕'), Colors.Gray),
                PixelBackground.Transparent));
            Assert.That(withGlyph.Foreground.Symbol.Character, Is.EqualTo('▕'));
            Assert.That(withGlyph.Background.Tile, Is.EqualTo(tile),
                "a glyph with transparent background must draw OVER the picture, not evict it");

            Pixel shaded = tilePixel.Blend(new Pixel(new PixelBackground(Color.Parse("#7F000000"))));
            Assert.That(shaded.Background.Tile, Is.EqualTo(tile),
                "a translucent wash must not evict the picture");
            Assert.That(shaded.Background.Color, Is.EqualTo(Color.Parse("#7F000000")),
                "the wash accumulates in the tile cell's color, to be laid over the image");

            Pixel covered = tilePixel.Blend(new Pixel(new PixelBackground(Colors.White)));
            Assert.That(covered.Background.Tile.IsEmpty, Is.True,
                "an opaque background owns the cell: the picture must be evicted");

            Assert.That(tilePixel.Shade().Background.Tile, Is.EqualTo(tile));
            Assert.That(tilePixel.Invert().Background.Tile, Is.EqualTo(tile));
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