//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using JeremyAnsel.ColorQuant;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    /// High-performance SIXEL encoder.
    /// Takes pre-quantized indexed pixel data and palette.
    /// Uses SIMD for bitmask building, pooled buffers, direct byte[] output.
    /// </summary>
    static class SixelEncoder
    {
        [ThreadStatic] private static byte[] _outputBuf;

        public static byte[] Encode(byte[] indexed, int width, int height, byte[] palette, int paletteCount)
        {
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

            var result = GC.AllocateUninitializedArray<byte>(pos);
            output.AsSpan(0, pos).CopyTo(result);
            _outputBuf = output;
            return result;
        }

        /// <summary>
        /// Quantize BGRX pixel data using Wu's variance-minimizing algorithm.
        /// Returns BGRX palette (4 bytes per entry) and indexed pixel data.
        /// </summary>
        public static void Quantize(byte[] bgrx,
            out byte[] palette, out int paletteCount, out byte[] indexed)
        {
            var quantizer = new WuColorQuantizer();
            var result = quantizer.Quantize(bgrx, 256);

            palette = result.Palette;
            paletteCount = palette.Length / 4;
            indexed = result.Bytes;
        }


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
    }

}