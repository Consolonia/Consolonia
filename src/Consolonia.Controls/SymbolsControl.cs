using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace Consolonia.Controls
{
    /// <summary>
    ///     Represents a control that displays symbols.
    ///     This control has two modes of operation depending on the Fill property.
    ///     If the Fill property is false, it just draws <see cref="Text" />
    ///     If the Fill property is true, the symbol (Text[0]) is repeated and fills the control.
    /// </summary>
    public sealed class SymbolsControl : Control, IDisposable
    {
        /// <summary>
        ///     Defines the <see cref="Foreground" /> property.
        /// </summary>
        public static readonly StyledProperty<IBrush> ForegroundProperty =
            TextBlock.ForegroundProperty.AddOwner<SymbolsControl>();

        public static readonly DirectProperty<SymbolsControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<SymbolsControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v,
                TextBlock.TextProperty.GetDefaultValue(typeof(TextBlock)),
                BindingMode.TwoWay);

        public static readonly StyledProperty<bool> FillProperty =
            AvaloniaProperty.Register<SymbolsControl, bool>(nameof(Fill));

        private GlyphRun _shapedText;

        private string _text;

        static SymbolsControl()
        {
            AffectsRender<SymbolsControl>(ForegroundProperty, TextProperty, FillProperty);
            AffectsMeasure<SymbolsControl>(TextProperty, FillProperty);
            AffectsArrange<SymbolsControl>(TextProperty, FillProperty);
        }

        /// <summary>
        ///     Gets or sets a brush used to paint the text.
        /// </summary>
        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public bool Fill
        {
            get => GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;

                var textShaper = AvaloniaLocator.Current.GetService<ITextShaperImpl>();
                FontManager.Current.TryGetGlyphTypeface(Typeface.Default, out GlyphTypeface typeface);
                ArgumentNullException.ThrowIfNull(typeface);
                ShapedBuffer glyphs =
                    textShaper.ShapeText(value.AsMemory(),
                        new TextShaperOptions(typeface, typeface.Metrics.DesignEmHeight));
                var shapedText = new GlyphRun(typeface,
                    glyphs.FontRenderingEmSize,
                    _text.AsMemory(),
                    glyphs.ToArray(),
                    default(Point),
                    glyphs.BidiLevel);
                _shapedText?.Dispose();
                _shapedText = shapedText;
            }
        }

        public void Dispose()
        {
            _shapedText?.Dispose();
        }

        public override void Render(DrawingContext context)
        {
            if (!Fill)
            {
                context.DrawGlyphRun(Foreground, _shapedText);
            }
            else
            {
                if (Text is not { Length: > 0 }) return;
                // Draw the text as a repeating pattern
                var formattedText = new FormattedText(
                    string.Concat(
                        Enumerable.Repeat(Text, (int)Bounds.Width / Text.MeasureText())),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default, 1, Foreground);
                for (int y = 0; y < Bounds.Height; y++)
                    context.DrawText(formattedText, new Point(0, y));
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return !Fill ? _shapedText?.Bounds.Size ?? default : default;
        }
    }
}
