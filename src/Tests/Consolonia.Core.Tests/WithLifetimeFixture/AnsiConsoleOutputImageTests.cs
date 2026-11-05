using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     What AnsiConsoleOutput does with the cells of the pixel buffer that show a picture: with an
    ///     encoder from the <see cref="IConsoleImageEncoderFactory" /> it is given, the output writes the text
    ///     over the pictures and the encoder renders them at the end of the frame's update; without one, a
    ///     picture is block glyphs.
    /// </summary>
    [TestFixture]
    [NonParallelizable] // Console.Out and the locator are process-wide
    public sealed class AnsiConsoleOutputImageTests : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _originalOut = Console.Out;
            _factory = new RecordingEncoderFactory();
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Creates = false;
            Console.SetOut(_originalOut);
            Dispose();
        }

        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int Columns = 16;
        private const int Rows = 8;

        private TextWriter _originalOut;
        private RecordingEncoderFactory _factory;
        private MemoryStream _stream;
        private AnsiConsoleOutput _output;
        private PixelBuffer _screen;

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        private void Open(ConsoleCapabilities capabilities, bool withEncoder)
        {
            _factory.Creates = withEncoder;
            _stream = new MemoryStream();
            _output = new AnsiConsoleOutput { Size = new PixelBufferSize(Columns, Rows) };
            _output.RedirectOutput(_stream, capabilities);
            _output.SetImageEncoderFactory(_factory);
            _screen = new PixelBuffer(Columns, Rows);
        }

        /// <summary>A frame as the render target writes one: the given cells changed, then the frame ends.</summary>
        private string Frame(params (int X, int Y, Pixel Pixel)[] changed)
        {
            Clear();
            _output.BeginUpdate();
            foreach ((int x, int y, Pixel pixel) in changed)
            {
                _screen[(ushort)x, (ushort)y] = pixel;
                _output.WritePixel(new PixelBufferCoordinate((ushort)x, (ushort)y), in pixel);
            }

            _output.EndUpdate(_screen);
            return Written();
        }

        /// <summary>Forgets what was written so far, buffered or not.</summary>
        private void Clear()
        {
            _output.Flush();
            _stream.SetLength(0);
        }

        private string Written()
        {
            return Encoding.ASCII.GetString(_stream.ToArray());
        }

        private static PixelImage SolidImage(byte red, byte green, byte blue)
        {
            byte[] bgra = new byte[CellWidth * CellHeight * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = blue;
                bgra[i + 1] = green;
                bgra[i + 2] = red;
                bgra[i + 3] = 0xFF;
            }

            return new PixelImage(bgra, CellWidth, CellHeight);
        }

        private static Pixel ImageCell(PixelImage image, Color paint = default)
        {
            return new Pixel(PixelImageBitmapConverter.PictureCellForeground, new PixelBackground(paint, image));
        }

        private static Pixel TextCell(char character)
        {
            return new Pixel(new PixelForeground(new Symbol(character), Colors.White),
                new PixelBackground(Colors.Black));
        }

        [Test]
        public void TheFactoryIsAskedWithTheOutputAndItsCapabilities()
        {
            Open(ConsoleCapabilities.SupportsSixel, true);

            Assert.That(_factory.CreatedFor, Is.SameAs(_output));
            Assert.That(_factory.CapabilitiesSeen, Is.EqualTo(ConsoleCapabilities.SupportsSixel));
        }

        [Test]
        public void WithAnEncoderTheConsoleShowsPixelImages()
        {
            Open(ConsoleCapabilities.None, true);

            Assert.That(_output.Capabilities.HasFlag(ConsoleCapabilities.SupportsPixelImages), Is.True);
        }

        [Test]
        public void WithoutAnEncoderTheConsoleDoesNotShowPixelImages()
        {
            Open(ConsoleCapabilities.SupportsSixel, false);

            Assert.That(_output.Capabilities.HasFlag(ConsoleCapabilities.SupportsPixelImages), Is.False);
        }

        [Test]
        public void TheEncoderRendersTheImagesOnceAFrameWithTheScreen()
        {
            Open(ConsoleCapabilities.None, true);

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))), (1, 0, TextCell('x')),
                (2, 0, ImageCell(SolidImage(40, 50, 60))));

            Assert.That(_factory.Encoder.Calls, Is.EqualTo(new[] { "WriteImages" }));
            Assert.That(_factory.Encoder.Screen, Is.SameAs(_screen));
            Assert.That(frame, Does.Contain("x"), "the text cell is written as text");
        }

        [Test]
        public void WithTextOverlayAGlyphOverAPictureIsOnTheDefaultBackground()
        {
            Open(ConsoleCapabilities.None, true);
            _factory.Encoder.SupportsTextOverlay = true;
            Pixel glyph = ImageCell(SolidImage(10, 20, 30))
                .Blend(new Pixel(new PixelForeground(new Symbol('x'), Colors.White)));

            string frame = Frame((0, 0, glyph));

            Assert.That(frame, Does.Contain(Esc.DefaultBackground + "x"));
            Assert.That(frame, Does.Not.Contain("48;2;"), "no explicit background to hide the picture");
        }

        [Test]
        public void WithTextOverlayAPictureCellWithNoGlyphIsASpaceOnTheDefaultBackground()
        {
            Open(ConsoleCapabilities.None, true);
            _factory.Encoder.SupportsTextOverlay = true;

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            Assert.That(frame, Does.Contain(Esc.DefaultBackground + " "));
        }

        [Test]
        public void WithoutTextOverlayAGlyphOverAPictureIsOnThePicturesColor()
        {
            Open(ConsoleCapabilities.None, true);
            Pixel glyph = ImageCell(SolidImage(10, 20, 30))
                .Blend(new Pixel(new PixelForeground(new Symbol('x'), Colors.White)));

            string frame = Frame((0, 0, glyph));

            Assert.That(frame, Does.Contain("\u001b[48;2;10;20;30m"));
            Assert.That(frame, Does.Contain("x"));
        }

        [Test]
        public void WithoutTextOverlayAPictureCellWithNoGlyphIsLeftToThePicture()
        {
            Open(ConsoleCapabilities.None, true);

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            Assert.That(frame, Is.EqualTo(RecordingEncoder.Payload + Esc.SetCursorPosition(0, 0)),
                "only the encoder's payload, and the cursor put back after it");
        }

        [Test]
        public void AfterBytesTheCursorIsMovedEvenToWhereItWas()
        {
            Open(ConsoleCapabilities.None, false);
            _output.SetCaretPosition(new PixelBufferCoordinate(3, 2));
            _output.WriteBytes(Encoding.ASCII.GetBytes(RecordingEncoder.Payload));
            Clear();

            _output.SetCaretPosition(new PixelBufferCoordinate(3, 2));
            _output.Flush();

            Assert.That(Written(), Is.EqualTo(Esc.SetCursorPosition(3, 2)));
        }

        /// <summary>
        ///     After a glyph in the last column the terminal's cursor is still on that row, waiting to wrap:
        ///     the next character goes to the next row on its own, but anything else needs the move.
        /// </summary>
        [Test]
        public void AfterTheLastColumnTextNeedsNoMoveButTheCaretDoes()
        {
            Open(ConsoleCapabilities.None, false);
            _output.WritePixel(new PixelBufferCoordinate(Columns - 1, 0), TextCell('a'));
            Clear();

            _output.WritePixel(new PixelBufferCoordinate(0, 1), TextCell('b'));
            _output.Flush();
            Assert.That(Written(), Is.EqualTo("b"));

            _output.WritePixel(new PixelBufferCoordinate(Columns - 1, 1), TextCell('c'));
            Clear();
            _output.SetCaretPosition(new PixelBufferCoordinate(0, 2));
            _output.Flush();
            Assert.That(Written(), Is.EqualTo(Esc.SetCursorPosition(0, 2)));
        }

        [Test]
        public void TheEncoderWritesInsideTheSynchronizedUpdate()
        {
            Open(ConsoleCapabilities.SupportsSynchronizedOutput, true);

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            Assert.That(frame, Does.StartWith(Esc.BeginSynchronizedUpdate));
            Assert.That(frame, Does.EndWith(Esc.EndSynchronizedUpdate));
            Assert.That(frame, Does.Contain(RecordingEncoder.Payload));
        }

        [Test]
        public void TheCursorGoesBackToTheCaretAfterTheEncoderMovesIt()
        {
            Open(ConsoleCapabilities.None, true);
            _factory.Encoder.MovesTo = new PixelBufferCoordinate(9, 6);

            _output.BeginUpdate();
            _output.SetCaretPosition(new PixelBufferCoordinate(3, 2));
            Clear();
            _output.EndUpdate(_screen);

            Assert.That(Written(), Does.EndWith(Esc.SetCursorPosition(3, 2)));
        }

        [Test]
        public void AnEncoderThatThrowsStillLeavesTheUpdateClosed()
        {
            Open(ConsoleCapabilities.SupportsSynchronizedOutput, true);
            _factory.Encoder.Throws = true;

            _output.BeginUpdate();
            Assert.Throws<InvalidOperationException>(() => _output.EndUpdate(_screen));

            Assert.That(Written(), Does.EndWith(Esc.EndSynchronizedUpdate));
        }

        [Test]
        public void TheEncoderIsResetWithTheCacheAndRestoredWithTheConsole()
        {
            Open(ConsoleCapabilities.None, true);

            _output.ResetCache();
            _output.RestoreConsole();

            Assert.That(_factory.Encoder.Calls, Is.EqualTo(new[] { "Reset", "RestoreConsole", "Reset" }));
        }

        /// <summary>
        ///     A picture cell with no symbol is the picture, and one with any symbol is text, a space as
        ///     much as a glyph.
        /// </summary>
        [Test]
        public void WithoutAnEncoderAPictureCellWithNoSymbolIsABlockGlyphAndASpaceOverItIsText()
        {
            Open(ConsoleCapabilities.None, false);
            Pixel picture = ImageCell(SolidImage(10, 20, 30));
            Pixel spaceOverPicture = picture.Blend(new Pixel(new PixelForeground(Symbol.Space, Colors.White)));

            string asPicture = Frame((0, 0, picture));
            string asText = Frame((0, 0, spaceOverPicture));

            Assert.That(asText, Does.EndWith(" "), "the space is written");
            Assert.That(asPicture, Does.Not.Contain(" "), "the picture's block glyph, not a space");
        }

        /// <summary>
        ///     A modal's translucent backdrop blended over a picture cell, as the converter makes it, colors
        ///     the cell's foreground but draws no symbol on it: the cell is still the picture, dimmed.
        /// </summary>
        [Test]
        public void WithoutAnEncoderABackdropBlendedOverAPictureCellLeavesThePicture()
        {
            Open(ConsoleCapabilities.None, false);
            Pixel dimmed = ImageCell(SolidImage(10, 20, 30))
                .Blend(new Pixel(new PixelBackground(Color.Parse("#7F000000"))));

            string frame = Frame((0, 0, dimmed));

            Assert.That(frame, Does.Not.Contain(" "), "still the picture's block glyph");
        }

        [Test]
        public void WithoutAnEncoderAPictureIsBlockGlyphs()
        {
            Open(ConsoleCapabilities.SupportsSixel, false);

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            Assert.That(frame, Does.Contain("\u001b[48;2;10;20;30m"), "the picture's color as the cell's");
        }

        private sealed class RecordingEncoderFactory : IConsoleImageEncoderFactory
        {
            public bool Creates { get; set; }

            public RecordingEncoder Encoder { get; private set; }

            public IConsoleOutput CreatedFor { get; private set; }

            public ConsoleCapabilities CapabilitiesSeen { get; private set; }

            public IConsoleImageEncoder CreateEncoder(IConsoleOutput output)
            {
                CreatedFor = output;
                CapabilitiesSeen = output.Capabilities;
                Encoder = Creates ? new RecordingEncoder(output) : null;
                return Encoder;
            }
        }

        private sealed class RecordingEncoder(IConsoleOutput output) : IConsoleImageEncoder
        {
            public const string Payload = "\u001b_Gpicture\u001b\\";

            public List<string> Calls { get; } = new();

            public PixelBufferCoordinate? MovesTo { get; set; }

            public bool Throws { get; set; }

            public PixelBuffer Screen { get; private set; }

            public bool SupportsTextOverlay { get; set; }

            public void WriteImages(PixelBuffer screen)
            {
                Calls.Add(nameof(WriteImages));
                Screen = screen;
                if (Throws)
                    throw new InvalidOperationException("the encoder failed");
                if (MovesTo is { } position)
                    output.SetCaretPosition(position);
                output.WriteBytes(Encoding.ASCII.GetBytes(Payload));
            }

            public void Reset()
            {
                Calls.Add(nameof(Reset));
            }

            public void RestoreConsole()
            {
                Calls.Add(nameof(RestoreConsole));
            }
        }
    }
}
