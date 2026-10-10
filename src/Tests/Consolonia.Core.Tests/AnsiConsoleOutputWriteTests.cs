using System;
using System.IO;
using System.Text;
using Consolonia.Core.Drawing;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     How AnsiConsoleOutput gets a frame out: text and image bytes in the order written, as the UTF-8
    ///     the terminal receives, in one write to the output stream, or as text to Console.Out when that
    ///     was redirected.
    /// </summary>
    [TestFixture]
    [NonParallelizable] // Console.Out is process-wide
    public sealed class AnsiConsoleOutputWriteTests : IDisposable
    {
        private TextWriter _originalOut;
        private MemoryStream _stream;
        private AnsiConsoleOutput _output;

        [SetUp]
        public void Setup()
        {
            _originalOut = Console.Out;
            _stream = new MemoryStream();
            _output = new AnsiConsoleOutput();
            _output.RedirectOutput(_stream);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(_originalOut);
            Dispose();
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        private byte[] Written()
        {
            return _stream.ToArray();
        }

        [Test]
        public void TextAndBytesGoOutInTheOrderWrittenAsUtf8()
        {
            _output.WriteText("a");
            _output.WriteBytes("\u001b_Gb\u001b\\"u8);
            _output.WriteText("é漢");
            _output.Flush();

            Assert.That(Written(), Is.EqualTo(Encoding.UTF8.GetBytes("a\u001b_Gb\u001b\\é漢")));
        }

        [Test]
        public void ASurrogatePairSplitAcrossTwoWritesIsOneCharacter()
        {
            _output.WriteText("\uD83D");
            _output.WriteText("\uDE00");
            _output.Flush();

            Assert.That(Written(), Is.EqualTo(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }));
        }

        [Test]
        public void ALoneHighSurrogateBeforeBytesBecomesAReplacementCharacter()
        {
            _output.WriteText("\uD83D");
            _output.WriteBytes("x"u8);
            _output.Flush();

            Assert.That(Written(), Is.EqualTo(new byte[] { 0xEF, 0xBF, 0xBD, (byte)'x' }));
        }

        [Test]
        public void TextWrittenStraightToConsoleOutComesBeforeTheFrame()
        {
            Console.Out.Write("first ");
            _output.WriteText("frame");
            _output.Flush();

            Assert.That(Encoding.UTF8.GetString(Written()), Is.EqualTo("first frame"));
        }

        [Test]
        public void AConsoleOutRedirectedAfterwardsGetsTheFrameAsText()
        {
            var redirected = new StringWriter();
            Console.SetOut(redirected);

            _output.WriteText("é");
            _output.WriteBytes("\u001b_G\u001b\\"u8);
            _output.Flush();

            Assert.That(redirected.ToString(), Is.EqualTo("é\u001b_G\u001b\\"));
            Assert.That(Written(), Is.Empty);
        }

        [Test]
        public void AKittyTransmitIsWrittenAsTheSameBytesItIsBuiltAs()
        {
            byte[] data = new byte[10000]; // three chunks, the last one short
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i * 31);

            KittyGraphics.WriteTransmitSequence(_output, 9, 50, 40, data, KittyImageFormat.Rgba, true);
            _output.Flush();

            Assert.That(Encoding.ASCII.GetString(Written()),
                Is.EqualTo(KittyGraphics.BuildTransmitSequence(9, 50, 40, data, KittyImageFormat.Rgba, true)));
        }

        [Test]
        public void AnEmptyFrameWritesNothing()
        {
            _output.Flush();

            Assert.That(Written(), Is.Empty);
        }
    }
}
