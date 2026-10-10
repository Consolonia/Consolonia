using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
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

        public static int AllocateImageId()
        {
            return NextId(ref _nextImageId);
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

            int payloadLength = (data.Length + 2) / 3 * 4;
            char[] payload = ArrayPool<char>.Shared.Rent(payloadLength);
            try
            {
                Convert.TryToBase64Chars(data, payload, out payloadLength);
                return BuildChunks(imageId, pixelWidth, pixelHeight, payload.AsSpan(0, payloadLength), format,
                    zlibCompressed);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(payload);
            }
        }

        private static string BuildChunks(int imageId, int pixelWidth, int pixelHeight, ReadOnlySpan<char> payload,
            KittyImageFormat format, bool zlibCompressed)
        {
            var stringBuilder = new StringBuilder(payload.Length + payload.Length / MaxChunkSize * 12 + 128);
            int offset = 0;
            bool first = true;
            while (offset < payload.Length)
            {
                int chunkLength = Math.Min(MaxChunkSize, payload.Length - offset);
                bool last = offset + chunkLength >= payload.Length;
                stringBuilder.Append("\u001b_G");
                if (first)
                {
                    string header = format == KittyImageFormat.Png
                        ? string.Create(CultureInfo.InvariantCulture, $"a=t,f=100,q=2,i={imageId},")
                        : string.Create(CultureInfo.InvariantCulture,
                            $"a=t,f=32,{(zlibCompressed ? "o=z," : "")}q=2,i={imageId},s={pixelWidth},v={pixelHeight},");
                    stringBuilder.Append(header);
                    first = false;
                }

                stringBuilder.Append(last ? "m=0;" : "m=1;")
                    .Append(payload.Slice(offset, chunkLength))
                    .Append("\u001b\\");
                offset += chunkLength;
            }

            return stringBuilder.ToString();
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
        ///     the image storage in the terminal.
        /// </summary>
        public static string BuildDeleteSequence(int imageId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"\u001b_Ga=d,d=I,q=2,i={imageId}\u001b\\");
        }
    }
}