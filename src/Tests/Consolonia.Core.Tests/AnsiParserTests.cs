using System.IO;
using System.Text;
using Avalonia.Media;
using Consolonia.Core.Drawing;
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
        public void Parse_SgrColor_ReturnsCorrectPixels()
        {
            // \x1B[31mRed\x1B[m
            string ansi = "\x1B[31mRed\x1B[0m";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            
            PixelBuffer buffer = AnsiParser.Parse(stream);
            
            Assert.That(buffer.Width, Is.EqualTo(3));
            Assert.That(buffer[0, 0].Foreground.Symbol.GetText(), Is.EqualTo("R"));
            
            // Our Red is Color.FromRgb(255, 0, 0), but standard ANSI 31 is often Maroon (128, 0, 0)
            Assert.That(buffer[0, 0].Foreground.Color, Is.EqualTo(Color.FromRgb(128, 0, 0)));
        }

        [Test]
        public void Parse_PlainChars_ReturnsCorrectPixels()
        {
            string ansi = "AB";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer.Width, Is.EqualTo(2));
        }

        [Test]
        public void Parse_CursorMove_ReturnsCorrectPixels()
        {
            // Z\x1B[1;2HX
            string ansi = "Z\x1B[1;2HX";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            
            PixelBuffer buffer = AnsiParser.Parse(stream);
            
            Assert.That(buffer.Width, Is.EqualTo(2));
            Assert.That(buffer[0, 0].Foreground.Symbol.GetText(), Is.EqualTo("Z"));
            Assert.That(buffer[1, 0].Foreground.Symbol.GetText(), Is.EqualTo("X"));
        }

        [Test]
        public void Parse_Background_DefaultsToBlack()
        {
            string ansi = "A";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer[0, 0].Background.Color, Is.EqualTo(Colors.Black));
        }

        [Test]
        public void Parse_EraseLine_ClearsCorrectly()
        {
            string ansi = "ABC\x1B[1G\x1B[K";
            using var stream = new MemoryStream(Encoding.GetEncoding(437).GetBytes(ansi));
            PixelBuffer buffer = AnsiParser.Parse(stream);
            Assert.That(buffer.Width, Is.EqualTo(1));
        }
    }
}
