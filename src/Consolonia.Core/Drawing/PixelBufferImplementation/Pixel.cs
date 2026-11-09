using System;
using System.Collections.Generic;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel()
        {
            Foreground = PixelForeground.Default;
            Background = PixelBackground.Transparent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(CaretStyle caretStyle)
        {
            Foreground = PixelForeground.Default;
            Background = PixelBackground.Transparent;
            CaretStyle = caretStyle;
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
            CaretStyle caretStyle = CaretStyle.None)
        {
            Foreground = foreground;
            Background = PixelBackground.Transparent;
            CaretStyle = caretStyle;
        }

        /// <summary>
        ///     Make a pixel with foreground and background
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Pixel(PixelForeground foreground,
            PixelBackground background,
            CaretStyle caretStyle = CaretStyle.None)
        {
            Foreground = foreground;
            Background = background;
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

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly PixelForeground Foreground;
        public readonly PixelBackground Background;
        public readonly CaretStyle CaretStyle;
#pragma warning restore CA1051 // Do not declare visible instance fields

        [JsonIgnore] public ushort Width => Foreground.Symbol.Width;

        /// <summary>
        ///     The visible background color of the cell, as a single color. For a plain cell this is the
        ///     background color. For an image cell this is the background color blended over the image's
        ///     <see cref="PixelImage.DominantColor" />. Use it where one color is needed for the whole cell.
        /// </summary>
        [JsonIgnore]
        public Color EffectiveBackgroundColor => Background.Image == null
            ? Background.Color
            : MergeColors(Background.Image.DominantColor, Background.Color, false);

        public bool Equals(Pixel other)
        {
            return EqualityComparer<PixelForeground>.Default.Equals(Foreground, other.Foreground) &&
                   EqualityComparer<PixelBackground>.Default.Equals(Background, other.Background) &&
                   CaretStyle == other.CaretStyle;
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
            return new Pixel(new PixelForeground(Foreground.Symbol,
                    Background.Color, // background color becomes the new foreground color
                    Foreground.Weight,
                    Foreground.Style,
                    Foreground.TextDecoration),
                new PixelBackground(Foreground.Color, Background.Image),
                CaretStyle);
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

            Color aboveBgColor = pixelAbove.Background.Color;
            // An image cell above replaces the whole cell, like an opaque color does. Its background color
            // is only the (usually transparent) tint over the image, so its alpha is ignored here.
            byte aboveBgA = pixelAbove.Background.Image != null ? (byte)0xFF : aboveBgColor.A;

            bool foregroundAboveIsTransparent;

            switch (aboveBgA)
            {
                // Fast path: fully opaque overlay -> just return the overlay pixel
                case 0xFF:
                    return pixelAbove;
                // Fast path: fully transparent overlay with no foreground and no caret change -> nothing to do
                case 0x0:
                {
                    foregroundAboveIsTransparent = pixelAbove.Foreground.IsTransparent;
                    if (foregroundAboveIsTransparent && pixelAbove.CaretStyle == CaretStyle.None)
                        return this;
                    newForeground = foregroundAboveIsTransparent
                        ? Foreground
                        : Foreground.Blend(pixelAbove.Foreground);
                    newCaretStyle = CaretStyle.Blend(pixelAbove.CaretStyle);
                }
                    break;
                default:
                    newCaretStyle = pixelAbove.CaretStyle;
                    foregroundAboveIsTransparent = pixelAbove.Foreground.IsTransparent;
                    if (foregroundAboveIsTransparent)
                        // merge the PixelForeground color with the pixelAbove background color
                        newForeground = new PixelForeground(Foreground.Symbol,
                            MergeColors(Foreground.Color, aboveBgColor, true),
                            Foreground.Weight,
                            Foreground.Style,
                            Foreground.TextDecoration);
                    else
                        newForeground = pixelAbove.Foreground;

                    break;
            }

            // The overlay is not opaque here (the opaque case returned above), so the image below is kept.
            // The background color is blended. For an image cell it is the tint drawn over the image.
            var newBackground = new PixelBackground(MergeColors(Background.Color, aboveBgColor, false),
                Background.Image);

            return new Pixel(newForeground, newBackground, newCaretStyle);
        }

        /// <summary>
        ///     merge colors with alpha blending
        /// </summary>
        /// <returns>source blended into target</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Color MergeColors(in Color target, in Color source, bool isTargetForeground)
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