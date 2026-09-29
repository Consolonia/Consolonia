using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Consolonia.Controls.Brushes;

if (RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Publish and execute this probe as NativeAOT.");

var from = new LineBrush
{
    Brush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1) }
    },
    LineStyle = LineStyle.SingleLine
};
var to = new LineBrush
{
    Brush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1) }
    },
    LineStyle = LineStyle.DoubleLine
};

var border = new Border { BorderBrush = from };
LineBrushAnimator.EnsureRegistered();
var animator = new LineBrushAnimator();
var middle = (LineBrush)animator.Interpolate(0.5, border.BorderBrush, to);
if (middle.Brush is not ILinearGradientBrush gradient ||
    Math.Abs(gradient.StartPoint.Point.X - 0.5) > 0.001 ||
    middle.LineStyle.Top != LineStyle.DoubleLine)
    throw new InvalidOperationException("NativeAOT LineBrush interpolation failed.");

var solid = (ISolidColorBrush)animator.Interpolate(0.5,
    new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White));
if (solid.Color != Color.FromRgb(188, 188, 188))
    throw new InvalidOperationException("NativeAOT ordinary brush interpolation failed.");

Console.WriteLine("NativeAOT LineBrush registration, BorderBrush construction and interpolation passed.");
