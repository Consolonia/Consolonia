using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    /// <summary>
    ///     The pixels one cell of a picture shows: a cell-sized BGRA image a <see cref="PixelBackground" />
    ///     refers to. What the terminal is sent for it (a sixel, a kitty image, a block glyph) is the
    ///     console output's business; the pixel buffer only holds the reference.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Compared by reference. The renderer that makes cell images keeps one instance per distinct
    ///         content (see <see cref="ContentCache{T}" />), so two cells showing the same pixels share one
    ///         instance and compare equal, and a cell whose pixels did not change keeps its instance and is
    ///         not written again. The bytes are a copy the image owns: a picture is edited in place and
    ///         scaled to the cells it is drawn at, so nothing else holds these pixels unchanged.
    ///     </para>
    ///     <para>
    ///         The pixels may sit in an array shared with other cell images made in the same render (a
    ///         slab), at an offset: one allocation for a handful of cells rather than an array header and a
    ///         heap object per cell. An instance is 56 bytes: the slab reference, its offset, the size, the
    ///         16-byte digest the renderer finds it by, and the dominant color once computed.
    ///     </para>
    /// </remarks>
    public sealed class CellImage
    {
        private readonly int _offset;
        private readonly byte[] _pixels;

        /// <summary>The dominant color as ARGB once computed, 0 (which an opaque color never is) until then.</summary>
        private uint _dominantColor;

        /// <param name="bgra">Exactly <paramref name="width" /> x <paramref name="height" /> pixels, 4 bytes each, row by row.</param>
        /// <param name="width">Width of the cell in pixels.</param>
        /// <param name="height">Height of the cell in pixels.</param>
        /// <exception cref="ArgumentException"><paramref name="bgra" /> is not exactly the pixels of the cell.</exception>
        public CellImage(byte[] bgra, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(bgra);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (bgra.Length != width * height * 4)
                throw new ArgumentException("The pixels are not exactly those of the cell.", nameof(bgra));

            _pixels = bgra;
            Width = width;
            Height = height;
            Digest = XxHash128.HashToUInt128(bgra);
        }

        /// <summary>
        ///     A cell image whose pixels are the <paramref name="width" /> x <paramref name="height" /> x 4
        ///     bytes of <paramref name="pixels" /> from <paramref name="offset" />, already hashed to
        ///     <paramref name="digest" />. For the renderer, which hashes a cell to look it up before it
        ///     decides to keep it, and keeps the new cells of one render in one array.
        /// </summary>
        internal CellImage(byte[] pixels, int offset, int width, int height, UInt128 digest)
        {
            _pixels = pixels;
            _offset = offset;
            Width = width;
            Height = height;
            Digest = digest;
        }

        /// <summary>The pixels, 4 bytes each (blue, green, red, alpha), row by row.</summary>
        public ReadOnlySpan<byte> Bgra => _pixels.AsSpan(_offset, Width * Height * 4);

        /// <summary>Width of the cell in pixels.</summary>
        public int Width { get; }

        /// <summary>Height of the cell in pixels.</summary>
        public int Height { get; }

        /// <summary>What the pixels show, as a hash: how the renderer finds the instance for a block of pixels.</summary>
        internal ContentKey Key => new(Digest, Width, Height);

        /// <summary>The 128-bit hash of the pixels; what the encoders describe a cell by.</summary>
        internal UInt128 Digest { get; }

        /// <summary>
        ///     The opaque color covering most of the cell, computed on first use. Where a cell needs a
        ///     single color (a glyph a protocol cannot draw over the picture, the mouse cursor's contrast),
        ///     this keeps it looking like the picture rather than a hole in it.
        /// </summary>
        public Color DominantColor
        {
            get
            {
                uint dominant = _dominantColor;
                if (dominant == 0)
                {
                    dominant = FindDominantColor();
                    _dominantColor = dominant;
                }

                return Color.FromUInt32(dominant);
            }
        }

        /// <summary>
        ///     Composites <paramref name="paint" /> over the <paramref name="width" /> x <paramref name="height" />
        ///     BGRA pixels at <paramref name="offset" /> of an image <paramref name="rowBytes" /> wide: what a
        ///     translucent overlay on a cell does to the picture when the cell is encoded.
        /// </summary>
        internal static void Paint(Span<byte> bgra, int rowBytes, int offset, int width, int height, Color paint)
        {
            int alpha = paint.A;
            int inverse = 255 - alpha;
            int blue = paint.B * alpha;
            int green = paint.G * alpha;
            int red = paint.R * alpha;
            for (int y = 0; y < height; y++)
            {
                int i = offset + y * rowBytes;
                for (int x = 0; x < width; x++, i += 4)
                {
                    bgra[i] = (byte)((blue + bgra[i] * inverse) / 255);
                    bgra[i + 1] = (byte)((green + bgra[i + 1] * inverse) / 255);
                    bgra[i + 2] = (byte)((red + bgra[i + 2] * inverse) / 255);
                }
            }
        }

        /// <summary>
        ///     The most frequent color, alpha left out (a translucent picture's colors are still its colors),
        ///     as opaque ARGB. The colors are sorted on the stack and the longest run wins: no dictionary,
        ///     and for the few hundred pixels of a cell faster than one.
        /// </summary>
        private uint FindDominantColor()
        {
            ReadOnlySpan<uint> pixels = MemoryMarshal.Cast<byte, uint>(Bgra);
            Span<uint> colors = pixels.Length <= 2048 ? stackalloc uint[pixels.Length] : new uint[pixels.Length];
            // a BGRA pixel read as a little-endian uint is ARGB; the mask leaves RGB
            for (int i = 0; i < pixels.Length; i++)
                colors[i] = pixels[i] & 0x00FFFFFF;
            colors.Sort();

            uint best = colors[0];
            int bestCount = 0;
            int runStart = 0;
            for (int i = 1; i <= colors.Length; i++)
            {
                if (i < colors.Length && colors[i] == colors[runStart])
                    continue;

                if (i - runStart > bestCount)
                {
                    bestCount = i - runStart;
                    best = colors[runStart];
                }

                runStart = i;
            }

            return 0xFF000000 | best;
        }
    }
}