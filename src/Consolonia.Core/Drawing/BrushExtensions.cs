using System;
using Avalonia.Media;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    public static class BrushExtensions //todo: investigate why resharper does not complain about wrong file naming
    {
        public static Color FromPosition(this IBrush brush, int x, int y, int width, int height)
        {
            //todo: apply brush opacity
            ArgumentNullException.ThrowIfNull(brush);
            if (x < 0 || x > width)
                throw new ArgumentOutOfRangeException(nameof(x), "x is out bounds");
            if (y < 0 || y > height)
                throw new ArgumentOutOfRangeException(nameof(y), "y is out bounds");

            switch (brush)
            {
                case ILinearGradientBrush gradientBrush:
                {
                    if (width <= 0)
                        width = 1;
                    if (height <= 0)
                        height = 1;
                    // Calculate the relative position within the gradient
                    double horizontalRelativePosition = (double)x / width;
                    double verticalRelativePosition = (double)y / height;

                    // Interpolate horizontal and vertical colors
                    Color horizontalColor = InterpolateColor(gradientBrush, horizontalRelativePosition);
                    Color verticalColor = InterpolateColor(gradientBrush, verticalRelativePosition);

                    // Average the two colors to get the final color
                    Color color = BlendColors(horizontalColor, verticalColor);
                    return color;
                }
                case IRadialGradientBrush radialBrush:
                {
                    // Calculate the normalized center coordinates
                    double centerX = radialBrush.Center.Point.X * width;
                    double centerY = radialBrush.Center.Point.Y * height;

                    // Calculate the distance from the center
                    double dx = x - centerX;
                    double dy = y - centerY;

                    // Calculate the distance based on separate X and Y radii
                    double distanceX = dx / (width * radialBrush.RadiusX.Scalar);
                    double distanceY = dy / (height * radialBrush.RadiusY.Scalar);
                    double distance = Math.Sqrt(distanceX * distanceX + distanceY * distanceY);

                    // Normalize the distance
                    double normalizedDistance = distance /
                                                Math.Sqrt(radialBrush.RadiusX.Scalar * radialBrush.RadiusX.Scalar +
                                                          radialBrush.RadiusY.Scalar * radialBrush.RadiusY.Scalar);

                    // Clamp the normalized distance to [0, 1]
                    normalizedDistance = Math.Min(Math.Max(normalizedDistance, 0), 1);

                    // Interpolate the color based on the normalized distance
                    Color color = InterpolateColor(radialBrush, normalizedDistance);
                    return color;
                }
                case IConicGradientBrush conicBrush:
                {
                    if (width <= 0)
                        width = 1;
                    if (height <= 0)
                        height = 1;
                    // Calculate the relative position within the gradient
                    double horizontalRelativePosition = (double)x / width;
                    double verticalRelativePosition = (double)y / height;

                    // Interpolate horizontal and vertical colors
                    Color horizontalColor = InterpolateColor(conicBrush, horizontalRelativePosition);
                    Color verticalColor = InterpolateColor(conicBrush, verticalRelativePosition);

                    // Average the two colors to get the final color
                    Color color = BlendColors(horizontalColor, verticalColor);
                    return color;
                }

                case ISolidColorBrush solidColorBrush:
                    return solidColorBrush.Color;
                
                default:
                    return ConsoloniaPlatform.RaiseNotSupported<Color>(NotSupportedRequestCode.ColorFromBrushPosition,
                        brush, x, y,
                        width,
                        height);
            }
        }

        private static Color InterpolateColor(IGradientBrush brush, double relativePosition)
        {
            IGradientStop before = null;
            IGradientStop after = null;

            foreach (IGradientStop stop in brush.GradientStops)
                if (stop.Offset <= relativePosition)
                {
                    before = stop;
                }
                else if (stop.Offset >= relativePosition)
                {
                    after = stop;
                    break;
                }

            if (before == null && after == null)
                throw new ArgumentException("no gradientstops defined");

            if (before == null) return after.Color;
            if (after == null) return before.Color;

            double ratio = (relativePosition - before.Offset) / (after.Offset - before.Offset);
            byte r = (byte)(before.Color.R + ratio * (after.Color.R - before.Color.R));
            byte g = (byte)(before.Color.G + ratio * (after.Color.G - before.Color.G));
            byte b = (byte)(before.Color.B + ratio * (after.Color.B - before.Color.B));
            byte a = (byte)(before.Color.A + ratio * (after.Color.A - before.Color.A));

            return Color.FromArgb(a, r, g, b);
        }

        private static Color BlendColors(Color color1, Color color2)
        {
            int r = (color1.R + color2.R) / 2;
            int g = (color1.G + color2.G) / 2;
            int b = (color1.B + color2.B) / 2;
            int a = (color1.A + color2.A) / 2;
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }
    }
}