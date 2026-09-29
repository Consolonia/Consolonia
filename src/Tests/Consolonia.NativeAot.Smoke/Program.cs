using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Consolonia.Controls;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing.PixelBufferImplementation;

if (RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Publish and execute this probe as NativeAOT.");

AvaloniaLocator.CurrentMutable.Bind<IConsoleCapabilities>().ToConstant(new ProbeConsoleCapabilities());

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

var buffer = new PixelBuffer(1, 1);
string json = JsonSerializer.Serialize(buffer, PixelBufferJsonContext.Default.PixelBuffer);
PixelBuffer roundTrip = JsonSerializer.Deserialize(json, PixelBufferJsonContext.Default.PixelBuffer);
if (roundTrip is null || roundTrip.Width != 1 || roundTrip.Height != 1 || roundTrip[0] != buffer[0])
    throw new InvalidOperationException("NativeAOT PixelBuffer serialization failed.");

Console.WriteLine("NativeAOT LineBrush registration, BorderBrush construction, interpolation and PixelBuffer JSON passed.");

[JsonSerializable(typeof(PixelBuffer))]
internal partial class PixelBufferJsonContext : JsonSerializerContext
{
}

internal sealed class ProbeConsoleCapabilities : IConsoleCapabilities
{
    public ConsoleCapabilities Capabilities => ConsoleCapabilities.SupportsComplexEmoji;
}
