using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    [SuppressMessage("ReSharper", "NotResolvedInText", MessageId = "Text")]
    [DebuggerDisplay("'{Symbol}' [{Color}]")]
    [JsonConverter(typeof(PixelForegroundConverter))]
    public readonly struct PixelForeground : IEquatable<PixelForeground>
    {
        /// <summary>Not set, in the byte that holds a style or a decoration.</summary>
        private const byte None = 0xFF;

        public static readonly PixelForeground Default = new();

        public static readonly PixelForeground Space = new(Symbol.Space, Colors.Transparent);

        public static readonly PixelForeground Empty = new(Symbol.Empty, Colors.Transparent);

        public PixelForeground()
        {
            Symbol = Symbol.Space;
            Color = Colors.Transparent;
            _weight = 0;
            _style = None;
            _textDecoration = None;
        }

        public PixelForeground(Symbol symbol, Color color,
            FontWeight? weight = null, FontStyle? style = null,
            TextDecorationLocation? textDecoration = null)
        {
            Symbol = symbol;
            Color = color;
            _weight = weight ?? 0;
            _style = style == null ? None : (byte)style.Value;
            _textDecoration = textDecoration == null ? None : (byte)textDecoration.Value;
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Symbol Symbol;
        public readonly Color Color;
#pragma warning restore CA1051 // Do not declare visible instance fields

        // The three optional attributes packed into 6 bytes rather than three nullable enums of 8 each:
        // every cell of the pixel buffer carries them, and it keeps a Pixel within a cache line.
        // FontWeight has no zero (Thin is 100), so 0 is "not set"; the other two fit a byte with a sentinel.
        private readonly FontWeight _weight;
        private readonly byte _style;
        private readonly byte _textDecoration;

        public FontWeight? Weight => _weight == 0 ? null : _weight;

        public FontStyle? Style => _style == None ? null : (FontStyle)_style;

        public TextDecorationLocation? TextDecoration =>
            _textDecoration == None ? null : (TextDecorationLocation)_textDecoration;

        public bool Equals(PixelForeground other)
        {
            return Symbol.Equals(other.Symbol) &&
                   Color.Equals(other.Color) &&
                   _weight == other._weight &&
                   _style == other._style &&
                   _textDecoration == other._textDecoration;
        }

        public PixelForeground Shade()
        {
            return new PixelForeground(Symbol, Color.Shade(), Weight, Style, TextDecoration);
        }

        public PixelForeground Brighten()
        {
            return new PixelForeground(Symbol, Color.Brighten(), Weight, Style, TextDecoration);
        }

        public PixelForeground Blend(PixelForeground pixelAboveForeground)
        {
            //todo: check default(char) is there
            Symbol symbolAbove = pixelAboveForeground.Symbol;

            if (pixelAboveForeground.Color == Colors.Transparent)
                // if pixelAbove is transparent then the foreground below should be unchanged.
                return this;

            return new PixelForeground(Symbol.Blend(ref symbolAbove),
                pixelAboveForeground.Color,
                pixelAboveForeground.Weight ?? Weight,
                pixelAboveForeground.Style ?? Style,
                pixelAboveForeground.TextDecoration ?? TextDecoration);
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            return obj is PixelForeground other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Symbol, Color, (int)_weight, _style, _textDecoration);
        }

        public static bool operator ==(PixelForeground left, PixelForeground right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PixelForeground left, PixelForeground right)
        {
            return !left.Equals(right);
        }

        public bool IsNothingToDraw()
        {
            return Color.A == 0x0 || Symbol.NothingToDraw();
        }
    }
}
