using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.AnsiArt;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class AnsiParserTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Test]
        public void CheckDpIinAvaloniaHardcodedTo96()
        {
            var originalPoint = new Point(1, 1);
            PixelPoint fromPointWithDpi = PixelPoint.FromPointWithDpi(originalPoint, RenderTarget.AvaloniaHardcodedDpi);
            Assert.That(fromPointWithDpi.X, Is.EqualTo(1));
            Assert.That(fromPointWithDpi.Y, Is.EqualTo(1));
        }

        [Test]
        public void ParseSgrColorReturnsCorrectPixels()
        {
            // \x1B[31mRed\x1B[m
            const string ansi = "\x1B[31mRed\x1B[0m";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));

            PixelBuffer buffer = AnsiParser.Parse(stream);

            Assert.That(buffer.Width, Is.EqualTo(3));
            Assert.That(buffer[0, 0].Foreground.Symbol.GetText(), Is.EqualTo("R"));

            // Our Red is Color.FromRgb(255, 0, 0), but standard ANSI 31 is often Maroon (128, 0, 0)
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Color.FromRgb(128, 0, 0)));
        }

        [Test]
        public void ParsePlainCharsReturnsCorrectPixels()
        {
            const string ansi = "AB";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer.Width, Is.EqualTo(2));
        }

        [Test]
        public void ParseCursorMoveReturnsCorrectPixels()
        {
            const string ansi = "Z\x1B[1;2HX";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));

            PixelBuffer buffer = AnsiParser.Parse(stream);

            Assert.That(buffer.Width, Is.EqualTo(2));
            Assert.That(buffer[0, 0].Foreground.Symbol.GetText(), Is.EqualTo("Z"));
            Assert.That(buffer[1, 0].Foreground.Symbol.GetText(), Is.EqualTo("X"));
        }

        [Test]
        public void ParseBackgroundDefaultsToBlack()
        {
            const string ansi = "A";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer[0, 0].Background.Color, Is.EqualTo(Colors.Black));
        }

        [Test]
        public void ParseEraseLineClearsCorrectly()
        {
            const string ansi = "ABC\x1B[1G\x1B[K";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer.Width, Is.EqualTo(1));
        }

        [Test]
        public void ParseAutoWrapMovesToNextLine()
        {
            // Default wrap width is 80 columns (standard terminal width).
            StringBuilder sb = new();
            sb.Append(new string('A', 80));
            sb.Append('B');

            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(sb.ToString()));
            PixelBuffer buffer = AnsiParser.Parse(stream);

            Assert.That(buffer.Width, Is.EqualTo(80));
            Assert.That(buffer.Height, Is.AtLeast(2));
            Assert.That(buffer[0, 1].Foreground.Symbol.GetText(), Is.EqualTo("B"));
        }

        [Test]
        public void ParseCursorForwardCorrectPosition()
        {
            // AB[5CXY => A@0, B@1, cursor moves forward 5 to col 7, X@7, Y@8
            const string ansi = "AB\x1B[5CXY";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);

            Assert.That(buffer[0, 0].Foreground.Symbol.GetText(), Is.EqualTo("A"), "A at 0");
            Assert.That(buffer[1, 0].Foreground.Symbol.GetText(), Is.EqualTo("B"), "B at 1");
            Assert.That(buffer[7, 0].Foreground.Symbol.GetText(), Is.EqualTo("X"),
                $"X at 7, got '{buffer[7, 0].Foreground.Symbol.GetText()}', width={buffer.Width}");
            Assert.That(buffer[8, 0].Foreground.Symbol.GetText(), Is.EqualTo("Y"), "Y at 8");
        }

        [Test]
        public void ParseMultipleSgrNoCharLoss()
        {
            // Test that SGR sequences don't eat adjacent characters
            // A[0m[40m B  => A@0, space@1, B@2... but with SGR between
            string ansi = "A\x1B[0m\x1B[40m B";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);

            Console.WriteLine($"[DEBUG_LOG] MultipleSgr buffer: {buffer.Width}x{buffer.Height}");
            for (int x = 0; x < buffer.Width; x++)
            {
                string t = buffer[(ushort)x, 0].Foreground.Symbol.GetText();
                Console.WriteLine($"[DEBUG_LOG] x={x}: '{t}'");
            }

            Assert.That(buffer.Width, Is.EqualTo(3), "Buffer should have 3 chars: A, space, B");
        }

        [Test]
        public void ParseSgrThenCursorForwardCorrectPosition()
        {
            // Simulate pattern from ANS file: SGR then CSI C
            const string ansi = "\x1B[0m\x1B[40m        \x1B[8C";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer unused = AnsiParser.Parse(stream);

            // 8 spaces = cursor at 8, then CSI 8C = cursor at 16
            // Buffer should be at least 16 wide (all spaces/empty)
            // The key test: cursor should be at position 16
            // Add a char after to verify
            const string ansi2 = "\x1B[0m\x1B[40m        \x1B[8CX";
            using var stream2 = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi2));
            PixelBuffer buffer2 = AnsiParser.Parse(stream2);

            Assert.That(buffer2[16, 0].Foreground.Symbol.GetText(), Is.EqualTo("X"),
                $"X should be at 16, width={buffer2.Width}");
        }
    }
}