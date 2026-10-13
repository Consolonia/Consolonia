using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    /// <summary>
    ///     The pixels one cell of a picture shows: a cell-sized BGRA image a <see cref="PixelBackground" />
    ///     refers to. What the terminal is sent for it (a sixel, a kitty image, a block glyph) is the
    ///     console output's business; the pixel buffer only holds the reference.
    /// </summary>
    /// <remarks>
    ///     Compared by reference. The renderer that makes cell images keeps one instance per distinct
    ///     content (see <see cref="ContentCache{T}" />), so two cells showing the same pixels share one
    ///     instance and compare equal, and a cell whose pixels did not change keeps its instance and is
    ///     not written again. The bytes are a copy the image owns: a picture is edited in place and
    ///     scaled to the cells it is drawn at, so nothing else holds these pixels unchanged.
    /// </remarks>
    public sealed class CellImage
    {
        private Color? _dominantColor;

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

            Bgra = bgra;
            Width = width;
            Height = height;
            Key = ContentKey.Of(bgra, width, height);
        }

        /// <summary>The pixels, 4 bytes each (blue, green, red, alpha), row by row. Not to be changed.</summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
            Justification = "The encoders read the backing array directly to avoid copies")]
        public byte[] Bgra { get; }

        /// <summary>Width of the cell in pixels.</summary>
        public int Width { get; }

        /// <summary>Height of the cell in pixels.</summary>
        public int Height { get; }

        /// <summary>What the pixels show, as a hash: how the renderer finds the instance for a block of pixels.</summary>
        internal ContentKey Key { get; }

        /// <summary>
        ///     The opaque color covering most of the cell, computed on first use. Where a cell needs a
        ///     single color (a glyph a protocol cannot draw over the picture, the mouse cursor's contrast),
        ///     this keeps it looking like the picture rather than a hole in it.
        /// </summary>
        public Color DominantColor
        {
            get
            {
                Color? dominant = _dominantColor;
                if (dominant == null)
                {
                    dominant = FindDominantColor();
                    _dominantColor = dominant;
                }

                return dominant.Value;
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

        private Color FindDominantColor()
        {
            var counts = new Dictionary<int, int>();
            int best = 0;
            int bestCount = 0;
            byte[] bgra = Bgra;
            for (int offset = 0; offset < bgra.Length; offset += 4)
            {
                // alpha left out: a translucent picture's colors are still its colors
                int color = bgra[offset] | (bgra[offset + 1] << 8) | (bgra[offset + 2] << 16);
                int count = counts.GetValueOrDefault(color) + 1;
                counts[color] = count;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = color;
                }
            }

            return Color.FromRgb((byte)(best >> 16), (byte)(best >> 8), (byte)best);
        }
    }
}
