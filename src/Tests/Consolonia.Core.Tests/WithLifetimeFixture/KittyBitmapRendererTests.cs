using System;
using Avalonia;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.NUnit;
using NUnit.Framework;
using FakeReadableBitmap = Consolonia.Core.Tests.WithLifetimeFixture.SixelBitmapRendererTests.FakeReadableBitmap;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public sealed class KittyBitmapRendererTests : IDisposable
    {
        private const int TileColumns = KittyBitmapRenderer.TileColumns;
        private const int TileRows = KittyBitmapRenderer.TileRows;

        private UnitTestConsole _console;
        private ConsoleCapabilities _originalCapabilities;
        private ConsoleWindowImpl _consoleWindowImpl;
        private PixelBuffer _buffer;
        private DrawingContextImpl _dc;

        [SetUp]
        public void Setup()
        {
            _console = (UnitTestConsole)AvaloniaLocator.Current.GetRequiredService<IConsole>();
            _originalCapabilities = _console.Capabilities;
            _console.Capabilities = ConsoleCapabilities.SupportsKittyGraphics;

            _consoleWindowImpl = new ConsoleWindowImpl();
            _buffer = _consoleWindowImpl.PixelBuffer;
            _dc = new DrawingContextImpl(_consoleWindowImpl, null);
        }

        [TearDown]
        public void TearDown()
        {
            _console.Capabilities = _originalCapabilities;
            _consoleWindowImpl?.Dispose();
            _consoleWindowImpl = null;
        }

        public void Dispose()
        {
            _consoleWindowImpl?.Dispose();
        }

        /// <summary>Two tiles across and two down, the second row and column short.</summary>
        private (PixelSize Size, Rect Dest) TwoByTwoTiles()
        {
            int cellsWide = TileColumns + 3;
            int cellsHigh = TileRows + 2;
            return (new PixelSize(cellsWide * _console.CellPixelWidth, cellsHigh * _console.CellPixelHeight),
                new Rect(0, 0, cellsWide, cellsHigh));
        }

        private int ImageAt(int x, int y)
        {
            return _buffer[(ushort)x, (ushort)y].Background.Tile.ImageId;
        }

        [Test]
        public void EachTileIsItsOwnImage()
        {
            (PixelSize size, Rect dest) = TwoByTwoTiles();
            using var bitmap = new FakeReadableBitmap(size);

            _dc.DrawBitmap(bitmap, 1, new Rect(dest.Size), dest);

            Assert.That(ImageAt(0, 0), Is.Not.Zero);
            Assert.That(ImageAt(TileColumns - 1, TileRows - 1), Is.EqualTo(ImageAt(0, 0)), "one tile, one image");
            Assert.That(ImageAt(TileColumns, 0), Is.Not.EqualTo(ImageAt(0, 0)), "next tile across");
            Assert.That(ImageAt(0, TileRows), Is.Not.EqualTo(ImageAt(0, 0)), "next tile down");

            KittyTile tile = _buffer[(ushort)(TileColumns + 1), (ushort)(TileRows + 1)].Background.Tile;
            Assert.That((tile.X, tile.Y), Is.EqualTo((1, 1)), "cells are placed within their tile");
        }

        [Test]
        public void TilesThatLookAlikeShareAnImage()
        {
            int cellsWide = TileColumns * 2;
            var size = new PixelSize(cellsWide * _console.CellPixelWidth, TileRows * _console.CellPixelHeight);
            using FakeReadableBitmap blank = new FakeReadableBitmap(size)
                .Fill(new PixelRect(size), 255, 255, 255);

            _dc.DrawBitmap(blank, 1, new Rect(size.ToSize(1)),
                new Rect(0, 0, cellsWide, TileRows));

            Assert.That(ImageAt(TileColumns, 0), Is.EqualTo(ImageAt(0, 0)));
        }

        /// <summary>
        ///     An edit to the picture (a new bitmap, as a paint program publishes per stroke) retransmits only
        ///     the tile it touched; every other cell keeps its image, and so its placement.
        /// </summary>
        [Test]
        public void EditedPictureRetransmitsOnlyTheTileItTouched()
        {
            (PixelSize size, Rect dest) = TwoByTwoTiles();
            using var before = new FakeReadableBitmap(size);
            _dc.DrawBitmap(before, 1, new Rect(dest.Size), dest);
            int[,] images = Images(dest);

            // one pixel in the bottom-right tile painted over
            using FakeReadableBitmap after = new FakeReadableBitmap(size)
                .Fill(new PixelRect(size.Width - 2, size.Height - 2, 1, 1), 0, 0, 255);
            _dc.DrawBitmap(after, 1, new Rect(dest.Size), dest);

            for (int y = 0; y < (int)dest.Height; y++)
            for (int x = 0; x < (int)dest.Width; x++)
            {
                bool touched = x >= TileColumns && y >= TileRows;
                Assert.That(ImageAt(x, y), touched ? Is.Not.EqualTo(images[x, y]) : Is.EqualTo(images[x, y]),
                    $"cell {x},{y}");
            }
        }

        /// <summary>
        ///     Regression test: a partial redraw (a dialog opening over the picture) rendered by its clip, so
        ///     the redrawn cells got images of their own, cut differently, and the rest went blank once the
        ///     earlier images were evicted. A partial redraw shows the images already there.
        /// </summary>
        [Test]
        public void PartialRedrawKeepsTheImagesAlreadyShown()
        {
            (PixelSize size, Rect dest) = TwoByTwoTiles();
            using var bitmap = new FakeReadableBitmap(size);
            _dc.DrawBitmap(bitmap, 1, new Rect(dest.Size), dest);
            int[,] images = Images(dest);

            _buffer[3, 2] = Pixel.Space;
            _dc.PushClip(new Rect(3, 2, 7, 3));
            _dc.DrawBitmap(bitmap, 1, new Rect(dest.Size), dest);
            _dc.PopClip();

            Assert.That(Images(dest), Is.EqualTo(images));
        }

        private int[,] Images(Rect dest)
        {
            var images = new int[(int)dest.Width, (int)dest.Height];
            for (int y = 0; y < (int)dest.Height; y++)
            for (int x = 0; x < (int)dest.Width; x++)
                images[x, y] = ImageAt(x, y);
            return images;
        }
    }
}
