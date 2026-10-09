using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Avalonia.Media;
using JeremyAnsel.ColorQuant;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Represents a sixel image with palette and indexed pixel data.
    ///     Supports composition via BitBlt and serialization via ToBytes.
    /// </summary>
    public class Sixel
    {
        public Sixel(byte[] palette, int paletteCount, byte[] pixels, int width, int height,
            int cellWidth, int cellHeight)
        {
            Palette = palette;
            PaletteCount = paletteCount;
            Pixels = pixels;
            Width = width;
            Height = height;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
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

        /// <summary>Height of a single cell in pixels.</summary>
        public int CellHeight { get; }

        /// <summary>Width of this image in cells.</summary>
        public int CellsWidth => Width / CellWidth;

        /// <summary>Height of this image in cells.</summary>
        public int CellsHeight => Height / CellHeight;

        /// <summary>
        ///     The palette color covering the most pixels. A glyph drawn over a sixel cell turns it into a
        ///     text cell, and a glyph with a transparent background takes this as its background so the
        ///     cell still looks like the picture instead of a hole in it.
        /// </summary>
        /// <remarks>
        ///     Only the index is cached, so a washed variant (same pixels, washed palette) yields the
        ///     washed color.
        /// </remarks>
        public Color DominantColor
        {
            get
            {
                int index = _dominantIndex;
                if (index < 0)
                {
                    Span<int> counts = stackalloc int[256];
                    foreach (byte pixel in Pixels)
                        counts[pixel]++;

                    index = 0;
                    for (int i = 1; i < counts.Length; i++)
                        if (counts[i] > counts[index])
                            index = i;
                    _dominantIndex = index;
                }

                int offset = index * 4;
                return Color.FromRgb(Palette[offset + 2], Palette[offset + 1], Palette[offset]);
            }
        }

        /// <summary>
        ///     Create a Sixel from raw BGRX pixel data.
        ///     If a palette is provided it is used to quantize against, otherwise a new palette is created.
        /// </summary>
        public static Sixel CreateFromBitmap(byte[] bgrx, int width, int height,
            int cellWidth, int cellHeight, byte[] palette = null)
        {
            ArgumentNullException.ThrowIfNull(bgrx);
            // Render and BuildSixelRow index Pixels with unchecked Unsafe.Add offsets derived from
            // width * height, so a short buffer would read past the array
            if (width <= 0 || height <= 0 || bgrx.Length < width * height * 4)
                throw new ArgumentException("Bitmap size does not match the given dimensions.", nameof(bgrx));

            if (palette != null)
            {
                int paletteCount = palette.Length / 4;
                byte[] indexed = QuantizeWithPalette(bgrx, palette);
                return new Sixel(palette, paletteCount, indexed, width, height, cellWidth, cellHeight);
            }
            else
            {
                Quantize(bgrx, out byte[] newPalette, out int paletteCount, out byte[] indexed);
                return new Sixel(newPalette, paletteCount, indexed, width, height, cellWidth, cellHeight);
            }
        }

        /// <summary>
        ///     Copy source image pixels into this image at pixel position (x, y).
        ///     Clips if source extends beyond this image's bounds.
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

            _renderedBytes = null;
            _dominantIndex = -1;
        }

        /// <summary>
        ///     Returns this image with <paramref name="wash" /> alpha-composited over every palette
        ///     color, which is how a translucent overlay (a modal backdrop, a shade) tints a sixel.
        ///     Only the palette changes, so the copy shares <see cref="Pixels" /> with this image.
        /// </summary>
        /// <remarks>
        ///     Variants are cached: overlays are re-blended onto the pixel buffer every frame, and
        ///     since symbols compare sixels by reference, returning the same instance is what keeps an
        ///     unchanged dimmed image from being re-sent to the terminal. Cells of one image also share
        ///     the derived palette, so the renderer can still combine them.
        /// </remarks>
        public Sixel Wash(Color wash)
        {
            if (wash.A == 0)
                return this;

            return GetOrCreateVariant(wash, () =>
                new Sixel(GetWashedPalette(Palette, PaletteCount, wash), PaletteCount, Pixels, Width, Height,
                    CellWidth, CellHeight));
        }

        private Sixel GetOrCreateVariant(Color color, Func<Sixel> create)
        {
            lock (_variantsLock)
            {
                if (_variants != null && _variants.TryGetValue(color, out Sixel variant))
                    return variant;

                // an animated overlay produces a new color every frame; don't hoard them
                if (_variants == null || _variants.Count >= MaxVariants)
                    _variants = new Dictionary<Color, Sixel>();

                variant = create();
                _variants[color] = variant;
                return variant;
            }
        }

        private static byte[] GetWashedPalette(byte[] palette, int paletteCount, Color wash)
        {
            Dictionary<Color, byte[]> washedPalettes = WashedPalettes.GetOrCreateValue(palette);
            lock (washedPalettes)
            {
                if (washedPalettes.TryGetValue(wash, out byte[] washedPalette))
                    return washedPalette;

                if (washedPalettes.Count >= MaxVariants)
                    washedPalettes.Clear();

                int alpha = wash.A;
                int inverseAlpha = 255 - alpha;
                washedPalette = new byte[palette.Length];
                for (int i = 0; i < paletteCount; i++)
                {
                    int offset = i * 4;
                    washedPalette[offset] = (byte)((wash.B * alpha + palette[offset] * inverseAlpha) / 255);
                    washedPalette[offset + 1] = (byte)((wash.G * alpha + palette[offset + 1] * inverseAlpha) / 255);
                    washedPalette[offset + 2] = (byte)((wash.R * alpha + palette[offset + 2] * inverseAlpha) / 255);
                    washedPalette[offset + 3] = palette[offset + 3];
                }

                washedPalettes[wash] = washedPalette;
                return washedPalette;
            }
        }

        #region Variants

        private const int MaxVariants = 8;

        private static readonly ConditionalWeakTable<byte[], Dictionary<Color, byte[]>> WashedPalettes = new();

        private readonly object _variantsLock = new();
        private Dictionary<Color, Sixel> _variants;

        #endregion

        #region Serialization

        [ThreadStatic] private static byte[] _scratchRenderBuf;
        [ThreadStatic] private static WuColorQuantizer _quantizer;

        private static readonly ConditionalWeakTable<byte[], PaletteLookup> PaletteLookups = new();

        private byte[] _renderedBytes;
        private int _dominantIndex = -1;

        /// <summary>
        ///     Serialize this image to SIXEL escape sequence bytes.
        ///     The returned span is cached on the instance after the first render.
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

            int maxOutput = 64 + paletteCount * 20 + width * ((height + 5) / 6) * 4 + 4096;
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

            byte[] rendered = GC.AllocateUninitializedArray<byte>(pos);
            output.AsSpan(0, pos).CopyTo(rendered);
            _renderedBytes = rendered;
            return rendered;
        }

        public ReadOnlySpan<byte> ToBytes()
        {
            return Render();
        }

        #endregion

        #region Quantization

        /// <summary>
        ///     Quantize BGRX pixel data using Wu's variance-minimizing algorithm.
        /// </summary>
        private static void Quantize(byte[] bgrx,
            out byte[] palette, out int paletteCount, out byte[] indexed)
        {
            WuColorQuantizer quantizer = _quantizer ??= new WuColorQuantizer();
            ColorQuantizerResult result = quantizer.Quantize(bgrx, 256);

            palette = result.Palette;
            paletteCount = palette.Length / 4;
            indexed = result.Bytes;
        }

        /// <summary>
        ///     Map BGRX pixel data to an existing palette using nearest-color matching.
        /// </summary>
        private static byte[] QuantizeWithPalette(byte[] bgrx, byte[] palette)
        {
            int pixelCount = bgrx.Length / 4;
            byte[] indexed = GC.AllocateUninitializedArray<byte>(pixelCount);
            ReadOnlySpan<byte> bgrxSpan = bgrx;
            PaletteLookup lookup = PaletteLookups.GetValue(palette, static currentPalette =>
                new PaletteLookup(currentPalette));
            ReadOnlySpan<byte> paletteLookup = lookup.Lookup;
            Dictionary<int, byte> exact = lookup.Exact;

            for (int i = 0, offset = 0; i < pixelCount; i++, offset += 4)
            {
                int b = bgrxSpan[offset];
                int g = bgrxSpan[offset + 1];
                int r = bgrxSpan[offset + 2];

                // A color the palette holds is that entry, never a neighbour the binned lookup
                // happens to land nearer to.
                if (exact.TryGetValue((r << 16) | (g << 8) | b, out byte exactIndex))
                {
                    indexed[i] = exactIndex;
                    continue;
                }

                int lookupIndex = ((r >> PaletteLookup.ChannelShift) << (PaletteLookup.ChannelBits * 2)) |
                                  ((g >> PaletteLookup.ChannelShift) << PaletteLookup.ChannelBits) |
                                  (b >> PaletteLookup.ChannelShift);
                indexed[i] = paletteLookup[lookupIndex];
            }

            return indexed;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T[] RentOrGrow<T>(ref T[] buf, int minSize)
        {
            if (buf == null || buf.Length < minSize)
                buf = GC.AllocateUninitializedArray<T>(Math.Max(minSize, 4096));
            return buf;
        }

        private sealed class PaletteLookup
        {
            public const int ChannelBits = 5;
            public const int ChannelShift = 8 - ChannelBits;
            private const int LookupSize = 1 << (ChannelBits * 3);

            // Each bin is matched by its centre. Matching by its low corner treated white as 248,
            // so a palette that also held a light antialiasing gray mapped white onto the gray.
            private const int BinCentre = 1 << (ChannelShift - 1);

            public PaletteLookup(byte[] palette)
            {
                Lookup = GC.AllocateUninitializedArray<byte>(LookupSize);

                int paletteCount = palette.Length / 4;
                Exact = new Dictionary<int, byte>(paletteCount);
                for (int paletteIndex = paletteCount - 1; paletteIndex >= 0; paletteIndex--)
                {
                    int paletteOffset = paletteIndex * 4;
                    Exact[(palette[paletteOffset + 2] << 16) | (palette[paletteOffset + 1] << 8) |
                          palette[paletteOffset]] = (byte)paletteIndex;
                }

                for (int index = 0; index < Lookup.Length; index++)
                {
                    int r = (((index >> (ChannelBits * 2)) & ((1 << ChannelBits) - 1)) << ChannelShift) | BinCentre;
                    int g = (((index >> ChannelBits) & ((1 << ChannelBits) - 1)) << ChannelShift) | BinCentre;
                    int b = ((index & ((1 << ChannelBits) - 1)) << ChannelShift) | BinCentre;

                    int bestPaletteIndex = 0;
                    int bestDistance = int.MaxValue;

                    for (int paletteIndex = 0; paletteIndex < paletteCount; paletteIndex++)
                    {
                        int paletteOffset = paletteIndex * 4;
                        int db = b - palette[paletteOffset];
                        int dg = g - palette[paletteOffset + 1];
                        int dr = r - palette[paletteOffset + 2];
                        int distance = dr * dr + dg * dg + db * db;
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestPaletteIndex = paletteIndex;
                        }
                    }

                    Lookup[index] = (byte)bestPaletteIndex;
                }
            }

            public byte[] Lookup { get; }

            /// <summary>Palette colors (0xRRGGBB) to their index, so an exact color is never approximated.</summary>
            public Dictionary<int, byte> Exact { get; }
        }

        #endregion
    }
}