#define SIXEL_CELLS
//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Dummy;

namespace Consolonia.Core.Drawing
{
    internal readonly record struct BitmapQuantizedCacheKey(
        int Version,
        PixelSize TargetSize);

    /// <summary>
    ///     Bitmap - drawing implementation
    /// </summary>
    internal partial class DrawingContextImpl
    {
        private static readonly ConditionalWeakTable<IBitmapImpl, Dictionary<BitmapQuantizedCacheKey, PixelBuffer>>
            RenderedBitmapCache = new();

        public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            if (source is DummyBitmap)
                return;

            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;
            var renderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
            bool trackedDirtyRegions = false;
#if SIXEL

            int cellPixelWidth = _consoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = _consoleWindowImpl.Console.CellPixelHeight;

            if (!_sixels.TryGetValue(source, out var sixel))
            {

                var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                    targetRect.Height * cellPixelHeight);
                var visibleTargetSize = new PixelSize(intersectedRect.Width * cellPixelWidth,
                    intersectedRect.Height * cellPixelHeight);

                using IBitmapImpl resizedBitmap =
                    renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality);

                var readableBitmap = (IReadableBitmapImpl)resizedBitmap;

                using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

                unsafe
                {
                    ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.AsRef<byte>((void*)frameBuffer.Address), frameBuffer.RowBytes * frameBuffer.Size.Height);

                    int visibleOffsetX = (intersectedRect.X - targetRect.X) * cellPixelWidth;
                    int visibleOffsetY = (intersectedRect.Y - targetRect.Y) * cellPixelHeight;
                    byte[] visibleBytes = CopyVisibleBitmapBytes(pixelBytes, frameBuffer.RowBytes, visibleTargetSize,
                        visibleOffsetX, visibleOffsetY);

                    SixelEncoder.Quantize(visibleBytes, out byte[] palette, out int paletteCount, out byte[] indexed);

                    sixel = SixelEncoder.Encode(indexed, visibleTargetSize.Width, visibleTargetSize.Height,
                        palette, paletteCount);
                    _sixels[source] = sixel;
                }
            }
            var topLeft = intersectedRect.TopLeft;
            _pixelBuffer[topLeft] = new Pixel(
                new PixelForeground(new Symbol(sixel, (byte)intersectedRect.Width), Colors.Transparent));

            for (int y = 0; y < intersectedRect.Height; y++)
            {
                int startX = y == 0 ? 1 : 0;
                for (int x = startX; x < intersectedRect.Width; x++)
                {
                    var point = new PixelPoint(intersectedRect.X + x, intersectedRect.Y + y);
                    _pixelBuffer[point] = Pixel.Empty;
                }
            }
#endif

#if SIXEL_CELLS

            int cellPixelWidth = _consoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = _consoleWindowImpl.Console.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            var visibleRectInTarget = new PixelRect(
                intersectedRect.X - targetRect.X,
                intersectedRect.Y - targetRect.Y,
                intersectedRect.Width,
                intersectedRect.Height);

            PixelBuffer renderedBitmap = GetOrCreateRenderedBitmap(source, targetSize, () =>
                {
                    var fullTargetSize = new PixelSize(targetSize.Width, targetSize.Height);

                    using IBitmapImpl resizedBitmap = !source.PixelSize.Equals(targetSize) ? 
                        renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality) : null;
                    if (resizedBitmap != null)
                        source = resizedBitmap;

                    var readableBitmap = (IReadableBitmapImpl)resizedBitmap;

                    using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

                    unsafe
                    {
                        ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                            ref Unsafe.AsRef<byte>((void*)frameBuffer.Address),
                            frameBuffer.RowBytes * frameBuffer.Size.Height);

                        byte[] fullBytes = CopyVisibleBitmapBytes(pixelBytes, frameBuffer.RowBytes,
                            fullTargetSize, 0, 0);

                        SixelEncoder.Quantize(fullBytes, out byte[] palette, out int paletteCount,
                            out byte[] indexed);

                        var bitmapBuffer = new PixelBuffer((ushort)targetRect.Width, (ushort)targetRect.Height);
                        byte[] cellIndexed = GC.AllocateUninitializedArray<byte>(cellPixelWidth * cellPixelHeight);

                        for (int cellY = 0; cellY < targetRect.Height; cellY++)
                        {
                            for (int cellX = 0; cellX < targetRect.Width; cellX++)
                            {
                                FillCellIndexedBuffer(indexed, targetSize.Width, cellX, cellY,
                                    cellPixelWidth, cellPixelHeight, cellIndexed);

                                byte[] sixel = SixelEncoder.Encode(cellIndexed, cellPixelWidth, cellPixelHeight,
                                    palette, paletteCount);
                                bitmapBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                                    new PixelForeground(new Symbol(sixel, 1), Colors.Transparent));
                            }
                        }

                        return bitmapBuffer;
                    }
                });

            trackedDirtyRegions = true;
            for (int y = 0; y < intersectedRect.Height; y++)
            {
                int dirtyRunStart = -1;
                for (int x = 0; x < intersectedRect.Width; x++)
                {
                    var sourcePoint = new PixelPoint(visibleRectInTarget.X + x, visibleRectInTarget.Y + y);
                    var destPoint = new PixelPoint(intersectedRect.X + x, intersectedRect.Y + y);
                    Pixel newPixel = renderedBitmap[sourcePoint];
                    if (_pixelBuffer[destPoint] == newPixel)
                    {
                        if (dirtyRunStart >= 0)
                        {
                            _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
                                intersectedRect.X + dirtyRunStart,
                                intersectedRect.Y + y,
                                x - dirtyRunStart,
                                1));
                            dirtyRunStart = -1;
                        }

                        continue;
                    }

                    _pixelBuffer[destPoint] = newPixel;
                    if (dirtyRunStart < 0)
                        dirtyRunStart = x;
                }

                if (dirtyRunStart >= 0)
                {
                    _consoleWindowImpl.DirtyRegions.AddRect(new PixelRect(
                        intersectedRect.X + dirtyRunStart,
                        intersectedRect.Y + y,
                        intersectedRect.Width - dirtyRunStart,
                        1));
                }
            }

#endif
#if QUAD_PIXEL
            // Resize source to be target rect * 2 so we can map to quad pixels
            var targetSize = new PixelSize(targetRect.Width * 2, targetRect.Height * 2);
            using IBitmapImpl resizedBitmap =
                renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality);

            var readableBitmap = (IReadableBitmapImpl)resizedBitmap;

            using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

            int stride = frameBuffer.RowBytes;
            int bytesPerPixel = frameBuffer.Format.BitsPerPixel / 8;
            unsafe
            {
                ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.AsRef<byte>((void*)frameBuffer.Address), stride * frameBuffer.Size.Height);
                ReadOnlySpan<BgraColor> pixels = MemoryMarshal.Cast<byte, BgraColor>(pixelBytes);

                int startY = (intersectedRect.Y - targetRect.TopLeft.Y) * 2;
                int startX = (intersectedRect.X - targetRect.TopLeft.X) * 2;
                int endY = startY + intersectedRect.Height * 2;
                int endX = startX + intersectedRect.Width * 2;

                int py = intersectedRect.Y;

                for (int y = startY; y < endY; y += 2, py++)
                {
                    int px = intersectedRect.X;
                    for (int x = startX; x < endX; x += 2, px++)
                    {
                        var point = new PixelPoint(px, py);

                        // get the quad pixel from the bitmap as a quad of 4 BgraColor values
                        Span<BgraColor> quadPixelColors =
                        [
                            GetPixelColor(pixels, x, y, stride, bytesPerPixel),
                            GetPixelColor(pixels, x + 1, y, stride, bytesPerPixel),
                            GetPixelColor(pixels, x, y + 1, stride, bytesPerPixel),
                            GetPixelColor(pixels, x + 1, y + 1, stride, bytesPerPixel)
                        ];

                        // map it to a single char to represent the 4 pixels
                        char quadPixelChar = GetQuadPixelCharacter(quadPixelColors);

                        // get the combined colors for the quad pixel
                        Color foreground = GetForegroundColorForQuadPixel(quadPixelChar, quadPixelColors);
                        Color background = GetBackgroundColorForQuadPixel(quadPixelChar, quadPixelColors);

                        var imagePixel = new Pixel(
                            new PixelForeground(new Symbol(quadPixelChar), foreground),
                            new PixelBackground(background));

                        _pixelBuffer[point] = _pixelBuffer[point].Blend(imagePixel);
                    }
                }
            }
#endif
            if (!trackedDirtyRegions)
                _consoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            throw new NotImplementedException();
        }

        private static byte[] CopyVisibleBitmapBytes(ReadOnlySpan<byte> pixelBytes, int rowBytes,
            PixelSize visibleTargetSize, int visibleOffsetX, int visibleOffsetY)
        {
            int visibleRowBytes = visibleTargetSize.Width * 4;
            byte[] visibleBytes = GC.AllocateUninitializedArray<byte>(visibleRowBytes * visibleTargetSize.Height);

            for (int row = 0; row < visibleTargetSize.Height; row++)
            {
                int sourceOffset = (visibleOffsetY + row) * rowBytes + visibleOffsetX * 4;
                int targetOffset = row * visibleRowBytes;
                pixelBytes.Slice(sourceOffset, visibleRowBytes)
                    .CopyTo(visibleBytes.AsSpan(targetOffset, visibleRowBytes));
            }

            return visibleBytes;
        }

        private static void FillCellIndexedBuffer(ReadOnlySpan<byte> indexed, int imageWidth, int cellX, int cellY,
            int cellPixelWidth, int cellPixelHeight, Span<byte> cellIndexed)
        {
            for (int row = 0; row < cellPixelHeight; row++)
            {
                int sourceOffset = ((cellY * cellPixelHeight) + row) * imageWidth + (cellX * cellPixelWidth);
                int targetOffset = row * cellPixelWidth;
                indexed.Slice(sourceOffset, cellPixelWidth)
                    .CopyTo(cellIndexed.Slice(targetOffset, cellPixelWidth));
            }
        }

        private static BgraColor GetPixelColor(ReadOnlySpan<BgraColor> pixels, int x, int y, int stride,
            int bytesPerPixel)
        {
            int bytesPerRow = stride;
            int pixelsPerRow = bytesPerRow / bytesPerPixel;
            int offset = y * pixelsPerRow + x;

            BgraColor pixel = pixels[offset];

            // Handle RGB24 format (no alpha channel)
            if (bytesPerPixel == 3)
                pixel.A = 255;

            return pixel;
        }

        private char GetQuadPixelCharacter(ReadOnlySpan<BgraColor> colors)
        {
            char character = GetColorsPattern(colors) switch
            {
                // ReSharper disable StringLiteralTypo
                0b0000 => ' ',
                0b1000 => '▘',
                0b0100 => '▝',
                0b0010 => '▖',
                0b0001 => '▗',
                0b1001 => '▚',
                0b0110 => '▞',
                0b1010 => '▌',
                0b0101 => '▐',
                0b0011 => '▄',
                0b1100 => '▀',
                0b1110 => '▛',
                0b1101 => '▜',
                0b1011 => '▙',
                0b0111 => '▟',
                0b1111 => '█',
                // ReSharper restore StringLiteralTypo
                _ => throw new NotImplementedException()
            };
            return character;
        }

        /// <summary>
        ///     Combine the colors for the white part of the quad pixel character.
        /// </summary>
        /// <param name="quadPixel"></param>
        /// <param name="pixelColors">4 colors</param>
        /// <returns>foreground color</returns>
        /// <exception cref="NotImplementedException"></exception>
        private static Color GetForegroundColorForQuadPixel(char quadPixel, ReadOnlySpan<BgraColor> pixelColors)
        {
            if (pixelColors.Length != 4)
                throw new ArgumentException($"{nameof(pixelColors)} must have 4 elements.");

            // TODO: Some of these chars don't work in IBM Codepage
            BgraColor bgraColor = quadPixel switch
            {
                ' ' => BgraColor.Transparent,
                '▘' => pixelColors[0],
                '▝' => pixelColors[1],
                '▖' => pixelColors[2],
                '▗' => pixelColors[3],
                '▚' => CombineColors([pixelColors[0], pixelColors[3]]),
                '▞' => CombineColors([pixelColors[1], pixelColors[2]]),
                '▌' => CombineColors([pixelColors[0], pixelColors[2]]),
                '▐' => CombineColors([pixelColors[1], pixelColors[3]]),
                '▄' => CombineColors([pixelColors[2], pixelColors[3]]),
                '▀' => CombineColors([pixelColors[0], pixelColors[1]]),
                '▛' => CombineColors([pixelColors[0], pixelColors[1], pixelColors[2]]),
                '▜' => CombineColors([pixelColors[0], pixelColors[1], pixelColors[3]]),
                '▙' => CombineColors([pixelColors[0], pixelColors[2], pixelColors[3]]),
                '▟' => CombineColors([pixelColors[1], pixelColors[2], pixelColors[3]]),
                '█' => CombineColors(pixelColors),
                _ => throw new NotImplementedException()
            };

            return bgraColor.ToColor();
        }


        /// <summary>
        ///     Combine the colors for the black part of the quad pixel character.
        /// </summary>
        /// <param name="quadPixel"></param>
        /// <param name="pixelColors"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private static Color GetBackgroundColorForQuadPixel(char quadPixel, ReadOnlySpan<BgraColor> pixelColors)
        {
            // TODO: Some of these chars don't work in IBM Codepage
            BgraColor bgraColor = quadPixel switch
            {
                ' ' => CombineColors(pixelColors),
                '▘' => CombineColors([pixelColors[1], pixelColors[2], pixelColors[3]]),
                '▝' => CombineColors([pixelColors[0], pixelColors[2], pixelColors[3]]),
                '▖' => CombineColors([pixelColors[0], pixelColors[1], pixelColors[3]]),
                '▗' => CombineColors([pixelColors[0], pixelColors[1], pixelColors[2]]),
                '▚' => CombineColors([pixelColors[1], pixelColors[2]]),
                '▞' => CombineColors([pixelColors[0], pixelColors[3]]),
                '▌' => CombineColors([pixelColors[1], pixelColors[3]]),
                '▐' => CombineColors([pixelColors[0], pixelColors[2]]),
                '▄' => CombineColors([pixelColors[0], pixelColors[1]]),
                '▀' => CombineColors([pixelColors[2], pixelColors[3]]),
                '▛' => pixelColors[3],
                '▜' => pixelColors[2],
                '▙' => pixelColors[1],
                '▟' => pixelColors[0],
                '█' => BgraColor.Transparent,
                _ => throw new NotImplementedException()
            };
            return bgraColor.ToColor();
        }


        private static BgraColor CombineColors(ReadOnlySpan<BgraColor> colors)
        {
            float accumR = 0, accumG = 0, accumB = 0;
            float accumAlpha = 0;

            foreach (ref readonly BgraColor color in colors)
            {
                float a1 = color.A / 255f;
                float oneMinusA = 1f - accumAlpha;

                accumR += color.R * a1 * oneMinusA;
                accumG += color.G * a1 * oneMinusA;
                accumB += color.B * a1 * oneMinusA;
                accumAlpha += a1 * oneMinusA;
            }

            byte r = (byte)Math.Clamp(accumR, 0, 255);
            byte g = (byte)Math.Clamp(accumG, 0, 255);
            byte b = (byte)Math.Clamp(accumB, 0, 255);
            byte a = (byte)Math.Clamp(accumAlpha * 255f, 0, 255);

            return new BgraColor(b, g, r, a);
        }

        /// <summary>
        ///     Cluster quad colors into a pattern (like: TTFF) based on relative closeness
        /// </summary>
        /// <param name="colors"></param>
        /// <returns>T or F for each color as a string</returns>
        /// <exception cref="ArgumentException"></exception>
        private byte GetColorsPattern(ReadOnlySpan<BgraColor> colors)
        {
            if (colors.Length != 4) throw new ArgumentException("Array must contain exactly 4 colors.");

            if (!_consoleWindowImpl.Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsComplexEmoji))
            {
                BgraColor topRowColor = Average(colors[0], colors[1]);
                BgraColor bottomRowColor = Average(colors[2], colors[3]);

                if (colors[0].A == 0 && colors[1].A == 0 && colors[2].A == 0 && colors[3].A == 0)
                    return 0b0000;

                if (ColorEquals(topRowColor, bottomRowColor))
                    return topRowColor.A == 0 ? (byte)0b0000 : (byte)0b1111;

                double topBr = GetColorBrightness(topRowColor);
                double bottomBr = GetColorBrightness(bottomRowColor);
                return (byte)(topBr >= bottomBr ? 0b1100 : 0b0011);
            }

            // Initial guess: two clusters with the first two colors as centers
            Span<BgraColor> clusterCenters = [colors[0], colors[1]];
            Span<BgraColor> newClusterCenters = stackalloc BgraColor[2];
            Span<int> clusters = stackalloc int[4];

            for (int iteration = 0; iteration < 10; iteration++) // limit iterations to avoid infinite loop
            {
                // Assign colors to the closest cluster center
                for (int i = 0; i < colors.Length; i++)
                    clusters[i] = GetColorCluster(colors[i], clusterCenters);

                // Recalculate cluster centers
                newClusterCenters[0] = BgraColor.Transparent;
                newClusterCenters[1] = BgraColor.Transparent;
                for (int cluster = 0; cluster < 2; cluster++)
                {
                    // Calculate average for this cluster 
                    int totalRed = 0, totalGreen = 0, totalBlue = 0, totalAlpha = 0;
                    int count = 0;
                    bool allTransparent = true;

                    for (int i = 0; i < colors.Length; i++)
                        if (clusters[i] == cluster)
                        {
                            BgraColor color = colors[i];
                            totalRed += color.R;
                            totalGreen += color.G;
                            totalBlue += color.B;
                            totalAlpha += color.A;
                            count++;

                            if (color.A != 0)
                                allTransparent = false;
                        }

                    if (count > 0)
                    {
                        newClusterCenters[cluster].B = (byte)(totalBlue / count);
                        newClusterCenters[cluster].G = (byte)(totalGreen / count);
                        newClusterCenters[cluster].R = (byte)(totalRed / count);
                        newClusterCenters[cluster].A = (byte)(totalAlpha / count);
                    }

                    if (count == 4 && allTransparent)
                        return 0;
                }

                // Check for convergence
                bool converged = true;
                for (int i = 0; i < 2; i++)
                    if (!ColorEquals(clusterCenters[i], newClusterCenters[i]))
                    {
                        converged = false;
                        break;
                    }

                if (converged)
                    break;

                clusterCenters[0] = newClusterCenters[0];
                clusterCenters[1] = newClusterCenters[1];
            }

            // Determine which cluster is lower and which is higher
            int lowerCluster = GetColorBrightness(clusterCenters[0]) < GetColorBrightness(clusterCenters[1]) ? 0 : 1;
            int higherCluster = 1 - lowerCluster;

            // represent bitmask where 0 for lower cluster and 1 for higher cluster
            return (byte)
                ((clusters[0] == higherCluster ? 0b1000 : 0) |
                 (clusters[1] == higherCluster ? 0b0100 : 0) |
                 (clusters[2] == higherCluster ? 0b0010 : 0) |
                 (clusters[3] == higherCluster ? 0b0001 : 0));
        }

        private static BgraColor Average(in BgraColor a, in BgraColor b)
        {
            return new BgraColor(
                (byte)((a.B + b.B) / 2),
                (byte)((a.G + b.G) / 2),
                (byte)((a.R + b.R) / 2),
                (byte)((a.A + b.A) / 2));
        }

        private static bool ColorEquals(BgraColor c1, BgraColor c2)
        {
            return Unsafe.As<BgraColor, int>(ref c1) == Unsafe.As<BgraColor, int>(ref c2);
        }

        private static int GetColorCluster(BgraColor color, ReadOnlySpan<BgraColor> clusterCenters)
        {
            double minDistance = double.MaxValue;
            int closestCluster = -1;

            for (int i = 0; i < clusterCenters.Length; i++)
            {
                double distance = GetColorDistance(color, clusterCenters[i]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestCluster = i;
                }
            }

            return closestCluster;
        }

        private static double GetColorDistance(BgraColor c1, BgraColor c2)
        {
            int dr = c1.R - c2.R;
            int dg = c1.G - c2.G;
            int db = c1.B - c2.B;
            int da = c1.A - c2.A;

            return Math.Sqrt(dr * dr + dg * dg + db * db + da * da);
        }

        private static double GetColorBrightness(BgraColor color)
        {
            return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B + color.A;
        }

        private static PixelBuffer GetOrCreateRenderedBitmap(IBitmapImpl source, PixelSize targetSize,
            Func<PixelBuffer> factory)
        {
            IBitmapImpl cacheSource = GetCacheBitmapImpl(source);
            var perBitmap = RenderedBitmapCache.GetOrCreateValue(cacheSource);
            var key = new BitmapQuantizedCacheKey(cacheSource.Version, targetSize);

            if (perBitmap.TryGetValue(key, out PixelBuffer renderedBitmap))
                return renderedBitmap;

            renderedBitmap = factory();
            perBitmap[key] = renderedBitmap;
            return renderedBitmap;
        }

        private static IBitmapImpl GetCacheBitmapImpl(IBitmapImpl bitmapImpl)
        {
            return bitmapImpl is AspectRatioAdjustedBitmap adjustedBitmap
                ? adjustedBitmap.InnerBitmap
                : bitmapImpl;
        }
    }

}