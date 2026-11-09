using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    /// <summary>
    ///     The pixels of one cell of an image. It is a cell-sized BGRA image that a
    ///     <see cref="PixelBackground" /> refers to. The console output decides what to send to the terminal
    ///     for it (a sixel, a kitty image, or a block character). The pixel buffer only holds the reference.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Instances are compared by reference. The converter keeps one instance per distinct content
    ///         (see <see cref="PixelImageCache" />). So two cells with the same pixels share one instance and
    ///         compare equal. A cell whose pixels did not change keeps its instance and is not written again.
    ///         The image owns a copy of its pixels. This is needed because the source bitmap can be edited in
    ///         place and is scaled to the drawn size, so no other buffer keeps these exact pixels.
    ///     </para>
    ///     <para>
    ///         The pixels may be stored at an offset in an array shared with other pixel images (a slab).
    ///         One allocation then serves many cells, instead of one array and one heap object per cell.
    ///         An instance holds the slab reference, the offset, the width and height, the 16-byte digest,
    ///         and the cached dominant color (about 56 bytes).
    ///     </para>
    /// </remarks>
    public sealed class PixelImage
    {
        private readonly int _offset;
        private readonly byte[] _pixels;

        /// <summary>
        ///     The cached dominant color as ARGB. It is 0 until computed. A computed value is always opaque,
        ///     so it is never 0.
        /// </summary>
        private uint _dominantColor;

        /// <param name="bgra">Exactly <paramref name="width" /> x <paramref name="height" /> pixels, 4 bytes each, row by row.</param>
        /// <param name="width">Width of the cell in pixels.</param>
        /// <param name="height">Height of the cell in pixels.</param>
        /// <exception cref="ArgumentException">
        ///     <paramref name="bgra" /> does not have exactly width x height x 4 bytes.
        /// </exception>
        public PixelImage(byte[] bgra, int width, int height)
            : this(bgra ?? throw new ArgumentNullException(nameof(bgra)), 0, width, height,
                XxHash128.HashToUInt128(bgra))
        {
            if (bgra.Length != width * height * 4)
                throw new ArgumentException("The pixels are not exactly those of the cell.", nameof(bgra));
        }

        /// <summary>
        ///     Creates a pixel image from <paramref name="width" /> x <paramref name="height" /> x 4 bytes of
        ///     <paramref name="pixels" />, starting at <paramref name="offset" />. <paramref name="digest" /> is
        ///     the precomputed hash of those bytes. Used by the converter, which hashes a cell to look it up in
        ///     the cache before creating it, and stores new cells in a shared array (a slab).
        /// </summary>
        /// <exception cref="ArgumentException">The pixels extend past the end of <paramref name="pixels" />.</exception>
        internal PixelImage(byte[] pixels, int offset, int width, int height, UInt128 digest)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (pixels.Length - offset < width * height * 4)
                throw new ArgumentException("The pixels run past the end of the array.", nameof(pixels));

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

        /// <summary>
        ///     The 128-bit XXH3 hash of the pixels. The cache uses it as the lookup key, and encoders use it to
        ///     identify the image.
        /// </summary>
        public UInt128 Digest { get; }

        /// <summary>
        ///     The most common color in the cell, as an opaque color. It is computed on first use. It is used
        ///     where a cell needs a single color, for example for text that the image protocol cannot draw over
        ///     the image, or for the mouse cursor contrast. This keeps the cell close to the image instead of
        ///     showing a hole.
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
        ///     Returns the most frequent color as opaque ARGB. Alpha is ignored when counting, so translucent
        ///     pixels count by their RGB color. The colors are sorted (on the stack for small cells) and the
        ///     longest run of equal values wins. For the few hundred pixels in a cell this is faster than a
        ///     dictionary.
        /// </summary>
        private uint FindDominantColor()
        {
            ReadOnlySpan<uint> pixels = MemoryMarshal.Cast<byte, uint>(Bgra);
            Span<uint> colors = pixels.Length <= 2048 ? stackalloc uint[pixels.Length] : new uint[pixels.Length];
            // A BGRA pixel read as a little-endian uint is ARGB. The mask removes the alpha.
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