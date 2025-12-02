#nullable enable
using System;
using Avalonia.Media;

namespace Consolonia.Core.Helpers
{
    /// <summary>
    ///     Helper class for calculating color contrast ratios per WCAG 2.0 guidelines.
    ///     Reference: https://www.w3.org/TR/WCAG20/#contrast-ratiodef
    /// </summary>
    public static class ColorContrastHelper
    {
        /// <summary>
        ///     Minimum contrast ratio for non-text elements per WCAG 2.0 Level AA.
        /// </summary>
        public const double MinimumContrastRatio = 3.0;

        /// <summary>
        ///     Calculates the relative luminance of a color per WCAG 2.0.
        ///     Reference: https://www.w3.org/TR/WCAG20/#relativeluminancedef
        /// </summary>
        /// <param name="color">The color to calculate luminance for.</param>
        /// <returns>Relative luminance value between 0 and 1.</returns>
        public static double GetRelativeLuminance(Color color)
        {
            double r = GetLinearChannelValue(color.R);
            double g = GetLinearChannelValue(color.G);
            double b = GetLinearChannelValue(color.B);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>
        ///     Converts an 8-bit sRGB channel value to its linear value.
        /// </summary>
        private static double GetLinearChannelValue(byte channelValue)
        {
            double sRgb = channelValue / 255.0;
            return sRgb <= 0.03928
                ? sRgb / 12.92
                : Math.Pow((sRgb + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        ///     Calculates the contrast ratio between two colors per WCAG 2.0.
        ///     Reference: https://www.w3.org/TR/WCAG20/#contrast-ratiodef
        /// </summary>
        /// <param name="color1">First color.</param>
        /// <param name="color2">Second color.</param>
        /// <returns>Contrast ratio between 1 and 21.</returns>
        public static double GetContrastRatio(Color color1, Color color2)
        {
            double l1 = GetRelativeLuminance(color1);
            double l2 = GetRelativeLuminance(color2);

            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>
        ///     Gets a high-contrast color for the given background color.
        ///     Returns white or black, whichever provides higher contrast with the background.
        /// </summary>
        /// <param name="backgroundColor">The background color.</param>
        /// <returns>Either black or white, whichever provides higher contrast.</returns>
        public static Color GetHighContrastColor(Color backgroundColor)
        {
            double contrastWithBlack = GetContrastRatio(Colors.Black, backgroundColor);
            double contrastWithWhite = GetContrastRatio(Colors.White, backgroundColor);

            // Return the color that provides better contrast
            return contrastWithBlack > contrastWithWhite ? Colors.Black : Colors.White;
        }

        /// <summary>
        ///     Gets a contrasting color for the given background, ensuring minimum WCAG contrast ratio.
        ///     First tries simple inversion; if that doesn't meet the minimum contrast,
        ///     falls back to high-contrast black or white.
        /// </summary>
        /// <param name="backgroundColor">The background color to contrast against.</param>
        /// <param name="minimumContrastRatio">Minimum required contrast ratio (default: 3.0 per WCAG AA).</param>
        /// <returns>A color that meets the minimum contrast requirement.</returns>
        public static Color GetContrastingColor(Color backgroundColor,
            double minimumContrastRatio = MinimumContrastRatio)
        {
            // First try simple inversion
            Color invertedColor = Color.FromRgb(
                (byte)(255 - backgroundColor.R),
                (byte)(255 - backgroundColor.G),
                (byte)(255 - backgroundColor.B));

            double contrastRatio = GetContrastRatio(invertedColor, backgroundColor);

            // If inversion provides sufficient contrast, use it
            if (contrastRatio >= minimumContrastRatio)
                return invertedColor;

            // Otherwise, fall back to high-contrast color (black or white)
            return GetHighContrastColor(backgroundColor);
        }
    }
}