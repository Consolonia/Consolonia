using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;
using Avalonia.Media;
using JeremyAnsel.ColorQuant;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Represents a sixel image with palette and indexed pixel data, serialized via Render.
    /// </summary>
    public sealed class Sixel
    {
        /// <exception cref="ArgumentException">
        ///     The arrays are smaller than the counts and dimensions say. Render reads them with unchecked
        ///     offsets, so they are checked here.
        /// </exception>
        public Sixel(byte[] palette, int paletteCount, byte[] pixels, int width, int height,
            int cellWidth, int cellHeight)
        {
            ArgumentNullException.ThrowIfNull(palette);
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paletteCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(paletteCount, 256);
            if (palette.Length < paletteCount * 4)
                throw new ArgumentException("The palette holds fewer colors than paletteCount.", nameof(palette));
            if (pixels.Length < width * height)
                throw new ArgumentException("There are fewer pixels than width * height.", nameof(pixels));

            Palette = palette;
            PaletteCount = paletteCount;
            Pixels = pixels;
            Width = width;
            Height = height;
            CellWidth = cellWidth;
        }

        /// <summary>BGRX palette, 4 bytes per entry.</summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
            Justification = "The sixel hot path uses the backing array directly to avoid extra copies.")]
        public byte[] Palette { get; }

        /// <summary>Number of colors in the palette.</summary>
        public int PaletteCount { get; }

        /// <summary>Indexed pixel data (one byte per pixel, index into Palette).</summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
            Justification = "The sixel hot path uses the backing array directly to avoid extra copies.")]
        public byte[] Pixels { get; }

        /// <summary>Pixel width of the image.</summary>
        public int Width { get; }

        /// <summary>Pixel height of the image.</summary>
        public int Height { get; }

        /// <summary>Width of a single cell in pixels.</summary>
        public int CellWidth { get; }

        /// <summary>Width of this image in cells.</summary>
        public int CellsWidth => Width / CellWidth;

        /// <summary>
        ///     Create a Sixel from raw BGRX pixel data, quantized to a palette of its own.
        /// </summary>
        /// <param name="bgrx">The pixels, 4 bytes each; the fourth byte is ignored.</param>
        /// <param name="width">Width of the image in pixels.</param>
        /// <param name="height">Height of the image in pixels.</param>
        /// <param name="cellWidth">Width of a cell in pixels.</param>
        /// <param name="cellHeight">Height of a cell in pixels.</param>
        /// <param name="transient">Whether the image is for a single write; see <see cref="IsTransient" />.</param>
        public static Sixel CreateFromBitmap(byte[] bgrx, int width, int height, int cellWidth, int cellHeight,
            bool transient = false)
        {
            ArgumentNullException.ThrowIfNull(bgrx);
            // Render and BuildSixelRow index Pixels with unchecked Unsafe.Add offsets derived from
            // width * height, so a short buffer would read past the array
            if (width <= 0 || height <= 0 || bgrx.Length < width * height * 4)
                throw new ArgumentException("Bitmap size does not match the given dimensions.", nameof(bgrx));

            // only this image's pixels: a pooled buffer can be longer, and its tail would sway the palette
            int byteCount = width * height * 4;
            if (bgrx.Length > byteCount)
                bgrx = bgrx[..byteCount];

            Quantize(bgrx, out byte[] palette, out int paletteCount, out byte[] indexed);
            return new Sixel(palette, paletteCount, indexed, width, height, cellWidth, cellHeight)
                { IsTransient = transient };
        }


        #region Serialization

        [ThreadStatic] private static byte[] _scratchRenderBuf;
        [ThreadStatic] private static WuColorQuantizer _quantizer;

        private byte[] _renderedBytes;

        /// <summary>
        ///     An image made for a single write, such as the one the renderer combines from neighbouring
        ///     cells each frame. <see cref="Render" /> does not keep its bytes: it returns them straight
        ///     from a per-thread scratch buffer, valid only until the next render on the same thread.
        /// </summary>
        internal bool IsTransient { get; init; }

        /// <summary>
        ///     Serialize this image to SIXEL escape sequence bytes.
        ///     The returned span is cached on the instance after the first render, unless the image is
        ///     <see cref="IsTransient" />.
        /// </summary>
        public ReadOnlySpan<byte> Render()
        {
            if (_renderedBytes != null)
                return _renderedBytes;

            int width = Width;
            int height = Height;
            byte[] palette = Palette;
            int paletteCount = PaletteCount;
            byte[] indexed = Pixels;

            // Only the colors the image uses are defined. A cell uses a handful of a palette shared by a
            // whole picture, and defining all of them made up most of the bytes sent.
            Span<bool> used = stackalloc bool[paletteCount];
            int usedCount = 0;
            foreach (byte index in indexed.AsSpan(0, width * height))
                if (!used[index])
                {
                    used[index] = true;
                    usedCount++;
                }

            int maxOutput = 64 + usedCount * 20 + width * ((height + 5) / 6) * 4 + 4096;
            byte[] output = RentOrGrow(ref _scratchRenderBuf, maxOutput);
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
                if (!used[i])
                    continue;

                // rounded, not truncated: 254 truncated to 99%, which the terminal reads back as 252
                int r = (palette[i * 4 + 2] * 100 + 127) / 255;
                int g = (palette[i * 4 + 1] * 100 + 127) / 255;
                int b = (palette[i * 4] * 100 + 127) / 255;

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

            // Band encoding: 6 pixel rows per band, '$' returns to column 0 for the next color, '-' ends the band
            int bandCount = (height + 5) / 6;
            Span<bool> colorPresent = stackalloc bool[paletteCount];
            byte[] sixelRow = ArrayPool<byte>.Shared.Rent(width);

            try
            {
                for (int band = 0; band < bandCount; band++)
                {
                    int yStart = band * 6;
                    int bandRows = Math.Min(6, height - yStart);

                    colorPresent.Clear();
                    int bandColors = 0;
                    for (int row = 0; row < bandRows; row++)
                    {
                        int rowOff = (yStart + row) * width;
                        for (int x = 0; x < width; x++)
                        {
                            ref bool present = ref colorPresent[indexed[rowOff + x]];
                            if (!present)
                            {
                                present = true;
                                bandColors++;
                            }
                        }
                    }

                    int bandWorstCase = bandColors * (width + 20);
                    if (pos + bandWorstCase > output.Length)
                    {
                        int newLen = Math.Max(output.Length * 2, pos + bandWorstCase + 4096);
                        byte[] newBuf = new byte[newLen];
                        output.AsSpan(0, pos).CopyTo(newBuf);
                        _scratchRenderBuf = newBuf;
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

            if (IsTransient)
                return output.AsSpan(0, pos);

            byte[] rendered = GC.AllocateUninitializedArray<byte>(pos);
            output.AsSpan(0, pos).CopyTo(rendered);
            _renderedBytes = rendered;
            return rendered;
        }

        #endregion

        #region Quantization

        /// <summary>
        ///     Quantize BGRX pixel data: exactly when it has at most 256 colors, otherwise using Wu's
        ///     variance-minimizing algorithm.
        /// </summary>
        /// <param name="bgrx">The pixels, 4 bytes each; the fourth byte is ignored.</param>
        /// <param name="palette">The colors, 4 bytes each, <paramref name="paletteCount" /> of them used.</param>
        /// <param name="paletteCount">How many colors the palette holds, at most 256.</param>
        /// <param name="indexed">One palette index per pixel, in the order of <paramref name="bgrx" />.</param>
        internal static void Quantize(byte[] bgrx,
            out byte[] palette, out int paletteCount, out byte[] indexed)
        {
            if (TryIndexExactly(bgrx, out palette, out paletteCount, out indexed))
                return;

            WuColorQuantizer quantizer = _quantizer ??= new WuColorQuantizer();
            ColorQuantizerResult result = quantizer.Quantize(bgrx, 256);

            palette = result.Palette;
            paletteCount = palette.Length / 4;
            indexed = result.Bytes;
        }

        /// <summary>
        ///     Indexes pixels with at most 256 distinct colors exactly: the palette is those colors. Gives
        ///     up as soon as a 257th color appears.
        /// </summary>
        /// <remarks>
        ///     Wu's quantizer has a large fixed cost however few pixels it is given (its color histogram
        ///     and box cutting don't shrink with the image), while the cells a brush stroke changes hold a
        ///     handful of colors. Exact indexing is cheaper for them and loses nothing.
        /// </remarks>
        private static bool TryIndexExactly(byte[] bgrx,
            out byte[] palette, out int paletteCount, out byte[] indexed)
        {
            int pixelCount = bgrx.Length / 4;
            var indices = new Dictionary<int, byte>(64);
            // rented: a photo gives up at its 257th color, usually within the first rows, and the result
            // for a whole screen would be a large-object allocation thrown away
            byte[] result = ArrayPool<byte>.Shared.Rent(pixelCount);
            int lastColor = -1;
            byte lastIndex = 0;

            for (int i = 0, offset = 0; i < pixelCount; i++, offset += 4)
            {
                int color = bgrx[offset] | (bgrx[offset + 1] << 8) | (bgrx[offset + 2] << 16);
                if (color != lastColor)
                {
                    if (!indices.TryGetValue(color, out lastIndex))
                    {
                        if (indices.Count == 256)
                        {
                            ArrayPool<byte>.Shared.Return(result);
                            palette = null;
                            paletteCount = 0;
                            indexed = null;
                            return false;
                        }

                        lastIndex = (byte)indices.Count;
                        indices.Add(color, lastIndex);
                    }

                    lastColor = color;
                }

                result[i] = lastIndex;
            }

            paletteCount = indices.Count;
            palette = new byte[paletteCount * 4];
            foreach ((int color, byte index) in indices)
            {
                int offset = index * 4;
                palette[offset] = (byte)color;
                palette[offset + 1] = (byte)(color >> 8);
                palette[offset + 2] = (byte)(color >> 16);
                palette[offset + 3] = 0xFF;
            }

            indexed = GC.AllocateUninitializedArray<byte>(pixelCount);
            result.AsSpan(0, pixelCount).CopyTo(indexed);
            ArrayPool<byte>.Shared.Return(result);
            return true;
        }

        #endregion

        #region SIMD helpers

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void BuildSixelRow(byte[] indexed, byte[] sixelRow, int width, int yStart, int bandRows,
            byte color)
        {
            ref byte rows0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(indexed), yStart * width);
            ref byte outRef = ref MemoryMarshal.GetArrayDataReference(sixelRow);

            if (Vector256.IsHardwareAccelerated && width >= 32)
            {
                Vector256<byte> vColor = Vector256.Create(color);
                var v63 = Vector256.Create((byte)63);

                int x = 0;
                for (; x + 32 <= width; x += 32)
                {
                    Vector256<byte> bits = Vector256<byte>.Zero;

                    Vector256<byte> eq = Vector256.Equals(Vector256.LoadUnsafe(ref rows0, (nuint)x), vColor);
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
                Vector128<byte> vColor = Vector128.Create(color);
                var v63 = Vector128.Create((byte)63);

                int x = 0;
                for (; x + 16 <= width; x += 16)
                {
                    Vector128<byte> bits = Vector128<byte>.Zero;

                    Vector128<byte> eq = Vector128.Equals(Vector128.LoadUnsafe(ref rows0, (nuint)x), vColor);
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
            while (tmp > 0)
            {
                digits++;
                tmp /= 10;
            }

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

        /// <summary>
        ///     A per-thread scratch buffer of at least <paramref name="minSize" />. One very large render
        ///     does not pin its buffer for the life of the thread: it is replaced once renders are small again.
        /// </summary>
        private static T[] RentOrGrow<T>(ref T[] buf, int minSize)
        {
            const int keepAtMost = 4 * 1024 * 1024;
            if (buf == null || buf.Length < minSize || buf.Length > keepAtMost && minSize <= keepAtMost / 4)
                buf = GC.AllocateUninitializedArray<T>(Math.Max(minSize, 4096));
            return buf;
        }

        #endregion
    }
}