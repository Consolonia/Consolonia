using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Consolonia;
using Consolonia.Controls.DataGrid;
using Consolonia.Controls;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using Consolonia.ManagedWindows;
using Consolonia.Themes.Templates.Controls.Helpers;
using Consolonia.Themes.Infrastructure;

if (RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Publish and execute this probe as NativeAOT.");

var console = new ProbeConsole();
AvaloniaLocator.CurrentMutable.Bind<IConsoleCapabilities>().ToConstant(console)
    .Bind<IConsole>().ToConstant(console);

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

IBrush emptyGradient = new LinearGradientBrush();
IBrush solidBrush = new SolidColorBrush(Colors.Green);
foreach (double progress in new[] { 0.25, 0.5, 0.75 })
{
    if (!ReferenceEquals(animator.Interpolate(progress, emptyGradient, solidBrush),
            progress >= 0.5 ? solidBrush : emptyGradient) ||
        !ReferenceEquals(animator.Interpolate(progress, solidBrush, emptyGradient),
            progress >= 0.5 ? emptyGradient : solidBrush))
        throw new InvalidOperationException("NativeAOT empty gradient fallback failed.");
}

var buffer = new PixelBuffer(1, 1);
string json = JsonSerializer.Serialize(buffer, PixelBufferJsonContext.Default.PixelBuffer);
PixelBuffer roundTrip = JsonSerializer.Deserialize(json, PixelBufferJsonContext.Default.PixelBuffer);
if (roundTrip is null || roundTrip.Width != 1 || roundTrip.Height != 1 || roundTrip[0] != buffer[0])
    throw new InvalidOperationException("NativeAOT PixelBuffer serialization failed.");

new ConsoloniaPlatform().Initialize();
using (var window = new ConsoleWindowImpl())
{
    if (window.TryGetFeature(typeof(ILauncher)) is not ILauncher launcher ||
        await launcher.LaunchUriAsync(new Uri("relative", UriKind.Relative)))
        throw new InvalidOperationException("NativeAOT Avalonia launcher activation failed.");
}

var screen = new ConsoloniaScreen(new PixelRect(0, 0, 85, 28)).AllScreens[0];
if (!screen.IsPrimary || screen.Bounds.Width != 85 || screen.Bounds.Height != 28 ||
    screen.WorkingArea != screen.Bounds || screen.DisplayName != "Console")
    throw new InvalidOperationException("NativeAOT console screen initialization failed.");

if (new ConsoloniaTextPresenter().CaretBlinkInterval >= TimeSpan.Zero)
    throw new InvalidOperationException("NativeAOT text presenter caret timer was not disabled.");

var modernGrid = new ModernDataGridStyles();
var turboGrid = new TurboVisionDataGridStyles();
var modernWindows = new ModernManagedWindowStyles();
if (modernGrid.Count == 0 ||
    turboGrid.Resources.Count == 0 ||
    modernWindows.Resources.ThemeDictionaries.Count == 0)
    throw new InvalidOperationException("NativeAOT compiled theme styles failed to load.");

var autoGrid = new AutoDataGridStyles();
var host = new Control();
host.Styles.Add(autoGrid);
host.Resources[AutoThemeStylesBase.ConsoloniaThemeFamilyKey] = AutoThemeStylesBase.ModernThemeKey;
if (autoGrid.Count != 1 || autoGrid[0] is not ModernDataGridStyles)
    throw new InvalidOperationException("NativeAOT Modern DataGrid styles were not selected.");
host.Resources[AutoThemeStylesBase.ConsoloniaThemeFamilyKey] = AutoThemeStylesBase.TurboVisionThemeKey;
if (autoGrid.Count != 1 || autoGrid[0] is not TurboVisionDataGridStyles)
    throw new InvalidOperationException("NativeAOT TurboVision DataGrid styles were not selected.");

AppBuilder.Configure<ProbeApp>()
    .UseConsolonia()
    .UseConsole(console)
    .UseClipboard(new ConsoleClipboard())
    .SetupWithoutStarting();
IClipboard clipboard = AvaloniaLocator.Current.GetRequiredService<IClipboard>();
using (var data = new AsyncDataTransfer(new AsyncDataTransferItem("native clipboard", DataFormat.Text)))
{
    await clipboard.SetDataAsync(data);
    using IAsyncDataTransfer clipboardData = await clipboard.TryGetDataAsync();
    if (clipboardData is null || await clipboardData.TryGetTextAsync() != "native clipboard" ||
        await clipboard.TryGetInProcessDataAsync() is not null)
        throw new InvalidOperationException("NativeAOT Avalonia clipboard round-trip failed.");
}
await clipboard.FlushAsync();
await clipboard.ClearAsync();
using IAsyncDataTransfer clearedData = await clipboard.TryGetDataAsync();
if (clearedData is null || clearedData.Items.Count != 0 || await clearedData.TryGetTextAsync() is not null)
    throw new InvalidOperationException("NativeAOT Avalonia clipboard clear failed.");

Console.WriteLine("NativeAOT brushes, PixelBuffer JSON, compiled theme styles, launcher and clipboard setup passed.");

[JsonSerializable(typeof(PixelBuffer))]
internal partial class PixelBufferJsonContext : JsonSerializerContext
{
}

internal sealed class ProbeConsole : DummyConsoleOutput, IConsole
{
    public ProbeConsole() : base(85, 28)
    {
    }

    event Action IConsole.Resized
    {
        add { }
        remove { }
    }

    event Action<Key, char, RawInputModifiers, bool, ulong, bool> IConsole.KeyEvent
    {
        add { }
        remove { }
    }

    event Action<string, ulong, CanBeHandledEventArgs> IConsole.TextInputEvent
    {
        add { }
        remove { }
    }

    event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> IConsole.MouseEvent
    {
        add { }
        remove { }
    }

    event Action<bool> IConsole.FocusEvent
    {
        add { }
        remove { }
    }

    public void StartInputLoop()
    {
    }
}

internal sealed class ProbeApp : Application
{
}
