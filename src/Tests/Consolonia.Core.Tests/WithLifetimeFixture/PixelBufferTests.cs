using System;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;                                                        r
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class PixelBufferTests
    {
        [SetUp]
        public void Setup()
        {
            AvaloniaLocator.CurrentMutable.Bind<IConsoleCapabilities>().ToConstant(new MockConsoleCapabilities() { Capabilities=ConsoleCapabilities.SupportsComplexEmoji});
        }

        private static PixelBuffer CreateBuffer()
        {
            var buffer = new PixelBuffer(4, 5);
            for (ushort y = 0; y < buffer.Height; y++)
            for (ushort x = 0; x < buffer.Width; x++)
                if (x % 3 == 0)
                    buffer[x, y] = new Pixel(new Symbol("👍"), Colors.Blue);
                else if (x % 3 == 2)
                    buffer[x, y] = new Pixel(new Symbol($"{x}"), Colors.White, FontStyle.Italic,
                        FontWeight.Bold, TextDecorationLocation.Underline);

            return buffer;
        }

        private static void AssertBuffer(PixelBuffer buffer)
        {
            Assert.That(buffer.Width == 4);
            Assert.That(buffer.Height == 5);
            for (ushort y = 0; y < buffer.Height; y++)
            for (ushort x = 0; x < buffer.Width; x++)
                switch (x % 3)
                {
                    case 0:
                        Assert.That(buffer[x, y].Foreground.Symbol.Complex == "👍\uFE0F");
                        Assert.That(buffer[x, y].Foreground.Color == Colors.Blue);
                        break;
                    case 1:
                        Assert.That(buffer[x, y] == Pixel.Space);
                        break;
                    case 2:
                        Assert.That(buffer[x, y].Foreground.Symbol.Character == $"{x}"[0]);
                        Assert.That(buffer[x, y].Foreground.Color == Colors.White);
                        Assert.That(buffer[x, y].Foreground.Style == FontStyle.Italic);
                        Assert.That(buffer[x, y].Foreground.Weight == FontWeight.Bold);
                        Assert.That(buffer[x, y].Foreground.TextDecoration == TextDecorationLocation.Underline);
                        break;
                }
        }

        private static void AssertBufferEqual(PixelBuffer buffer1, PixelBuffer buffer2)
        {
            Assert.That(buffer1.Width == buffer2.Width);
            Assert.That(buffer1.Height == buffer2.Height);
            for (ushort y = 0; y < buffer1.Height; y++)
            for (ushort x = 0; x < buffer1.Width; x++)
                Assert.AreEqual(buffer1[x, y], buffer2[x, y]);
        }


        [Test]
        public void Constructor()
        {
            PixelBuffer buffer = CreateBuffer();
            AssertBuffer(buffer);
        }

        [Test]
        public void ConstructorInitializesWithSpace()
        {
            var buffer = new PixelBuffer(4, 3);

            // Verify all pixels are initialized to Space
            for (ushort y = 0; y < buffer.Height; y++)
            for (ushort x = 0; x < buffer.Width; x++)
                Assert.That(buffer[x, y], Is.EqualTo(Pixel.Space));
        }

        [Test]
        public void JsonSerialization()
        {
            PixelBuffer buffer = CreateBuffer();
            string json = JsonSerializer.Serialize(buffer);
            var buffer2 = JsonSerializer.Deserialize<PixelBuffer>(json);
            AssertBufferEqual(buffer, buffer2);
        }

        [Test]
        public void TestSetChar()
        {
            PixelBuffer buffer = CreateBuffer();
            buffer[1, 1] = new Pixel(new Symbol('A'), Colors.Red);
            Assert.That(buffer[1, 1].Foreground.Symbol.Character == 'A');
            Assert.That(buffer[1, 1].Foreground.Color == Colors.Red);
        }

        [Test]
        public void TestSetWideChar()
        {
            var buffer = new PixelBuffer(4, 1);

            // Set a wide character (emoji) at position 0
            buffer[0, 0] = new Pixel(new Symbol("👍"), Colors.Yellow);

            // Verify the wide character is stored at position 0
            Assert.That(buffer[0, 0].Foreground.Symbol.Complex, Is.EqualTo("👍\ufe0f"));
            Assert.That(buffer[0, 0].Foreground.Symbol.Width, Is.EqualTo(2));
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Colors.Yellow));

            // Position 1 should remain unchanged (Space) - no automatic Empty marking
            Assert.That(buffer[1, 0], Is.EqualTo(Pixel.Space));
        }

        [Test]
        public void TestOverwritePixel()
        {
            var buffer = new PixelBuffer(4, 1);

            // Set initial character
            buffer[0, 0] = new Pixel(new Symbol('A'), Colors.Blue);
            Assert.That(buffer[0, 0].Foreground.Symbol.Character, Is.EqualTo('A'));

            // Overwrite with different character
            buffer[0, 0] = new Pixel(new Symbol('B'), Colors.Red);
            Assert.That(buffer[0, 0].Foreground.Symbol.Character, Is.EqualTo('B'));
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Colors.Red));
        }

        [Test]
        public void TestSetMultipleWideChars()
        {
            var buffer = new PixelBuffer(6, 1);

            // Set multiple wide characters
            buffer[0, 0] = new Pixel(new Symbol("👍"), Colors.Yellow);
            buffer[2, 0] = new Pixel(new Symbol("👨"), Colors.Blue);
            buffer[4, 0] = new Pixel(new Symbol("🎉"), Colors.Red);

            // Verify all wide characters are stored correctly
            Assert.That(buffer[0, 0].Foreground.Symbol.Complex, Is.EqualTo("👍\ufe0f"));
            Assert.That(buffer[2, 0].Foreground.Symbol.Complex, Is.EqualTo("👨\ufe0f"));
            Assert.That(buffer[4, 0].Foreground.Symbol.Complex, Is.EqualTo("🎉\ufe0f"));

            // Verify other positions remain Space
            Assert.That(buffer[1, 0], Is.EqualTo(Pixel.Space));
            Assert.That(buffer[3, 0], Is.EqualTo(Pixel.Space));
            Assert.That(buffer[5, 0], Is.EqualTo(Pixel.Space));
        }

        [Test]
        public void TestIndexerWithCoordinate()
        {
            var buffer = new PixelBuffer(4, 3);
            var coord = new PixelBufferCoordinate(2, 1);

            buffer[coord] = new Pixel(new Symbol('X'), Colors.Green);

            Assert.That(buffer[coord].Foreground.Symbol.Character, Is.EqualTo('X'));
            Assert.That(buffer[coord].Foreground.Color, Is.EqualTo(Colors.Green));
        }

        [Test]
        public void TestIndexerWithLinearIndex()
        {
            var buffer = new PixelBuffer(4, 3);

            // Index 5 = x:1, y:1 (row 1, column 1)
            buffer[5] = new Pixel(new Symbol('Z'), Colors.Magenta);

            Assert.That(buffer[1, 1].Foreground.Symbol.Character, Is.EqualTo('Z'));
            Assert.That(buffer[1, 1].Foreground.Color, Is.EqualTo(Colors.Magenta));
        }

        [Test]
        public void TestBufferBounds()
        {
            var buffer = new PixelBuffer(10, 20);

            Assert.That(buffer.Width, Is.EqualTo(10));
            Assert.That(buffer.Height, Is.EqualTo(20));
            Assert.That(buffer.Length, Is.EqualTo(200));
            Assert.That(buffer.Size.Width, Is.EqualTo(10));
            Assert.That(buffer.Size.Height, Is.EqualTo(20));
        }

        [Test]
        public void TestComplexEmoji()
        {
            var buffer = new PixelBuffer(4, 1);

            // Set a complex multi-character emoji
            buffer[0, 0] = new Pixel(new Symbol("👨‍👩‍👧‍👦"), Colors.Purple);

            // Verify it's stored correctly
            Assert.That(buffer[0, 0].Foreground.Symbol.Complex, Is.EqualTo("👨‍👩‍👧‍👦\uFE0F"));
            Assert.That(buffer[0, 0].Foreground.Symbol.Width, Is.EqualTo(2));
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Colors.Purple));
        }

        [Test]
        public void TestSetPixelWithBackground()
        {
            var buffer = new PixelBuffer(4, 1);

            var pixel = new Pixel(
                new PixelForeground(new Symbol('A'), Colors.White),
                new PixelBackground(Colors.Blue));

            buffer[0, 0] = pixel;

            Assert.That(buffer[0, 0].Foreground.Symbol.Character, Is.EqualTo('A'));
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Colors.White));
            Assert.That(buffer[0, 0].Background.Color, Is.EqualTo(Colors.Blue));
        }

        [Test]
        public void TestPrintBuffer()
        {
            var buffer = new PixelBuffer(3, 2);
            buffer[0, 0] = new Pixel(new Symbol('A'), Colors.White);
            buffer[1, 0] = new Pixel(new Symbol('B'), Colors.White);
            buffer[2, 0] = new Pixel(new Symbol('C'), Colors.White);
            buffer[0, 1] = new Pixel(new Symbol('D'), Colors.White);
            buffer[1, 1] = new Pixel(new Symbol('E'), Colors.White);
            // Last position (2,1) should not be printed

            string output = buffer.PrintBuffer();

            Assert.That(output.Contains("ABC", StringComparison.Ordinal));
            Assert.That(output.Contains("DE", StringComparison.Ordinal));
        }
    }
}