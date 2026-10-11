using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using Avalonia.Media;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Pixel data formats of the kitty graphics protocol (the f key of a transmit command).
    /// </summary>
    internal enum KittyImageFormat
    {
        /// <summary>Raw 32 bit RGBA (f=32). Roughly 4 bytes per pixel plus base64 expansion.</summary>
        Rgba,

        /// <summary>PNG encoded (f=100). Orders of magnitude smaller on the wire for real images.</summary>
        Png
    }

    /// <summary>
    ///     Helpers for the kitty graphics protocol (https://sw.kovidgoyal.net/kitty/graphics-protocol/).
    ///     An image is transmitted once and shown through classic placements: each covered cell
    ///     carries a <see cref="PixelBufferImplementation.KittyTile" /> in its background, and
    ///     RenderTarget coalesces those into placements cropped from the image, below text.
    /// </summary>
    internal static class KittyGraphics
    {
        // maximum base64 payload length per APC chunk allowed by the protocol
        private const int MaxChunkSize = 4096;

        /// <summary>
        ///     Queries kitty graphics support; a supporting terminal replies "APC _Gi=31;OK ST". Follow it
        ///     with a Device Attributes request as a fence: every terminal answers DA1.
        /// </summary>
        public const string QuerySupport = "\u001b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\u001b\\";

        /// <summary>Deletes all kitty images and placements, freeing terminal-side image storage.</summary>
        public const string DeleteAllImages = "\u001b_Ga=d,d=A,q=2\u001b\\";

        /// <summary>
        ///     The z-index classic rect placements (the "image as cell background" mode) are created
        ///     at: below text, above background colors, so glyphs on the covered cells composite
        ///     over the picture.
        /// </summary>
        public const int RectPlacementZIndex = -2;

        /// <summary>
        ///     The z-index of wash placements: translucent overlays tinting a rect placement (a modal
        ///     backdrop over a picture), above the image and still below text.
        /// </summary>
        public const int WashPlacementZIndex = -1;

        // Kept in the range 1..int.MaxValue (0 is not a valid id; the protocol allows 32 bits). The wider
        // the range, the longer before a wrapped id lands on an image still shown: re-transmitting an id
        // the terminal holds deletes that image and its placements.
        private static int _nextImageId;

        private static int _nextPlacementId;

        // The images in the terminal, by id: allocated for transmission and not deleted since. Deleting
        // every visible placement (d=A) leaves the images nothing shows, and the tile cache keeps a few
        // screens of those in the terminal to show again without resending; at exit, and when the
        // terminal's contents are forgotten, they are deleted one by one.
        private static readonly HashSet<int> TransmittedImages = new();

        /// <summary>
        ///     An id for an image about to be transmitted. It counts as in the terminal until
        ///     <see cref="BuildDeleteSequence" /> is built for it, or every image is deleted with
        ///     <see cref="BuildDeleteTransmittedImagesSequence" />.
        /// </summary>
        public static int AllocateImageId()
        {
            int imageId = NextId(ref _nextImageId);
            lock (TransmittedImages)
            {
                TransmittedImages.Add(imageId);
            }

            return imageId;
        }

        /// <summary>
        ///     The next id in 1..int.MaxValue. The counter is read as unsigned so it keeps wrapping within
        ///     that range after it passes int.MaxValue, instead of turning negative.
        /// </summary>
        private static int NextId(ref int counter)
        {
            return (int)((uint)(Interlocked.Increment(ref counter) - 1) % int.MaxValue) + 1;
        }

        /// <summary>
        ///     Builds the chunked APC sequence transmitting an image (a=t): PNG encoded (f=100,
        ///     the image carries its own dimensions) or raw 32 bit RGBA (f=32), the latter optionally
        ///     zlib compressed (o=z).
        /// </summary>
        public static string BuildTransmitSequence(int imageId, int pixelWidth, int pixelHeight, byte[] data,
            KittyImageFormat format, bool zlibCompressed = false)
        {
            ArgumentNullException.ThrowIfNull(data);

            byte[] sequence = ArrayPool<byte>.Shared.Rent(MaxTransmitLength(data.Length));
            try
            {
                int length = ComposeTransmit(sequence, imageId, pixelWidth, pixelHeight, data, format,
                    zlibCompressed);
                return Encoding.ASCII.GetString(sequence, 0, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sequence);
            }
        }

        /// <summary>
        ///     Writes the transmit sequence of <see cref="BuildTransmitSequence" /> to <paramref name="console" />
        ///     as bytes: a payload of megabytes is never turned into text and back.
        /// </summary>
        public static void WriteTransmitSequence(Infrastructure.IConsoleOutput console, int imageId, int pixelWidth,
            int pixelHeight, byte[] data, KittyImageFormat format, bool zlibCompressed = false)
        {
            ArgumentNullException.ThrowIfNull(console);
            ArgumentNullException.ThrowIfNull(data);

            byte[] sequence = ArrayPool<byte>.Shared.Rent(MaxTransmitLength(data.Length));
            try
            {
                int length = ComposeTransmit(sequence, imageId, pixelWidth, pixelHeight, data, format,
                    zlibCompressed);
                console.WriteBytes(sequence.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sequence);
            }
        }

        /// <summary>Payload bytes per chunk: exactly <see cref="MaxChunkSize" /> base64 characters.</summary>
        private const int RawChunkSize = MaxChunkSize / 4 * 3;

        private static int MaxTransmitLength(int dataLength)
        {
            int chunks = (dataLength + RawChunkSize - 1) / RawChunkSize;
            // per chunk "ESC_G" "m=1;" "ESC\", and the first chunk's header
            return (dataLength + 2) / 3 * 4 + chunks * 16 + 96;
        }

        private static int ComposeTransmit(Span<byte> destination, int imageId, int pixelWidth, int pixelHeight,
            ReadOnlySpan<byte> data, KittyImageFormat format, bool zlibCompressed)
        {
            int position = 0;
            int offset = 0;
            bool first = true;
            while (offset < data.Length)
            {
                int chunkLength = Math.Min(RawChunkSize, data.Length - offset);
                bool last = offset + chunkLength >= data.Length;

                "\u001b_G"u8.CopyTo(destination[position..]);
                position += 3;
                if (first)
                {
                    int headerLength;
                    bool written = format == KittyImageFormat.Png
                        ? Utf8.TryWrite(destination[position..], CultureInfo.InvariantCulture,
                            $"a=t,f=100,q=2,i={imageId},", out headerLength)
                        : Utf8.TryWrite(destination[position..], CultureInfo.InvariantCulture,
                            $"a=t,f=32,{(zlibCompressed ? "o=z," : "")}q=2,i={imageId},s={pixelWidth},v={pixelHeight},",
                            out headerLength);
                    Debug.Assert(written, "MaxTransmitLength leaves room for the header");
                    position += headerLength;
                    first = false;
                }

                (last ? "m=0;"u8 : "m=1;"u8).CopyTo(destination[position..]);
                position += 4;

                // whole groups of three bytes until the last chunk, so each chunk's base64 stands alone
                Base64.EncodeToUtf8(data.Slice(offset, chunkLength), destination[position..], out _,
                    out int encoded);
                position += encoded;

                "\u001b\\"u8.CopyTo(destination[position..]);
                position += 2;
                offset += chunkLength;
            }

            return position;
        }

        public static int AllocatePlacementId()
        {
            return NextId(ref _nextPlacementId);
        }

        /// <summary>
        ///     Builds the APC sequence creating a classic placement showing a source-pixel crop of
        ///     an already transmitted image at the current cursor position, below text (z=-2),
        ///     without moving the cursor (C=1). The image is pre-scaled to the cell grid, so the
        ///     crop maps 1:1 onto cells and no c=/r= stretching is involved.
        /// </summary>
        public static string BuildRectPlacementSequence(int imageId, int placementId,
            int sourceX, int sourceY, int sourceWidth, int sourceHeight)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"\u001b_Ga=p,q=2,C=1,z={RectPlacementZIndex},i={imageId},p={placementId},x={sourceX},y={sourceY},w={sourceWidth},h={sourceHeight}\u001b\\");
        }

        /// <summary>
        ///     Builds the APC sequence transmitting a solid <paramref name="wash" /> image of one pixel per
        ///     cell, <paramref name="columns" /> x <paramref name="rows" /> (zlib compressed, so its size
        ///     on the wire barely depends on the screen's).
        /// </summary>
        /// <remarks>
        ///     A single pixel stretched with c=/r= would do on kitty, but terminals which map every cell
        ///     to whole source pixels drop a placement whose cells get less than one; at one pixel per
        ///     cell every cell gets exactly one.
        /// </remarks>
        public static string BuildTransmitWashSequence(int imageId, Color wash, int columns, int rows)
        {
            byte[] rgba = new byte[columns * rows * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = wash.R;
                rgba[i + 1] = wash.G;
                rgba[i + 2] = wash.B;
                rgba[i + 3] = wash.A;
            }

            using var compressed = new MemoryStream();
            // a solid color compresses to almost nothing at any level; an animated overlay sends a new
            // one every frame, so take the fastest
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            {
                zlib.Write(rgba);
            }

            return BuildTransmitSequence(imageId, columns, rows, compressed.ToArray(), KittyImageFormat.Rgba, true);
        }

        /// <summary>
        ///     Builds the APC sequence placing a wash image (see <see cref="BuildTransmitWashSequence" />)
        ///     at the current cursor position over <paramref name="columns" /> x <paramref name="rows" />
        ///     cells, above rect placements and below text, without moving the cursor (C=1). It crops
        ///     the image to one pixel per covered cell, so the image must be at least that large.
        /// </summary>
        public static string BuildWashPlacementSequence(int imageId, int placementId, int columns, int rows)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"\u001b_Ga=p,q=2,C=1,z={WashPlacementZIndex},i={imageId},p={placementId},x=0,y=0,w={columns},h={rows},c={columns},r={rows}\u001b\\");
        }

        /// <summary>
        ///     Builds the APC sequence deleting one specific placement of an image while keeping
        ///     the image data, so other placements of the same image survive and re-placement
        ///     needs no retransmission.
        /// </summary>
        public static string BuildDeletePlacementSequence(int imageId, int placementId)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"\u001b_Ga=d,d=i,q=2,i={imageId},p={placementId}\u001b\\");
        }

        /// <summary>
        ///     Builds the APC sequence deleting an image and its placements (uppercase d=I), freeing
        ///     the image storage in the terminal. The image no longer counts as in the terminal.
        /// </summary>
        public static string BuildDeleteSequence(int imageId)
        {
            lock (TransmittedImages)
            {
                TransmittedImages.Remove(imageId);
            }

            return DeleteSequence(imageId);
        }

        /// <summary>
        ///     Builds the APC sequences deleting every image transmitted and not deleted since, each with
        ///     its placements, and counts them all as gone. Empty when there is none. For leaving the
        ///     terminal: deleting the visible placements alone would leave the images kept for showing again.
        /// </summary>
        public static string BuildDeleteTransmittedImagesSequence()
        {
            lock (TransmittedImages)
            {
                if (TransmittedImages.Count == 0)
                    return string.Empty;

                var stringBuilder = new StringBuilder(TransmittedImages.Count * 32);
                foreach (int imageId in TransmittedImages)
                    stringBuilder.Append(DeleteSequence(imageId));
                TransmittedImages.Clear();
                return stringBuilder.ToString();
            }
        }

        private static string DeleteSequence(int imageId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"\u001b_Ga=d,d=I,q=2,i={imageId}\u001b\\");
        }
    }
}