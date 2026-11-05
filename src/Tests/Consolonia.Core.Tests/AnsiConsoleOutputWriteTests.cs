using System;
using System.IO;
using System.Text;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>
    ///     How AnsiConsoleOutput gets a frame out: text and image bytes through Console.Out in the order
    ///     written, as the UTF-8 the terminal receives, in a synchronized update when the terminal has one.
    /// </summary>
    [TestFixture]
    [NonParallelizable] // Console.Out is process-wide
    public sealed class AnsiConsoleOutputWriteTests : IDisposable
    {
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

        private static readonly PixelBuffer Screen = new(1, 1);

        private TextWriter _originalOut;
        private MemoryStream _stream;
        private AnsiConsoleOutput _output;

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
        public void BeginUpdateToEndUpdateIsOneSynchronizedUpdateWhenTheTerminalHasThem()
        {
            _output.RedirectOutput(_stream, ConsoleCapabilities.SupportsSynchronizedOutput);

            _output.BeginUpdate();
            _output.WriteText("a");
            _output.WriteBytes("\u001b_Gb\u001b\\"u8);
            _output.WriteText("c");
            _output.EndUpdate(Screen);
            _output.Flush();
            _output.BeginUpdate();
            _output.WriteText("d");
            _output.EndUpdate(Screen);
            _output.WriteText("e");
            _output.Flush();

            Assert.That(Encoding.UTF8.GetString(Written()), Is.EqualTo(
                Esc.BeginSynchronizedUpdate + "a\u001b_Gb\u001b\\c" + Esc.EndSynchronizedUpdate +
                Esc.BeginSynchronizedUpdate + "d" + Esc.EndSynchronizedUpdate + "e"));
        }

        [Test]
        public void AnImagePayloadLongerThanAChunkIsWrittenWhole()
        {
            byte[] payload = new byte[10000];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)('?' + i % 64);

            _output.WriteBytes(payload);
            _output.Flush();

            Assert.That(Written(), Is.EqualTo(payload));
        }

        [Test]
        public void AnEmptyFrameWritesNothing()
        {
            _output.Flush();

            Assert.That(Written(), Is.Empty);
        }
    }
}