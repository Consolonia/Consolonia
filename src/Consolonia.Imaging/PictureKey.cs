using System;
using System.IO.Hashing;

namespace Consolonia.Imaging
{
    /// <summary>
    ///     What an encoder sends for part of the screen, by what it shows: a 128-bit XXH3 hash of a
    ///     description of its cells (each cell's <c>PixelImage.Digest</c> and paint) and its size in pixels.
    ///     Two different pictures never share a key in practice, so nothing needs keeping to compare against.
    /// </summary>
    internal readonly record struct PictureKey(UInt128 Digest, int Width, int Height)
    {
        public static PictureKey Of(ReadOnlySpan<byte> description, int width, int height)
        {
            return new PictureKey(XxHash128.HashToUInt128(description), width, height);
        }
    }
}