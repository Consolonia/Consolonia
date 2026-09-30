using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;

namespace Consolonia.Controls.Brushes
{
    /// <summary>
    ///     Animates a <see cref="LineBrush" /> by interpolating its inner brush between keyframes.
    /// </summary>
    /// <remarks>
    ///     Avalonia's public animator registration API selects by property type, not brush value type.
    ///     Registration for <see cref="IBrush" /> also handles ordinary solid and gradient brushes.
    /// </remarks>
    public sealed class LineBrushAnimator : InterpolatingAnimator<IBrush>
    {
        private static readonly object RegistrationGate = new();
        private static bool _registered;

        /// <summary>
        ///     Registers the animator with Avalonia. Safe to call multiple times.
        /// </summary>
        public static void EnsureRegistered()
        {
            lock (RegistrationGate)
            {
                if (!_registered)
                {
                    Animation.RegisterCustomAnimator<IBrush, LineBrushAnimator>();
                    _registered = true;
                }
            }
        }

        /// <summary>
        ///     Interpolates compatible brushes, switching unsupported values at the halfway point.
        /// </summary>
        public override IBrush Interpolate(double progress, IBrush oldValue, IBrush newValue)
        {
            if (oldValue is LineBrush oldLine && newValue is LineBrush newLine)
                return new LineBrush
                {
                    Brush = InterpolateBrush(progress, oldLine.Brush, newLine.Brush, true),
                    LineStyle = progress >= 0.5 ? newLine.LineStyle : oldLine.LineStyle
                };

            if (oldValue is LineBrush || newValue is LineBrush)
                return progress >= 0.5 ? newValue : oldValue;

            return InterpolateBrush(progress, oldValue, newValue, false);
        }

        private static IBrush InterpolateBrush(double progress, IBrush oldBrush, IBrush newBrush, bool isInnerBrush)
        {
            if (oldBrush is IGradientBrush oldGradient)
            {
                if (newBrush is IGradientBrush newGradient)
                    return InterpolateGradient(progress, oldGradient, newGradient);

                if (!isInnerBrush && newBrush is ISolidColorBrush newSolid)
                    return InterpolateGradient(progress, oldGradient, ConvertSolidToGradient(oldGradient, newSolid));
            }

            if (!isInnerBrush && newBrush is IGradientBrush nextGradient && oldBrush is ISolidColorBrush oldColor)
                return InterpolateGradient(progress, ConvertSolidToGradient(nextGradient, oldColor), nextGradient);

            if (oldBrush is ISolidColorBrush oldSolid && newBrush is ISolidColorBrush nextSolid)
            {
                Color color = isInnerBrush
                    ? InterpolateColor(progress, oldSolid.Color, nextSolid.Color)
                    : InterpolateSrgbColor(progress, oldSolid.Color, nextSolid.Color);
                double opacity = Lerp(progress, oldSolid.Opacity, nextSolid.Opacity);

                return isInnerBrush
                    ? new ImmutableSolidColorBrush(color, opacity,
                        InterpolateTransform(progress, oldSolid.Transform, nextSolid.Transform))
                    : new ImmutableSolidColorBrush(color, opacity);
            }

            return progress >= 0.5 ? newBrush : oldBrush;
        }

        private static IGradientBrush InterpolateGradient(double progress, IGradientBrush from, IGradientBrush to)
        {
            bool compatible = from is ILinearGradientBrush && to is ILinearGradientBrush ||
                              from is IRadialGradientBrush && to is IRadialGradientBrush ||
                              from is IConicGradientBrush && to is IConicGradientBrush;
            if (!compatible || from.GradientStops.Count == 0 || to.GradientStops.Count == 0)
                return progress >= 0.5 ? to : from;

            IReadOnlyList<ImmutableGradientStop> stops =
                InterpolateStops(progress, from.GradientStops, to.GradientStops);
            double opacity = Lerp(progress, from.Opacity, to.Opacity);
            ImmutableTransform transform = InterpolateTransform(progress, from.Transform, to.Transform);
            RelativePoint origin = InterpolatePoint(progress, from.TransformOrigin, to.TransformOrigin);

            return (from, to) switch
            {
                (ILinearGradientBrush oldLinear, ILinearGradientBrush newLinear) =>
                    new ImmutableLinearGradientBrush(stops, opacity, transform, origin, from.SpreadMethod,
                        InterpolatePoint(progress, oldLinear.StartPoint, newLinear.StartPoint),
                        InterpolatePoint(progress, oldLinear.EndPoint, newLinear.EndPoint)),
                (IRadialGradientBrush oldRadial, IRadialGradientBrush newRadial) =>
                    new ImmutableRadialGradientBrush(stops, opacity, transform, origin, from.SpreadMethod,
                        InterpolatePoint(progress, oldRadial.Center, newRadial.Center),
                        InterpolatePoint(progress, oldRadial.GradientOrigin, newRadial.GradientOrigin),
                        InterpolateScalar(progress, oldRadial.RadiusX, newRadial.RadiusX),
                        InterpolateScalar(progress, oldRadial.RadiusY, newRadial.RadiusY)),
                (IConicGradientBrush oldConic, IConicGradientBrush newConic) =>
                    new ImmutableConicGradientBrush(stops, opacity, transform, origin, from.SpreadMethod,
                        InterpolatePoint(progress, oldConic.Center, newConic.Center),
                        Lerp(progress, oldConic.Angle, newConic.Angle)),
                _ => progress >= 0.5 ? to : from
            };
        }

        private static IReadOnlyList<ImmutableGradientStop> InterpolateStops(
            double progress, IReadOnlyList<IGradientStop> from, IReadOnlyList<IGradientStop> to)
        {
            var stops = new ImmutableGradientStop[Math.Max(from.Count, to.Count)];
            for (int i = 0; i < stops.Length; i++)
            {
                IGradientStop oldStop = from[Math.Min(i, from.Count - 1)];
                IGradientStop newStop = to[Math.Min(i, to.Count - 1)];
                stops[i] = new ImmutableGradientStop(
                    Lerp(progress, oldStop.Offset, newStop.Offset),
                    InterpolateSrgbColor(progress, oldStop.Color, newStop.Color));
            }

            return stops;
        }

        private static IGradientBrush ConvertSolidToGradient(IGradientBrush gradient, ISolidColorBrush solid)
        {
            var stops = new ImmutableGradientStop[gradient.GradientStops.Count];
            for (int i = 0; i < stops.Length; i++)
                stops[i] = new ImmutableGradientStop(gradient.GradientStops[i].Offset, solid.Color);

            ImmutableTransform transform = gradient.Transform is { } original
                ? new ImmutableTransform(original.Value)
                : null;

            return gradient switch
            {
                ILinearGradientBrush linear => new ImmutableLinearGradientBrush(stops, solid.Opacity, transform,
                    linear.TransformOrigin, linear.SpreadMethod, linear.StartPoint, linear.EndPoint),
                IRadialGradientBrush radial => new ImmutableRadialGradientBrush(stops, solid.Opacity, transform,
                    radial.TransformOrigin, radial.SpreadMethod, radial.Center, radial.GradientOrigin,
                    radial.RadiusX, radial.RadiusY),
                IConicGradientBrush conic => new ImmutableConicGradientBrush(stops, solid.Opacity, transform,
                    conic.TransformOrigin, conic.SpreadMethod, conic.Center, conic.Angle),
                _ => throw new NotSupportedException($"Gradient of type {gradient.GetType()} is not supported")
            };
        }

        private static RelativePoint InterpolatePoint(double progress, RelativePoint from, RelativePoint to)
        {
            if (from.Unit != to.Unit)
                return progress >= 0.5 ? to : from;

            return new RelativePoint(
                new Point(Lerp(progress, from.Point.X, to.Point.X),
                    Lerp(progress, from.Point.Y, to.Point.Y)), from.Unit);
        }

        private static RelativeScalar InterpolateScalar(double progress, RelativeScalar from, RelativeScalar to)
        {
            if (from.Unit != to.Unit)
                return progress >= 0.5 ? to : from;

            return new RelativeScalar(Lerp(progress, from.Scalar, to.Scalar), from.Unit);
        }

        private static Color InterpolateColor(double progress, Color from, Color to)
        {
            return Color.FromArgb(
                Lerp(from.A, to.A, progress),
                Lerp(from.R, to.R, progress),
                Lerp(from.G, to.G, progress),
                Lerp(from.B, to.B, progress));
        }

        private static Color InterpolateSrgbColor(double progress, Color from, Color to)
        {
            static double Decode(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
            }

            static byte Encode(double value)
            {
                double srgb = value <= 0.0031308d
                    ? value * 12.92d
                    : Math.Pow(value, 1d / 2.4d) * 1.055d - 0.055d;
                return (byte)Math.Round(srgb * 255d);
            }

            return Color.FromArgb(
                Lerp(from.A, to.A, progress),
                Encode(Lerp(progress, Decode(from.R), Decode(to.R))),
                Encode(Lerp(progress, Decode(from.G), Decode(to.G))),
                Encode(Lerp(progress, Decode(from.B), Decode(to.B))));
        }

        private static double Lerp(double progress, double from, double to)
        {
            return from + (to - from) * progress;
        }

        private static ImmutableTransform InterpolateTransform(
            double progress, ITransform oldTransform, ITransform newTransform)
        {
            if (oldTransform is TransformOperations oldTransformOperations &&
                newTransform is TransformOperations newTransformOperations)
                return new ImmutableTransform(
                    TransformOperations.Interpolate(oldTransformOperations, newTransformOperations, progress).Value);

            if (oldTransform is not null)
                return new ImmutableTransform(oldTransform.Value);

            return null;
        }

        private static byte Lerp(byte from, byte to, double progress)
        {
            return (byte)Math.Round(from + (to - from) * progress);
        }
    }
}