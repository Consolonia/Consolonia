using System;
using Avalonia.Media;

namespace Consolonia.Imaging
{
    internal static class BgraExtensions
    {
        /// <summary>
        ///     Composites <paramref name="paint" /> over the <paramref name="width" /> x <paramref name="height" />
        ///     BGRA pixels at <paramref name="offset" /> of an image <paramref name="rowBytes" /> wide: what a
        ///     translucent overlay on a cell does to the picture when the cell is encoded.
        /// </summary>
        public static void Paint(this Span<byte> bgra, int rowBytes, int offset, int width, int height, Color paint)
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
    }
}