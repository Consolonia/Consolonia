using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Consolonia.Controls;

namespace Consolonia.Themes.Templates.Controls.Helpers
{
    public class ConsoloniaTextPresenter : TextPresenter
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public static readonly StyledProperty<Point> CaretPositionProperty =
            AvaloniaProperty.Register<ConsoloniaTextPresenter, Point>(nameof(CaretPosition));

        static ConsoloniaTextPresenter()
        {
            SelectionEndProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs<int>>(args =>
            {
                if (args.Sender is not ConsoloniaTextPresenter textPresenter)
                    return;

                textPresenter.UpdateCaretPosition(null);
            }));

            CaretIndexProperty.Changed
                .SubscribeAction(args =>
                {
                    if (args.Sender is not ConsoloniaTextPresenter textPresenter)
                        return;

                    // once avalonia moved the caret we then moving it additionally to scroll outside the boundaries

                    int caretIndex = args.NewValue.Value;

                    Rect hitTestTextPosition = textPresenter.UpdateCaretPosition(caretIndex);

                    Dispatcher.UIThread.Post(
                        () =>
                        {
                            textPresenter.BringIntoView(new Rect(hitTestTextPosition.X, hitTestTextPosition.Y, 1, 1));
                        },
                        DispatcherPriority
                            .UiThreadRender /*Must be lower than DispatcherPriority.AfterRender which is used by TextPresenter*/);
                });

            CaretBrushProperty.Changed
                .Subscribe(
                    new AnonymousObserver<AvaloniaPropertyChangedEventArgs<IBrush>>(
                        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local //todo: what does this mean?
                        args =>
                        {
                            if (args.NewValue.Value.Opacity != 0 &&
                                ((ISolidColorBrush)args.NewValue.Value).Color.A != 0x0)
                                throw new NotSupportedException(
                                    "CaretBrush must have a transparent background. This ensures proper rendering of the caret over text content.");
                        }));
        }

        public ConsoloniaTextPresenter()
        {
            // Avalonia does not create a caret timer when the interval is non-positive.
            CaretBlinkInterval = TimeSpan.FromSeconds(-1);

            CaretBrush = Brushes.Transparent; // we want to draw own caret
        }

        public Point CaretPosition
        {
            get => GetValue(CaretPositionProperty);
            private set => SetValue(CaretPositionProperty, value);
        }

        private Rect UpdateCaretPosition(int? caretIndex)
        {
            caretIndex ??= CaretIndex;

            Rect hitTestTextPosition = TextLayout.HitTestTextPosition(caretIndex.Value);
            CaretPosition = new Point(hitTestTextPosition.X, hitTestTextPosition.Y);
            return hitTestTextPosition;
        }
    }
}