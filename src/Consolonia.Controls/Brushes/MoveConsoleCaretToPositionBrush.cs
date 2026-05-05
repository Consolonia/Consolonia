using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Consolonia.Controls.Brushes
{
    /// <summary>
    ///     This brush will move the console caret to the specified position it is drawn into.
    /// </summary>
    public class MoveConsoleCaretToPositionBrush : AvaloniaObject, IImmutableBrush
    {
        private CaretStyle _caretStyle;

        static MoveConsoleCaretToPositionBrush()
        {
            CaretStyleProperty.Changed.AddClassHandler<MoveConsoleCaretToPositionBrush>((brush, args) =>
                brush._caretStyle = (CaretStyle)args.NewValue);
        }

        public MoveConsoleCaretToPositionBrush()
        {
            _caretStyle = CaretStyle;
        }

        public static readonly StyledProperty<CaretStyle> CaretStyleProperty =
            AvaloniaProperty.Register<MoveConsoleCaretToPositionBrush, CaretStyle>(nameof(CaretStyle),
                CaretStyle.BlinkingBar);

        /// <summary>
        ///     style of caret
        /// </summary>
        public CaretStyle CaretStyle
        {
            get
            {
                if (Dispatcher.UIThread.CheckAccess())
                    return GetValue(CaretStyleProperty);
                return _caretStyle;
            }
            set => SetValue(CaretStyleProperty, value);
        }

        //todo: Search for B75ABC91-2CDD-4557-9201-16AC483C8D7B
        public double Opacity => 1;
        public ITransform Transform => null;
        public RelativePoint TransformOrigin => RelativePoint.TopLeft;
    }
}