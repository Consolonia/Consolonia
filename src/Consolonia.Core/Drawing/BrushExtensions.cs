using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    public static class BrushExtensions //todo: investigate why resharper does not complain about wrong file naming
    {
        /// <summary>
        ///     Resolve the color a brush produces for a single console cell.
        /// </summary>
        /// <param name="brush">
        ///     The brush. Solid color brushes return their color directly; gradient brushes are sampled.
        ///     The brush may be wrapped in a <see cref="LineBrush" />, in which case its inner brush is used.
        /// </param>
        /// <param name="point">The absolute buffer coordinate of the cell being painted.</param>
        /// <param name="bounds">
        ///     The bounds the gradient is mapped onto (typically the shape being painted). Gradient relative
        ///     coordinates (0,0)-(1,1) span this rectangle, and <paramref name="point" /> is sampled within it.
        /// </param>
        public static Color ColorAt(this IBrush brush, PixelPoint point, PixelRect bounds)
        {
            ArgumentNullException.ThrowIfNull(brush);

            // A gradient (or solid) may be wrapped inside a LineBrush; unwrap to the painting brush.
            if (brush is LineBrush lineBrush)
            {
                if (lineBrush.Brush == null)
                    return Colors.Transparent;
                brush = lineBrush.Brush;
            }

            switch (brush)
            {
                case ISolidColorBrush solidColorBrush:
                    return solidColorBrush.Color;

                case IGradientBrush gradientBrush:
                {
                    // Treat the clip as the gradient's coordinate space and sample the center of the cell.
                    double width = Math.Max(1, bounds.Width);
                    double height = Math.Max(1, bounds.Height);
                    var size = new Size(width, height);
                    double px = point.X - bounds.X + 0.5;
                    double py = point.Y - bounds.Y + 0.5;

                    double t = GetGradientPosition(gradientBrush, px, py, size);
                    return GetGradientColor(gradientBrush, t);
                }

                default:
                    return ConsoloniaPlatform.RaiseNotSupported<Color>(NotSupportedRequestCode.ColorFromBrushPosition,
                        brush, point.X, point.Y, bounds.Width, bounds.Height);
            }
        }

        /// <summary>
        ///     Compute the gradient parameter (before spread/stop evaluation) for a point within the gradient bounds.
        /// </summary>
        private static double GetGradientPosition(IGradientBrush brush, double px, double py, Size size)
        {
            switch (brush)
            {
                case ILinearGradientBrush linearBrush:
                {
                    Point start = linearBrush.StartPoint.ToPixels(size);
                    Point end = linearBrush.EndPoint.ToPixels(size);
                    double vx = end.X - start.X;
                    double vy = end.Y - start.Y;
                    double lengthSquared = vx * vx + vy * vy;
                    if (lengthSquared <= double.Epsilon)
                        return 0;

                    // Project the point onto the gradient axis.
                    return ((px - start.X) * vx + (py - start.Y) * vy) / lengthSquared;
                }

                case IRadialGradientBrush radialBrush:
                {
                    Point center = radialBrush.Center.ToPixels(size);
                    Point origin = radialBrush.GradientOrigin.ToPixels(size);
                    double radiusX = radialBrush.RadiusX.ToValue(size.Width);
                    double radiusY = radialBrush.RadiusY.ToValue(size.Height);
                    if (radiusX <= double.Epsilon || radiusY <= double.Epsilon)
                        return 0;

                    // Work in a normalized space where the gradient ellipse becomes the unit circle.
                    double fx = (origin.X - center.X) / radiusX;
                    double fy = (origin.Y - center.Y) / radiusY;
                    double pxn = (px - center.X) / radiusX;
                    double pyn = (py - center.Y) / radiusY;

                    double dx = pxn - fx;
                    double dy = pyn - fy;
                    double a = dx * dx + dy * dy;
                    if (a <= double.Epsilon)
                        return 0;

                    // Cast a ray from the focal point through the sample point to the unit circle.
                    // Solve |f + (p-f)*s| = 1 for the positive root s; the gradient value is 1/s.
                    double b = 2 * (fx * dx + fy * dy);
                    double c = fx * fx + fy * fy - 1;
                    double discriminant = b * b - 4 * a * c;
                    if (discriminant < 0)
                        return 1;

                    double s = (-b + Math.Sqrt(discriminant)) / (2 * a);
                    return s <= double.Epsilon ? 1 : 1 / s;
                }

                case IConicGradientBrush conicBrush:
                {
                    Point center = conicBrush.Center.ToPixels(size);
                    double dx = px - center.X;
                    double dy = py - center.Y;

                    // Angle measured clockwise from straight up (12 o'clock), matching Avalonia.
                    double angleDegrees = Math.Atan2(dx, -dy) * (180.0 / Math.PI);
                    double t = (angleDegrees - conicBrush.Angle) / 360.0;
                    t -= Math.Floor(t); // wrap into [0, 1)
                    return t;
                }

                default:
                    return 0;
            }
        }

        /// <summary>
        ///     Apply the gradient's spread method and evaluate its stops at the given parameter.
        /// </summary>
        private static Color GetGradientColor(IGradientBrush brush, double position)
        {
            IReadOnlyList<IGradientStop> stops = brush.GradientStops;


            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (stops == null || stops.Count == 0)
                return Colors.Transparent;

            // Spread is defined over the gradient vector's [0, 1] interval, independent of where the stops sit.
            // Conic gradients already wrap into [0, 1), so they are unaffected by this mapping.
            position = ApplySpread(position, brush.SpreadMethod);

            IGradientStop before = null;
            IGradientStop after = null;

            foreach (IGradientStop stop in stops)
                if (stop.Offset <= position)
                {
                    if (before == null || stop.Offset >= before.Offset)
                        before = stop;
                }
                else
                {
                    if (after == null || stop.Offset <= after.Offset)
                        after = stop;
                }

            // Pad to the first/last stop color outside the covered offset range.
            if (before == null)
                return ApplyOpacity(after.Color, brush.Opacity);
            if (after == null)
                return ApplyOpacity(before.Color, brush.Opacity);
            if (before == after)
                return ApplyOpacity(before.Color, brush.Opacity);

            // Avoid possible division by zero.
            if (Math.Abs(after.Offset - before.Offset) < double.Epsilon)
                return ApplyOpacity(before.Color, brush.Opacity);

            double ratio = (position - before.Offset) / (after.Offset - before.Offset);

            Color color = Color.FromArgb(
                Lerp(before.Color.A, after.Color.A, ratio),
                Lerp(before.Color.R, after.Color.R, ratio),
                Lerp(before.Color.G, after.Color.G, ratio),
                Lerp(before.Color.B, after.Color.B, ratio));

            return ApplyOpacity(color, brush.Opacity);
        }

        /// <summary>
        ///     Map a raw gradient parameter into [0, 1] according to the spread method.
        /// </summary>
        private static double ApplySpread(double t, GradientSpreadMethod spreadMethod)
        {
            switch (spreadMethod)
            {
                case GradientSpreadMethod.Repeat:
                    return t - Math.Floor(t);
                case GradientSpreadMethod.Reflect:
                {
                    double m = t - 2.0 * Math.Floor(t / 2.0); // [0, 2)
                    return m > 1.0 ? 2.0 - m : m;
                }
                default: // Pad
                    return Math.Clamp(t, 0.0, 1.0);
            }
        }

        /// <summary>
        ///     Scales a color's alpha channel by the brush opacity, leaving the RGB channels unchanged.
        /// </summary>
        /// <param name="color">The color to apply opacity to.</param>
        /// <param name="opacity">The brush opacity, clamped to [0, 1].</param>
        /// <returns>The color with its alpha scaled by <paramref name="opacity" />.</returns>
        private static Color ApplyOpacity(Color color, double opacity)
        {
            if (opacity >= 1.0)
                return color;
            opacity = Math.Clamp(opacity, 0.0, 1.0);
            return Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }

        /// <summary>
        ///     Linearly interpolates a single byte channel and rounds to the nearest value.
        /// </summary>
        /// <param name="from">The channel value at ratio 0.</param>
        /// <param name="to">The channel value at ratio 1.</param>
        /// <param name="ratio">The interpolation ratio, normally in [0, 1].</param>
        /// <returns>The interpolated channel value.</returns>
        private static byte Lerp(byte from, byte to, double ratio)
        {
            return (byte)Math.Round(from + ratio * (to - from));
        }
    }
}