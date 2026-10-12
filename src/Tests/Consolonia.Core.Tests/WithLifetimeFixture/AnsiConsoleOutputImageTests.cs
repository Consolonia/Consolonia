using System;
using System.IO;
using System.Text;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    /// <summary>
    ///     What AnsiConsoleOutput sends for the cells of the pixel buffer that show a picture, per image
    ///     protocol: the render target writes the changed cells and ends the frame, and the output's
    ///     encoder decides what the terminal gets.
    /// </summary>
    [TestFixture]
    [NonParallelizable] // Console.Out is process-wide
    public sealed class AnsiConsoleOutputImageTests : IDisposable
    {
        [SetUp]
        public void Setup()
        {
            _originalOut = Console.Out;
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(_originalOut);
            Dispose();
        }

        private const int CellWidth = 8;
        private const int CellHeight = 16;
        private const int Columns = 16;
        private const int Rows = 8;

        private TextWriter _originalOut;
        private MemoryStream _stream;
        private AnsiConsoleOutput _output;
        private PixelBuffer _screen;

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        private void Open(ConsoleCapabilities protocol)
        {
            _stream = new MemoryStream();
            _output = new AnsiConsoleOutput { Size = new PixelBufferSize(Columns, Rows) };
            _output.RedirectOutput(_stream, protocol);
            _screen = new PixelBuffer(Columns, Rows);
        }

        /// <summary>A frame as the render target writes one: the given cells changed, then the frame ends.</summary>
        private string Frame(params (int X, int Y, Pixel Pixel)[] changed)
        {
            _stream.SetLength(0);
            _output.BeginFrame();
            foreach ((int x, int y, Pixel pixel) in changed)
            {
                _screen[(ushort)x, (ushort)y] = pixel;
                _output.WritePixel(new PixelBufferCoordinate((ushort)x, (ushort)y), in pixel);
            }

            _output.EndFrame(_screen);
            _output.Flush();
            return Encoding.ASCII.GetString(_stream.ToArray());
        }

        private static CellImage SolidImage(byte red, byte green, byte blue)
        {
            byte[] bgra = new byte[CellWidth * CellHeight * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = blue;
                bgra[i + 1] = green;
                bgra[i + 2] = red;
                bgra[i + 3] = 0xFF;
            }

            return new CellImage(bgra, CellWidth, CellHeight);
        }

        private static Pixel ImageCell(CellImage image, Color paint = default)
        {
            return new Pixel(PixelForeground.Space, new PixelBackground(paint, image));
        }

        private static int Count(string frame, string sequence)
        {
            int count = 0;
            for (int found = frame.IndexOf(sequence, StringComparison.Ordinal);
                 found >= 0;
                 found = frame.IndexOf(sequence, found + sequence.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private const string SixelStart = "\u001bPq";
        private const string KittyTransmit = "\u001b_Ga=t,";
        private const string KittyPlace = "\u001b_Ga=p,";
        private const string KittyDeletePlacement = "\u001b_Ga=d,d=i,";

        [Test]
        public void SixelNeighboursOnARowAreOneSixel()
        {
            Open(ConsoleCapabilities.SupportsSixel);
            CellImage image = SolidImage(10, 20, 30);

            string frame = Frame((3, 2, ImageCell(image)), (4, 2, ImageCell(image)), (5, 2, ImageCell(image)));

            Assert.That(Count(frame, SixelStart), Is.EqualTo(1));
            Assert.That(frame, Does.Contain(Esc.SetCursorPosition(3, 2)), "placed at the run's first cell");
            Assert.That(frame, Does.Contain(Esc.SetCursorPosition(6, 2)), "the cursor moved past the run");
        }

        [Test]
        public void SixelCellsApartAreTwoSixels()
        {
            Open(ConsoleCapabilities.SupportsSixel);
            CellImage image = SolidImage(10, 20, 30);

            string frame = Frame((3, 2, ImageCell(image)), (5, 2, ImageCell(image)), (3, 3, ImageCell(image)));

            Assert.That(Count(frame, SixelStart), Is.EqualTo(3));
        }

        [Test]
        public void SixelCannotShowAGlyphOverAPictureSoItIsTextOnTheDominantColor()
        {
            Open(ConsoleCapabilities.SupportsSixel);
            var glyphOverImage = new Pixel(new PixelForeground(new Symbol('a'), Colors.White),
                new PixelBackground(Colors.Transparent, SolidImage(10, 20, 30)));

            string frame = Frame((0, 0, glyphOverImage));

            Assert.That(Count(frame, SixelStart), Is.Zero);
            Assert.That(frame, Does.Contain("a"));
            Assert.That(frame, Does.Contain("\u001b[48;2;10;20;30m"), "the glyph sits on the picture's color");
        }

        [Test]
        public void SixelRunsSentBeforeComeFromTheCache()
        {
            Open(ConsoleCapabilities.SupportsSixel);
            CellImage image = SolidImage(10, 20, 30);
            var paint = Color.Parse("#7F000000");

            string shown = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));
            string dimmed = Frame((0, 0, ImageCell(image, paint)), (1, 0, ImageCell(image, paint)));
            string shownAgain = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));

            Assert.That(dimmed, Is.Not.EqualTo(shown), "the paint darkens the picture");
            Assert.That(dimmed, Does.Contain("#0;2;2;4;6"), "half black over (10,20,30) in sixel percent");
            Assert.That(shownAgain, Is.EqualTo(shown), "the un-dimmed run is the bytes sent before");
        }

        /// <summary>
        ///     A translucent overlay blended over a picture cell (a modal's backdrop) leaves the cell's space
        ///     with the overlay's color as its foreground. A space draws nothing, so the cell is still a
        ///     picture, dimmed, not text on the picture's color.
        /// </summary>
        [Test]
        public void SixelDimmedByAnOverlayBlendIsStillASixel()
        {
            Open(ConsoleCapabilities.SupportsSixel);
            CellImage image = SolidImage(10, 20, 30);
            Pixel dimmed = ImageCell(image).Blend(new Pixel(new PixelBackground(Color.Parse("#7F767676"))));
            Assert.That(dimmed.Background.Image, Is.SameAs(image));

            string frame = Frame((0, 0, dimmed));

            Assert.That(Count(frame, SixelStart), Is.EqualTo(1), "a dimmed picture cell is still a picture");
            Assert.That(frame, Does.Contain("#0;2;25;27;29"), "half grey (118) over (10,20,30) in sixel percent");
        }

        [Test]
        public void KittyBlockOfCellsIsOneImageAndOnePlacement()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            var cells = new (int, int, Pixel)[KittyConsoleImageEncoder.BlockColumns * KittyConsoleImageEncoder.BlockRows];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = (i % KittyConsoleImageEncoder.BlockColumns, i / KittyConsoleImageEncoder.BlockColumns,
                    ImageCell(image));

            string frame = Frame(cells);

            Assert.That(Count(frame, KittyTransmit), Is.EqualTo(1));
            Assert.That(frame, Does.Contain("a=t,f=32,o=z,"), "RGBA, zlib compressed");
            Assert.That(Count(frame, KittyPlace), Is.EqualTo(1));
            Assert.That(frame, Does.Contain("z=-2,"), "placed below the text");
            Assert.That(Count(frame, Esc.DefaultBackground), Is.EqualTo(1),
                "the cells are written on the default background so the placement shows through");
        }

        /// <summary>
        ///     A placement below the text is drawn above the cells' background colors, so a text cell must
        ///     never be under one: a dialog over a picture would show the picture through its body. Only
        ///     the rectangles of image cells of a block are placed, as crops of the block's image.
        /// </summary>
        [Test]
        public void KittyPlacesOnlyTheImageCellsOfABlock()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            var text = new Pixel(new PixelForeground(new Symbol('x'), Colors.White), new PixelBackground(Colors.Blue));

            string frame = Frame((0, 0, ImageCell(image)), (1, 0, text), (2, 0, ImageCell(image)),
                (3, 0, ImageCell(image)));

            Assert.That(Count(frame, KittyTransmit), Is.EqualTo(1), "one block image");
            Assert.That(Count(frame, KittyPlace), Is.EqualTo(2), "the image cells either side of the text cell");
            Assert.That(frame, Does.Contain("x=0,y=0,w=8,h=16"), "the first cell");
            Assert.That(frame, Does.Contain("x=16,y=0,w=16,h=16"), "the two cells after the text cell");
            Assert.That(frame, Does.Contain(Esc.SetCursorPosition(2, 0)), "placed where those cells are");
        }

        [Test]
        public void KittyStrokeReplacesTheBlockItTouched()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)), (9, 0, ImageCell(image)));

            string stroke = Frame((1, 0, ImageCell(SolidImage(200, 0, 0))));

            Assert.That(Count(stroke, KittyTransmit), Is.EqualTo(1), "the touched block is sent anew");
            Assert.That(Count(stroke, KittyPlace), Is.EqualTo(1), "and placed anew");
            Assert.That(Count(stroke, KittyDeletePlacement), Is.EqualTo(1), "its old placement goes");

            string unchanged = Frame();
            Assert.That(unchanged, Does.Not.Contain("\u001b_G"), "a frame that changes nothing sends nothing");
        }

        [Test]
        public void KittyPaintOverAPictureIsAWashPlacement()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            var paint = Color.Parse("#7F000000");
            Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));

            string dimmed = Frame((0, 0, ImageCell(image, paint)), (1, 0, ImageCell(image, paint)));

            Assert.That(Count(dimmed, KittyTransmit), Is.EqualTo(1), "the wash image");
            Assert.That(Count(dimmed, "z=-1,"), Is.EqualTo(1), "placed between the picture and the text");
            Assert.That(dimmed, Does.Contain("w=2,h=1,c=2,r=1"), "over the two painted cells");

            string undimmed = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));
            Assert.That(Count(undimmed, KittyDeletePlacement), Is.EqualTo(1), "the wash placement goes");
            Assert.That(undimmed, Does.Contain("a=d,d=I,"), "and so does the wash image nothing shows");
        }

        [Test]
        public void KittyPlacesEverythingAgainAfterTheCacheIsReset()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            Frame((0, 0, ImageCell(image)));

            _output.ResetCache();
            string again = Frame((0, 0, ImageCell(image)));

            Assert.That(Count(again, KittyTransmit), Is.Zero, "the image is still in the terminal");
            Assert.That(Count(again, KittyPlace), Is.EqualTo(1), "placed again");
            Assert.That(Count(again, KittyDeletePlacement), Is.EqualTo(1), "the old placement deleted after");
            Assert.That(again.IndexOf(KittyPlace, StringComparison.Ordinal),
                Is.LessThan(again.IndexOf(KittyDeletePlacement, StringComparison.Ordinal)),
                "the new placement is there before the old one goes");
        }

        [Test]
        public void WithoutAnImageProtocolAPictureIsBlockGlyphs()
        {
            Open(ConsoleCapabilities.None);

            string frame = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            Assert.That(frame, Does.Not.Contain("\u001b_G"));
            Assert.That(frame, Does.Not.Contain(SixelStart));
            Assert.That(frame, Does.Contain("\u001b[48;2;10;20;30m"), "the picture's color as the cell's");
        }
    }
}
