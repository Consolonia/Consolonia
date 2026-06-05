using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class BrushExtensionsTests
    {
        private static readonly Color Red = Color.FromRgb(255, 0, 0);
        private static readonly Color Green = Color.FromRgb(0, 255, 0);
        private static readonly Color Blue = Color.FromRgb(0, 0, 255);
        private static readonly Color Yellow = Color.FromRgb(255, 255, 0);
        private static readonly Color White = Color.FromRgb(255, 255, 255);
        private static readonly Color Black = Color.FromRgb(0, 0, 0);

        private static RelativePoint Rel(double x, double y)
        {
            return new RelativePoint(x, y, RelativeUnit.Relative);
        }

        private static Color ColorAt(IBrush brush, int x, int y, PixelRect bounds)
        {
            return brush.ColorAt(new PixelPoint(x, y), bounds);
        }

        [Test]
        public void SolidColorBrushReturnsItsColor()
        {
            var brush = new ImmutableSolidColorBrush(Red);
            Assert.AreEqual(Red, ColorAt(brush, 3, 7, new PixelRect(0, 0, 10, 10)));
        }

        [Test]
        public void SolidColorBrushWrappedInLineBrushReturnsInnerColor()
        {
            var brush = new LineBrush { Brush = new ImmutableSolidColorBrush(Blue) };
            Assert.AreEqual(Blue, ColorAt(brush, 0, 0, new PixelRect(0, 0, 4, 4)));
        }

        [Test]
        public void LinearGradientWrappedInLineBrushIsSampled()
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                GradientStops = { new GradientStop(White, 0), new GradientStop(Red, 1) }
            };
            var lineBrush = new LineBrush { Brush = gradient };
            var bounds = new PixelRect(0, 0, 4, 1);

            // Should be identical to sampling the gradient directly (LineBrush is transparent to sampling).
            Assert.AreEqual(ColorAt(gradient, 0, 0, bounds), ColorAt(lineBrush, 0, 0, bounds));
            Assert.AreEqual(ColorAt(gradient, 3, 0, bounds), ColorAt(lineBrush, 3, 0, bounds));
        }

        // Horizontal gradient: color must vary along X only. This is the canonical case the old
        // "average horizontal and vertical position" implementation got wrong.
        [Test]
        public void HorizontalLinearGradientVariesAlongXOnly()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                GradientStops = { new GradientStop(White, 0), new GradientStop(Red, 1) }
            };
            var bounds = new PixelRect(0, 0, 4, 2);

            // Cell centers at x+0.5 over width 4 => t = (x+0.5)/4. White->Red lerp (only G,B change).
            Assert.AreEqual(Color.FromArgb(255, 255, 223, 223), ColorAt(brush, 0, 0, bounds));
            Assert.AreEqual(Color.FromArgb(255, 255, 159, 159), ColorAt(brush, 1, 0, bounds));
            Assert.AreEqual(Color.FromArgb(255, 255, 96, 96), ColorAt(brush, 2, 0, bounds));
            Assert.AreEqual(Color.FromArgb(255, 255, 32, 32), ColorAt(brush, 3, 0, bounds));

            // Y must not affect a purely horizontal gradient.
            for (int x = 0; x < 4; x++)
                Assert.AreEqual(ColorAt(brush, x, 0, bounds), ColorAt(brush, x, 1, bounds),
                    $"row 1 differs from row 0 at x={x}");
        }

        [Test]
        public void PadSpreadHoldsEndColorsBeyondStops()
        {
            // Gradient axis spans only half the bounds, so raw t reaches ~1.875 in the right half.
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(0.5, 0),
                SpreadMethod = GradientSpreadMethod.Pad,
                GradientStops = { new GradientStop(Red, 0), new GradientStop(Blue, 1) }
            };
            var bounds = new PixelRect(0, 0, 8, 1);

            // t = (x+0.5)/4. x=0 => 0.125 interpolated; x>=4 => t>=1 clamped to Blue.
            Assert.AreEqual(Color.FromArgb(255, 223, 0, 32), ColorAt(brush, 0, 0, bounds));
            Assert.AreEqual(Blue, ColorAt(brush, 4, 0, bounds));
            Assert.AreEqual(Blue, ColorAt(brush, 7, 0, bounds));
        }

        [Test]
        public void RepeatSpreadWrapsParameter()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(0.5, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops = { new GradientStop(Red, 0), new GradientStop(Blue, 1) }
            };
            var bounds = new PixelRect(0, 0, 8, 1);

            // t at x=4 is 1.125 -> wraps to 0.125, same color as x=0.
            Color atZero = Color.FromArgb(255, 223, 0, 32);
            Assert.AreEqual(atZero, ColorAt(brush, 0, 0, bounds));
            Assert.AreEqual(atZero, ColorAt(brush, 4, 0, bounds));
        }

        [Test]
        public void ReflectSpreadMirrorsParameter()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(0.5, 0),
                SpreadMethod = GradientSpreadMethod.Reflect,
                GradientStops = { new GradientStop(Red, 0), new GradientStop(Blue, 1) }
            };
            var bounds = new PixelRect(0, 0, 8, 1);

            // t at x=4 is 1.125 -> reflects to 0.875.
            Assert.AreEqual(Color.FromArgb(255, 223, 0, 32), ColorAt(brush, 0, 0, bounds));
            Assert.AreEqual(Color.FromArgb(255, 32, 0, 223), ColorAt(brush, 4, 0, bounds));
        }

        // Stops that do not start at 0 / end at 1 must pad to the first/last stop color.
        [Test]
        public void StopsNotCoveringFullRangePadToEndColors()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                SpreadMethod = GradientSpreadMethod.Pad,
                GradientStops = { new GradientStop(Red, 0.25), new GradientStop(Blue, 0.75) }
            };
            var bounds = new PixelRect(0, 0, 8, 1);

            // t = (x+0.5)/8. x=0 => 0.0625 < 0.25 => Red; x=7 => 0.9375 > 0.75 => Blue.
            Assert.AreEqual(Red, ColorAt(brush, 0, 0, bounds));
            Assert.AreEqual(Red, ColorAt(brush, 1, 0, bounds)); // 0.1875 < 0.25
            Assert.AreEqual(Blue, ColorAt(brush, 7, 0, bounds));
        }

        // Conic gradient must produce a distinct color in each quadrant, rotating clockwise from the top.
        [Test]
        public void ConicGradientColorsEachQuadrant()
        {
            var brush = new ConicGradientBrush
            {
                Center = Rel(0.5, 0.5),
                Angle = 0,
                GradientStops =
                {
                    new GradientStop(Red, 0),
                    new GradientStop(Green, 0.25),
                    new GradientStop(Blue, 0.5),
                    new GradientStop(Yellow, 0.75),
                    new GradientStop(Red, 1)
                }
            };
            var bounds = new PixelRect(0, 0, 2, 2);

            // center is at pixel (1,1); cell centers sit diagonally at +-45 degrees.
            Assert.AreEqual(Color.FromArgb(255, 255, 128, 0), ColorAt(brush, 0, 0, bounds)); // top-left  (~315deg)
            Assert.AreEqual(Color.FromArgb(255, 128, 128, 0), ColorAt(brush, 1, 0, bounds)); // top-right (~45deg)
            Assert.AreEqual(Color.FromArgb(255, 128, 128, 128), ColorAt(brush, 0, 1, bounds)); // bottom-left (~225deg)
            Assert.AreEqual(Color.FromArgb(255, 0, 128, 128), ColorAt(brush, 1, 1, bounds)); // bottom-right (~135deg)
        }

        // Radial gradient with origin == center reduces to distance-from-center.
        [Test]
        public void RadialGradientReducesToDistanceFromCenter()
        {
            var brush = new RadialGradientBrush
            {
                Center = Rel(0.5, 0.5),
                GradientOrigin = Rel(0.5, 0.5),
                RadiusX = RelativeScalar.Parse("50%"),
                RadiusY = RelativeScalar.Parse("50%"),
                SpreadMethod = GradientSpreadMethod.Pad,
                GradientStops = { new GradientStop(Black, 0), new GradientStop(White, 1) }
            };
            var bounds = new PixelRect(0, 0, 4, 4);

            // center pixel (2,2). Nearest cell center is distance sqrt(0.125)=0.3536 of the radius.
            Assert.AreEqual(Color.FromArgb(255, 90, 90, 90), ColorAt(brush, 2, 2, bounds));
            // corner is past the radius => clamped to White.
            Assert.AreEqual(White, ColorAt(brush, 0, 0, bounds));
        }

        // Gradient stops may be declared in any offset order; sampling must pick the nearest enclosing stops
        // regardless of declaration order.
        [Test]
        public void UnsortedGradientStopsMatchSortedOrder()
        {
            var unsorted = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                GradientStops =
                {
                    new GradientStop(Green, 0.5),
                    new GradientStop(Blue, 1),
                    new GradientStop(Red, 0)
                }
            };
            var sorted = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                GradientStops =
                {
                    new GradientStop(Red, 0),
                    new GradientStop(Green, 0.5),
                    new GradientStop(Blue, 1)
                }
            };
            var bounds = new PixelRect(0, 0, 4, 1);

            for (int x = 0; x < 4; x++)
                Assert.AreEqual(ColorAt(sorted, x, 0, bounds), ColorAt(unsorted, x, 0, bounds),
                    $"unsorted differs from sorted at x={x}");

            // x=1: t=0.375 between Red@0 and Green@0.5, ratio 0.75 => R=64, G=191, B=0.
            Assert.AreEqual(Color.FromArgb(255, 64, 191, 0), ColorAt(unsorted, 1, 0, bounds));
        }

        // Brush opacity scales the resulting alpha while leaving the RGB channels untouched.
        [Test]
        public void BrushOpacityScalesAlphaOnInterpolatedStops()
        {
            var bounds = new PixelRect(0, 0, 4, 1);

            var opaque = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                GradientStops = { new GradientStop(Red, 0), new GradientStop(Blue, 1) }
            };
            var translucent = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                Opacity = 0.5,
                GradientStops = { new GradientStop(Red, 0), new GradientStop(Blue, 1) }
            };

            // x=1: t=0.375, Red->Blue => (A=255, R=159, G=0, B=96).
            Assert.AreEqual(Color.FromArgb(255, 159, 0, 96), ColorAt(opaque, 1, 0, bounds));
            // Opacity 0.5 halves the alpha (round(255*0.5)=128); RGB unchanged.
            Assert.AreEqual(Color.FromArgb(128, 159, 0, 96), ColorAt(translucent, 1, 0, bounds));
        }

        // Opacity must also apply on the padded path (where the sample falls outside the covered stop range).
        [Test]
        public void BrushOpacityAppliesOnPaddedStops()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = Rel(0, 0),
                EndPoint = Rel(1, 0),
                Opacity = 0.25,
                SpreadMethod = GradientSpreadMethod.Pad,
                GradientStops = { new GradientStop(Red, 0.25), new GradientStop(Blue, 0.75) }
            };
            var bounds = new PixelRect(0, 0, 4, 1);

            // x=0: t=0.125 < 0.25 => padded to Red, alpha scaled by 0.25 (round(255*0.25)=64).
            Assert.AreEqual(Color.FromArgb(64, 255, 0, 0), ColorAt(brush, 0, 0, bounds));
        }
    }
}
