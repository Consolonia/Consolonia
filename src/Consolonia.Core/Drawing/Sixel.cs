using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Avalonia.Platform;
using JeremyAnsel.ColorQuant;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    /// Represents a sixel image with palette and indexed pixel data.
    /// Supports composition via BitBlt and serialization via ToBytes.
    /// </summary>
    public class Sixel
    {
        /// <summary>BGRX palette, 4 bytes per entry.</summary>
        public byte[] Palette { get; }

        /// <summary>Number of colors in the palette.</summary>
        public int PaletteCount { get; }

        /// <summary>Indexed pixel data (one byte per pixel, index into Palette).</summary>
        public byte[] Pixels { get; }

        /// <summary>Pixel width of the image.</summary>
        public int Width { get; }

        /// <summary>Pixel height of the image.</summary>
        public int Height { get; }

        /// <summary>Original bitmap source (may be null).</summary>
        public IBitmapImpl Source { get; }

        public Sixel(byte[] palette, int paletteCount, byte[] pixels, int width, int height, IBitmapImpl source = null)
        {
            Palette = palette;
            PaletteCount = paletteCount;
            Pixels = pixels;
            Width = width;
            Height = height;
            Source = source;
        }

        /// <summary>
        /// Create a Sixel from raw BGRX pixel data.
        /// If a palette is provided it is used to quantize against, otherwise a new palette is created.
        /// </summary>
        public static Sixel CreateFromBitmap(byte[] bgrx, int width, int height, byte[] palette = null)
        {
            if (palette != null)
            {
                int paletteCount = palette.Length / 4;
                byte[] indexed = QuantizeWithPalette(bgrx, palette, paletteCount);
                return new Sixel(palette, paletteCount, indexed, width, height);
            }
            else
            {
                Quantize(bgrx, out byte[] newPalette, out int paletteCount, out byte[] indexed);
                return new Sixel(newPalette, paletteCount, indexed, width, height);
            }
        }

        /// <summary>
        /// Copy source image pixels into this image at pixel position (x, y).
        /// Clips if source extends beyond this image's bounds.
        /// </summary>
        public void BitBlt(Sixel source, int x, int y)
        {
            for (int row = 0; row < source.Height; row++)
            {
                int destY = y + row;
                if (destY < 0)
                    continue;
                if (destY >= Height)
                    break;

                int srcOffset = row * source.Width;
                int dstOffset = destY * Width + x;

                int srcX = 0;
                int dstX = x;

                if (dstX < 0)
                {
                    srcX = -dstX;
                    dstX = 0;
                    dstOffset = destY * Width;
                }

                int copyLen = Math.Min(source.Width - srcX, Width - dstX);
                if (copyLen <= 0)
                    continue;

                Array.Copy(source.Pixels, srcOffset + srcX, Pixels, dstOffset, copyLen);
            }
        }

        #region Serialization

        [ThreadStatic] private static byte[] _outputBuf;

        /// <summary>
        /// Serialize this image to SIXEL escape sequence bytes.
        /// The returned span is backed by a thread-static buffer and is valid until the next call to ToBytes.
        /// </summary>
        public ReadOnlySpan<byte> ToBytes()
        {
            int width = Width;
            int height = Height;
            byte[] palette = Palette;
            int paletteCount = PaletteCount;
            byte[] indexed = Pixels;

            int maxOutput = 64 + paletteCount * 20 + width * ((height + 5) / 6) * 4 + 4096;
            var output = RentOrGrow(ref _outputBuf, maxOutput);
            int pos = 0;

            // DCS q
            output[pos++] = 0x1B;
            output[pos++] = (byte)'P';
            output[pos++] = (byte)'q';

            // Raster attributes "1;1;W;H
            output[pos++] = (byte)'"';
            output[pos++] = (byte)'1';
            output[pos++] = (byte)';';
            output[pos++] = (byte)'1';
            output[pos++] = (byte)';';
            pos = WriteIntBuf(output, pos, width);
            output[pos++] = (byte)';';
            pos = WriteIntBuf(output, pos, height);

            // Palette: #idx;2;R%;G%;B%
            for (int i = 0; i < paletteCount; i++)
            {
                int r = palette[i * 4 + 2] * 100 / 255;
                int g = palette[i * 4 + 1] * 100 / 255;
                int b = palette[i * 4] * 100 / 255;

                output[pos++] = (byte)'#';
                pos = WriteIntBuf(output, pos, i);
                output[pos++] = (byte)';';
                output[pos++] = (byte)'2';
                output[pos++] = (byte)';';
                pos = WriteIntBuf(output, pos, r);
                output[pos++] = (byte)';';
                pos = WriteIntBuf(output, pos, g);
                output[pos++] = (byte)';';
                pos = WriteIntBuf(output, pos, b);
            }

            // Band encoding
            int bandCount = (height + 5) / 6;
            Span<bool> colorPresent = stackalloc bool[paletteCount];
            var sixelRow = ArrayPool<byte>.Shared.Rent(width);

            try
            {
                for (int band = 0; band < bandCount; band++)
                {
                    int yStart = band * 6;
                    int bandRows = Math.Min(6, height - yStart);

                    colorPresent.Clear();
                    for (int row = 0; row < bandRows; row++)
                    {
                        int rowOff = (yStart + row) * width;
                        for (int x = 0; x < width; x++)
                            colorPresent[indexed[rowOff + x]] = true;
                    }

                    int bandWorstCase = paletteCount * (width + 20);
                    if (pos + bandWorstCase > output.Length)
                    {
                        int newLen = Math.Max(output.Length * 2, pos + bandWorstCase + 4096);
                        var newBuf = new byte[newLen];
                        output.AsSpan(0, pos).CopyTo(newBuf);
                        _outputBuf = newBuf;
                        output = newBuf;
                    }

                    bool anyColor = false;
                    for (int color = 0; color < paletteCount; color++)
                    {
                        if (!colorPresent[color]) continue;

                        BuildSixelRow(indexed, sixelRow, width, yStart, bandRows, (byte)color);

                        if (anyColor)
                            output[pos++] = (byte)'$';

                        output[pos++] = (byte)'#';
                        pos = WriteIntBuf(output, pos, color);
                        pos = WriteRleBuf(output, pos, sixelRow, width);
                        anyColor = true;
                    }

                    if (band < bandCount - 1)
                        output[pos++] = (byte)'-';
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sixelRow);
            }

            // ST
            output[pos++] = 0x1B;
            output[pos++] = (byte)'\\';

            _outputBuf = output;
            return output.AsSpan(0, pos);
        }

        #endregion

        #region Quantization

        /// <summary>
        /// Quantize BGRX pixel data using Wu's variance-minimizing algorithm.
        /// </summary>
        private static void Quantize(byte[] bgrx,
            out byte[] palette, out int paletteCount, out byte[] indexed)
        {
            var quantizer = new WuColorQuantizer();
            var result = quantizer.Quantize(bgrx, 256);

            palette = result.Palette;
            paletteCount = palette.Length / 4;
            indexed = result.Bytes;
        }

        /// <summary>
        /// Map BGRX pixel data to an existing palette using nearest-color matching.
        /// </summary>
        private static byte[] QuantizeWithPalette(byte[] bgrx, byte[] palette, int paletteCount)
        {
            int pixelCount = bgrx.Length / 4;
            byte[] indexed = GC.AllocateUninitializedArray<byte>(pixelCount);

            for (int i = 0; i < pixelCount; i++)
            {
                int off = i * 4;
                int b = bgrx[off];
                int g = bgrx[off + 1];
                int r = bgrx[off + 2];

                int bestIdx = 0;
                int bestDist = int.MaxValue;
                for (int c = 0; c < paletteCount; c++)
                {
                    int po = c * 4;
                    int db = b - palette[po];
                    int dg = g - palette[po + 1];
                    int dr = r - palette[po + 2];
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = c;
                    }
                }
                indexed[i] = (byte)bestIdx;
            }

            return indexed;
        }

        #endregion

        #region SIMD helpers

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void BuildSixelRow(byte[] indexed, byte[] sixelRow, int width, int yStart, int bandRows, byte color)
        {
            ref byte rows0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(indexed), yStart * width);
            ref byte outRef = ref MemoryMarshal.GetArrayDataReference(sixelRow);

            if (Vector256.IsHardwareAccelerated && width >= 32)
            {
                var vColor = Vector256.Create(color);
                var v63 = Vector256.Create((byte)63);

                int x = 0;
                for (; x + 32 <= width; x += 32)
                {
                    var bits = Vector256<byte>.Zero;

                    var eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)x), vColor);
                    bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)1)));

                    if (bandRows > 1)
                    {
                        eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)(width + x)), vColor);
                        bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)2)));
                    }
                    if (bandRows > 2)
                    {
                        eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)(width * 2 + x)), vColor);
                        bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)4)));
                    }
                    if (bandRows > 3)
                    {
                        eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)(width * 3 + x)), vColor);
                        bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)8)));
                    }
                    if (bandRows > 4)
                    {
                        eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)(width * 4 + x)), vColor);
                        bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)16)));
                    }
                    if (bandRows > 5)
                    {
                        eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)(width * 5 + x)), vColor);
                        bits = Vector256.BitwiseOr(bits, Vector256.BitwiseAnd(eq, Vector256.Create((byte)32)));
                    }

                    Vector256.Add(bits, v63).StoreUnsafe(ref outRef, (nuint)x);
                }

                for (; x < width; x++)
                    Unsafe.Add(ref outRef, x) = BuildSixelScalar(ref rows0, x, width, bandRows, color);
            }
            else if (Vector128.IsHardwareAccelerated && width >= 16)
            {
                var vColor = Vector128.Create(color);
                var v63 = Vector128.Create((byte)63);

                int x = 0;
                for (; x + 16 <= width; x += 16)
                {
                    var bits = Vector128<byte>.Zero;

                    var eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)x), vColor);
                    bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)1)));

                    if (bandRows > 1)
                    {
                        eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)(width + x)), vColor);
                        bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)2)));
                    }
                    if (bandRows > 2)
                    {
                        eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)(width * 2 + x)), vColor);
                        bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)4)));
                    }
                    if (bandRows > 3)
                    {
                        eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)(width * 3 + x)), vColor);
                        bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)8)));
                    }
                    if (bandRows > 4)
                    {
                        eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)(width * 4 + x)), vColor);
                        bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)16)));
                    }
                    if (bandRows > 5)
                    {
                        eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)(width * 5 + x)), vColor);
                        bits = Vector128.BitwiseOr(bits, Vector128.BitwiseAnd(eq, Vector128.Create((byte)32)));
                    }

                    Vector128.Add(bits, v63).StoreUnsafe(ref outRef, (nuint)x);
                }

                for (; x < width; x++)
                    Unsafe.Add(ref outRef, x) = BuildSixelScalar(ref rows0, x, width, bandRows, color);
            }
            else
            {
                for (int x = 0; x < width; x++)
                    Unsafe.Add(ref outRef, x) = BuildSixelScalar(ref rows0, x, width, bandRows, color);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte BuildSixelScalar(ref byte rows0, int x, int width, int bandRows, byte color)
        {
            int bits = 0;
            if (Unsafe.Add(ref rows0, x) == color) bits |= 1;
            if (bandRows > 1 && Unsafe.Add(ref rows0, width + x) == color) bits |= 2;
            if (bandRows > 2 && Unsafe.Add(ref rows0, width * 2 + x) == color) bits |= 4;
            if (bandRows > 3 && Unsafe.Add(ref rows0, width * 3 + x) == color) bits |= 8;
            if (bandRows > 4 && Unsafe.Add(ref rows0, width * 4 + x) == color) bits |= 16;
            if (bandRows > 5 && Unsafe.Add(ref rows0, width * 5 + x) == color) bits |= 32;
            return (byte)(bits + 63);
        }

        #endregion

        #region Buffer helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteIntBuf(byte[] buf, int pos, int value)
        {
            if (value < 10)
            {
                buf[pos] = (byte)('0' + value);
                return pos + 1;
            }
            if (value < 100)
            {
                buf[pos] = (byte)('0' + value / 10);
                buf[pos + 1] = (byte)('0' + value % 10);
                return pos + 2;
            }
            int tmp = value;
            int digits = 0;
            while (tmp > 0) { digits++; tmp /= 10; }
            pos += digits;
            int p = pos;
            while (value > 0)
            {
                buf[--p] = (byte)('0' + value % 10);
                value /= 10;
            }
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteRleBuf(byte[] output, int pos, byte[] data, int length)
        {
            int i = 0;
            while (i < length)
            {
                byte ch = data[i];
                int run = 1;
                while (i + run < length && data[i + run] == ch)
                    run++;

                if (run >= 4)
                {
                    output[pos++] = (byte)'!';
                    pos = WriteIntBuf(output, pos, run);
                    output[pos++] = ch;
                }
                else if (run == 3)
                {
                    output[pos++] = ch;
                    output[pos++] = ch;
                    output[pos++] = ch;
                }
                else if (run == 2)
                {
                    output[pos++] = ch;
                    output[pos++] = ch;
                }
                else
                {
                    output[pos++] = ch;
                }
                i += run;
            }
            return pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T[] RentOrGrow<T>(ref T[] buf, int minSize)
        {
            if (buf == null || buf.Length < minSize)
                buf = GC.AllocateUninitializedArray<T>(Math.Max(minSize, 4096));
            return buf;
        }

        #endregion
    }
}
