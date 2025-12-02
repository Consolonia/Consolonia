using Avalonia.Media;
using Consolonia.Core.Helpers;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class ColorContrastHelperTests
    {
        [Test]
        public void GetRelativeLuminance_Black_ReturnsZero()
        {
            double luminance = ColorContrastHelper.GetRelativeLuminance(Colors.Black);
            Assert.AreEqual(0.0, luminance, 0.0001);
        }

        [Test]
        public void GetRelativeLuminance_White_ReturnsOne()
        {
            double luminance = ColorContrastHelper.GetRelativeLuminance(Colors.White);
            Assert.AreEqual(1.0, luminance, 0.0001);
        }

        [Test]
        public void GetRelativeLuminance_MidGray_ReturnsMidValue()
        {
            var gray = Color.FromRgb(128, 128, 128);
            double luminance = ColorContrastHelper.GetRelativeLuminance(gray);
            // Mid-gray should have luminance around 0.2 due to sRGB gamma curve
            Assert.That(luminance, Is.GreaterThan(0.1).And.LessThan(0.3));
        }

        [Test]
        public void GetContrastRatio_BlackAndWhite_Returns21()
        {
            double ratio = ColorContrastHelper.GetContrastRatio(Colors.Black, Colors.White);
            Assert.AreEqual(21.0, ratio, 0.0001);
        }

        [Test]
        public void GetContrastRatio_SameColor_Returns1()
        {
            var red = Colors.Red;
            double ratio = ColorContrastHelper.GetContrastRatio(red, red);
            Assert.AreEqual(1.0, ratio, 0.0001);
        }

        [Test]
        public void GetContrastRatio_IsSymmetric()
        {
            var color1 = Color.FromRgb(100, 150, 200);
            var color2 = Color.FromRgb(200, 100, 50);

            double ratio1 = ColorContrastHelper.GetContrastRatio(color1, color2);
            double ratio2 = ColorContrastHelper.GetContrastRatio(color2, color1);

            Assert.AreEqual(ratio1, ratio2, 0.0001);
        }

        [Test]
        public void GetHighContrastColor_DarkBackground_ReturnsWhite()
        {
            Color result = ColorContrastHelper.GetHighContrastColor(Colors.Black);
            Assert.AreEqual(Colors.White, result);
        }

        [Test]
        public void GetHighContrastColor_LightBackground_ReturnsBlack()
        {
            Color result = ColorContrastHelper.GetHighContrastColor(Colors.White);
            Assert.AreEqual(Colors.Black, result);
        }

        [Test]
        public void GetHighContrastColor_DarkBlue_ReturnsWhite()
        {
            var darkBlue = Color.FromRgb(0, 0, 139);
            Color result = ColorContrastHelper.GetHighContrastColor(darkBlue);
            Assert.AreEqual(Colors.White, result);
        }

        [Test]
        public void GetHighContrastColor_LightYellow_ReturnsBlack()
        {
            var lightYellow = Color.FromRgb(255, 255, 200);
            Color result = ColorContrastHelper.GetHighContrastColor(lightYellow);
            Assert.AreEqual(Colors.Black, result);
        }

        [Test]
        public void GetContrastingColor_Black_ReturnsInvertedWhite()
        {
            Color result = ColorContrastHelper.GetContrastingColor(Colors.Black);
            // Inverted black is white, which has 21:1 contrast, so should be returned
            Assert.AreEqual(Colors.White, result);
        }

        [Test]
        public void GetContrastingColor_White_ReturnsInvertedBlack()
        {
            Color result = ColorContrastHelper.GetContrastingColor(Colors.White);
            // Inverted white is black, which has 21:1 contrast, so should be returned
            Assert.AreEqual(Colors.Black, result);
        }

        [Test]
        public void GetContrastingColor_MidGray_ReturnsFallbackHighContrast()
        {
            // Mid-gray (128,128,128) inverts to (127,127,127) with very low contrast
            var midGray = Color.FromRgb(128, 128, 128);
            Color result = ColorContrastHelper.GetContrastingColor(midGray);

            // Should fall back to high-contrast color, not the inverted color
            var invertedGray = Color.FromRgb(127, 127, 127);
            Assert.AreNotEqual(invertedGray, result);

            // Result should be either black or white
            Assert.That(result == Colors.Black || result == Colors.White);
        }

        [Test]
        public void GetContrastingColor_MidGray_MeetsMinimumContrastRatio()
        {
            var midGray = Color.FromRgb(128, 128, 128);
            Color result = ColorContrastHelper.GetContrastingColor(midGray);

            double ratio = ColorContrastHelper.GetContrastRatio(result, midGray);

            Assert.That(ratio, Is.GreaterThanOrEqualTo(ColorContrastHelper.MinimumContrastRatio));
        }

        [Test]
        public void GetContrastingColor_LowContrastInversion_ReturnsFallback()
        {
            // Gray values around 128 result in low contrast inversions
            for (byte grayValue = 100; grayValue <= 155; grayValue++)
            {
                var color = Color.FromRgb(grayValue, grayValue, grayValue);
                Color result = ColorContrastHelper.GetContrastingColor(color);

                double ratio = ColorContrastHelper.GetContrastRatio(result, color);

                Assert.That(ratio, Is.GreaterThanOrEqualTo(ColorContrastHelper.MinimumContrastRatio),
                    $"Failed for gray value {grayValue}: contrast ratio was {ratio}");
            }
        }

        [Test]
        public void GetContrastingColor_HighContrastInversion_ReturnsInversion()
        {
            // Very dark colors should invert to very light colors with good contrast
            var darkColor = Color.FromRgb(10, 10, 10);
            Color result = ColorContrastHelper.GetContrastingColor(darkColor);

            var expectedInversion = Color.FromRgb(245, 245, 245);
            Assert.AreEqual(expectedInversion, result);
        }

        [Test]
        public void GetContrastingColor_VeryLightColor_ReturnsInversion()
        {
            // Very light colors should invert to very dark colors with good contrast
            var lightColor = Color.FromRgb(245, 245, 245);
            Color result = ColorContrastHelper.GetContrastingColor(lightColor);

            var expectedInversion = Color.FromRgb(10, 10, 10);
            Assert.AreEqual(expectedInversion, result);
        }

        [Test]
        public void GetContrastingColor_CustomMinimumRatio_IsRespected()
        {
            var midGray = Color.FromRgb(128, 128, 128);

            // With a very low minimum ratio, even the inverted gray should work
            Color resultLowThreshold = ColorContrastHelper.GetContrastingColor(midGray, 1.0);
            var invertedGray = Color.FromRgb(127, 127, 127);
            Assert.AreEqual(invertedGray, resultLowThreshold);

            // With higher minimum ratio, should fall back to high-contrast
            Color resultHighThreshold = ColorContrastHelper.GetContrastingColor(midGray, 3.0);
            Assert.That(resultHighThreshold == Colors.Black || resultHighThreshold == Colors.White);
        }

        [Test]
        public void MinimumContrastRatio_Is3()
        {
            // WCAG AA requires 3:1 for non-text elements
            Assert.AreEqual(3.0, ColorContrastHelper.MinimumContrastRatio);
        }

        [Test]
        public void GetContrastingColor_AllColors_MeetMinimumContrast()
        {
            // Sample various colors across the RGB spectrum
            var testColors = new[]
            {
                Colors.Red,
                Colors.Green,
                Colors.Blue,
                Colors.Cyan,
                Colors.Magenta,
                Colors.Yellow,
                Color.FromRgb(128, 128, 128), // Mid gray
                Color.FromRgb(100, 100, 100), // Dark gray
                Color.FromRgb(155, 155, 155), // Light gray
                Color.FromRgb(50, 100, 150),
                Color.FromRgb(150, 100, 50),
                Color.FromRgb(200, 200, 100),
                Color.FromRgb(100, 200, 200)
            };

            foreach (Color color in testColors)
            {
                Color result = ColorContrastHelper.GetContrastingColor(color);
                double ratio = ColorContrastHelper.GetContrastRatio(result, color);

                Assert.That(ratio, Is.GreaterThanOrEqualTo(ColorContrastHelper.MinimumContrastRatio),
                    $"Failed for color ({color.R}, {color.G}, {color.B}): contrast ratio was {ratio:F2}");
            }
        }
    }
}