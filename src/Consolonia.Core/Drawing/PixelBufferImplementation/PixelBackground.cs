using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Consolonia.Core.Drawing.PixelBufferImplementation
{
    [DebuggerDisplay("[{Color}]")]
    [JsonConverter(typeof(PixelBackgroundConverter))]
    public readonly struct PixelBackground(Color color) : IEquatable<PixelBackground>
    {
        public static readonly PixelBackground Transparent = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground() : this(Colors.Transparent)
        {
        }

        /// <summary>
        ///     A background showing a cell of a picture. <paramref name="color" /> is the paint laid over
        ///     the picture: transparent for none, and translucent overlays blended onto the cell accumulate
        ///     into it exactly as they do on a plain cell. The console output applies it to the image when
        ///     the cell is written.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground(Color color, CellImage image) : this(color)
        {
            Image = image;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Shade()
        {
            // image cells never get here: Pixel.Shade paints them instead
            return new PixelBackground(Color.Shade(), Image);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Brighten()
        {
            return new PixelBackground(Color.Brighten(), Image);
        }

        /// <summary>
        ///     The color this background looks: <see cref="Color" /> for a plain cell, and for an image cell
        ///     the paint composited over the picture's <see cref="CellImage.DominantColor" />. For the places
        ///     that need one color for the cell.
        /// </summary>
        public Color EffectiveColor => Image == null ? Color : ColorOver(Color, Image.DominantColor);

        /// <summary>Composites a (possibly translucent) <paramref name="paint" /> over an opaque color.</summary>
        internal static Color ColorOver(Color paint, Color opaque)
        {
            int alpha = paint.A;
            if (alpha == 0xFF)
                return paint;
            if (alpha == 0)
                return opaque;

            int inverse = 255 - alpha;
            return Color.FromRgb(
                (byte)((paint.R * alpha + opaque.R * inverse) / 255),
                (byte)((paint.G * alpha + opaque.G * inverse) / 255),
                (byte)((paint.B * alpha + opaque.B * inverse) / 255));
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Color Color = color;

        /// <summary>The cell of a picture this background shows, or null for a plain color.</summary>
        public readonly CellImage Image;
#pragma warning restore CA1051 // Do not declare visible instance fields

        public bool Equals(PixelBackground other)
        {
            return Color.Equals(other.Color) && ReferenceEquals(Image, other.Image);
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            return obj is PixelBackground other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Color, RuntimeHelpers.GetHashCode(Image));
        }

        public static bool operator ==(PixelBackground left, PixelBackground right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PixelBackground left, PixelBackground right)
        {
            return !left.Equals(right);
        }
    }
}