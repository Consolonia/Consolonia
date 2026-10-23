using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using Consolonia.Controls;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable NotResolvedInText
namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    /// <summary>
    ///     One cell of the pixel buffer: a <see cref="Foreground" /> (symbol, color and text attributes), a
    ///     <see cref="Background" /> (color, and the cell of a picture when the cell shows one) and a caret.
    /// </summary>
    /// <remarks>
    ///     Stored flat rather than as a foreground struct and a background struct. Each nested struct
    ///     pads to its own alignment, and a <see cref="Symbol" /> inside a <see cref="PixelForeground" />
    ///     inside a Pixel came to 56 bytes for 35 bytes of fields; flat, the runtime lays the two
    ///     references, three ints, a char and five bytes out in 40. Every cell of the screen is one of
    ///     these, copied on every blend and compared on every frame, so the buffer and the terminal
    ///     cache are a third smaller. The <see cref="Foreground" /> and <see cref="Background" /> are
    ///     built on read, which the JIT does in registers.
    /// </remarks>
    [SuppressMessage("ReSharper", "NotResolvedInText",
        Justification = "Properties are accessible but are not seen by resharper", Scope = "type")]
    [SuppressMessage("ReSharper", "NotResolvedInText", MessageId = "Color")]
    [SuppressMessage("ReSharper", "NotResolvedInText", MessageId = "Symbol")]
    [DebuggerDisplay(
        "'{Foreground.Symbol}', Foreground: {Foreground.Color}, Background: {Background.Color}, CaretStyle: {CaretStyle}")]
    [JsonConverter(typeof(PixelConverter))]
    public readonly struct Pixel : IEquatable<Pixel>
    {
        private static readonly Lazy<IConsoleColorMode> ConsoleColorMode =
            new(() => AvaloniaLocator.Current.GetRequiredService<IConsoleColorMode>());

        // the fields of the foreground's symbol, the foreground, and the background, flat (see the remarks)
        private readonly string _symbolReference;
        private readonly CellImage _image;
        private readonly Color _foregroundColor;
        private readonly Color _backgroundColor;
        private readonly FontWeight _weight;
        private readonly char _character;
        private readonly byte _pattern;
        private readonly byte _width;
        private readonly byte _style;
        private readonly byte _textDecoration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel() : this(PixelForeground.Default, PixelBackground.Transparent)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(CaretStyle caretStyle) : this(PixelForeground.Default, PixelBackground.Transparent, caretStyle)
        {
        }

        /// <summary>
        ///     Make a pixel foreground with transparent background
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="foregroundColor"></param>
        /// <param name="style"></param>
        /// <param name="weight"></param>
        /// <param name="textDecorations"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(Symbol symbol,
            Color foregroundColor,
            FontStyle style = FontStyle.Normal,
            FontWeight weight = FontWeight.Normal,
            TextDecorationLocation? textDecorations = null) : this(
            new PixelForeground(symbol, foregroundColor, weight, style, textDecorations),
            PixelBackground.Transparent)
        {
        }

        /// <summary>
        ///     Make a pixel with only background color, but no foreground
        /// </summary>
        /// <param name="background"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(PixelBackground background) :
            this(PixelForeground.Default, background)
        {
        }

        /// <summary>
        ///     Make a pixel with foreground and background
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(PixelForeground foreground,
            CaretStyle caretStyle = CaretStyle.None) : this(foreground, PixelBackground.Transparent, caretStyle)
        {
        }

        /// <summary>
        ///     Make a pixel with foreground and background
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(PixelForeground foreground,
            PixelBackground background,
            CaretStyle caretStyle = CaretStyle.None)
        {
            Symbol symbol = foreground.Symbol;
            _character = symbol.Character;
            _symbolReference = symbol.Reference;
            _pattern = symbol.Pattern;
            _width = symbol.Width;
            _foregroundColor = foreground.Color;
            _weight = foreground.PackedWeight;
            _style = foreground.PackedStyle;
            _textDecoration = foreground.PackedTextDecoration;
            _backgroundColor = background.Color;
            _image = background.Image;
            CaretStyle = caretStyle;
        }

        // Pixel empty is a non-pixel. It has no symbol, no color, no weight, no style, no text decoration, and no background.
        // it is used only when a multichar sequence overlaps a pixel making it a non-entity.
        public static Pixel Empty =>
            new(new PixelForeground(Symbol.Empty, Colors.Transparent), PixelBackground.Transparent);

        // pixel space is a pixel with a space symbol, but could have color blended into. it is used to advance the cursor
        // and set the background color
        public static Pixel Space => new(new PixelForeground(Symbol.Space, Colors.Transparent),
            PixelBackground.Transparent);

        public PixelForeground Foreground => new(new Symbol(_character, _symbolReference, _pattern, _width),
            _foregroundColor, _weight, _style, _textDecoration);

        public PixelBackground Background => new(_backgroundColor, _image);

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly CaretStyle CaretStyle;
#pragma warning restore CA1051 // Do not declare visible instance fields

        [JsonIgnore] public ushort Width => _width;

        public bool Equals(Pixel other)
        {
            return _character == other._character &&
                   _width == other._width &&
                   _pattern == other._pattern &&
                   _foregroundColor == other._foregroundColor &&
                   _backgroundColor == other._backgroundColor &&
                   ReferenceEquals(_image, other._image) &&
                   _weight == other._weight &&
                   _style == other._style &&
                   _textDecoration == other._textDecoration &&
                   CaretStyle == other.CaretStyle &&
                   string.Equals(_symbolReference, other._symbolReference, StringComparison.Ordinal);
        }

        public Pixel Shade()
        {
            return new Pixel(Foreground.Shade(), Background.Shade(), CaretStyle);
        }

        public Pixel Brighten()
        {
            return new Pixel(Foreground.Brighten(), Background.Brighten(), CaretStyle);
        }

        public Pixel Invert()
        {
            PixelForeground foreground = Foreground;
            return new Pixel(new PixelForeground(foreground.Symbol,
                    _backgroundColor, // background color becomes the new foreground color
                    foreground.Weight,
                    foreground.Style,
                    foreground.TextDecoration),
                new PixelBackground(_foregroundColor, _image),
                CaretStyle);
        }

        /// <summary>A cell showing a picture (see <see cref="CellImage" />).</summary>
        private bool IsImage()
        {
            return _image != null;
        }

        /// <summary>
        ///     Blend the pixelAbove with this pixel.
        /// </summary>
        /// <param name="pixelAbove"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel Blend(Pixel pixelAbove)
        {
            PixelForeground newForeground;
            CaretStyle newCaretStyle;

            // An image cell takes the cell whole: its colors are in the picture, and its own colors are
            // transparent, which would otherwise read as nothing drawn on top
            if (pixelAbove.IsImage())
                return pixelAbove;

            Color aboveBgColor = pixelAbove._backgroundColor;
            byte aboveBgA = aboveBgColor.A;

            bool isNoForegroundOnTop;

            switch (aboveBgA)
            {
                // Fast path: fully opaque overlay -> just return the overlay pixel
                case 0xFF:
                    return pixelAbove;
                // Fast path: fully transparent overlay with no foreground and no caret change -> nothing to do
                case 0x0:
                {
                    isNoForegroundOnTop = pixelAbove.Foreground.IsNothingToDraw();
                    if (isNoForegroundOnTop && pixelAbove.CaretStyle == CaretStyle.None)
                        return this;
                    if (isNoForegroundOnTop)
                        newForeground = Foreground;
                    else
                        newForeground = Foreground.Blend(pixelAbove.Foreground);
                    newCaretStyle = CaretStyle.Blend(pixelAbove.CaretStyle);
                }
                    break;
                default:
                    newCaretStyle = pixelAbove.CaretStyle;
                    isNoForegroundOnTop = pixelAbove.Foreground.IsNothingToDraw();
                    if (isNoForegroundOnTop)
                    {
                        // merge the PixelForeground color with the pixelAbove background color
                        PixelForeground foreground = Foreground;
                        newForeground = new PixelForeground(foreground.Symbol,
                            MergeColors(_foregroundColor, aboveBgColor, true),
                            foreground.Weight,
                            foreground.Style,
                            foreground.TextDecoration);
                    }
                    else
                    {
                        newForeground = pixelAbove.Foreground;
                    }

                    break;
            }

            // Background is always blended, image cell or not. The image survives a non-opaque overlay;
            // only an opaque one evicts it, via the fast path above. An image cell's color starts out
            // transparent, so what accumulates here is exactly the paint to lay over the picture. A glyph
            // lands in the foreground and the picture stays below it; what that looks like on a terminal
            // whose protocol cannot draw text over an image is the console output's decision.
            var newBackground = new PixelBackground(MergeColors(_backgroundColor, aboveBgColor, false), _image);

            return new Pixel(newForeground, newBackground, newCaretStyle);
        }

        /// <summary>
        ///     merge colors with alpha blending
        /// </summary>
        /// <returns>source blended into target</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color MergeColors(in Color target, in Color source, bool isTargetForeground)
        {
            // Fast paths to avoid calling into the ConsoleColorMode when not needed
            byte a = source.A;
            if (a == 0x00) return target; // fully transparent source
            if (a == 0xFF) return source; // fully opaque source

            return ConsoleColorMode.Value.Blend(target, source, isTargetForeground);
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            return obj is Pixel pixel && Equals(pixel);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Foreground, Background, CaretStyle);
        }

        public static bool operator ==(Pixel left, Pixel right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Pixel left, Pixel right)
        {
            return !left.Equals(right);
        }
    }
}