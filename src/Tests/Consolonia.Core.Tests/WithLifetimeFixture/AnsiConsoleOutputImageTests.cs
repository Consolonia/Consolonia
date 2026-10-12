using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        ///     A block is sent again under its own image id, which replaces the image and its placements in
        ///     the terminal: a change is a transmit and the new placements, never a delete.
        /// </summary>
        [Test]
        public void KittyStrokeResendsTheBlockItTouchedUnderTheSameId()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            string first = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)), (9, 0, ImageCell(image)));
            string firstBlockId = ImageIdOfFirstTransmit(first);

            string stroke = Frame((1, 0, ImageCell(SolidImage(200, 0, 0))));

            Assert.That(Count(stroke, KittyTransmit), Is.EqualTo(1), "the touched block is sent anew");
            Assert.That(ImageIdOfFirstTransmit(stroke), Is.EqualTo(firstBlockId), "under the id it had");
            Assert.That(Count(stroke, KittyPlace), Is.EqualTo(1), "and placed anew");
            Assert.That(stroke, Does.Not.Contain("a=d,"), "nothing is deleted: the transmit replaced it");

            string unchanged = Frame();
            Assert.That(unchanged, Does.Not.Contain("\u001b_G"), "a frame that changes nothing sends nothing");
        }

        /// <summary>Paint is composited into the pixels: a dimmed block is sent again, and so is an undimmed one.</summary>
        [Test]
        public void KittyPaintOverAPictureIsSentInThePixels()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            var paint = Color.Parse("#7F000000");
            string shown = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));

            string dimmed = Frame((0, 0, ImageCell(image, paint)), (1, 0, ImageCell(image, paint)));

            Assert.That(Count(dimmed, KittyTransmit), Is.EqualTo(1), "the block again, darker");
            Assert.That(dimmed, Does.Not.Contain("z=-1,"), "no wash placement: the paint is in the pixels");
            Assert.That(TransmitPayload(dimmed), Is.Not.EqualTo(TransmitPayload(shown)));

            string undimmed = Frame((0, 0, ImageCell(image)), (1, 0, ImageCell(image)));
            Assert.That(Count(undimmed, KittyTransmit), Is.EqualTo(1), "and again as it was");
            Assert.That(TransmitPayload(undimmed), Is.EqualTo(TransmitPayload(shown)));
        }

        /// <summary>
        ///     After a reset nothing the terminal shows is trusted: every block is sent again under its id,
        ///     which replaces whatever the terminal still had for it.
        /// </summary>
        [Test]
        public void KittySendsEverythingAgainAfterTheCacheIsReset()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            CellImage image = SolidImage(10, 20, 30);
            string first = Frame((0, 0, ImageCell(image)));

            _output.ResetCache();
            string again = Frame((0, 0, ImageCell(image)));

            Assert.That(Count(again, KittyTransmit), Is.EqualTo(1), "sent again");
            Assert.That(ImageIdOfFirstTransmit(again), Is.EqualTo(ImageIdOfFirstTransmit(first)), "under the same id");
            Assert.That(Count(again, KittyPlace), Is.EqualTo(1), "placed again");
            Assert.That(again, Does.Not.Contain("a=d,"));
        }

        /// <summary>A block with no picture left has its image deleted, and the terminal holds nothing for it.</summary>
        [Test]
        public void KittyDeletesTheImageOfABlockWithNoPictureLeft()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            var text = new Pixel(new PixelForeground(new Symbol('x'), Colors.White), new PixelBackground(Colors.Blue));
            string first = Frame((0, 0, ImageCell(SolidImage(10, 20, 30))));

            string covered = Frame((0, 0, text));

            Assert.That(covered, Does.Contain($"a=d,d=I,q=2,i={ImageIdOfFirstTransmit(first)}"), "the block's image goes");
            Assert.That(Count(covered, KittyTransmit), Is.Zero);
        }

        private static string ImageIdOfFirstTransmit(string frame)
        {
            var match = System.Text.RegularExpressions.Regex.Match(frame, @"\u001b_Ga=t,[^;]*?i=(\d+)");
            Assert.That(match.Success, Is.True, "no transmit in the frame");
            return match.Groups[1].Value;
        }

        private static string TransmitPayload(string frame)
        {
            var match = System.Text.RegularExpressions.Regex.Match(frame, @"\u001b_Ga=t,[^;]*;([^\u001b]*)");
            Assert.That(match.Success, Is.True, "no transmit in the frame");
            return match.Groups[1].Value;
        }

        /// <summary>
        ///     A dialog dragged across a picture: after every frame the placements alive in the terminal
        ///     cover exactly the cells that show the picture, no more and no fewer. Judged from the
        ///     sequences sent, applied as a terminal applies them: a placement covers its cells until a
        ///     delete names it or its image.
        /// </summary>
        [Test]
        public void KittyPlacementsCoverExactlyTheImageCellsAsADialogMoves()
        {
            Open(ConsoleCapabilities.SupportsKittyGraphics);
            var live = new Dictionary<(int Image, int Placement), HashSet<(int X, int Y)>>();
            var previous = new PixelBuffer(Columns, Rows);

            // every cell of the picture distinct, so no two blocks share an image
            var picture = new CellImage[Columns, Rows];
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
                picture[x, y] = SolidImage((byte)(x * 16), (byte)(y * 32), 7);

            var dialog = new Pixel(new PixelForeground(new Symbol('d'), Colors.Black), new PixelBackground(Colors.White));
            (int X, int Y, int W, int H)?[] dialogs = { null, (2, 2, 7, 5), (3, 2, 7, 5), (5, 3, 7, 5), (9, 1, 6, 6), null, (0, 0, 16, 8), null };

            int transmitsAfterThePicture = 0;
            for (int step = 0; step < dialogs.Length; step++)
            {
                (int X, int Y, int W, int H)? at = dialogs[step];
                var screen = new PixelBuffer(Columns, Rows);
                var changed = new List<(int, int, Pixel)>();
                for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                {
                    bool underDialog = at is { } d && x >= d.X && x < d.X + d.W && y >= d.Y && y < d.Y + d.H;
                    Pixel pixel = underDialog ? dialog : ImageCell(picture[x, y]);
                    screen[(ushort)x, (ushort)y] = pixel;
                    if (pixel != previous[(ushort)x, (ushort)y])
                        changed.Add((x, y, pixel));
                }

                _screen = screen;
                string frame = Frame(changed.ToArray());
                ApplyToTerminal(frame, live);
                if (step > 0)
                    transmitsAfterThePicture += Count(frame, KittyTransmit);
                previous = screen;

                var covered = new HashSet<(int, int)>(live.Values.SelectMany(cells => cells));
                var expected = new HashSet<(int, int)>();
                for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                    if (screen[(ushort)x, (ushort)y].Background.Image != null)
                        expected.Add((x, y));

                Assert.That(covered, Is.EquivalentTo(expected), $"dialog at {at}");
            }

            // every block the dialog's edge crossed is sent again, each one under its own id; the fully
            // covered ones are deleted and the untouched ones are not sent at all
            Assert.That(transmitsAfterThePicture, Is.GreaterThan(0).And.LessThan(dialogs.Length * 8),
                "dragging a dialog over a picture re-sends the blocks along its edges, not the screen");
        }

        /// <summary>Applies a frame's kitty placements and deletes to <paramref name="live" />, as a terminal would.</summary>
        private static void ApplyToTerminal(string frame, Dictionary<(int Image, int Placement), HashSet<(int X, int Y)>> live)
        {
            var cursor = (X: 0, Y: 0);
            var tokens = System.Text.RegularExpressions.Regex.Matches(frame,
                @"\u001b\[(\d+);(\d+)f|\u001b_G([^\u001b]*)\u001b\\");
            foreach (System.Text.RegularExpressions.Match token in tokens)
            {
                if (token.Groups[1].Success)
                {
                    cursor = (int.Parse(token.Groups[2].Value) - 1, int.Parse(token.Groups[1].Value) - 1);
                    continue;
                }

                string command = token.Groups[3].Value;
                var keys = command.Split(';')[0].Split(',')
                    .Select(part => part.Split('='))
                    .Where(part => part.Length == 2)
                    .ToDictionary(part => part[0], part => part[1]);
                if (!keys.TryGetValue("a", out string action))
                    continue;

                if (action == "t" && keys.TryGetValue("i", out string transmitted))
                {
                    // a transmit under an id the terminal holds replaces the image and its placements
                    int replaced = int.Parse(transmitted);
                    foreach ((int Image, int Placement) key in live.Keys.Where(k => k.Image == replaced).ToList())
                        live.Remove(key);
                }
                else if (action == "p" && keys.TryGetValue("i", out string image) && keys.TryGetValue("p", out string placement))
                {
                    int cols = keys.TryGetValue("c", out string c) ? int.Parse(c) : int.Parse(keys["w"]) / CellWidth;
                    int rows = keys.TryGetValue("r", out string r) ? int.Parse(r) : int.Parse(keys["h"]) / CellHeight;
                    var cells = new HashSet<(int, int)>();
                    for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                        cells.Add((cursor.X + x, cursor.Y + y));
                    if (keys.TryGetValue("z", out string z) && z == "-1")
                        continue; // a wash is not the picture
                    live[(int.Parse(image), int.Parse(placement))] = cells;
                }
                else if (action == "d" && keys.TryGetValue("d", out string target) && keys.TryGetValue("i", out string deleted))
                {
                    int imageId = int.Parse(deleted);
                    if (target == "i" && keys.TryGetValue("p", out string p))
                        live.Remove((imageId, int.Parse(p)));
                    else if (target == "I")
                        foreach ((int Image, int Placement) key in live.Keys.Where(k => k.Image == imageId).ToList())
                            live.Remove(key);
                }
            }
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
