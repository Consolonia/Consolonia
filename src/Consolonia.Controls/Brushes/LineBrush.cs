using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Threading;

// ReSharper disable CheckNamespace
namespace Consolonia.Controls.Brushes
{
    /// <summary>
    ///     This brush will draw using a LineStyle
    /// </summary>
    public class LineBrush : Animatable, IImmutableBrush
    {
        //todo: we don't really implement immutable brush
        public static readonly StyledProperty<IBrush> BrushProperty =
            AvaloniaProperty.Register<LineBrush, IBrush>(
                ControlUtils.GetStyledPropertyName() /*todo: re-use this method everywhere*/);

        public static readonly StyledProperty<LineStyles> LineStyleProperty =
            AvaloniaProperty.Register<LineBrush, LineStyles>(ControlUtils.GetStyledPropertyName());

        private IBrush _brush;
        private LineStyles _lineStyle;

        static LineBrush()
        {
            BrushProperty.Changed.AddClassHandler<LineBrush>((brush, args) =>
            {
                if (args.OldValue is AvaloniaObject oldBrush)
                    oldBrush.PropertyChanged -= brush.OnUnderlyingBrushPropertyChanged;

                if (args.NewValue is AvaloniaObject newBrush)
                    newBrush.PropertyChanged += brush.OnUnderlyingBrushPropertyChanged;

                brush._brush = ((IBrush)args.NewValue)?.ToImmutable();
            });
            LineStyleProperty.Changed.AddClassHandler<LineBrush>((brush, args) =>
                brush._lineStyle = ((LineStyles)args.NewValue)?.Clone());
        }

        public LineBrush()
        {
            if (Brush is AvaloniaObject avaloniaObject)
                avaloniaObject.PropertyChanged += OnUnderlyingBrushPropertyChanged;

            _brush = Brush?.ToImmutable();
            _lineStyle = LineStyle?.Clone();
        }

        public IBrush Brush
        {
            get
            {
                if (Dispatcher.UIThread.CheckAccess())
                    return GetValue(BrushProperty);
                return _brush;
            }
            set => SetValue(BrushProperty, value);
        }

        public LineStyles LineStyle
        {
            get
            {
                if (Dispatcher.UIThread.CheckAccess())
                    return GetValue(LineStyleProperty);
                return _lineStyle;
            }
            set => SetValue(LineStyleProperty, value);
        }

        //todo: how did it work without following 3 items? How should it work now, check avalonia. Search for B75ABC91-2CDD-4557-9201-16AC483C8D7B
        public double Opacity => 1;
        public ITransform Transform => null;
        public RelativePoint TransformOrigin => RelativePoint.TopLeft;

        private void OnUnderlyingBrushPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            _brush = Brush?.ToImmutable();
        }

        public bool HasEdgeLineStyle()
        {
            return LineStyle.Left == Brushes.LineStyle.Edge || LineStyle.Left == Brushes.LineStyle.EdgeWide ||
                   LineStyle.Top == Brushes.LineStyle.Edge || LineStyle.Top == Brushes.LineStyle.EdgeWide ||
                   LineStyle.Right == Brushes.LineStyle.Edge || LineStyle.Right == Brushes.LineStyle.EdgeWide ||
                   LineStyle.Bottom == Brushes.LineStyle.Edge || LineStyle.Bottom == Brushes.LineStyle.EdgeWide;
        }
    }
}