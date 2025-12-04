using Avalonia.Media;
using Consolonia.Core.Drawing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class RenderTargetTests
    {
        [TestCase(0, 0, 0, 255, 255, 255)]           // Black -> White (simple inversion works)
        [TestCase(255, 255, 255, 0, 0, 0)]           // White -> Black (simple inversion works)
        [TestCase(0, 0, 255, 255, 255, 0)]           // Blue -> Yellow (simple inversion works)
        [TestCase(255, 0, 0, 0, 255, 255)]           // Red -> Cyan (simple inversion works)
        [TestCase(128, 128, 128, 0, 0, 0)]           // Mid-gray -> Black (fallback, inversion has low contrast)
        [TestCase(127, 127, 127, 0, 0, 0)]           // Mid-gray -> Black (fallback, inversion has low contrast)
        [TestCase(100, 100, 100, 255, 255, 255)]     // Dark gray -> White (fallback gives better contrast)
        [TestCase(155, 155, 155, 0, 0, 0)]           // Light gray -> Black (fallback gives better contrast)
        public void GetInvertColorReturnsContrastingColor(
            byte inputR, byte inputG, byte inputB,
            byte expectedR, byte expectedG, byte expectedB)
        {
            Color input = Color.FromRgb(inputR, inputG, inputB);
            Color expected = Color.FromRgb(expectedR, expectedG, expectedB);

            Color result = RenderTarget.GetInvertColor(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(0, 0, 0)]           // Black
        [TestCase(255, 255, 255)]     // White
        [TestCase(128, 128, 128)]     // Mid-gray
        [TestCase(64, 64, 64)]        // Dark gray
        [TestCase(192, 192, 192)]     // Light gray
        [TestCase(255, 0, 0)]         // Red
        [TestCase(0, 255, 0)]         // Green
        [TestCase(0, 0, 255)]         // Blue
        public void GetInvertColorMeetsMinimumContrastRatio(byte r, byte g, byte b)
        {
            const double minimumContrastRatio = 3.0;
            Color input = Color.FromRgb(r, g, b);

            Color result = RenderTarget.GetInvertColor(input);

            double contrastRatio = GetContrastRatio(input, result);
            Assert.That(contrastRatio, Is.GreaterThanOrEqualTo(minimumContrastRatio));
        }

        private static double GetContrastRatio(Color c1, Color c2)
        {
            double l1 = GetRelativeLuminance(c1);
            double l2 = GetRelativeLuminance(c2);
            double lighter = System.Math.Max(l1, l2);
            double darker = System.Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double GetRelativeLuminance(Color c)
        {
            double r = GetLinearChannelValue(c.R);
            double g = GetLinearChannelValue(c.G);
            double b = GetLinearChannelValue(c.B);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double GetLinearChannelValue(byte channelValue)
        {
            double sRgb = channelValue / 255.0;
            return sRgb <= 0.03928
                ? sRgb / 12.92
                : System.Math.Pow((sRgb + 0.055) / 1.055, 2.4);
        }
    }
}
