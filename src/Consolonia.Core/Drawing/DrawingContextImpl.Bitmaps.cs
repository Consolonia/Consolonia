//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
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
    /// <summary>
    ///     Bitmap - drawing implementation
    /// </summary>
    internal partial class DrawingContextImpl
    {
        public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            if (_consoleWindowImpl.Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsUnicode))
                DrawQuadBitmap(source, opacity, sourceRect, destRect);
            else
                DrawHalfBlockBitmap(source, opacity, sourceRect, destRect);
        }

        public void DrawQuadBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            if (source is DummyBitmap)
                return;

            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            var renderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();

            // Resize source to be target rect * 2 so we can map to quad pixels
            var targetSize = new PixelSize(targetRect.Width * 2, targetRect.Height * 2);
            using IBitmapImpl resizedBitmap =
                renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality);

            var readableBitmap = (IReadableBitmapImpl)resizedBitmap;

            using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;

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

            _consoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
        {
            throw new NotImplementedException();
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

        private static char GetQuadPixelCharacter(ReadOnlySpan<BgraColor> colors)
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
        private static byte GetColorsPattern(ReadOnlySpan<BgraColor> colors)
        {
            if (colors.Length != 4) throw new ArgumentException("Array must contain exactly 4 colors.");

            // Initial guess: two clusters with the first two colors as centers
            Span<BgraColor> clusterCenters = stackalloc BgraColor[2] { colors[0], colors[1] };
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

        #region Half-Block Bitmap Rendering (1x2 subpixels per character)

        /// <summary>
        /// Half-block rendering uses upper half block '▀' to represent 2 vertical pixels per character.
        /// Each pixel gets its exact color - no clustering or averaging needed.
        /// Foreground = top pixel color, Background = bottom pixel color.
        /// This provides 2x vertical resolution with universal terminal support.
        /// </summary>
        public void DrawHalfBlockBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
        {
            if (source is DummyBitmap)
                return;

            var targetRect = new Rect(Transform.Transform(destRect.TopLeft),
                    Transform.Transform(destRect.BottomRight))
                .ToPixelRect();

            var renderInterface = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();

            // Resize source to target width x (height * 2) for half-block mapping
            var targetSize = new PixelSize(targetRect.Width, targetRect.Height * 2);
            using IBitmapImpl resizedBitmap =
                renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality);

            var readableBitmap = (IReadableBitmapImpl)resizedBitmap;

            using ILockedFramebuffer frameBuffer = readableBitmap.Lock();

            PixelRect intersectedRect = CurrentClip.Intersect(targetRect);

            if (intersectedRect.IsEmpty())
                return;

            int stride = frameBuffer.RowBytes;
            int bytesPerPixel = frameBuffer.Format.BitsPerPixel / 8;
            unsafe
            {
                ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.AsRef<byte>((void*)frameBuffer.Address), stride * frameBuffer.Size.Height);
                ReadOnlySpan<BgraColor> pixels = MemoryMarshal.Cast<byte, BgraColor>(pixelBytes);

                int startY = (intersectedRect.Y - targetRect.TopLeft.Y) * 2;
                int startX = intersectedRect.X - targetRect.TopLeft.X;
                int endY = startY + intersectedRect.Height * 2;
                int endX = startX + intersectedRect.Width;

                int py = intersectedRect.Y;

                for (int y = startY; y < endY; y += 2, py++)
                {
                    int px = intersectedRect.X;
                    for (int x = startX; x < endX; x++, px++)
                    {
                        var point = new PixelPoint(px, py);

                        // Get top and bottom pixel colors
                        BgraColor topColor = GetPixelColor(pixels, x, y, stride, bytesPerPixel);
                        BgraColor bottomColor = GetPixelColor(pixels, x, y + 1, stride, bytesPerPixel);

                        // Determine which character to use based on transparency
                        char halfBlockChar;
                        Color foreground;
                        Color background;

                        bool topTransparent = topColor.A < 128;
                        bool bottomTransparent = bottomColor.A < 128;

                        if (topTransparent && bottomTransparent)
                        {
                            // Both transparent - use space with transparent colors
                            halfBlockChar = ' ';
                            foreground = Colors.Transparent;
                            background = Colors.Transparent;
                        }
                        else if (topTransparent)
                        {
                            // Only bottom visible - use lower half block
                            halfBlockChar = '▄';
                            foreground = bottomColor.ToColor();
                            background = Colors.Transparent;
                        }
                        else if (bottomTransparent)
                        {
                            // Only top visible - use upper half block
                            halfBlockChar = '▀';
                            foreground = topColor.ToColor();
                            background = Colors.Transparent;
                        }
                        else
                        {
                            // Both visible - use upper half block with top as foreground, bottom as background
                            halfBlockChar = '▀';
                            foreground = topColor.ToColor();
                            background = bottomColor.ToColor();
                        }

                        var imagePixel = new Pixel(
                            new PixelForeground(new Symbol(halfBlockChar), foreground),
                            new PixelBackground(background));

                        _pixelBuffer[point] = _pixelBuffer[point].Blend(imagePixel);
                    }
                }
            }

            _consoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        #endregion
    }
}