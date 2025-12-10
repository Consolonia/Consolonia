using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Utilities;
using Consolonia.Controls;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Themes.Templates.Controls.Helpers
{
    internal static class SelectTextWithPointerUpExtension
    {
        public static readonly AttachedProperty<bool> SelectOnMouseLeftUpProperty =
            AvaloniaProperty.RegisterAttached<SelectableTextBlock, bool>(ControlUtils.GetStyledPropertyName(),
                typeof(SelectTextWithPointerUpExtension));

        static SelectTextWithPointerUpExtension()
        {
            var console = AvaloniaLocator.Current.GetService<IConsole>();
            bool supportsMouse = console.Capabilities.HasFlag(ConsoleCapabilities.SupportsMouse);
            bool supportsMouseMove = console.Capabilities.HasFlag(ConsoleCapabilities.SupportsMouseMove);
            if (!supportsMouse || supportsMouseMove)
                return;

            SelectOnMouseLeftUpProperty.Changed.SubscribeAction(OnPropertyChanged);
        }

        private static void OnPropertyChanged(AvaloniaPropertyChangedEventArgs<bool> args)
        {
            if (args.Sender is not SelectableTextBlock selectableTextBlock) return;

            selectableTextBlock.PointerReleased -= OnPointerReleased;

            if (args.GetNewValue<bool>()) selectableTextBlock.PointerReleased += OnPointerReleased;
        }

        private static void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            // simplified copy of SelectableTextBlock.PointerMove
            var tb = (SelectableTextBlock)sender;

            if (e.InitialPressMouseButton != MouseButton.Left)
                return;

            Thickness padding = tb.Padding;

            Point point = e.GetPosition(tb) - new Point(padding.Left, padding.Top);

            point = new Point(
                MathUtilities.Clamp(point.X, 0, Math.Max(tb.TextLayout.WidthIncludingTrailingWhitespace, 0)),
                MathUtilities.Clamp(point.Y, 0, Math.Max(tb.TextLayout.Height, 0)));

            TextHitTestResult hit = tb.TextLayout.HitTestPoint(point);
            int textPosition = hit.TextPosition;
            tb.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, textPosition);
        }
    }
}