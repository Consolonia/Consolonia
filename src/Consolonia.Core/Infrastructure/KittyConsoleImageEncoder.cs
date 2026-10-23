using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Shows image cells through the kitty graphics protocol, the way the sixel encoder shows them:
    ///     pixels are sent when they change, and nothing is remembered about the terminal. A kitty picture
    ///     is a <em>placement</em>, an object below the text rather than the content of a cell, so an image
    ///     cell is written as an ordinary cell on the default background, and at the end of every frame
    ///     each block of the screen whose picture changed is transmitted again under the block's own
    ///     image id and placed at its cells.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Blocks, because kitty cannot portably change part of an image once transmitted, so the
    ///         image is the unit of retransmission: one image per picture meant a brush stroke re-sent the
    ///         whole picture. A block of a few cells is the compromise between how many images a screen is
    ///         and how much a change re-sends.
    ///     </para>
    ///     <para>
    ///         One image id per block position, for the life of the session. Transmitting under an id
    ///         the terminal already holds replaces that image and every placement of it, so a block that
    ///         changed is sent again and its old picture is gone with no delete, and the terminal never holds
    ///         more images than the screen has blocks. Nothing is ever placed that was not transmitted in the
    ///         same frame, so no placement can name an image the terminal has dropped. The earlier encoder
    ///         kept images by content and placed them again by id, and a terminal that had trimmed one left a
    ///         hole; the only state here is what each position was last sent, which is this encoder's own.
    ///     </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "The MemoryStream is over a managed array and holds nothing a Dispose would free")]
    internal sealed class KittyConsoleImageEncoder : IConsoleImageEncoder
    {
        /// <summary>Block size in cells: small enough that a stroke touches few, large enough to be few.</summary>
        internal const int BlockColumns = 8;

        internal const int BlockRows = 4;

        /// <summary>Bytes of the description of one cell in a block's key: its image's digest and its paint.</summary>
        private const int KeyBytesPerCell = 16 + 4;

        /// <summary>What each block position was last sent: its image id and the key of the pixels placed.</summary>
        private readonly Dictionary<(int X, int Y), BlockState> _blocks = new();

        private readonly List<(int X, int Y)> _blocksToForget = new();

        /// <summary>The encoded bytes of the block being transmitted: PNG or zlib. One stream, emptied per block.</summary>
        private readonly MemoryStream _encoded = new();

        private readonly AnsiConsoleOutput _output;
        private byte[] _blockKey;
        private byte[] _blockScratch;
        private bool[] _blockVisited;

        /// <summary>The platform's bitmaps, which encode PNG; looked up on the first transmit, null sends RGBA.</summary>
        private IPlatformRenderInterface _renderInterface;

        private bool _renderInterfaceResolved;

        public KittyConsoleImageEncoder(AnsiConsoleOutput output)
        {
            _output = output;
        }

        public void BeginFrame()
        {
        }

        /// <summary>
        ///     The cell is written as text on the terminal's default background, so the placement below
        ///     shows through. Konsole draws below-text (z&lt;0) placements under any explicit cell
        ///     background, so painting the cell a color, even black, hides the picture there; kitty draws
        ///     them above either way.
        /// </summary>
        public void WriteCell(PixelBufferCoordinate position, in Pixel pixel)
        {
            _output.WriteTextCell(position, in pixel, true);
        }

        /// <summary>
        ///     Walks the screen in blocks. A block whose picture changed since it was last sent (its cells'
        ///     images, their paint, or which of its cells show a picture) is transmitted again under its id,
        ///     which replaces what the terminal showed for it, and placed at its image cells, cropped
        ///     rectangle by rectangle (z=-2, below the text, above background colors). A block with no
        ///     picture any more has its image deleted. Paint is composited into the pixels: a dimmed
        ///     picture is a different picture.
        /// </summary>
        /// <remarks>
        ///     Only image cells are ever under a placement. A placement below the text is drawn above the
        ///     cells' background colors, and a terminal need not show a cell's color through the image's
        ///     transparent pixels: placing a whole block put the picture over a dialog's body and left only
        ///     its border glyphs.
        /// </remarks>
        public void EndFrame(PixelBuffer screen)
        {
            int cellPixelWidth = _output.CellPixelWidth;
            int cellPixelHeight = _output.CellPixelHeight;
            int screenWidth = screen.Width;
            bool[] visited = CellRectangles.Scratch(ref _blockVisited, screenWidth * screen.Height);

            for (int blockY = 0; blockY < screen.Height; blockY += BlockRows)
            for (int blockX = 0; blockX < screenWidth; blockX += BlockColumns)
            {
                int columns = Math.Min(BlockColumns, screenWidth - blockX);
                int rows = Math.Min(BlockRows, screen.Height - blockY);
                bool anyImage = TryGetBlockKey(screen, blockX, blockY, columns, rows, cellPixelWidth,
                    cellPixelHeight, out ContentKey key);

                _blocks.TryGetValue((blockX, blockY), out BlockState state);
                if (!anyImage)
                {
                    if (state != null && state.Sent != default)
                    {
                        _output.WriteText(KittyGraphics.BuildDeleteSequence(state.ImageId));
                        state.Sent = default;
                    }

                    continue;
                }

                if (state == null)
                {
                    state = new BlockState { ImageId = KittyGraphics.AllocateImageId() };
                    _blocks[(blockX, blockY)] = state;
                }

                if (state.Sent == key)
                    continue;

                // the pixels first, replacing the block's previous image and placements, then the placements
                TransmitBlock(state.ImageId, screen, blockX, blockY, columns, rows, cellPixelWidth, cellPixelHeight);
                state.Sent = key;

                var block = new ImageCellsInBlock(screen, blockX, blockY, columns, rows);
                int placementId = 0;
                for (int y = blockY; y < blockY + rows; y++)
                for (int x = blockX; x < blockX + columns; x++)
                {
                    if (visited[y * screenWidth + x] || screen.CellAt(x, y).Background.Image == null)
                        continue;

                    (int width, int height) = CellRectangles.GrowRectangle(block, visited, screenWidth,
                        screen.Height, x, y, false);
                    _output.SetCaretPosition(new PixelBufferCoordinate((ushort)x, (ushort)y));
                    _output.WriteText(KittyGraphics.BuildRectPlacementSequence(state.ImageId, ++placementId,
                        (x - blockX) * cellPixelWidth, (y - blockY) * cellPixelHeight,
                        width * cellPixelWidth, height * cellPixelHeight));
                }
            }

            ForgetBlocksOutside(screenWidth, screen.Height);
        }

        /// <summary>
        ///     The terminal may no longer show what was sent (the screen was resized, and terminals differ in
        ///     what becomes of placements then; or another program had the terminal), so every block is sent
        ///     again next frame. The ids stay: sending under them replaces whatever the terminal still holds.
        /// </summary>
        public void Reset()
        {
            foreach (BlockState state in _blocks.Values)
                state.Sent = default;
        }

        /// <summary>
        ///     What a block shows, as a key: each cell's image digest and paint, zero for a cell without an
        ///     image. False when no cell of the block shows an image.
        /// </summary>
        private bool TryGetBlockKey(PixelBuffer screen, int blockX, int blockY, int columns, int rows,
            int cellPixelWidth, int cellPixelHeight, out ContentKey key)
        {
            int bytes = columns * rows * KeyBytesPerCell;
            if (_blockKey == null || _blockKey.Length < bytes)
                _blockKey = new byte[BlockColumns * BlockRows * KeyBytesPerCell];
            Span<byte> description = _blockKey.AsSpan(0, bytes);
            description.Clear();

            bool anyImage = false;
            int offset = 0;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++, offset += KeyBytesPerCell)
            {
                PixelBackground background = screen.CellAt(blockX + column, blockY + row).Background;
                if (background.Image == null)
                    continue;

                anyImage = true;
                MemoryMarshal.Write(description[offset..], background.Image.Digest);
                BitConverter.TryWriteBytes(description[(offset + 16)..], background.Color.ToUInt32());
            }

            key = anyImage ? ContentKey.Of(description, columns * cellPixelWidth, rows * cellPixelHeight) : default;
            return anyImage;
        }

        /// <summary>
        ///     Sends the block as one image under <paramref name="imageId" />, its cells' pixels side by side
        ///     with each cell's paint composited in and a cell without an image transparent: PNG through the
        ///     platform's bitmaps when there are any (every kitty terminal decodes it, and a photograph is
        ///     half the bytes), RGBA with zlib otherwise.
        /// </summary>
        private void TransmitBlock(int imageId, PixelBuffer screen, int blockX, int blockY, int columns, int rows,
            int cellPixelWidth, int cellPixelHeight)
        {
            int blockWidth = columns * cellPixelWidth;
            int blockHeight = rows * cellPixelHeight;
            int blockBytes = blockWidth * blockHeight * 4;
            if (_blockScratch == null || _blockScratch.Length < blockBytes)
                _blockScratch = new byte[BlockColumns * cellPixelWidth * BlockRows * cellPixelHeight * 4];
            Span<byte> block = _blockScratch.AsSpan(0, blockBytes);
            block.Clear();

            int blockRowBytes = blockWidth * 4;
            int cellRowBytes = cellPixelWidth * 4;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                PixelBackground background = screen.CellAt(blockX + column, blockY + row).Background;
                CellImage image = background.Image;
                if (image == null)
                    continue;

                int cellOffset = row * cellPixelHeight * blockRowBytes + column * cellRowBytes;
                ReadOnlySpan<byte> bgra = image.Bgra;
                for (int y = 0; y < cellPixelHeight; y++)
                    bgra.Slice(y * cellRowBytes, cellRowBytes).CopyTo(block.Slice(cellOffset + y * blockRowBytes));

                if (background.Color.A != 0)
                    CellImage.Paint(block, blockRowBytes, cellOffset, cellPixelWidth, cellPixelHeight,
                        background.Color);
            }

            MemoryStream encoded = _encoded;
            encoded.SetLength(0);
            if (TryEncodePng(block, blockWidth, blockHeight, encoded))
            {
                KittyGraphics.WriteTransmitSequence(_output, imageId, blockWidth, blockHeight, Encoded(),
                    KittyImageFormat.Png);
                return;
            }

            // RGBA: the blue and red bytes swap. Fastest is zlib level 1: level 6 saved a quarter of the
            // bytes on photographs and cost more than twice the time per block, and this is the fallback.
            for (int i = 0; i < block.Length; i += 4)
                (block[i], block[i + 2]) = (block[i + 2], block[i]);

            encoded.SetLength(0);
            using (var zlib = new ZLibStream(encoded, CompressionLevel.Fastest, true))
            {
                zlib.Write(block);
            }

            KittyGraphics.WriteTransmitSequence(_output, imageId, blockWidth, blockHeight, Encoded(),
                KittyImageFormat.Rgba, true);
        }

        /// <summary>What <see cref="_encoded" /> holds, without copying it out.</summary>
        private ReadOnlySpan<byte> Encoded()
        {
            return _encoded.GetBuffer().AsSpan(0, (int)_encoded.Length);
        }

        /// <summary>
        ///     Writes the PNG of <paramref name="width" /> x <paramref name="height" /> BGRA pixels to
        ///     <paramref name="png" /> through the platform's bitmaps (a bitmap loaded from the pixels and
        ///     saved, which Avalonia does through Skia). False, with nothing written, where the platform has
        ///     none or cannot encode: the headless tests, the benchmarks, a platform without an image codec.
        /// </summary>
        private unsafe bool TryEncodePng(ReadOnlySpan<byte> bgra, int width, int height, MemoryStream png)
        {
            if (!_renderInterfaceResolved)
            {
                _renderInterface = AvaloniaLocator.Current.GetService<IPlatformRenderInterface>();
                _renderInterfaceResolved = true;
            }

            if (_renderInterface == null)
                return false;

            try
            {
                fixed (byte* pixels = bgra)
                {
                    using IBitmapImpl bitmap = _renderInterface.LoadBitmap(PixelFormat.Bgra8888, AlphaFormat.Premul,
                        (IntPtr)pixels, new PixelSize(width, height), new Vector(96, 96), width * 4);
                    bitmap.Save(png, PngBitmapEncoderOptions.Default);
                    // a platform with no image codec saves nothing rather than throwing
                    return png.Length > 0;
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or NotImplementedException
                                                  or ConsoloniaNotSupportedException)
            {
                png.SetLength(0);
                return false;
            }
        }

        /// <summary>
        ///     Deletes the images of block positions the screen no longer has (it shrank), so the terminal
        ///     holds no more than the screen's blocks.
        /// </summary>
        private void ForgetBlocksOutside(int screenWidth, int screenHeight)
        {
            _blocksToForget.Clear();
            foreach (((int x, int y), BlockState state) in _blocks)
                if (x >= screenWidth || y >= screenHeight)
                {
                    if (state.Sent != default)
                        _output.WriteText(KittyGraphics.BuildDeleteSequence(state.ImageId));
                    _blocksToForget.Add((x, y));
                }

            foreach ((int X, int Y) position in _blocksToForget)
                _blocks.Remove(position);
        }

        /// <summary>
        ///     A block position's image id, for the session, and the key of what was last sent under it:
        ///     default when the terminal holds nothing for it.
        /// </summary>
        private sealed class BlockState
        {
            public int ImageId;
            public ContentKey Sent;
        }

        /// <summary>Image cells within one block.</summary>
        private readonly struct ImageCellsInBlock(PixelBuffer screen, int blockX, int blockY, int columns, int rows)
            : IRectangleCells
        {
            public bool Joins(int x, int y, int dx, int dy)
            {
                int cellX = x + dx;
                int cellY = y + dy;
                return cellX < blockX + columns && cellY < blockY + rows &&
                       screen.CellAt(cellX, cellY).Background.Image != null;
            }
        }
    }
}