using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Consolonia.Controls;
using NeoSmart.Unicode;
using Wcwidth;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    [DebuggerDisplay("'{GetText()}' Box[{BoxPattern.GetMaskText(Pattern)}]")]
    [JsonConverter(typeof(SymbolConverter))]
    public readonly struct Symbol : IEquatable<Symbol>
    {
        private const char TextVariation = '\ufe0e';
        private const char EmojiVariation = '\ufe0f';

        private static readonly Dictionary<char, string> GlyphCharCache = new();
        private static readonly Dictionary<string, string> GlyphComplexCache = new();

        public static readonly Symbol Empty = new();
        public static readonly Symbol Space = new(' ');

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Symbol()
        {
            // we use String.Empty to represent an empty symbol
            Character = char.MinValue;
            Complex = null;
            Width = 0;
            Pattern = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Symbol(char ch, byte? width = null)
        {
            Character = ch;
            Complex = null;
            Width = width ?? (byte)UnicodeCalculator.GetWidth(ch);
            Pattern = 0;
            // Use EmojiVariation for actual emoji and for wide symbol glyphs such as ☰.
            // East Asian letters and full-width punctuation are natively 2-wide and must not have U+FE0F appended.
            if (ShouldUseEmojiVariation(ch, Width))
            {
                // we want to use EmojiVariation to signal we think it's wide.
                Character = char.MinValue;
                Width = 2;
                lock (GlyphCharCache)
                {
                    if (GlyphCharCache.TryGetValue(ch, out string wideChar))
                        Complex = wideChar;
                    else
                        Complex = GlyphCharCache[ch] = $"{ch}{EmojiVariation}";
                }
            }
        }

        private static bool ShouldUseEmojiVariation(char ch, byte width)
        {
            if (Emoji.IsEmoji(new string(ch, 1)))
                return true;

            return width == 2 && char.GetUnicodeCategory(ch) == UnicodeCategory.OtherSymbol;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Symbol(byte boxPattern)
        {
            Character = BoxPattern.GetBoxChar(boxPattern);
            Complex = null;
            Width = 1;
            Pattern = boxPattern;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Symbol(string glyph, byte? width = null)
        {
            Pattern = 0;
            Complex = null;
            Character = char.MinValue;
            ArgumentNullException.ThrowIfNull(glyph);
            if (glyph.Length == 0)
            {
                // empty string means nothing to draw, this is used in blending situations
                // we want to use char.MinValue and null for Complex to indicate this is empty.
                Width = 0;
            }
            else
            {
                Width = width ?? (byte)glyph.MeasureText();

                if (glyph.Length == 1)
                {
                    // we can use the single char constructor for optimization
                    this = new Symbol(glyph[0], width);
                }
                else if (glyph.Any(ch => ch == TextVariation || ch == EmojiVariation))
                {
                    // it already has the variation selector so we just use it as is
                    Complex = glyph;
                }
                else
                {
                    // we want to store as complex glyph with variation selector
                    Character = char.MinValue;
                    lock (GlyphComplexCache)
                    {
                        if (GlyphComplexCache.TryGetValue(glyph, out string complex))
                        {
                            Complex = complex;
                        }
                        else
                        {
                            // use text variation for narrow glyphs, emoji variation for wide glyphs
                            char variation = width == 1 ? TextVariation : EmojiVariation;
                            Complex = GlyphComplexCache[glyph] = $"{glyph}{variation}";
                        }
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Symbol(Rune rune)
            : this(rune.ToString())
        {
        }


        public bool Equals(Symbol other)
        {
            return Character == other.Character &&
                   string.Equals(Complex, other.Complex, StringComparison.Ordinal) &&
                   Width == other.Width &&
                   Pattern == other.Pattern;
        }


#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>
        ///     The character for this symbol. If Complex is set this is Char.MinValue.
        /// </summary>
        public readonly char Character;

        /// <summary>
        ///     If cell has complex text (more than one char) this contains the full unicode sequence to draw this symbol.
        /// </summary>
        public readonly string Complex;

        // box pattern for box merging.
        public readonly byte Pattern;

        [JsonIgnore] public readonly byte Width;
#pragma warning restore CA1051 // Do not declare visible instance fields


        /// <summary>
        ///     Get the symbol as text
        /// </summary>
        /// <returns>symbol as string</returns>
        /// NOTE: This is only for debug purposes, do not use in rendering code as it allocates a string for the character.
#pragma warning disable CA1024 // Use properties where appropriate
        public string GetText()
#pragma warning restore CA1024 // Use properties where appropriate
        {
            if (Width == 0)
                return string.Empty;
            return Complex != null && Complex.Length > 1 ? Complex : new string(Character, 1);
        }

        public bool NothingToDraw()
        {
            return Character == char.MinValue &&
                   string.IsNullOrEmpty(Complex);
        }

        public Symbol Blend(ref Symbol symbolAbove)
        {
            if (symbolAbove.NothingToDraw())
                return this;

            // if both are box symbols we need to merge them.
            if (IsBoxSymbol() && symbolAbove.IsBoxSymbol())
                return new Symbol(BoxPattern.Merge(Pattern, symbolAbove.Pattern));

            // top symbol always overwrites bottom symbol. We know it's not empty because we checked that above.
            return symbolAbove;
        }

        public bool IsBoxSymbol()
        {
            return Pattern > 0;
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            return obj is Symbol other &&
                   Character == other.Character &&
                   string.Equals(Complex, other.Complex, StringComparison.Ordinal) &&
                   Width == other.Width &&
                   Pattern == other.Pattern;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Character, Complex, Width, Pattern);
        }

        public static bool operator ==(Symbol left, Symbol right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Symbol left, Symbol right)
        {
            return !left.Equals(right);
        }
    }
}