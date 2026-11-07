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
        public static readonly PixelForeground Default = new();

        public static readonly PixelForeground Space = new(Symbol.Space, Colors.Transparent);

        public static readonly PixelForeground Empty = new(Symbol.Empty, Colors.Transparent);

        public PixelForeground()
        {
            Symbol = Symbol.Space;
            Color = Colors.Transparent;
            Weight = null;
            Style = null;
            TextDecoration = null;
        }

        public PixelForeground(Symbol symbol, Color color,
            FontWeight? weight = null, FontStyle? style = null,
            TextDecorationLocation? textDecoration = null)
        {
            Symbol = symbol;
            Color = color;
            Weight = weight;
            Style = style;
            TextDecoration = textDecoration;
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Symbol Symbol;
        public readonly Color Color;
        public readonly FontWeight? Weight;
        public readonly FontStyle? Style;
        public readonly TextDecorationLocation? TextDecoration;
#pragma warning restore CA1051 // Do not declare visible instance fields

        public bool Equals(PixelForeground other)
        {
            return Symbol.Equals(other.Symbol) &&
                   Color.Equals(other.Color) &&
                   Weight == other.Weight &&
                   Style == other.Style &&
                   TextDecoration == other.TextDecoration;
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

            if (pixelAboveForeground.IsTransparent)
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
            return HashCode.Combine(Symbol, Color, (int)(Weight ?? FontWeight.Normal), (int)(Style ?? FontStyle.Normal),
                TextDecoration);
        }

        public static bool operator ==(PixelForeground left, PixelForeground right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PixelForeground left, PixelForeground right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        ///     Lets what is below show through: no symbol, or a color with no alpha. Blended over another
        ///     foreground, it leaves that one.
        /// </summary>
        public bool IsTransparent => Color.A == 0x0 || Symbol.IsEmpty;

        [Obsolete("Use IsTransparent.")]
        public bool IsNothingToDraw()
        {
            return IsTransparent;
        }
    }
}