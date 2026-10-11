using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Renders a bitmap by approximating each cell with a colored quad-pixel block character.
    ///     This is the fallback for terminals without graphics protocol support.
    /// </summary>
    internal sealed class QuadPixelBitmapRenderer : BitmapRenderer
    {
        public QuadPixelBitmapRenderer(IConsoleOutput console)
            : base(console)
        {
        }

        public override void Draw(DrawingContextImpl context, IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelRect intersectedRect,
            BitmapInterpolationMode interpolationMode)
        {
            // two pixels across and two down per cell, and only the cells being drawn are scaled: a
            // picture zoomed far past the screen is not scaled whole every frame
            var targetSize = new PixelSize(targetRect.Width * 2, targetRect.Height * 2);
            var visible = new PixelRect((intersectedRect.X - targetRect.X) * 2, (intersectedRect.Y - targetRect.Y) * 2,
                intersectedRect.Width * 2, intersectedRect.Height * 2);
            byte[] bgra = GC.AllocateUninitializedArray<byte>(visible.Width * visible.Height * 4);
            GetVisiblePixels(source, renderInterface, targetSize, visible, interpolationMode, bgra);
            int rowBytes = visible.Width * 4;

            bool complexEmoji = ConsoleOutput.Capabilities.HasFlag(ConsoleCapabilities.SupportsComplexEmoji);
            PixelBuffer pixelBuffer = context.PixelBuffer;

            for (int cellY = 0; cellY < intersectedRect.Height; cellY++)
            for (int cellX = 0; cellX < intersectedRect.Width; cellX++)
            {
                int top = cellY * 2 * rowBytes + cellX * 2 * 4;
                int bottom = top + rowBytes;

                // the quad pixel from the bitmap as a quad of 4 BgraColor values
                Span<BgraColor> quadPixelColors =
                [
                    PixelAt(bgra, top),
                    PixelAt(bgra, top + 4),
                    PixelAt(bgra, bottom),
                    PixelAt(bgra, bottom + 4)
                ];

                // split the 4 pixels into a foreground and a background group, and map the split to the
                // block character that draws the foreground quadrants
                byte pattern = GetColorsPattern(quadPixelColors, complexEmoji);
                char quadPixelChar = GetQuadPixelCharacter(pattern);

                // nothing in the foreground draws nothing; with no quadrant left for the background, the
                // background takes all four colours. It is NOT transparent: a transparent background lets
                // the color under the picture show through the hairlines terminals leave around a full
                // block glyph
                Color foreground = pattern == 0b0000
                    ? BgraColor.Transparent.ToColor()
                    : CombineQuadrants(quadPixelColors, pattern).ToColor();
                var background = CombineQuadrants(quadPixelColors,
                    pattern == 0b1111 ? (byte)0b1111 : (byte)(~pattern & 0b1111)).ToColor();

                var imagePixel = new Pixel(
                    new PixelForeground(new Symbol(quadPixelChar), foreground),
                    new PixelBackground(background));

                var point = new PixelPoint(intersectedRect.X + cellX, intersectedRect.Y + cellY);
                pixelBuffer[point] = pixelBuffer[point].Blend(imagePixel);
            }

            context.ConsoleWindowImpl.DirtyRegions.AddRect(intersectedRect);
        }

        private static BgraColor PixelAt(byte[] bgra, int offset)
        {
            return new BgraColor(bgra[offset], bgra[offset + 1], bgra[offset + 2], bgra[offset + 3]);
        }

        private static char GetQuadPixelCharacter(byte pattern)
        {
            char character = pattern switch
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
        ///     Combines the colors of the quadrants in a pattern, one bit per quadrant as
        ///     <see cref="GetColorsPattern" /> returns it. A single quadrant keeps its color as it is.
        /// </summary>
        private static BgraColor CombineQuadrants(ReadOnlySpan<BgraColor> colors, byte pattern)
        {
            Span<BgraColor> selected = stackalloc BgraColor[4];
            int count = 0;
            for (int i = 0; i < 4; i++)
                if ((pattern & (0b1000 >> i)) != 0)
                    selected[count++] = colors[i];

            return count == 1 ? selected[0] : CombineColors(selected[..count]);
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
        ///     Cluster the 4 quad colors into two groups by relative closeness.
        /// </summary>
        /// <param name="colors">the 4 colors, top left, top right, bottom left, bottom right</param>
        /// <param name="complexEmoji">whether the console draws the quadrant characters</param>
        /// <returns>a 4-bit mask, one bit per color, set for the colors in the foreground group</returns>
        private static byte GetColorsPattern(ReadOnlySpan<BgraColor> colors, bool complexEmoji)
        {
            if (!complexEmoji)
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
            int lowerCluster = GetColorBrightness(clusterCenters[0]) < GetColorBrightness(clusterCenters[1])
                ? 0
                : 1;
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
    }
}