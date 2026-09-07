using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Helpers for the kitty graphics protocol using unicode placeholders
    ///     (https://sw.kovidgoyal.net/kitty/graphics-protocol/#unicode-placeholders).
    ///     An image is transmitted once, a virtual placement scales it to a rectangle of cells,
    ///     and every covered cell is rendered as an ordinary character cell containing U+10EEEE
    ///     with combining diacritics encoding the row/column and the image id encoded in the
    ///     foreground color. Because placeholders are plain text cells they integrate with pixel
    ///     buffer diffing and occlusion like any other cell, while the image pixels cross the
    ///     wire only once.
    /// </summary>
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

    internal static class KittyGraphics
    {
        /// <summary>The unicode placeholder codepoint U+10EEEE as a utf-16 string.</summary>
        public const string PlaceholderCharacter = "\U0010EEEE";

        // maximum base64 payload length per APC chunk allowed by the protocol
        private const int MaxChunkSize = 4096;

        // Image ids are carried in the 24 bit foreground color of placeholder cells,
        // so they must stay in the range 1..0xFFFFFF (0 is not a valid id).
        private static int _nextImageId;

        // Ids whose terminal side placements were deleted because no placeholder cell referenced
        // them anymore; the renderer re-creates the placement (without retransmitting the pixels)
        // when the image is drawn again.
        private static readonly ConcurrentDictionary<int, byte> DeletedPlacements = new();

        /// <summary>
        ///     Combining diacritics used to encode row/column numbers of unicode placeholders,
        ///     taken verbatim from kitty's rowcolumn-diacritics.txt. The index in this table is
        ///     the zero based row/column the diacritic denotes.
        /// </summary>
        private static readonly string[] RowColumnDiacritics =
        {
            "\u0305", "\u030D", "\u030E", "\u0310", "\u0312", "\u033D",
            "\u033E", "\u033F", "\u0346", "\u034A", "\u034B", "\u034C",
            "\u0350", "\u0351", "\u0352", "\u0357", "\u035B", "\u0363",
            "\u0364", "\u0365", "\u0366", "\u0367", "\u0368", "\u0369",
            "\u036A", "\u036B", "\u036C", "\u036D", "\u036E", "\u036F",
            "\u0483", "\u0484", "\u0485", "\u0486", "\u0487", "\u0592",
            "\u0593", "\u0594", "\u0595", "\u0597", "\u0598", "\u0599",
            "\u059C", "\u059D", "\u059E", "\u059F", "\u05A0", "\u05A1",
            "\u05A8", "\u05A9", "\u05AB", "\u05AC", "\u05AF", "\u05C4",
            "\u0610", "\u0611", "\u0612", "\u0613", "\u0614", "\u0615",
            "\u0616", "\u0617", "\u0657", "\u0658", "\u0659", "\u065A",
            "\u065B", "\u065D", "\u065E", "\u06D6", "\u06D7", "\u06D8",
            "\u06D9", "\u06DA", "\u06DB", "\u06DC", "\u06DF", "\u06E0",
            "\u06E1", "\u06E2", "\u06E4", "\u06E7", "\u06E8", "\u06EB",
            "\u06EC", "\u0730", "\u0732", "\u0733", "\u0735", "\u0736",
            "\u073A", "\u073D", "\u073F", "\u0740", "\u0741", "\u0743",
            "\u0745", "\u0747", "\u0749", "\u074A", "\u07EB", "\u07EC",
            "\u07ED", "\u07EE", "\u07EF", "\u07F0", "\u07F1", "\u07F3",
            "\u0816", "\u0817", "\u0818", "\u0819", "\u081B", "\u081C",
            "\u081D", "\u081E", "\u081F", "\u0820", "\u0821", "\u0822",
            "\u0823", "\u0825", "\u0826", "\u0827", "\u0829", "\u082A",
            "\u082B", "\u082C", "\u082D", "\u0951", "\u0953", "\u0954",
            "\u0F82", "\u0F83", "\u0F86", "\u0F87", "\u135D", "\u135E",
            "\u135F", "\u17DD", "\u193A", "\u1A17", "\u1A75", "\u1A76",
            "\u1A77", "\u1A78", "\u1A79", "\u1A7A", "\u1A7B", "\u1A7C",
            "\u1B6B", "\u1B6D", "\u1B6E", "\u1B6F", "\u1B70", "\u1B71",
            "\u1B72", "\u1B73", "\u1CD0", "\u1CD1", "\u1CD2", "\u1CDA",
            "\u1CDB", "\u1CE0", "\u1DC0", "\u1DC1", "\u1DC3", "\u1DC4",
            "\u1DC5", "\u1DC6", "\u1DC7", "\u1DC8", "\u1DC9", "\u1DCB",
            "\u1DCC", "\u1DD1", "\u1DD2", "\u1DD3", "\u1DD4", "\u1DD5",
            "\u1DD6", "\u1DD7", "\u1DD8", "\u1DD9", "\u1DDA", "\u1DDB",
            "\u1DDC", "\u1DDD", "\u1DDE", "\u1DDF", "\u1DE0", "\u1DE1",
            "\u1DE2", "\u1DE3", "\u1DE4", "\u1DE5", "\u1DE6", "\u1DFE",
            "\u20D0", "\u20D1", "\u20D4", "\u20D5", "\u20D6", "\u20D7",
            "\u20DB", "\u20DC", "\u20E1", "\u20E7", "\u20E9", "\u20F0",
            "\u2CEF", "\u2CF0", "\u2CF1", "\u2DE0", "\u2DE1", "\u2DE2",
            "\u2DE3", "\u2DE4", "\u2DE5", "\u2DE6", "\u2DE7", "\u2DE8",
            "\u2DE9", "\u2DEA", "\u2DEB", "\u2DEC", "\u2DED", "\u2DEE",
            "\u2DEF", "\u2DF0", "\u2DF1", "\u2DF2", "\u2DF3", "\u2DF4",
            "\u2DF5", "\u2DF6", "\u2DF7", "\u2DF8", "\u2DF9", "\u2DFA",
            "\u2DFB", "\u2DFC", "\u2DFD", "\u2DFE", "\u2DFF", "\uA66F",
            "\uA67C", "\uA67D", "\uA6F0", "\uA6F1", "\uA8E0", "\uA8E1",
            "\uA8E2", "\uA8E3", "\uA8E4", "\uA8E5", "\uA8E6", "\uA8E7",
            "\uA8E8", "\uA8E9", "\uA8EA", "\uA8EB", "\uA8EC", "\uA8ED",
            "\uA8EE", "\uA8EF", "\uA8F0", "\uA8F1", "\uAAB0", "\uAAB2",
            "\uAAB3", "\uAAB7", "\uAAB8", "\uAABE", "\uAABF", "\uAAC1",
            "\uFE20", "\uFE21", "\uFE22", "\uFE23", "\uFE24", "\uFE25",
            "\uFE26", "\U00010A0F", "\U00010A38", "\U0001D185", "\U0001D186", "\U0001D187",
            "\U0001D188", "\U0001D189", "\U0001D1AA", "\U0001D1AB", "\U0001D1AC", "\U0001D1AD",
            "\U0001D242", "\U0001D243", "\U0001D244"
        };

        /// <summary>
        ///     The largest number of columns or rows a placeholder placement can address.
        /// </summary>
        public static int MaxPlacementSize => RowColumnDiacritics.Length;

        public static int AllocateImageId()
        {
            return (Interlocked.Increment(ref _nextImageId) - 1) % 0xFFFFFF + 1;
        }

        /// <summary>
        ///     Encodes an image id as the foreground color carrying it to the terminal.
        /// </summary>
        public static Color GetImageIdColor(int imageId)
        {
            return Color.FromRgb((byte)(imageId >> 16), (byte)(imageId >> 8), (byte)imageId);
        }

        /// <summary>
        ///     Builds the unicode sequence for the placeholder cell at the given zero based
        ///     row/column of a placement.
        /// </summary>
        public static string GetPlaceholderCell(int row, int column)
        {
            return PlaceholderCharacter + RowColumnDiacritics[row] + RowColumnDiacritics[column];
        }

        public static void MarkPlacementDeleted(int imageId)
        {
            DeletedPlacements[imageId] = 0;
        }

        public static bool TryReclaimPlacement(int imageId)
        {
            return DeletedPlacements.TryRemove(imageId, out _);
        }

        /// <summary>
        ///     Checks whether a symbol is a kitty unicode placeholder cell.
        ///     Cheap enough to run on every cell of every frame: two character comparisons.
        /// </summary>
        public static bool IsPlaceholder(in Symbol symbol)
        {
            string complex = symbol.Complex;
            return complex != null && complex.Length >= 2 &&
                   complex[0] == PlaceholderCharacter[0] &&
                   complex[1] == PlaceholderCharacter[1];
        }

        /// <summary>
        ///     Extracts the image id from a placeholder cell (see <see cref="GetPlaceholderCell" />),
        ///     which carries it in the foreground color.
        /// </summary>
        public static bool TryGetImageId(in Pixel pixel, out int imageId)
        {
            imageId = 0;
            if (!IsPlaceholder(in pixel.Foreground.Symbol))
                return false;

            Color color = pixel.Foreground.Color;
            imageId = (color.R << 16) | (color.G << 8) | color.B;
            return imageId != 0;
        }

        /// <summary>
        ///     Builds the chunked APC sequence transmitting an image (a=t): PNG encoded (f=100,
        ///     the image carries its own dimensions) or raw 32 bit RGBA (f=32).
        /// </summary>
        public static string BuildTransmitSequence(int imageId, int pixelWidth, int pixelHeight, byte[] data,
            KittyImageFormat format)
        {
            ArgumentNullException.ThrowIfNull(data);

            string payload = Convert.ToBase64String(data);
            var stringBuilder = new StringBuilder(payload.Length + 128);
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
                            $"a=t,f=32,q=2,i={imageId},s={pixelWidth},v={pixelHeight},");
                    stringBuilder.Append(header);
                    first = false;
                }

                stringBuilder.Append(last ? "m=0;" : "m=1;")
                    .Append(payload, offset, chunkLength)
                    .Append("\u001b\\");
                offset += chunkLength;
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        ///     Builds the APC sequence creating a virtual placement (U=1) scaling the image
        ///     to the given rectangle of cells, referenced by placeholder cells.
        /// </summary>
        public static string BuildVirtualPlacementSequence(int imageId, int columns, int rows)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"\u001b_Ga=p,U=1,q=2,i={imageId},c={columns},r={rows}\u001b\\");
        }

        /// <summary>
        ///     Builds the APC sequence deleting an image and its placements, freeing the
        ///     image storage in the terminal.
        /// </summary>
        /// <summary>
        ///     Builds the APC sequence deleting the placements of an image while keeping its data,
        ///     so it can be placed again later without retransmission.
        /// </summary>
        public static string BuildDeletePlacementSequence(int imageId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"\u001b_Ga=d,d=i,q=2,i={imageId}\u001b\\");
        }

        // ---- classic rect placements (the "image as cell background" mode) ----

        /// <summary>
        ///     The z-index rect placements are created at: below text, above background colors,
        ///     so glyphs printed on the covered cells composite over the picture.
        /// </summary>
        public const int RectPlacementZIndex = -1;

        private static int _nextPlacementId;

        public static int AllocatePlacementId()
        {
            return (Interlocked.Increment(ref _nextPlacementId) - 1) % 0xFFFFFF + 1;
        }

        /// <summary>
        ///     Builds the APC sequence creating a classic placement showing a source-pixel crop of
        ///     an already transmitted image at the current cursor position, below text (z=-1),
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
        ///     Builds the APC sequence deleting one specific placement of an image while keeping
        ///     the image data, so other placements of the same image survive and re-placement
        ///     needs no retransmission.
        /// </summary>
        public static string BuildDeleteRectPlacementSequence(int imageId, int placementId)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"\u001b_Ga=d,d=i,q=2,i={imageId},p={placementId}\u001b\\");
        }

        public static string BuildDeleteSequence(int imageId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"\u001b_Ga=d,d=I,q=2,i={imageId}\u001b\\");
        }
    }
}
