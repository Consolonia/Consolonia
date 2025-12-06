using System;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    public class RgbConsoleColorMode : IConsoleColorMode
    {
        /// <inheritdoc />
        public Color Blend(Color color1, Color color2, bool isTargetForeground)
        {
            // Early exit for common cases
            if (color2.A == 0) return color1; // Fully transparent foreground
            if (color2.A == 255) return color2; // Fully opaque foreground
            if (color1.A == 0) return color2; // Transparent background

            // Use integer arithmetic to avoid float operations
            int fgAlpha = color2.A;
            int bgAlpha = color1.A;
            int invFgAlpha = 255 - fgAlpha;

            // Compute output alpha using integer math
            int outAlpha = fgAlpha + bgAlpha * invFgAlpha / 255;

            // Early exit for fully transparent result
            if (outAlpha == 0) return Color.FromArgb(0, 0, 0, 0);

            // Calculate composited RGB channels using integer arithmetic
            // Formula: (fg * fgAlpha + bg * bgAlpha * (1 - fgAlpha/255)) / outAlpha
            int outR = (color2.R * fgAlpha + color1.R * bgAlpha * invFgAlpha / 255) / outAlpha;
            int outG = (color2.G * fgAlpha + color1.G * bgAlpha * invFgAlpha / 255) / outAlpha;
            int outB = (color2.B * fgAlpha + color1.B * bgAlpha * invFgAlpha / 255) / outAlpha;

            // Clamp values to byte range (should not be necessary with correct math, but safety check)
            byte a = (byte)outAlpha;
            byte r = (byte)Math.Min(255, outR);
            byte g = (byte)Math.Min(255, outG);
            byte b = (byte)Math.Min(255, outB);

            return Color.FromArgb(a, r, g, b);
        }

        /// <inheritdoc />
        public (object background, object foreground) MapColors(Color background, Color foreground, FontWeight? weight)
        {
            foreground = weight switch
            {
                FontWeight.Medium or FontWeight.SemiBold or FontWeight.Bold or FontWeight.ExtraBold or FontWeight.Black
                    or FontWeight.ExtraBlack
                    => foreground.Brighten(background),
                FontWeight.Thin or FontWeight.ExtraLight or FontWeight.Light
                    => foreground.Shade(background),
                _ => foreground
            };

            return (background, foreground);
        }
    }
}