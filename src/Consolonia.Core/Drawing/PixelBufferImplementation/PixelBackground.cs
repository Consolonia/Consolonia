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
        ///     Creates a background that shows a pixel image. <paramref name="color" /> is a tint drawn over the
        ///     image. Use transparent for no tint. Translucent overlays blend into this color, the same way they
        ///     blend into the color of a plain cell.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground(Color color, PixelImage image) : this(color)
        {
            Image = image;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Shade()
        {
            return new PixelBackground(Color.Shade(), Image);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PixelBackground Brighten()
        {
            return new PixelBackground(Color.Brighten(), Image);
        }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public readonly Color Color = color;

        /// <summary>The pixel image shown in this cell, or null for a plain color background.</summary>
        public readonly PixelImage Image;
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