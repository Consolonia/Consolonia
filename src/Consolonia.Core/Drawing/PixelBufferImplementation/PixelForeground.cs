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
            PackedWeight = 0;
            PackedStyle = None;
            PackedTextDecoration = None;
        }

        public PixelForeground(Symbol symbol, Color color,
            FontWeight? weight = null, FontStyle? style = null,
            TextDecorationLocation? textDecoration = null)
        {
            Symbol = symbol;
            Color = color;
            PackedWeight = weight ?? 0;
            PackedStyle = style == null ? None : (byte)style.Value;
            PackedTextDecoration = textDecoration == null ? None : (byte)textDecoration.Value;
        }

        /// <summary>The attributes as packed, for <see cref="Pixel" />, which stores them flat.</summary>
        internal PixelForeground(Symbol symbol, Color color, FontWeight packedWeight, byte packedStyle,
            byte packedTextDecoration)
        {
            Symbol = symbol;
            Color = color;
            PackedWeight = packedWeight;
            PackedStyle = packedStyle;
            PackedTextDecoration = packedTextDecoration;
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Symbol Symbol;
        public readonly Color Color;
#pragma warning restore CA1051 // Do not declare visible instance fields

        // The three optional attributes packed into 6 bytes rather than three nullable enums of 8 each:
        // every cell of the pixel buffer carries them, and it keeps a Pixel within a cache line.
        // FontWeight has no zero (Thin is 100), so 0 is "not set"; the other two fit a byte with a sentinel.

        public FontWeight? Weight => PackedWeight == 0 ? null : PackedWeight;

        public FontStyle? Style => PackedStyle == None ? null : (FontStyle)PackedStyle;

        public TextDecorationLocation? TextDecoration =>
            PackedTextDecoration == None ? null : (TextDecorationLocation)PackedTextDecoration;

        internal FontWeight PackedWeight { get; }

        internal byte PackedStyle { get; }

        internal byte PackedTextDecoration { get; }

        public bool Equals(PixelForeground other)
        {
            return Symbol.Equals(other.Symbol) &&
                   Color.Equals(other.Color) &&
                   PackedWeight == other.PackedWeight &&
                   PackedStyle == other.PackedStyle &&
                   PackedTextDecoration == other.PackedTextDecoration;
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
            return HashCode.Combine(Symbol, Color, (int)PackedWeight, PackedStyle, PackedTextDecoration);
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